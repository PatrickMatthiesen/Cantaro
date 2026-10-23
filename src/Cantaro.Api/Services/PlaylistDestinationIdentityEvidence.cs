using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

// Shared eligibility for review and resolution. Global canonical mappings alone
// never authorize a destination ID for the requester.
internal static class PlaylistDestinationIdentityEvidence
{
    private const int BatchSize = 256;

    public static async Task<string?> FindAsync(ApplicationDbContext db, PlatformAccountContext account,
        string service, PlaylistEntry entry, CancellationToken ct, ServicePlaylistMapping? link = null)
    {
        var links = link is null
            ? await db.ServicePlaylistMappings.AsNoTracking().Include(item => item.ConnectedServiceAccount)
                .Where(item => item.PlaylistId == entry.PlaylistId && item.UserId == account.UserId
                    && item.Playlist!.UserId == account.UserId && item.Service == service
                    && item.ConnectedServiceAccountId == account.ConnectedServiceAccountId && item.State == "active")
                .ToListAsync(ct)
            : [link];
        var identities = await FindCoreAsync(db, account, service, [entry], links, ct);
        return identities.GetValueOrDefault(entry.Id);
    }

    // All evidence lives only for this operation. Proofs remain scoped to each
    // entry even when several entries share the same canonical track.
    public static Task<Dictionary<Guid, string>> FindManyAsync(ApplicationDbContext db,
        PlatformAccountContext account, string service, IReadOnlyList<PlaylistEntry> entries,
        ServicePlaylistMapping link, CancellationToken ct)
        => FindCoreAsync(db, account, service, entries, [link], ct);

    private static async Task<Dictionary<Guid, string>> FindCoreAsync(ApplicationDbContext db,
        PlatformAccountContext account, string service, IReadOnlyList<PlaylistEntry> entries,
        IReadOnlyList<ServicePlaylistMapping> links, CancellationToken ct)
    {
        var savedByEntry = new Dictionary<Guid, List<PlaylistMatchingResult>>();
        foreach (var link in links)
        {
            if (!IsCurrentAccount(link, account, service)) continue;
            var progress = PlaylistMatchingProgress.Read(link.MatchingProgressJson);
            foreach (var entry in entries)
            {
                if (link.PlaylistId != entry.PlaylistId || !progress.TryGetValue(entry.Id, out var saved)
                    || string.IsNullOrWhiteSpace(saved.ExternalId)
                    || !PlaylistMatchingProgress.CanReuse(saved, entry, link)) continue;
                if (!savedByEntry.TryGetValue(entry.Id, out var savedResults))
                    savedByEntry[entry.Id] = savedResults = [];
                savedResults.Add(saved);
            }
        }

        // Restrict reassignment checks to keys from eligible proofs, never the
        // whole catalog. Keep all assignments so any conflicting track rejects it.
        var assignments = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);
        foreach (var keys in savedByEntry.Values.SelectMany(items => items)
            .Select(saved => saved.ExternalId!).Distinct(StringComparer.Ordinal).Chunk(BatchSize))
        {
            var sources = await db.TrackSourceIds.AsNoTracking()
                .Where(source => source.SourceType == service && keys.Contains(source.ExternalId))
                .Select(source => new { source.ExternalId, source.TrackId }).ToListAsync(ct);
            foreach (var source in sources)
            {
                if (!assignments.TryGetValue(source.ExternalId, out var tracks))
                    assignments[source.ExternalId] = tracks = [];
                tracks.Add(source.TrackId);
            }
        }

        string? FindSaved(PlaylistEntry entry, bool manualConfirmation)
        {
            if (!savedByEntry.TryGetValue(entry.Id, out var savedResults)) return null;
            var trackId = entry.TrackId ?? entry.TrackObservation?.TrackId;
            return savedResults.FirstOrDefault(saved => saved.ManualConfirmation == manualConfirmation
                && (!assignments.TryGetValue(saved.ExternalId!, out var tracks)
                    || tracks.All(id => id == trackId)))?.ExternalId;
        }

        var identities = new Dictionary<Guid, string>();
        var remaining = new List<PlaylistEntry>();
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            var confirmed = FindSaved(entry, manualConfirmation: true);
            var observation = entry.TrackObservation;
            var trackId = entry.TrackId ?? observation?.TrackId;
            if (confirmed is not null) identities[entry.Id] = confirmed;
            else if (observation?.SourceType == service && (trackId is null || observation.TrackId == trackId))
                identities[entry.Id] = observation.ExternalId;
            else remaining.Add(entry);
        }

        var ownedByTrack = new Dictionary<Guid, string>();
        foreach (var trackIds in remaining.Select(entry => entry.TrackId ?? entry.TrackObservation?.TrackId)
            .OfType<Guid>().Distinct().Chunk(BatchSize))
        {
            var owned = await db.PlaylistEntries.AsNoTracking()
                .Where(item => item.Playlist!.UserId == account.UserId && item.TrackId != null
                    && trackIds.Contains(item.TrackId.Value) && item.TrackObservation != null
                    && item.TrackObservation.SourceType == service && item.TrackObservation.TrackId == item.TrackId)
                .Select(item => new { TrackId = item.TrackId!.Value, item.TrackObservation!.ExternalId })
                .Distinct().OrderBy(item => item.ExternalId).ToListAsync(ct);
            // Preserve the database's provider-key ordering used by the single lookup.
            foreach (var observation in owned) ownedByTrack.TryAdd(observation.TrackId, observation.ExternalId);
        }
        foreach (var entry in remaining)
        {
            ct.ThrowIfCancellationRequested();
            var trackId = entry.TrackId ?? entry.TrackObservation?.TrackId;
            var externalId = trackId is { } id && ownedByTrack.TryGetValue(id, out var owned)
                ? owned : FindSaved(entry, manualConfirmation: false);
            if (externalId is not null) identities[entry.Id] = externalId;
        }
        return identities;
    }

    internal static bool IsCurrentAccount(ServicePlaylistMapping candidate, PlatformAccountContext account, string service)
    {
        var connected = candidate.ConnectedServiceAccount;
        return candidate.UserId == account.UserId && candidate.Service == service && candidate.State == "active"
            && candidate.ConnectedServiceAccountId == account.ConnectedServiceAccountId
            && connected is not null && connected.UserId == account.UserId && connected.Service == service
            && connected.ConnectionState == "connected" && connected.ExternalAccountId == candidate.ExternalAccountId
            && (account.ExpectedExternalAccountId is not { } expected || expected == candidate.ExternalAccountId);
    }
}
