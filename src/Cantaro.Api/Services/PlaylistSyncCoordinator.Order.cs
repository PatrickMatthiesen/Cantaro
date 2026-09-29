using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

public sealed partial class PlaylistSyncCoordinator
{
    public Task ResolveOrderAsync(int userId, Guid playlistId, Guid mappingId, string use, CancellationToken ct)
        => WithLeaseAsync(userId, playlistId, async (playlist, token) =>
        {
            if (use is not ("cantaro" or "platform"))
                throw new PlatformApiException("invalid_order", "Choose which playlist order to keep.", 400);
            var link = await db.ServicePlaylistMappings.SingleOrDefaultAsync(item => item.Id == mappingId
                && item.PlaylistId == playlistId && item.UserId == userId && item.State == "active", token)
                ?? throw new PlatformApiException("link_not_found", "Playlist link not found.", 404);
            if (link.LastSyncStatus != "order_conflict")
                throw new PlatformApiException("order_already_resolved", "This playlist no longer has an order conflict.", 409);
            var account = await RequireAccountAsync(playlist, link, token);
            var remote = await Provider(link.Service).ReadAsync(account, link.ServicePlaylistId, token);
            RequireComplete(remote);
            var incoming = await NormalizeIdentitiesAsync(KnownTracks(remote).Select(item => PlaylistSyncItem.From(item, link.Service)), token);
            var baseline = PlaylistSyncBaseline.Parse(link.BaselineJson);
            var previous = baseline is null ? null : await NormalizeIdentitiesAsync(baseline.Items, token);
            var current = await ReadItemsAsync(playlistId, token);
            var merged = link.SyncMode == "from_cantaro" ? current
                : PlaylistSyncMerge.Merge(current, previous, incoming, playlist.AllowDuplicateTracks).Items;
            if (use == "platform") merged = UseRemoteOrder(merged, incoming);
            await PersistAtomicallyAsync(async () =>
            {
                await SaveItemsAsync(playlist, merged, token);
                link.BaselineJson = new PlaylistSyncBaseline(incoming, remote.Revision).Serialize();
                link.LastSyncStatus = "pending";
                link.LastError = null;
                link.NextAttemptAt = clock.GetUtcNow();
                await db.SaveChangesAsync(token);
            }, token);
        }, ct);

    private static List<PlaylistSyncItem> UseRemoteOrder(List<PlaylistSyncItem> current, List<PlaylistSyncItem> incoming)
    {
        var available = current.GroupBy(item => item.Identity).ToDictionary(group => group.Key, group => new Queue<PlaylistSyncItem>(group));
        var ordered = new Queue<PlaylistSyncItem>();
        foreach (var item in incoming)
            if (available.TryGetValue(item.Identity, out var occurrences) && occurrences.TryDequeue(out var occurrence))
                ordered.Enqueue(occurrence);
        var counts = ordered.GroupBy(item => item.Identity).ToDictionary(group => group.Key, group => group.Count());
        return current.Select(item =>
        {
            if (counts.GetValueOrDefault(item.Identity) == 0) return item;
            counts[item.Identity]--;
            return ordered.Dequeue();
        }).ToList();
    }
}
