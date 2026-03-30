using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

public class TrackMatchingService
{
    private const decimal AutoMatchThreshold = 0.85m;
    private const decimal AmbiguousThreshold = 0.65m;
    private const decimal AutoMatchMargin = 0.10m;
    private const int ClusterDurationToleranceSeconds = 30;

    private readonly ApplicationDbContext _dbContext;
    private readonly IEnumerable<ITrackMetadataSearchProvider> _metadataProviders;
    private readonly ILogger<TrackMatchingService> _logger;

    public TrackMatchingService(
        ApplicationDbContext dbContext,
        IEnumerable<ITrackMetadataSearchProvider> metadataProviders,
        ILogger<TrackMatchingService> logger)
    {
        _dbContext = dbContext;
        _metadataProviders = metadataProviders;
        _logger = logger;
    }

    public async Task<TrackObservation> ProcessObservationAsync(Guid observationId, CancellationToken cancellationToken)
    {
        var observation = await _dbContext.TrackObservations
            .Include(o => o.Candidates)
            .FirstOrDefaultAsync(o => o.Id == observationId, cancellationToken)
            ?? throw new InvalidOperationException($"Track observation {observationId} was not found.");

        return await ProcessObservationAsync(observation, cancellationToken);
    }

    public async Task<TrackObservation> AcceptCandidateAsync(Guid observationId, Guid candidateId, CancellationToken cancellationToken)
    {
        var observation = await _dbContext.TrackObservations
            .Include(o => o.Candidates)
            .FirstOrDefaultAsync(o => o.Id == observationId, cancellationToken)
            ?? throw new InvalidOperationException($"Track observation {observationId} was not found.");

        var candidate = observation.Candidates.FirstOrDefault(c => c.Id == candidateId)
            ?? throw new InvalidOperationException($"Candidate {candidateId} does not belong to observation {observationId}.");

        await ResolveObservationToTrackAsync(observation, candidate, "Resolved by manual candidate selection.", cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return observation;
    }

    public async Task<TrackObservation> MarkNoMatchAsync(Guid observationId, CancellationToken cancellationToken)
    {
        var observation = await _dbContext.TrackObservations
            .FirstOrDefaultAsync(o => o.Id == observationId, cancellationToken)
            ?? throw new InvalidOperationException($"Track observation {observationId} was not found.");

        observation.TrackId = null;
        observation.MatchStatus = TrackMatchingStatuses.NoMatch;
        observation.ResolutionNotes = "Marked as no match during review.";
        observation.AcceptedCandidateId = null;
        observation.UpdatedAt = DateTimeOffset.UtcNow;

        await UpdatePlaylistEntriesForObservationAsync(observation.Id, null, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return observation;
    }

    public async Task<TrackObservation> CreateTrackFromObservationAsync(Guid observationId, CancellationToken cancellationToken)
    {
        var observation = await _dbContext.TrackObservations
            .FirstOrDefaultAsync(o => o.Id == observationId, cancellationToken)
            ?? throw new InvalidOperationException($"Track observation {observationId} was not found.");

        var track = await CreateTrackAsync(
            mbidRecording: null,
            isrc: null,
            title: observation.Title,
            artist: observation.Artist,
            durationSeconds: observation.DurationSeconds,
            description: null,
            thumbnailUrl: observation.ThumbnailUrl,
            cancellationToken);

        await EnsureSourceMappingAsync(track.Id, observation.SourceType, observation.ExternalId, cancellationToken);

        observation.TrackId = track.Id;
        observation.MatchStatus = TrackMatchingStatuses.Matched;
        observation.ResolutionNotes = "Created canonical track from observation during manual review.";
        observation.AcceptedCandidateId = null;
        observation.UpdatedAt = DateTimeOffset.UtcNow;

        await UpdatePlaylistEntriesForObservationAsync(observation.Id, track.Id, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return observation;
    }

    private async Task<TrackObservation> ProcessObservationAsync(TrackObservation observation, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        observation.MatchAttemptCount += 1;
        observation.LastMatchAttemptedAt = now;
        observation.LastMatchError = null;
        observation.UpdatedAt = now;

        var exactSourceMatch = await _dbContext.TrackSourceIds
            .AsNoTracking()
            .FirstOrDefaultAsync(
                sourceId => sourceId.SourceType == observation.SourceType && sourceId.ExternalId == observation.ExternalId,
                cancellationToken);

        if (exactSourceMatch != null)
        {
            observation.TrackId = exactSourceMatch.TrackId;
            observation.MatchStatus = TrackMatchingStatuses.Matched;
            observation.ResolutionNotes = "Matched existing source mapping.";
            observation.AcceptedCandidateId = null;
            await UpdatePlaylistEntriesForObservationAsync(observation.Id, exactSourceMatch.TrackId, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return observation;
        }

        try
        {
            if (observation.Candidates.Count > 0)
            {
                _dbContext.TrackResolutionCandidates.RemoveRange(observation.Candidates);
                observation.Candidates.Clear();
            }

            var searchCandidates = new List<(TrackMatchSearchCandidate Candidate, decimal Score)>();
            foreach (var provider in _metadataProviders)
            {
                var providerCandidates = await provider.SearchAsync(observation, cancellationToken);
                searchCandidates.AddRange(providerCandidates.Select(candidate => (candidate, ScoreCandidate(observation, candidate))));
            }

            var rankedCandidates = searchCandidates
                .Where(result => result.Score >= 0.35m)
                .OrderByDescending(result => result.Score)
                .Take(5)
                .ToList();

            var persistedCandidates = new List<TrackResolutionCandidate>(rankedCandidates.Count);
            foreach (var result in rankedCandidates)
            {
                var persistedCandidate = new TrackResolutionCandidate
                {
                    Id = Guid.NewGuid(),
                    TrackObservationId = observation.Id,
                    CandidateSource = result.Candidate.CandidateSource,
                    ExternalId = result.Candidate.ExternalId,
                    Title = result.Candidate.Title,
                    Artist = result.Candidate.Artist,
                    MbidRecording = result.Candidate.MbidRecording,
                    Isrc = result.Candidate.Isrc,
                    DurationSeconds = result.Candidate.DurationSeconds,
                    Score = result.Score,
                    Explanation = BuildCandidateExplanation(observation, result.Candidate, result.Score),
                    RawMetadata = result.Candidate.RawMetadata,
                    CreatedAt = now
                };

                _dbContext.TrackResolutionCandidates.Add(persistedCandidate);
                persistedCandidates.Add(persistedCandidate);
            }

            if (rankedCandidates.Count == 0)
            {
                observation.TrackId = null;
                observation.MatchStatus = TrackMatchingStatuses.NoMatch;
                observation.ResolutionNotes = "No credible candidate was found.";
                observation.AcceptedCandidateId = null;
                await UpdatePlaylistEntriesForObservationAsync(observation.Id, null, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return observation;
            }

            var topCandidate = rankedCandidates[0];
            var distinctClusters = CollapseDuplicateClusters(rankedCandidates);
            var secondDistinctScore = distinctClusters.Count > 1 ? distinctClusters[1].Score : 0m;

            if (topCandidate.Score >= AutoMatchThreshold && topCandidate.Score - secondDistinctScore >= AutoMatchMargin)
            {
                var persistedCandidate = persistedCandidates
                    .OrderByDescending(candidate => candidate.Score)
                    .First();

                await ResolveObservationToTrackAsync(observation, persistedCandidate, "Automatically resolved by the matcher.", cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return observation;
            }

            observation.TrackId = null;
            observation.AcceptedCandidateId = null;
            observation.MatchStatus = topCandidate.Score >= AmbiguousThreshold
                ? TrackMatchingStatuses.Ambiguous
                : TrackMatchingStatuses.NoMatch;
            observation.ResolutionNotes = observation.MatchStatus == TrackMatchingStatuses.Ambiguous
                ? "Multiple plausible candidates require review."
                : "Candidates were found, but confidence stayed below the auto-match threshold.";

            await UpdatePlaylistEntriesForObservationAsync(observation.Id, null, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return observation;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Track matching failed for observation {ObservationId}", observation.Id);
            observation.TrackId = null;
            observation.MatchStatus = TrackMatchingStatuses.Pending;
            observation.LastMatchError = ex.Message;
            observation.ResolutionNotes = "Matching attempt failed. Retry is required.";
            await UpdatePlaylistEntriesForObservationAsync(observation.Id, null, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return observation;
        }
    }

    private async Task ResolveObservationToTrackAsync(
        TrackObservation observation,
        TrackResolutionCandidate candidate,
        string resolutionNotes,
        CancellationToken cancellationToken)
    {
        var existingTrack = await FindExistingTrackAsync(candidate.MbidRecording, candidate.Isrc, cancellationToken);
        var track = existingTrack ?? await CreateTrackAsync(
            candidate.MbidRecording,
            candidate.Isrc,
            candidate.Title,
            candidate.Artist,
            candidate.DurationSeconds ?? observation.DurationSeconds,
            description: null,
            thumbnailUrl: observation.ThumbnailUrl,
            cancellationToken);

        await EnsureSourceMappingAsync(track.Id, observation.SourceType, observation.ExternalId, cancellationToken);

        if (!string.IsNullOrWhiteSpace(candidate.MbidRecording))
        {
            await EnsureSourceMappingAsync(track.Id, "musicbrainz", candidate.MbidRecording, cancellationToken);
        }

        candidate.IsAccepted = true;
        observation.TrackId = track.Id;
        observation.MatchStatus = TrackMatchingStatuses.Matched;
        observation.AcceptedCandidateId = candidate.Id;
        observation.ResolutionNotes = resolutionNotes;
        observation.UpdatedAt = DateTimeOffset.UtcNow;

        await UpdatePlaylistEntriesForObservationAsync(observation.Id, track.Id, cancellationToken);
    }

    private async Task<Track?> FindExistingTrackAsync(string? mbidRecording, string? isrc, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(mbidRecording))
        {
            var exactMusicBrainzMapping = await _dbContext.TrackSourceIds
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    sourceId => sourceId.SourceType == "musicbrainz" && sourceId.ExternalId == mbidRecording,
                    cancellationToken);

            if (exactMusicBrainzMapping != null)
            {
                return await _dbContext.Tracks.FirstOrDefaultAsync(track => track.Id == exactMusicBrainzMapping.TrackId, cancellationToken);
            }

            var mbidTrack = await _dbContext.Tracks.FirstOrDefaultAsync(track => track.MbidRecording == mbidRecording, cancellationToken);
            if (mbidTrack != null)
            {
                return mbidTrack;
            }
        }

        if (!string.IsNullOrWhiteSpace(isrc))
        {
            return await _dbContext.Tracks.FirstOrDefaultAsync(track => track.Isrc == isrc, cancellationToken);
        }

        return null;
    }

    private async Task<Track> CreateTrackAsync(
        string? mbidRecording,
        string? isrc,
        string title,
        string? artist,
        int? durationSeconds,
        string? description,
        string? thumbnailUrl,
        CancellationToken cancellationToken)
    {
        var track = new Track
        {
            Id = Guid.NewGuid(),
            MbidRecording = mbidRecording,
            Isrc = isrc,
            CanonicalMetadata = JsonSerializer.Serialize(new TrackCanonicalMetadata
            {
                Title = title,
                Artist = artist,
                Description = description,
                ThumbnailUrl = thumbnailUrl,
                DurationSeconds = durationSeconds
            }),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.Tracks.Add(track);
        return track;
    }

    private async Task EnsureSourceMappingAsync(Guid trackId, string sourceType, string externalId, CancellationToken cancellationToken)
    {
        var existingSourceId = await _dbContext.TrackSourceIds
            .FirstOrDefaultAsync(sourceId => sourceId.SourceType == sourceType && sourceId.ExternalId == externalId, cancellationToken);

        if (existingSourceId != null)
        {
            if (existingSourceId.TrackId != trackId)
            {
                existingSourceId.TrackId = trackId;
            }

            existingSourceId.LastVerifiedAt = DateTimeOffset.UtcNow;
            return;
        }

        _dbContext.TrackSourceIds.Add(new TrackSourceId
        {
            Id = Guid.NewGuid(),
            TrackId = trackId,
            SourceType = sourceType,
            ExternalId = externalId,
            LastVerifiedAt = DateTimeOffset.UtcNow
        });
    }

    private async Task UpdatePlaylistEntriesForObservationAsync(Guid observationId, Guid? trackId, CancellationToken cancellationToken)
    {
        var entries = await _dbContext.PlaylistEntries
            .Where(entry => entry.TrackObservationId == observationId)
            .ToListAsync(cancellationToken);

        foreach (var entry in entries)
        {
            entry.TrackId = trackId;
        }
    }

    private static decimal ScoreCandidate(TrackObservation observation, TrackMatchSearchCandidate candidate)
    {
        var parsedMetadata = TrackMetadataParser.Parse(observation.Title, observation.Artist);
        var titleScore = BestSimilarity(candidate.Title, observation.Title, parsedMetadata.DisplayTitle, parsedMetadata.SearchTitle);
        var artistScore = BestSimilarity(candidate.Artist, observation.Artist, parsedMetadata.DisplayArtist, parsedMetadata.SearchArtist);

        decimal durationScore = 0m;
        if (observation.DurationSeconds.HasValue && candidate.DurationSeconds.HasValue)
        {
            var difference = Math.Abs(observation.DurationSeconds.Value - candidate.DurationSeconds.Value);
            durationScore = difference switch
            {
                <= 2 => 1m,
                <= 5 => 0.7m,
                <= 10 => 0.4m,
                _ => 0m
            };
        }

        var score = (titleScore * 0.55m) + (artistScore * 0.30m) + (durationScore * 0.15m);
        return Math.Round(score, 3, MidpointRounding.AwayFromZero);
    }

    private static string BuildCandidateExplanation(TrackObservation observation, TrackMatchSearchCandidate candidate, decimal score)
    {
        var parsedMetadata = TrackMetadataParser.Parse(observation.Title, observation.Artist);
        var titleSimilarity = BestSimilarity(candidate.Title, observation.Title, parsedMetadata.DisplayTitle, parsedMetadata.SearchTitle);
        var artistSimilarity = BestSimilarity(candidate.Artist, observation.Artist, parsedMetadata.DisplayArtist, parsedMetadata.SearchArtist);

        var interpretation = string.Empty;
        if (!string.Equals(parsedMetadata.SearchTitle, observation.Title, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(parsedMetadata.SearchArtist, observation.Artist, StringComparison.OrdinalIgnoreCase))
        {
            interpretation = $" Interpreted as title '{parsedMetadata.SearchTitle}'";
            if (!string.IsNullOrWhiteSpace(parsedMetadata.SearchArtist))
            {
                interpretation += $" and artist '{parsedMetadata.SearchArtist}'";
            }

            interpretation += ".";
        }

        return $"{candidate.Explanation}{interpretation} Title similarity: {titleSimilarity:P0}; artist similarity: {artistSimilarity:P0}; total confidence: {score:P0}.";
    }

    private static List<(TrackMatchSearchCandidate Candidate, decimal Score)> CollapseDuplicateClusters(
        List<(TrackMatchSearchCandidate Candidate, decimal Score)> rankedCandidates)
    {
        if (rankedCandidates.Count <= 1)
        {
            return rankedCandidates;
        }

        var representatives = new List<(TrackMatchSearchCandidate Candidate, decimal Score)>();

        foreach (var candidate in rankedCandidates)
        {
            var normalizedTitle = TrackTextNormalizer.Normalize(candidate.Candidate.Title);
            var normalizedArtist = TrackTextNormalizer.Normalize(candidate.Candidate.Artist);

            var isDuplicate = representatives.Any(rep =>
            {
                var repTitle = TrackTextNormalizer.Normalize(rep.Candidate.Title);
                var repArtist = TrackTextNormalizer.Normalize(rep.Candidate.Artist);
                return normalizedTitle == repTitle
                    && normalizedArtist == repArtist
                    && AreDurationsClose(candidate.Candidate.DurationSeconds, rep.Candidate.DurationSeconds);
            });

            if (!isDuplicate)
            {
                representatives.Add(candidate);
            }
        }

        return representatives;
    }

    private static bool AreDurationsClose(int? a, int? b)
    {
        // When either duration is unknown, treat candidates as potentially the same
        // recording rather than splitting them into separate clusters.
        if (!a.HasValue || !b.HasValue)
        {
            return true;
        }

        return Math.Abs(a.Value - b.Value) <= ClusterDurationToleranceSeconds;
    }

    private static decimal BestSimilarity(string? candidateValue, params string?[] values)
    {
        if (string.IsNullOrWhiteSpace(candidateValue))
        {
            return 0m;
        }

        var best = 0m;
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            best = Math.Max(best, TrackTextNormalizer.CalculateSimilarity(value, candidateValue));
        }

        return best;
    }
}
