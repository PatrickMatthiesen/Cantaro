using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;

namespace Cantaro.Api.Services;

public static class SimklHttpClientRegistration
{
    public static IHttpClientBuilder AddSimklApiClient(this IServiceCollection services)
    {
        services.TryAddSingleton<SimklTokenRefreshGate>();
        services.TryAddSingleton<SimklImportGate>();
        var builder = services.AddHttpClient<SimklApiClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Cantaro/1.0 (+https://github.com/PatrickMatthiesen/Cantaro)");
        });
#pragma warning disable EXTEXP0001
        builder.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
        return builder;
    }
}

public sealed class SimklImportGate
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, SemaphoreSlim> _gates = new();
    public async Task<IDisposable> AcquireAsync(int userId, CancellationToken cancellationToken)
    {
        var gate = _gates.GetOrAdd(userId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Lease(gate);
    }
    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

public sealed class SimklTokenRefreshGate
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, SemaphoreSlim> _gates = new();
    public async Task<IDisposable> AcquireAsync(int accountId, CancellationToken cancellationToken)
    {
        var gate = _gates.GetOrAdd(accountId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Lease(gate);
    }

    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}
