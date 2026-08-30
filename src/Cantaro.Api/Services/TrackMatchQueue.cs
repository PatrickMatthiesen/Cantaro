using Cantaro.Api.Configuration;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cantaro.Api.Services;

public sealed record ClaimedTrackMatch(Guid ObservationId, Guid LeaseId, int RetryCount);

public sealed class TrackMatchQueue(ApplicationDbContext dbContext, TimeProvider timeProvider)
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(10);

    public async Task EnqueueAsync(
        Guid observationId,
        CancellationToken cancellationToken,
        DateTimeOffset? nextAttemptAt = null)
    {
        var requestedAt = (nextAttemptAt ?? timeProvider.GetUtcNow()).UtcDateTime;
        if (dbContext.Database.IsNpgsql())
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "TrackMatchQueueItems" ("TrackObservationId", "NextAttemptAt")
                VALUES ({observationId}, {requestedAt})
                ON CONFLICT ("TrackObservationId") DO UPDATE
                SET "NextAttemptAt" = LEAST("TrackMatchQueueItems"."NextAttemptAt", EXCLUDED."NextAttemptAt");
                """, cancellationToken);
            return;
        }

        var tracked = dbContext.TrackMatchQueueItems.Local
            .FirstOrDefault(item => item.TrackObservationId == observationId);
        var existing = tracked ?? await dbContext.TrackMatchQueueItems
            .FirstOrDefaultAsync(item => item.TrackObservationId == observationId, cancellationToken);
        if (existing is null)
        {
            dbContext.TrackMatchQueueItems.Add(new TrackMatchQueueItem
            {
                TrackObservationId = observationId,
                NextAttemptAt = requestedAt
            });
            return;
        }

        if (requestedAt < existing.NextAttemptAt)
        {
            existing.NextAttemptAt = requestedAt;
        }
    }

    public async Task EnqueueManualRetryAsync(Guid observationId, CancellationToken cancellationToken)
    {
        var requestedAt = timeProvider.GetUtcNow().UtcDateTime;
        if (dbContext.Database.IsNpgsql())
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "TrackMatchQueueItems" ("TrackObservationId", "NextAttemptAt", "RetryCount")
                VALUES ({observationId}, {requestedAt}, 0)
                ON CONFLICT ("TrackObservationId") DO UPDATE
                SET "NextAttemptAt" = EXCLUDED."NextAttemptAt",
                    "RetryCount" = 0,
                    "LeaseId" = NULL,
                    "LeaseExpiresAt" = NULL;
                """, cancellationToken);
            return;
        }

        var existing = await dbContext.TrackMatchQueueItems
            .FirstOrDefaultAsync(item => item.TrackObservationId == observationId, cancellationToken);
        if (existing is null)
        {
            dbContext.TrackMatchQueueItems.Add(new TrackMatchQueueItem
            {
                TrackObservationId = observationId,
                NextAttemptAt = requestedAt
            });
        }
        else
        {
            existing.NextAttemptAt = requestedAt;
            existing.RetryCount = 0;
            existing.LeaseId = null;
            existing.LeaseExpiresAt = null;
        }
    }

    public async Task<ClaimedTrackMatch?> TryClaimNextAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var candidate = await dbContext.TrackMatchQueueItems
                .AsNoTracking()
                .Where(item => item.NextAttemptAt <= now
                    && (item.LeaseExpiresAt == null || item.LeaseExpiresAt <= now))
                .OrderBy(item => item.NextAttemptAt)
                .ThenBy(item => item.TrackObservationId)
                .Select(item => new { item.TrackObservationId, item.RetryCount })
                .FirstOrDefaultAsync(cancellationToken);
            if (candidate is null) return null;

            var leaseId = Guid.NewGuid();
            var claimed = await dbContext.TrackMatchQueueItems
                .Where(item => item.TrackObservationId == candidate.TrackObservationId
                    && item.NextAttemptAt <= now
                    && (item.LeaseExpiresAt == null || item.LeaseExpiresAt <= now))
                .ExecuteUpdateAsync(
                    updates => updates
                        .SetProperty(item => item.LeaseId, leaseId)
                        .SetProperty(item => item.LeaseExpiresAt, now + LeaseDuration),
                    cancellationToken);
            if (claimed == 1) return new ClaimedTrackMatch(candidate.TrackObservationId, leaseId, candidate.RetryCount);
        }

        return null;
    }

    public Task CompleteAsync(ClaimedTrackMatch claim, CancellationToken cancellationToken) =>
        dbContext.TrackMatchQueueItems
            .Where(item => item.TrackObservationId == claim.ObservationId && item.LeaseId == claim.LeaseId)
            .ExecuteDeleteAsync(cancellationToken);

    public Task RescheduleAsync(
        ClaimedTrackMatch claim,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken) =>
        dbContext.TrackMatchQueueItems
            .Where(item => item.TrackObservationId == claim.ObservationId && item.LeaseId == claim.LeaseId)
            .ExecuteUpdateAsync(
                updates => updates
                    .SetProperty(item => item.NextAttemptAt, nextAttemptAt.UtcDateTime)
                    .SetProperty(item => item.RetryCount, item => item.RetryCount + 1)
                    .SetProperty(item => item.LeaseId, (Guid?)null)
                    .SetProperty(item => item.LeaseExpiresAt, (DateTime?)null),
                cancellationToken);

    public async Task RecoverPendingAsync(int maximumAttempts, CancellationToken cancellationToken)
    {
        var queuedIds = dbContext.TrackMatchQueueItems.Select(item => item.TrackObservationId);
        var missing = await dbContext.TrackObservations
            .AsNoTracking()
            .Where(observation => observation.TrackId == null
                && observation.MatchStatus == TrackMatchingStatuses.Pending
                && observation.MatchAttemptCount < maximumAttempts
                && !queuedIds.Contains(observation.Id))
            .Select(observation => observation.Id)
            .ToListAsync(cancellationToken);
        foreach (var observationId in missing)
        {
            await EnqueueAsync(observationId, cancellationToken);
        }

        if (missing.Count > 0) await dbContext.SaveChangesAsync(cancellationToken);
    }
}

