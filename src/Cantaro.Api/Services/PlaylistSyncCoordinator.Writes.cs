using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Cantaro.Api.Services;

public sealed partial class PlaylistSyncCoordinator
{
    private async Task CreateMissingLinksAsync(Playlist playlist, List<LinkRead> reads,
        CancellationToken ct, string? service = null, bool skipDuplicateNameCheck = false)
    {
        var accounts = await db.ConnectedServiceAccounts.AsNoTracking().Where(account => account.UserId == playlist.UserId
            && account.ConnectionState == "connected" && (account.Service == "youtube" || account.Service == "spotify")
            && (service == null || account.Service == service)).ToListAsync(ct);
        foreach (var account in accounts)
        {
            var existing = await db.ServicePlaylistMappings.Where(link => link.PlaylistId == playlist.Id && link.Service == account.Service
                && (link.ExternalAccountId == account.ExternalAccountId || (link.ExternalAccountId == "" && link.ConnectedServiceAccountId == account.Id))).ToListAsync(ct);
            if (existing.Any(link => link.State != "creation_failed" || link.AttemptCount >= 5 || link.NextAttemptAt > clock.GetUtcNow())) continue;
            if (!skipDuplicateNameCheck)
            {
                try
                {
                    var remote = await Provider(account.Service).ListPlaylistsAsync(
                        new PlatformAccountContext(playlist.UserId, account.Id, account.ExternalAccountId), ct);
                    if (remote.Any(item => PlaylistLinkLifecycleService.NormalizePlaylistName(item.Name)
                        == PlaylistLinkLifecycleService.NormalizePlaylistName(playlist.Name)))
                        continue;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not check existing {Service} playlists before automatic creation for {PlaylistId}",
                        account.Service, playlist.Id);
                    continue;
                }
            }
            var attempt = existing.Select(link => link.AttemptCount).DefaultIfEmpty(0).Max();
            db.ServicePlaylistMappings.RemoveRange(existing);
            var context = new PlatformAccountContext(playlist.UserId, account.Id, account.ExternalAccountId);
            var writer = Writer(account.Service);
            var link = new ServicePlaylistMapping
            {
                Id = Guid.NewGuid(), PlaylistId = playlist.Id, UserId = playlist.UserId,
                Service = account.Service, ConnectedServiceAccountId = account.Id, ExternalAccountId = account.ExternalAccountId,
                ServicePlaylistId = $"pending:{Guid.NewGuid():N}", SyncMode = "bidirectional", State = "creating", LastSyncStatus = "creating",
                AttemptCount = attempt
            };
            try
            {
                db.ServicePlaylistMappings.Add(link);
                await db.SaveChangesAsync(ct); // reserve before the non-idempotent provider POST
                try { await writer.ValidateCreationAsync(context, ct); }
                catch { link.State = "creation_failed"; throw; }
                var pendingId = link.ServicePlaylistId;
                try
                {
                    link.ServicePlaylistId = await writer.CreatePlaylistAsync(context, playlist.Name, ct);
                    link.State = "active";
                    link.BaselineName = playlist.Name;
                    link.BaselineJson = new PlaylistSyncBaseline([]).Serialize();
                    await db.SaveChangesAsync(ct);
                }
                catch (PlatformApiException ex) when (ex.Code == "spotify_quota_exceeded"
                    || ex.Code.EndsWith("playlist_creation_rejected", StringComparison.Ordinal))
                {
                    link.State = "creation_failed";
                    await db.SaveChangesAsync(CancellationToken.None);
                    throw;
                }
                catch
                {
                    link.ServicePlaylistId = pendingId;
                    link.State = "creation_uncertain";
                    link.LastSyncStatus = "creation_uncertain";
                    link.LastError = "The platform may have created this playlist. Check it before attaching the resulting copy.";
                    await db.SaveChangesAsync(CancellationToken.None);
                    throw;
                }
                var snapshot = await Provider(account.Service).ReadAsync(context, link.ServicePlaylistId, ct);
                RequireComplete(snapshot);
                reads.Add(new(link, context, snapshot, [], []));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not create {Service} counterpart for playlist {PlaylistId}", account.Service, playlist.Id);
                if (db.Entry(link).State != EntityState.Detached && link.State != "creation_uncertain")
                    RecordFailure(link, ex);
                await db.SaveChangesAsync(ct);
            }
        }
    }

    private async Task WriteLinkAsync(Playlist playlist, LinkRead read, CancellationToken ct)
    {
        var link = read.Link;
        var provider = Provider(link.Service);
        link.LastSyncStatus = "running";
        link.LastError = null;
        await db.SaveChangesAsync(ct);
        var entries = await db.PlaylistEntries.Where(item => item.PlaylistId == playlist.Id)
            .Include(item => item.Track).ThenInclude(track => track!.SourceIds)
            .Include(item => item.TrackObservation).ThenInclude(observation => observation!.Track)
                .ThenInclude(track => track!.SourceIds)
            .OrderBy(item => item.Position).ToListAsync(ct);
        var resolutions = new Dictionary<Guid, string?>();
        var available = read.Items.ToList();
        var progress = PlaylistMatchingProgress.Read(link.MatchingProgressJson);
        var entryIds = entries.Select(entry => entry.Id).ToHashSet();
        foreach (var obsolete in progress.Keys.Where(id => !entryIds.Contains(id)).ToList()) progress.Remove(obsolete);
        var completed = entries.Where(entry => KnownDestinationId(entry, link.Service) is not null
            || progress.TryGetValue(entry.Id, out var saved) && saved.Fingerprint == PlaylistMatchingProgress.Fingerprint(entry))
            .Select(entry => entry.Id).ToHashSet();
        link.MatchingTotalCount = entries.Count;
        link.MatchingProcessedCount = completed.Count;
        await db.SaveChangesAsync(ct);
        var batchStarted = clock.GetTimestamp();
        var searched = 0;
        foreach (var entry in entries)
        {
            string? id;
            var fingerprint = PlaylistMatchingProgress.Fingerprint(entry);
            try
            {
                var identity = PlaylistSyncItem.From(entry).Identity;
                var occurrence = available.FirstOrDefault(item => item.Identity == identity
                    && item.ObservationId == entry.TrackObservationId)
                    ?? available.FirstOrDefault(item => item.Identity == identity);
                if (occurrence is not null) available.Remove(occurrence);
                if (occurrence is not null) id = occurrence.ExternalId;
                else if (KnownDestinationId(entry, link.Service) is { } knownId) id = knownId;
                else if (progress.TryGetValue(entry.Id, out var saved) && saved.Fingerprint == fingerprint)
                    id = saved.ExternalId;
                else
                {
                    // Return to the scheduler between batches. Do not write a partially
                    // searched list, since that could temporarily remove unresolved songs.
                    if (searched >= 25 || clock.GetElapsedTime(batchStarted) >= TimeSpan.FromMinutes(2))
                    {
                        link.MatchingProgressJson = JsonSerializer.Serialize(progress);
                        link.LastSyncStatus = "pending";
                        link.LastError = null;
                        link.NextAttemptAt = clock.GetUtcNow().AddSeconds(1);
                        await db.SaveChangesAsync(ct);
                        return;
                    }
                    searched++;
                    id = await provider.ResolveAsync(read.Account, entry, ct);
                }
            }
            catch (PlatformApiException ex) when (ex.Code is "no_match" or "ambiguous_match") { id = null; }
            resolutions[entry.Id] = id;
            // Resolution may have attached a canonical track and enriched its metadata.
            // Checkpoint that saved state so our own update does not invalidate the result.
            var resolvedEntry = await db.PlaylistEntries.AsNoTracking()
                .Include(item => item.Track).ThenInclude(track => track!.SourceIds)
                .Include(item => item.TrackObservation).SingleOrDefaultAsync(item => item.Id == entry.Id, ct);
            if (resolvedEntry is not null)
            {
                progress[entry.Id] = new(PlaylistMatchingProgress.Fingerprint(resolvedEntry), id);
                completed.Add(entry.Id);
            }
            else
            {
                progress.Remove(entry.Id);
                completed.Remove(entry.Id);
            }
            link.MatchingProcessedCount = completed.Count;
            link.MatchingProgressJson = JsonSerializer.Serialize(progress);
            await db.SaveChangesAsync(ct);
        }
        // Matching can unify observations and remove duplicate entries. Export the resulting
        // membership rather than the list that existed before destination lookup.
        entries = await db.PlaylistEntries.AsNoTracking().Where(item => item.PlaylistId == playlist.Id)
            .Include(item => item.TrackObservation).OrderBy(item => item.Position).ToListAsync(ct);
        var ids = new List<string>();
        var exported = new List<PlaylistSyncItem>();
        var unresolved = 0;
        foreach (var entry in entries)
        {
            if (!resolutions.TryGetValue(entry.Id, out var id))
                throw new PlatformApiException("remote_changed", "The Cantaro playlist changed during matching. Cantaro will refresh before writing.", 409);
            if (string.IsNullOrWhiteSpace(id)) { unresolved++; continue; }
            ids.Add(id);
            exported.Add(PlaylistSyncItem.From(entry) with { ExternalId = id, Service = link.Service });
        }

        // Search and metadata resolution can take time. Refuse to overwrite an edit made since the read phase.
        var fresh = await provider.ReadAsync(read.Account, link.ServicePlaylistId, ct);
        RequireComplete(fresh);
        if (!fresh.Tracks.Select(item => (item.ExternalId, item.IsAvailable)).SequenceEqual(read.Snapshot.Tracks.Select(item => (item.ExternalId, item.IsAvailable)))
            || fresh.Name != read.Snapshot.Name)
            throw new PlatformApiException("remote_changed", "The platform playlist changed during sync. Cantaro will read it again before writing.", 409);
        var revision = await db.Playlists.AsNoTracking().Where(item => item.Id == playlist.Id).Select(item => item.SyncRevision).SingleAsync(ct);
        if (revision != playlist.SyncRevision)
            throw new PlatformApiException("remote_changed", "The Cantaro playlist changed during sync. The platform was not overwritten.", 409);

        link.PendingWriteJson = new PlaylistSyncBaseline(exported).Serialize();
        await db.SaveChangesAsync(ct);
        var unavailableIds = fresh.Tracks.Where(item => !item.IsAvailable)
            .Select(item => item.ExternalId).ToHashSet(StringComparer.Ordinal);
        var writableIds = ids.Where(id => !unavailableIds.Contains(id)).ToList();
        if (!writableIds.SequenceEqual(fresh.Tracks.Where(item => item.IsAvailable).Select(item => item.ExternalId)))
            await Writer(link.Service).ReconcileAsync(read.Account, link.ServicePlaylistId, ids, ct);
        if (link.DesiredName is { } desiredName && desiredName != fresh.Name)
            await provider.RenameAsync(read.Account, link.ServicePlaylistId, desiredName, ct);
        var verified = await provider.ReadAsync(read.Account, link.ServicePlaylistId, ct);
        RequireComplete(verified);
        if (!writableIds.SequenceEqual(verified.Tracks.Where(item => item.IsAvailable).Select(item => item.ExternalId))
            || !fresh.Tracks.Where(item => !item.IsAvailable).Select(item => item.ExternalId)
                .SequenceEqual(verified.Tracks.Where(item => !item.IsAvailable).Select(item => item.ExternalId)))
            throw new PlatformApiException("playlist_verification_failed", "The platform did not retain the requested tracks. Cantaro will retry from fresh state.", 502);
        if (link.DesiredName is { } expectedName && verified.Name != expectedName)
            throw new PlatformApiException("playlist_verification_failed", "The platform did not retain the approved name. Cantaro will retry.", 502);
        link.DesiredName = null;
        link.PendingWriteJson = null;
        link.BaselineName = verified.Name;
        link.BaselineJson = new PlaylistSyncBaseline(
            await NormalizeIdentitiesAsync(KnownTracks(verified).Select(item => PlaylistSyncItem.From(item, link.Service)), ct),
            verified.Revision).Serialize();
        link.CanonicalRevision = playlist.SyncRevision;
        link.UnresolvedCount = unresolved;
        link.LastSyncedAt = clock.GetUtcNow();
        link.LastSyncStatus = unresolved > 0 ? "partial" : "success";
        link.NextAttemptAt = null; // unresolved identities are looked up again on the next daily run
        link.AttemptCount = 0;
        link.LastError = null;
        link.MatchingProgressJson = null;
        link.MatchingProcessedCount = 0;
        link.MatchingTotalCount = 0;
    }

    private static string? KnownDestinationId(PlaylistEntry entry, string service)
    {
        var observation = entry.TrackObservation;
        var track = entry.Track ?? observation?.Track;
        if (observation?.SourceType == service && (track is null || observation.TrackId == track.Id))
            return observation.ExternalId;
        return track?.SourceIds.Where(source => source.SourceType == service)
            .OrderByDescending(source => source.Confidence).ThenByDescending(source => source.LastVerifiedAt)
            .FirstOrDefault()?.ExternalId;
    }
}
