using Cantaro.Api.Models;

namespace Cantaro.Api.Services;

public sealed class SpotifyPlatformService : IPlatformService
{
    private readonly SpotifyService _spotifyService;
    private readonly SpotifyPlaylistSyncService _playlistSyncService;

    public SpotifyPlatformService(
        SpotifyService spotifyService,
        SpotifyPlaylistSyncService playlistSyncService)
    {
        _spotifyService = spotifyService;
        _playlistSyncService = playlistSyncService;
    }

    public string PlatformId => "spotify";

    public Task<ConnectedServiceAccount?> GetConnectedAccountAsync(int userId)
    {
        return _spotifyService.GetConnectedAccountAsync(userId);
    }

    public string GetAuthorizationUrl(string redirectUri, string state)
    {
        return _spotifyService.GetAuthorizationUrl(redirectUri, state);
    }

    public Task<ConnectedServiceAccount> ExchangeCodeAndSaveAsync(int userId, string authorizationCode, string redirectUri)
    {
        return _spotifyService.ExchangeCodeAndSaveAsync(userId, authorizationCode, redirectUri);
    }

    public Task DisconnectAsync(int userId)
    {
        return _spotifyService.DisconnectAsync(userId);
    }

    public async Task<IReadOnlyList<PlatformPlaylistDto>> GetPlaylistsAsync(int userId)
    {
        var playlists = await _spotifyService.GetPlaylistsAsync(userId);

        return playlists.Select(playlist => new PlatformPlaylistDto
        {
            Id = playlist.Id,
            Title = playlist.Title,
            Description = playlist.Description,
            ThumbnailUrl = playlist.ThumbnailUrl,
            ItemCount = playlist.ItemCount
        }).ToList();
    }

    public async Task<IReadOnlyList<PlatformSongDto>> GetPlaylistSongsAsync(int userId, string playlistId)
    {
        var items = await _spotifyService.GetPlaylistItemsAsync(userId, playlistId);

        return items.Select(item => new PlatformSongDto
        {
            Id = item.TrackId,
            Title = item.Title,
            Description = item.AlbumName,
            ThumbnailUrl = item.ThumbnailUrl,
            ArtistName = item.ArtistName,
            Index = item.Position,
            PublishedAt = item.AddedAt
        }).ToList();
    }

    public Task<Guid> SyncPlaylistAsync(int userId, string playlistId, CancellationToken cancellationToken)
    {
        return _playlistSyncService.SyncSpotifyPlaylistAsync(userId, playlistId, cancellationToken);
    }

    public bool TryValidatePlaylistId(string playlistId, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(playlistId))
        {
            error = "Playlist ID is required";
            return false;
        }

        if (playlistId.Length < 10 || playlistId.Length > 100 || !playlistId.All(char.IsLetterOrDigit))
        {
            error = "Invalid Spotify playlist ID format";
            return false;
        }

        return true;
    }
}
