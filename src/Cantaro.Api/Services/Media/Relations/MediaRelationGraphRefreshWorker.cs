using Cantaro.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

public sealed class MediaRelationGraphRefreshWorker(
    MediaRelationGraphRefreshQueue queue,
    IServiceScopeFactory serviceScopeFactory,
    ILogger<MediaRelationGraphRefreshWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Freshness = TimeSpan.FromHours(6);
    private static readonly TimeSpan MinimumDelayBetweenRefreshes = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromSeconds(30);
    private const int MaximumAttempts = 3;
    private readonly MediaRelationGraphRefreshQueue _queue = queue;
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;
    private readonly ILogger<MediaRelationGraphRefreshWorker> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var isFirst = true;
        await foreach (var workItem in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                if (!isFirst)
                {
                    await Task.Delay(MinimumDelayBetweenRefreshes, stoppingToken);
                }
                isFirst = false;
                await ProcessWithRetryAsync(workItem, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Relation graph refresh failed for {Provider}/{ProviderMediaId}.",
                    workItem.ProviderId,
                    workItem.ProviderMediaId);
            }
            finally
            {
                _queue.Complete(workItem);
            }
        }
    }

    private async Task ProcessWithRetryAsync(
        MediaRelationGraphRefreshWorkItem workItem,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            try
            {
                await ProcessAsync(workItem, cancellationToken);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (attempt < MaximumAttempts)
            {
                var exponentialDelay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                var requestedDelay = exception is AniListRequestException { RetryAfter: { } retryAfter }
                    ? retryAfter
                    : TimeSpan.Zero;
                var retryDelay = requestedDelay > exponentialDelay ? requestedDelay : exponentialDelay;
                retryDelay = retryDelay < TimeSpan.Zero
                    ? exponentialDelay
                    : retryDelay > MaximumRetryDelay
                        ? MaximumRetryDelay
                        : retryDelay;
                _logger.LogWarning(
                    exception,
                    "Relation graph refresh attempt {Attempt}/{MaximumAttempts} failed for {Provider}/{ProviderMediaId}; retrying in {RetryDelay}.",
                    attempt,
                    MaximumAttempts,
                    workItem.ProviderId,
                    workItem.ProviderMediaId,
                    retryDelay);
                await Task.Delay(retryDelay, cancellationToken);
            }
        }
    }

    private async Task ProcessAsync(
        MediaRelationGraphRefreshWorkItem workItem,
        CancellationToken cancellationToken)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var verifiedAt = await dbContext.MediaProviderLinks
            .AsNoTracking()
            .Where(link => link.Provider == workItem.ProviderId
                && link.ExternalId == workItem.ProviderMediaId)
            .Select(link => link.RelationsLastVerifiedAt)
            .SingleOrDefaultAsync(cancellationToken);
        if (verifiedAt >= DateTimeOffset.UtcNow - Freshness)
        {
            return;
        }

        var registry = scope.ServiceProvider.GetRequiredService<IMediaProviderRegistry>();
        if (registry.GetRequired(workItem.ProviderId) is not IMediaRelationGraphProvider provider)
        {
            return;
        }

        var syncService = scope.ServiceProvider.GetRequiredService<MediaTitleRelationSyncService>();
        await syncService.SyncAsync(
            workItem.UserId,
            provider,
            workItem.ProviderMediaId,
            cancellationToken);
    }
}
