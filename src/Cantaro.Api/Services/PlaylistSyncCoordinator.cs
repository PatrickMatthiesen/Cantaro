using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

public sealed partial class PlaylistSyncCoordinator(
    ApplicationDbContext db,
    IEnumerable<IPlaylistSyncProvider> providers,
    IEnumerable<IPlaylistWriter> writers,
    TimeProvider clock,
    ILogger<PlaylistSyncCoordinator> logger)
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, SemaphoreSlim> Gates = new();

    public async Task QueueAsync(int userId, Guid playlistId, string? service, CancellationToken ct)
    {
        var playlist = await RequirePlaylistAsync(userId, playlistId, ct);
        var now = clock.GetUtcNow();
        if (service is null)
        {
            var cooldown = await db.ServicePlaylistMappings.Where(link => link.PlaylistId == playlistId
                && link.State == "active" && (link.LastSyncStatus == "rate_limited" || link.LastSyncStatus == "quota_limited") && link.NextAttemptAt > now)
                .MaxAsync(link => link.NextAttemptAt, ct);
            playlist.NextSyncAt = cooldown ?? now;
        }
        else
        {
            var links = await db.ServicePlaylistMappings.Where(link => link.PlaylistId == playlistId
                && link.Service == service && link.State == "active").ToListAsync(ct);
            if (links.Count == 0) throw new PlatformApiException("link_not_found", "Attach or create a playlist on this platform first.", 404);
            foreach (var link in links)
            {
                if (IsProviderCooldown(link.LastSyncStatus) && link.NextAttemptAt > now) continue;
                link.NextAttemptAt = now;
                link.AttemptCount = 0;
                link.LastSyncStatus = "pending";
                link.LastError = null;
            }
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task EnableAsync(int userId, Guid playlistId, CancellationToken ct)
    {
        var playlist = await RequirePlaylistAsync(userId, playlistId, ct);
        playlist.SyncEnabled = true;
        playlist.NextSyncAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    public Task RunAsync(int userId, Guid playlistId, string? service, CancellationToken ct, bool readOnly = false)
        => WithLeaseAsync(userId, playlistId, async (playlist, token) =>
        {
            await RunCoreAsync(playlist, service, readOnly, token);
        }, ct);

    public Task WithPlaylistLeaseAsync(int userId, Guid playlistId, Func<CancellationToken, Task> operation, CancellationToken ct)
        => WithLeaseAsync(userId, playlistId, (_, token) => operation(token), ct);

    public Task InitializeLinkAsync(int userId, Guid playlistId, Guid mappingId, string initialMode, CancellationToken ct)
        => WithLeaseAsync(userId, playlistId, async (playlist, token) =>
        {
            if (initialMode is not ("combine" or "platform" or "cantaro"))
                throw new PlatformApiException("invalid_initial_mode", "Choose how to initialize this playlist link.", 400);
            var link = await db.ServicePlaylistMappings.SingleAsync(item => item.Id == mappingId && item.PlaylistId == playlistId, token);
            var account = await RequireAccountAsync(playlist, link, token);
            var remote = await Provider(link.Service).ReadAsync(account, link.ServicePlaylistId, token);
            RequireComplete(remote);
            var incoming = await NormalizeIdentitiesAsync(KnownTracks(remote).Select(item => PlaylistSyncItem.From(item, link.Service)), token);
            var current = await ReadItemsAsync(playlistId, token);
            var target = initialMode switch
            {
                "platform" => PlaylistSyncMerge.Normalize(incoming, playlist.AllowDuplicateTracks),
                "combine" => PlaylistSyncMerge.Merge(current, null, incoming, playlist.AllowDuplicateTracks).Items,
                _ => current
            };
            await PersistAtomicallyAsync(async () =>
            {
                await SaveItemsAsync(playlist, target, token);
                link.BaselineJson = new PlaylistSyncBaseline(incoming, remote.Revision).Serialize();
                link.BaselineName = remote.Name;
                link.InitialMode = null;
                link.NextAttemptAt = clock.GetUtcNow();
                link.State = "active";
                link.UserId = userId;
                link.ExternalAccountId = account.ExpectedExternalAccountId!;
                playlist.SyncEnabled = true;
                playlist.NextSyncAt ??= clock.GetUtcNow().AddDays(1);
                await db.SaveChangesAsync(token);
            }, token);
        }, ct);

    private async Task RunCoreAsync(Playlist playlist, string? service, bool readOnly, CancellationToken ct)
    {
        var links = await db.ServicePlaylistMappings.Where(link => link.PlaylistId == playlist.Id
            && link.State == "active" && (service == null || link.Service == service)).OrderBy(link => link.Service).ToListAsync(ct);
        if (links.Any(link => IsProviderCooldown(link.LastSyncStatus) && link.NextAttemptAt > clock.GetUtcNow()))
            return;
        var reads = new List<LinkRead>();
        foreach (var link in links)
        {
            var previousStatus = link.LastSyncStatus;
            try
            {
                link.LastSyncStatus = "running";
                await db.SaveChangesAsync(ct);
                var account = await RequireAccountAsync(playlist, link, ct);
                var snapshot = await Provider(link.Service).ReadAsync(account, link.ServicePlaylistId, ct);
                RequireComplete(snapshot);
                var incoming = await NormalizeIdentitiesAsync(KnownTracks(snapshot).Select(item => PlaylistSyncItem.From(item, link.Service)), ct);
                var baseline = PlaylistSyncBaseline.Parse(link.BaselineJson);
                var previous = baseline is null ? null : await NormalizeIdentitiesAsync(baseline.Items, ct);
                if (PlaylistSyncBaseline.Parse(link.PendingWriteJson) is { } interruptedWrite)
                {
                    // A provider mutation can stop halfway through a reorder. Its temporary
                    // omissions are our unfinished work, not evidence of a user's deletion.
                    var attempted = await NormalizeIdentitiesAsync(interruptedWrite.Items, ct);
                    var known = (previous ?? []).Concat(attempted).Select(item => item.Identity).ToHashSet();
                    previous = incoming.Where(item => known.Contains(item.Identity)).ToList();
                }
                reads.Add(new(link, account, snapshot, incoming, previous, previousStatus));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                RecordFailure(link, ex);
                await db.SaveChangesAsync(ct);
                if (readOnly) throw;
            }
        }

        if (reads.Count != links.Count)
        {
            // A daily fan-out must not write while one of the selected sources is unreadable.
            foreach (var read in reads) read.Link.LastSyncStatus = read.PreviousStatus;
            if (service is null) playlist.NextSyncAt = clock.GetUtcNow().AddDays(1);
            await db.SaveChangesAsync(ct);
            return;
        }

        // Read every selected copy before writing any. Each baseline reflects that copy's acknowledged subset.
        var current = await ReadItemsAsync(playlist.Id, ct);
        foreach (var read in reads)
        {
            var link = read.Link;
            if (link.SyncMode != "from_cantaro")
            {
                var merged = PlaylistSyncMerge.Merge(current, read.Previous, read.Items, playlist.AllowDuplicateTracks);
                current = merged.Items;
                read.OrderConflict = merged.OrderConflict;
            }
            if (link.BaselineName is null) link.BaselineName = read.Snapshot.Name;
            if (read.Snapshot.Name != link.BaselineName && read.Snapshot.Name != playlist.Name
                && read.Snapshot.Name != link.RejectedName)
            {
                link.PendingName = read.Snapshot.Name;
                // Approval covered the earlier observed name, not a subsequent external edit.
                link.DesiredName = null;
            }
            else link.PendingName = null;
        }
        await PersistAtomicallyAsync(async () =>
        {
            await SaveItemsAsync(playlist, current, ct);
            foreach (var read in reads)
            {
                // Persist accepted reads with canonical changes. An interrupted write restarts from this exact observed subset.
                if (!read.OrderConflict)
                    read.Link.BaselineJson = new PlaylistSyncBaseline(read.Items, read.Snapshot.Revision).Serialize();
                if (read.OrderConflict) read.Link.LastSyncStatus = "order_conflict";
                else read.Link.LastSyncStatus = readOnly ? read.PreviousStatus : "running";
            }
            await db.SaveChangesAsync(ct);
        }, ct);

        if (readOnly) return;
        if (service is null && playlist.SyncEnabled)
            await CreateMissingLinksAsync(playlist, reads, ct);

        foreach (var read in reads)
        {
            try
            {
                if (read.OrderConflict)
                {
                    read.Link.LastError = "Playlist order changed differently on linked platforms. Choose an order before syncing this copy.";
                    read.Link.NextAttemptAt = null;
                    continue;
                }
                if (read.Link.SyncMode != "import_only") await WriteLinkAsync(playlist, read, ct);
                else
                {
                    read.Link.UnresolvedCount = 0;
                    read.Link.LastSyncedAt = clock.GetUtcNow();
                    read.Link.LastSyncStatus = "success";
                    read.Link.NextAttemptAt = null;
                    read.Link.AttemptCount = 0;
                    read.Link.LastError = null;
                }
                await db.SaveChangesAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { RecordFailure(read.Link, ex); await db.SaveChangesAsync(ct); }
        }
        if (service is null) playlist.NextSyncAt = clock.GetUtcNow().AddDays(1);
        await db.SaveChangesAsync(ct);
    }

    private async Task<Playlist> RequirePlaylistAsync(int userId, Guid id, CancellationToken ct)
        => await db.Playlists.SingleOrDefaultAsync(item => item.Id == id && item.UserId == userId, ct)
            ?? throw new PlatformApiException("playlist_not_found", "Playlist not found.", 404);

    private async Task<PlatformAccountContext> RequireAccountAsync(Playlist playlist, ServicePlaylistMapping link, CancellationToken ct)
    {
        var account = await db.ConnectedServiceAccounts.AsNoTracking().SingleOrDefaultAsync(item => item.Id == link.ConnectedServiceAccountId
            && item.UserId == playlist.UserId && item.Service == link.Service, ct);
        if (account is null || account.ConnectionState != "connected"
            || (!string.IsNullOrEmpty(link.ExternalAccountId) && account.ExternalAccountId != link.ExternalAccountId))
            throw new PlatformApiException("account_changed", "Reconnect the linked platform account before syncing.", 409);
        link.UserId = playlist.UserId;
        link.ExternalAccountId = account.ExternalAccountId;
        return new(playlist.UserId, account.Id, account.ExternalAccountId);
    }

    private IPlaylistSyncProvider Provider(string service) => providers.Single(provider => provider.PlatformId == service);
    private IPlaylistWriter Writer(string service) => writers.Single(writer => writer.PlatformId == service);
    private static bool IsProviderCooldown(string? status) => status is "rate_limited" or "quota_limited";
    // Unavailable remote slots have no new metadata to import. Known identities still
    // participate in reconciliation so loss of access never looks like a deletion.
    private static IEnumerable<PlaylistRemoteTrack> KnownTracks(PlaylistRemoteSnapshot snapshot)
        => snapshot.Tracks.Where(item => item.TrackId is not null || item.ObservationId is not null);

    private static void RequireComplete(PlaylistRemoteSnapshot snapshot)
    {
        if (!snapshot.IsComplete || snapshot.Tracks.Any(item => item.IsAvailable && item.TrackId is null && item.ObservationId is null))
            throw new PlatformApiException("incomplete_playlist", "The platform returned an incomplete playlist. Its missing items were not treated as removals.", 503);
    }

    private sealed record LinkRead(ServicePlaylistMapping Link, PlatformAccountContext Account,
        PlaylistRemoteSnapshot Snapshot, List<PlaylistSyncItem> Items, List<PlaylistSyncItem>? Previous,
        string? PreviousStatus = null)
    {
        public bool OrderConflict { get; set; }
    }
}
