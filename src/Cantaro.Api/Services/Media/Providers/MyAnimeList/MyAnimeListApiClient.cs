using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cantaro.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Cantaro.Api.Services;

public sealed class MyAnimeListApiClient(
    HttpClient httpClient,
    IOptions<MyAnimeListOptions> options,
    ILogger<MyAnimeListApiClient> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient = httpClient;
    private readonly MyAnimeListOptions _options = options.Value;
    private readonly ILogger<MyAnimeListApiClient> _logger = logger;

    public string BuildAuthorizationUrl(string redirectUri, string state, string codeChallenge)
    {
        EnsureClientIdConfigured();

        return BuildUrl(
            _options.AuthorizationUrl,
            new Dictionary<string, string?>
            {
                ["response_type"] = "code",
                ["client_id"] = _options.ClientId,
                ["state"] = state,
                ["redirect_uri"] = redirectUri,
                ["code_challenge"] = codeChallenge,
                ["code_challenge_method"] = "plain"
            });
    }

    public Task<MyAnimeListTokenResponse> ExchangeCodeAsync(
        string authorizationCode,
        string redirectUri,
        string codeVerifier,
        CancellationToken cancellationToken)
    {
        return SendTokenRequestAsync(
            new Dictionary<string, string?>
            {
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["grant_type"] = "authorization_code",
                ["code"] = authorizationCode,
                ["redirect_uri"] = redirectUri,
                ["code_verifier"] = codeVerifier
            },
            cancellationToken);
    }

    public Task<MyAnimeListTokenResponse> RefreshAccessTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        return SendTokenRequestAsync(
            new Dictionary<string, string?>
            {
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken
            },
            cancellationToken);
    }

    public Task<T> GetAsync<T>(
        string pathOrUrl,
        string? accessToken,
        IReadOnlyDictionary<string, string?>? query,
        CancellationToken cancellationToken)
    {
        var url = query is null ? pathOrUrl : BuildUrl(pathOrUrl, query);
        return SendAsync<T>(HttpMethod.Get, url, accessToken, form: null, cancellationToken);
    }

    public async Task<T> PutFormAsync<T>(
        string path,
        string accessToken,
        IReadOnlyDictionary<string, string?> form,
        CancellationToken cancellationToken)
    {
        try
        {
            return await SendAsync<T>(HttpMethod.Put, path, accessToken, form, cancellationToken);
        }
        catch (MyAnimeListRequestException exception)
            when (exception.StatusCode == HttpStatusCode.MethodNotAllowed)
        {
            // MAL's official mutation example uses PUT while its ReDoc operation
            // model says PATCH. A 405 guarantees PUT was not applied, so this is
            // the only response for which replaying with PATCH is safe.
            return await SendAsync<T>(HttpMethod.Patch, path, accessToken, form, cancellationToken);
        }
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string pathOrUrl,
        string? accessToken,
        IReadOnlyDictionary<string, string?>? form,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, ResolveApiUri(pathOrUrl));
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            EnsureClientIdConfigured();
            request.Headers.TryAddWithoutValidation("X-MAL-CLIENT-ID", _options.ClientId);
        }
        else
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        if (form is not null)
        {
            request.Content = new FormUrlEncodedContent(
                form.Where(pair => pair.Value is not null)
                    .Select(pair => new KeyValuePair<string, string>(pair.Key, pair.Value!)));
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "MyAnimeList request {Method} {Path} failed with status {StatusCode}.",
                method,
                request.RequestUri?.AbsolutePath,
                response.StatusCode);
            throw new MyAnimeListRequestException(response.StatusCode, ReadRetryAfter(response));
        }

        return JsonSerializer.Deserialize<T>(responseBody, SerializerOptions)
            ?? throw new InvalidOperationException("MyAnimeList returned an unreadable response.");
    }

    private async Task<MyAnimeListTokenResponse> SendTokenRequestAsync(
        IReadOnlyDictionary<string, string?> payload,
        CancellationToken cancellationToken)
    {
        EnsureClientIdConfigured();
        using var content = new FormUrlEncodedContent(
            payload.Where(pair => pair.Value is not null)
                .Select(pair => new KeyValuePair<string, string>(pair.Key, pair.Value!)));
        using var response = await _httpClient.PostAsync(_options.TokenUrl, content, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("MyAnimeList token request failed with status {StatusCode}.", response.StatusCode);
            throw new MyAnimeListRequestException(response.StatusCode, ReadRetryAfter(response));
        }

        return JsonSerializer.Deserialize<MyAnimeListTokenResponse>(responseBody, SerializerOptions)
            ?? throw new InvalidOperationException("MyAnimeList returned an unreadable token response.");
    }

    private Uri ResolveApiUri(string pathOrUrl)
    {
        var apiBaseUri = new Uri(_options.ApiBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var absolute))
        {
            if (Uri.Compare(
                    apiBaseUri,
                    absolute,
                    UriComponents.SchemeAndServer,
                    UriFormat.Unescaped,
                    StringComparison.OrdinalIgnoreCase) != 0)
            {
                throw new InvalidOperationException(
                    "MyAnimeList paging URL did not use the configured API origin.");
            }

            return absolute;
        }

        return new Uri(apiBaseUri, pathOrUrl.TrimStart('/'));
    }

    private static string BuildUrl(string pathOrUrl, IReadOnlyDictionary<string, string?> query)
    {
        var separator = pathOrUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        var queryString = string.Join(
            "&",
            query.Where(pair => pair.Value is not null)
                .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}"));
        return string.IsNullOrEmpty(queryString) ? pathOrUrl : $"{pathOrUrl}{separator}{queryString}";
    }

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            return delta;
        }

        if (response.Headers.RetryAfter?.Date is { } retryAt)
        {
            var delay = retryAt - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }

        return null;
    }

    private void EnsureClientIdConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId))
        {
            throw new InvalidOperationException(
                "MyAnimeList OAuth is not configured. Set MyAnimeList:ClientId before connecting a provider.");
        }
    }
}

public sealed class MyAnimeListRequestException(
    HttpStatusCode statusCode,
    TimeSpan? retryAfter) : InvalidOperationException(
        $"MyAnimeList request failed with status {(int)statusCode}.")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

public sealed class MyAnimeListTokenResponse
{
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}
