using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Controllers;

public class MatchingSummaryResponse
{
    public int TotalUnresolved { get; set; }
    public int Pending { get; set; }
    public int Ambiguous { get; set; }
    public int NoMatch { get; set; }
}

public class MatchingQueuePlaylistResponse
{
    public required string PlaylistId { get; set; }
    public required string PlaylistName { get; set; }
    public int Position { get; set; }
}

public class MatchingQueueCandidateResponse
{
    public required string CandidateId { get; set; }
    public required string CandidateSource { get; set; }
    public required string ExternalId { get; set; }
    public required string Title { get; set; }
    public string? Artist { get; set; }
    public string? MbidRecording { get; set; }
    public string? Isrc { get; set; }
    public int? DurationSeconds { get; set; }
    public decimal Score { get; set; }
    public string? Explanation { get; set; }
    public bool IsAccepted { get; set; }
}

public class MatchingQueueItemResponse
{
    public required string ObservationId { get; set; }
    public required string SourceType { get; set; }
    public required string ExternalId { get; set; }
    public required string Title { get; set; }
    public string? Artist { get; set; }
    public string? ThumbnailUrl { get; set; }
    public int? DurationSeconds { get; set; }
    public required string MatchStatus { get; set; }
    public int MatchAttemptCount { get; set; }
    public DateTimeOffset? LastMatchAttemptedAt { get; set; }
    public string? LastMatchError { get; set; }
    public string? ResolutionNotes { get; set; }
    public List<MatchingQueuePlaylistResponse> Playlists { get; set; } = [];
    public List<MatchingQueueCandidateResponse> Candidates { get; set; } = [];
}

public class SelectMatchingCandidateRequest
{
    public required Guid CandidateId { get; set; }
}

