using Cantaro.Api.Models;

namespace Cantaro.Api.Services;

public interface IPlatformService
{
    string PlatformId { get; }

    Task<ConnectedServiceAccount?> GetConnectedAccountAsync(int userId);

    string GetAuthorizationUrl(string redirectUri, string state);

    Task<ConnectedServiceAccount> ExchangeCodeAndSaveAsync(int userId, string authorizationCode, string redirectUri);

    Task DisconnectAsync(int userId);

    Task<IReadOnlyList<PlatformPlaylistDto>> GetPlaylistsAsync(int userId);

    Task<IReadOnlyList<PlatformSongDto>> GetPlaylistSongsAsync(int userId, string playlistId);

    Task<Guid> SyncPlaylistAsync(int userId, string playlistId, CancellationToken cancellationToken);

    bool TryValidatePlaylistId(string playlistId, out string? error);
}