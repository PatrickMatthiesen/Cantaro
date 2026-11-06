using Cantaro.Api.Models;

namespace Cantaro.Api.Services;

/// <summary>
/// Routes YouTube playlist imports through the shared sync coordinator.
/// </summary>
public sealed class YouTubePlaylistSyncService(PlaylistSyncCoordinator coordinator)
{
    public Task<Guid> SyncYouTubePlaylistAsync(
        PlatformAccountContext account,
        string youtubePlaylistId,
        CancellationToken cancellationToken)
        => SyncYouTubePlaylistAsync(account, youtubePlaylistId, reportProgressAsync: null, cancellationToken);

    public Task<Guid> SyncYouTubePlaylistAsync(
        PlatformAccountContext account,
        string youtubePlaylistId,
        Func<PlatformSyncProgress, CancellationToken, Task>? reportProgressAsync,
        CancellationToken cancellationToken)
        => coordinator.ImportAsync(account, "youtube", youtubePlaylistId, cancellationToken);
}
