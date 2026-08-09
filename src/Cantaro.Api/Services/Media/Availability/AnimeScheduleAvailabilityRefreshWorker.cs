using Cantaro.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Cantaro.Api.Services;

public sealed class AnimeScheduleAvailabilityRefreshWorker(
    IServiceScopeFactory serviceScopeFactory,
    IOptions<AnimeScheduleOptions> options,
    ILogger<AnimeScheduleAvailabilityRefreshWorker> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(10);
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;
    private readonly AnimeScheduleOptions _options = options.Value;
    private readonly ILogger<AnimeScheduleAvailabilityRefreshWorker> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(StartupDelay, stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var syncService = scope.ServiceProvider
                    .GetRequiredService<AnimeScheduleAvailabilitySyncService>();
                await syncService.SyncAllLibrariesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Scheduled AnimeSchedule availability refresh failed.");
            }

            var refreshInterval = TimeSpan.FromMinutes(Math.Max(5, _options.RefreshIntervalMinutes));
            await Task.Delay(refreshInterval, stoppingToken);
        }
    }
}
