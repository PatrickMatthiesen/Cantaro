using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

public sealed partial class PlaylistSyncCoordinator
{
    public async Task<Guid> ImportAsync(PlatformAccountContext account, string service, string remoteId, CancellationToken ct)
    {
        var connected = await db.ConnectedServiceAccounts.AsNoTracking().SingleOrDefaultAsync(item => item.Id == account.ConnectedServiceAccountId
            && item.UserId == account.UserId && item.Service == service && item.ConnectionState == "connected", ct)
            ?? throw new PlatformApiException("account_changed", "Reconnect the original platform account before importing.", 409);
        if (account.ExpectedExternalAccountId is { } expected && connected.ExternalAccountId != expected)
            throw new PlatformApiException("account_changed", "The connected platform account changed.", 409);
        var pinned = account with { ExpectedExternalAccountId = connected.ExternalAccountId };
        var existing = await db.ServicePlaylistMappings.SingleOrDefaultAsync(link => link.Service == service
            && link.ServicePlaylistId == remoteId && link.UserId == account.UserId && link.ExternalAccountId == connected.ExternalAccountId
            && link.State != "unlinked", ct);
        if (existing is not null)
        {
            if (existing.State is "unlinked" or "missing")
                throw new PlatformApiException("link_removed", "This playlist was previously unlinked. Attach it explicitly to resume syncing.", 409);
            await QueueAsync(account.UserId, existing.PlaylistId, service, ct);
            return existing.PlaylistId;
        }

        if (await db.ServicePlaylistMappings.AnyAsync(link => link.Service == service && link.ServicePlaylistId == remoteId
            && link.UserId == account.UserId && link.ExternalAccountId == connected.ExternalAccountId && link.State == "unlinked", ct))
            throw new PlatformApiException("link_removed", "This playlist was unlinked. Attach it to the intended Cantaro playlist to resume syncing.", 409);

        var snapshot = await Provider(service).ReadAsync(pinned, remoteId, ct);
        RequireComplete(snapshot);
        var now = clock.GetUtcNow();
        var playlist = new Playlist
        {
            Id = Guid.NewGuid(), UserId = account.UserId, Name = snapshot.Name, ImportedFromService = service,
            CreatedAt = now, UpdatedAt = now, SyncEnabled = true, NextSyncAt = now
        };
        var mapping = new ServicePlaylistMapping
        {
            Id = Guid.NewGuid(), PlaylistId = playlist.Id, UserId = account.UserId,
            ConnectedServiceAccountId = connected.Id, ExternalAccountId = connected.ExternalAccountId,
            Service = service, ServicePlaylistId = remoteId, SyncMode = "bidirectional", State = "active",
            BaselineName = snapshot.Name, BaselineJson = new PlaylistSyncBaseline(KnownTracks(snapshot).Select(item => PlaylistSyncItem.From(item, service)).ToList(), snapshot.Revision).Serialize(),
            LastSyncedAt = now, LastSyncStatus = "success"
        };
        await PersistAtomicallyAsync(async () =>
        {
            db.Playlists.Add(playlist);
            db.ServicePlaylistMappings.Add(mapping);
            await db.SaveChangesAsync(ct);
            await SaveItemsAsync(playlist, KnownTracks(snapshot).Select(item => PlaylistSyncItem.From(item, service)).ToList(), ct);
            await db.SaveChangesAsync(ct);
        }, ct);
        return playlist.Id;
    }

    public Task CreateLinkAsync(int userId, Guid playlistId, string service, CancellationToken ct)
        => CreateLinkCoreAsync(userId, playlistId, service, _ => Task.CompletedTask,
            reviewedCreate: false, ct: ct);

    public Task CreateLinkAsync(int userId, Guid playlistId, string service,
        Func<CancellationToken, Task> validateBeforeCreate, CancellationToken ct)
        => CreateLinkCoreAsync(userId, playlistId, service, validateBeforeCreate,
            reviewedCreate: true, ct: ct);

    private Task CreateLinkCoreAsync(int userId, Guid playlistId, string service,
        Func<CancellationToken, Task> validateBeforeCreate, bool reviewedCreate, CancellationToken ct)
        => WithLeaseAsync(userId, playlistId, async (playlist, token) =>
        {
            if (service is not ("youtube" or "spotify")) throw new PlatformApiException("invalid_platform", "Choose YouTube or Spotify.", 400);
            var connected = await db.ConnectedServiceAccounts.AsNoTracking().SingleOrDefaultAsync(account => account.UserId == userId
                && account.Service == service && account.ConnectionState == "connected", token);
            if (connected is null) throw new PlatformApiException("account_not_connected", "Connect this platform before creating its playlist.", 409);
            var current = await db.ServicePlaylistMappings.FirstOrDefaultAsync(link =>
                link.PlaylistId == playlistId && link.Service == service && link.State != "unlinked", token);
            if (current?.State == "active")
            {
                await QueueAsync(userId, playlistId, service, token);
                return;
            }
            if (current?.State is "creating" or "creation_uncertain")
                throw new PlatformApiException("playlist_creation_unavailable",
                    current.LastError ?? "Check whether the platform created this playlist before trying again.", 409);
            await validateBeforeCreate(token);
            var tombstones = await db.ServicePlaylistMappings.Where(link => link.PlaylistId == playlistId && link.Service == service
                && link.ExternalAccountId == connected.ExternalAccountId && (link.State == "unlinked" || link.State == "creation_failed")).ToListAsync(token);
            db.ServicePlaylistMappings.RemoveRange(tombstones);
            await db.SaveChangesAsync(token);
            var reads = new List<LinkRead>();
            await CreateMissingLinksAsync(playlist, reads, token, service,
                skipDuplicateNameCheck: reviewedCreate);
            if (reads.Count == 0)
            {
                var existing = await db.ServicePlaylistMappings.FirstOrDefaultAsync(link => link.PlaylistId == playlistId && link.Service == service, token);
                throw new PlatformApiException("playlist_creation_unavailable", existing?.LastError
                    ?? "The platform playlist could not be created. Check the connection or attach an existing playlist.", 409);
            }
            foreach (var read in reads)
            {
                read.Link.NextAttemptAt = clock.GetUtcNow();
                read.Link.LastSyncStatus = "pending";
            }
            await db.SaveChangesAsync(token);
        }, ct);
}
