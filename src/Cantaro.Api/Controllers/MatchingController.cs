using System.Text.Json;
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

public class MatchingCandidateComparisonResponse
{
    public required string Label { get; set; }
    public string? ObservationValue { get; set; }
    public string? CandidateValue { get; set; }
    public decimal? Score { get; set; }
    public string? ScoreLabel { get; set; }
    public required string Tone { get; set; }
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
    public List<string> VersionMarkers { get; set; } = [];
    public List<string> PlaybackModifiers { get; set; } = [];
    public List<MatchingCandidateComparisonResponse> Comparisons { get; set; } = [];
    public decimal? TitleSimilarity { get; set; }
    public decimal? ArtistSimilarity { get; set; }
    public decimal? DurationScore { get; set; }
    public decimal? SemanticAdjustment { get; set; }
    public string? SemanticExplanation { get; set; }
    public string? ClusterId { get; set; }
    public int ClusterSize { get; set; }
    public string? ClusterReason { get; set; }
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
    public TrackMatchObservationDiagnostics Diagnostics { get; set; } = new();
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
public class MatchingController(
    ApplicationDbContext dbContext,
    UserManager<User> userManager,
    TrackMatchingService trackMatchingService,
    ILogger<MatchingController> logger) : ControllerBase
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly UserManager<User> _userManager = userManager;
    private readonly TrackMatchingService _trackMatchingService = trackMatchingService;
    private readonly ILogger<MatchingController> _logger = logger;

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
        var parsedObservation = TrackMetadataParser.Parse(observation.Title, observation.Artist);
        var storedObservationMetadata = ExtractObservationMetadata(observation);
        var displayTitle = TrackObservationDisplayFormatter.GetQueueTitle(observation, storedObservationMetadata);
        var displayArtist = TrackObservationDisplayFormatter.GetQueueArtist(observation, storedObservationMetadata);
        var candidateProjections = observation.Candidates
            .OrderByDescending(candidate => candidate.Score)
            .Select(candidate => new
            {
                Candidate = candidate,
                Diagnostics = ExtractCandidateDiagnostics(candidate)
            })
            .ToList();

        var distinctClusterScores = candidateProjections
            .GroupBy(
                projection => string.IsNullOrWhiteSpace(projection.Diagnostics.ClusterId)
                    ? projection.Candidate.Id.ToString()
                    : projection.Diagnostics.ClusterId,
                StringComparer.Ordinal)
            .Select(group => group.Max(projection => projection.Candidate.Score))
            .OrderByDescending(score => score)
            .ToList();

