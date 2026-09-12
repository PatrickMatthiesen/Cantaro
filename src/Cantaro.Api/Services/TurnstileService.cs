using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Cantaro.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Cantaro.Api.Services;

public interface ITurnstileValidator
{
    Task<bool> ValidateAsync(string token, string action, CancellationToken cancellationToken);
}

public sealed class TurnstileValidator(
    IHttpClientFactory httpClientFactory,
    IOptions<TurnstileOptions> options,
    ILogger<TurnstileValidator> logger) : ITurnstileValidator
{
    private const string SiteVerifyUrl = "https://challenges.cloudflare.com/turnstile/v0/siteverify";
    private readonly TurnstileOptions _options = options.Value;

    public async Task<bool> ValidateAsync(string token, string action, CancellationToken cancellationToken)
    {
        if (!_options.IsEnabled
            || string.IsNullOrWhiteSpace(_options.Secret)
            || string.IsNullOrWhiteSpace(token)
            || token.Length > 2048
            || string.IsNullOrWhiteSpace(action))
        {
            return false;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, SiteVerifyUrl)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["secret"] = _options.Secret,
                    ["response"] = token
                })
            };

            using var response = await httpClientFactory.CreateClient("turnstile").SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Turnstile siteverify returned HTTP {StatusCode}.", response.StatusCode);
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<SiteVerifyResponse>(cancellationToken);
            return result?.Success == true
                && string.Equals(result.Action, action, StringComparison.Ordinal)
                && IsAllowedHostname(result.Hostname);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Turnstile siteverify failed.");
            return false;
        }
    }

    private bool IsAllowedHostname(string? hostname) => !string.IsNullOrWhiteSpace(hostname)
        && (_options.AllowedHostnames ?? []).Any(allowed => string.Equals(allowed.Trim(), hostname, StringComparison.OrdinalIgnoreCase));

    private sealed class SiteVerifyResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("action")]
        public string? Action { get; set; }

        [JsonPropertyName("hostname")]
        public string? Hostname { get; set; }
    }
}
