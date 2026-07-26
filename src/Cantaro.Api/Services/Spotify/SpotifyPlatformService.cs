using Cantaro.Api.Models;

namespace Cantaro.Api.Services.Spotify;

public sealed class SpotifyPlatformService(
    SpotifyService spotifyService,
    SpotifyPlaylistSyncService playlistSyncService) : IPlatformService
{
    public string PlatformId => SpotifyService.ServiceName;

    public Task<ConnectedServiceAccount?> GetConnectedAccountAsync(int userId)
        => spotifyService.GetConnectedAccountAsync(userId, CancellationToken.None);

    public Task<ConnectedServiceAccount?> GetConnectedAccountAsync(
        int userId,
        CancellationToken cancellationToken)
        => spotifyService.GetConnectedAccountAsync(userId, cancellationToken);

    public string ResolveRedirectUri(string suggestedRedirectUri)
        => spotifyService.ResolveRedirectUri(suggestedRedirectUri);

    public string GetAuthorizationUrl(string redirectUri, string state)
        => spotifyService.GetAuthorizationUrl(redirectUri, state);

    public Task<ConnectedServiceAccount> ExchangeCodeAndSaveAsync(
        int userId,
        string authorizationCode,
        string redirectUri)
        => spotifyService.ExchangeCodeAndSaveAsync(
            userId,
            authorizationCode,
            redirectUri,
            CancellationToken.None);

    public Task<ConnectedServiceAccount> ExchangeCodeAndSaveAsync(
        int userId,
        string authorizationCode,
        string redirectUri,
        CancellationToken cancellationToken)
        => spotifyService.ExchangeCodeAndSaveAsync(userId, authorizationCode, redirectUri, cancellationToken);

    public Task DisconnectAsync(int userId)
        => spotifyService.DisconnectAsync(userId, CancellationToken.None);

    public Task DisconnectAsync(int userId, CancellationToken cancellationToken)
        => spotifyService.DisconnectAsync(userId, cancellationToken);

    public Task<IReadOnlyList<PlatformPlaylistDto>> GetPlaylistsAsync(int userId)
        => GetPlaylistsAsync(userId, CancellationToken.None);

    public async Task<IReadOnlyList<PlatformPlaylistDto>> GetPlaylistsAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        var playlists = await spotifyService.GetPlaylistsAsync(userId, cancellationToken);
        return playlists.Select(playlist => new PlatformPlaylistDto
        {
            Id = playlist.Id,
            Title = playlist.Name,
            Description = playlist.Description,
            ThumbnailUrl = playlist.ImageUrl,
            ItemCount = playlist.ItemCount,
            ExternalUrl = playlist.ExternalUrl,
            OwnerName = string.IsNullOrWhiteSpace(playlist.OwnerName) ? "Spotify" : playlist.OwnerName
        }).ToList();
    }

    public Task<IReadOnlyList<PlatformSongDto>> GetPlaylistSongsAsync(int userId, string playlistId)
        => GetPlaylistSongsAsync(userId, playlistId, CancellationToken.None);

    public async Task<IReadOnlyList<PlatformSongDto>> GetPlaylistSongsAsync(
        int userId,
        string playlistId,
        CancellationToken cancellationToken)
    {
        var tracks = await spotifyService.GetPlaylistItemsAsync(userId, playlistId, cancellationToken);
        return tracks.Select(track => new PlatformSongDto
        {
            Id = track.Id,
            Title = track.Name,
            ArtistName = track.Artist,
            ThumbnailUrl = track.ImageUrl,
            Index = track.Position,
            PublishedAt = track.AddedAt,
            ExternalUrl = track.ExternalUrl,
            AlbumName = track.AlbumName,
            AlbumUrl = track.AlbumUrl,
            DurationSeconds = track.DurationSeconds
        }).ToList();
    }

    public Task<Guid> SyncPlaylistAsync(int userId, string playlistId, CancellationToken cancellationToken)
        => playlistSyncService.SyncPlaylistAsync(userId, playlistId, cancellationToken);

    public bool TryValidatePlaylistId(string playlistId, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(playlistId)
            || playlistId.Length > 100
            || !playlistId.All(char.IsLetterOrDigit))
        {
            error = "Invalid Spotify playlist ID format.";
            return false;
        }

        return true;
    }
}
