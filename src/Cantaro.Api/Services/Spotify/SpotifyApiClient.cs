using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Cantaro.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Cantaro.Api.Services.Spotify;

public interface ISpotifyRetryDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class SpotifyRetryDelay : ISpotifyRetryDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        => Task.Delay(delay, cancellationToken);
}

public sealed class SpotifyApiClient
{
    private const int PageSize = 50;
    private const int MaxRateLimitRetries = 3;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly SpotifyOptions _options;
    private readonly ISpotifyRetryDelay _retryDelay;

    public SpotifyApiClient(
        HttpClient httpClient,
        IOptions<SpotifyOptions> options,
        ISpotifyRetryDelay retryDelay)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _retryDelay = retryDelay;
    }

    public Task<SpotifyTokenResponse> ExchangeCodeAsync(
        string code,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        return SendTokenRequestAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri
            },
            cancellationToken);
    }

    public Task<SpotifyTokenResponse> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        return SendTokenRequestAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken
            },
            cancellationToken);
    }

    public Task<SpotifyTokenResponse> GetClientCredentialsTokenAsync(CancellationToken cancellationToken)
    {
        return SendTokenRequestAsync(
            new Dictionary<string, string> { ["grant_type"] = "client_credentials" },
            cancellationToken);
    }

    public Task<SpotifyProfileResponse> GetProfileAsync(string accessToken, CancellationToken cancellationToken)
        => GetAsync<SpotifyProfileResponse>("/v1/me", accessToken, cancellationToken);

    public async Task<IReadOnlyList<SpotifyTrackSnapshot>> SearchTracksAsync(
        string accessToken,
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        var boundedLimit = Math.Clamp(limit, 1, 50);
        var response = await GetAsync<SpotifySearchResponse>(
            $"/v1/search?type=track&limit={boundedLimit}&q={Uri.EscapeDataString(query)}",
            accessToken,
            cancellationToken);

        return response.Tracks?.Items
            .Select((item, index) => ToTrackSnapshot(item, addedAt: null, index))
            .Where(item => item != null)
            .Select(item => item!)
            .ToArray() ?? [];
    }

    public async Task<IReadOnlyList<SpotifyPlaylistSnapshot>> GetPlaylistsAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        var playlists = new List<SpotifyPlaylistSnapshot>();
        var offset = 0;

        while (true)
        {
            var page = await GetAsync<SpotifyPage<SpotifyPlaylistResponse>>(
                $"/v1/me/playlists?limit={PageSize}&offset={offset}",
                accessToken,
                cancellationToken);

            foreach (var item in page.Items)
            {
                if (item?.Id is not { Length: > 0 } id || item.Name is not { Length: > 0 } name)
                {
                    continue;
                }

                playlists.Add(new SpotifyPlaylistSnapshot(
                    id,
                    name,
                    item.Description,
                    item.Owner?.DisplayName,
                    item.Images.FirstOrDefault()?.Url,
                    item.ExternalUrls?.Spotify ?? $"https://open.spotify.com/playlist/{id}",
                    item.SnapshotId,
                    item.ItemsReference?.Total ?? 0));
            }

            if (string.IsNullOrWhiteSpace(page.Next))
            {
                return playlists;
            }

            offset += PageSize;
        }
    }

    public async Task<IReadOnlyList<SpotifyTrackSnapshot>> GetPlaylistItemsAsync(
        string accessToken,
        string playlistId,
        CancellationToken cancellationToken)
    {
        var tracks = new List<SpotifyTrackSnapshot>();
        var offset = 0;

        while (true)
        {
            var page = await GetAsync<SpotifyPage<SpotifyPlaylistItemResponse>>(
                $"/v1/playlists/{Uri.EscapeDataString(playlistId)}/items?limit={PageSize}&offset={offset}",
                accessToken,
                cancellationToken);

            foreach (var envelope in page.Items)
            {
                var item = envelope?.Item;
                if (envelope is null
                    || envelope.IsLocal
                    || item?.Type is not "track"
                    || item.Id is not { Length: > 0 } id
                    || item.Name is not { Length: > 0 } name)
                {
                    continue;
                }

                tracks.Add(ToTrackSnapshot(item, envelope.AddedAt, tracks.Count)!);
            }

            if (string.IsNullOrWhiteSpace(page.Next))
            {
                return tracks;
            }

            offset += PageSize;
        }
    }

    private static SpotifyTrackSnapshot? ToTrackSnapshot(SpotifyItemResponse? item, DateTimeOffset? addedAt, int position)
    {
        if (item?.Type is not "track"
            || item.Id is not { Length: > 0 } id
            || item.Name is not { Length: > 0 } name)
        {
            return null;
        }

        var artistNames = item.Artists
            .Select(candidate => candidate.Name?.Trim())
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(candidate => candidate!)
            .ToArray();
        var artist = string.Join(", ", artistNames);
        return new SpotifyTrackSnapshot(
            id, name, string.IsNullOrWhiteSpace(artist) ? "Unknown artist" : artist, artistNames,
            item.Album?.Name, item.Album?.Images.FirstOrDefault()?.Url,
            item.ExternalUrls?.Spotify ?? $"https://open.spotify.com/track/{id}",
            item.Album?.ExternalUrls?.Spotify, item.ExternalIds?.Isrc,
            Math.Max(0, item.DurationMs / 1000), addedAt, position);
    }

    private async Task<T> GetAsync<T>(string requestUri, string accessToken, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxRateLimitRetries)
            {
                var retryAfter = GetRetryAfter(response, attempt);
                await _retryDelay.DelayAsync(retryAfter, cancellationToken);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw await CreateApiExceptionAsync(response, cancellationToken);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
                ?? throw new PlatformApiException(
                    "spotify_invalid_response",
                    "Spotify returned an empty or malformed response.",
                    StatusCodes.Status502BadGateway);
        }
    }

    private async Task<SpotifyTokenResponse> SendTokenRequestAsync(
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            throw new PlatformApiException(
                "spotify_not_configured",
                "Spotify OAuth is not configured.",
                StatusCodes.Status503ServiceUnavailable);
        }

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(values);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxRateLimitRetries)
            {
                await _retryDelay.DelayAsync(GetRetryAfter(response, attempt), cancellationToken);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                var payload = await DeserializeAsync<SpotifyTokenErrorResponse>(response, cancellationToken);
                var code = string.IsNullOrWhiteSpace(payload?.Error) ? "spotify_token_error" : payload.Error;
                var retryAfter = response.StatusCode == HttpStatusCode.TooManyRequests
                    ? GetRetryAfter(response, MaxRateLimitRetries)
                    : (TimeSpan?)null;
                throw new PlatformApiException(
                    code,
                    payload?.Description ?? "Spotify rejected the authorization token request.",
                    (int)response.StatusCode,
                    retryAfter);
            }

            var token = await DeserializeAsync<SpotifyTokenResponse>(response, cancellationToken);
            return token is { AccessToken.Length: > 0 }
                ? token
                : throw new PlatformApiException(
                    "spotify_invalid_token_response",
                    "Spotify returned an invalid token response.",
                    StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<PlatformApiException> CreateApiExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var payload = await DeserializeAsync<SpotifyErrorEnvelope>(response, cancellationToken);
        var upstream = payload?.Error;
        var statusCode = (int)response.StatusCode;
        TimeSpan? retryAfter = response.StatusCode == HttpStatusCode.TooManyRequests
            ? GetRetryAfter(response, MaxRateLimitRetries)
            : null;
        var code = upstream?.Reason switch
        {
            "QUOTA_EXCEEDED" => "spotify_quota_exceeded",
            _ when response.StatusCode == HttpStatusCode.Unauthorized => "spotify_unauthorized",
            _ when response.StatusCode == HttpStatusCode.Forbidden => "spotify_forbidden",
            _ when response.StatusCode == HttpStatusCode.TooManyRequests => "spotify_rate_limited",
            _ => "spotify_api_error"
        };

        return new PlatformApiException(
            code,
            upstream?.Message ?? $"Spotify request failed with HTTP {statusCode}.",
            statusCode,
            retryAfter);
    }

    private static TimeSpan GetRetryAfter(HttpResponseMessage response, int attempt)
    {
        var exponential = TimeSpan.FromSeconds(Math.Pow(2, attempt));
        var delta = response.Headers.RetryAfter?.Delta;
        if (delta is { } explicitDelta)
        {
            return explicitDelta > exponential ? explicitDelta : exponential;
        }

        if (response.Headers.TryGetValues("Retry-After", out var values)
            && int.TryParse(values.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            var parsed = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return parsed > exponential ? parsed : exponential;
        }

        return exponential;
    }

    private static async Task<T?> DeserializeAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
