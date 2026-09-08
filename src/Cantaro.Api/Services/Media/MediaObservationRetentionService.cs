using Cantaro.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

/// <summary>
/// Removes expired, user-scoped media observations while retaining the
/// canonical media catalog and provider identities they may have resolved to.
/// </summary>
public sealed class MediaObservationRetentionService(ApplicationDbContext dbContext)
{
    public static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(30);

    public static DateTimeOffset GetCutoff(DateTimeOffset now) => now - RetentionPeriod;

    /// <summary>
    /// Deletes observations whose last server update is outside the retention
    /// window. Rows written before UpdatedAt was populated use CreatedAt as a
    /// compatibility fallback.
    /// </summary>
    public async Task<int> CleanupExpiredAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var cutoff = GetCutoff(now);
        var defaultUpdatedAt = default(DateTimeOffset);

        // UpdatedAt is server maintained for current rows. A small number of
        // legacy rows may still contain its CLR default, so use CreatedAt for
        // those rows without loading the entire observation table.
        return await dbContext.MediaObservations
            .Where(observation =>
                observation.UpdatedAt <= cutoff
                && (observation.UpdatedAt != defaultUpdatedAt || observation.CreatedAt <= cutoff))
            .ExecuteDeleteAsync(cancellationToken);
    }
}

public sealed class MediaObservationRetentionWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<MediaObservationRetentionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan RunInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunCleanupAsync(stoppingToken);

        using var timer = new PeriodicTimer(RunInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunCleanupAsync(stoppingToken);
        }
    }

    private async Task RunCleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<MediaObservationRetentionService>();
            var deleted = await service.CleanupExpiredAsync(timeProvider.GetUtcNow(), cancellationToken);
            if (deleted > 0)
            {
                logger.LogInformation("Removed {DeletedCount} expired media observations.", deleted);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Unable to clean up expired media observations.");
        }
    }
}
