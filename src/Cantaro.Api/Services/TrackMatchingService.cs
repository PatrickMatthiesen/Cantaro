using System.Text.Json;
using Cantaro.Api.Configuration;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cantaro.Api.Services;

public class TrackMatchingService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEnumerable<ITrackMetadataSearchProvider> _metadataProviders;
    private readonly ILogger<TrackMatchingService> _logger;
    private readonly TrackMatchingOptions _options;
    private readonly TrackIdentityResolver _identityResolver;
    private readonly PlaylistCanonicalReconciliationService _playlistReconciler;

    public TrackMatchingService(
        ApplicationDbContext dbContext,
        IEnumerable<ITrackMetadataSearchProvider> metadataProviders,
        ILogger<TrackMatchingService> logger)
        : this(dbContext, metadataProviders, logger, Options.Create(new TrackMatchingOptions()))
    {
    }

    public TrackMatchingService(
        ApplicationDbContext dbContext,
        IEnumerable<ITrackMetadataSearchProvider> metadataProviders,
        ILogger<TrackMatchingService> logger,
        IOptions<TrackMatchingOptions> options)
        : this(
            dbContext,
            metadataProviders,
            logger,
            options,
            new TrackIdentityResolver(dbContext, options))
    {
    }

    public TrackMatchingService(
        ApplicationDbContext dbContext,
        IEnumerable<ITrackMetadataSearchProvider> metadataProviders,
        ILogger<TrackMatchingService> logger,
        IOptions<TrackMatchingOptions> options,
        TrackIdentityResolver identityResolver,
        PlaylistCanonicalReconciliationService? playlistReconciler = null)
    {
        _dbContext = dbContext;
        _metadataProviders = metadataProviders;
        _logger = logger;
        _options = options.Value;
        _identityResolver = identityResolver;
        _playlistReconciler = playlistReconciler ?? new PlaylistCanonicalReconciliationService(dbContext);
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

    public async Task<TrackObservation> AcceptCandidateAsVersionAsync(
        Guid observationId,
        Guid candidateId,
        TrackVersionFlags versionFlags,
        CancellationToken cancellationToken)
    {
        ValidateVersionFlags(versionFlags);

        var observation = await _dbContext.TrackObservations
            .Include(item => item.Candidates)
            .FirstOrDefaultAsync(item => item.Id == observationId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Track observation {observationId} was not found.");
        var candidate = observation.Candidates.FirstOrDefault(item => item.Id == candidateId)
            ?? throw new InvalidOperationException(
                $"Candidate {candidateId} does not belong to observation {observationId}.");

        if (observation.TrackId != null
            || observation.MatchStatus == TrackMatchingStatuses.Matched)
        {
            if (await IsAcceptedCandidateVersionResolutionAsync(
                    observation,
                    candidateId,
                    versionFlags,
                    cancellationToken))
            {
                return observation;
            }

            throw new InvalidOperationException(
                "This observation has already been resolved to a Track.");
        }

        var originalUpdatedAt = observation.UpdatedAt;
        var claimedAt = DateTimeOffset.UtcNow;
        var claimed = await _dbContext.TrackObservations
            .Where(item =>
                item.Id == observationId
                && item.TrackId == null
                && item.MatchStatus != TrackMatchingStatuses.Matched
                && item.UpdatedAt == originalUpdatedAt)
            .ExecuteUpdateAsync(
                updates => updates.SetProperty(
                    item => item.UpdatedAt,
                    claimedAt),
                cancellationToken);
        if (claimed == 0)
        {
            _dbContext.ChangeTracker.Clear();
            var current = await _dbContext.TrackObservations
                .Include(item => item.Candidates)
                .SingleAsync(item => item.Id == observationId, cancellationToken);
            if (await IsAcceptedCandidateVersionResolutionAsync(
                    current,
                    candidateId,
                    versionFlags,
                    cancellationToken))
            {
                return current;
            }

            throw new InvalidOperationException(
                "This observation is already being resolved or has been resolved to another Track.");
        }
        observation.UpdatedAt = claimedAt;

        var anchorTrack = await ResolveCandidateAnchorTrackAsync(
            candidate,
            cancellationToken);
        var targetSongId = await GetSingleSongIdAsync(
            anchorTrack.Id,
            cancellationToken);
        var parsedObservation = TrackObservationParser.Parse(observation);
        var artistIdentity = ReadCandidateArtistIdentity(candidate.RawMetadata);
        var versionEvidence = CreateVersionEvidence(
            observation,
            candidate,
            parsedObservation,
            versionFlags,
            "manual-candidate-version");
        var versionTrack = await CreateTrackAsync(
            mbidRecording: null,
            isrc: null,
            title: candidate.Title,
            artist: candidate.Artist ?? parsedObservation.DisplayArtist,
            durationSeconds: observation.DurationSeconds,
            description: null,
            thumbnailUrl: observation.ThumbnailUrl,
            artistMusicBrainzId: artistIdentity.MusicBrainzId,
            artistSortName: artistIdentity.SortName,
            targetSongId,
            versionFlags,
            versionEvidence,
            cancellationToken);

        await EnsureSourceMappingAsync(
            versionTrack.Id,
            observation.SourceType,
            observation.ExternalId,
            cancellationToken);

        candidate.IsAccepted = true;
        observation.TrackId = versionTrack.Id;
        observation.MatchStatus = TrackMatchingStatuses.Matched;
        observation.AcceptedCandidateId = candidate.Id;
        observation.ResolutionNotes =
            $"Created a {DescribeVersion(versionFlags)} version Track under the selected candidate's Song.";
        observation.UpdatedAt = DateTimeOffset.UtcNow;

        await _playlistReconciler.ReconcileObservationAsync(
            observation.Id,
            versionTrack.Id,
            cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return observation;
    }

    private async Task<bool> IsAcceptedCandidateVersionResolutionAsync(
        TrackObservation observation,
        Guid candidateId,
        TrackVersionFlags versionFlags,
        CancellationToken cancellationToken)
    {
        if (observation.TrackId == null
            || observation.AcceptedCandidateId != candidateId
            || observation.MatchStatus != TrackMatchingStatuses.Matched)
        {
            return false;
        }

        var track = await _dbContext.Tracks
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == observation.TrackId,
                cancellationToken);
        if (track?.VersionFlags != versionFlags
            || string.IsNullOrWhiteSpace(track.VersionEvidence))
        {
            return false;
        }

        try
        {
            using var evidence = JsonDocument.Parse(track.VersionEvidence);
            var root = evidence.RootElement;
            return root.TryGetProperty("origin", out var origin)
                && string.Equals(
                    origin.GetString(),
                    "manual-candidate-version",
                    StringComparison.Ordinal)
                && root.TryGetProperty("observationId", out var observationId)
                && observationId.TryGetGuid(out var evidenceObservationId)
                && evidenceObservationId == observation.Id
                && root.TryGetProperty("candidateId", out var acceptedCandidateId)
                && acceptedCandidateId.TryGetGuid(out var evidenceCandidateId)
                && evidenceCandidateId == candidateId;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public async Task<TrackObservation> MarkNotMusicAsync(Guid observationId, CancellationToken cancellationToken)
    {
        var observation = await _dbContext.TrackObservations
            .FirstOrDefaultAsync(o => o.Id == observationId, cancellationToken)
            ?? throw new InvalidOperationException($"Track observation {observationId} was not found.");

        if (observation.TrackId != null || observation.MatchStatus == TrackMatchingStatuses.Matched)
        {
            throw new InvalidOperationException("A matched observation cannot be marked as not music.");
        }

        observation.TrackId = null;
        observation.MatchStatus = TrackMatchingStatuses.NotMusic;
        observation.LastMatchError = null;
        observation.ResolutionNotes = "Marked as not music during review.";
        observation.AcceptedCandidateId = null;
        observation.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.TrackMatchQueueItems
            .Where(item => item.TrackObservationId == observationId)
            .ExecuteDeleteAsync(cancellationToken);
        foreach (var trackedQueueItem in _dbContext.TrackMatchQueueItems.Local
                     .Where(item => item.TrackObservationId == observationId)
                     .ToList())
        {
            _dbContext.Entry(trackedQueueItem).State = EntityState.Detached;
        }

        await _playlistReconciler.ReconcileObservationAsync(observation.Id, null, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return observation;
    }

    public async Task<TrackObservation> CreateTrackFromObservationAsync(Guid observationId, CancellationToken cancellationToken)
    {
        var observation = await _dbContext.TrackObservations
            .FirstOrDefaultAsync(o => o.Id == observationId, cancellationToken)
            ?? throw new InvalidOperationException($"Track observation {observationId} was not found.");

        var parsedObservation = TrackObservationParser.Parse(observation);
        var inferredFlags = TrackVersionClassifier.Infer(parsedObservation);
        var track = await CreateTrackAsync(
            mbidRecording: null,
            isrc: null,
            title: parsedObservation.DisplayTitle,
            artist: parsedObservation.DisplayArtist,
            durationSeconds: observation.DurationSeconds,
            description: null,
            thumbnailUrl: observation.ThumbnailUrl,
            artistMusicBrainzId: null,
            artistSortName: null,
            targetSongId: null,
            inferredFlags,
            CreateVersionEvidence(
                observation,
                candidate: null,
                parsedObservation,
                inferredFlags,
                "manual-observation-track"),
            cancellationToken);

        await EnsureSourceMappingAsync(track.Id, observation.SourceType, observation.ExternalId, cancellationToken);

        observation.TrackId = track.Id;
        observation.MatchStatus = TrackMatchingStatuses.Matched;
        observation.ResolutionNotes = "Created canonical track from observation during manual review.";
        observation.AcceptedCandidateId = null;
        observation.UpdatedAt = DateTimeOffset.UtcNow;

        await _playlistReconciler.ReconcileObservationAsync(observation.Id, track.Id, cancellationToken);
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

        var parsedObservation = TrackObservationParser.Parse(observation);
        var localIdentityMatch = await _identityResolver.ResolveExistingAsync(
            new TrackIdentityQuery(
                observation.SourceType,
                observation.ExternalId,
                observation.Title,
                observation.Artist,
                observation.DurationSeconds,
                ArtistCredits: parsedObservation.ArtistCredits),
            cancellationToken);

        if (localIdentityMatch != null)
        {
            if (localIdentityMatch.Kind != TrackIdentityMatchKind.ExactSource
                && observation.Candidates.Count > 0)
            {
                _dbContext.TrackResolutionCandidates.RemoveRange(observation.Candidates);
                observation.Candidates.Clear();
            }

            await EnsurePrimaryArtistCreditAsync(
                localIdentityMatch.Track,
                ReadCanonicalArtist(localIdentityMatch.Track) ?? observation.Artist,
                artistMusicBrainzId: null,
                artistSortName: null,
                cancellationToken);
            TrackArtworkUpdater.FillMissingCanonicalThumbnail(
                localIdentityMatch.Track,
                observation.ThumbnailUrl);
            if (localIdentityMatch.Kind != TrackIdentityMatchKind.ExactSource)
            {
                await EnsureSourceMappingAsync(
                    localIdentityMatch.Track.Id,
                    observation.SourceType,
                    observation.ExternalId,
                    cancellationToken);
            }

            observation.TrackId = localIdentityMatch.Track.Id;
            observation.MatchStatus = TrackMatchingStatuses.Matched;
            observation.ResolutionNotes = localIdentityMatch.Reason;
            observation.AcceptedCandidateId = null;
            PersistObservationDiagnostics(
                observation,
                CreateObservationDiagnostics(
                    parsedObservation,
                    decisionReason: observation.ResolutionNotes,
                    topScore: localIdentityMatch.Kind == TrackIdentityMatchKind.ExactSource ? null : 1m,
                    secondDistinctScore: null,
                    distinctClusterCount: localIdentityMatch.Kind == TrackIdentityMatchKind.ExactSource ? 0 : 1));
            await _playlistReconciler.ReconcileObservationAsync(
                observation.Id,
                localIdentityMatch.Track.Id,
                cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return observation;
        }

        try
        {
            var scoredCandidates = new List<TrackMatchScoredCandidate>();
            foreach (var provider in _metadataProviders)
            {
                var providerCandidates = await provider.SearchAsync(observation, cancellationToken);
                scoredCandidates.AddRange(providerCandidates.Select(candidate => TrackMatchScorer.Score(observation, candidate, _options)));
            }

            if (observation.Candidates.Count > 0)
            {
                _dbContext.TrackResolutionCandidates.RemoveRange(observation.Candidates);
                observation.Candidates.Clear();
            }

            var rankedCandidates = scoredCandidates
                .Where(result => result.Score >= _options.MinimumCandidateScore)
                .OrderByDescending(result => result.Score)
                .ToList();

            var clusters = TrackMatchClusterer.BuildClusters(rankedCandidates, _options.ClusterDurationToleranceSeconds);
            var decision = TrackMatchDecisionEngine.Decide(
                rankedCandidates,
                clusters,
                _options.AutoMatchThreshold,
                _options.AmbiguousThreshold,
                _options.AutoMatchMargin);

            var displayedCandidates = SelectDisplayedCandidates(rankedCandidates, clusters, maximumCount: 5);

            var persistedCandidates = new List<TrackResolutionCandidate>(displayedCandidates.Count);
            foreach (var result in displayedCandidates)
            {
                var cluster = clusters.FirstOrDefault(existingCluster => existingCluster.Members.Any(member => member.Candidate.ExternalId == result.Candidate.ExternalId));
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
                    Explanation = BuildCandidateExplanation(result),
                    RawMetadata = SerializeCandidateMetadata(result, cluster),
                    CreatedAt = now
                };

                _dbContext.TrackResolutionCandidates.Add(persistedCandidate);
                persistedCandidates.Add(persistedCandidate);
            }

            if (rankedCandidates.Count == 0)
            {
                observation.TrackId = null;
                observation.MatchStatus = decision.MatchStatus;
                observation.ResolutionNotes = decision.ResolutionNotes;
                observation.AcceptedCandidateId = null;
                PersistObservationDiagnostics(observation, CreateObservationDiagnostics(
                    rankedCandidates.FirstOrDefault()?.ObservationMetadata ?? TrackObservationParser.Parse(observation),
                    decision.DecisionReason,
                    decision.TopScore,
                    decision.SecondDistinctScore,
                    clusters.Count));
                await _playlistReconciler.ReconcileObservationAsync(observation.Id, null, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return observation;
            }

            if (decision.AcceptedCandidate != null)
            {
                var persistedCandidate = persistedCandidates
                    .OrderByDescending(candidate => candidate.Score)
                    .ThenBy(candidate => candidate.Title, StringComparer.Ordinal)
                    .First(candidate => candidate.ExternalId == decision.AcceptedCandidate.Candidate.ExternalId);

                PersistObservationDiagnostics(
                    observation,
                    CreateObservationDiagnostics(
                        decision.AcceptedCandidate.ObservationMetadata,
                        decision.DecisionReason,
                        decision.TopScore,
                        decision.SecondDistinctScore,
                        clusters.Count));

                await ResolveObservationToTrackAsync(observation, persistedCandidate, decision.ResolutionNotes, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return observation;
            }

            observation.TrackId = null;
            observation.AcceptedCandidateId = null;
            observation.MatchStatus = decision.MatchStatus;
            observation.ResolutionNotes = decision.ResolutionNotes;
            PersistObservationDiagnostics(
                observation,
                CreateObservationDiagnostics(
                    rankedCandidates[0].ObservationMetadata,
                    decision.DecisionReason,
                    decision.TopScore,
                    decision.SecondDistinctScore,
                    clusters.Count));

            await _playlistReconciler.ReconcileObservationAsync(observation.Id, null, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return observation;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Track matching failed for observation {ObservationId}", observation.Id);
            observation.TrackId = null;
            observation.MatchStatus = TrackMatchingStatuses.Pending;
            observation.LastMatchError = ex.Message;
            observation.ResolutionNotes = "Matching attempt failed. Retry is required.";
            PersistObservationDiagnostics(
                observation,
                CreateObservationDiagnostics(
                    TrackObservationParser.Parse(observation),
                    $"Matching failed: {ex.Message}",
                    topScore: null,
                    secondDistinctScore: null,
                    distinctClusterCount: 0));
            await _playlistReconciler.ReconcileObservationAsync(observation.Id, null, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return observation;
        }
    }

    private static IReadOnlyList<TrackMatchScoredCandidate> SelectDisplayedCandidates(
        IReadOnlyList<TrackMatchScoredCandidate> rankedCandidates,
        IReadOnlyList<TrackMatchCluster> clusters,
        int maximumCount)
    {
        var selected = new List<TrackMatchScoredCandidate>(maximumCount);
        foreach (var cluster in clusters.Take(maximumCount))
        {
            selected.Add(cluster.Representative);
        }

        foreach (var candidate in rankedCandidates)
        {
            if (selected.Count >= maximumCount)
            {
                break;
            }

            if (!selected.Any(existing => existing.Candidate.ExternalId == candidate.Candidate.ExternalId))
            {
                selected.Add(candidate);
            }
        }

        return selected
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Candidate.Title, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task ResolveObservationToTrackAsync(
        TrackObservation observation,
        TrackResolutionCandidate candidate,
        string resolutionNotes,
        CancellationToken cancellationToken)
    {
        var identityMatch = await _identityResolver.ResolveExistingAsync(
            new TrackIdentityQuery(
                observation.SourceType,
                observation.ExternalId,
                candidate.Title,
                candidate.Artist,
                candidate.DurationSeconds ?? observation.DurationSeconds,
                candidate.Isrc,
                candidate.MbidRecording,
                TrackMetadataParser.Parse(candidate.Title, candidate.Artist).ArtistCredits),
            cancellationToken);
        var existingTrack = identityMatch?.Track;
        var artistIdentity = ReadCandidateArtistIdentity(candidate.RawMetadata);
        var track = existingTrack ?? await CreateTrackAsync(
            candidate.MbidRecording,
            candidate.Isrc,
            candidate.Title,
            candidate.Artist,
            candidate.DurationSeconds ?? observation.DurationSeconds,
            description: null,
            thumbnailUrl: observation.ThumbnailUrl,
            artistMusicBrainzId: artistIdentity.MusicBrainzId,
            artistSortName: artistIdentity.SortName,
            cancellationToken);

        if (existingTrack != null)
        {
            await EnsurePrimaryArtistCreditAsync(
                track,
                candidate.Artist,
                artistIdentity.MusicBrainzId,
                artistIdentity.SortName,
                cancellationToken);
            TrackArtworkUpdater.FillMissingCanonicalThumbnail(
                track,
                observation.ThumbnailUrl);
        }

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

        await _playlistReconciler.ReconcileObservationAsync(observation.Id, track.Id, cancellationToken);
    }

    private async Task<Track> ResolveCandidateAnchorTrackAsync(
        TrackResolutionCandidate candidate,
        CancellationToken cancellationToken)
    {
        var parsedCandidate = TrackMetadataParser.Parse(
            candidate.Title,
            candidate.Artist);
        var identityMatch = await _identityResolver.ResolveExistingAsync(
            new TrackIdentityQuery(
                candidate.CandidateSource,
                candidate.ExternalId,
                candidate.Title,
                candidate.Artist,
                candidate.DurationSeconds,
                candidate.Isrc,
                candidate.MbidRecording,
                parsedCandidate.ArtistCredits),
            cancellationToken);
        var artistIdentity = ReadCandidateArtistIdentity(candidate.RawMetadata);
        var anchorTrack = identityMatch?.Track ?? await CreateTrackAsync(
            candidate.MbidRecording,
            candidate.Isrc,
            candidate.Title,
            candidate.Artist,
            candidate.DurationSeconds,
            description: null,
            thumbnailUrl: null,
            artistMusicBrainzId: artistIdentity.MusicBrainzId,
            artistSortName: artistIdentity.SortName,
            cancellationToken);

        if (identityMatch != null)
        {
            if (string.IsNullOrWhiteSpace(anchorTrack.MbidRecording)
                && !string.IsNullOrWhiteSpace(candidate.MbidRecording))
            {
                anchorTrack.MbidRecording = candidate.MbidRecording.Trim().ToLowerInvariant();
            }

            if (string.IsNullOrWhiteSpace(anchorTrack.Isrc)
                && !string.IsNullOrWhiteSpace(candidate.Isrc))
            {
                anchorTrack.Isrc = TrackIdentityResolver.NormalizeIsrc(candidate.Isrc);
            }

            anchorTrack.UpdatedAt = DateTimeOffset.UtcNow;
            await EnsurePrimaryArtistCreditAsync(
                anchorTrack,
                candidate.Artist,
                artistIdentity.MusicBrainzId,
                artistIdentity.SortName,
                cancellationToken);
        }

        await EnsureSourceMappingAsync(
            anchorTrack.Id,
            candidate.CandidateSource,
            candidate.ExternalId,
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(candidate.MbidRecording)
            && (!string.Equals(
                    candidate.CandidateSource,
                    "musicbrainz",
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    candidate.ExternalId,
                    candidate.MbidRecording,
                    StringComparison.OrdinalIgnoreCase)))
        {
            await EnsureSourceMappingAsync(
                anchorTrack.Id,
                "musicbrainz",
                candidate.MbidRecording,
                cancellationToken);
        }

        return anchorTrack;
    }

    private async Task<Guid> GetSingleSongIdAsync(
        Guid trackId,
        CancellationToken cancellationToken)
    {
        var songIds = _dbContext.SongTracks.Local
            .Where(membership => membership.TrackId == trackId)
            .Select(membership => membership.SongId)
            .Concat(await _dbContext.SongTracks
                .Where(membership => membership.TrackId == trackId)
                .Select(membership => membership.SongId)
                .Take(2)
                .ToListAsync(cancellationToken))
            .Distinct()
            .Take(2)
            .ToArray();
        if (songIds.Length == 1)
        {
            return songIds[0];
        }

        if (songIds.Length > 1)
        {
            throw new InvalidOperationException(
                "The selected candidate Track belongs to multiple Songs and requires separate review.");
        }

        var now = DateTimeOffset.UtcNow;
        var song = new Song
        {
            Id = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now
        };
        _dbContext.AddRange(song, new SongTrack
        {
            SongId = song.Id,
            Song = song,
            TrackId = trackId
        });
        return song.Id;
    }

    private async Task<Track> CreateTrackAsync(
        string? mbidRecording,
        string? isrc,
        string title,
        string? artist,
        int? durationSeconds,
        string? description,
        string? thumbnailUrl,
        string? artistMusicBrainzId,
        string? artistSortName,
        CancellationToken cancellationToken)
        => await CreateTrackAsync(
            mbidRecording,
            isrc,
            title,
            artist,
            durationSeconds,
            description,
            thumbnailUrl,
            artistMusicBrainzId,
            artistSortName,
            targetSongId: null,
            TrackVersionFlags.None,
            versionEvidence: null,
            cancellationToken);

    private async Task<Track> CreateTrackAsync(
        string? mbidRecording,
        string? isrc,
        string title,
        string? artist,
        int? durationSeconds,
        string? description,
        string? thumbnailUrl,
        string? artistMusicBrainzId,
        string? artistSortName,
        Guid? targetSongId,
        TrackVersionFlags versionFlags,
        string? versionEvidence,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var song = targetSongId == null
            ? new Song
            {
                Id = Guid.NewGuid(),
                CreatedAt = now,
                UpdatedAt = now
            }
            : null;
        var songId = targetSongId ?? song!.Id;
        var track = new Track
        {
            Id = Guid.NewGuid(),
            MbidRecording = mbidRecording,
            Isrc = TrackIdentityResolver.NormalizeIsrc(isrc),
            VersionFlags = versionFlags,
            VersionEvidence = versionEvidence,
            CanonicalMetadata = JsonSerializer.Serialize(new TrackCanonicalMetadata
            {
                Title = title,
                Artist = artist,
                Description = description,
                ThumbnailUrl = thumbnailUrl,
                DurationSeconds = durationSeconds
            }),
            CreatedAt = now,
            UpdatedAt = now
        };
        var membership = new SongTrack
        {
            SongId = songId,
            Song = song,
            TrackId = track.Id,
            Track = track
        };

        if (song == null)
        {
            _dbContext.AddRange(track, membership);
        }
        else
        {
            _dbContext.AddRange(song, track, membership);
        }
        await EnsurePrimaryArtistCreditAsync(
            track,
            artist,
            artistMusicBrainzId,
            artistSortName,
            cancellationToken);
        return track;
    }

    private static string CreateVersionEvidence(
        TrackObservation observation,
        TrackResolutionCandidate? candidate,
        ParsedTrackMetadata parsedObservation,
        TrackVersionFlags selectedFlags,
        string origin) =>
        JsonSerializer.Serialize(new
        {
            origin,
            selectedFlags = selectedFlags.ToString(),
            observationId = observation.Id,
            observationTitle = observation.Title,
            observationArtist = observation.Artist,
            detectedVersionMarkers = parsedObservation.VersionMarkers,
            detectedPlaybackModifiers = parsedObservation.PlaybackModifiers,
            candidateId = candidate?.Id,
            candidateExternalId = candidate?.ExternalId
        });

    private static void ValidateVersionFlags(TrackVersionFlags versionFlags)
    {
        var knownFlags = Enum.GetValues<TrackVersionFlags>()
            .Aggregate(TrackVersionFlags.None, (current, value) => current | value);
        if (versionFlags == TrackVersionFlags.None
            || (versionFlags & ~knownFlags) != TrackVersionFlags.None)
        {
            throw new ArgumentOutOfRangeException(
                nameof(versionFlags),
                versionFlags,
                "A known non-empty Track version classification is required.");
        }
    }

    private static string DescribeVersion(TrackVersionFlags versionFlags) =>
        versionFlags.ToString().Replace(",", " +", StringComparison.Ordinal)
            .Replace("SpedUp", "sped-up", StringComparison.Ordinal)
            .Replace("RadioEdit", "radio edit", StringComparison.Ordinal)
            .Replace("ACappella", "a cappella", StringComparison.Ordinal)
            .Replace("AlternateTake", "alternate take", StringComparison.Ordinal)
            .ToLowerInvariant();

    private async Task EnsurePrimaryArtistCreditAsync(
        Track track,
        string? creditedArtist,
        string? artistMusicBrainzId,
        string? artistSortName,
        CancellationToken cancellationToken)
    {
        var creditedName = creditedArtist?.Trim();
        if (string.IsNullOrWhiteSpace(creditedName))
        {
            return;
        }

        var stableId = string.IsNullOrWhiteSpace(artistMusicBrainzId)
            ? null
            : artistMusicBrainzId.Trim().ToLowerInvariant();
        var primaryCredit = track.ArtistCredits
            .OrderBy(credit => credit.Position)
            .FirstOrDefault(credit => credit.Role == TrackArtistRole.Primary);

        if (primaryCredit != null)
        {
            if (stableId != null)
            {
                await AttachStableArtistIdentityAsync(primaryCredit, stableId, artistSortName, cancellationToken);
            }

            return;
        }

        Artist? artist = null;
        if (stableId != null)
        {
            artist = _dbContext.Artists.Local.FirstOrDefault(existing =>
                string.Equals(existing.MusicBrainzArtistId, stableId, StringComparison.OrdinalIgnoreCase));
            artist ??= await _dbContext.Artists.FirstOrDefaultAsync(
                existing => existing.MusicBrainzArtistId == stableId,
                cancellationToken);
        }

        if (artist == null)
        {
            var now = DateTimeOffset.UtcNow;
            artist = new Artist
            {
                Id = Guid.NewGuid(),
                Name = creditedName,
                SortName = string.IsNullOrWhiteSpace(artistSortName) ? null : artistSortName.Trim(),
                MusicBrainzArtistId = stableId,
                CreatedAt = now,
                UpdatedAt = now
            };
            _dbContext.Artists.Add(artist);
        }

        var credit = new TrackArtistCredit
        {
            Id = Guid.NewGuid(),
            TrackId = track.Id,
            ArtistId = artist.Id,
            Artist = artist,
            Role = TrackArtistRole.Primary,
            Position = track.ArtistCredits.Count == 0 ? 0 : track.ArtistCredits.Max(credit => credit.Position) + 1,
            CreditedName = creditedName
        };
        track.ArtistCredits.Add(credit);
        // Existing tracks discover an assigned-Guid dependent as Modified when
        // it is added only through the navigation. Mark the new credit as Added
        // explicitly so backfills insert instead of issuing a phantom update.
        _dbContext.TrackArtistCredits.Add(credit);
    }

    private async Task AttachStableArtistIdentityAsync(
        TrackArtistCredit credit,
        string stableId,
        string? artistSortName,
        CancellationToken cancellationToken)
    {
        var currentArtist = credit.Artist;
        if (currentArtist == null)
        {
            currentArtist = await _dbContext.Artists.FirstAsync(artist => artist.Id == credit.ArtistId, cancellationToken);
            credit.Artist = currentArtist;
        }

        if (!string.IsNullOrWhiteSpace(currentArtist.MusicBrainzArtistId))
        {
            // A conflicting stable identity is not safe to overwrite implicitly.
            return;
        }

        var stableArtist = _dbContext.Artists.Local.FirstOrDefault(artist =>
            artist.Id != currentArtist.Id
            && string.Equals(artist.MusicBrainzArtistId, stableId, StringComparison.OrdinalIgnoreCase));
        stableArtist ??= await _dbContext.Artists.FirstOrDefaultAsync(
            artist => artist.Id != currentArtist.Id && artist.MusicBrainzArtistId == stableId,
            cancellationToken);

        if (stableArtist != null)
        {
            credit.Artist = stableArtist;
            credit.ArtistId = stableArtist.Id;
            return;
        }

        currentArtist.MusicBrainzArtistId = stableId;
        if (string.IsNullOrWhiteSpace(currentArtist.SortName) && !string.IsNullOrWhiteSpace(artistSortName))
        {
            currentArtist.SortName = artistSortName.Trim();
        }
        currentArtist.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static CandidateArtistIdentity ReadCandidateArtistIdentity(string? rawMetadata)
    {
        if (string.IsNullOrWhiteSpace(rawMetadata))
        {
            return default;
        }

        try
        {
            var metadata = JsonSerializer.Deserialize<TrackMatchCandidateStoredMetadata>(rawMetadata);
            return new CandidateArtistIdentity(metadata?.ArtistMusicBrainzId, metadata?.ArtistSortName);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string? ReadCanonicalArtist(Track track)
    {
        if (string.IsNullOrWhiteSpace(track.CanonicalMetadata))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TrackCanonicalMetadata>(track.CanonicalMetadata)?.Artist;
        }
        catch (JsonException)
        {
            return null;
        }
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

    private static string BuildCandidateExplanation(TrackMatchScoredCandidate result)
    {
        var parsedObservation = result.ObservationMetadata;

        var interpretation = string.Empty;
        if (!string.Equals(parsedObservation.SearchTitle, result.ObservationTitle, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(parsedObservation.SearchArtist, result.ObservationArtist, StringComparison.OrdinalIgnoreCase))
        {
            interpretation = $" Interpreted as title '{parsedObservation.SearchTitle}'";
            if (!string.IsNullOrWhiteSpace(parsedObservation.SearchArtist))
            {
                interpretation += $" and artist '{parsedObservation.SearchArtist}'";
            }

            interpretation += ".";
        }

        var semanticExplanation = string.IsNullOrWhiteSpace(result.SemanticExplanation)
            ? string.Empty
            : $" {result.SemanticExplanation}";

        return $"{result.Candidate.Explanation}{interpretation}{semanticExplanation} Title similarity: {result.TitleSimilarity:P0}; artist similarity: {result.ArtistSimilarity:P0}; total confidence: {result.Score:P0}.";
    }

    private static string SerializeCandidateMetadata(TrackMatchScoredCandidate result, TrackMatchCluster? cluster)
    {
        var metadata = new TrackMatchCandidateStoredMetadata
        {
            ProviderRawMetadata = result.Candidate.RawMetadata,
            ArtistMusicBrainzId = result.Candidate.ArtistMusicBrainzId,
            ArtistSortName = result.Candidate.ArtistSortName,
            Matching = new TrackMatchCandidateDiagnostics
            {
                ObservationSearchTitle = result.ObservationMetadata.SearchTitle,
                ObservationSearchArtist = result.ObservationMetadata.SearchArtist,
                CandidateSearchTitle = result.CandidateMetadata.SearchTitle,
                CandidateSearchArtist = result.CandidateMetadata.SearchArtist,
                ObservationVersionMarkers = [.. result.ObservationMetadata.VersionMarkers],
                ObservationPlaybackModifiers = [.. result.ObservationMetadata.PlaybackModifiers],
                CandidateVersionMarkers = [.. result.CandidateMetadata.VersionMarkers],
                CandidatePlaybackModifiers = [.. result.CandidateMetadata.PlaybackModifiers],
                TitleSimilarity = result.TitleSimilarity,
                ArtistSimilarity = result.ArtistSimilarity,
                DurationScore = result.DurationScore,
                SemanticAdjustment = result.SemanticAdjustment,
                TotalScore = result.Score,
                SemanticExplanation = string.IsNullOrWhiteSpace(result.SemanticExplanation) ? null : result.SemanticExplanation,
                ClusterId = cluster?.ClusterId,
                ClusterSize = cluster?.Members.Count ?? 1,
                ClusterReason = cluster?.ClusterReason
            }
        };

        return JsonSerializer.Serialize(metadata);
    }

    private static TrackMatchObservationDiagnostics CreateObservationDiagnostics(
        ParsedTrackMetadata parsedObservation,
        string decisionReason,
        decimal? topScore,
        decimal? secondDistinctScore,
        int distinctClusterCount)
    {
        return new TrackMatchObservationDiagnostics
        {
            VersionMarkers = [.. parsedObservation.VersionMarkers],
            PlaybackModifiers = [.. parsedObservation.PlaybackModifiers],
            DecisionReason = decisionReason,
            TopScore = topScore,
            SecondDistinctScore = secondDistinctScore,
            DistinctClusterCount = distinctClusterCount
        };
    }

    private static void PersistObservationDiagnostics(TrackObservation observation, TrackMatchObservationDiagnostics diagnostics)
    {
        TrackObservationMetadata observationMetadata;
        if (string.IsNullOrWhiteSpace(observation.RawMetadata))
        {
            observationMetadata = new TrackObservationMetadata();
        }
        else
        {
            try
            {
                observationMetadata = JsonSerializer.Deserialize<TrackObservationMetadata>(observation.RawMetadata) ?? new TrackObservationMetadata();
            }
            catch (JsonException)
            {
                observationMetadata = new TrackObservationMetadata();
            }
        }

        observationMetadata.SourceType = observation.SourceType;
        observationMetadata.ExternalId = observation.ExternalId;
        observationMetadata.Title = observation.Title;
        observationMetadata.Artist = observation.Artist;
        var parsedObservation = TrackObservationParser.Parse(observation, observationMetadata);
        observationMetadata.SearchTitle = parsedObservation.SearchTitle;
        observationMetadata.SearchArtist = parsedObservation.SearchArtist;
        observationMetadata.ThumbnailUrl = observation.ThumbnailUrl;
        observationMetadata.DurationSeconds = observation.DurationSeconds;
        observationMetadata.Matching = diagnostics;

        observation.RawMetadata = JsonSerializer.Serialize(observationMetadata);
    }

    private readonly record struct CandidateArtistIdentity(string? MusicBrainzId, string? SortName);
}
