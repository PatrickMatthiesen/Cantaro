using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

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
            .Where(observation => observation.MatchStatus == MediaObservationStatuses.Matched
                && !observation.IsCatalogObservation && observation.MediaTitleId != null)
            .ToListAsync(cancellationToken);
        var completedResolvedIds = new List<Guid>();
        foreach (var observation in matchedRows)
        {
            var expectedProgress = observation.ResolvedProgress;
            if (expectedProgress is not > 0
                && MediaObservationProgressService.TryParseProgressHint(observation.ProgressHint, out var parsed))
            {
                expectedProgress = parsed;
            }
            // A manual resolution without a progress request is complete. An
            // automatic match without usable progress remains a private task.
            if (expectedProgress is not > 0 && observation.ResolvedLibraryEntryId == null)
            {
                continue;
            }
            var saved = await dbContext.MediaLibraryEntries.AsNoTracking()
                .AnyAsync(entry => entry.UserId == observation.UserId
                    && entry.MediaTitleId == observation.MediaTitleId
                    && (observation.ResolvedLibraryEntryId == null || entry.Id == observation.ResolvedLibraryEntryId)
                    && (expectedProgress == null
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

        // Catalog observations pre-dating immediate cleanup have no completion
        // marker. They are safe to remove only when every rendered episode in
        // the row is already represented by a canonical provider identity for
        // the same title.
        var catalogRows = await dbContext.MediaObservations
            .AsNoTracking()
            .Include(observation => observation.Episodes)
            .Where(observation =>
                observation.IsCatalogObservation
                && observation.MatchStatus == MediaObservationStatuses.Matched
                && observation.MediaTitleId != null)
            .ToListAsync(cancellationToken);
        var completedCatalogIds = new List<Guid>();
        foreach (var observation in catalogRows)
        {
            var provider = observation.SiteIdentifier.Trim().ToLowerInvariant();
            var episodeIds = observation.Episodes
                .Select(episode => string.Equals(
                    provider,
                    MediaObservationSiteIdentifiers.Crunchyroll,
                    StringComparison.OrdinalIgnoreCase)
                    ? episode.ProviderEpisodeId.Trim().ToUpperInvariant()
                    : episode.ProviderEpisodeId.Trim())
                .Where(id => id.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (episodeIds.Count == 0)
            {
                continue;
            }

            var recordedEpisodeIds = await dbContext.MediaEpisodeProviderIdentities
                .Where(identity => identity.Provider == provider && !identity.HasConflict
                    && episodeIds.Contains(identity.ProviderEpisodeId))
                .Include(identity => identity.Content)
                .ThenInclude(content => content!.MediaEpisode)
                .Where(identity => identity.Content!.MediaEpisode!.MediaTitleId == observation.MediaTitleId)
                .Select(identity => identity.ProviderEpisodeId)
                .Distinct()
                .ToListAsync(cancellationToken);
            if (recordedEpisodeIds.Count == episodeIds.Count)
            {
                completedCatalogIds.Add(observation.Id);
            }
        }

        if (completedCatalogIds.Count > 0)
        {
            foreach (var snapshot in catalogRows.Where(row => completedCatalogIds.Contains(row.Id)))
            {
                deleted += await dbContext.MediaObservations
                    .Where(observation => observation.Id == snapshot.Id
                        && observation.UpdatedAt == snapshot.UpdatedAt
                        && observation.MatchStatus == MediaObservationStatuses.Matched)
                    .ExecuteDeleteAsync(cancellationToken);
            }
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
    public async Task<bool> DeleteAfterDurableOutcomeAsync(
        MediaObservation observation,
        bool outcomeVerified,
        CancellationToken cancellationToken = default)
    {
        if (!outcomeVerified)
        {
            return false;
        }

        dbContext.MediaObservations.Remove(observation);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
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
