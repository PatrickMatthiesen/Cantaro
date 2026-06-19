namespace Cantaro.Api.Services;

public sealed class MusicSyncJobWorker(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<MusicSyncJobWorker> logger) : BackgroundService
{
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;
    private readonly ILogger<MusicSyncJobWorker> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<MusicSyncJobProcessor>();
                if (await processor.ProcessNextAsync(stoppingToken)) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Music sync job worker failed while checking queued work.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(750), stoppingToken);
        }
    }
}
