using Cantaro.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

public class MediaProviderOperationWorker(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<MediaProviderOperationWorker> logger) : BackgroundService
{
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;
    private readonly ILogger<MediaProviderOperationWorker> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                if (!await dbContext.Database.CanConnectAsync(stoppingToken))
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                    continue;
                }

                var processor = scope.ServiceProvider.GetRequiredService<MediaProviderOperationProcessor>();
                var processedCount = await processor.ProcessDueOperationsAsync(stoppingToken);
                if (processedCount > 0)
                {
                    _logger.LogInformation("Processed {Count} queued media provider operations.", processedCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Media provider operation worker failed while processing queued operations.");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