        return new MatchingQueueItemResponse
        {
            ObservationId = observation.Id.ToString(),
            SourceType = observation.SourceType,
            ExternalId = observation.ExternalId,
            Title = displayTitle,
            Artist = displayArtist,
            ThumbnailUrl = observation.ThumbnailUrl,
            DurationSeconds = observation.DurationSeconds,
            MatchStatus = observation.MatchStatus,
            MatchAttemptCount = observation.MatchAttemptCount,
            LastMatchAttemptedAt = observation.LastMatchAttemptedAt,
            LastMatchError = observation.LastMatchError,
            ResolutionNotes = observation.ResolutionNotes,
            Diagnostics = storedObservationMetadata?.Matching ?? new TrackMatchObservationDiagnostics
            {
                VersionMarkers = [.. parsedObservation.VersionMarkers],
                PlaybackModifiers = [.. parsedObservation.PlaybackModifiers],
                DecisionReason = observation.ResolutionNotes,
                TopScore = distinctClusterScores.Count > 0 ? distinctClusterScores[0] : null,
                SecondDistinctScore = distinctClusterScores.Count > 1 ? distinctClusterScores[1] : null,
                DistinctClusterCount = distinctClusterScores.Count
            },
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
            Candidates = candidateProjections
                .Select(projection => new MatchingQueueCandidateResponse
                {
                    CandidateId = projection.Candidate.Id.ToString(),
                    CandidateSource = projection.Candidate.CandidateSource,
                    ExternalId = projection.Candidate.ExternalId,
                    Title = projection.Candidate.Title,
                    Artist = projection.Candidate.Artist,
                    MbidRecording = projection.Candidate.MbidRecording,
                    Isrc = projection.Candidate.Isrc,
                    DurationSeconds = projection.Candidate.DurationSeconds,
                    Score = projection.Candidate.Score,
                    Explanation = projection.Candidate.Explanation,
                    IsAccepted = projection.Candidate.IsAccepted,
                    VersionMarkers = projection.Diagnostics.CandidateVersionMarkers,
                    PlaybackModifiers = projection.Diagnostics.CandidatePlaybackModifiers,
                    Comparisons = BuildCandidateComparisons(observation, projection.Candidate, projection.Diagnostics),
                    TitleSimilarity = projection.Diagnostics.TitleSimilarity,
                    ArtistSimilarity = projection.Diagnostics.ArtistSimilarity,
                    DurationScore = projection.Diagnostics.DurationScore,
                    SemanticAdjustment = projection.Diagnostics.SemanticAdjustment,
                    SemanticExplanation = projection.Diagnostics.SemanticExplanation,
                    ClusterId = projection.Diagnostics.ClusterId,
                    ClusterSize = projection.Diagnostics.ClusterSize,
                    ClusterReason = projection.Diagnostics.ClusterReason
                })
                .ToList()
        };
    }

    private static List<MatchingCandidateComparisonResponse> BuildCandidateComparisons(
        TrackObservation observation,
        TrackResolutionCandidate candidate,
        TrackMatchCandidateDiagnostics diagnostics)
    {
        var observationMetadata = TrackMetadataParser.Parse(observation.Title, observation.Artist);
        var candidateMetadata = TrackMetadataParser.Parse(candidate.Title, candidate.Artist);
        var observationTitle = PreferValue(diagnostics.ObservationSearchTitle, observationMetadata.SearchTitle, observation.Title);
        var observationArtist = PreferValue(diagnostics.ObservationSearchArtist, observationMetadata.SearchArtist, observation.Artist);
        var candidateTitle = PreferValue(diagnostics.CandidateSearchTitle, candidateMetadata.SearchTitle, candidate.Title);
        var candidateArtist = PreferValue(diagnostics.CandidateSearchArtist, candidateMetadata.SearchArtist, candidate.Artist);

        var comparisons = new List<MatchingCandidateComparisonResponse>
        {
            CreateComparison("Title", observationTitle, candidateTitle, diagnostics.TitleSimilarity),
            CreateComparison("Artist", observationArtist, candidateArtist, diagnostics.ArtistSimilarity),
            CreateComparison(
                "Duration",
                FormatDuration(observation.DurationSeconds),
                FormatDuration(candidate.DurationSeconds),
                diagnostics.DurationScore),
            CreateMarkerComparison(
                "Version",
                diagnostics.ObservationVersionMarkers.Count > 0 ? diagnostics.ObservationVersionMarkers : observationMetadata.VersionMarkers,
                diagnostics.CandidateVersionMarkers.Count > 0 ? diagnostics.CandidateVersionMarkers : candidateMetadata.VersionMarkers),
            CreateMarkerComparison(
                "Playback",
                diagnostics.ObservationPlaybackModifiers.Count > 0 ? diagnostics.ObservationPlaybackModifiers : observationMetadata.PlaybackModifiers,
                diagnostics.CandidatePlaybackModifiers.Count > 0 ? diagnostics.CandidatePlaybackModifiers : candidateMetadata.PlaybackModifiers)
        };

        comparisons.Add(CreateSemanticComparison(diagnostics.SemanticAdjustment, diagnostics.SemanticExplanation));

        return comparisons;
    }

    private static MatchingCandidateComparisonResponse CreateComparison(
        string label,
        string? observationValue,
        string? candidateValue,
        decimal? score)
    {
        return new MatchingCandidateComparisonResponse
        {
            Label = label,
            ObservationValue = string.IsNullOrWhiteSpace(observationValue) ? "None" : observationValue,
            CandidateValue = string.IsNullOrWhiteSpace(candidateValue) ? "None" : candidateValue,
            Score = score,
            ScoreLabel = null,
            Tone = ScoreTone(score)
        };
    }

    private static MatchingCandidateComparisonResponse CreateMarkerComparison(
        string label,
        IReadOnlyList<string> observationMarkers,
        IReadOnlyList<string> candidateMarkers)
    {
        var score = TrackMatchScorer.HaveSameMarkers(observationMarkers, candidateMarkers) ? 1m : 0m;
        var comparison = CreateComparison(label, FormatMarkers(observationMarkers), FormatMarkers(candidateMarkers), score);
        comparison.ScoreLabel = score == 1m ? "Match" : "Diff";
        return comparison;
    }

    private static MatchingCandidateComparisonResponse CreateSemanticComparison(decimal adjustment, string? explanation)
    {
        var label = adjustment switch
        {
            > 0m => $"+{Math.Round(adjustment * 100m, 0, MidpointRounding.AwayFromZero)} pts",
            < 0m => $"{Math.Round(adjustment * 100m, 0, MidpointRounding.AwayFromZero)} pts",
            _ => "0 pts"
        };

        return new MatchingCandidateComparisonResponse
        {
            Label = "Semantic",
            ObservationValue = "Rule adjustment",
            CandidateValue = string.IsNullOrWhiteSpace(explanation) ? "No marker penalty" : explanation.Trim().TrimEnd('.'),
            Score = null,
            ScoreLabel = label,
            Tone = adjustment < 0m ? "miss" : "match"
        };
    }

    private static string? PreferValue(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static string FormatDuration(int? durationSeconds)
    {
        if (!durationSeconds.HasValue || durationSeconds.Value <= 0)
        {
            return "Unknown";
        }

        var minutes = durationSeconds.Value / 60;
        var seconds = durationSeconds.Value % 60;
        return $"{minutes}:{seconds:00}";
    }

    private static string FormatMarkers(IReadOnlyList<string> markers)
    {
        return markers.Count == 0 ? "None" : string.Join(", ", markers);
    }

    private static string ScoreTone(decimal? score)
    {
        if (!score.HasValue)
        {
            return "neutral";
        }

        if (score.Value >= 0.9m)
        {
            return "match";
        }

        return score.Value >= 0.65m ? "close" : "miss";
    }

    private static TrackMatchCandidateDiagnostics ExtractCandidateDiagnostics(TrackResolutionCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.RawMetadata))
        {
            return new TrackMatchCandidateDiagnostics();
        }

        try
        {
            var storedMetadata = JsonSerializer.Deserialize<TrackMatchCandidateStoredMetadata>(candidate.RawMetadata);
            return storedMetadata?.Matching ?? new TrackMatchCandidateDiagnostics();
        }
        catch (JsonException)
        {
            return new TrackMatchCandidateDiagnostics();
        }
    }

    private static TrackObservationMetadata? ExtractObservationMetadata(TrackObservation observation)
    {
        if (string.IsNullOrWhiteSpace(observation.RawMetadata))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TrackObservationMetadata>(observation.RawMetadata);
        }
        catch (JsonException)
        {
            return null;
        }
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
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            throw new UnauthorizedAccessException("User not authenticated");
        }

        return user.Id;
    }
}
