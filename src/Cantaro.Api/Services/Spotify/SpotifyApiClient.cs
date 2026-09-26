using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Cantaro.Api.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;

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
    private readonly SpotifyCooldown _cooldown;
    private readonly ILogger<SpotifyApiClient> _logger;

    internal SpotifyApiClient(
        HttpClient httpClient,
        IOptions<SpotifyOptions> options,
        ISpotifyRetryDelay retryDelay)
        : this(httpClient, options, retryDelay, new SpotifyCooldown(TimeProvider.System))
    {
    }

    public SpotifyApiClient(
        HttpClient httpClient,
        IOptions<SpotifyOptions> options,
        ISpotifyRetryDelay retryDelay,
        SpotifyCooldown cooldown,
        ILogger<SpotifyApiClient>? logger = null)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _retryDelay = retryDelay;
        _cooldown = cooldown;
        _logger = logger ?? NullLogger<SpotifyApiClient>.Instance;
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

    internal async Task<SpotifyCreatedPlaylist> CreateUnlistedPlaylistAsync(
        string accessToken, string name, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/me/playlists");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { name, @public = false }),
            Encoding.UTF8,
            "application/json");

        // The server may create the playlist before a response is lost. Do not retry
        // this POST; the caller reports an unknown outcome instead.
        await _cooldown.AcquireAsync(cancellationToken);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await CreateApiExceptionAsync(response, cancellationToken);

        await _cooldown.RecordSuccessAsync(cancellationToken);
        var created = await DeserializeAsync<SpotifyPlaylistResponse>(response, cancellationToken);
        return created is null
            ? throw new PlatformApiException("spotify_playlist_creation_unknown",
                "Spotify may have created the playlist, but Cantaro did not receive its details. Check Spotify before trying again.",
                StatusCodes.Status409Conflict)
            : new SpotifyCreatedPlaylist(created.Id, created.Owner?.Id, created.IsPublic);
    }

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
                    item.Images?.FirstOrDefault()?.Url,
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

    internal async Task<SpotifyPlaylistWriteSnapshot> GetPlaylistWriteSnapshotAsync(
        string accessToken,
        string playlistId,
        CancellationToken cancellationToken)
    {
        var playlist = await GetAsync<SpotifyPlaylistResponse>(
            $"/v1/playlists/{Uri.EscapeDataString(playlistId)}",
            accessToken,
            cancellationToken);
        if (!string.Equals(playlist.Id, playlistId, StringComparison.Ordinal))
        {
            throw new PlatformApiException(
                "spotify_playlist_invalid_response",
                "Spotify returned the wrong playlist.",
                StatusCodes.Status502BadGateway);
        }

        var trackIds = new List<string>();
        var offset = 0;
        while (true)
        {
            var page = await GetAsync<SpotifyPlaylistWritePage>(
                $"/v1/playlists/{Uri.EscapeDataString(playlistId)}/items?limit={PageSize}&offset={offset}&additional_types=episode",
                accessToken,
                cancellationToken);
            if (page.Items is null || page.Total is null || page.Total < 0
                || (page.Items.Count == 0 && !string.IsNullOrWhiteSpace(page.Next)))
            {
                throw new PlatformApiException(
                    "spotify_playlist_invalid_response",
                    "Spotify returned an invalid playlist page. Try syncing again.",
                    StatusCodes.Status502BadGateway);
            }
            foreach (var envelope in page.Items)
            {
                // Keep every provider position. Items Cantaro cannot represent must
                // still cause a mismatch so the replacement removes them.
                trackIds.Add(envelope is { IsLocal: false, Item: { Type: "track", Id: { Length: > 0 } id } }
                    ? id
                    : $"!unsupported:{trackIds.Count}");
            }

            if (string.IsNullOrWhiteSpace(page.Next))
            {
                if (trackIds.Count != page.Total)
                {
                    throw new PlatformApiException(
                        "spotify_playlist_incomplete_response",
                        "Spotify returned an incomplete playlist. Try syncing again.",
                        StatusCodes.Status502BadGateway);
                }

                return new SpotifyPlaylistWriteSnapshot(
                    playlistId,
                    playlist.Owner?.Id,
                    playlist.IsPublic,
                    playlist.IsCollaborative,
                    trackIds);
            }

            offset += PageSize;
        }
    }

    internal async Task<SpotifySyncReadSnapshot> GetPlaylistSyncReadSnapshotAsync(
        string accessToken, string playlistId, CancellationToken cancellationToken)
    {
        var playlist = await GetAsync<SpotifyPlaylistResponse>(
            $"/v1/playlists/{Uri.EscapeDataString(playlistId)}", accessToken, cancellationToken);
        if (playlist.Id != playlistId || string.IsNullOrWhiteSpace(playlist.Name))
            throw new PlatformApiException("spotify_playlist_invalid_response",
                "Spotify returned incomplete playlist details.", StatusCodes.Status502BadGateway);

        var items = new List<SpotifySyncReadItem>();
        var incomplete = false;
        var offset = 0;
        var pageCount = 0;
        while (true)
        {
            if (++pageCount > 120)
                throw new PlatformApiException("spotify_playlist_incomplete_response",
                    "Spotify playlist has too many pages to read completely.", StatusCodes.Status502BadGateway);
            var page = await GetAsync<SpotifyPlaylistWritePage>(
                $"/v1/playlists/{Uri.EscapeDataString(playlistId)}/items?limit={PageSize}&offset={offset}&additional_types=episode",
                accessToken, cancellationToken);
            if (page.Items is null || page.Total is null || page.Total < 0
                || (page.Items.Count == 0 && !string.IsNullOrWhiteSpace(page.Next)))
                throw new PlatformApiException("spotify_playlist_incomplete_response",
                    "Spotify returned an incomplete playlist page.", StatusCodes.Status502BadGateway);

            foreach (var envelope in page.Items)
            {
                var position = items.Count;
                var track = envelope is { IsLocal: false, Item: { Type: "track", Id: { Length: > 0 }, Name: { Length: > 0 } } item }
                    ? ToTrackSnapshot(item, envelope.AddedAt, position)
                    : null;
                if (track is null) incomplete = true;
                items.Add(new SpotifySyncReadItem(position, track));
            }

            if (string.IsNullOrWhiteSpace(page.Next))
            {
                if (items.Count != page.Total)
                    throw new PlatformApiException("spotify_playlist_incomplete_response",
                        "Spotify returned fewer playlist items than reported.", StatusCodes.Status502BadGateway);
                var after = await GetAsync<SpotifyPlaylistResponse>(
                    $"/v1/playlists/{Uri.EscapeDataString(playlistId)}", accessToken, cancellationToken);
                var stable = after.Id == playlist.Id && after.Name == playlist.Name
                    && !string.IsNullOrWhiteSpace(playlist.SnapshotId)
                    && after.SnapshotId == playlist.SnapshotId;
                return new SpotifySyncReadSnapshot(playlistId, playlist.Name, playlist.SnapshotId,
                    playlist.Owner?.Id, playlist.IsPublic, playlist.IsCollaborative, items, !incomplete && stable);
            }
            offset += PageSize;
        }
    }

    internal Task ReplacePlaylistItemsAsync(
        string accessToken,
        string playlistId,
        IReadOnlyList<string> trackIds,
        CancellationToken cancellationToken)
        => SendPlaylistMutationAsync(HttpMethod.Put, accessToken, playlistId, trackIds, cancellationToken);

    internal Task AddPlaylistItemsAsync(
        string accessToken,
        string playlistId,
        IReadOnlyList<string> trackIds,
        CancellationToken cancellationToken)
        => SendPlaylistMutationAsync(HttpMethod.Post, accessToken, playlistId, trackIds, cancellationToken);

    internal async Task RenamePlaylistAsync(
        string accessToken, string playlistId, string name, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put,
            $"/v1/playlists/{Uri.EscapeDataString(playlistId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new StringContent(JsonSerializer.Serialize(new { name }), Encoding.UTF8, "application/json");
        await _cooldown.AcquireAsync(cancellationToken);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await CreateApiExceptionAsync(response, cancellationToken);
        await _cooldown.RecordSuccessAsync(cancellationToken);
    }

    private async Task SendPlaylistMutationAsync(
        HttpMethod method,
        string accessToken,
        string playlistId,
        IReadOnlyList<string> trackIds,
        CancellationToken cancellationToken)
    {
        if (trackIds.Count > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(trackIds), "Spotify accepts at most 100 items per write.");
        }

        using var request = new HttpRequestMessage(
            method,
            $"/v1/playlists/{Uri.EscapeDataString(playlistId)}/items");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { uris = trackIds.Select(id => $"spotify:track:{id}").ToArray() }),
            Encoding.UTF8,
            "application/json");
        // Mutations are not retried here. A job retry starts with a fresh playlist read.
        await _cooldown.AcquireAsync(cancellationToken);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateApiExceptionAsync(response, cancellationToken);
        }
        await _cooldown.RecordSuccessAsync(cancellationToken);
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
        await _cooldown.AcquireAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw await CreateApiExceptionAsync(response, cancellationToken);
        }

        await _cooldown.RecordSuccessAsync(cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
            ?? throw new PlatformApiException(
                "spotify_invalid_response",
                "Spotify returned an empty or malformed response.",
                StatusCodes.Status502BadGateway);
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

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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

    private async Task<PlatformApiException> CreateApiExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        TimeSpan? retryAfter = null;
        SpotifyErrorEnvelope? payload = null;
        try
        {
            payload = await DeserializeAsync<SpotifyErrorEnvelope>(response, cancellationToken);
        }
        catch (Exception ex) when (response.StatusCode == HttpStatusCode.TooManyRequests
            && ex is OperationCanceledException or HttpRequestException or IOException)
        {
            // The headers already establish a definite rejection. Losing the
            // optional error body must not turn it into an unknown write outcome.
        }
        finally
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                // A caller cancelling its request must not erase a limit already
                // received from Spotify. Bound the independent persistence work.
                using var persistenceTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var limit = await _cooldown.RecordAsync(response.Headers.RetryAfter,
                    payload?.Error?.Reason, persistenceTimeout.Token);
                retryAfter = limit.RetryAfter;
                _logger.LogWarning(
                    "Spotify Web API returned 429: Reason={Reason}, RetryAfterHeader={RetryAfterHeader}, RetryAt={RetryAt}, EffectiveDelaySeconds={EffectiveDelaySeconds}, FallbackCount={FallbackCount}, Persisted={Persisted}",
                    payload?.Error?.Reason ?? "unspecified",
                    response.Headers.RetryAfter?.ToString() ?? "missing or invalid",
                    limit.NotBefore, limit.RetryAfter.TotalSeconds, limit.FallbackCount, limit.Persisted);
                if (!limit.Persisted)
                    _logger.LogError("Spotify cooldown persistence failed. This API instance will still block requests until {RetryAt}, but other instances may not know this deadline.", limit.NotBefore);
            }
        }
        var upstream = payload?.Error;
        var statusCode = (int)response.StatusCode;
        var code = upstream?.Reason switch
        {
            "QUOTA_EXCEEDED" => "spotify_quota_exceeded",
            _ when response.StatusCode == HttpStatusCode.TooManyRequests => "spotify_rate_limited",
            _ when response.StatusCode == HttpStatusCode.Unauthorized => "spotify_unauthorized",
            _ when response.StatusCode == HttpStatusCode.Forbidden => "spotify_forbidden",
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

        if (response.Headers.RetryAfter?.Date is { } retryAt)
        {
            var dateDelay = retryAt - DateTimeOffset.UtcNow;
            return dateDelay > exponential ? dateDelay : exponential;
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
