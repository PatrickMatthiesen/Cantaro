using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Cantaro.Api.Services;

public sealed partial class PlaylistSyncCoordinator
{
    private Task PersistAtomicallyAsync(Func<Task> operation, CancellationToken ct)
        => new PlaylistTransactionStrategy(db).ExecuteAsync(async () =>
        {
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(ct) : null;
            await operation();
            if (transaction is not null) await transaction.CommitAsync(ct);
        });

    // SaveItemsAsync saves in stages to release unique positions. Replaying the delegate
    // would reuse tracked entities from a rolled-back attempt. Suppress command-level
    // retries for this transaction; a later sync retries with a fresh context and read.
    // Provider calls stay outside this scope so database retries cannot replay them.
    private sealed class PlaylistTransactionStrategy(DbContext context)
        : ExecutionStrategy(context, 0, TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => false;
    }

    private async Task WithLeaseAsync(int userId, Guid playlistId, Func<Playlist, CancellationToken, Task> operation, CancellationToken ct)
    {
        var gate = Gates.GetOrAdd(playlistId, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, ct)) throw new PlatformApiException("sync_in_progress", "This playlist is already being synced.", 409);
        var leaseId = Guid.NewGuid();
        var acquired = false;
        try
        {
            var now = clock.GetUtcNow();
            var playlist = await RequirePlaylistAsync(userId, playlistId, ct);
            if (db.Database.IsRelational())
            {
                var count = await db.Playlists.Where(item => item.Id == playlistId && item.UserId == userId
                    && (item.SyncLeaseExpiresAt == null || item.SyncLeaseExpiresAt <= now))
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.SyncLeaseId, leaseId)
                        .SetProperty(item => item.SyncLeaseExpiresAt, now.AddMinutes(30)), ct);
                if (count != 1) throw new PlatformApiException("sync_in_progress", "This playlist is already being synced.", 409);
                await db.Entry(playlist).ReloadAsync(ct);
            }
            else
            {
                if (playlist.SyncLeaseExpiresAt > now) throw new PlatformApiException("sync_in_progress", "This playlist is already being synced.", 409);
                playlist.SyncLeaseId = leaseId;
                playlist.SyncLeaseExpiresAt = now.AddMinutes(30);
                await db.SaveChangesAsync(ct);
            }
            acquired = true;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromMinutes(10));
            await operation(playlist, deadline.Token);
        }
        finally
        {
            try
            {
                if (acquired)
                {
                    try
                    {
                        if (db.Database.IsRelational())
                        {
                            await db.ServicePlaylistMappings.Where(link => link.PlaylistId == playlistId
                                    && link.LastSyncStatus == "running" && link.Playlist!.SyncLeaseId == leaseId)
                                .ExecuteUpdateAsync(setters => setters.SetProperty(link => link.LastSyncStatus, "pending"),
                                    CancellationToken.None);
                            foreach (var link in db.ServicePlaylistMappings.Local.Where(link => link.PlaylistId == playlistId
                                         && link.LastSyncStatus == "running"))
                                link.LastSyncStatus = "pending";
                        }
                        else
                        {
                            var running = await db.ServicePlaylistMappings.Where(link => link.PlaylistId == playlistId
                                && link.LastSyncStatus == "running").ToListAsync(CancellationToken.None);
                            foreach (var link in running) link.LastSyncStatus = "pending";
                            if (running.Count > 0) await db.SaveChangesAsync(CancellationToken.None);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Could not clear running status for playlist {PlaylistId}", playlistId);
                    }
                    if (db.Database.IsRelational())
                        await db.Playlists.Where(item => item.Id == playlistId && item.SyncLeaseId == leaseId)
                            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.SyncLeaseId, (Guid?)null)
                                .SetProperty(item => item.SyncLeaseExpiresAt, (DateTimeOffset?)null), CancellationToken.None);
                    else
                    {
                        var playlist = await db.Playlists.SingleOrDefaultAsync(item => item.Id == playlistId, CancellationToken.None);
                        if (playlist?.SyncLeaseId == leaseId)
                        {
                            playlist.SyncLeaseId = null;
                            playlist.SyncLeaseExpiresAt = null;
                            await db.SaveChangesAsync(CancellationToken.None);
                        }
                    }
                }
            }
            finally { gate.Release(); }
        }
    }

    private async Task<List<PlaylistSyncItem>> ReadItemsAsync(Guid playlistId, CancellationToken ct)
    {
        var entries = await db.PlaylistEntries.AsNoTracking().Include(item => item.TrackObservation)
            .Where(item => item.PlaylistId == playlistId).OrderBy(item => item.Position).ToListAsync(ct);
        return await NormalizeIdentitiesAsync(entries.Select(PlaylistSyncItem.From), ct);
    }

    private async Task<List<PlaylistSyncItem>> NormalizeIdentitiesAsync(IEnumerable<PlaylistSyncItem> source, CancellationToken ct)
    {
        var items = source.ToList();
        var ids = items.Where(item => item.ObservationId.HasValue).Select(item => item.ObservationId!.Value).Distinct().ToList();
        var observations = await db.TrackObservations.AsNoTracking().Where(item => ids.Contains(item.Id))
            .Select(item => new { item.Id, item.TrackId, item.SourceType, item.ExternalId }).ToDictionaryAsync(item => item.Id, ct);
        return items.Select(item => item.ObservationId is { } id && observations.TryGetValue(id, out var observation)
            ? item with { TrackId = observation.TrackId ?? item.TrackId, ExternalId = observation.ExternalId, Service = observation.SourceType }
            : item).ToList();
    }

    private async Task SaveItemsAsync(Playlist playlist, IReadOnlyList<PlaylistSyncItem> target, CancellationToken ct)
    {
        var normalized = PlaylistSyncMerge.Normalize(target, playlist.AllowDuplicateTracks);
        var previous = await ReadItemsAsync(playlist.Id, ct);
        var entries = await db.PlaylistEntries.Where(item => item.PlaylistId == playlist.Id).OrderBy(item => item.Position).ToListAsync(ct);
        var available = entries.Select((entry, index) => (Entry: entry, Item: previous[index])).ToList();
        var retained = new PlaylistEntry?[normalized.Count];
        for (var index = 0; index < normalized.Count; index++)
        {
            var item = normalized[index];
            var match = item.ObservationId is { } observationId
                ? available.FindIndex(candidate => candidate.Item.Identity == item.Identity
                    && candidate.Entry.TrackObservationId == observationId)
                : -1;
            if (match < 0 && item.ExternalId is not null)
                match = available.FindIndex(candidate => candidate.Item.Identity == item.Identity
                    && candidate.Item.Service == item.Service && candidate.Item.ExternalId == item.ExternalId);
            if (match < 0)
                match = available.FindIndex(candidate => candidate.Item.Identity == item.Identity);
            if (match < 0) continue;
            retained[index] = available[match].Entry;
            available.RemoveAt(match);
        }

        var changed = available.Count > 0 || retained.Any(entry => entry is null);
        var positionsChanged = false;
        for (var index = 0; index < normalized.Count; index++)
        {
            var item = normalized[index];
            var entry = retained[index];
            if (entry is null) continue;
            positionsChanged |= entry.Position != index;
            changed |= entry.Position != index || entry.TrackId != item.TrackId
                || entry.TrackObservationId != item.ObservationId;
        }
        if (!changed) return;

        db.PlaylistEntries.RemoveRange(available.Select(candidate => candidate.Entry));
        // PostgreSQL checks the unique (PlaylistId, Position) index for each row update.
        // Move survivors above both the old and final ranges before assigning final positions.
        if (positionsChanged)
        {
            var temporaryPosition = checked(Math.Max(entries.Count == 0 ? -1 : entries.Max(entry => entry.Position), normalized.Count - 1) + 1);
            foreach (var entry in retained)
                if (entry is not null) entry.Position = checked(temporaryPosition++);
            await db.SaveChangesAsync(ct);
        }
        else if (available.Count > 0)
            await db.SaveChangesAsync(ct);

        for (var index = 0; index < normalized.Count; index++)
        {
            var item = normalized[index];
            if (retained[index] is { } entry)
            {
                entry.Position = index;
                if (entry.TrackId != item.TrackId || entry.TrackObservationId != item.ObservationId)
                    entry.SourceService = item.Service;
                entry.TrackId = item.TrackId;
                entry.TrackObservationId = item.ObservationId;
            }
            else
                db.PlaylistEntries.Add(new PlaylistEntry
                {
                    Id = Guid.NewGuid(), PlaylistId = playlist.Id, Position = index,
                    TrackId = item.TrackId, TrackObservationId = item.ObservationId,
                    SourceService = item.Service, AddedAt = clock.GetUtcNow()
                });
        }
        playlist.SyncRevision++;
        playlist.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    private void RecordFailure(ServicePlaylistMapping link, Exception exception)
    {
        if (exception is PlatformApiException { StatusCode: 429 } rateLimit)
        {
            link.LastSyncStatus = IsSpotifyQuotaExceeded(rateLimit) ? "quota_limited" : "rate_limited";
            link.LastError = null;
            link.NextAttemptAt = clock.GetUtcNow().Add(rateLimit.RetryAfter is { } retryAfter && retryAfter > TimeSpan.Zero
                ? retryAfter : link.Service == "spotify"
                    ? (IsSpotifyQuotaExceeded(rateLimit) ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(30))
                    : TimeSpan.FromMinutes(1));
            logger.LogWarning(exception, "Playlist link {MappingId} was deferred after a provider 429 response", link.Id);
            return;
        }
        var failure = MusicSyncFailureClassifier.Classify(exception);
        link.LastSyncStatus = "error";
        link.LastError = failure.Message;
        link.AttemptCount++;
        var retryable = failure.Retryable || failure.Code is "remote_changed" or "sync_in_progress";
        var delay = TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, link.AttemptCount)));
        if (exception is PlatformApiException { RetryAfter: { } providerDelay } && providerDelay > delay) delay = providerDelay;
        link.NextAttemptAt = retryable && link.AttemptCount < 5
            ? clock.GetUtcNow().Add(delay) : null;
        // A bare 404 may mean lost access. Retain the identity and ask the user; never cascade deletion.
        if (exception is PlatformApiException { StatusCode: 404 })
        {
            link.State = "missing";
            link.LastError = "The linked playlist is missing or inaccessible. Check the platform before removing this link.";
            link.NextAttemptAt = null;
        }
        logger.LogWarning(exception, "Playlist link {MappingId} could not sync", link.Id);
    }

    private static bool IsSpotifyQuotaExceeded(PlatformApiException exception)
        => exception.Code == "spotify_quota_exceeded"
            || exception.InnerException is PlatformApiException { Code: "spotify_quota_exceeded" };
}
