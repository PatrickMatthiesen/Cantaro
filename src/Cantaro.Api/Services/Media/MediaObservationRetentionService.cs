using System.Data.Common;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Cantaro.Api.Services;

/// <summary>
/// Removes observation rows after their private workflow has completed. The
/// canonical media catalog and provider identities are stored independently.
/// </summary>
public sealed class MediaObservationRetentionService(ApplicationDbContext dbContext)
{
    /// <summary>
    /// Deletes rows whose workflow is already complete. Unresolved rows are
    /// deliberately excluded: they remain available until the user resolves
    /// or rejects them.
    /// </summary>
    public async Task<int> CleanupCompletedAsync(CancellationToken cancellationToken = default)
    {
        var deleted = await dbContext.MediaObservations
            .Where(observation =>
                observation.MatchStatus == MediaObservationStatuses.Rejected)
            .ExecuteDeleteAsync(cancellationToken);

        // Resolve stores the library entry before it attempts the progress
        // update. Verify the resulting value before cleaning up legacy rows so
        // a crash between those operations cannot lose a private task.
        var matchedRows = await dbContext.MediaObservations.AsNoTracking()
            .Include(observation => observation.MediaTitle)
            .Where(observation => observation.MatchStatus == MediaObservationStatuses.Matched
                && !observation.IsCatalogObservation && observation.MediaTitleId != null)
            .ToListAsync(cancellationToken);
        var completedResolvedIds = new List<Guid>();
        foreach (var observation in matchedRows)
        {
            var completion = observation.MediaTitle?.IsCompletionOnly == true;
            var expectedProgress = observation.ResolvedProgress;
            if (!completion && expectedProgress is not > 0
                && MediaObservationProgressService.TryParseProgressHint(observation.ProgressHint, out var parsed))
            {
                expectedProgress = parsed;
            }
            // A manual resolution without a progress request is complete. An
            // automatic match without usable progress remains a private task.
            if (!completion && expectedProgress is not > 0 && observation.ResolvedLibraryEntryId == null)
            {
                continue;
            }
            var saved = await dbContext.MediaLibraryEntries.AsNoTracking()
                .AnyAsync(entry => entry.UserId == observation.UserId
                    && entry.MediaTitleId == observation.MediaTitleId
                    && (observation.ResolvedLibraryEntryId == null || entry.Id == observation.ResolvedLibraryEntryId)
                    && (completion ? entry.Status == MediaLibraryStatuses.Completed :
                        expectedProgress == null
                        || (entry.MediaTitle!.PrimaryProgressDimension == MediaProgressDimensions.Chapter
                            ? entry.ProgressChapters >= expectedProgress
                            : entry.MediaTitle!.PrimaryProgressDimension == MediaProgressDimensions.Volume
                                ? entry.ProgressVolumes >= expectedProgress
                                : entry.ProgressEpisodes >= expectedProgress)), cancellationToken);
            if (saved)
            {
                completedResolvedIds.Add(observation.Id);
            }
        }
        if (completedResolvedIds.Count > 0)
        {
            foreach (var snapshot in matchedRows.Where(row => completedResolvedIds.Contains(row.Id)))
            {
                deleted += await dbContext.MediaObservations
                    .Where(observation => observation.Id == snapshot.Id
                        && observation.UpdatedAt == snapshot.UpdatedAt
                        && observation.MatchStatus == MediaObservationStatuses.Matched)
                    .ExecuteDeleteAsync(cancellationToken);
            }
        }

        var catalogRows = await dbContext.MediaObservations
            .AsNoTracking()
            .Include(observation => observation.Episodes)
            .Where(observation =>
                observation.IsCatalogObservation
                && observation.MatchStatus == MediaObservationStatuses.Matched)
            .ToListAsync(cancellationToken);
        foreach (var snapshot in catalogRows)
        {
            deleted += await MediaProviderSeasonMappingService.WithWriteAsync(dbContext, async () =>
            {
                if (!await MediaObservationLifecycleService.HasPublishedCatalogAsync(dbContext, snapshot, cancellationToken))
                {
                    return 0;
                }

                return await dbContext.MediaObservations
                    .Where(observation => observation.Id == snapshot.Id
                        && observation.UpdatedAt == snapshot.UpdatedAt
                        && observation.MatchStatus == MediaObservationStatuses.Matched)
                    .ExecuteDeleteAsync(cancellationToken);
            }, cancellationToken);
        }

        return deleted;
    }
}

