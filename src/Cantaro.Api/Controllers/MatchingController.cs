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
    public TrackVersionFlags SuggestedVersionFlags { get; set; }
    public TrackMatchObservationDiagnostics Diagnostics { get; set; } = new();
    public List<MatchingQueuePlaylistResponse> Playlists { get; set; } = [];
    public List<MatchingQueueCandidateResponse> Candidates { get; set; } = [];
}

public class MatchingQueuePageResponse
{
    public List<MatchingQueueItemResponse> Items { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
}

public class SelectMatchingCandidateRequest
{
    public required Guid CandidateId { get; set; }
}

public class SelectMatchingCandidateAsVersionRequest
{
    public required Guid CandidateId { get; set; }
    public TrackVersionFlags VersionFlags { get; set; }
}

public sealed class MatchingQueueFilterRequest
{
    public string? Status { get; set; }
    public string? SourceType { get; set; }
    public bool ErrorsOnly { get; set; }
    public string? Query { get; set; }
}

public sealed record MatchingBulkRetryResponse(int QueuedCount);

public sealed record TrackMatchWorkItemResponse(
    Guid ObservationId,
    string SourceType,
    string ExternalId,
    string Title,
    string? Artist,
    string QueueStatus,
    int RetryCount,
    int LifetimeAttemptCount,
    DateTimeOffset NextAttemptAt,
    DateTimeOffset? LeaseExpiresAt,
    DateTimeOffset? LastAttemptedAt,
    string? LastError);

public sealed record TrackMatchWorkPageResponse(
    IReadOnlyList<TrackMatchWorkItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    int ReadyCount,
    int ScheduledCount,
    int ProcessingCount,
    DateTimeOffset? ProviderNotBefore,
    DateTimeOffset AsOf);

public sealed record SongGroupingTrackResponse(
    Guid TrackId,
    string? Title,
    string? Artist,
    string? Isrc,
    string? MusicBrainzRecordingId,
    TrackVersionFlags VersionFlags);

public sealed record SongGroupingSuggestionResponse(
    Guid SuggestionId,
    decimal Confidence,
    string EvidenceJson,
    DateTimeOffset CreatedAt,
    SongGroupingTrackResponse Candidate,
    SongGroupingTrackResponse Anchor);

public sealed record SongGroupingSuggestionPageResponse(
    IReadOnlyList<SongGroupingSuggestionResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record GenerateSongGroupingSuggestionsResponse(int CreatedCount);

[ApiController]
[Route("api/matching")]
[Authorize]
public class MatchingController(
    ApplicationDbContext dbContext,
    UserManager<User> userManager,
    TrackMatchingService trackMatchingService,
    TrackMatchQueue trackMatchQueue,
    MusicBrainzRequestGate musicBrainzRequestGate,
    SongGroupingSuggestionService songGroupingSuggestionService) : ControllerBase
{
    // TODO: Matching decisions mutate Cantaro's global canonical music identity.
    // Require a reviewer/admin-level role before this is exposed beyond the current trusted-user setup.
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly UserManager<User> _userManager = userManager;
    private readonly TrackMatchingService _trackMatchingService = trackMatchingService;
    private readonly TrackMatchQueue _trackMatchQueue = trackMatchQueue;
    private readonly MusicBrainzRequestGate _musicBrainzRequestGate = musicBrainzRequestGate;
    private readonly SongGroupingSuggestionService _songGroupingSuggestionService =
        songGroupingSuggestionService;

    [HttpGet("summary")]
    public async Task<ActionResult<MatchingSummaryResponse>> GetSummary(CancellationToken cancellationToken)
    {
        var grouped = await _dbContext.TrackObservations
            .Where(o => o.MatchStatus != TrackMatchingStatuses.Matched)
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
    public async Task<ActionResult<MatchingQueuePageResponse>> GetQueue(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 5,
        [FromQuery] string? status = null,
        [FromQuery] string? sourceType = null,
        [FromQuery] bool errorsOnly = false,
        [FromQuery] string? query = null,
        CancellationToken cancellationToken = default)
    {
        var userId = await GetCurrentUserIdAsync();
        pageSize = Math.Clamp(pageSize, 1, 20);

        var unresolved = ApplyQueueFilters(
            _dbContext.TrackObservations.Where(observation => observation.MatchStatus != TrackMatchingStatuses.Matched),
            status,
            sourceType,
            errorsOnly,
            query);
        var totalCount = await unresolved.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);

        var observations = await unresolved
            .Include(o => o.Candidates)
            .Include(o => o.PlaylistEntries)
                .ThenInclude(entry => entry.Playlist)
            .AsSplitQuery()
            .OrderByDescending(o => o.MatchStatus == TrackMatchingStatuses.Ambiguous)
            .ThenByDescending(o => o.LastMatchAttemptedAt)
            .ThenBy(o => o.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Ok(new MatchingQueuePageResponse
        {
            Items = observations.Select(observation => MapQueueItem(observation, userId)).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages
        });
    }

    [HttpGet("work-queue")]
    public async Task<ActionResult<TrackMatchWorkPageResponse>> GetWorkQueue(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        pageSize = Math.Clamp(pageSize, 1, 50);
        var totalCount = await _dbContext.TrackMatchQueueItems.CountAsync(cancellationToken);
        var processingCount = await _dbContext.TrackMatchQueueItems.CountAsync(
            item => item.LeaseExpiresAt != null && item.LeaseExpiresAt > now.UtcDateTime,
            cancellationToken);
        var scheduledCount = await _dbContext.TrackMatchQueueItems.CountAsync(
            item => (item.LeaseExpiresAt == null || item.LeaseExpiresAt <= now.UtcDateTime)
                && item.NextAttemptAt > now.UtcDateTime,
            cancellationToken);
        var readyCount = totalCount - processingCount - scheduledCount;
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);
        var queuedItems = await _dbContext.TrackMatchQueueItems
            .AsNoTracking()
            .Include(item => item.TrackObservation)
            .OrderByDescending(item => item.LeaseExpiresAt != null && item.LeaseExpiresAt > now.UtcDateTime)
            .ThenBy(item => item.NextAttemptAt)
            .ThenBy(item => item.TrackObservationId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(item => new
            {
                item.TrackObservationId,
                item.NextAttemptAt,
                item.LeaseExpiresAt,
                item.RetryCount,
                item.TrackObservation!.SourceType,
                item.TrackObservation.ExternalId,
                item.TrackObservation.Title,
                item.TrackObservation.Artist,
                item.TrackObservation.MatchAttemptCount,
                item.TrackObservation.LastMatchAttemptedAt,
                item.TrackObservation.LastMatchError
            })
            .ToListAsync(cancellationToken);
        var items = queuedItems.Select(item => new TrackMatchWorkItemResponse(
            item.TrackObservationId,
            item.SourceType,
            item.ExternalId,
            item.Title,
            item.Artist,
            item.LeaseExpiresAt != null && item.LeaseExpiresAt > now.UtcDateTime
                ? "processing"
                : item.NextAttemptAt > now.UtcDateTime ? "scheduled" : "ready",
            item.RetryCount,
            item.MatchAttemptCount,
            new DateTimeOffset(item.NextAttemptAt, TimeSpan.Zero),
            item.LeaseExpiresAt == null ? null : new DateTimeOffset(item.LeaseExpiresAt.Value, TimeSpan.Zero),
            item.LastMatchAttemptedAt,
            item.LastMatchError)).ToList();

        return Ok(new TrackMatchWorkPageResponse(
            items,
            page,
            pageSize,
            totalCount,
            totalPages,
            readyCount,
            scheduledCount,
            processingCount,
            _musicBrainzRequestGate.NotBefore > now ? _musicBrainzRequestGate.NotBefore : null,
            now));
    }

    [HttpGet("song-grouping")]
    public async Task<ActionResult<SongGroupingSuggestionPageResponse>> GetSongGroupingSuggestions(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 5,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 20);
        var pending = _dbContext.SongGroupingSuggestions
            .Where(suggestion =>
                suggestion.Status == SongGroupingSuggestionStatuses.Pending);
        var totalCount = await pending.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);
        var suggestions = await pending
            .Include(suggestion => suggestion.CandidateTrack)
            .Include(suggestion => suggestion.AnchorTrack)
            .OrderByDescending(suggestion => suggestion.Confidence)
            .ThenBy(suggestion => suggestion.CreatedAt)
            .ThenBy(suggestion => suggestion.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Ok(new SongGroupingSuggestionPageResponse(
            suggestions.Select(MapSongGroupingSuggestion).ToList(),
            page,
            pageSize,
            totalCount,
            totalPages));
    }

    [HttpPost("song-grouping/generate")]
    public async Task<ActionResult<GenerateSongGroupingSuggestionsResponse>>
        GenerateSongGroupingSuggestions(CancellationToken cancellationToken)
    {
        var created = await _songGroupingSuggestionService.GenerateAsync(cancellationToken);
        return Ok(new GenerateSongGroupingSuggestionsResponse(created.Count));
    }

    [HttpPost("song-grouping/{suggestionId:guid}/accept")]
    public Task<ActionResult<SongGroupingSuggestionResponse>> AcceptSongGroupingSuggestion(
        Guid suggestionId,
        CancellationToken cancellationToken) =>
        ReviewSongGroupingSuggestion(suggestionId, accept: true, cancellationToken);

    [HttpPost("song-grouping/{suggestionId:guid}/reject")]
    public Task<ActionResult<SongGroupingSuggestionResponse>> RejectSongGroupingSuggestion(
        Guid suggestionId,
        CancellationToken cancellationToken) =>
        ReviewSongGroupingSuggestion(suggestionId, accept: false, cancellationToken);

    [HttpPost("queue/{observationId:guid}/retry")]
    public async Task<IActionResult> Retry(Guid observationId, CancellationToken cancellationToken)
    {
        if (!await ObservationExistsAsync(observationId, cancellationToken))
        {
            return NotFound(new { error = "Track observation not found." });
        }

        await QueueManualRetriesAsync([observationId], cancellationToken);
        return Accepted();
    }

    [HttpPost("queue/retry-filtered")]
    public async Task<ActionResult<MatchingBulkRetryResponse>> RetryFiltered(
        [FromBody] MatchingQueueFilterRequest request,
        CancellationToken cancellationToken)
    {
        var ids = await ApplyQueueFilters(
                _dbContext.TrackObservations.Where(observation =>
                    observation.TrackId == null
                    && observation.MatchStatus != TrackMatchingStatuses.Matched),
                request.Status,
                request.SourceType,
                request.ErrorsOnly,
                request.Query)
            .Select(observation => observation.Id)
            .ToListAsync(cancellationToken);
        await QueueManualRetriesAsync(ids, cancellationToken);
        return Accepted(new MatchingBulkRetryResponse(ids.Count));
    }

    [HttpPost("queue/{observationId:guid}/select-candidate")]
    public async Task<ActionResult<MatchingQueueItemResponse>> SelectCandidate(
        Guid observationId,
        [FromBody] SelectMatchingCandidateRequest request,
        CancellationToken cancellationToken)
    {
        if (!await ObservationExistsAsync(observationId, cancellationToken))
        {
            return NotFound(new { error = "Track observation not found." });
        }

        var observation = await _trackMatchingService.AcceptCandidateAsync(observationId, request.CandidateId, cancellationToken);
        return Ok(await LoadQueueItemAsync(observation.Id, cancellationToken));
    }

    [HttpPost("queue/{observationId:guid}/select-candidate-as-version")]
    public async Task<ActionResult<MatchingQueueItemResponse>> SelectCandidateAsVersion(
        Guid observationId,
        [FromBody] SelectMatchingCandidateAsVersionRequest request,
        CancellationToken cancellationToken)
    {
        if (!await ObservationExistsAsync(observationId, cancellationToken))
        {
            return NotFound(new { error = "Track observation not found." });
        }

        if (request.VersionFlags == TrackVersionFlags.None)
        {
            return BadRequest(new
            {
                error = "A Track version classification is required."
            });
        }

        try
        {
            var observation =
                await _trackMatchingService.AcceptCandidateAsVersionAsync(
                    observationId,
                    request.CandidateId,
                    request.VersionFlags,
                    cancellationToken);
            return Ok(await LoadQueueItemAsync(observation.Id, cancellationToken));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }

    [HttpPost("queue/{observationId:guid}/mark-no-match")]
    public async Task<ActionResult<MatchingQueueItemResponse>> MarkNoMatch(Guid observationId, CancellationToken cancellationToken)
    {
        if (!await ObservationExistsAsync(observationId, cancellationToken))
        {
            return NotFound(new { error = "Track observation not found." });
        }

        var observation = await _trackMatchingService.MarkNoMatchAsync(observationId, cancellationToken);
        return Ok(await LoadQueueItemAsync(observation.Id, cancellationToken));
    }

    [HttpPost("queue/{observationId:guid}/create-track")]
    public async Task<ActionResult<MatchingQueueItemResponse>> CreateTrack(Guid observationId, CancellationToken cancellationToken)
    {
        if (!await ObservationExistsAsync(observationId, cancellationToken))
        {
            return NotFound(new { error = "Track observation not found." });
        }

        var observation = await _trackMatchingService.CreateTrackFromObservationAsync(observationId, cancellationToken);
        return Ok(await LoadQueueItemAsync(observation.Id, cancellationToken));
    }

    private async Task<MatchingQueueItemResponse> LoadQueueItemAsync(Guid observationId, CancellationToken cancellationToken)
    {
        var observation = await _dbContext.TrackObservations
            .Include(o => o.Candidates)
            .Include(o => o.PlaylistEntries)
                .ThenInclude(entry => entry.Playlist)
            .AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == observationId, cancellationToken)
            ?? throw new InvalidOperationException($"Track observation {observationId} was not found.");

        return MapQueueItem(observation, await GetCurrentUserIdAsync());
    }

    private async Task<ActionResult<SongGroupingSuggestionResponse>>
        ReviewSongGroupingSuggestion(
            Guid suggestionId,
            bool accept,
            CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserIdAsync();
        var accessible = await _dbContext.SongGroupingSuggestions.AnyAsync(
            suggestion => suggestion.Id == suggestionId,
            cancellationToken);
        if (!accessible)
        {
            return NotFound(new { error = "Song grouping suggestion not found." });
        }

        try
        {
            await _songGroupingSuggestionService.ReviewAsync(
                suggestionId,
                userId,
                accept,
                cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { error = exception.Message });
        }

        var reviewed = await _dbContext.SongGroupingSuggestions
            .AsNoTracking()
            .Include(suggestion => suggestion.CandidateTrack)
            .Include(suggestion => suggestion.AnchorTrack)
            .SingleAsync(suggestion => suggestion.Id == suggestionId, cancellationToken);
        return Ok(MapSongGroupingSuggestion(reviewed));
    }

    private static SongGroupingSuggestionResponse MapSongGroupingSuggestion(
        SongGroupingSuggestion suggestion) => new(
        suggestion.Id,
        suggestion.Confidence,
        suggestion.EvidenceJson,
        suggestion.CreatedAt,
        MapSongGroupingTrack(suggestion.CandidateTrack!),
        MapSongGroupingTrack(suggestion.AnchorTrack!));

    private static SongGroupingTrackResponse MapSongGroupingTrack(Track track)
    {
        TrackCanonicalMetadata? metadata = null;
        if (!string.IsNullOrWhiteSpace(track.CanonicalMetadata))
        {
            try
            {
                metadata = JsonSerializer.Deserialize<TrackCanonicalMetadata>(
                    track.CanonicalMetadata);
            }
            catch (JsonException)
            {
            }
        }

        return new SongGroupingTrackResponse(
            track.Id,
            metadata?.Title,
            metadata?.Artist,
            track.Isrc,
            track.MbidRecording,
            track.VersionFlags);
    }

    private static MatchingQueueItemResponse MapQueueItem(TrackObservation observation, int userId)
    {
        var storedObservationMetadata = TrackObservationParser.ReadMetadata(observation);
        var parsedObservation = TrackObservationParser.Parse(
            observation,
            storedObservationMetadata);
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
        var storedDiagnostics = storedObservationMetadata?.Matching;
        var effectiveDiagnostics = new TrackMatchObservationDiagnostics
        {
            VersionMarkers =
            [
                .. parsedObservation.VersionMarkers
                    .Concat(storedDiagnostics?.VersionMarkers ?? [])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
            ],
            PlaybackModifiers =
            [
                .. parsedObservation.PlaybackModifiers
                    .Concat(storedDiagnostics?.PlaybackModifiers ?? [])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
            ],
            DecisionReason = storedDiagnostics?.DecisionReason ?? observation.ResolutionNotes,
            TopScore = storedDiagnostics?.TopScore
                ?? (distinctClusterScores.Count > 0 ? distinctClusterScores[0] : null),
            SecondDistinctScore = storedDiagnostics?.SecondDistinctScore
                ?? (distinctClusterScores.Count > 1 ? distinctClusterScores[1] : null),
            DistinctClusterCount = storedDiagnostics?.DistinctClusterCount
                ?? distinctClusterScores.Count
        };

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
            SuggestedVersionFlags = TrackVersionClassifier.Infer(parsedObservation),
            Diagnostics = effectiveDiagnostics,
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
                    DurationScore = observation.DurationSeconds.HasValue && projection.Candidate.DurationSeconds.HasValue
                        ? projection.Diagnostics.DurationScore
                        : null,
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
        var observationMetadata = TrackObservationParser.Parse(observation);
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
                observation.DurationSeconds.HasValue && candidate.DurationSeconds.HasValue
                    ? diagnostics.DurationScore
                    : null),
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

    private Task<bool> ObservationExistsAsync(Guid observationId, CancellationToken cancellationToken) =>
        _dbContext.TrackObservations.AnyAsync(observation => observation.Id == observationId, cancellationToken);

    private static IQueryable<TrackObservation> ApplyQueueFilters(
        IQueryable<TrackObservation> observations,
        string? status,
        string? sourceType,
        bool errorsOnly,
        string? query)
    {
        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = status.Trim().ToLowerInvariant().Replace('-', '_');
            observations = observations.Where(observation => observation.MatchStatus == normalizedStatus);
        }

        if (!string.IsNullOrWhiteSpace(sourceType))
        {
            var normalizedSource = sourceType.Trim().ToLowerInvariant();
            observations = observations.Where(observation => observation.SourceType == normalizedSource);
        }

        if (errorsOnly)
        {
            observations = observations.Where(observation => observation.LastMatchError != null && observation.LastMatchError != "");
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim().ToLowerInvariant();
            observations = observations.Where(observation =>
                observation.Title.ToLower().Contains(term)
                || (observation.Artist != null && observation.Artist.ToLower().Contains(term))
                || observation.ExternalId.ToLower().Contains(term)
                || (observation.LastMatchError != null && observation.LastMatchError.ToLower().Contains(term)));
        }

        return observations;
    }

    private async Task QueueManualRetriesAsync(
        IReadOnlyCollection<Guid> observationIds,
        CancellationToken cancellationToken)
    {
        if (observationIds.Count == 0) return;

        var now = DateTimeOffset.UtcNow;
        await _dbContext.TrackObservations
            .Where(observation => observationIds.Contains(observation.Id)
                && observation.TrackId == null
                && observation.MatchStatus != TrackMatchingStatuses.Matched)
            .ExecuteUpdateAsync(
                updates => updates
                    .SetProperty(observation => observation.MatchStatus, TrackMatchingStatuses.Pending)
                    .SetProperty(observation => observation.ResolutionNotes, "Queued for manual retry.")
                    .SetProperty(observation => observation.UpdatedAt, now),
                cancellationToken);

        foreach (var observationId in observationIds)
        {
            await _trackMatchQueue.EnqueueManualRetryAsync(observationId, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
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
