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
    ILogger<MediaObservationsController> logger) : ControllerBase
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly UserManager<User> _userManager = userManager;
    private readonly MediaObservationMatchingService _matchingService = matchingService;
    private readonly MediaObservationProgressService _progressService = progressService;
    private readonly ILogger<MediaObservationsController> _logger = logger;

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
        var userId = await GetCurrentUserIdAsync();

        var normalizedSite = request.SiteIdentifier.ToLowerInvariant().Trim();
        var normalizedSiteMediaId = string.IsNullOrWhiteSpace(request.SiteMediaId)
            ? null
            : request.SiteMediaId.Trim();

        // Deduplicate by (UserId, SiteIdentifier, SiteMediaId) when SiteMediaId
        // is available. This prevents duplicate rows when the extension re-fires
        // for the same episode.
        if (normalizedSiteMediaId is not null)
        {
            var existing = await _dbContext.MediaObservations
                .Include(o => o.Candidates)
                .FirstOrDefaultAsync(
                    o => o.UserId == userId
                         && o.SiteIdentifier == normalizedSite
                         && o.SiteMediaId == normalizedSiteMediaId,
                    cancellationToken);

            if (existing is not null)
            {
                _logger.LogInformation(
                    "Deduplicated MediaObservation for user {UserId}: {SiteIdentifier}/{SiteMediaId} → {ObservationId}.",
                    userId, normalizedSite, normalizedSiteMediaId, existing.Id);

                return Ok(MapToSubmitResponse(existing, wasDeduplicated: true));
            }
        }

        var rawPayload = JsonSerializer.Serialize(request);
        var now = DateTimeOffset.UtcNow;

        var observation = new MediaObservation
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SiteIdentifier = normalizedSite,
            ObservedUrl = request.ObservedUrl,
            SiteMediaId = normalizedSiteMediaId,
            ObservedTitle = request.ObservedTitle.Trim(),
            ProgressHint = string.IsNullOrWhiteSpace(request.ProgressHint)
                ? null
                : request.ProgressHint.Trim(),
            ObservedAt = request.ObservedAt ?? now,
            ExtensionVersion = request.ExtensionVersion?.Trim(),
            RawPayload = rawPayload,
            MatchStatus = MediaObservationStatuses.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };

        _dbContext.MediaObservations.Add(observation);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Stored new MediaObservation {ObservationId} for user {UserId}: {SiteIdentifier} \"{ObservedTitle}\".",
            observation.Id, userId, normalizedSite, observation.ObservedTitle);

        // Run candidate matching synchronously for MVP — fast enough for library
        // lookups and avoids requiring a background worker.
        observation = await _matchingService.ProcessObservationAsync(observation, cancellationToken);

        // Attempt auto-progress if the matching step produced a confident match.
        if (observation.MatchStatus == MediaObservationStatuses.Matched)
        {
            await _progressService.TryEnqueueAutoProgressAsync(observation, cancellationToken);
        }

        return Ok(MapToSubmitResponse(observation, wasDeduplicated: false));
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
                        o.MatchStatus != MediaObservationStatuses.Matched &&
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
            query = query.Where(o =>
                o.MatchStatus != MediaObservationStatuses.Matched &&
                o.MatchStatus != MediaObservationStatuses.Rejected);
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

        var candidate = observation.Candidates.FirstOrDefault(c => c.Id == request.CandidateId);
        if (candidate is null)
        {
            return BadRequest(new { error = "Candidate does not belong to this observation." });
        }

        // Clear previous acceptance state.
        foreach (var c in observation.Candidates)
        {
            c.IsAccepted = false;
        }

        candidate.IsAccepted = true;
        observation.MediaTitleId = candidate.MediaTitleId;
        observation.AcceptedCandidateId = candidate.Id;
        observation.MatchStatus = MediaObservationStatuses.Matched;
        observation.ResolutionNotes = "Resolved by user candidate selection.";
        observation.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User {UserId} resolved MediaObservation {ObservationId} to MediaTitle {MediaTitleId} via candidate {CandidateId}.",
            userId, observationId, candidate.MediaTitleId, candidate.Id);

        // Trigger auto-progress for opted-in entries now that the user has
        // explicitly confirmed the match.
        await _progressService.TryEnqueueAutoProgressAsync(observation, cancellationToken);

        return Ok(MapToDto(observation));
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

        _logger.LogInformation(
            "User {UserId} rejected MediaObservation {ObservationId}.",
            userId, observationId);

        return Ok(MapToDto(observation));
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

        observation = await _matchingService.ProcessObservationAsync(observation, cancellationToken);
        return Ok(MapToDto(observation));
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task<MediaObservation?> LoadObservationAsync(
        Guid observationId,
        int userId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.MediaObservations
            .Include(o => o.Candidates)
            .FirstOrDefaultAsync(
                o => o.Id == observationId && o.UserId == userId,
                cancellationToken);
    }

    private static SubmitMediaObservationResponse MapToSubmitResponse(
        MediaObservation observation,
        bool wasDeduplicated)
    {
        return new SubmitMediaObservationResponse
        {
            ObservationId = observation.Id.ToString(),
            MatchStatus = observation.MatchStatus,
            WasDeduplicated = wasDeduplicated,
            MatchedMediaTitleId = observation.MediaTitleId?.ToString(),
            MatchedTitle = observation.MediaTitle?.CanonicalTitle,
            CandidateCount = observation.Candidates.Count
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
            ExtensionVersion = observation.ExtensionVersion,
            MatchStatus = observation.MatchStatus,
            MediaTitleId = observation.MediaTitleId?.ToString(),
            ResolutionNotes = observation.ResolutionNotes,
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
                .ToList()
        };
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
}
