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
    ILogger<MediaObservationProgressService> logger)
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly MediaProviderOperationProcessor _operationProcessor = operationProcessor;
    private readonly ILogger<MediaObservationProgressService> _logger = logger;

    /// <summary>
    /// Evaluates whether the matched observation should trigger an automatic
    /// progress update for any of the user's library entries.
    /// Returns the number of operations enqueued (0 when no update applies).
    /// </summary>
    public async Task<int> TryEnqueueAutoProgressAsync(
        MediaObservation observation,
        CancellationToken cancellationToken)
    {
        if (observation.MatchStatus != MediaObservationStatuses.Matched
            || observation.MediaTitleId is null)
        {
            return 0;
        }

        if (!TryResolveObservationProgress(observation, out var parsedProgress))
        {
            _logger.LogDebug(
                "Skipping auto-progress for observation {ObservationId}: progress hint " +
                "\"{ProgressHint}\" could not be parsed as a positive integer.",
                observation.Id,
                observation.ProgressHint);
            return 0;
        }

        // Load the matched title so we know which progress dimension to update.
        var mediaTitle = await _dbContext.MediaTitles
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == observation.MediaTitleId, cancellationToken);

        if (mediaTitle is null)
        {
            return 0;
        }

        // Resolve the accepted candidate to get the match score for provenance.
        var matchScore = await GetAcceptedCandidateScoreAsync(observation, cancellationToken);

        // Find all library entries for this user and title. Local Cantaro state
        // advances immediately; connected entries also sync to the provider.
        var entries = await _dbContext.MediaLibraryEntries
            .Include(entry => entry.ProviderBindings)
                .ThenInclude(binding => binding.MediaProviderLink)
            .Where(e => e.UserId == observation.UserId
                        && e.MediaTitleId == observation.MediaTitleId)
            .ToListAsync(cancellationToken);

        if (entries.Count == 0)
        {
            _logger.LogDebug(
                "No library entries for user {UserId} / title {MediaTitleId}. " +
                "Auto-progress skipped.",
                observation.UserId,
                observation.MediaTitleId);
            return 0;
        }

        var enqueuedCount = 0;
        var localUpdatesCount = 0;

        foreach (var entry in entries)
        {
            var (progressEpisodes, progressChapters, progressVolumes) =
                DetermineProgressValues(mediaTitle, parsedProgress);

            if (!IsProgressAdvancing(entry, progressEpisodes, progressChapters, progressVolumes))
            {
                _logger.LogDebug(
                    "Auto-progress suppressed for entry {EntryId} (user {UserId}): " +
                    "new value ep={Ep}/ch={Ch}/vol={Vol} does not exceed current " +
                    "ep={CurEp}/ch={CurCh}/vol={CurVol}.",
                    entry.Id,
                    entry.UserId,
                    progressEpisodes, progressChapters, progressVolumes,
                    entry.ProgressEpisodes, entry.ProgressChapters, entry.ProgressVolumes);
                continue;
            }

            ApplyLocalProgressUpdate(entry, progressEpisodes, progressChapters, progressVolumes);
            localUpdatesCount++;

            var connectedBindings = entry.ProviderBindings
                .Where(binding => binding.ConnectedServiceAccountId is not null
                    && binding.MediaProviderLink is not null)
                .ToList();
            if (connectedBindings.Count == 0)
            {
                _logger.LogInformation(
                    "Applied local observation progress for user {UserId}, entry {EntryId}: " +
                    "episodes={Ep}, chapters={Ch}, volumes={Vol} from observation {ObservationId}. " +
                    "Provider sync was not queued because the entry is disconnected.",
                    entry.UserId,
                    entry.Id,
                    progressEpisodes,
                    progressChapters,
                    progressVolumes,
                    observation.Id);
                continue;
            }

            foreach (var binding in connectedBindings)
            {
                var payload = new AutoProgressUpdatePayload
                {
                    ProviderMediaId = binding.MediaProviderLink!.ExternalId,
                    ProgressEpisodes = progressEpisodes,
                    ProgressChapters = progressChapters,
                    ProgressVolumes = progressVolumes,
                    LastKnownRemoteUpdateAt = binding.LastRemoteUpdateAt,
                    TriggeredByObservationId = observation.Id.ToString(),
                    ObservedSiteIdentifier = observation.SiteIdentifier,
                    ObservedProgressHint = observation.ProgressHint,
                    ObservationMatchScore = matchScore,
                    TriggeredAt = DateTimeOffset.UtcNow
                };

                await _operationProcessor.EnqueueAutoProgressAsync(
                    entry.UserId, binding, payload, cancellationToken);

            _logger.LogInformation(
                "Queued auto-progress for user {UserId}, entry {EntryId} " +
                "(provider={Provider}, providerMediaId={ProviderMediaId}): " +
                "episodes={Ep} from observation {ObservationId} " +
                "(site={Site}, hint=\"{Hint}\", score={Score:P0}).",
                entry.UserId,
                entry.Id,
                binding.MediaProviderLink.Provider,
                binding.MediaProviderLink.ExternalId,
                progressEpisodes,
                observation.Id,
                observation.SiteIdentifier,
                observation.ProgressHint,
                matchScore ?? 0);

                enqueuedCount++;
            }
        }

        if (localUpdatesCount > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return enqueuedCount;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempt to parse a numeric episode/chapter count from the raw progress hint.
    /// Accepts plain integers ("5"), strings like "5", and falls back gracefully.
    /// Returns false when the hint is null, empty, or not parseable.
    /// </summary>
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

        return TryParseProgressHint(observation.ProgressHint, out value);
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

    private async Task<decimal?> GetAcceptedCandidateScoreAsync(
        MediaObservation observation,
        CancellationToken cancellationToken)
    {
        if (observation.AcceptedCandidateId is null)
        {
            return null;
        }

        var candidate = await _dbContext.MediaObservationCandidates
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == observation.AcceptedCandidateId, cancellationToken);

        return candidate?.Score;
    }
}
