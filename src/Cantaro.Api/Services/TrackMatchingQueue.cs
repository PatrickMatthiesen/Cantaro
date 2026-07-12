using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Cantaro.Api.Services;

public sealed class TrackMatchingQueue
{
    private readonly Channel<Guid> _queue = Channel.CreateUnbounded<Guid>();
    private readonly ConcurrentDictionary<Guid, byte> _queued = new();

    public void Enqueue(Guid observationId)
    {
        if (_queued.TryAdd(observationId, 0)) _queue.Writer.TryWrite(observationId);
    }

    public async ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken)
        => await _queue.Reader.ReadAsync(cancellationToken);

    public void Complete(Guid observationId) => _queued.TryRemove(observationId, out _);
}

public sealed class TrackMatchingWorker(
    TrackMatchingQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<TrackMatchingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var observationId = await queue.DequeueAsync(stoppingToken);
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<TrackMatchingService>()
                    .ProcessObservationAsync(observationId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                logger.LogError(exception, "Background matching failed for track observation {ObservationId}", observationId);
            }
            finally { queue.Complete(observationId); }
        }
    }
}
