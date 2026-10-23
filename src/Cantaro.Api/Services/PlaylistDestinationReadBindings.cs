using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

// Read membership is scoped to the exact account/link/entry. Apply it before a
// provider can mistake our own export for independent catalog/artist evidence.
internal static class PlaylistDestinationReadBindings
{
    public static async Task<IReadOnlyList<PlaylistRemoteTrack>> BindReadAsync(ApplicationDbContext db,
        PlatformAccountContext account, string service, string playlistId,
        IReadOnlyList<PlaylistRemoteTrack> incoming, CancellationToken ct)
    {
        var link = await db.ServicePlaylistMappings.AsNoTracking().Include(item => item.ConnectedServiceAccount)
            .SingleOrDefaultAsync(item => item.UserId == account.UserId && item.Playlist!.UserId == account.UserId
                && item.Service == service && item.ServicePlaylistId == playlistId && item.State == "active"
                && item.ConnectedServiceAccountId == account.ConnectedServiceAccountId
                && item.ConnectedServiceAccount!.ExternalAccountId == item.ExternalAccountId
                && (account.ExpectedExternalAccountId == null || item.ExternalAccountId == account.ExpectedExternalAccountId), ct);
        if (link is null) return incoming;
        var bound = await BindAsync(db, link, account,
            incoming.Select(item => PlaylistSyncItem.From(item, service)).ToList(), ct, scopedOnly: true);
        return incoming.Select((item, index) => bound[index].DestinationBinding is { } binding
            ? item with { TrackId = bound[index].TrackId, ObservationId = bound[index].ObservationId,
                DestinationBinding = binding }
            : item).ToArray();
    }

