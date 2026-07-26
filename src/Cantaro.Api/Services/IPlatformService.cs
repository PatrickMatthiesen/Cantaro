using Cantaro.Api.Models;

namespace Cantaro.Api.Services;

public interface IPlatformService
{
    string PlatformId { get; }

    Task<ConnectedServiceAccount?> GetConnectedAccountAsync(int userId);

    Task<ConnectedServiceAccount?> GetConnectedAccountAsync(int userId, CancellationToken cancellationToken)
        => GetConnectedAccountAsync(userId);

    string ResolveRedirectUri(string suggestedRedirectUri) => suggestedRedirectUri;

    string GetAuthorizationUrl(string redirectUri, string state);

    Task<ConnectedServiceAccount> ExchangeCodeAndSaveAsync(int userId, string authorizationCode, string redirectUri);

    Task<ConnectedServiceAccount> ExchangeCodeAndSaveAsync(
        int userId,
        string authorizationCode,
        string redirectUri,
        CancellationToken cancellationToken)
        => ExchangeCodeAndSaveAsync(userId, authorizationCode, redirectUri);

    Task DisconnectAsync(int userId);

    Task DisconnectAsync(int userId, CancellationToken cancellationToken)
        => DisconnectAsync(userId);

    Task<IReadOnlyList<PlatformPlaylistDto>> GetPlaylistsAsync(int userId);

    Task<IReadOnlyList<PlatformPlaylistDto>> GetPlaylistsAsync(int userId, CancellationToken cancellationToken)
        => GetPlaylistsAsync(userId);

    Task<IReadOnlyList<PlatformSongDto>> GetPlaylistSongsAsync(int userId, string playlistId);

    Task<IReadOnlyList<PlatformSongDto>> GetPlaylistSongsAsync(
        int userId,
        string playlistId,
        CancellationToken cancellationToken)
        => GetPlaylistSongsAsync(userId, playlistId);

    Task<Guid> SyncPlaylistAsync(int userId, string playlistId, CancellationToken cancellationToken);

    bool TryValidatePlaylistId(string playlistId, out string? error);
}
