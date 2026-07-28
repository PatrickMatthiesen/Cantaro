using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Retry;

namespace Cantaro.Api.Services;

public static class MusicBrainzHttpClientRegistration
{
    internal static readonly TimeSpan MaximumRetryAfter = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan TotalRequestTimeout = TimeSpan.FromSeconds(15);

    public static IHttpClientBuilder AddMusicBrainzQueryClient(this IServiceCollection services)
    {
        var httpClient = services.AddHttpClient<IMusicBrainzQueryClient, MusicBrainzQueryClient>();

        // Aspire's standard handler is useful as a general default, but its
        // retry budget is too large for MusicBrainz's deliberately paced API.
#pragma warning disable EXTEXP0001 // This is the package-provided per-client opt-out API.
        httpClient.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
        httpClient.AddResilienceHandler("musicbrainz", pipeline =>
        {
            pipeline.AddTimeout(TotalRequestTimeout);
            pipeline.AddRetry(CreateRetryOptions());
        });

        return httpClient;
    }

    internal static HttpRetryStrategyOptions CreateRetryOptions() => new()
    {
        MaxRetryAttempts = 1,
        Delay = TimeSpan.FromSeconds(1),
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        MaxDelay = MaximumRetryAfter,
        ShouldRetryAfterHeader = true,
        ShouldHandle = arguments =>
            ValueTask.FromResult(ShouldRetry(arguments.Outcome))
    };

    internal static bool ShouldRetry(Outcome<HttpResponseMessage> outcome)
    {
        if (!HttpClientResiliencePredicates.IsTransient(outcome))
        {
            return false;
        }

        var response = outcome.Result;
        if (response == null)
        {
            return true;
        }

        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
        {
            return delta <= MaximumRetryAfter;
        }

        if (retryAfter?.Date is { } date)
        {
            return date - DateTimeOffset.UtcNow <= MaximumRetryAfter;
        }

        return true;
    }
}