    public static async Task<List<PlaylistSyncItem>> BindAsync(ApplicationDbContext db, ServicePlaylistMapping link,
        PlatformAccountContext account, List<PlaylistSyncItem> incoming, CancellationToken ct,
        HashSet<Guid>? confirmedEntries = null, IReadOnlyList<PlaylistSyncItem>? acknowledged = null, bool scopedOnly = false)
    {
        if (!PlaylistDestinationIdentityEvidence.IsCurrentAccount(link, account, link.Service)) return incoming;
        var entries = await db.PlaylistEntries.AsNoTracking().Where(item => item.PlaylistId == link.PlaylistId)
            .Include(item => item.Track).ThenInclude(track => track!.SourceIds)
            .Include(item => item.TrackObservation).OrderBy(item => item.Position).ToListAsync(ct);
        var byId = entries.ToDictionary(entry => entry.Id);
        var saved = PlaylistMatchingProgress.Read(link.MatchingProgressJson);
        var authorized = await PlaylistDestinationIdentityEvidence.FindManyAsync(db, account, link.Service, entries, link, ct);
        var choices = new List<PlaylistSyncItem>();

        // Baselines acknowledge membership, not permission for a new export. Keep
        // them usable after metadata changes and local entry removal, so a remote
        // occurrence does not become a spurious addition in the three-way merge.
        // Pending writes acknowledge only occurrences actually present in this read.
        // They do not authorize exporting an absent destination after a proof expires.
        var membership = acknowledged ?? (PlaylistSyncBaseline.Parse(link.BaselineJson)?.Items ?? [])
            .Concat(PlaylistSyncBaseline.Parse(link.PendingWriteJson)?.Items ?? []).ToList();
        foreach (var item in membership)
        {
            var binding = item.DestinationBinding;
            if (binding is null && item.TrackId is not null && item.ExternalId is not null)
            {
                // Upgrade older acknowledged baselines only when a scoped proof
                // identifies the occurrence and its canonical/source membership.
                // The metadata fingerprint still gates new outbound matching.
                var proofs = saved.Where(pair => pair.Value.ProofVersion == PlaylistMatchingProgress.CurrentProofVersion
                    && pair.Value.ExternalId == item.ExternalId
                    && pair.Value.Scope == PlaylistMatchingProgress.ScopeFor(link, new PlaylistEntry { Id = pair.Key })
                    && (!byId.TryGetValue(pair.Key, out var member)
                        || member.TrackId == item.TrackId && member.TrackObservationId == item.ObservationId)).ToArray();
                if (proofs.Length == 1)
                {
                    var entryId = proofs[0].Key;
                    binding = new(link.UserId, link.PlaylistId, link.Id, link.ConnectedServiceAccountId,
                        link.ExternalAccountId, link.Service, link.ServicePlaylistId, entryId,
                        item.ObservationId is null ? null : item.TrackId);
                }
            }
            if (binding is null || item.TrackId is null || item.ExternalId is null
                || binding.UserId != account.UserId || binding.PlaylistId != link.PlaylistId
                || binding.MappingId != link.Id || binding.Service != link.Service
                || binding.ConnectedServiceAccountId != account.ConnectedServiceAccountId
                || binding.ExternalAccountId != link.ExternalAccountId
                || binding.ServicePlaylistId != link.ServicePlaylistId) continue;
            if (byId.TryGetValue(binding.EntryId, out var entry)
                && (entry.TrackId != item.TrackId || entry.TrackObservationId != item.ObservationId)) continue;
            choices.Add(item with { DestinationBinding = binding });
        }
        // Fresh choices can bind an observed occurrence before its first export.
        foreach (var entry in entries.Where(entry => acknowledged is null && entry.TrackId is not null
            && saved.TryGetValue(entry.Id, out var proof) && PlaylistMatchingProgress.CanReuse(proof, entry, link)
            && proof.ExternalId is not null && authorized.GetValueOrDefault(entry.Id) == proof.ExternalId))
        {
            var id = saved[entry.Id].ExternalId!;
            if (saved[entry.Id].ManualConfirmation) confirmedEntries?.Add(entry.Id);
            if (!choices.Any(item => item.DestinationBinding!.EntryId == entry.Id && item.ExternalId == id))
                choices.Add(PlaylistSyncItem.From(entry) with { ExternalId = id, Service = link.Service,
                    DestinationBinding = PlaylistDestinationBinding.From(link, entry) });
        }

        var observationIds = choices.Select(item => item.ObservationId).OfType<Guid>().Distinct().ToArray();
        var observations = await db.TrackObservations.AsNoTracking().Where(item => observationIds.Contains(item.Id))
            .Select(item => new { item.Id, item.TrackId, item.SourceType, item.ExternalId }).ToDictionaryAsync(item => item.Id, ct);
        var trackIds = choices.Select(item => item.TrackId!.Value).Distinct().ToArray();
        var tracks = await db.Tracks.AsNoTracking().Where(item => trackIds.Contains(item.Id)).Select(item => item.Id).ToListAsync(ct);
        var assignments = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);
        foreach (var keys in choices.Select(item => item.ExternalId!).Distinct(StringComparer.Ordinal).Chunk(256))
        {
            var sources = await db.TrackSourceIds.AsNoTracking()
                .Where(item => item.SourceType == link.Service && keys.Contains(item.ExternalId))
                .Select(item => new { item.ExternalId, item.TrackId }).ToListAsync(ct);
            foreach (var source in sources)
            {
                if (!assignments.TryGetValue(source.ExternalId, out var ids)) assignments[source.ExternalId] = ids = [];
                ids.Add(source.TrackId);
            }
        }
        choices = choices.Where(item => tracks.Contains(item.TrackId!.Value)
            && (item.ObservationId is null || observations.TryGetValue(item.ObservationId.Value, out var observation)
                && observation.TrackId == item.DestinationBinding!.ObservationTrackId)
            && (!assignments.TryGetValue(item.ExternalId!, out var ids) || ids.All(id => id == item.TrackId))).ToList();
        var grouped = choices.DistinctBy(item => (item.ExternalId, item.DestinationBinding!.EntryId))
            .GroupBy(item => item.ExternalId!).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        return incoming.Select(item =>
        {
            if (item.ExternalId is not { } id || !grouped.TryGetValue(id, out var candidates))
            {
                if (item.DestinationBinding is not null) throw ChangedBinding();
                return item;
            }
            var index = occurrences.GetValueOrDefault(id);
            occurrences[id] = index + 1;
            // Consume each acknowledged choice in order, then consistently bind
            // additional repeats before duplicate normalization. Never drop slots.
            var choice = candidates[index % candidates.Length];
            if (item.TrackId is not null && item.TrackId != choice.TrackId)
            {
                if (item.DestinationBinding is not null) throw ChangedBinding();
                return item;
            }
            // A real source observation or established recording mapping remains
            // independent import evidence. Keep those reads on normal ingestion.
            if (scopedOnly && (assignments.ContainsKey(id)
                || choice.ObservationId is { } observationId && observations.TryGetValue(observationId, out var source)
                    && source.SourceType == link.Service && source.ExternalId == id && source.TrackId == choice.TrackId))
                return item;
            if (byId.ContainsKey(choice.DestinationBinding!.EntryId))
                confirmedEntries?.Add(choice.DestinationBinding.EntryId);
            return choice;
        }).ToList();
    }

    private static PlatformApiException ChangedBinding()
        => new("remote_changed", "The playlist binding changed during its read. Cantaro will refresh before syncing.", 409);

}
