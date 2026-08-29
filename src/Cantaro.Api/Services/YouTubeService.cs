using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Xml;

namespace Cantaro.Api.Services;

public sealed record YouTubePlaylistRemovalResult(IReadOnlyList<long?> Positions)
{
    public bool Changed => Positions.Count > 0;
}

public sealed class YouTubePlaylistReconciliationException(string message, Exception innerException)
    : Exception(message, innerException);

internal sealed record YouTubePlaylistItemMappingResult(
    YouTubePlaylistItemDto? Item,
    string? SkipReason,
    long? ProviderPosition);

/// <summary>
/// DTO for YouTube playlist information
/// </summary>
public class YouTubePlaylistDto
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public int ItemCount { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}

/// <summary>
/// DTO for YouTube playlist item/video
/// </summary>
public class YouTubePlaylistItemDto
{
    public required string VideoId { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? ChannelTitle { get; set; }
    public int Position { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public int? DurationSeconds { get; set; }
}

public sealed class YouTubeVideoMetadataDto
{
    public required string VideoId { get; init; }
    public required string Title { get; init; }
    public string? ChannelTitle { get; init; }
    public string? ThumbnailUrl { get; init; }
    public string? CategoryId { get; init; }
    public bool LicensedContent { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<string> TopicCategories { get; init; } = [];
    public int? DurationSeconds { get; init; }
}

/// <summary>
/// Service for YouTube OAuth and API operations
/// </summary>
public class YouTubeService
{
    private readonly IConfiguration _configuration;
    private readonly ApplicationDbContext _dbContext;
    private readonly TokenEncryptionService _tokenEncryption;
    private readonly ILogger<YouTubeService> _logger;

    private const string ServiceName = "youtube";

    public YouTubeService(
        IConfiguration configuration,
        ApplicationDbContext dbContext,
        TokenEncryptionService tokenEncryption,
        ILogger<YouTubeService> logger)
    {
        _configuration = configuration;
        _dbContext = dbContext;
        _tokenEncryption = tokenEncryption;
        _logger = logger;
    }

    private static DateTimeOffset? ParsePublishedAtRaw(string? publishedAtRaw)
    {
        if (string.IsNullOrWhiteSpace(publishedAtRaw))
            return null;

        // Try a forgiving parse; assume UTC when no offset is present and adjust to UTC
        if (DateTimeOffset.TryParse(publishedAtRaw, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
        {
            return dto;
        }

        // If the API returns an unexpected format, return null rather than throw
        return null;
    }

    public async Task<YouTubeVideoMetadataDto?> GetVideoMetadataAsync(int userId, string videoId, CancellationToken cancellationToken)
    {
        var account = await GetConnectedAccountAsync(userId);
        if (account is null) return null;
        VideoListResponse response;
        try
        {
            using var youtubeService = await CreateYouTubeServiceAsync(account);
            var request = youtubeService.Videos.List("snippet,contentDetails,topicDetails");
            request.Id = videoId;
            request.MaxResults = 1;
            response = await request.ExecuteAsync(cancellationToken);
        }
        catch (Google.Apis.Auth.OAuth2.Responses.TokenResponseException ex)
        {
            _logger.LogWarning(ex, "YouTube metadata is unavailable because the account for user {UserId} needs reconnecting", userId);
            return null;
        }
        var video = response.Items?.FirstOrDefault();
        if (video is null) return null;

        int? durationSeconds = null;
        if (!string.IsNullOrWhiteSpace(video.ContentDetails?.Duration))
        {
            try { durationSeconds = (int)Math.Round(XmlConvert.ToTimeSpan(video.ContentDetails.Duration).TotalSeconds); }
            catch (FormatException) { }
        }

        return new YouTubeVideoMetadataDto
        {
            VideoId = video.Id,
            Title = video.Snippet?.Title ?? videoId,
            ChannelTitle = video.Snippet?.ChannelTitle,
            ThumbnailUrl = video.Snippet?.Thumbnails?.High?.Url ?? video.Snippet?.Thumbnails?.Default__?.Url,
            CategoryId = video.Snippet?.CategoryId,
            LicensedContent = video.ContentDetails?.LicensedContent ?? false,
            Tags = video.Snippet?.Tags?.ToArray() ?? [],
            TopicCategories = video.TopicDetails?.TopicCategories?.ToArray() ?? [],
            DurationSeconds = durationSeconds
        };
    }

    /// <summary>
    /// Gets the Google OAuth authorization URL for YouTube
    /// </summary>
    public string GetAuthorizationUrl(string redirectUri, string state)
    {
        var clientId = _configuration["YouTube:ClientId"]
            ?? throw new InvalidOperationException("YouTube:ClientId is not configured");

        var scopes = new[]
        {
            "https://www.googleapis.com/auth/youtube.force-ssl",
            "openid",
            "email",
            "profile"
        };

        var url = $"https://accounts.google.com/o/oauth2/v2/auth?" +
            $"client_id={Uri.EscapeDataString(clientId)}&" +
            $"redirect_uri={Uri.EscapeDataString(redirectUri)}&" +
            $"response_type=code&" +
            $"scope={Uri.EscapeDataString(string.Join(" ", scopes))}&" +
            $"access_type=offline&" +
            $"prompt=consent&" +
            $"state={Uri.EscapeDataString(state)}";

        return url;
    }

    public async Task<bool> AddVideoToPlaylistAsync(int userId, string playlistId, string videoId, CancellationToken cancellationToken)
        => await AddVideoToPlaylistAsync(userId, playlistId, videoId, position: null, cancellationToken);

    public async Task<bool> AddVideoToPlaylistAsync(
        int userId,
        string playlistId,
        string videoId,
        long? position,
        CancellationToken cancellationToken)
    {
        var account = await GetConnectedAccountAsync(userId) ?? throw new InvalidOperationException("YouTube account not connected");
        if (!HasPlaylistWriteScope(account.Scopes))
            throw new InvalidOperationException("Reconnect YouTube to allow playlist edits.");
        using var service = await CreateYouTubeServiceAsync(account);
        var existing = service.PlaylistItems.List("id");
        existing.PlaylistId = playlistId;
        existing.VideoId = videoId;
        existing.MaxResults = 1;
        if ((await existing.ExecuteAsync(cancellationToken)).Items?.Count > 0)
            return false;
        await InsertVideoIntoPlaylistAsync(service, playlistId, videoId, position, cancellationToken);
        return true;
    }

    public async Task RestoreVideoToPlaylistAsync(
        int userId,
        string playlistId,
        string videoId,
        long? position,
        CancellationToken cancellationToken)
    {
        var account = await GetConnectedAccountAsync(userId) ?? throw new InvalidOperationException("YouTube account not connected");
        if (!HasPlaylistWriteScope(account.Scopes))
            throw new InvalidOperationException("Reconnect YouTube to allow playlist edits.");
        using var service = await CreateYouTubeServiceAsync(account);
        await InsertVideoIntoPlaylistAsync(service, playlistId, videoId, position, cancellationToken);
    }

    private static async Task InsertVideoIntoPlaylistAsync(
        Google.Apis.YouTube.v3.YouTubeService service,
        string playlistId,
        string videoId,
        long? position,
        CancellationToken cancellationToken)
    {
        var item = new PlaylistItem
        {
            Snippet = new PlaylistItemSnippet
            {
                PlaylistId = playlistId,
                Position = position,
                ResourceId = new ResourceId { Kind = "youtube#video", VideoId = videoId }
            }
        };
        await service.PlaylistItems.Insert(item, "snippet").ExecuteAsync(cancellationToken);
    }

    public async Task<YouTubePlaylistRemovalResult> RemoveVideoFromPlaylistAsync(int userId, string playlistId, string videoId, CancellationToken cancellationToken)
    {
        var account = await GetConnectedAccountAsync(userId) ?? throw new InvalidOperationException("YouTube account not connected");
        if (!HasPlaylistWriteScope(account.Scopes))
            throw new InvalidOperationException("Reconnect YouTube to allow playlist edits.");
        using var service = await CreateYouTubeServiceAsync(account);
        var items = new List<PlaylistItem>();
        string? pageToken = null;
        do
        {
            var list = service.PlaylistItems.List("id,snippet,contentDetails");
            list.PlaylistId = playlistId;
            list.VideoId = videoId;
            list.MaxResults = 50;
            list.PageToken = pageToken;
            var response = await list.ExecuteAsync(cancellationToken);
            if (response.Items is not null) items.AddRange(response.Items);
            pageToken = response.NextPageToken;
        }
        while (!string.IsNullOrWhiteSpace(pageToken));

        if (items.Count == 0) return new YouTubePlaylistRemovalResult([]);
        var deletedItems = new List<PlaylistItem>();
        try
        {
            foreach (var item in items)
            {
                await service.PlaylistItems.Delete(item.Id).ExecuteAsync(cancellationToken);
                deletedItems.Add(item);
            }
        }
        catch (Exception deleteException)
        {
            try
            {
                foreach (var item in deletedItems.OrderBy(item => item.Snippet?.Position))
                    await InsertVideoIntoPlaylistAsync(service, playlistId, videoId, item.Snippet?.Position, CancellationToken.None);
            }
            catch (Exception restoreException)
            {
                throw new YouTubePlaylistReconciliationException(
                    "YouTube changed and the partial playlist update could not be rolled back.",
                    new AggregateException(deleteException, restoreException));
            }

            throw;
        }

        return new YouTubePlaylistRemovalResult(deletedItems.Select(item => item.Snippet?.Position).Order().ToList());
    }

    private static bool HasPlaylistWriteScope(string? scopes) => (scopes ?? string.Empty)
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Any(scope => scope is "https://www.googleapis.com/auth/youtube" or "https://www.googleapis.com/auth/youtube.force-ssl");

    /// <summary>
    /// Exchanges authorization code for tokens and saves the connected account
    /// </summary>
    public async Task<ConnectedServiceAccount> ExchangeCodeAndSaveAsync(
        int userId,
        string authorizationCode,
        string redirectUri)
    {
        var clientId = _configuration["YouTube:ClientId"]
            ?? throw new InvalidOperationException("YouTube:ClientId is not configured");
        var clientSecret = _configuration["YouTube:ClientSecret"]
            ?? throw new InvalidOperationException("YouTube:ClientSecret is not configured");

        using var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = clientId,
                ClientSecret = clientSecret
            }
        });

        var tokenResponse = await flow.ExchangeCodeForTokenAsync(
            userId.ToString(),
            authorizationCode,
            redirectUri,
            CancellationToken.None);

        // Get user info from Google
        var credential = new UserCredential(flow, userId.ToString(), tokenResponse);
        using var youtubeService = new Google.Apis.YouTube.v3.YouTubeService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Cantaro"
        });

        // Get the authenticated user's channel
        var channelRequest = youtubeService.Channels.List("snippet");
        channelRequest.Mine = true;
        var channelResponse = await channelRequest.ExecuteAsync();
        var channel = channelResponse.Items?.FirstOrDefault();

        var displayName = channel?.Snippet?.Title ?? "Unknown";

        // Check if account already exists
        var existingAccount = await _dbContext.ConnectedServiceAccounts
            .FirstOrDefaultAsync(a => a.UserId == userId && a.Service == ServiceName);

        if (existingAccount != null)
        {
            // Update external account ID only if we got a real channel ID from the API
            if (channel?.Id != null)
            {
                existingAccount.ExternalAccountId = channel.Id;
            }
            // Keep existing ExternalAccountId if we didn't get a channel ID

            existingAccount.DisplayName = displayName;
            // Only update refresh token if we received a new one
            if (tokenResponse.RefreshToken != null)
            {
                existingAccount.EncryptedRefreshToken = _tokenEncryption.Encrypt(tokenResponse.RefreshToken);
                _logger.LogInformation("Updated refresh token for YouTube account of user {UserId}", userId);
            }
            else
            {
                _logger.LogDebug("No new refresh token received for user {UserId}, keeping existing token", userId);
            }
            existingAccount.Scopes = tokenResponse.Scope;
            existingAccount.TokenExpiresAt = tokenResponse.IssuedUtc.AddSeconds(tokenResponse.ExpiresInSeconds ?? 3600);
            existingAccount.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            // For new accounts, refresh token is required for background sync
            if (tokenResponse.RefreshToken == null)
            {
                throw new InvalidOperationException("No refresh token received from Google. User may need to revoke access at https://myaccount.google.com/permissions and reconnect.");
            }

            // Use channel ID if available, otherwise generate a unique fallback ID for new accounts
            var externalAccountId = channel?.Id ?? $"yt_user_{Guid.NewGuid():N}";

            existingAccount = new ConnectedServiceAccount
            {
                UserId = userId,
                Service = ServiceName,
                ExternalAccountId = externalAccountId,
                DisplayName = displayName,
                EncryptedRefreshToken = _tokenEncryption.Encrypt(tokenResponse.RefreshToken),
                Scopes = tokenResponse.Scope,
                TokenExpiresAt = tokenResponse.IssuedUtc.AddSeconds(tokenResponse.ExpiresInSeconds ?? 3600),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _dbContext.ConnectedServiceAccounts.Add(existingAccount);
        }

        await _dbContext.SaveChangesAsync();
        return existingAccount;
    }

    /// <summary>
    /// Gets the connected YouTube account for a user, or null if not connected
    /// </summary>
    public async Task<ConnectedServiceAccount?> GetConnectedAccountAsync(int userId)
    {
        return await _dbContext.ConnectedServiceAccounts
            .FirstOrDefaultAsync(a => a.UserId == userId && a.Service == ServiceName);
    }

    /// <summary>
    /// Disconnects a YouTube account for a user
    /// </summary>
    public async Task DisconnectAsync(int userId)
    {
        var account = await _dbContext.ConnectedServiceAccounts
            .FirstOrDefaultAsync(a => a.UserId == userId && a.Service == ServiceName);

        if (account != null)
        {
            // Revoke the token with Google before deleting locally
            if (!string.IsNullOrEmpty(account.EncryptedRefreshToken))
            {
                try
                {
                    var refreshToken = _tokenEncryption.Decrypt(account.EncryptedRefreshToken);
                    using var httpClient = new HttpClient();
                    await httpClient.PostAsync($"https://oauth2.googleapis.com/revoke?token={Uri.EscapeDataString(refreshToken)}", null);
                    _logger.LogInformation("Revoked YouTube token for user {UserId}", userId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to revoke YouTube token for user {UserId}", userId);
                    // Continue with local deletion even if revocation fails
                }
            }

            _dbContext.ConnectedServiceAccounts.Remove(account);
            await _dbContext.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Gets the user's YouTube playlists
    /// </summary>
    public async Task<List<YouTubePlaylistDto>> GetPlaylistsAsync(int userId)
    {
        var account = await GetConnectedAccountAsync(userId)
            ?? throw new InvalidOperationException("YouTube account not connected");

        using var youtubeService = await CreateYouTubeServiceAsync(account);
        var playlists = new List<YouTubePlaylistDto>();
        string? nextPageToken = null;
        const int maxPages = 20; // Limit to ~1000 playlists to prevent runaway requests
        int pageCount = 0;

        do
        {
            if (++pageCount > maxPages)
            {
                _logger.LogWarning("Hit max page limit ({MaxPages}) fetching playlists for user {UserId}", maxPages, userId);
                break;
            }

            var request = youtubeService.Playlists.List("snippet,contentDetails");
            request.Mine = true;
            request.MaxResults = 50;
            request.PageToken = nextPageToken;

            var response = await request.ExecuteAsync();

            if (response.Items != null)
            {
                foreach (var playlist in response.Items)
                {
                    if (playlist == null)
                    {
                        _logger.LogWarning("Encountered null playlist item for user {UserId}", userId);
                        continue;
                    }

                    if (playlist.Snippet == null)
                    {
                        _logger.LogWarning("Encountered playlist with null snippet for user {UserId}, playlist ID {PlaylistId}", userId, playlist.Id);
                        continue;
                    }

                    playlists.Add(new YouTubePlaylistDto
                    {
                        Id = playlist.Id,
                        Title = playlist.Snippet.Title,
                        Description = playlist.Snippet.Description,
                        ThumbnailUrl = playlist.Snippet.Thumbnails?.Medium?.Url
                            ?? playlist.Snippet.Thumbnails?.Default__?.Url,
                        ItemCount = (int)(playlist.ContentDetails?.ItemCount ?? 0),
                        PublishedAt = ParsePublishedAtRaw(playlist.Snippet?.PublishedAtRaw)
                    });
                }
            }

            nextPageToken = response.NextPageToken;
        } while (!string.IsNullOrEmpty(nextPageToken));

        return playlists;
    }

    /// <summary>
    /// Gets items in a YouTube playlist
    /// </summary>
    public async Task<List<YouTubePlaylistItemDto>> GetPlaylistItemsAsync(int userId, string playlistId)
    {
        var account = await GetConnectedAccountAsync(userId)
            ?? throw new InvalidOperationException("YouTube account not connected");

        using var youtubeService = await CreateYouTubeServiceAsync(account);
        var items = new List<YouTubePlaylistItemDto>();
        string? nextPageToken = null;
        const int maxPages = 40; // Limit to ~2000 items to prevent runaway requests
        int pageCount = 0;

        do
        {
            if (++pageCount > maxPages)
            {
                _logger.LogWarning("Hit max page limit ({MaxPages}) fetching playlist items for user {UserId}, playlist {PlaylistId}", maxPages, userId, playlistId);
                break;
            }

            var request = youtubeService.PlaylistItems.List("snippet,contentDetails,status");
            request.PlaylistId = playlistId;
            request.MaxResults = 50;
            request.PageToken = nextPageToken;

            var response = await request.ExecuteAsync();

            if (response.Items != null)
            {
                var videoIds = response.Items
                    .Select(item => item?.ContentDetails?.VideoId)
                    .Where(videoId => !string.IsNullOrWhiteSpace(videoId))
                    .Cast<string>()
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                var availableVideos = await GetAvailableVideosAsync(youtubeService, videoIds);

                foreach (var item in response.Items)
                {
                    var mapping = MapAvailablePlaylistItem(item, availableVideos);
                    if (mapping.Item == null)
                    {
                        _logger.LogInformation(
                            "Skipping YouTube playlist item at position {Position} in playlist {PlaylistId}: {Reason}",
                            mapping.ProviderPosition,
                            playlistId,
                            mapping.SkipReason);
                        continue;
                    }

                    items.Add(mapping.Item);
                }
            }

            nextPageToken = response.NextPageToken;
        } while (!string.IsNullOrEmpty(nextPageToken));

        return CompactPlaylistPositions(items);
    }

    internal static List<YouTubePlaylistItemDto> CompactPlaylistPositions(
        IEnumerable<YouTubePlaylistItemDto> items)
    {
        var orderedItems = items.OrderBy(item => item.Position).ToList();
        for (var index = 0; index < orderedItems.Count; index++)
        {
            orderedItems[index].Position = index;
        }

        return orderedItems;
    }

    internal static YouTubePlaylistItemMappingResult MapAvailablePlaylistItem(
        PlaylistItem? item,
        IReadOnlyDictionary<string, int?> availableVideos)
    {
        if (item?.Snippet == null)
        {
            return new(null, item == null ? "missing_item" : "missing_snippet", null);
        }

        var position = item.Snippet.Position;
        if (string.IsNullOrWhiteSpace(item.ContentDetails?.VideoId))
        {
            return new(null, "missing_video_id", position);
        }

        if (string.Equals(item.Status?.PrivacyStatus, "private", StringComparison.OrdinalIgnoreCase))
        {
            return new(null, "private", position);
        }

        var videoId = item.ContentDetails.VideoId;
        if (!availableVideos.TryGetValue(videoId, out var durationSeconds))
        {
            return new(null, "provider_unavailable", position);
        }

        return new(
            new YouTubePlaylistItemDto
            {
                VideoId = videoId,
                Title = item.Snippet.Title,
                Description = item.Snippet.Description,
                ThumbnailUrl = item.Snippet.Thumbnails?.Medium?.Url
                    ?? item.Snippet.Thumbnails?.Default__?.Url,
                ChannelTitle = item.Snippet.VideoOwnerChannelTitle,
                Position = (int)(position ?? 0),
                PublishedAt = ParsePublishedAtRaw(item.Snippet.PublishedAtRaw),
                DurationSeconds = durationSeconds
            },
            null,
            position);
    }

    private async Task<Dictionary<string, int?>> GetAvailableVideosAsync(
        Google.Apis.YouTube.v3.YouTubeService youtubeService,
        IReadOnlyCollection<string> videoIds)
    {
        if (videoIds.Count == 0)
        {
            return [];
        }

        var request = youtubeService.Videos.List("contentDetails,status");
        request.Id = string.Join(",", videoIds);
        request.MaxResults = videoIds.Count;

        var response = await request.ExecuteAsync();
        var availableVideos = new Dictionary<string, int?>(StringComparer.Ordinal);

        if (response.Items == null)
        {
            return availableVideos;
        }

        foreach (var video in response.Items)
        {
            if (string.IsNullOrWhiteSpace(video.Id)
                || string.Equals(video.Status?.PrivacyStatus, "private", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int? durationSeconds = null;
            if (!string.IsNullOrWhiteSpace(video.ContentDetails?.Duration))
            {
                try
                {
                    var duration = XmlConvert.ToTimeSpan(video.ContentDetails.Duration);
                    durationSeconds = (int)Math.Round(duration.TotalSeconds, MidpointRounding.AwayFromZero);
                }
                catch (FormatException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse YouTube duration '{Duration}' for video {VideoId}", video.ContentDetails.Duration, video.Id);
                }
            }

            availableVideos[video.Id] = durationSeconds;
        }

        return availableVideos;
    }

    private async Task<Google.Apis.YouTube.v3.YouTubeService> CreateYouTubeServiceAsync(ConnectedServiceAccount account)
    {
        if (string.IsNullOrEmpty(account.EncryptedRefreshToken))
            throw new InvalidOperationException("No refresh token available");

        var clientId = _configuration["YouTube:ClientId"]
            ?? throw new InvalidOperationException("YouTube:ClientId is not configured");
        var clientSecret = _configuration["YouTube:ClientSecret"]
            ?? throw new InvalidOperationException("YouTube:ClientSecret is not configured");

        var refreshToken = _tokenEncryption.Decrypt(account.EncryptedRefreshToken);

        using var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = clientId,
                ClientSecret = clientSecret
            }
        });

        var token = new TokenResponse
        {
            RefreshToken = refreshToken
        };

        var credential = new UserCredential(flow, account.UserId.ToString(), token);

        // Refresh the access token
        if (await credential.RefreshTokenAsync(CancellationToken.None))
        {
            _logger.LogDebug("Successfully refreshed YouTube access token for user {UserId}", account.UserId);
        }

        return new Google.Apis.YouTube.v3.YouTubeService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Cantaro"
        });
    }
}
