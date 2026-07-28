using Cantaro.Api.Models;

namespace Cantaro.Api.Services;

public sealed class YouTubePlatformService : IPlatformService
{
    private readonly YouTubeService _youtubeService;
    private readonly YouTubePlaylistSyncService _playlistSyncService;

    public YouTubePlatformService(
        YouTubeService youtubeService,
        YouTubePlaylistSyncService playlistSyncService)
    {
        _youtubeService = youtubeService;
        _playlistSyncService = playlistSyncService;
    }

    public string PlatformId => "youtube";

    public Task<ConnectedServiceAccount?> GetConnectedAccountAsync(int userId)
    {
        return _youtubeService.GetConnectedAccountAsync(userId);
    }

    public string GetAuthorizationUrl(string redirectUri, string state)
    {
        return _youtubeService.GetAuthorizationUrl(redirectUri, state);
    }

    public Task<ConnectedServiceAccount> ExchangeCodeAndSaveAsync(int userId, string authorizationCode, string redirectUri)
    {
        return _youtubeService.ExchangeCodeAndSaveAsync(userId, authorizationCode, redirectUri);
    }

    public Task DisconnectAsync(int userId)
    {
        return _youtubeService.DisconnectAsync(userId);
    }

    public async Task<IReadOnlyList<PlatformPlaylistDto>> GetPlaylistsAsync(int userId)
    {
        var playlists = await _youtubeService.GetPlaylistsAsync(userId);

        return playlists.Select(playlist => new PlatformPlaylistDto
        {
            Id = playlist.Id,
            Title = playlist.Title,
            Description = playlist.Description,
            ThumbnailUrl = playlist.ThumbnailUrl,
            ItemCount = playlist.ItemCount,
            PublishedAt = playlist.PublishedAt
        }).ToList();
    }

    public async Task<IReadOnlyList<PlatformSongDto>> GetPlaylistSongsAsync(int userId, string playlistId)
    {
        var items = await _youtubeService.GetPlaylistItemsAsync(userId, playlistId);

        return items.Select(item => new PlatformSongDto
        {
            Id = item.VideoId,
            Title = item.Title,
            Description = item.Description,
            ThumbnailUrl = item.ThumbnailUrl,
            ArtistName = item.ChannelTitle,
            Index = item.Position,
            PublishedAt = item.PublishedAt
        }).ToList();
    }

    public Task<Guid> SyncPlaylistAsync(int userId, string playlistId, CancellationToken cancellationToken)
    {
        return _playlistSyncService.SyncYouTubePlaylistAsync(userId, playlistId, cancellationToken);
    }

    public Task<Guid> SyncPlaylistAsync(
        int userId,
        string playlistId,
        Func<PlatformSyncProgress, CancellationToken, Task> reportProgressAsync,
        CancellationToken cancellationToken)
    {
        return _playlistSyncService.SyncYouTubePlaylistAsync(
            userId,
            playlistId,
            reportProgressAsync,
            cancellationToken);
    }

    public bool TryValidatePlaylistId(string playlistId, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(playlistId))
        {
            error = "Playlist ID is required";
            return false;
        }

        if (playlistId.Length < 11 || playlistId.Length > 100 || !playlistId.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-'))
        {
            error = "Invalid playlist ID format";
            return false;
        }

        return true;
    }
}
