using Cantaro.Api.Models;
using Cantaro.Api.Services;

namespace Cantaro.Api.Services.Spotify;

/// <summary>
/// Routes Spotify playlist imports through the shared sync coordinator.
/// </summary>
public sealed class SpotifyPlaylistSyncService(PlaylistSyncCoordinator coordinator)
{
    public Task<Guid> SyncPlaylistAsync(
        PlatformAccountContext account,
        string spotifyPlaylistId,
        CancellationToken cancellationToken)
        => coordinator.ImportAsync(account, SpotifyService.ServiceName, spotifyPlaylistId, cancellationToken);
}
