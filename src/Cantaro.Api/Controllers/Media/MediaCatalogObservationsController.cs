using System.Security.Cryptography;
using System.Text;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Controllers;

/// <summary>
/// Ingests provider episode destinations rendered on series and season pages.
/// Catalog observations participate in title matching but never advance watch
/// progress or enqueue provider progress mutations.
/// </summary>
[ApiController]
[Route("api/media/catalog-observations")]
[Authorize]
public class MediaCatalogObservationsController(
    ApplicationDbContext dbContext,
    UserManager<User> userManager,
    MediaObservationMatchingService matchingService,
    MediaEpisodeIdentityService episodeIdentityService,
    ILogger<MediaCatalogObservationsController> logger,
    MediaObservationLifecycleService? lifecycleService = null) : ControllerBase
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly UserManager<User> _userManager = userManager;
    private readonly MediaObservationMatchingService _matchingService = matchingService;
    private readonly MediaEpisodeIdentityService _episodeIdentityService = episodeIdentityService;
    private readonly MediaObservationLifecycleService _lifecycleService = lifecycleService
        ?? new MediaObservationLifecycleService(dbContext);
    private readonly ILogger<MediaCatalogObservationsController> _logger = logger;

    [HttpPost]
    public async Task<ActionResult<SubmitMediaCatalogObservationResponse>> Submit(
        [FromBody] SubmitMediaCatalogObservationRequest request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateAndNormalize(request);
        if (validation.Error is not null)
        {
            return BadRequest(CreateRejectedResponse(request.Episodes.Count, validation.Error));
        }

        var userId = await GetCurrentUserIdAsync();
        var now = DateTimeOffset.UtcNow;
        var observedAt = MediaObservationLifecycleService.NormalizeObservedAt(request.ObservedAt, now);
        var observedTitle = BuildObservedTitle(request);
        var catalogKey = BuildCatalogKey(
            validation.Provider,
            validation.ProviderSeriesId,
            request.ProviderSeasonId,
            request.SeasonNumber);
        // Record provider facts from detached evidence before inspecting the private observation.
        var evidence = CreateObservation();
        var recordedCount = await _episodeIdentityService.RecordCatalogObservationAsync(evidence, cancellationToken);

        const int maxAttempts = 3;
        var retryingAfterSaveConflict = false;
        var incomingEvidenceSaved = false;
        for (var attempt = 1; ; attempt++)
        {
            var observation = await _dbContext.MediaObservations
                .Include(item => item.Candidates)
                .Include(item => item.Episodes)
                .FirstOrDefaultAsync(item => item.UserId == userId
                    && item.SiteIdentifier == validation.Provider
                    && item.SiteMediaId == catalogKey, cancellationToken);
            if (observation is not null && MediaObservationLifecycleService.ShouldKeepPersistedEvidence(
                observation.ObservedAt, observedAt, retryingAfterSaveConflict, incomingEvidenceSaved))
            {
                return Ok(CreateResponse(observation, MediaCatalogObservationStatuses.Deduplicated,
                    validation.ObservedEpisodeCount, recordedCount, validation.RejectedEpisodeCount));
            }
            var isDuplicate = observation is not null;
            if (observation is null)
            {
                observation = attempt == 1 ? evidence : CreateObservation();
                _dbContext.MediaObservations.Add(observation);
            }
            else
            {
                ApplyLatestEvidence(observation, request, validation, observedTitle, observedAt, now);
            }

            var saved = false;
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                saved = true;
                observation = await _matchingService.ProcessObservationAsync(observation, cancellationToken);
                var status = isDuplicate ? MediaCatalogObservationStatuses.Deduplicated
                    : observation.MatchStatus == MediaObservationStatuses.Matched
                        ? MediaCatalogObservationStatuses.Accepted
                        : MediaCatalogObservationStatuses.PendingMatch;

                _logger.LogInformation(
                    "Stored catalog observation {ObservationId} for user {UserId}: {Provider}/{ProviderSeriesId}, status={Status}.",
                    observation.Id, userId, validation.Provider, validation.ProviderSeriesId, status);

                var response = CreateResponse(
                    observation,
                    status,
                    validation.ObservedEpisodeCount,
                    recordedCount,
                    observation.MatchStatus == MediaObservationStatuses.Matched
                        ? validation.ObservedEpisodeCount - recordedCount
                        : validation.RejectedEpisodeCount);
                if (await DeleteCompletedCatalogObservationIfSafeAsync(observation, recordedCount, cancellationToken))
                {
                    response.ObservationId = null;
                }
                return Ok(response);
            }
            catch (DbUpdateException exception) when (attempt < maxAttempts && !isDuplicate && !saved
                && _lifecycleService.IsDuplicateObservationInsert(exception))
            {
                retryingAfterSaveConflict = true;
                _lifecycleService.DetachSubmission(observation);
            }
            catch (DbUpdateConcurrencyException exception) when ((isDuplicate || saved)
                && MediaObservationLifecycleService.IsSubmissionConcurrency(exception, observation))
            {
                if (attempt == maxAttempts)
                {
                    return Conflict(new { error = "Observation was updated by a newer submission." });
                }
                retryingAfterSaveConflict = true;
                incomingEvidenceSaved |= saved;
                _lifecycleService.DetachSubmission(observation);
            }
            catch (DbUpdateException exception) when (attempt < maxAttempts && (isDuplicate || saved)
                && _lifecycleService.IsSubmissionForeignKeyViolation(exception, observation.Id))
            {
                if (await _dbContext.MediaObservations.AnyAsync(item => item.Id == observation.Id, cancellationToken))
                {
                    throw;
                }
                retryingAfterSaveConflict = true;
                incomingEvidenceSaved |= saved;
                _lifecycleService.DetachSubmission(observation);
            }
        }

        MediaObservation CreateObservation()
        {
            var observation = new MediaObservation
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SiteIdentifier = validation.Provider,
                SiteMediaId = catalogKey,
                ObservedUrl = validation.SeriesUrl,
                ObservedTitle = observedTitle,
                MatchStatus = MediaObservationStatuses.Pending,
                CreatedAt = now
            };
            ApplyLatestEvidence(observation, request, validation, observedTitle, observedAt, now);
            return observation;
        }
    }

    private Task<bool> DeleteCompletedCatalogObservationIfSafeAsync(
        MediaObservation observation,
        int recordedCount,
        CancellationToken cancellationToken)
        => recordedCount > 0 && recordedCount == observation.Episodes.Count
            ? _lifecycleService.DeletePublishedCatalogObservationAsync(observation, cancellationToken)
            : Task.FromResult(false);

    private static void ApplyLatestEvidence(
        MediaObservation observation,
        SubmitMediaCatalogObservationRequest request,
        CatalogRequestValidation validation,
        string observedTitle,
        DateTimeOffset observedAt,
        DateTimeOffset now)
    {
        observation.ObservedUrl = validation.SeriesUrl;
        observation.ObservedTitle = observedTitle;
        observation.ObservedAt = observedAt;
        var seriesTitle = request.SeriesTitle.Trim();
        if (!string.Equals(observation.SeriesTitle, seriesTitle, StringComparison.Ordinal))
        {
            observation.ProviderChoicesPayload = null;
        }
        observation.SeriesTitle = seriesTitle;
        observation.ResetMatch();
        observation.SeasonTitle = TrimToNull(request.SeasonTitle);
        observation.ProviderSeriesId = validation.ProviderSeriesId;
        observation.ProviderSeasonId = TrimToNull(request.ProviderSeasonId);
        observation.SeasonNumber = request.SeasonNumber;
        observation.IsCatalogObservation = true;
        ApplyEpisodeEvidence(observation, request);
        observation.Touch(now);
    }

    private static void ApplyEpisodeEvidence(MediaObservation observation, SubmitMediaCatalogObservationRequest request)
    {
        var incomingIds = request.Episodes
            .Select(episode => episode.ProviderEpisodeId.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var existing in observation.Episodes
                     .Where(existing => !incomingIds.Contains(existing.ProviderEpisodeId))
                     .ToList())
        {
            // Remove orphaned children explicitly. Clearing a required
            // relationship and immediately adding replacements can make EF
            // issue a stale DELETE when the same tracked observation is
            // refreshed during duplicate handling.
            observation.Episodes.Remove(existing);
        }

        foreach (var episode in request.Episodes)
        {
            var existing = observation.Episodes.FirstOrDefault(item =>
                string.Equals(item.ProviderEpisodeId, episode.ProviderEpisodeId, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                // A default key lets EF track this episode as a new dependent.
                observation.Episodes.Add(new MediaObservationEpisode
                {
                    ProviderEpisodeId = episode.ProviderEpisodeId.Trim(), ProviderUrl = MediaDestinationUrlPolicy.NormalizeObservedUrl(episode.ProviderUrl),
                    EpisodeNumber = episode.EpisodeNumber, EpisodeTitle = TrimToNull(episode.EpisodeTitle), ReleaseTrack = TrimToNull(episode.ReleaseTrack),
                    AvailableSubtitleLanguageCodes = episode.AvailableSubtitleLanguageCodes.ToList(), AvailableAudioLanguageCodes = episode.AvailableAudioLanguageCodes.ToList()
                });
                continue;
            }

            existing.ProviderUrl = MediaDestinationUrlPolicy.NormalizeObservedUrl(episode.ProviderUrl);
            existing.EpisodeNumber = episode.EpisodeNumber;
            existing.EpisodeTitle = TrimToNull(episode.EpisodeTitle);
            existing.ReleaseTrack = TrimToNull(episode.ReleaseTrack);
            existing.AvailableSubtitleLanguageCodes = episode.AvailableSubtitleLanguageCodes.ToList();
            existing.AvailableAudioLanguageCodes = episode.AvailableAudioLanguageCodes.ToList();
        }
    }

    private static CatalogRequestValidation ValidateAndNormalize(SubmitMediaCatalogObservationRequest request)
    {
        request.Episodes ??= [];
        if (request.Episodes.Count is < 1 or > MediaCatalogObservationLimits.MaximumEpisodesPerObservation)
        {
            return CatalogRequestValidation.Rejected(
                $"A catalog observation must contain between 1 and {MediaCatalogObservationLimits.MaximumEpisodesPerObservation} episodes.");
        }

        var provider = request.Provider.Trim().ToLowerInvariant();
        var providerSeriesId = request.ProviderSeriesId.Trim().ToUpperInvariant();
        if (!MediaDestinationUrlPolicy.TryNormalizeSeriesUrl(
                provider,
                request.SeriesUrl,
                providerSeriesId,
                out var seriesUrl))
        {
            return CatalogRequestValidation.Rejected("The provider series URL is not allowed or does not match the provider series ID.");
        }

        var observedEpisodeCount = request.Episodes.Count;
        var distinctEpisodes = request.Episodes
            .Where(item => item is not null && !string.IsNullOrWhiteSpace(item.ProviderEpisodeId))
            .DistinctBy(item => item.ProviderEpisodeId.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToList();
        var safeEpisodes = distinctEpisodes.Where(item =>
            item.EpisodeNumber > 0
            && MediaDestinationUrlPolicy.TryNormalizePath(
                provider,
                item.ProviderUrl,
                item.ProviderEpisodeId,
                out _))
            .ToList();
        if (safeEpisodes.Count == 0)
        {
            return CatalogRequestValidation.Rejected("The catalog observation did not contain any safe provider episode URLs.");
        }
        request.Episodes = safeEpisodes;

        return new CatalogRequestValidation(
            provider,
            providerSeriesId,
            seriesUrl,
            observedEpisodeCount,
            observedEpisodeCount - safeEpisodes.Count,
            null);
    }

    private static string BuildCatalogKey(
        string provider,
        string providerSeriesId,
        string? providerSeasonId,
        int? seasonNumber)
    {
        var source = string.Join(
            ':',
            provider,
            providerSeriesId,
            TrimToNull(providerSeasonId)?.ToUpperInvariant() ?? string.Empty,
            seasonNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
        return $"catalog:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))}";
    }

    private static string BuildObservedTitle(SubmitMediaCatalogObservationRequest request)
    {
        var seriesTitle = request.SeriesTitle.Trim();
        var seasonTitle = TrimToNull(request.SeasonTitle);
        return seasonTitle is null ? seriesTitle : $"{seriesTitle} — {seasonTitle}";
    }

    private static SubmitMediaCatalogObservationResponse CreateResponse(
        MediaObservation observation,
        string status,
        int observedEpisodeCount,
        int recordedEpisodeCount,
        int rejectedEpisodeCount) => new()
        {
            Status = status,
            ObservationId = observation.Id.ToString(),
            MatchStatus = observation.MatchStatus,
            MatchedMediaTitleId = observation.MediaTitleId?.ToString(),
            ObservedEpisodeCount = observedEpisodeCount,
            RecordedEpisodeCount = recordedEpisodeCount,
            RejectedEpisodeCount = rejectedEpisodeCount
        };

    private static SubmitMediaCatalogObservationResponse CreateRejectedResponse(
        int observedEpisodeCount,
        string error) => new()
        {
            Status = MediaCatalogObservationStatuses.Rejected,
            ObservedEpisodeCount = observedEpisodeCount,
            RejectedEpisodeCount = observedEpisodeCount,
            Error = error
        };

    private async Task<int> GetCurrentUserIdAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            throw new UnauthorizedAccessException("User not authenticated.");
        }

        return user.Id;
    }

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record CatalogRequestValidation(
        string Provider,
        string ProviderSeriesId,
        string SeriesUrl,
        int ObservedEpisodeCount,
        int RejectedEpisodeCount,
        string? Error)
    {
        public static CatalogRequestValidation Rejected(string error) => new(
            string.Empty,
            string.Empty,
            string.Empty,
            0,
            0,
            error);
    }
}