/// <summary>
/// Deletes a private observation only after its caller has verified that the
/// canonical facts and any personal progress have been durably written.
/// </summary>
public sealed class MediaObservationLifecycleService(ApplicationDbContext dbContext)
{
    internal static bool ShouldKeepPersistedEvidence(DateTimeOffset persistedAt, DateTimeOffset incomingAt,
        bool retryingAfterSaveConflict, bool incomingEvidenceSaved) =>
        incomingEvidenceSaved || persistedAt > incomingAt
        || (retryingAfterSaveConflict && persistedAt == incomingAt);

    internal static DateTimeOffset NormalizeObservedAt(DateTimeOffset? observedAt, DateTimeOffset receivedAt)
    {
        var effectiveAt = observedAt is { } clientAt && clientAt <= receivedAt ? clientAt : receivedAt;
        var ticks = effectiveAt.UtcTicks;
        return new DateTimeOffset(ticks - ticks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);
    }

    internal void DetachSubmission(MediaObservation observation)
    {
        foreach (var entry in dbContext.ChangeTracker.Entries()
            .Where(entry => BelongsToSubmission(entry.Entity, observation.Id)).ToList())
        {
            entry.State = EntityState.Detached;
        }
        dbContext.Entry(observation).State = EntityState.Detached;
    }

    internal static bool IsSubmissionConcurrency(DbUpdateConcurrencyException exception, MediaObservation observation) =>
        exception.Entries.Any(entry => ReferenceEquals(entry.Entity, observation) ||
            BelongsToSubmission(entry.Entity, observation.Id));

    internal bool IsSubmissionForeignKeyViolation(DbUpdateException exception, Guid observationId) =>
        exception.Entries.Any(entry => BelongsToSubmission(entry.Entity, observationId))
        && (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation }
            || (dbContext.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite" && exception.InnerException is DbException sqliteException
                && sqliteException.Message.Contains("FOREIGN KEY constraint failed", StringComparison.Ordinal)));

    internal bool IsDuplicateObservationInsert(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_MediaObservations_UserId_SiteIdentifier_SiteMediaId"
        }
        || (dbContext.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite" && exception.InnerException is DbException sqliteException
            && sqliteException.Message.Contains(
                "UNIQUE constraint failed: MediaObservations.UserId, MediaObservations.SiteIdentifier, MediaObservations.SiteMediaId",
                StringComparison.Ordinal));

    private static bool BelongsToSubmission(object entity, Guid observationId) => entity switch
    {
        MediaObservationEpisode episode => episode.MediaObservationId == observationId,
        MediaObservationCandidate candidate => candidate.MediaObservationId == observationId,
        _ => false
    };

    internal Task<bool> DeletePublishedCatalogObservationAsync(
        MediaObservation observation, CancellationToken cancellationToken) =>
        MediaProviderSeasonMappingService.WithWriteAsync(dbContext, async () =>
            await DeleteAfterDurableOutcomeAsync(observation,
                await HasPublishedCatalogAsync(dbContext, observation, cancellationToken), cancellationToken),
            cancellationToken);

    internal static async Task<bool> HasPublishedCatalogAsync(
        ApplicationDbContext dbContext, MediaObservation observation, CancellationToken cancellationToken)
    {
        return observation.MatchStatus == MediaObservationStatuses.Matched
            && await MediaProviderSeasonMappingService.GetCatalogCoverageAsync(dbContext, observation, cancellationToken) is not null;
    }

    public async Task<bool> DeleteAfterDurableOutcomeAsync(
        MediaObservation observation,
        bool outcomeVerified,
        CancellationToken cancellationToken = default)
    {
        if (!outcomeVerified)
        {
            return false;
        }

        var deleted = await dbContext.MediaObservations
            .Where(item => item.Id == observation.Id && item.UpdatedAt == observation.UpdatedAt
                && item.MatchStatus == observation.MatchStatus)
            .ExecuteDeleteAsync(cancellationToken);
        dbContext.Entry(observation).State = EntityState.Detached;
        return deleted > 0;
    }
}

public sealed class MediaObservationRetentionWorker(
    IServiceScopeFactory scopeFactory,
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
            var deleted = await service.CleanupCompletedAsync(cancellationToken);
            if (deleted > 0)
            {
                logger.LogInformation("Removed {DeletedCount} completed media observations.", deleted);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Unable to clean up completed media observations.");
        }
    }
}
