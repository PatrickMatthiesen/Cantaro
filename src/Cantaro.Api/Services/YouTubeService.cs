using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

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
    public DateTime? PublishedAt { get; set; }
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
    public DateTime? PublishedAt { get; set; }
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

    /// <summary>
    /// Gets the Google OAuth authorization URL for YouTube
    /// </summary>
    public string GetAuthorizationUrl(string redirectUri, string state)
    {
        var clientId = _configuration["YouTube:ClientId"]
            ?? throw new InvalidOperationException("YouTube:ClientId is not configured");

        var scopes = new[]
        {
            "https://www.googleapis.com/auth/youtube.readonly",
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

        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
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
        var youtubeService = new Google.Apis.YouTube.v3.YouTubeService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Cantaro"
        });

        // Get the authenticated user's channel
        var channelRequest = youtubeService.Channels.List("snippet");
        channelRequest.Mine = true;
        var channelResponse = await channelRequest.ExecuteAsync();
        var channel = channelResponse.Items?.FirstOrDefault();

        var externalAccountId = channel?.Id ?? $"google_{userId}";
        var displayName = channel?.Snippet?.Title ?? "Unknown";

        // Check if account already exists
        var existingAccount = await _dbContext.ConnectedServiceAccounts
            .FirstOrDefaultAsync(a => a.UserId == userId && a.Service == ServiceName);

        if (existingAccount != null)
        {
            existingAccount.ExternalAccountId = externalAccountId;
            existingAccount.DisplayName = displayName;
            existingAccount.EncryptedRefreshToken = tokenResponse.RefreshToken != null
                ? _tokenEncryption.Encrypt(tokenResponse.RefreshToken)
                : existingAccount.EncryptedRefreshToken;
            existingAccount.Scopes = tokenResponse.Scope;
            existingAccount.TokenExpiresAt = tokenResponse.IssuedUtc.AddSeconds(tokenResponse.ExpiresInSeconds ?? 3600);
            existingAccount.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            existingAccount = new ConnectedServiceAccount
            {
                UserId = userId,
                Service = ServiceName,
                ExternalAccountId = externalAccountId,
                DisplayName = displayName,
                EncryptedRefreshToken = tokenResponse.RefreshToken != null
                    ? _tokenEncryption.Encrypt(tokenResponse.RefreshToken)
                    : null,
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

        var youtubeService = await CreateYouTubeServiceAsync(account);
        var playlists = new List<YouTubePlaylistDto>();
        string? nextPageToken = null;

        do
        {
            var request = youtubeService.Playlists.List("snippet,contentDetails");
            request.Mine = true;
            request.MaxResults = 50;
            request.PageToken = nextPageToken;

            var response = await request.ExecuteAsync();

            if (response.Items != null)
            {
                foreach (var playlist in response.Items)
                {
                    playlists.Add(new YouTubePlaylistDto
                    {
                        Id = playlist.Id,
                        Title = playlist.Snippet.Title,
                        Description = playlist.Snippet.Description,
                        ThumbnailUrl = playlist.Snippet.Thumbnails?.Medium?.Url
                            ?? playlist.Snippet.Thumbnails?.Default__?.Url,
                        ItemCount = (int)(playlist.ContentDetails?.ItemCount ?? 0),
                        PublishedAt = playlist.Snippet.PublishedAtDateTimeOffset?.UtcDateTime
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

        var youtubeService = await CreateYouTubeServiceAsync(account);
        var items = new List<YouTubePlaylistItemDto>();
        string? nextPageToken = null;

        do
        {
            var request = youtubeService.PlaylistItems.List("snippet,contentDetails");
            request.PlaylistId = playlistId;
            request.MaxResults = 50;
            request.PageToken = nextPageToken;

            var response = await request.ExecuteAsync();

            if (response.Items != null)
            {
                foreach (var item in response.Items)
                {
                    items.Add(new YouTubePlaylistItemDto
                    {
                        VideoId = item.ContentDetails.VideoId,
                        Title = item.Snippet.Title,
                        Description = item.Snippet.Description,
                        ThumbnailUrl = item.Snippet.Thumbnails?.Medium?.Url
                            ?? item.Snippet.Thumbnails?.Default__?.Url,
                        ChannelTitle = item.Snippet.VideoOwnerChannelTitle,
                        Position = (int)(item.Snippet.Position ?? 0),
                        PublishedAt = item.Snippet.PublishedAtDateTimeOffset?.UtcDateTime
                    });
                }
            }

            nextPageToken = response.NextPageToken;
        } while (!string.IsNullOrEmpty(nextPageToken));

        return items;
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

        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
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
