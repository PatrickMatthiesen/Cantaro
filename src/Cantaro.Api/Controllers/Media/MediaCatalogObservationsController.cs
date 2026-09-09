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
        var catalogKey = BuildCatalogKey(
            validation.Provider,
            validation.ProviderSeriesId,
            request.ProviderSeasonId,
            request.SeasonNumber);
        var existing = await _dbContext.MediaObservations
            .Include(item => item.Candidates)
            .Include(item => item.Episodes)
            .FirstOrDefaultAsync(item =>
                item.UserId == userId
                && item.SiteIdentifier == validation.Provider
                && item.SiteMediaId == catalogKey,
                cancellationToken);

        if (existing is not null)
        {
            return Ok(await ProcessDuplicateAsync(
                existing,
                request,
                validation,
                userId,
                now,
                cancellationToken));
        }

        var observation = new MediaObservation
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SiteIdentifier = validation.Provider,
            SiteMediaId = catalogKey,
            ObservedUrl = validation.SeriesUrl,
            ObservedTitle = BuildObservedTitle(request),
            ObservedAt = request.ObservedAt ?? now,
            SeriesTitle = request.SeriesTitle.Trim(),
            SeasonTitle = TrimToNull(request.SeasonTitle),
            ProviderSeriesId = validation.ProviderSeriesId,
            ProviderSeasonId = TrimToNull(request.ProviderSeasonId),
            SeasonNumber = request.SeasonNumber,
            IsCatalogObservation = true,
            MatchStatus = MediaObservationStatuses.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };

        ApplyEpisodeEvidence(observation, request);
        _dbContext.MediaObservations.Add(observation);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _dbContext.Entry(observation).State = EntityState.Detached;
            existing = await _dbContext.MediaObservations
                .Include(item => item.Candidates)
                .Include(item => item.Episodes)
                .FirstOrDefaultAsync(item =>
                    item.UserId == userId
                    && item.SiteIdentifier == validation.Provider
                    && item.SiteMediaId == catalogKey,
                    cancellationToken);
            if (existing is null)
            {
                throw;
            }

            return Ok(await ProcessDuplicateAsync(
                existing,
                request,
                validation,
                userId,
                now,
                cancellationToken));
        }

        observation = await _matchingService.ProcessObservationAsync(observation.Id, cancellationToken);
        var acceptedCount = await RecordMatchedCatalogAsync(observation, request, cancellationToken);
        var status = observation.MatchStatus == MediaObservationStatuses.Matched
            ? MediaCatalogObservationStatuses.Accepted
            : MediaCatalogObservationStatuses.PendingMatch;

        _logger.LogInformation(
            "Stored catalog observation {ObservationId} for user {UserId}: {Provider}/{ProviderSeriesId}, status={Status}.",
            observation.Id,
            userId,
            validation.Provider,
            validation.ProviderSeriesId,
            status);

        var response = CreateResponse(
            observation,
            status,
            validation.ObservedEpisodeCount,
            acceptedCount,
            observation.MatchStatus == MediaObservationStatuses.Matched
                ? validation.ObservedEpisodeCount - acceptedCount
                : validation.RejectedEpisodeCount);
        await DeleteCompletedCatalogObservationIfSafeAsync(observation, acceptedCount, cancellationToken);
        return Ok(response);
    }

    private async Task<SubmitMediaCatalogObservationResponse> ProcessDuplicateAsync(
        MediaObservation observation,
        SubmitMediaCatalogObservationRequest request,
        CatalogRequestValidation validation,
        int userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ApplyLatestEvidence(observation, request, validation.SeriesUrl, now);
        observation = await RefreshMatchAsync(observation, cancellationToken);
        var recordedCount = await RecordMatchedCatalogAsync(observation, request, cancellationToken);

        _logger.LogInformation(
            "Deduplicated catalog observation {ObservationId} for user {UserId}: {Provider}/{ProviderSeriesId}.",
            observation.Id,
            userId,
            validation.Provider,
            validation.ProviderSeriesId);

        var response = CreateResponse(
            observation,
            MediaCatalogObservationStatuses.Deduplicated,
            validation.ObservedEpisodeCount,
            recordedCount,
            observation.MatchStatus == MediaObservationStatuses.Matched
                ? validation.ObservedEpisodeCount - recordedCount
                : validation.RejectedEpisodeCount);
        await DeleteCompletedCatalogObservationIfSafeAsync(observation, recordedCount, cancellationToken);
        return response;
    }

    private async Task<MediaObservation> RefreshMatchAsync(
        MediaObservation observation,
        CancellationToken cancellationToken)
    {
        // Re-evaluate catalog observations as split-season titles arrive.
        return await _matchingService.ProcessObservationAsync(observation.Id, cancellationToken);
    }

    private async Task<int> RecordMatchedCatalogAsync(
        MediaObservation observation,
        SubmitMediaCatalogObservationRequest request,
        CancellationToken cancellationToken)
    {
        if (observation.MatchStatus != MediaObservationStatuses.Matched)
        {
            return 0;
        }

        return await _episodeIdentityService.RecordCatalogObservationAsync(observation, cancellationToken);
    }

    private Task DeleteCompletedCatalogObservationIfSafeAsync(
        MediaObservation observation,
        int recordedCount,
        CancellationToken cancellationToken)
        => _lifecycleService.DeleteAfterDurableOutcomeAsync(
            observation,
            observation.MatchStatus == MediaObservationStatuses.Matched
                && recordedCount > 0
                && recordedCount == observation.Episodes.Count,
            cancellationToken);

    private void ApplyLatestEvidence(
        MediaObservation observation,
        SubmitMediaCatalogObservationRequest request,
        string normalizedSeriesUrl,
        DateTimeOffset now)
    {
        observation.ObservedUrl = normalizedSeriesUrl;
        observation.ObservedTitle = BuildObservedTitle(request);
        observation.ObservedAt = request.ObservedAt ?? now;
        observation.SeriesTitle = request.SeriesTitle.Trim();
        observation.SeasonTitle = TrimToNull(request.SeasonTitle);
        observation.ProviderSeriesId = observation.ProviderSeriesId ?? request.ProviderSeriesId.Trim();
        observation.ProviderSeasonId = TrimToNull(request.ProviderSeasonId);
        observation.SeasonNumber = request.SeasonNumber;
        observation.IsCatalogObservation = true;
        ApplyEpisodeEvidence(observation, request);
        observation.UpdatedAt = now;
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
                observation.Episodes.Add(new MediaObservationEpisode
                {
                    Id = Guid.NewGuid(), ProviderEpisodeId = episode.ProviderEpisodeId.Trim(), ProviderUrl = MediaDestinationUrlPolicy.NormalizeObservedUrl(episode.ProviderUrl),
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
