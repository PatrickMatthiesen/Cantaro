using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

public sealed class SpotifyPlaylistDto
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public int ItemCount { get; set; }
}

public sealed class SpotifyPlaylistItemDto
{
    public required string TrackId { get; set; }
    public required string Title { get; set; }
    public string? ArtistName { get; set; }
    public string? AlbumName { get; set; }
    public string? ThumbnailUrl { get; set; }
    public int Position { get; set; }
    public DateTimeOffset? AddedAt { get; set; }
    public int? DurationSeconds { get; set; }
    public string? Isrc { get; set; }
}

public sealed class SpotifyService
{
    private const string ServiceName = "spotify";
    private const string AccountsBaseUrl = "https://accounts.spotify.com";
    private const string ApiBaseUrl = "https://api.spotify.com/v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IConfiguration _configuration;
    private readonly ApplicationDbContext _dbContext;
    private readonly TokenEncryptionService _tokenEncryption;
    private readonly HttpClient _httpClient;
    private readonly ILogger<SpotifyService> _logger;

    public SpotifyService(
        IConfiguration configuration,
        ApplicationDbContext dbContext,
        TokenEncryptionService tokenEncryption,
        HttpClient httpClient,
        ILogger<SpotifyService> logger)
    {
        _configuration = configuration;
        _dbContext = dbContext;
        _tokenEncryption = tokenEncryption;
        _httpClient = httpClient;
        _logger = logger;
    }

    public string GetAuthorizationUrl(string redirectUri, string state)
    {
        var clientId = GetClientId();
        var scopes = new[]
        {
            "playlist-read-private",
            "playlist-read-collaborative",
            "user-read-private",
            "user-read-email"
        };

        var query = new QueryStringBuilder()
            .Add("client_id", clientId)
            .Add("response_type", "code")
            .Add("redirect_uri", redirectUri)
            .Add("scope", string.Join(" ", scopes))
            .Add("state", state)
            .ToString();

        return $"{AccountsBaseUrl}/authorize?{query}";
    }