public sealed class TrackMatchingWorker(
    IServiceScopeFactory scopeFactory,
    MusicBrainzRequestGate musicBrainzRequestGate,
    TimeProvider timeProvider,
    IOptions<TrackMatchingOptions> options,
    ILogger<TrackMatchingWorker> logger) : BackgroundService
{
    private static readonly TimeSpan EmptyQueueDelay = TimeSpan.FromSeconds(1);
    private readonly TrackMatchingOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverPendingAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            var cooldownDelay = musicBrainzRequestGate.NotBefore - timeProvider.GetUtcNow();
            if (cooldownDelay > TimeSpan.Zero)
            {
                await Task.Delay(cooldownDelay, timeProvider, stoppingToken);
                continue;
            }

            var processed = await ProcessNextAsync(stoppingToken);
            if (!processed) await Task.Delay(EmptyQueueDelay, timeProvider, stoppingToken);
        }
    }

    private async Task RecoverPendingAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TrackMatchQueue>()
            .RecoverPendingAsync(_options.MaximumAutomaticAttempts, cancellationToken);
    }

    private async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<TrackMatchQueue>();
        var claim = await queue.TryClaimNextAsync(cancellationToken);
        if (claim is null) return false;

        try
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var current = await dbContext.TrackObservations
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == claim.ObservationId, cancellationToken);
            if (current is null
                || current.TrackId != null
                || current.MatchStatus != TrackMatchingStatuses.Pending
                || claim.RetryCount >= _options.MaximumAutomaticAttempts)
            {
                await queue.CompleteAsync(claim, cancellationToken);
                return true;
            }

            var observation = await scope.ServiceProvider.GetRequiredService<TrackMatchingService>()
                .ProcessObservationAsync(claim.ObservationId, cancellationToken);
            if (observation.MatchStatus != TrackMatchingStatuses.Pending
                || string.IsNullOrWhiteSpace(observation.LastMatchError)
                || claim.RetryCount + 1 >= _options.MaximumAutomaticAttempts)
            {
                await queue.CompleteAsync(claim, cancellationToken);
                return true;
            }

            var nextAttemptAt = timeProvider.GetUtcNow() + GetRetryDelay(observation.MatchAttemptCount);
            var providerNotBefore = musicBrainzRequestGate.NotBefore;
            if (providerNotBefore > nextAttemptAt) nextAttemptAt = providerNotBefore;
            await queue.RescheduleAsync(claim, nextAttemptAt, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Background matching failed for track observation {ObservationId}", claim.ObservationId);
            if (claim.RetryCount + 1 >= _options.MaximumAutomaticAttempts)
            {
                await queue.CompleteAsync(claim, cancellationToken);
            }
            else
            {
                await queue.RescheduleAsync(
                    claim,
                    timeProvider.GetUtcNow() + GetRetryDelay(claim.RetryCount + 1),
                    cancellationToken);
            }
        }

        return true;
    }

    private TimeSpan GetRetryDelay(int attemptCount)
    {
        var exponent = Math.Clamp(attemptCount - 1, 0, 20);
        var ticks = _options.RetryBaseDelaySeconds * TimeSpan.TicksPerSecond * (1L << exponent);
        return TimeSpan.FromTicks(Math.Min(ticks, TimeSpan.FromMinutes(_options.RetryMaximumDelayMinutes).Ticks));
    }
}