[ApiController]
[Route("api/matching")]
[Authorize]
public class MatchingController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<User> _userManager;
    private readonly TrackMatchingService _trackMatchingService;
    private readonly ILogger<MatchingController> _logger;

    public MatchingController(
        ApplicationDbContext dbContext,
        UserManager<User> userManager,
        TrackMatchingService trackMatchingService,
        ILogger<MatchingController> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _trackMatchingService = trackMatchingService;
        _logger = logger;
    }

    [HttpGet("summary")]
    public async Task<ActionResult<MatchingSummaryResponse>> GetSummary(CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserIdAsync();
        var observationIds = await GetUserObservationIdsAsync(userId, cancellationToken);

        var grouped = await _dbContext.TrackObservations
            .Where(o => observationIds.Contains(o.Id) && o.MatchStatus != TrackMatchingStatuses.Matched)
            .GroupBy(o => o.MatchStatus)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return Ok(new MatchingSummaryResponse
        {
            TotalUnresolved = grouped.Sum(item => item.Count),
            Pending = grouped.FirstOrDefault(item => item.Key == TrackMatchingStatuses.Pending)?.Count ?? 0,
            Ambiguous = grouped.FirstOrDefault(item => item.Key == TrackMatchingStatuses.Ambiguous)?.Count ?? 0,
            NoMatch = grouped.FirstOrDefault(item => item.Key == TrackMatchingStatuses.NoMatch)?.Count ?? 0
        });
    }

    [HttpGet("queue")]
    public async Task<ActionResult<List<MatchingQueueItemResponse>>> GetQueue(CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserIdAsync();
        var observationIds = await GetUserObservationIdsAsync(userId, cancellationToken);

        var observations = await _dbContext.TrackObservations
            .Include(o => o.Candidates)
            .Include(o => o.PlaylistEntries)
                .ThenInclude(entry => entry.Playlist)
            .Where(o => observationIds.Contains(o.Id) && o.MatchStatus != TrackMatchingStatuses.Matched)
            .OrderByDescending(o => o.MatchStatus == TrackMatchingStatuses.Ambiguous)
            .ThenByDescending(o => o.LastMatchAttemptedAt)
            .ToListAsync(cancellationToken);

        return Ok(observations.Select(observation => MapQueueItem(observation, userId)).ToList());
    }

    [HttpPost("queue/{observationId:guid}/retry")]
    public async Task<ActionResult<MatchingQueueItemResponse>> Retry(Guid observationId, CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserIdAsync();
        if (!await ObservationAccessibleAsync(observationId, userId, cancellationToken))
        {
            return NotFound(new { error = "Track observation not found." });
        }

        var observation = await _trackMatchingService.ProcessObservationAsync(observationId, cancellationToken);
        return Ok(await LoadQueueItemAsync(observation.Id, userId, cancellationToken));
    }

    [HttpPost("queue/{observationId:guid}/select-candidate")]
    public async Task<ActionResult<MatchingQueueItemResponse>> SelectCandidate(
        Guid observationId,
        [FromBody] SelectMatchingCandidateRequest request,
        CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserIdAsync();
        if (!await ObservationAccessibleAsync(observationId, userId, cancellationToken))
        {
            return NotFound(new { error = "Track observation not found." });
        }

        var observation = await _trackMatchingService.AcceptCandidateAsync(observationId, request.CandidateId, cancellationToken);
        return Ok(await LoadQueueItemAsync(observation.Id, userId, cancellationToken));
    }

    [HttpPost("queue/{observationId:guid}/mark-no-match")]
    public async Task<ActionResult<MatchingQueueItemResponse>> MarkNoMatch(Guid observationId, CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserIdAsync();
        if (!await ObservationAccessibleAsync(observationId, userId, cancellationToken))
        {
            return NotFound(new { error = "Track observation not found." });
        }

        var observation = await _trackMatchingService.MarkNoMatchAsync(observationId, cancellationToken);
        return Ok(await LoadQueueItemAsync(observation.Id, userId, cancellationToken));
    }

    [HttpPost("queue/{observationId:guid}/create-track")]
    public async Task<ActionResult<MatchingQueueItemResponse>> CreateTrack(Guid observationId, CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserIdAsync();
        if (!await ObservationAccessibleAsync(observationId, userId, cancellationToken))
        {
            return NotFound(new { error = "Track observation not found." });
        }

        var observation = await _trackMatchingService.CreateTrackFromObservationAsync(observationId, cancellationToken);
        return Ok(await LoadQueueItemAsync(observation.Id, userId, cancellationToken));
    }

    private async Task<MatchingQueueItemResponse> LoadQueueItemAsync(Guid observationId, int userId, CancellationToken cancellationToken)
    {
        var observation = await _dbContext.TrackObservations
            .Include(o => o.Candidates)
            .Include(o => o.PlaylistEntries)
                .ThenInclude(entry => entry.Playlist)
            .FirstOrDefaultAsync(o => o.Id == observationId, cancellationToken)
            ?? throw new InvalidOperationException($"Track observation {observationId} was not found.");

        return MapQueueItem(observation, userId);
    }

    private static MatchingQueueItemResponse MapQueueItem(TrackObservation observation, int userId)
    {
        return new MatchingQueueItemResponse
        {
            ObservationId = observation.Id.ToString(),
            SourceType = observation.SourceType,
            ExternalId = observation.ExternalId,
            Title = observation.Title,
            Artist = observation.Artist,
            ThumbnailUrl = observation.ThumbnailUrl,
            DurationSeconds = observation.DurationSeconds,
            MatchStatus = observation.MatchStatus,
            MatchAttemptCount = observation.MatchAttemptCount,
            LastMatchAttemptedAt = observation.LastMatchAttemptedAt,
            LastMatchError = observation.LastMatchError,
            ResolutionNotes = observation.ResolutionNotes,
            Playlists = observation.PlaylistEntries
                .Where(entry => entry.Playlist?.UserId == userId)
                .OrderBy(entry => entry.Playlist!.Name)
                .ThenBy(entry => entry.Position)
                .Select(entry => new MatchingQueuePlaylistResponse
                {
                    PlaylistId = entry.PlaylistId.ToString(),
                    PlaylistName = entry.Playlist?.Name ?? "Unknown playlist",
                    Position = entry.Position
                })
                .ToList(),
            Candidates = observation.Candidates
                .OrderByDescending(candidate => candidate.Score)
                .Select(candidate => new MatchingQueueCandidateResponse
                {
                    CandidateId = candidate.Id.ToString(),
                    CandidateSource = candidate.CandidateSource,
                    ExternalId = candidate.ExternalId,
                    Title = candidate.Title,
                    Artist = candidate.Artist,
                    MbidRecording = candidate.MbidRecording,
                    Isrc = candidate.Isrc,
                    DurationSeconds = candidate.DurationSeconds,
                    Score = candidate.Score,
                    Explanation = candidate.Explanation,
                    IsAccepted = candidate.IsAccepted
                })
                .ToList()
        };
    }

    private async Task<bool> ObservationAccessibleAsync(Guid observationId, int userId, CancellationToken cancellationToken)
    {
        var accessible = await _dbContext.PlaylistEntries
            .AnyAsync(
                entry => entry.TrackObservationId == observationId && entry.Playlist != null && entry.Playlist.UserId == userId,
                cancellationToken);

        if (!accessible)
        {
            _logger.LogWarning("User {UserId} attempted to access observation {ObservationId} outside their queue.", userId, observationId);
        }

        return accessible;
    }

    private async Task<List<Guid>> GetUserObservationIdsAsync(int userId, CancellationToken cancellationToken)
    {
        return await _dbContext.PlaylistEntries
            .Where(entry => entry.TrackObservationId != null && entry.Playlist != null && entry.Playlist.UserId == userId)
            .Select(entry => entry.TrackObservationId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    private async Task<int> GetCurrentUserIdAsync()
    {
        var email = User.Identity?.Name;
        if (string.IsNullOrEmpty(email))
        {
            throw new UnauthorizedAccessException("User not authenticated");
        }

        var user = await _userManager.FindByEmailAsync(email);
        if (user == null)
        {
            throw new UnauthorizedAccessException("User not found");
        }

        return user.Id;
    }
}
