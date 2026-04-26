using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cantaro.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Cantaro.Api.Services;

public class AniListApiClient(
    HttpClient httpClient,
    IOptions<AniListOptions> options,
    ILogger<AniListApiClient> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient = httpClient;
    private readonly AniListOptions _options = options.Value;
    private readonly ILogger<AniListApiClient> _logger = logger;

    public string BuildAuthorizationUrl(string redirectUri, string state, string codeChallenge)
    {
        EnsureClientIdConfigured();

        var query = new Dictionary<string, string?>
        {
            ["client_id"] = _options.ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["state"] = state,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256"
        };

        var queryString = string.Join(
            "&",
            query.Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value ?? string.Empty)}"));

        return $"{_options.AuthorizationUrl}?{queryString}";
    }

    public async Task<AniListTokenResponse> ExchangeCodeAsync(
        string authorizationCode,
        string redirectUri,
        string codeVerifier,
        CancellationToken cancellationToken)
    {
        EnsureClientIdConfigured();

        var payload = new Dictionary<string, string?>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["code"] = authorizationCode,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = codeVerifier
        };

        return await SendTokenRequestAsync(payload, cancellationToken);
    }

    public async Task<AniListTokenResponse> RefreshAccessTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        EnsureClientIdConfigured();

        var payload = new Dictionary<string, string?>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["refresh_token"] = refreshToken
        };

        return await SendTokenRequestAsync(payload, cancellationToken);
    }

    public async Task<TData> SendGraphQlAsync<TData>(
        string accessToken,
        string query,
        object? variables,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.GraphQlUrl)
        {
            Content = JsonContent.Create(new
            {
                query,
                variables
            })
        };

        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("AniList GraphQL request failed with status {StatusCode}: {Body}", response.StatusCode, responseBody);
            throw new InvalidOperationException($"AniList request failed with status {(int)response.StatusCode}.");
        }

        var graphQlResponse = JsonSerializer.Deserialize<AniListGraphQlResponse<TData>>(responseBody, SerializerOptions)
            ?? throw new InvalidOperationException("AniList returned an unreadable GraphQL response.");

        if (graphQlResponse.Errors is { Count: > 0 })
        {
            var message = string.Join("; ", graphQlResponse.Errors.Select(error => error.Message).Where(message => !string.IsNullOrWhiteSpace(message)));
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(message) ? "AniList returned an unknown GraphQL error." : message);
        }

        return graphQlResponse.Data ?? throw new InvalidOperationException("AniList returned an empty GraphQL response.");
    }

    public static string GenerateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static string BuildCodeChallenge(string codeVerifier)
    {
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private async Task<AniListTokenResponse> SendTokenRequestAsync(
        IReadOnlyDictionary<string, string?> payload,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(_options.TokenUrl, payload, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("AniList token request failed with status {StatusCode}: {Body}", response.StatusCode, responseBody);
            throw new InvalidOperationException($"AniList token request failed with status {(int)response.StatusCode}.");
        }

        return JsonSerializer.Deserialize<AniListTokenResponse>(responseBody, SerializerOptions)
            ?? throw new InvalidOperationException("AniList returned an unreadable token response.");
    }

    private void EnsureClientIdConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId))
        {
            throw new InvalidOperationException("AniList OAuth is not configured. Set AniList:ClientId before connecting a provider.");
        }
    }
}

public class AniListTokenResponse
{
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }
}

public class AniListGraphQlResponse<TData>
{
    [JsonPropertyName("data")]
    public TData? Data { get; set; }

    [JsonPropertyName("errors")]
    public List<AniListGraphQlError> Errors { get; set; } = [];
}

public class AniListGraphQlError
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
