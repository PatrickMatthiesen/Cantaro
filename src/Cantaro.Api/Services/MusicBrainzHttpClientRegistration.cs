using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;

namespace Cantaro.Api.Services;

public static class MusicBrainzHttpClientRegistration
{
    internal static readonly TimeSpan TotalRequestTimeout = TimeSpan.FromSeconds(15);

    public static IHttpClientBuilder AddMusicBrainzQueryClient(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<MusicBrainzRequestGate>();
        var httpClient = services.AddHttpClient<IMusicBrainzQueryClient, MusicBrainzQueryClient>(client =>
        {
            client.Timeout = TotalRequestTimeout;
        });

        // Aspire's standard handler is useful as a general default, but its
        // retry behavior bypasses the package's global one-request-per-second scheduler.
#pragma warning disable EXTEXP0001 // This is the package-provided per-client opt-out API.
        httpClient.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

        return httpClient;
    }
}
