using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cantaro.Api.Services.Spotify;

public static class SpotifyHttpClientRegistration
{
    public static IHttpClientBuilder AddSpotifyApiClient(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<SpotifyRequestGateOptions>()
            .BindConfiguration("Spotify:RequestGate")
            .Validate(options => options.RequestInterval > TimeSpan.Zero,
                "Spotify Web API request pacing must be positive.")
            .Validate(options => options.MissingRetryAfterBaseDelay > TimeSpan.Zero
                && options.QuotaMissingRetryAfterBaseDelay > TimeSpan.Zero
                && options.MissingRetryAfterMaxDelay >= options.MissingRetryAfterBaseDelay
                && options.MissingRetryAfterMaxDelay >= options.QuotaMissingRetryAfterBaseDelay
                && options.BackoffResetQuietPeriod > TimeSpan.Zero,
                "Spotify fallback delays and quiet period must be positive, and the cap must cover both initial delays.")
            .ValidateOnStart();
        services.TryAddSingleton<SpotifyCooldown>();
        var httpClient = services.AddHttpClient<SpotifyApiClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.spotify.com");
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Cantaro/1.0 (+https://github.com/PatrickMatthiesen/Cantaro)");
        });

        // Aspire's default handler retries POST automatically. Spotify playlist
        // appends are non-idempotent, so only the client's explicit GET/token
        // request retry logic may retry a physical request.
#pragma warning disable EXTEXP0001 // This is the package-provided per-client opt-out API.
        httpClient.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

        return httpClient;
    }
}
