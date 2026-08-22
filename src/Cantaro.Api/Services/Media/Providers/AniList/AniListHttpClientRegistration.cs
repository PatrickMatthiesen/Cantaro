using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;

namespace Cantaro.Api.Services;

public static class AniListHttpClientRegistration
{
    internal static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    public static IHttpClientBuilder AddAniListApiClient(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<AniListRequestGate>();
        var httpClient = services.AddHttpClient<AniListApiClient>(client =>
        {
            client.Timeout = RequestTimeout;
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Cantaro/1.0 (+https://github.com/PatrickMatthiesen/Cantaro)");
        });

        // Every physical GraphQL request must pass through AniListRequestGate. Retrying
        // below the gate defeats both its burst pacing and provider-wide cooldown.
#pragma warning disable EXTEXP0001 // This is the package-provided per-client opt-out API.
        httpClient.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

        return httpClient;
    }
}
