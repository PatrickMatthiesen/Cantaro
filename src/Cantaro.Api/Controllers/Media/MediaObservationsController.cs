using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Controllers;

/// <summary>
/// Ingests extension observations, records shared provider facts, and manages
/// the current user's matching tasks and watch progress.
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
    MediaLibraryEventHub mediaLibraryEventHub,
    ILogger<MediaObservationsController> logger,
    MediaCatalogCanonicalizationService catalogService,
    MediaProviderOperationProcessor operationProcessor,
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
    private readonly MediaLibraryEventHub _mediaLibraryEventHub = mediaLibraryEventHub;
    private readonly ILogger<MediaObservationsController> _logger = logger;
    private readonly MediaCatalogCanonicalizationService _catalogService = catalogService;
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
            : MediaProviderSeasonMappingService.IsSupported(normalizedSite)
                ? MediaProviderSeasonMappingService.NormalizeId(request.SiteMediaId)
                : request.SiteMediaId.Trim();
        var progressHint = string.IsNullOrWhiteSpace(request.ProgressHint)
            ? request.EpisodeNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : request.ProgressHint.Trim();
        var now = DateTimeOffset.UtcNow;
        var observedAt = MediaObservationLifecycleService.NormalizeObservedAt(request.ObservedAt, now);
        var normalizedObservedUrl = MediaDestinationUrlPolicy.NormalizeObservedUrl(request.ObservedUrl);

        var catalogOnly = request.ObservedEpisodes.Count > 0 &&
            MediaDestinationUrlPolicy.TryNormalizeSeriesUrl(normalizedSite, normalizedObservedUrl, request.ProviderSeriesId, out _);
        if (MediaProviderSeasonMappingService.IsSupported(normalizedSite))
        {
            // Commit validated provider facts before competing for the private observation.
            var evidence = CreateObservation();
            ApplyStructuredPayload(evidence, request, catalogOnly);
            await _episodeIdentityService.RecordObservationAsync(evidence, cancellationToken);
        }

        const int maxAttempts = 3;
        var progressUpdated = false;
        var retryingAfterSaveConflict = false;
        var incomingEvidenceSaved = false;
        Guid? conflictedObservationId = null;
        for (var attempt = 1; ; attempt++)
        {
            var observation = normalizedSiteMediaId is not null ? await FindExistingAsync()
                : conflictedObservationId is { } id ? await LoadObservationAsync(id, userId, cancellationToken)
                : null;
            if (observation is not null && MediaObservationLifecycleService.ShouldKeepPersistedEvidence(
                observation.ObservedAt, observedAt, retryingAfterSaveConflict, incomingEvidenceSaved))
            {
                // Replaying this request would replace the evidence that won the update.
                return Ok(MapToSubmitResponse(observation, wasDeduplicated: true, progressUpdated: progressUpdated));
            }
            var wasDeduplicated = observation is not null;
            if (observation is null)
            {
                observation = CreateObservation();
                _dbContext.MediaObservations.Add(observation);
            }

            var saved = false;
            try
            {
                var preserveMatch = await SaveSubmissionAsync(observation, wasDeduplicated);
                saved = true;
                if (!preserveMatch)
                {
                    await _matchingService.ProcessObservationAsync(observation, cancellationToken);
                }
                var providerChoicesUnavailableReason = await EnsureProviderChoicesAsync(observation, cancellationToken);
                var progress = await _progressService.TryEnqueueAutoProgressWithResultAsync(observation, cancellationToken);
                progressUpdated |= progress.ProgressUpdated;
                var deleted = await DeleteCompletedObservationIfSafeAsync(observation, progress, cancellationToken);
                return Ok(MapToSubmitResponse(observation, wasDeduplicated, providerChoicesUnavailableReason,
                    progressUpdated, deleted));
            }
            catch (DbUpdateException exception) when (attempt < maxAttempts && !saved && !wasDeduplicated
                && normalizedSiteMediaId is not null && _lifecycleService.IsDuplicateObservationInsert(exception))
            {
                retryingAfterSaveConflict = true;
                _lifecycleService.DetachSubmission(observation);
            }
            catch (DbUpdateConcurrencyException exception) when ((wasDeduplicated || saved)
                && MediaObservationLifecycleService.IsSubmissionConcurrency(exception, observation))
            {
                if (attempt == maxAttempts)
                {
                    return ObservationUpdatedConflict();
                }
                retryingAfterSaveConflict = true;
                incomingEvidenceSaved |= saved;
                conflictedObservationId = observation.Id;
                _lifecycleService.DetachSubmission(observation);
            }
            catch (DbUpdateException exception) when (attempt < maxAttempts && normalizedSiteMediaId is not null
                && (wasDeduplicated || saved)
                && _lifecycleService.IsSubmissionForeignKeyViolation(exception, observation.Id))
            {
                // A child insert can reach the database before the parent update.
                if (await _dbContext.MediaObservations.AnyAsync(item => item.Id == observation.Id, cancellationToken))
                {
                    throw;
                }
                retryingAfterSaveConflict = true;
                incomingEvidenceSaved |= saved;
                _lifecycleService.DetachSubmission(observation);
            }
        }

        MediaObservation CreateObservation() => new()
        {
            Id = Guid.NewGuid(), UserId = userId, SiteIdentifier = normalizedSite,
            SiteMediaId = normalizedSiteMediaId, ObservedTitle = request.ObservedTitle.Trim(),
            ObservedUrl = normalizedObservedUrl, MatchStatus = MediaObservationStatuses.Pending,
            ObservedAt = observedAt, CreatedAt = now
        };

        Task<MediaObservation?> FindExistingAsync() => _dbContext.MediaObservations
            .Include(item => item.Candidates).Include(item => item.Episodes).Include(item => item.MediaTitle)
            .FirstOrDefaultAsync(item => item.UserId == userId && item.SiteIdentifier == normalizedSite
                && item.SiteMediaId == normalizedSiteMediaId, cancellationToken);

        async Task<bool> SaveSubmissionAsync(MediaObservation target, bool deduplicated)
        {
            var previousQuery = GetProviderSearchQuery(target);
            target.ObservedUrl = normalizedObservedUrl;
            target.ObservedTitle = request.ObservedTitle.Trim();
            target.ProgressHint = progressHint;
            target.ObservedAt = observedAt;
            target.Touch(now);
            ApplyStructuredPayload(target, request, catalogOnly);
            if (!string.Equals(previousQuery, GetProviderSearchQuery(target), StringComparison.Ordinal))
            {
                target.ProviderChoicesPayload = null;
            }

            var keepMatch = deduplicated && target.MatchStatus == MediaObservationStatuses.Matched
                && !MediaProviderSeasonMappingService.IsSupported(target.SiteIdentifier);
            if (keepMatch)
            {
                var title = target.MediaTitleId is { } titleId
                    ? await _dbContext.MediaTitles.FindAsync([titleId], cancellationToken) : null;
                var resolved = TryParseObservationProgress(target, out var observedProgress)
                    ? (long?)observedProgress + (target.EpisodeOffset ?? 0) : null;
                target.ResolvedProgress = title is { IsCompletionOnly: false }
                    && MediaObservationMatchingService.IsCanonicalEpisodeNumber(title, resolved)
                    ? (int?)resolved : null;
            }
            else
            {
                target.ResetMatch();
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            return keepMatch;
        }
    }

    private async Task<bool> DeleteCompletedObservationIfSafeAsync(
        MediaObservation observation,
        MediaObservationProgressResult progress,
        CancellationToken cancellationToken)
    {
        if (observation.IsCatalogObservation)
        {
            return await _lifecycleService.DeletePublishedCatalogObservationAsync(observation, cancellationToken);
        }

        return await _lifecycleService.DeleteAfterDurableOutcomeAsync(observation,
            observation.MatchStatus == MediaObservationStatuses.Matched
                && progress.OutcomeSatisfied, cancellationToken);
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

        Guid? statusOperationId = null;
        var applyProgress = false;
        var requireSharedAssignment = false;
        var reviewedAt = observation.UpdatedAt;
        if (request.ExpectedUpdatedAt is { } expectedUpdatedAt && reviewedAt != expectedUpdatedAt)
        {
            return ObservationUpdatedConflict();
        }

        try
        {
            var selection = await ResolveTargetAsync(userId, observation, request, cancellationToken);
            if (selection.Error is not null)
            {
                return selection.Error;
            }

            var reload = false;
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    var error = await _seasonMappingService.WithWriteAsync<ActionResult?>(async () =>
                    {
                        if (reload)
                        {
                            _dbContext.ChangeTracker.Clear();
                            observation = await LoadObservationAsync(observationId, userId, cancellationToken);
                            statusOperationId = null;
                        }
                        reload = true;
                        if (observation is null || observation.UpdatedAt != reviewedAt)
                        {
                            return ObservationUpdatedConflict();
                        }

                        (statusOperationId, applyProgress, requireSharedAssignment) = await CommitResolutionAsync(userId, observation, selection,
                            request.EpisodeOffset, cancellationToken);
                        return null;
                    }, cancellationToken);
                    if (error is not null)
                    {
                        return error;
                    }
                    break;
                }
                catch (DbUpdateException exception) when (selection.ProviderTitle is not null
                    && attempt < 2 && MediaCatalogCanonicalizationService.IsIdentityRace(exception))
                {
                    _logger.LogDebug(exception, "Retrying observation library add after a catalog identity race, attempt {Attempt}.", attempt + 1);
                }
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            return ObservationUpdatedConflict();
        }
        catch (ArgumentException exception)
        {
            _dbContext.ChangeTracker.Clear();
            return BadRequest(new { error = exception.Message });
        }

        if (observation is null)
        {
            return NotFound(new { error = "Media observation not found." });
        }

        if (statusOperationId is { } operationId)
        {
            await operationProcessor.ProcessOperationAsync(operationId, cancellationToken);
            _mediaLibraryEventHub.Publish(userId);
        }

        var progress = applyProgress
            ? await _progressService.TryEnqueueAutoProgressWithResultAsync(observation, cancellationToken,
                (observation.MediaTitleId!.Value, observation.ResolvedProgress), requireSharedAssignment)
            : MediaObservationProgressResult.None;
        if (progress.AssignmentChanged)
        {
            return Conflict(new { error = "The provider episode assignment changed during review." });
        }

        var response = MapToDto(observation);
        await DeleteCompletedObservationIfSafeAsync(observation, progress, cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Reject all candidates and mark the observation as rejected.
    /// POST /api/media/observations/{id}/reject
    /// </summary>
    [HttpPost("{observationId:guid}/reject")]
    public async Task<ActionResult<MediaObservationDto>> Reject(
        Guid observationId,
        CancellationToken cancellationToken,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] MediaObservationVersionRequest? request = null)
    {
        var userId = await GetCurrentUserIdAsync();
        var observation = await LoadObservationAsync(observationId, userId, cancellationToken);
        if (observation is null)
        {
            return NotFound(new { error = "Media observation not found." });
        }

        if (request?.ExpectedUpdatedAt is { } expectedUpdatedAt && observation.UpdatedAt != expectedUpdatedAt)
        {
            return ObservationUpdatedConflict();
        }

        foreach (var c in observation.Candidates)
        {
            c.IsAccepted = false;
        }

        observation.MediaTitleId = null;
        observation.AcceptedCandidateId = null;
        observation.MatchStatus = MediaObservationStatuses.Rejected;
        observation.ResolutionNotes = "Rejected by user.";
        observation.Touch(DateTimeOffset.UtcNow);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ObservationUpdatedConflict();
        }

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
        CancellationToken cancellationToken,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] MediaObservationVersionRequest? request = null)
    {
        var userId = await GetCurrentUserIdAsync();
        var observation = await LoadObservationAsync(observationId, userId, cancellationToken);
        if (observation is null)
        {
            return NotFound(new { error = "Media observation not found." });
        }

        if (request?.ExpectedUpdatedAt is { } expectedUpdatedAt && observation.UpdatedAt != expectedUpdatedAt)
        {
            return ObservationUpdatedConflict();
        }

        // Reset to pending so the matcher starts fresh.
        observation.ResetMatch();
        observation.ProviderChoicesPayload = null;

        try
        {
            await _seasonMappingService.WithWriteAsync(async () =>
            {
                await _episodeIdentityService.RecordObservationAsync(observation, cancellationToken, countSeen: false);
                return await _matchingService.ProcessObservationAsync(observation, cancellationToken, reconsiderAutomaticMapping: true);
            }, cancellationToken);
            await EnsureProviderChoicesAsync(observation, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ObservationUpdatedConflict();
        }
        var progress = await _progressService.TryEnqueueAutoProgressWithResultAsync(observation, cancellationToken);
        var response = MapToDto(observation);
        await DeleteCompletedObservationIfSafeAsync(observation, progress, cancellationToken);
        return Ok(response);
    }

    private ConflictObjectResult ObservationUpdatedConflict() =>
        Conflict(new { error = "Observation was updated by a newer submission." });

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
        bool progressUpdated = false,
        bool deleted = false)
    {
        var dto = MapToDto(observation);
        var requiresResolution = observation.MatchStatus is MediaObservationStatuses.Ambiguous or MediaObservationStatuses.NoMatch;
        return new SubmitMediaObservationResponse
        {
            ObservationId = deleted ? null : observation.Id.ToString(),
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
            Observation = !deleted ? dto : null,
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
            UpdatedAt = observation.UpdatedAt,
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
                observation.Touch(DateTimeOffset.UtcNow);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return null;
            }
        }

        var choices = await MapProviderChoicesAsync(observation.UserId, provider.ProviderId, results, cancellationToken);
        observation.ProviderChoicesPayload = JsonSerializer.Serialize(choices, PayloadJsonOptions);
        observation.Touch(DateTimeOffset.UtcNow);
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
                candidate.Id,
                libraryEntryId,
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

        if (await FindProviderLibraryEntryAsync(userId, providerId, providerMediaId, cancellationToken) is { } existingEntry)
        {
            return new ResolveTargetResult(
                existingEntry.MediaTitleId,
                null,
                existingEntry.Id,
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

        var provider = _mediaProviderRegistry.GetRequired(providerId);
        var account = await provider.GetConnectedAccountAsync(userId, cancellationToken);
        if (account is null)
        {
            return ResolveTargetResult.FromError(BadRequest(new { error = $"{provider.ProviderId} is not connected." }));
        }

        var details = await provider.GetTitleDetailsAsync(userId, providerMediaId, cancellationToken);
        if (details is null)
        {
            return ResolveTargetResult.FromError(NotFound(new { error = "Provider title not found." }));
        }

        details.ProviderId = provider.ProviderId;
        details.ProviderMediaId = providerMediaId;
        return new ResolveTargetResult(null, null, null, "Resolved by user provider selection.", null,
            new ProviderTitleToAdd(account, details));
    }

    private Task<MediaLibraryEntry?> FindProviderLibraryEntryAsync(int userId, string providerId,
        string providerMediaId, CancellationToken cancellationToken) =>
        _dbContext.MediaLibraryProviderBindings.AsNoTracking()
            .Where(binding => binding.MediaLibraryEntry!.UserId == userId
                && binding.MediaProviderLink!.Provider == providerId
                && binding.MediaProviderLink.ExternalId == providerMediaId)
            .Select(binding => binding.MediaLibraryEntry)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<(Guid? OperationId, bool ApplyProgress, bool RequireSharedAssignment)> CommitResolutionAsync(
        int userId, MediaObservation observation,
        ResolveTargetResult selection, int offset, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var titleId = selection.MediaTitleId;
        var libraryEntryId = selection.LibraryEntryId;
        var notes = selection.Notes;
        MediaProviderLink? providerLink = null;
        if (selection.ProviderTitle is { } providerTitle)
        {
            var details = providerTitle.Details;
            // A concurrent resolution may have added the binding while we fetched provider details.
            var existing = await FindProviderLibraryEntryAsync(userId, details.ProviderId,
                details.ProviderMediaId, cancellationToken);
            if (existing is not null)
            {
                titleId = existing.MediaTitleId;
                libraryEntryId = existing.Id;
            }
            else
            {
                var catalog = await _catalogService.ResolveAsync(
                    _dbContext,
                    [new MediaCatalogCandidate(details, LinkSource: MediaMappingSources.UserConfirmed,
                        LinkedByUserId: userId)],
                    now,
                    cancellationToken);
                providerLink = catalog.Links[MediaCatalogIdentity.Create(details.ProviderId, details.ProviderMediaId)];
                titleId = providerLink.MediaTitleId;
            }
        }

        var title = providerLink?.MediaTitle ?? await _dbContext.MediaTitles.FindAsync([titleId!.Value], cancellationToken)
            ?? throw new ArgumentException("Selected media title no longer exists.");
        if (MediaProviderSeasonMappingService.IsSupported(observation.SiteIdentifier)
            && !MediaProviderSeasonMappingService.IsCompatibleTarget(title, numbered: !title.IsCompletionOnly))
        {
            throw new ArgumentException("A Crunchyroll observation requires a video title with episode progress or completion-only progress.");
        }

        var coordinates = await _seasonMappingService.GetCoordinatesAsync(observation, cancellationToken);
        var observedProgress = TryParseObservationProgress(observation, out var parsedProgress)
            ? parsedProgress : (int?)null;
        var resolvedProgress = (long?)(coordinates.EpisodeNumber ?? observedProgress) + offset;
        if (title.IsCompletionOnly)
        {
            if (offset != 0 || (observation.IsCatalogObservation && observation.Episodes.Count != 1))
            {
                throw new ArgumentException("A completion-only title requires a single destination and no episode offset.");
            }
        }
        if (!title.IsCompletionOnly && ((resolvedProgress is null ? offset != 0
                : !MediaObservationMatchingService.IsCanonicalEpisodeNumber(title, resolvedProgress))
            || observation.Episodes.Any(episode =>
                !MediaObservationMatchingService.IsCanonicalEpisodeNumber(title, (long)episode.EpisodeNumber + offset))))
        {
            throw new ArgumentException("Episode offset resolves to progress outside the title's episode range.");
        }

        Guid? operationId = null;
        if (providerLink is not null)
        {
            var binding = await StageProviderTitleAddAsync(userId, selection.ProviderTitle!, providerLink, now, cancellationToken);
            libraryEntryId = binding.MediaLibraryEntryId;
            notes = "Resolved by user provider selection and added to library.";
            var operation = await operationProcessor.EnqueueStatusUpdateAsync(userId, binding, new MediaStatusUpdateRequest
            {
                ProviderMediaId = providerLink.ExternalId,
                Status = MediaLibraryStatuses.Current
            }, cancellationToken, deferSave: true);
            operationId = operation.Id;
            // Mapping writes use SQL and need the title's foreign key before they can run.
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        await _episodeIdentityService.RecordObservationAsync(observation, cancellationToken, countSeen: false);
        observation.ResolvedProgress = !observation.IsCatalogObservation && !title.IsCompletionOnly
            ? (int?)resolvedProgress : null;
        var establishedMapping = await _matchingService.EstablishMappingAsync(observation, title, offset,
            MediaProviderSeasonMappingSources.Manual, 1m, cancellationToken);

        foreach (var candidate in observation.Candidates)
        {
            candidate.IsAccepted = candidate.Id == selection.AcceptedCandidateId;
        }
        observation.MediaTitle = title;
        observation.MediaTitleId = title.Id;
        observation.AcceptedCandidateId = selection.AcceptedCandidateId;
        observation.MatchStatus = MediaObservationStatuses.Matched;
        observation.ResolutionNotes = notes;
        observation.EpisodeOffset = offset;
        observation.ResolvedLibraryEntryId = libraryEntryId;
        observation.Touch(now);
        await _dbContext.SaveChangesAsync(cancellationToken);
        var hasCompletionPlayback = title.IsCompletionOnly
            && MediaDestinationUrlPolicy.TryNormalizePath(observation.SiteIdentifier, observation.ObservedUrl,
                observation.SiteMediaId, out _);
        return (operationId, resolvedProgress is not null || hasCompletionPlayback,
            establishedMapping || MediaObservationMatchingService.UsedSharedMapping(observation));
    }

    private async Task<MediaLibraryProviderBinding> StageProviderTitleAddAsync(
        int userId,
        ProviderTitleToAdd selection,
        MediaProviderLink providerLink,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var (account, details) = selection;
        var title = providerLink.MediaTitle!;
        if (details.AvailabilityRefreshSucceeded)
        {
            providerLink.AvailabilitySnapshot = MediaProviderAvailabilitySnapshotCodec.Serialize(details.AvailabilityLinks);
            providerLink.AvailabilityLastVerifiedAt = now;
        }
        var entry = await _dbContext.MediaLibraryEntries.SingleOrDefaultAsync(
            row => row.UserId == userId && row.MediaTitleId == title.Id, cancellationToken);
        if (entry is null)
        {
            entry = new MediaLibraryEntry
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                MediaTitleId = title.Id,
                Status = MediaLibraryStatuses.Current,
                ProgressEpisodes = details.PrimaryProgressDimension == MediaProgressDimensions.Episode ? 0 : null,
                ProgressChapters = details.PrimaryProgressDimension == MediaProgressDimensions.Chapter ? 0 : null,
                ProgressVolumes = details.PrimaryProgressDimension == MediaProgressDimensions.Volume ? 0 : null,
                CreatedAt = now
            };
            _dbContext.MediaLibraryEntries.Add(entry);
        }
        entry.Status = MediaLibraryStatuses.Current;
        entry.LastLocalEditAt = now;
        entry.LastMutationSource = MediaMutationSources.UserStatusUpdate;
        entry.UpdatedAt = now;
        var binding = new MediaLibraryProviderBinding
        {
            Id = Guid.NewGuid(),
            MediaLibraryEntryId = entry.Id,
            MediaProviderLinkId = providerLink.Id,
            ConnectedServiceAccountId = account.Id,
            ProviderAccountId = account.ExternalAccountId,
            CreatedAt = now,
            UpdatedAt = now
        };
        _dbContext.MediaLibraryProviderBindings.Add(binding);
        return binding;
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
        Guid? AcceptedCandidateId,
        Guid? LibraryEntryId,
        string Notes,
        ActionResult? Error,
        ProviderTitleToAdd? ProviderTitle = null)
    {
        public static ResolveTargetResult FromError(ActionResult error) => new(null, null, null, string.Empty, error);
    }

    private sealed record ProviderTitleToAdd(ConnectedServiceAccount Account, MediaProviderTitleDetails Details);

}
