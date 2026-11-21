using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

/// <summary>
/// Applies conservative local progress updates when a matched observation
/// carries a numeric progress hint.
///
/// Rules (per issue #32):
/// 1. Observation must be matched to a canonical MediaTitle.
/// 2. Progress hint must be parseable as a positive integer.
/// 3. Update is monotonic: only applied when the new value strictly exceeds
///    the current tracked progress.
/// 4. Local Cantaro progress is updated immediately.
/// 5. Connected entries also enqueue a provider operation so the next provider
///    refresh does not overwrite the tracked episode.
/// 6. Sync-metadata guard: the entry's <c>LastRemoteUpdateAt</c> is embedded in
///    the operation payload so the processor can skip the write if the provider
///    has since reported newer state.
/// </summary>
public class MediaObservationProgressService(
    ApplicationDbContext dbContext,
    MediaProviderOperationProcessor operationProcessor,
    MediaLibraryEventHub eventHub,
    ILogger<MediaObservationProgressService> logger,
    MediaProviderSeasonMappingService mappingService,
    MediaObservationMatchingService matchingService)
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly MediaProviderOperationProcessor _operationProcessor = operationProcessor;
    private readonly MediaLibraryEventHub _eventHub = eventHub;
    private readonly ILogger<MediaObservationProgressService> _logger = logger;

    /// <summary>
    /// Evaluates whether the matched observation should trigger an automatic
    /// progress update for any of the user's library entries.
    /// Returns the number of operations enqueued (0 when no update applies).
    /// </summary>
    public async Task<int> TryEnqueueAutoProgressAsync(
        MediaObservation observation,
        CancellationToken cancellationToken)
        => (await TryEnqueueAutoProgressWithResultAsync(observation, cancellationToken)).EnqueuedCount;

    public async Task<MediaObservationProgressResult> TryEnqueueAutoProgressWithResultAsync(
        MediaObservation observation,
        CancellationToken cancellationToken,
        (Guid TitleId, int? EpisodeNumber)? expectedAssignment = null,
        bool requireSharedAssignment = false)
    {
        if (observation.IsCatalogObservation || observation.MatchStatus != MediaObservationStatuses.Matched
            || observation.MediaTitleId is null)
        {
            return MediaObservationProgressResult.None;
        }

        var result = await MediaProviderSeasonMappingService.WithWriteAsync(_dbContext, async () =>
        {
            if (observation.Id != Guid.Empty
                && !await LockCurrentMatchAsync(observation, cancellationToken))
            {
                return MediaObservationProgressResult.None;
            }

            return await StageAutoProgressAsync(observation, cancellationToken, expectedAssignment,
                requireSharedAssignment, applyProgress: true);
        }, cancellationToken);

        if (result.ProgressUpdated)
        {
            _eventHub.Publish(observation.UserId);
        }

        return result;
    }

    // The caller holds the mapping lock and has locked or updated the observation.
    internal async Task<MediaObservationProgressResult> StageAutoProgressAsync(
        MediaObservation observation,
        CancellationToken cancellationToken,
        (Guid TitleId, int? EpisodeNumber)? expectedAssignment,
        bool requireSharedAssignment,
        bool applyProgress)
    {
        var effective = await mappingService.ResolveAsync(observation, cancellationToken);
        var hasPlaybackProgress = applyProgress && !observation.IsCatalogObservation;
        if (expectedAssignment is { } expected
            && (effective is not null
                ? effective.Title.Id != expected.TitleId
                    || (hasPlaybackProgress && effective.EpisodeNumber != expected.EpisodeNumber)
                : requireSharedAssignment))
        {
            return new(0, 0, AssignmentChanged: true);
        }

        if (!hasPlaybackProgress)
        {
            return MediaObservationProgressResult.None;
        }

        if (effective is not null
            && (effective.Title.Id != observation.MediaTitleId || effective.EpisodeNumber != observation.ResolvedProgress))
        {
            matchingService.ApplyMapping(observation, effective);
            observation.Touch(DateTimeOffset.UtcNow);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        else if (effective is null && MediaObservationMatchingService.UsedSharedMapping(observation))
        {
            await matchingService.ProcessObservationAsync(observation, cancellationToken);
        }

        var progress = await ApplyProgressAsync(observation, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return progress;
    }

    internal async Task LockObservationAsync(Guid observationId, CancellationToken cancellationToken)
    {
        if (_dbContext.Database.IsNpgsql())
        {
            await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT "Id" FROM "MediaObservations"
                WHERE "Id" = {observationId}
                FOR UPDATE
                """, cancellationToken);
        }
    }

    private async Task<bool> LockCurrentMatchAsync(MediaObservation observation, CancellationToken cancellationToken)
    {
        await LockObservationAsync(observation.Id, cancellationToken);
        return await _dbContext.MediaObservations.AsNoTracking().AnyAsync(row =>
            row.Id == observation.Id && row.MatchStatus == MediaObservationStatuses.Matched
            && row.UpdatedAt == observation.UpdatedAt, cancellationToken);
    }

    private async Task<MediaObservationProgressResult> ApplyProgressAsync(
        MediaObservation observation, CancellationToken cancellationToken)
    {
        if (observation.MatchStatus != MediaObservationStatuses.Matched || observation.MediaTitleId is not { } titleId)
        {
            return MediaObservationProgressResult.None;
        }

        var title = await _dbContext.MediaTitles.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == titleId, cancellationToken);
        if (title is null)
        {
            return MediaObservationProgressResult.None;
        }

        var completion = title.IsCompletionOnly;
        var parsedProgress = 0;
        if (!completion && !TryResolveObservationProgress(observation, out parsedProgress))
        {
            _logger.LogDebug("No numeric progress for observation {ObservationId}.", observation.Id);
            return MediaObservationProgressResult.None;
        }

        var (episodes, chapters, volumes) = DetermineProgressValues(title, parsedProgress);
        if (!completion && title.EpisodeCount is > 0 && episodes > title.EpisodeCount)
        {
            return MediaObservationProgressResult.None;
        }

        if (_dbContext.Database.IsNpgsql())
        {
            await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT "Id" FROM "MediaLibraryEntries"
                WHERE "UserId" = {observation.UserId} AND "MediaTitleId" = {titleId}
                FOR UPDATE
                """, cancellationToken);
        }

        var entries = await _dbContext.MediaLibraryEntries
            .Include(entry => entry.ProviderBindings).ThenInclude(binding => binding.MediaProviderLink)
            .Where(entry => entry.UserId == observation.UserId && entry.MediaTitleId == titleId)
            .ToListAsync(cancellationToken);
        var enqueuedCount = 0;
        var localUpdatesCount = 0;
        foreach (var entry in entries)
        {
            await _dbContext.Entry(entry).ReloadAsync(cancellationToken);
            if (completion ? entry.Status == MediaLibraryStatuses.Completed :
                !IsProgressAdvancing(entry, episodes, chapters, volumes))
            {
                continue;
            }

            if (completion)
            {
                entry.Status = MediaLibraryStatuses.Completed;
            }

            ApplyLocalProgressUpdate(entry, completion ? null : episodes, completion ? null : chapters,
                completion ? null : volumes);
            localUpdatesCount++;
            foreach (var binding in entry.ProviderBindings.Where(binding =>
                         binding.ConnectedServiceAccountId is not null && binding.MediaProviderLink is not null))
            {
                if (completion)
                {
                    await _operationProcessor.EnqueueStatusUpdateAsync(entry.UserId, binding,
                        new MediaStatusUpdateRequest
                        {
                            ProviderMediaId = binding.MediaProviderLink!.ExternalId,
                            Status = MediaLibraryStatuses.Completed
                        }, cancellationToken, deferSave: true);
                }
                else
                {
                    await _operationProcessor.EnqueueAutoProgressAsync(entry.UserId, binding,
                        new AutoProgressUpdatePayload
                        {
                            ProviderMediaId = binding.MediaProviderLink!.ExternalId,
                            ProgressEpisodes = episodes, ProgressChapters = chapters, ProgressVolumes = volumes,
                            LastKnownRemoteUpdateAt = binding.LastRemoteUpdateAt,
                            LocalProgressCommitted = true
                        }, cancellationToken, deferSave: true);
                }
                enqueuedCount++;
            }
        }

        return new(enqueuedCount, localUpdatesCount, entries.Count > 0);
    }

    public static bool TryParseProgressHint(string? hint, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(hint))
        {
            return false;
        }

        // Handle plain integer strings ("5") produced by Crunchyroll adapter.
        if (int.TryParse(hint.Trim(), out var parsed) && parsed > 0)
        {
            value = parsed;
            return true;
        }

        return false;
    }

    private static bool TryResolveObservationProgress(MediaObservation observation, out int value)
    {
        if (observation.ResolvedProgress is > 0)
        {
            value = observation.ResolvedProgress.Value;
            return true;
        }

        value = 0;
        return observation.EpisodeOffset is null or 0
            && !MediaObservationMatchingService.UsedSharedMapping(observation)
            && TryParseProgressHint(observation.ProgressHint, out value);
    }

    /// <summary>
    /// Maps a parsed integer progress value to the correct dimension based on
    /// the title's <see cref="MediaTitle.PrimaryProgressDimension"/>.
    /// </summary>
    private static (int? episodes, int? chapters, int? volumes) DetermineProgressValues(
        MediaTitle title,
        int parsedValue)
    {
        return title.PrimaryProgressDimension switch
        {
            MediaProgressDimensions.Chapter => (null, parsedValue, null),
            MediaProgressDimensions.Volume => (null, null, parsedValue),
            _ => (parsedValue, null, null)
        };
    }

    /// <summary>
    /// Returns true only when at least one progress dimension would strictly
    /// increase.  Prevents regressive or no-op writes.
    /// </summary>
    private static bool IsProgressAdvancing(
        MediaLibraryEntry entry,
        int? newEpisodes,
        int? newChapters,
        int? newVolumes)
    {
        if (newEpisodes.HasValue && newEpisodes.Value > (entry.ProgressEpisodes ?? 0))
        {
            return true;
        }

        if (newChapters.HasValue && newChapters.Value > (entry.ProgressChapters ?? 0))
        {
            return true;
        }

        if (newVolumes.HasValue && newVolumes.Value > (entry.ProgressVolumes ?? 0))
        {
            return true;
        }

        return false;
    }

    private static void ApplyLocalProgressUpdate(
        MediaLibraryEntry entry,
        int? progressEpisodes,
        int? progressChapters,
        int? progressVolumes)
    {
        var now = DateTimeOffset.UtcNow;

        entry.ProgressEpisodes = progressEpisodes ?? entry.ProgressEpisodes;
        entry.ProgressChapters = progressChapters ?? entry.ProgressChapters;
        entry.ProgressVolumes = progressVolumes ?? entry.ProgressVolumes;
        entry.LastMutationSource = MediaMutationSources.ObservationAutoProgress;
        entry.LastLocalEditAt = now;
        entry.UpdatedAt = now;
    }

}

public readonly record struct MediaObservationProgressResult(int EnqueuedCount, int LocalUpdatesCount,
    bool OutcomeSatisfied = false, bool AssignmentChanged = false)
{
    public static MediaObservationProgressResult None => new(0, 0);

    public bool ProgressUpdated => LocalUpdatesCount > 0;
}
