using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cantaro.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Cantaro.Api.Services;

public sealed class SimklApiClient(HttpClient client, IOptions<SimklOptions> options)
{
    private readonly SimklOptions _options = options.Value;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string BuildAuthorizationUrl(string redirectUri, string state, string challenge)
    {
        EnsureConfigured();
        return AddQuery(_options.AuthorizationUrl, new Dictionary<string, string?>
        {
            ["client_id"] = _options.ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = "media:read media:write",
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256"
        });
    }

    public Task<SimklTokenResponse> ExchangeCodeAsync(string code, string redirectUri, string verifier, CancellationToken cancellationToken)
        => TokenAsync(new Dictionary<string, string?>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = verifier
        }, cancellationToken);

    public Task<SimklTokenResponse> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
        => TokenAsync(new Dictionary<string, string?>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken
        }, cancellationToken);

    public async Task RevokeAsync(string token, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var values = new Dictionary<string, string?>
        {
            ["token"] = token, ["client_id"] = _options.ClientId, ["client_secret"] = _options.ClientSecret
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(_options.ApiBaseUrl.TrimEnd('/') + "/"), "oauth2/revoke"))
        {
            Content = new FormUrlEncodedContent(values.Where(x => x.Value is not null)
                .Select(x => new KeyValuePair<string, string>(x.Key, x.Value!)))
        };
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new SimklRequestException(response.StatusCode, ReadRetryAfter(response));
    }

    public Task<JsonDocument> GetAsync(string path, string? accessToken, IReadOnlyDictionary<string, string?>? query, CancellationToken cancellationToken)
        => SendAsync(HttpMethod.Get, path, accessToken, query, null, cancellationToken);

    public Task<JsonDocument> PostAsync(string path, string accessToken, object body, CancellationToken cancellationToken)
        => SendAsync(HttpMethod.Post, path, accessToken, null, body, cancellationToken);

    private async Task<SimklTokenResponse> TokenAsync(Dictionary<string, string?> values, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        values["client_id"] = _options.ClientId;
        if (!string.IsNullOrWhiteSpace(_options.ClientSecret)) values["client_secret"] = _options.ClientSecret;
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.TokenUrl)
        {
            Content = new FormUrlEncodedContent(values.Where(x => x.Value is not null).Select(x => new KeyValuePair<string, string>(x.Key, x.Value!)))
        };
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new SimklRequestException(response.StatusCode, ReadRetryAfter(response));
        return await response.Content.ReadFromJsonAsync<SimklTokenResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("SIMKL returned an empty token response.");
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, string? accessToken,
        IReadOnlyDictionary<string, string?>? query, object? body, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var values = new Dictionary<string, string?>(query ?? new Dictionary<string, string?>())
        {
            ["client_id"] = _options.ClientId,
            ["app-name"] = "cantaro",
            ["app-version"] = "1.0"
        };
        var uri = new Uri(new Uri(_options.ApiBaseUrl.TrimEnd('/') + "/"), path.TrimStart('/'));
        using var request = new HttpRequestMessage(method, AddQuery(uri.ToString(), values));
        if (!string.IsNullOrWhiteSpace(accessToken)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null) request.Content = JsonContent.Create(body, options: JsonOptions);
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new SimklRequestException(response.StatusCode, ReadRetryAfter(response));
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId))
            throw new InvalidOperationException("SIMKL OAuth is not configured. Set Simkl:ClientId before connecting.");
    }

    private static string AddQuery(string url, IReadOnlyDictionary<string, string?> values)
    {
        var parameters = string.Join("&", values.Where(x => x.Value is not null)
            .Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value!)}"));
        return url + (url.Contains('?') ? "&" : "?") + parameters;
    }

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta) return delta;
        if (response.Headers.RetryAfter?.Date is { } date) return date - DateTimeOffset.UtcNow;
        return null;
    }
}

public sealed class SimklTokenResponse
{
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; set; }
    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
    public string? Scope { get; set; }
}

public sealed class SimklRequestException(HttpStatusCode statusCode, TimeSpan? retryAfter)
    : InvalidOperationException($"SIMKL request failed with status {(int)statusCode}.")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
