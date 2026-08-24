using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;

namespace Cantaro.Api.Services;

public static class MyAnimeListHttpClientRegistration
{
    internal static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    public static IHttpClientBuilder AddMyAnimeListApiClient(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<MyAnimeListTokenRefreshGate>();
        services.TryAddSingleton<MyAnimeListRequestGate>();
        var httpClient = services.AddHttpClient<MyAnimeListApiClient>(client =>
        {
            client.Timeout = RequestTimeout;
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Cantaro/1.0 (+https://github.com/PatrickMatthiesen/Cantaro)");
        });

        // Reads, OAuth calls, and list mutations share this client. In particular,
        // PUT operations must never be replayed by the app-wide default handler.
#pragma warning disable EXTEXP0001
        httpClient.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

        return httpClient;
    }
}