    public async Task<ConnectedServiceAccount> ExchangeCodeAndSaveAsync(
        int userId,
        string authorizationCode,
        string redirectUri)
    {
        var token = await RequestTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = authorizationCode,
            ["redirect_uri"] = redirectUri
        });

        if (string.IsNullOrWhiteSpace(token.AccessToken))
        {
            throw new InvalidOperationException("Spotify did not return an access token.");
        }

        var profile = await SendSpotifyApiRequestAsync<SpotifyProfileResponse>(
            $"{ApiBaseUrl}/me",
            token.AccessToken);

        if (string.IsNullOrWhiteSpace(profile.Id))
        {
            throw new InvalidOperationException("Spotify did not return a user profile id.");
        }

        var displayName = profile.DisplayName ?? profile.Email ?? profile.Id;
        var existingAccount = await _dbContext.ConnectedServiceAccounts
            .FirstOrDefaultAsync(a => a.UserId == userId && a.Service == ServiceName);

        if (existingAccount is not null)
        {
            existingAccount.ExternalAccountId = profile.Id;
            existingAccount.DisplayName = displayName;
            existingAccount.Scopes = token.Scope;
            existingAccount.TokenExpiresAt = DateTime.UtcNow.AddSeconds(token.ExpiresInSeconds);
            existingAccount.UpdatedAt = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(token.RefreshToken))
            {
                existingAccount.EncryptedRefreshToken = _tokenEncryption.Encrypt(token.RefreshToken);
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(token.RefreshToken))
            {
                throw new InvalidOperationException("No refresh token received from Spotify. Revoke Cantaro access in Spotify and reconnect.");
            }

            existingAccount = new ConnectedServiceAccount
            {
                UserId = userId,
                Service = ServiceName,
                ExternalAccountId = profile.Id,
                DisplayName = displayName,
                EncryptedRefreshToken = _tokenEncryption.Encrypt(token.RefreshToken),
                Scopes = token.Scope,
                TokenExpiresAt = DateTime.UtcNow.AddSeconds(token.ExpiresInSeconds),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _dbContext.ConnectedServiceAccounts.Add(existingAccount);
        }

        await _dbContext.SaveChangesAsync();
        return existingAccount;
    }

    public Task<ConnectedServiceAccount?> GetConnectedAccountAsync(int userId)
    {
        return _dbContext.ConnectedServiceAccounts
            .FirstOrDefaultAsync(a => a.UserId == userId && a.Service == ServiceName);
    }

    public async Task DisconnectAsync(int userId)
    {
        var account = await GetConnectedAccountAsync(userId);
        if (account is null)
        {
            return;
        }

        _dbContext.ConnectedServiceAccounts.Remove(account);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<SpotifyPlaylistDto>> GetPlaylistsAsync(int userId, CancellationToken cancellationToken = default)
    {
        var account = await GetConnectedAccountAsync(userId)
            ?? throw new InvalidOperationException("Spotify account not connected");

        var playlists = new List<SpotifyPlaylistDto>();
        var offset = 0;
        const int limit = 50;
        const int maxPages = 20;

        for (var page = 0; page < maxPages; page++)
        {
            var response = await SendSpotifyApiRequestAsync<SpotifyPlaylistPageResponse>(
                $"{ApiBaseUrl}/me/playlists?limit={limit}&offset={offset}",
                account,
                cancellationToken);

            foreach (var playlist in response.Items ?? [])
            {
                if (string.IsNullOrWhiteSpace(playlist.Id) || string.IsNullOrWhiteSpace(playlist.Name))
                {
                    continue;
                }

                playlists.Add(new SpotifyPlaylistDto
                {
                    Id = playlist.Id,
                    Title = playlist.Name,
                    Description = playlist.Description,
                    ThumbnailUrl = playlist.Images?.FirstOrDefault()?.Url,
                    ItemCount = Math.Max(0, playlist.Tracks?.Total ?? 0)
                });
            }

            if (string.IsNullOrWhiteSpace(response.Next))
            {
                break;
            }

            offset += limit;
        }

        return playlists;
    }

    public async Task<IReadOnlyList<SpotifyPlaylistItemDto>> GetPlaylistItemsAsync(
        int userId,
        string playlistId,
        CancellationToken cancellationToken = default)
    {
        var account = await GetConnectedAccountAsync(userId)
            ?? throw new InvalidOperationException("Spotify account not connected");

        var items = new List<SpotifyPlaylistItemDto>();
        var offset = 0;
        const int limit = 50;
        const int maxPages = 40;

        for (var page = 0; page < maxPages; page++)
        {
            var response = await SendSpotifyApiRequestAsync<SpotifyPlaylistItemsPageResponse>(
                $"{ApiBaseUrl}/playlists/{Uri.EscapeDataString(playlistId)}/tracks?limit={limit}&offset={offset}&additional_types=track",
                account,
                cancellationToken);

            var pageItemIndex = 0;
            foreach (var item in response.Items ?? [])
            {
                var track = item.Track;
                if (track is null || !string.Equals(track.Type, "track", StringComparison.OrdinalIgnoreCase))
                {
                    pageItemIndex++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(track.Id) || string.IsNullOrWhiteSpace(track.Name))
                {
                    pageItemIndex++;
                    continue;
                }

                items.Add(new SpotifyPlaylistItemDto
                {
                    TrackId = track.Id,
                    Title = track.Name,
                    ArtistName = JoinArtistNames(track.Artists),
                    AlbumName = track.Album?.Name,
                    ThumbnailUrl = track.Album?.Images?.FirstOrDefault()?.Url,
                    Position = offset + pageItemIndex,
                    AddedAt = item.AddedAt,
                    DurationSeconds = track.DurationMs is > 0
                        ? (int)Math.Round(track.DurationMs.Value / 1000.0, MidpointRounding.AwayFromZero)
                        : null,
                    Isrc = track.ExternalIds?.Isrc
                });
                pageItemIndex++;
            }

            if (string.IsNullOrWhiteSpace(response.Next))
            {
                break;
            }

            offset += limit;
        }

        return items;
    }

    private async Task<string> RefreshAccessTokenAsync(ConnectedServiceAccount account, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(account.EncryptedRefreshToken))
        {
            throw new InvalidOperationException("No Spotify refresh token available");
        }

        var refreshToken = _tokenEncryption.Decrypt(account.EncryptedRefreshToken);
        var token = await RequestTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken
        }, cancellationToken);

        if (string.IsNullOrWhiteSpace(token.AccessToken))
        {
            throw new InvalidOperationException("Spotify did not return an access token.");
        }

        account.TokenExpiresAt = DateTime.UtcNow.AddSeconds(token.ExpiresInSeconds);
        account.Scopes = token.Scope ?? account.Scopes;
        account.UpdatedAt = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            account.EncryptedRefreshToken = _tokenEncryption.Encrypt(token.RefreshToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return token.AccessToken;
    }

    private async Task<SpotifyTokenResponse> RequestTokenAsync(
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{AccountsBaseUrl}/api/token")
        {
            Content = new FormUrlEncodedContent(values)
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", BuildClientCredential());

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Spotify token request failed with {StatusCode}: {Body}", response.StatusCode, content);
            throw new InvalidOperationException("Spotify token request failed.");
        }

        return JsonSerializer.Deserialize<SpotifyTokenResponse>(content, JsonOptions)
            ?? throw new InvalidOperationException("Spotify token response could not be parsed.");
    }

    private async Task<T> SendSpotifyApiRequestAsync<T>(
        string url,
        ConnectedServiceAccount account,
        CancellationToken cancellationToken)
    {
        var accessToken = await RefreshAccessTokenAsync(account, cancellationToken);
        return await SendSpotifyApiRequestAsync<T>(url, accessToken, cancellationToken);
    }

    private async Task<T> SendSpotifyApiRequestAsync<T>(
        string url,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Spotify API request failed with {StatusCode}: {Body}", response.StatusCode, content);
            throw new InvalidOperationException("Spotify API request failed.");
        }

        return JsonSerializer.Deserialize<T>(content, JsonOptions)
            ?? throw new InvalidOperationException("Spotify API response could not be parsed.");
    }

    private string GetClientId()
    {
        return _configuration["Spotify:ClientId"]
            ?? throw new InvalidOperationException("Spotify:ClientId is not configured");
    }

    private string GetClientSecret()
    {
        return _configuration["Spotify:ClientSecret"]
            ?? throw new InvalidOperationException("Spotify:ClientSecret is not configured");
    }

    private string BuildClientCredential()
    {
        var bytes = Encoding.UTF8.GetBytes($"{GetClientId()}:{GetClientSecret()}");
        return Convert.ToBase64String(bytes);
    }

    private static string? JoinArtistNames(IReadOnlyList<SpotifyArtistResponse>? artists)
    {
        var names = artists?
            .Select(artist => artist.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

        return names is { Length: > 0 } ? string.Join(", ", names) : null;
    }

    private sealed class QueryStringBuilder
    {
        private readonly List<string> _parts = [];

        public QueryStringBuilder Add(string name, string value)
        {
            _parts.Add($"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}");
            return this;
        }

        public override string ToString() => string.Join("&", _parts);
    }

    private sealed class SpotifyTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresInSeconds { get; set; } = 3600;
    }

    private sealed class SpotifyProfileResponse
    {
        public string? Id { get; set; }

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }

        public string? Email { get; set; }
    }

    private sealed class SpotifyPlaylistPageResponse
    {
        public string? Next { get; set; }
        public List<SpotifyPlaylistResponse>? Items { get; set; }
    }

    private sealed class SpotifyPlaylistResponse
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public List<SpotifyImageResponse>? Images { get; set; }
        public SpotifyTracksSummaryResponse? Tracks { get; set; }
    }

    private sealed class SpotifyPlaylistItemsPageResponse
    {
        public string? Next { get; set; }
        public List<SpotifyPlaylistItemResponse>? Items { get; set; }
    }

    private sealed class SpotifyPlaylistItemResponse
    {
        [JsonPropertyName("added_at")]
        public DateTimeOffset? AddedAt { get; set; }

        public SpotifyTrackResponse? Track { get; set; }
    }

    private sealed class SpotifyTrackResponse
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Type { get; set; }
        public SpotifyAlbumResponse? Album { get; set; }
        public List<SpotifyArtistResponse>? Artists { get; set; }

        [JsonPropertyName("duration_ms")]
        public int? DurationMs { get; set; }

        [JsonPropertyName("external_ids")]
        public SpotifyExternalIdsResponse? ExternalIds { get; set; }
    }

    private sealed class SpotifyAlbumResponse
    {
        public string? Name { get; set; }
        public List<SpotifyImageResponse>? Images { get; set; }
    }

    private sealed class SpotifyArtistResponse
    {
        public string? Name { get; set; }
    }

    private sealed class SpotifyImageResponse
    {
        public string? Url { get; set; }
    }

    private sealed class SpotifyTracksSummaryResponse
    {
        public int Total { get; set; }
    }

    private sealed class SpotifyExternalIdsResponse
    {
        public string? Isrc { get; set; }
    }
}
