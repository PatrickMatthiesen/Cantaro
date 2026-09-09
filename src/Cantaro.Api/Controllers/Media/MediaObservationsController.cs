using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Controllers;

/// <summary>
/// Manages the media observation pipeline: extension ingestion, review queue,
/// and candidate resolution/rejection. All operations are user-scoped.
/// </summary>
[ApiController]
[Route("api/media/observations")]
[Authorize]
public class MediaObservationsController(
    ApplicationDbContext dbContext,
    UserManager<User> userManager,
    MediaObservationMatchingService matchingService,
    MediaObservationProgressService progressService,
    MediaEpisodeIdentityService episodeIdentityService,
    MediaProviderSeasonMappingService seasonMappingService,
    IMediaProviderRegistry mediaProviderRegistry,
    ILogger<MediaObservationsController> logger,
    MediaObservationLifecycleService? lifecycleService = null) : ControllerBase
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly UserManager<User> _userManager = userManager;
    private readonly MediaObservationMatchingService _matchingService = matchingService;
    private readonly MediaObservationProgressService _progressService = progressService;
    private readonly MediaObservationLifecycleService _lifecycleService = lifecycleService
        ?? new MediaObservationLifecycleService(dbContext);
    private readonly MediaEpisodeIdentityService _episodeIdentityService = episodeIdentityService;
    private readonly MediaProviderSeasonMappingService _seasonMappingService = seasonMappingService;
    private readonly IMediaProviderRegistry _mediaProviderRegistry = mediaProviderRegistry;
    private readonly ILogger<MediaObservationsController> _logger = logger;
    private static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web);

    // -----------------------------------------------------------------------
    // Ingestion
    // -----------------------------------------------------------------------

    /// <summary>
    /// Ingest a media observation from the browser extension.
    /// POST /api/media/observations
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<SubmitMediaObservationResponse>> Submit(
        [FromBody] SubmitMediaObservationRequest request,
        CancellationToken cancellationToken)
    {
        request.ObservedEpisodes ??= [];
        if (request.ObservedEpisodes.Count > MediaCatalogObservationLimits.MaximumEpisodesPerObservation)
        {
            return BadRequest(new
            {
                error = $"A series-page observation may contain at most {MediaCatalogObservationLimits.MaximumEpisodesPerObservation} episodes."
            });
        }

        var userId = await GetCurrentUserIdAsync();

        var normalizedSite = request.SiteIdentifier.ToLowerInvariant().Trim();
        var normalizedSiteMediaId = string.IsNullOrWhiteSpace(request.SiteMediaId)
            ? null
            : request.SiteMediaId.Trim();
        var progressHint = string.IsNullOrWhiteSpace(request.ProgressHint)
            ? request.EpisodeNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : request.ProgressHint.Trim();
        var now = DateTimeOffset.UtcNow;
        var normalizedObservedUrl = MediaDestinationUrlPolicy.NormalizeObservedUrl(request.ObservedUrl);

        // Deduplicate by (UserId, SiteIdentifier, SiteMediaId) when SiteMediaId
        // is available. This prevents duplicate rows when the extension re-fires
        // for the same episode.
        if (normalizedSiteMediaId is not null)
        {
            var existing = await _dbContext.MediaObservations
                .Include(o => o.Candidates)
                .Include(o => o.Episodes)
                .FirstOrDefaultAsync(
                    o => o.UserId == userId
                         && o.SiteIdentifier == normalizedSite
                         && o.SiteMediaId == normalizedSiteMediaId,
                    cancellationToken);

            if (existing is not null)
            {
                existing.ObservedUrl = normalizedObservedUrl;
                existing.ObservedTitle = request.ObservedTitle.Trim();
                existing.ProgressHint = progressHint;
                existing.ObservedAt = request.ObservedAt ?? now;
                ApplyStructuredPayload(existing, request, isCatalogObservation: false);
                existing.UpdatedAt = now;

                var exactIdentityApplied = await _matchingService.TryApplyProviderEpisodeIdentityAsync(
                    existing,
                    cancellationToken);
                var shouldReprocess = !exactIdentityApplied
                    && (existing.MatchStatus != MediaObservationStatuses.Matched
                        || await HasConflictedProviderIdentityAsync(
                            existing.SiteIdentifier,
                            existing.SiteMediaId,
                            cancellationToken));
                if (shouldReprocess)
                {
                    // A previously matched observation may now disagree with
                    // a conflicted provider identity. Re-run matching so the
                    // trusted mismatch can enter the review workflow instead
                    // of silently replaying the stale canonical assignment.
                    existing = await _matchingService.ProcessObservationAsync(existing, cancellationToken);
                }
                var deduplicatedProviderChoicesUnavailableReason = await EnsureProviderChoicesAsync(existing, cancellationToken);

                var progressUpdated = false;
                if (existing.MatchStatus == MediaObservationStatuses.Matched)
                {
                    await ApplyStoredOffsetAsync(existing, cancellationToken);
                    await _episodeIdentityService.RecordObservationAsync(existing, cancellationToken);
                    progressUpdated = (await _progressService.TryEnqueueAutoProgressWithResultAsync(
                        existing,
                        cancellationToken)).ProgressUpdated;
                }

                var response = MapToSubmitResponse(
                    existing,
                    wasDeduplicated: true,
                    deduplicatedProviderChoicesUnavailableReason,
                    progressUpdated);
                await DeleteCompletedObservationIfSafeAsync(existing, progressUpdated, cancellationToken);

                _logger.LogInformation(
                    "Deduplicated MediaObservation for user {UserId}: {SiteIdentifier}/{SiteMediaId} → {ObservationId}.",
                    userId, normalizedSite, normalizedSiteMediaId, existing.Id);

                return Ok(response);
            }
        }

        var observation = new MediaObservation
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SiteIdentifier = normalizedSite,
            ObservedUrl = normalizedObservedUrl,
            SiteMediaId = normalizedSiteMediaId,
            ObservedTitle = request.ObservedTitle.Trim(),
            ProgressHint = progressHint,
            ObservedAt = request.ObservedAt ?? now,
            IsCatalogObservation = false,
            MatchStatus = MediaObservationStatuses.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };
        ApplyStructuredPayload(observation, request, isCatalogObservation: false);

        _dbContext.MediaObservations.Add(observation);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Stored new MediaObservation {ObservationId} for user {UserId}: {SiteIdentifier} \"{ObservedTitle}\".",
            observation.Id, userId, normalizedSite, observation.ObservedTitle);

        // Run candidate matching synchronously for MVP — fast enough for library
        // lookups and avoids requiring a background worker.
        observation = await _matchingService.ProcessObservationAsync(observation, cancellationToken);
        var providerChoicesUnavailableReason = await EnsureProviderChoicesAsync(observation, cancellationToken);

        // Attempt episode progress tracking if the matching step produced a confident match.
        var newProgressUpdated = false;
        if (observation.MatchStatus == MediaObservationStatuses.Matched)
        {
            await ApplyStoredOffsetAsync(observation, cancellationToken);
            await _episodeIdentityService.RecordObservationAsync(observation, cancellationToken);
            newProgressUpdated = (await _progressService.TryEnqueueAutoProgressWithResultAsync(
                observation,
                cancellationToken)).ProgressUpdated;
        }

        var submitResponse = MapToSubmitResponse(
            observation,
            wasDeduplicated: false,
            providerChoicesUnavailableReason,
            newProgressUpdated);
        await DeleteCompletedObservationIfSafeAsync(observation, newProgressUpdated, cancellationToken);
        return Ok(submitResponse);
    }

    private Task<bool> HasConflictedProviderIdentityAsync(
        string provider,
        string? providerEpisodeId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(providerEpisodeId)
            || providerEpisodeId.StartsWith("catalog:", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(false);
        }

        var normalizedProvider = provider.Trim().ToLowerInvariant();
        var normalizedEpisodeId = normalizedProvider == MediaObservationSiteIdentifiers.Crunchyroll
            ? providerEpisodeId.Trim().ToUpperInvariant()
            : providerEpisodeId.Trim();
        return _dbContext.MediaEpisodeProviderIdentities.AnyAsync(
            identity => identity.Provider == normalizedProvider
                && identity.ProviderEpisodeId == normalizedEpisodeId
                && identity.HasConflict,
            cancellationToken);
    }

    private async Task<bool> DeleteCompletedObservationIfSafeAsync(
        MediaObservation observation,
        bool progressUpdated,
        CancellationToken cancellationToken)
    {
        if (observation.MatchStatus != MediaObservationStatuses.Matched)
        {
            return false;
        }

        // A match is not by itself proof that the user's personal outcome was
        // persisted. Keep the task when no library progress was requested or
        // when the matched title is not currently in the user's library.
        var hasProgressRequest = observation.ResolvedProgress is > 0
            || MediaObservationProgressService.TryParseProgressHint(observation.ProgressHint, out _);
        var durableOutcome = progressUpdated
            || await HasAppliedProgressAsync(observation, cancellationToken)
            || (!hasProgressRequest && observation.Episodes.Count > 0);
        return await _lifecycleService.DeleteAfterDurableOutcomeAsync(
            observation,
            durableOutcome,
            cancellationToken);
    }

    private async Task<bool> HasAppliedProgressAsync(
        MediaObservation observation,
        CancellationToken cancellationToken)
    {
        if (observation.MediaTitleId is not { } mediaTitleId
            || !MediaObservationProgressService.TryParseProgressHint(
                observation.ResolvedProgress is > 0
                    ? observation.ResolvedProgress.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : observation.ProgressHint,
                out var expectedProgress))
        {
            return false;
        }

        var title = await _dbContext.MediaTitles
            .AsNoTracking()
            .Where(item => item.Id == mediaTitleId)
            .Select(item => item.PrimaryProgressDimension)
            .FirstOrDefaultAsync(cancellationToken);
        if (title is null)
        {
            return false;
        }

        return await _dbContext.MediaLibraryEntries.AnyAsync(entry =>
                entry.UserId == observation.UserId
                && entry.MediaTitleId == mediaTitleId
                && (title == MediaProgressDimensions.Chapter
                    ? entry.ProgressChapters >= expectedProgress
                    : title == MediaProgressDimensions.Volume
                        ? entry.ProgressVolumes >= expectedProgress
                        : entry.ProgressEpisodes >= expectedProgress),
                cancellationToken);
    }

    // -----------------------------------------------------------------------
    // Review queue
    // -----------------------------------------------------------------------

    /// <summary>
    /// Summary counts for unresolved observations.
    /// GET /api/media/observations/summary
    /// </summary>
    [HttpGet("summary")]
    public async Task<ActionResult<MediaObservationSummaryDto>> GetSummary(CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserIdAsync();

        var grouped = await _dbContext.MediaObservations
            .Where(o => o.UserId == userId &&
                        o.MatchStatus != MediaObservationStatuses.Rejected)
            .GroupBy(o => o.MatchStatus)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return Ok(new MediaObservationSummaryDto
        {
            TotalUnresolved = grouped.Sum(g => g.Count),
            Pending = grouped.FirstOrDefault(g => g.Key == MediaObservationStatuses.Pending)?.Count ?? 0,
            Ambiguous = grouped.FirstOrDefault(g => g.Key == MediaObservationStatuses.Ambiguous)?.Count ?? 0,
            NoMatch = grouped.FirstOrDefault(g => g.Key == MediaObservationStatuses.NoMatch)?.Count ?? 0
        });
    }

    /// <summary>
    /// List unresolved observations for the current user.
    /// GET /api/media/observations?includeResolved=false
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<MediaObservationDto>>> List(
        [FromQuery] bool includeResolved = false,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var userId = await GetCurrentUserIdAsync();

        var query = _dbContext.MediaObservations
            .Include(o => o.Candidates)
            .Where(o => o.UserId == userId);

        if (!includeResolved)
        {
            query = query.Where(o => o.MatchStatus != MediaObservationStatuses.Rejected);
        }

        var observations = await query
            .OrderByDescending(o => o.MatchStatus == MediaObservationStatuses.Ambiguous)
            .ThenByDescending(o => o.CreatedAt)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken);

        return Ok(observations.Select(MapToDto).ToList());
    }

    /// <summary>
    /// Get a single observation with full candidate list.
    /// GET /api/media/observations/{id}
    /// </summary>
    [HttpGet("{observationId:guid}")]
    public async Task<ActionResult<MediaObservationDto>> Get(
        Guid observationId,
        CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserIdAsync();
        var observation = await LoadObservationAsync(observationId, userId, cancellationToken);
        if (observation is null)
        {
            return NotFound(new { error = "Media observation not found." });
        }

        return Ok(MapToDto(observation));
    }

    // -----------------------------------------------------------------------
    // Resolution actions
    // -----------------------------------------------------------------------

    /// <summary>
    /// Accept a candidate, linking the observation to the matched MediaTitle.
    /// POST /api/media/observations/{id}/resolve
    /// </summary>
    [HttpPost("{observationId:guid}/resolve")]
    public async Task<ActionResult<MediaObservationDto>> Resolve(
        Guid observationId,
        [FromBody] ResolveMediaObservationRequest request,
        CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserIdAsync();
        var observation = await LoadObservationAsync(observationId, userId, cancellationToken);
        if (observation is null)
        {
            return NotFound(new { error = "Media observation not found." });
        }

        var resolution = await ResolveTargetAsync(userId, observation, request, cancellationToken);
        if (resolution.Error is not null)
        {
            return resolution.Error;
        }

        if (resolution.MediaTitleId is null)
        {
            return BadRequest(new { error = "A candidate or provider result is required." });
        }
        var resolvedMediaTitleId = resolution.MediaTitleId.Value;

        // Clear previous acceptance state.
        foreach (var c in observation.Candidates)
        {
            c.IsAccepted = false;
        }

        if (resolution.AcceptedCandidate is not null)
        {
            resolution.AcceptedCandidate.IsAccepted = true;
        }

        var observedProgress = TryParseObservationProgress(observation, out var parsedProgress)
            ? parsedProgress
            : (int?)null;
        var resolvedProgress = observedProgress + request.EpisodeOffset;
        if (observedProgress is not null && resolvedProgress <= 0)
        {
            return BadRequest(new { error = "Episode offset resolves to a non-positive progress value." });
        }

        observation.MediaTitleId = resolution.MediaTitleId;
        observation.AcceptedCandidateId = resolution.AcceptedCandidate?.Id;
        observation.MatchStatus = MediaObservationStatuses.Matched;
        observation.ResolutionNotes = resolution.Notes;
        observation.EpisodeOffset = request.EpisodeOffset;
        observation.ResolvedProgress = resolvedProgress;
        observation.ResolvedLibraryEntryId = resolution.LibraryEntryId;
        observation.UpdatedAt = DateTimeOffset.UtcNow;

        await UpsertEpisodeOffsetAsync(userId, observation.SiteIdentifier, resolvedMediaTitleId, request.EpisodeOffset, cancellationToken);
        await EstablishManualSeasonMappingAsync(
            observation,
            resolvedMediaTitleId,
            request.EpisodeOffset,
            cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _episodeIdentityService.RecordObservationAsync(
            observation,
            cancellationToken,
            isUserConfirmed: true,
            allowTrustedIdentityRemap: true);

        _logger.LogInformation(
            "User {UserId} resolved MediaObservation {ObservationId} to MediaTitle {MediaTitleId}.",
            userId, observationId, resolution.MediaTitleId);

        // Trigger progress for the confirmed match. A task carrying a
        // progress request remains until that personal update is verifiably
        // durable; choosing a canonical title alone is not enough.
        var progressResult = await _progressService.TryEnqueueAutoProgressWithResultAsync(
            observation,
            cancellationToken);

        var response = MapToDto(observation);
        var progressHandled = observedProgress is null
            || progressResult.ProgressUpdated
            || await HasAppliedProgressAsync(observation, cancellationToken);
        if (observation.MatchStatus == MediaObservationStatuses.Matched && progressHandled)
        {
            await _lifecycleService.DeleteAfterDurableOutcomeAsync(
                observation,
                outcomeVerified: true,
                cancellationToken);
        }

        return Ok(response);
    }

    /// <summary>
    /// Reject all candidates and mark the observation as rejected.
    /// POST /api/media/observations/{id}/reject
    /// </summary>
    [HttpPost("{observationId:guid}/reject")]
    public async Task<ActionResult<MediaObservationDto>> Reject(
        Guid observationId,
        CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserIdAsync();
        var observation = await LoadObservationAsync(observationId, userId, cancellationToken);
        if (observation is null)
        {
            return NotFound(new { error = "Media observation not found." });
        }

        foreach (var c in observation.Candidates)
        {
            c.IsAccepted = false;
        }

        observation.MediaTitleId = null;
        observation.AcceptedCandidateId = null;
        observation.MatchStatus = MediaObservationStatuses.Rejected;
        observation.ResolutionNotes = "Rejected by user.";
        observation.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = MapToDto(observation);
        await _lifecycleService.DeleteAfterDurableOutcomeAsync(
            observation,
            outcomeVerified: true,
            cancellationToken);

        _logger.LogInformation(
            "User {UserId} rejected MediaObservation {ObservationId}.",
            userId, observationId);

        return Ok(response);
    }

    /// <summary>
    /// Re-run candidate generation for an observation (e.g., after new titles
    /// are imported to the library).
    /// POST /api/media/observations/{id}/retry
    /// </summary>
    [HttpPost("{observationId:guid}/retry")]
    public async Task<ActionResult<MediaObservationDto>> Retry(
        Guid observationId,
        CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserIdAsync();
        var observation = await LoadObservationAsync(observationId, userId, cancellationToken);
        if (observation is null)
        {
            return NotFound(new { error = "Media observation not found." });
        }

        // Reset to pending so the matcher starts fresh.
        observation.MatchStatus = MediaObservationStatuses.Pending;
        observation.MediaTitleId = null;
        observation.AcceptedCandidateId = null;
        observation.ResolutionNotes = null;
        observation.ProviderChoicesPayload = null;
        observation.EpisodeOffset = null;
        observation.ResolvedProgress = null;

        observation = await _matchingService.ProcessObservationAsync(observation, cancellationToken);
        var progressUpdated = false;
        if (observation.MatchStatus == MediaObservationStatuses.Matched)
        {
            // Re-run catalog recording after a rematch so untrusted provider
            // episode identities follow the newly selected canonical season.
            await _episodeIdentityService.RecordObservationAsync(observation, cancellationToken);
            progressUpdated = (await _progressService.TryEnqueueAutoProgressWithResultAsync(
                observation,
                cancellationToken)).ProgressUpdated;
        }
        await EnsureProviderChoicesAsync(observation, cancellationToken);
        var response = MapToDto(observation);
        await DeleteCompletedObservationIfSafeAsync(observation, progressUpdated, cancellationToken);
        return Ok(response);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task EstablishManualSeasonMappingAsync(
        MediaObservation observation,
        Guid mediaTitleId,
        int episodeOffset,
        CancellationToken cancellationToken)
    {
        await _seasonMappingService.EstablishAsync(
            observation.SiteIdentifier,
            observation.ProviderSeriesId,
            observation.ProviderSeasonId,
            observation.SeasonNumber,
            mediaTitleId,
            episodeOffset,
            MediaProviderSeasonMappingSources.Manual,
            1m,
            cancellationToken);
    }

    private async Task<MediaObservation?> LoadObservationAsync(
        Guid observationId,
        int userId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.MediaObservations
            .Include(o => o.Candidates)
            .Include(o => o.Episodes)
            .Include(o => o.MediaTitle)
            .FirstOrDefaultAsync(
                o => o.Id == observationId && o.UserId == userId,
                cancellationToken);
    }

    private static SubmitMediaObservationResponse MapToSubmitResponse(
        MediaObservation observation,
        bool wasDeduplicated,
        string? providerChoicesUnavailableReason = null,
        bool progressUpdated = false)
    {
        var dto = MapToDto(observation);
        var requiresResolution = observation.MatchStatus is MediaObservationStatuses.Ambiguous or MediaObservationStatuses.NoMatch;
        return new SubmitMediaObservationResponse
        {
            ObservationId = observation.Id.ToString(),
            MatchStatus = observation.MatchStatus,
            WasDeduplicated = wasDeduplicated,
            MatchedMediaTitleId = observation.MediaTitleId?.ToString(),
            MatchedTitle = observation.MediaTitle?.CanonicalTitle,
            CandidateCount = observation.Candidates.Count,
            RequiresResolution = requiresResolution,
            ObservedProgress = dto.ObservedProgress,
            SuggestedEpisodeOffset = observation.EpisodeOffset ?? 0,
            ResolvedProgress = observation.ResolvedProgress,
            ProgressUpdated = progressUpdated,
            Observation = requiresResolution || wasDeduplicated ? dto : null,
            ProviderChoices = dto.ProviderChoices,
            ProviderChoicesUnavailableReason = providerChoicesUnavailableReason
        };
    }

    private static MediaObservationDto MapToDto(MediaObservation observation)
    {
        return new MediaObservationDto
        {
            ObservationId = observation.Id.ToString(),
            SiteIdentifier = observation.SiteIdentifier,
            ObservedUrl = observation.ObservedUrl,
            SiteMediaId = observation.SiteMediaId,
            ObservedTitle = observation.ObservedTitle,
            ProgressHint = observation.ProgressHint,
            ObservedAt = observation.ObservedAt,
            ExtensionVersion = null,
            MatchStatus = observation.MatchStatus,
            MediaTitleId = observation.MediaTitleId?.ToString(),
            ResolutionNotes = observation.ResolutionNotes,
            ObservedProgress = TryParseObservationProgress(observation, out var observedProgress) ? observedProgress : null,
            EpisodeOffset = observation.EpisodeOffset,
            ResolvedProgress = observation.ResolvedProgress,
            ResolvedLibraryEntryId = observation.ResolvedLibraryEntryId?.ToString(),
            MatchAttemptCount = observation.MatchAttemptCount,
            LastMatchAttemptedAt = observation.LastMatchAttemptedAt,
            LastMatchError = observation.LastMatchError,
            CreatedAt = observation.CreatedAt,
            Candidates = observation.Candidates
                .OrderByDescending(c => c.Score)
                .Select(c => new MediaObservationCandidateDto
                {
                    CandidateId = c.Id.ToString(),
                    CandidateSource = c.CandidateSource,
                    MediaTitleId = c.MediaTitleId.ToString(),
                    Provider = c.Provider,
                    ProviderMediaId = c.ProviderMediaId,
                    Title = c.Title,
                    MediaKind = c.MediaKind,
                    Score = c.Score,
                    Explanation = c.Explanation,
                    IsAccepted = c.IsAccepted
                })
                .ToList(),
            ProviderChoices = DeserializeProviderChoices(observation.ProviderChoicesPayload)
        };
    }

    private async Task<string?> EnsureProviderChoicesAsync(MediaObservation observation, CancellationToken cancellationToken)
    {
        if (observation.MatchStatus is not (MediaObservationStatuses.Ambiguous or MediaObservationStatuses.NoMatch)
            || !string.IsNullOrWhiteSpace(observation.ProviderChoicesPayload)
            || !_mediaProviderRegistry.IsSupported(MediaObservationSiteIdentifiers.AniList))
        {
            return null;
        }

        var query = GetProviderSearchQuery(observation);
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var provider = _mediaProviderRegistry.GetRequired(MediaObservationSiteIdentifiers.AniList);
        if (await provider.GetConnectedAccountAsync(observation.UserId, cancellationToken) is null)
        {
            _logger.LogInformation(
                "Skipping provider choices for MediaObservation {ObservationId}: provider {ProviderId} is not connected.",
                observation.Id,
                provider.ProviderId);
            return "anilist_not_connected";
        }

        IReadOnlyList<MediaProviderSearchResult> results;
        try
        {
            results = await provider.SearchAsync(
                observation.UserId,
                new MediaCatalogSearchRequest
                {
                    Query = query,
                    MediaKinds = [MediaKinds.Anime],
                    Limit = 6
                },
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Provider choice search failed for MediaObservation {ObservationId} using provider {ProviderId}.",
                observation.Id,
                provider.ProviderId);
            return "provider_search_failed";
        }

        if (await RefreshKnownTitleSynonymsAsync(provider.ProviderId, results, cancellationToken))
        {
            observation = await _matchingService.ProcessObservationAsync(observation, cancellationToken);
            if (observation.MatchStatus == MediaObservationStatuses.Matched)
            {
                observation.ProviderChoicesPayload = null;
                await _dbContext.SaveChangesAsync(cancellationToken);
                return null;
            }
        }

        var choices = await MapProviderChoicesAsync(observation.UserId, provider.ProviderId, results, cancellationToken);
        observation.ProviderChoicesPayload = JsonSerializer.Serialize(choices, PayloadJsonOptions);
        observation.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    private async Task<bool> RefreshKnownTitleSynonymsAsync(
        string providerId,
        IReadOnlyCollection<MediaProviderSearchResult> results,
        CancellationToken cancellationToken)
    {
        var synonymsByProviderMediaId = results
            .Where(result => result.Synonyms.Count > 0)
            .GroupBy(result => result.ProviderMediaId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .SelectMany(result => result.Synonyms)
                    .Where(synonym => !string.IsNullOrWhiteSpace(synonym))
                    .Select(synonym => synonym.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                StringComparer.Ordinal);
        if (synonymsByProviderMediaId.Count == 0)
        {
            return false;
        }

        var providerMediaIds = synonymsByProviderMediaId.Keys.ToList();
        var links = await _dbContext.MediaProviderLinks
            .Include(link => link.MediaTitle)
            .Where(link => link.Provider == providerId
                && providerMediaIds.Contains(link.ExternalId))
            .ToListAsync(cancellationToken);
        var changed = false;
        foreach (var link in links)
        {
            if (link.MediaTitle is not { } title
                || !synonymsByProviderMediaId.TryGetValue(link.ExternalId, out var synonyms)
                || title.Synonyms.SequenceEqual(synonyms, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            title.Synonyms = synonyms;
            title.UpdatedAt = DateTimeOffset.UtcNow;
            changed = true;
        }

        if (changed)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return changed;
    }

    private async Task<List<MediaObservationProviderChoiceDto>> MapProviderChoicesAsync(
        int userId,
        string providerId,
        IEnumerable<MediaProviderSearchResult> results,
        CancellationToken cancellationToken)
    {
        var materialized = results.ToList();
        var providerMediaIds = materialized.Select(result => result.ProviderMediaId).Distinct(StringComparer.Ordinal).ToList();
        var libraryEntries = await _dbContext.MediaLibraryProviderBindings
            .AsNoTracking()
            .Include(binding => binding.MediaLibraryEntry)
            .Include(binding => binding.MediaProviderLink)
            .Where(binding => binding.MediaLibraryEntry!.UserId == userId
                && binding.MediaProviderLink!.Provider == providerId
                && providerMediaIds.Contains(binding.MediaProviderLink.ExternalId))
            .ToListAsync(cancellationToken);
        var entryByProviderId = libraryEntries
            .OrderByDescending(binding => binding.ConnectedServiceAccountId is not null)
            .ThenByDescending(binding => binding.UpdatedAt)
            .GroupBy(binding => binding.MediaProviderLink!.ExternalId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        return materialized.Select(result =>
        {
            entryByProviderId.TryGetValue(result.ProviderMediaId, out var entry);
            return new MediaObservationProviderChoiceDto
            {
                ProviderId = result.ProviderId,
                ProviderMediaId = result.ProviderMediaId,
                Title = result.Title,
                NativeTitle = result.NativeTitle,
                MediaKind = result.MediaKind,
                PosterUrl = result.PosterUrl,
                BackgroundUrl = result.BackgroundUrl,
                StartYear = result.StartYear,
                EpisodeCount = result.EpisodeCount,
                ChapterCount = result.ChapterCount,
                VolumeCount = result.VolumeCount,
                PrimaryProgressDimension = result.PrimaryProgressDimension,
                IsInLibrary = entry is not null,
                LibraryEntryId = entry?.MediaLibraryEntryId.ToString(),
                MediaTitleId = entry?.MediaLibraryEntry?.MediaTitleId.ToString()
            };
        }).ToList();
    }

    private async Task<ResolveTargetResult> ResolveTargetAsync(
        int userId,
        MediaObservation observation,
        ResolveMediaObservationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.CandidateId is { } candidateId)
        {
            var candidate = observation.Candidates.FirstOrDefault(c => c.Id == candidateId);
            if (candidate is null)
            {
                return ResolveTargetResult.FromError(BadRequest(new { error = "Candidate does not belong to this observation." }));
            }

            var libraryEntryId = await _dbContext.MediaLibraryEntries
                .AsNoTracking()
                .Where(entry => entry.UserId == userId && entry.MediaTitleId == candidate.MediaTitleId)
                .Select(entry => (Guid?)entry.Id)
                .FirstOrDefaultAsync(cancellationToken);

            return new ResolveTargetResult(
                candidate.MediaTitleId,
                candidate,
                libraryEntryId,
                candidate.Provider,
                candidate.ProviderMediaId,
                candidate.Title,
                "Resolved by user candidate selection.",
                null);
        }

        if (string.IsNullOrWhiteSpace(request.ProviderId) || string.IsNullOrWhiteSpace(request.ProviderMediaId))
        {
            return ResolveTargetResult.FromError(BadRequest(new { error = "A candidate or provider result is required." }));
        }

        return await ResolveProviderTargetAsync(userId, request, cancellationToken);
    }

    private async Task<ResolveTargetResult> ResolveProviderTargetAsync(
        int userId,
        ResolveMediaObservationRequest request,
        CancellationToken cancellationToken)
    {
        var providerId = request.ProviderId!.Trim().ToLowerInvariant();
        var providerMediaId = request.ProviderMediaId!.Trim();
        if (!_mediaProviderRegistry.IsSupported(providerId))
        {
            return ResolveTargetResult.FromError(NotFound(new { error = $"Media provider '{providerId}' is not implemented" }));
        }

        var existingBinding = await _dbContext.MediaLibraryProviderBindings
            .AsNoTracking()
            .Include(binding => binding.MediaLibraryEntry)
            .Include(binding => binding.MediaProviderLink)
            .FirstOrDefaultAsync(binding => binding.MediaLibraryEntry!.UserId == userId
                && binding.MediaProviderLink!.Provider == providerId
                && binding.MediaProviderLink.ExternalId == providerMediaId, cancellationToken);

        if (existingBinding?.MediaLibraryEntry is { } existingEntry)
        {
            return new ResolveTargetResult(
                existingEntry.MediaTitleId,
                null,
                existingEntry.Id,
                providerId,
                providerMediaId,
                null,
                "Resolved by user provider selection.",
                null);
        }

        if (!request.AddToLibraryConfirmed)
        {
            return ResolveTargetResult.FromError(Conflict(new
            {
                error = "Selected title is not in the media library.",
                requiresAddToLibraryConfirmation = true
            }));
        }

        var addResult = await AddProviderTitleToLibraryAsync(userId, providerId, providerMediaId, cancellationToken);
        if (addResult.Error is not null)
        {
            return ResolveTargetResult.FromError(addResult.Error);
        }

        return new ResolveTargetResult(
            addResult.MediaTitleId,
            null,
            addResult.LibraryEntryId,
            providerId,
            providerMediaId,
            addResult.Title,
            "Resolved by user provider selection and added to library.",
            null);
    }

    private async Task<AddProviderTitleResult> AddProviderTitleToLibraryAsync(
        int userId,
        string providerId,
        string providerMediaId,
        CancellationToken cancellationToken)
    {
        var provider = _mediaProviderRegistry.GetRequired(providerId);
        var account = await provider.GetConnectedAccountAsync(userId, cancellationToken);
        if (account is null)
        {
            return AddProviderTitleResult.FromError(BadRequest(new { error = $"{provider.ProviderId} is not connected." }));
        }

        var details = await provider.GetTitleDetailsAsync(userId, providerMediaId, cancellationToken);
        if (details is null)
        {
            return AddProviderTitleResult.FromError(NotFound(new { error = "Provider title not found." }));
        }

        var mutationResult = await provider.UpdateStatusAsync(
            userId,
            new MediaStatusUpdateRequest
            {
                ProviderMediaId = providerMediaId,
                Status = MediaLibraryStatuses.Current
            },
            cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var title = await FindOrCreateMediaTitleAsync(userId, provider.ProviderId, providerMediaId, details, now, cancellationToken);
        var entry = new MediaLibraryEntry
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            MediaTitleId = title.Id,
            Status = MediaLibraryStatuses.Current,
            ProgressEpisodes = details.PrimaryProgressDimension == MediaProgressDimensions.Episode ? 0 : null,
            ProgressChapters = details.PrimaryProgressDimension == MediaProgressDimensions.Chapter ? 0 : null,
            ProgressVolumes = details.PrimaryProgressDimension == MediaProgressDimensions.Volume ? 0 : null,
            LastLocalEditAt = now,
            LastMutationSource = MediaMutationSources.UserStatusUpdate,
            CreatedAt = now,
            UpdatedAt = now
        };

        _dbContext.MediaLibraryEntries.Add(entry);
        var providerLink = _dbContext.MediaProviderLinks.Local.FirstOrDefault(
                link => link.Provider == provider.ProviderId && link.ExternalId == providerMediaId)
            ?? await _dbContext.MediaProviderLinks.SingleAsync(
                link => link.Provider == provider.ProviderId && link.ExternalId == providerMediaId,
                cancellationToken);
        _dbContext.MediaLibraryProviderBindings.Add(new MediaLibraryProviderBinding
        {
            Id = Guid.NewGuid(),
            MediaLibraryEntryId = entry.Id,
            MediaProviderLinkId = providerLink.Id,
            ConnectedServiceAccountId = account.Id,
            ProviderAccountId = account.ExternalAccountId,
            LastSyncedAt = now,
            LastRemoteUpdateAt = mutationResult.LastRemoteUpdateAt ?? now,
            CreatedAt = now,
            UpdatedAt = now
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new AddProviderTitleResult(entry.Id, title.Id, title.CanonicalTitle, null);
    }

    private async Task<MediaTitle> FindOrCreateMediaTitleAsync(
        int userId,
        string providerId,
        string providerMediaId,
        MediaProviderTitleDetails details,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existingLink = await _dbContext.MediaProviderLinks
            .Include(link => link.MediaTitle)
                .ThenInclude(title => title!.ProviderLinks)
            .FirstOrDefaultAsync(link => link.Provider == providerId && link.ExternalId == providerMediaId, cancellationToken);

        if (existingLink?.MediaTitle is { } linkedTitle)
        {
            if (MediaCanonicalMetadataPolicy.ShouldApply(
                    providerId,
                    linkedTitle.ProviderLinks.Select(link => link.Provider)))
            {
                ApplyProviderDetails(linkedTitle, details, now);
            }
            await PersistProviderCrossReferencesAsync(linkedTitle, details.CrossReferences, now, cancellationToken);
            existingLink.AvailabilitySnapshot = MediaProviderAvailabilitySnapshotCodec.Serialize(details.AvailabilityLinks);
            existingLink.AvailabilityLastVerifiedAt = now;
            existingLink.LastVerifiedAt = now;
            existingLink.UpdatedAt = now;
            return linkedTitle;
        }

        var crossReferenceAnchors = new List<MediaProviderLink>();
        foreach (var reference in details.CrossReferences)
        {
            var provider = reference.ProviderId.Trim().ToLowerInvariant();
            var mediaId = reference.ProviderMediaId.Trim();
            var crossReference = await _dbContext.MediaProviderLinks
                .Include(link => link.MediaTitle)
                    .ThenInclude(title => title!.ProviderLinks)
                .FirstOrDefaultAsync(
                    link => link.Provider == provider && link.ExternalId == mediaId,
                    cancellationToken);
            if (crossReference is not null)
            {
                var conflictingPrimary = _dbContext.MediaProviderLinks.Local.FirstOrDefault(
                        link => link.MediaTitleId == crossReference.MediaTitleId && link.Provider == providerId)
                    ?? await _dbContext.MediaProviderLinks.FirstOrDefaultAsync(
                        link => link.MediaTitleId == crossReference.MediaTitleId && link.Provider == providerId,
                        cancellationToken);
                if (conflictingPrimary is not null && conflictingPrimary.ExternalId != providerMediaId)
                {
                    _logger.LogWarning(
                        "Cross-reference anchor MediaTitle {MediaTitleId} already has provider link {Provider}/{ExistingProviderMediaId}; refusing requested {RequestedProviderMediaId}.",
                        crossReference.MediaTitleId,
                        providerId,
                        conflictingPrimary.ExternalId,
                        providerMediaId);
                    continue;
                }
            }
            if (crossReference is not null
                && crossReferenceAnchors.All(anchor => anchor.MediaTitleId != crossReference.MediaTitleId))
            {
                crossReferenceAnchors.Add(crossReference);
            }
        }

        MediaTitle title;
        if (crossReferenceAnchors.Count == 1 && crossReferenceAnchors[0].MediaTitle is { } anchoredTitle)
        {
            title = anchoredTitle;
            if (MediaCanonicalMetadataPolicy.ShouldApply(
                    providerId,
                    title.ProviderLinks.Select(link => link.Provider)))
            {
                ApplyProviderDetails(title, details, now);
            }
        }
        else
        {
            title = new MediaTitle
            {
                Id = Guid.NewGuid(),
                CanonicalTitle = details.Title,
                SortTitle = details.Title,
                OriginalTitle = details.NativeTitle,
                Synonyms = [.. details.Synonyms],
                MediaKind = details.MediaKind,
                Synopsis = details.Synopsis,
                Format = details.Format,
                PosterUrl = details.PosterUrl,
                BackgroundUrl = details.BackgroundUrl,
                StartYear = details.StartYear,
                EpisodeCount = details.EpisodeCount,
                ChapterCount = details.ChapterCount,
                VolumeCount = details.VolumeCount,
                ReleasedCount = details.ReleasedCount,
                TotalKnownCount = details.TotalKnownCount,
                NextReleaseAt = details.NextReleaseAt,
                NextReleaseLabel = details.NextReleaseLabel,
                SupportsEpisodeProgress = details.PrimaryProgressDimension == MediaProgressDimensions.Episode,
                SupportsChapterProgress = details.PrimaryProgressDimension == MediaProgressDimensions.Chapter,
                SupportsVolumeProgress = details.PrimaryProgressDimension == MediaProgressDimensions.Volume,
                IsCompletionOnly = details.PrimaryProgressDimension == MediaProgressDimensions.CompletionOnly,
                PrimaryProgressDimension = details.PrimaryProgressDimension,
                ReleaseStatusDimension = details.ReleaseStatusDimension,
                CreatedAt = now,
                UpdatedAt = now
            };
            _dbContext.MediaTitles.Add(title);
        }

        _dbContext.MediaProviderLinks.Add(new MediaProviderLink
        {
            Id = Guid.NewGuid(),
            MediaTitleId = title.Id,
            Provider = providerId,
            ExternalId = providerMediaId,
            LinkSource = MediaMappingSources.UserConfirmed,
            LinkedByUserId = userId,
            LastVerifiedAt = now,
            AvailabilitySnapshot = MediaProviderAvailabilitySnapshotCodec.Serialize(details.AvailabilityLinks),
            AvailabilityLastVerifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });
        await PersistProviderCrossReferencesAsync(title, details.CrossReferences, now, cancellationToken);

        return title;
    }

    private async Task PersistProviderCrossReferencesAsync(
        MediaTitle title,
        IReadOnlyList<MediaProviderCrossReference> crossReferences,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        foreach (var reference in crossReferences)
        {
            var provider = reference.ProviderId.Trim().ToLowerInvariant();
            var mediaId = reference.ProviderMediaId.Trim();
            if (provider.Length == 0 || mediaId.Length == 0)
            {
                continue;
            }

            var link = _dbContext.MediaProviderLinks.Local.FirstOrDefault(
                    candidate => candidate.Provider == provider && candidate.ExternalId == mediaId)
                ?? await _dbContext.MediaProviderLinks.FirstOrDefaultAsync(
                    candidate => candidate.Provider == provider && candidate.ExternalId == mediaId,
                    cancellationToken);
            if (link is null)
            {
                var conflictingProviderLink = _dbContext.MediaProviderLinks.Local.FirstOrDefault(
                        candidate => candidate.MediaTitleId == title.Id && candidate.Provider == provider)
                    ?? await _dbContext.MediaProviderLinks.FirstOrDefaultAsync(
                        candidate => candidate.MediaTitleId == title.Id && candidate.Provider == provider,
                        cancellationToken);
                if (conflictingProviderLink is not null)
                {
                    _logger.LogWarning(
                        "MediaTitle {MediaTitleId} already has provider link {Provider}/{ExistingProviderMediaId}; refusing cross-reference {RequestedProviderMediaId}.",
                        title.Id,
                        provider,
                        conflictingProviderLink.ExternalId,
                        mediaId);
                    continue;
                }

                _dbContext.MediaProviderLinks.Add(new MediaProviderLink
                {
                    Id = Guid.NewGuid(),
                    MediaTitleId = title.Id,
                    Provider = provider,
                    ExternalId = mediaId,
                    ExternalUrl = reference.ExternalUrl,
                    Confidence = 1m,
                    LinkSource = MediaMappingSources.Imported,
                    LastVerifiedAt = now,
                    CreatedAt = now,
                    UpdatedAt = now,
                    MediaTitle = title
                });
                continue;
            }

            if (link.MediaTitleId != title.Id)
            {
                _logger.LogWarning(
                    "Provider cross-reference {Provider}/{ProviderMediaId} already belongs to MediaTitle {ExistingTitleId}; refusing to move it to {RequestedTitleId}.",
                    provider,
                    mediaId,
                    link.MediaTitleId,
                    title.Id);
                continue;
            }

            link.ExternalUrl = reference.ExternalUrl ?? link.ExternalUrl;
            link.LastVerifiedAt = now;
            link.UpdatedAt = now;
        }
    }

    private static void ApplyProviderDetails(MediaTitle title, MediaProviderTitleDetails details, DateTimeOffset now)
    {
        title.CanonicalTitle = details.Title;
        title.SortTitle = details.Title;
        title.OriginalTitle = details.NativeTitle ?? title.OriginalTitle;
        title.Synonyms = [.. details.Synonyms];
        title.MediaKind = details.MediaKind;
        title.Synopsis = details.Synopsis ?? title.Synopsis;
        title.Format = details.Format ?? title.Format;
        title.PosterUrl = details.PosterUrl ?? title.PosterUrl;
        title.BackgroundUrl = details.BackgroundUrl ?? title.BackgroundUrl;
        title.StartYear = details.StartYear ?? title.StartYear;
        title.EpisodeCount = details.EpisodeCount ?? title.EpisodeCount;
        title.ChapterCount = details.ChapterCount ?? title.ChapterCount;
        title.VolumeCount = details.VolumeCount ?? title.VolumeCount;
        title.ReleasedCount = details.ReleasedCount ?? title.ReleasedCount;
        title.TotalKnownCount = details.TotalKnownCount ?? title.TotalKnownCount;
        title.NextReleaseAt = details.NextReleaseAt;
        title.NextReleaseLabel = details.NextReleaseLabel;
        title.SupportsEpisodeProgress = details.PrimaryProgressDimension == MediaProgressDimensions.Episode;
        title.SupportsChapterProgress = details.PrimaryProgressDimension == MediaProgressDimensions.Chapter;
        title.SupportsVolumeProgress = details.PrimaryProgressDimension == MediaProgressDimensions.Volume;
        title.IsCompletionOnly = details.PrimaryProgressDimension == MediaProgressDimensions.CompletionOnly;
        title.PrimaryProgressDimension = details.PrimaryProgressDimension;
        title.ReleaseStatusDimension = details.ReleaseStatusDimension;
        title.UpdatedAt = now;
    }

    private async Task ApplyStoredOffsetAsync(MediaObservation observation, CancellationToken cancellationToken)
    {
        if (observation.MediaTitleId is null || !TryParseObservationProgress(observation, out var observedProgress))
        {
            return;
        }

        var seasonMapping = await _seasonMappingService.FindAsync(
            observation.SiteIdentifier,
            observation.ProviderSeriesId,
            observation.ProviderSeasonId,
            observation.SeasonNumber,
            cancellationToken);
        if (seasonMapping is not null && seasonMapping.MediaTitleId == observation.MediaTitleId)
        {
            observation.EpisodeOffset = seasonMapping.EpisodeOffset;
            observation.ResolvedProgress = Math.Max(1, observedProgress + seasonMapping.EpisodeOffset);
            observation.UpdatedAt = DateTimeOffset.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var offset = await _dbContext.MediaObservationEpisodeOffsets
            .AsNoTracking()
            .FirstOrDefaultAsync(item =>
                item.UserId == observation.UserId
                && item.SiteIdentifier == observation.SiteIdentifier
                && item.MediaTitleId == observation.MediaTitleId,
                cancellationToken);
        if (offset is null)
        {
            var inferredOffset = observation.EpisodeOffset ?? 0;
            observation.ResolvedProgress = Math.Max(1, observedProgress + inferredOffset);
            observation.EpisodeOffset = inferredOffset;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        observation.EpisodeOffset = offset.EpisodeOffset;
        observation.ResolvedProgress = Math.Max(1, observedProgress + offset.EpisodeOffset);
        observation.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void ApplyStructuredPayload(
        MediaObservation observation,
        SubmitMediaObservationRequest request,
        bool isCatalogObservation)
    {
        observation.SeriesTitle = FirstNonBlank(request.SeriesTitle);
        observation.EpisodeTitle = FirstNonBlank(request.EpisodeTitle);
        observation.EpisodeNumber = request.EpisodeNumber;
        observation.SeasonTitle = FirstNonBlank(request.SeasonTitle);
        observation.SeasonNumber = request.SeasonNumber;
        observation.ProviderSeriesId = FirstNonBlank(request.ProviderSeriesId);
        observation.ProviderSeasonId = FirstNonBlank(request.ProviderSeasonId);
        observation.ProviderSequenceNumber = request.ProviderSequenceNumber;
        observation.ReleaseTrack = FirstNonBlank(request.ReleaseTrack);
        observation.NextEpisodeProviderId = FirstNonBlank(request.NextEpisodeProviderId);
        observation.NextEpisodeUrl = string.IsNullOrWhiteSpace(request.NextEpisodeUrl)
            ? null
            : MediaDestinationUrlPolicy.NormalizeObservedUrl(request.NextEpisodeUrl);
        observation.NextEpisodeTitle = FirstNonBlank(request.NextEpisodeTitle);
        observation.NextEpisodeNumber = request.NextEpisodeNumber;
        observation.NextEpisodeReleaseTrack = FirstNonBlank(request.NextEpisodeReleaseTrack);
        observation.IsCatalogObservation = isCatalogObservation;

        observation.Episodes.Clear();
        foreach (var episode in request.ObservedEpisodes
                     .Where(item => item is not null)
                     .Take(MediaCatalogObservationLimits.MaximumEpisodesPerObservation))
        {
            observation.Episodes.Add(new MediaObservationEpisode
            {
                Id = Guid.NewGuid(),
                ProviderEpisodeId = episode.ProviderEpisodeId.Trim(),
                ProviderUrl = MediaDestinationUrlPolicy.NormalizeObservedUrl(episode.ProviderUrl),
                EpisodeNumber = episode.EpisodeNumber,
                EpisodeTitle = FirstNonBlank(episode.EpisodeTitle),
                ReleaseTrack = FirstNonBlank(episode.ReleaseTrack),
                AvailableSubtitleLanguageCodes = NormalizeLanguageCodes(episode.AvailableSubtitleLanguageCodes),
                AvailableAudioLanguageCodes = NormalizeLanguageCodes(episode.AvailableAudioLanguageCodes)
            });
        }
    }

    private static List<string> NormalizeLanguageCodes(IEnumerable<string>? values) =>
        values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

    private async Task UpsertEpisodeOffsetAsync(
        int userId,
        string siteIdentifier,
        Guid mediaTitleId,
        int episodeOffset,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.MediaObservationEpisodeOffsets
            .FirstOrDefaultAsync(item =>
                item.UserId == userId
                && item.SiteIdentifier == siteIdentifier
                && item.MediaTitleId == mediaTitleId,
                cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (existing is null)
        {
            _dbContext.MediaObservationEpisodeOffsets.Add(new MediaObservationEpisodeOffset
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SiteIdentifier = siteIdentifier,
                MediaTitleId = mediaTitleId,
                EpisodeOffset = episodeOffset,
                CreatedAt = now,
                UpdatedAt = now
            });
            return;
        }

        existing.EpisodeOffset = episodeOffset;
        existing.UpdatedAt = now;
    }

    private static string? GetProviderSearchQuery(MediaObservation observation)
    {
        return FirstNonBlank(observation.SeriesTitle, observation.ObservedTitle);
    }

    private static bool TryParseObservationProgress(MediaObservation observation, out int progress)
    {
        return MediaObservationProgressService.TryParseProgressHint(observation.ProgressHint, out progress);
    }

    private static List<MediaObservationProviderChoiceDto> DeserializeProviderChoices(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<MediaObservationProviderChoiceDto>>(payload, PayloadJsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? FirstNonBlank(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    private async Task<int> GetCurrentUserIdAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            throw new UnauthorizedAccessException("User not authenticated.");
        }

        return user.Id;
    }

    private sealed record ResolveTargetResult(
        Guid? MediaTitleId,
        MediaObservationCandidate? AcceptedCandidate,
        Guid? LibraryEntryId,
        string? ProviderId,
        string? ProviderMediaId,
        string? Title,
        string Notes,
        ActionResult? Error)
    {
        public static ResolveTargetResult FromError(ActionResult error) => new(null, null, null, null, null, null, string.Empty, error);
    }

    private sealed record AddProviderTitleResult(Guid LibraryEntryId, Guid MediaTitleId, string Title, ActionResult? Error)
    {
        public static AddProviderTitleResult FromError(ActionResult error) => new(Guid.Empty, Guid.Empty, string.Empty, error);
    }

}
