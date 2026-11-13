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
            .Include(o => o.ProviderCatalogItem)
            .Include(o => o.Candidates).ThenInclude(c => c.ProviderCatalogItem)
            .FirstOrDefaultAsync(o => o.Id == observationId, cancellationToken)
            ?? throw new InvalidOperationException($"Track observation {observationId} was not found.");

        EnsureProcessable(observation);

        return await ProcessObservationAsync(observation, cancellationToken);
    }

    public async Task<TrackObservation?> ReevaluateStoredCandidatesAsync(
        Guid observationId,
        CancellationToken cancellationToken)
    {
        var observation = await _dbContext.TrackObservations
            .Include(o => o.ProviderCatalogItem)
            .Include(item => item.Candidates).ThenInclude(c => c.ProviderCatalogItem)
            .FirstOrDefaultAsync(item => item.Id == observationId, cancellationToken);
        if (observation is null || observation.TrackId != null)
        {
            return null;
        }
        if (!IsProcessable(observation)) return null;

        return await TryResolveFromStoredCandidatesAsync(
            observation,
            TrackObservationParser.Parse(Input(observation)),
            cancellationToken);
    }

    public async Task<TrackObservation> AcceptCandidateAsync(Guid observationId, Guid candidateId, CancellationToken cancellationToken)
    {
        var observation = await _dbContext.TrackObservations
            .Include(o => o.ProviderCatalogItem)
            .Include(o => o.Candidates).ThenInclude(c => c.ProviderCatalogItem)
            .FirstOrDefaultAsync(o => o.Id == observationId, cancellationToken)
            ?? throw new InvalidOperationException($"Track observation {observationId} was not found.");
        EnsureProcessable(observation);

        var candidate = observation.Candidates.FirstOrDefault(c => c.Id == candidateId)
            ?? throw new InvalidOperationException($"Candidate {candidateId} does not belong to observation {observationId}.");

        await ResolveObservationToTrackAsync(observation, candidate, "Resolved by manual candidate selection.", cancellationToken, manualReview: true);
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
            .Include(o => o.ProviderCatalogItem)
            .Include(item => item.Candidates).ThenInclude(c => c.ProviderCatalogItem)
            .FirstOrDefaultAsync(item => item.Id == observationId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Track observation {observationId} was not found.");
        EnsureProcessable(observation);
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
            .Include(o => o.ProviderCatalogItem)
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
            .Include(o => o.ProviderCatalogItem)
                .Include(item => item.Candidates).ThenInclude(c => c.ProviderCatalogItem)
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

        var candidateInput = await TrackMatchCandidateInput.ReloadAsync(_dbContext, candidate, cancellationToken);
        var anchorTrack = await ResolveCandidateAnchorTrackAsync(
            candidate,
            observation.OwnerUserId,
            cancellationToken);
        var targetSongId = await GetSingleSongIdAsync(
            anchorTrack.Id,
            cancellationToken);
        var parsedObservation = TrackObservationParser.Parse(Input(observation));
        var versionEvidence = CreateVersionEvidence(
            observation,
            candidate,
            versionFlags,
            "manual-candidate-version");
        var versionTrack = await CreateTrackAsync(
            mbidRecording: null,
            isrc: null,
            title: candidateInput.Title,
            artist: candidateInput.Artist ?? parsedObservation.DisplayArtist,
            durationSeconds: Input(observation).DurationSeconds,
            description: null,
            thumbnailUrl: null,
            artistMusicBrainzId: null,
            artistSortName: null,
            targetSongId,
            versionFlags,
            versionEvidence,
            cancellationToken, artistIdentities: null);

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
            .Include(o => o.ProviderCatalogItem)
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
            .Include(o => o.ProviderCatalogItem)
            .FirstOrDefaultAsync(o => o.Id == observationId, cancellationToken)
            ?? throw new InvalidOperationException($"Track observation {observationId} was not found.");

        return await CreateTrackFromObservationAsync(observation, matchedCandidate: null, cancellationToken);
    }

    public async Task<TrackObservation> CreateTrackFromMatchAsync(
        Guid observationId,
        TrackMatchSearchCandidate candidate,
        CancellationToken cancellationToken,
        bool manualReview = false)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var observation = await _dbContext.TrackObservations
            .Include(o => o.ProviderCatalogItem)
            .FirstOrDefaultAsync(o => o.Id == observationId, cancellationToken)
            ?? throw new InvalidOperationException($"Track observation {observationId} was not found.");

        return await CreateTrackFromObservationAsync(observation, candidate, cancellationToken, manualReview);
    }

    private async Task<TrackObservation> CreateTrackFromObservationAsync(
        TrackObservation observation,
        TrackMatchSearchCandidate? matchedCandidate,
        CancellationToken cancellationToken,
        bool manualReview = false)
    {
        EnsureProcessable(observation);
        var candidateIdentity = matchedCandidate == null ? null : await _identityResolver.ResolveExistingAsync(
            new TrackIdentityQuery(
                matchedCandidate.CandidateSource,
                matchedCandidate.ExternalId,
                matchedCandidate.Title,
                matchedCandidate.Artist,
                matchedCandidate.DurationSeconds ?? Input(observation).DurationSeconds,
                matchedCandidate.Isrc,
                matchedCandidate.MbidRecording,
                matchedCandidate.ArtistCredits,
                matchedCandidate.RawMetadata,
                OwnerUserId: observation.OwnerUserId),
            cancellationToken);
        if (observation.TrackId != null)
        {
            if (matchedCandidate != null)
            {
                var trackId = observation.TrackId.Value;
                var observedPublicMapping = await new ProviderRecordingAssociations(_dbContext, _options)
                    .FindUsableAsync(observation.SourceType, observation.ExternalId, cancellationToken);
                if (observedPublicMapping is not null && observedPublicMapping.TrackId != trackId)
                    throw new InvalidOperationException("The verified provider recording belongs to a different Track.");
                if (candidateIdentity is not null && candidateIdentity.Track.Id != trackId)
                {
                    throw new InvalidOperationException(
                        "The accepted catalog recording belongs to a different Track.");
                }

                var existing = await _dbContext.Tracks.SingleAsync(t => t.Id == trackId, cancellationToken);
                observation.MatchStatus = TrackMatchingStatuses.Matched;
                observation.UpdatedAt = DateTimeOffset.UtcNow;
                await _playlistReconciler.ReconcileObservationAsync(observation.Id, trackId, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            return observation;
        }

        var parsed = matchedCandidate == null
            ? TrackObservationParser.Parse(Input(observation))
            : TrackMetadataParser.Parse(matchedCandidate.Title, matchedCandidate.Artist);
        var inferredFlags = TrackVersionClassifier.Infer(parsed);
        var existingTrack = candidateIdentity?.Track;
        var track = existingTrack ?? await CreateTrackAsync(
            mbidRecording: null,
            isrc: null,
            title: matchedCandidate?.Title ?? parsed.DisplayTitle,
            artist: matchedCandidate?.Artist ?? parsed.DisplayArtist,
            durationSeconds: matchedCandidate?.DurationSeconds ?? Input(observation).DurationSeconds,
            description: null,
            thumbnailUrl: null,
            artistMusicBrainzId: null,
            artistSortName: null,
            targetSongId: null,
            inferredFlags,
            CreateVersionEvidence(
                observation,
                null,
                inferredFlags,
                matchedCandidate == null ? "manual-observation-track"
                    : manualReview ? "playlist-manual-review" : "automatic-catalog-match"),
            cancellationToken, artistIdentities: null);

        if (existingTrack is null && matchedCandidate is not null && !manualReview
            && matchedCandidate.ProviderCatalogItemId is not null)
            await new ProviderRecordingAssociations(_dbContext, _options)
                .TryAssociateCandidateAsync(track, matchedCandidate, independentFacts: null,
                    providerOrigin: true, cancellationToken);

        if (existingTrack != null && matchedCandidate != null)
        {
            // A destination choice that resolves to an existing Track stays scoped.
            track.UpdatedAt = DateTimeOffset.UtcNow;
        }

        observation.TrackId = track.Id;
        observation.MatchStatus = TrackMatchingStatuses.Matched;
        observation.ResolutionNotes = matchedCandidate == null
            ? "Created canonical track from observation during manual review."
            : manualReview ? "Confirmed recording during playlist review."
                : "Automatically created canonical track from matched catalog result.";
        observation.AcceptedCandidateId = null;
        observation.UpdatedAt = DateTimeOffset.UtcNow;

        await _playlistReconciler.ReconcileObservationAsync(observation.Id, track.Id, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return observation;
    }

    private async Task<TrackObservation> ProcessObservationAsync(TrackObservation observation, CancellationToken cancellationToken)
    {
        EnsureProcessable(observation);
        var now = DateTimeOffset.UtcNow;

        observation.MatchAttemptCount += 1;
        observation.LastMatchAttemptedAt = now;
        observation.LastMatchError = null;
        observation.UpdatedAt = now;

        var parsedObservation = TrackObservationParser.Parse(Input(observation));
        var localIdentityMatch = await _identityResolver.ResolveExistingAsync(
            new TrackIdentityQuery(
                observation.SourceType,
                observation.ExternalId,
                Input(observation).Title,
                Input(observation).Artist,
                Input(observation).DurationSeconds,
                ArtistCredits: parsedObservation.ArtistCredits,
                RawMetadata: JsonSerializer.Serialize(Input(observation).Metadata),
                OwnerUserId: observation.OwnerUserId),
            cancellationToken);

        if (localIdentityMatch != null)
        {
            if (localIdentityMatch.Kind != TrackIdentityMatchKind.ExactSource
                && observation.Candidates.Count > 0)
            {
                observation.AcceptedCandidateId = null;
                await _dbContext.SaveChangesAsync(cancellationToken);
                _dbContext.TrackResolutionCandidates.RemoveRange(observation.Candidates);
                observation.Candidates.Clear();
            }

            // Linking a private observation does not change shared artist or artwork facts.
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
            var providerErrors = new List<Exception>();
            foreach (var provider in _metadataProviders)
            {
                try
                {
                    var providerCandidates = await provider.SearchAsync(Input(observation), cancellationToken);
                    scoredCandidates.AddRange(providerCandidates.Select(candidate => TrackMatchScorer.Score(Input(observation), candidate, _options)));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    providerErrors.Add(exception);
                    _logger.LogWarning(
                        exception,
                        "Track metadata provider {ProviderType} failed for observation {ObservationId}; continuing with other evidence providers",
                        provider.GetType().Name,
                        observation.Id);
                }
            }

            if (scoredCandidates.Count == 0 && providerErrors.Count > 0)
            {
                throw providerErrors[0];
            }

            if (observation.Candidates.Count > 0)
            {
                observation.AcceptedCandidateId = null;
                await _dbContext.SaveChangesAsync(cancellationToken);
                _dbContext.TrackResolutionCandidates.RemoveRange(observation.Candidates);
                observation.Candidates.Clear();
            }

            var assessment = TrackMatchDecisionEngine.EvaluateScored(scoredCandidates, _options);
            var rankedCandidates = assessment.Ranked;
            var clusters = assessment.Clusters;
            var decision = assessment.Decision;
            var displayedCandidates = SelectDisplayedCandidates(
                clusters,
                decision.AcceptedCandidate,
                maximumCount: 5);

            var persistedCandidates = new List<TrackResolutionCandidate>(displayedCandidates.Count);
            foreach (var result in displayedCandidates)
            {
                var cluster = clusters.FirstOrDefault(existingCluster => existingCluster.Members.Any(member =>
                    member.Candidate.ExternalId == result.Candidate.ExternalId
                    && member.Candidate.CandidateSource == result.Candidate.CandidateSource));
                var persistedCandidate = new TrackResolutionCandidate
                {
                    Id = Guid.NewGuid(),
                    TrackObservationId = observation.Id,
                    CandidateSource = result.Candidate.CandidateSource,
                    ExternalId = result.Candidate.ExternalId,
                    ProviderCatalogItemId = result.Candidate.ProviderCatalogItemId,
                    ProviderCatalogRevision = result.Candidate.ProviderCatalogRevision,
                    Title = result.Candidate.ProviderCatalogItemId == null ? result.Candidate.Title : null,
                    Artist = result.Candidate.ProviderCatalogItemId == null ? result.Candidate.Artist : null,
                    MbidRecording = result.Candidate.ProviderCatalogItemId == null ? result.Candidate.MbidRecording : null,
                    Isrc = result.Candidate.ProviderCatalogItemId == null ? result.Candidate.Isrc : null,
                    DurationSeconds = result.Candidate.ProviderCatalogItemId == null ? result.Candidate.DurationSeconds : null,
                    Score = result.Score,
                    Explanation = BuildCandidateExplanation(result),
                    EvaluationJson = SerializeCandidateMetadata(result, cluster),
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
                    rankedCandidates.FirstOrDefault()?.ObservationMetadata ?? TrackObservationParser.Parse(Input(observation)),
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
                    .First(candidate =>
                        candidate.ExternalId == decision.AcceptedCandidate.Candidate.ExternalId
                        && candidate.CandidateSource == decision.AcceptedCandidate.Candidate.CandidateSource);

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
        catch (DbUpdateException)
        {
            // A failed write may leave catalog/association work staged in this
            // context. Let the worker retry with a fresh scope instead of saving
            // the failed change tracker again while recording a matching error.
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
                    TrackObservationParser.Parse(Input(observation)),
                    $"Matching failed: {ex.Message}",
                    topScore: null,
                    secondDistinctScore: null,
                    distinctClusterCount: 0));
            await _playlistReconciler.ReconcileObservationAsync(observation.Id, null, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return observation;
        }
    }

    private async Task<TrackObservation?> TryResolveFromStoredCandidatesAsync(
        TrackObservation observation,
        ParsedTrackMetadata parsedObservation,
        CancellationToken cancellationToken)
    {
        if (observation.Candidates.Count == 0)
        {
            return null;
        }

        var candidates = observation.Candidates.Select(c => TrackMatchCandidateInput.Read(c, DateTimeOffset.UtcNow))
            .Where(c => c != null).Cast<TrackMatchSearchCandidate>();
        var assessment = TrackMatchDecisionEngine.Evaluate(Input(observation), candidates, _options);
        var rankedCandidates = assessment.Ranked;
        var clusters = assessment.Clusters;
        var decision = assessment.Decision;
        if (decision.AcceptedCandidate == null)
        {
            return null;
        }

        var acceptedCandidate = observation.Candidates.First(candidate =>
            candidate.CandidateSource == decision.AcceptedCandidate.Candidate.CandidateSource
            && candidate.ExternalId == decision.AcceptedCandidate.Candidate.ExternalId);
        PersistObservationDiagnostics(
            observation,
            CreateObservationDiagnostics(
                parsedObservation,
                $"Re-evaluated stored evidence. {decision.DecisionReason}",
                decision.TopScore,
                decision.SecondDistinctScore,
                clusters.Count));
        await ResolveObservationToTrackAsync(
            observation,
            acceptedCandidate,
            $"Automatically resolved from stored provider evidence. {decision.ResolutionNotes}",
            cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return observation;
    }

    private static IReadOnlyList<TrackMatchScoredCandidate> SelectDisplayedCandidates(
        IReadOnlyList<TrackMatchCluster> clusters,
        TrackMatchScoredCandidate? acceptedCandidate,
        int maximumCount)
    {
        var selected = new List<TrackMatchScoredCandidate>(maximumCount);
        foreach (var cluster in clusters.Take(maximumCount))
        {
            var representative = acceptedCandidate != null && cluster.Members.Any(member =>
                member.Candidate.ExternalId == acceptedCandidate.Candidate.ExternalId
                && member.Candidate.CandidateSource == acceptedCandidate.Candidate.CandidateSource)
                    ? acceptedCandidate
                    : cluster.Representative;
            selected.Add(representative);
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
        CancellationToken cancellationToken,
        bool manualReview = false)
    {
        var input = await TrackMatchCandidateInput.ReloadAsync(_dbContext, candidate, cancellationToken);
        var identityMatch = await _identityResolver.ResolveExistingAsync(
            new TrackIdentityQuery(
                input.CandidateSource,
                input.ExternalId,
                input.Title,
                input.Artist,
                input.DurationSeconds ?? Input(observation).DurationSeconds,
                input.Isrc,
                input.MbidRecording,
                TrackMetadataParser.Parse(input.Title, input.Artist).ArtistCredits,
                OwnerUserId: observation.OwnerUserId),
            cancellationToken);
        var existingTrack = identityMatch?.Track;
        var track = existingTrack ?? await CreateTrackAsync(
            mbidRecording: null,
            isrc: null,
            input.Title,
            input.Artist,
            input.DurationSeconds ?? Input(observation).DurationSeconds,
            description: null,
            thumbnailUrl: null,
            artistMusicBrainzId: null,
            artistSortName: null,
            cancellationToken, artistIdentities: null);

        var catalogReference = candidate.ProviderCatalogItemId;
        var searchCandidate = new TrackMatchSearchCandidate
        {
            CandidateSource = candidate.CandidateSource,
            ExternalId = candidate.ExternalId,
            Title = input.Title,
            Artist = input.Artist,
            DurationSeconds = input.DurationSeconds,
            Isrc = input.Isrc,
            MbidRecording = input.MbidRecording,
            ProviderCatalogItemId = candidate.ProviderCatalogItemId,
            ProviderCatalogRevision = candidate.ProviderCatalogRevision
        };
        if (!manualReview && existingTrack is null && catalogReference is not null)
        {
            await new ProviderRecordingAssociations(_dbContext, _options).TryAssociateCandidateAsync(
                track, searchCandidate, independentFacts: null, providerOrigin: true, cancellationToken);
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
        int? ownerUserId,
        CancellationToken cancellationToken)
    {
        var input = await TrackMatchCandidateInput.ReloadAsync(_dbContext, candidate, cancellationToken);
        var parsedCandidate = TrackMetadataParser.Parse(
            input.Title,
            input.Artist);
        var identityMatch = await _identityResolver.ResolveExistingAsync(
            new TrackIdentityQuery(
                candidate.CandidateSource,
                candidate.ExternalId,
                input.Title,
                input.Artist,
                input.DurationSeconds,
                input.Isrc,
                input.MbidRecording,
                parsedCandidate.ArtistCredits,
                RawMetadata: input.RawMetadata,
                OwnerUserId: ownerUserId),
            cancellationToken);
        var anchorTrack = identityMatch?.Track ?? await CreateTrackAsync(
            mbidRecording: null,
            isrc: null,
            input.Title,
            input.Artist,
            input.DurationSeconds,
            description: null,
            thumbnailUrl: null,
            artistMusicBrainzId: null,
            artistSortName: null,
            cancellationToken, artistIdentities: null);

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
        CancellationToken cancellationToken,
        IReadOnlyList<ArtistCreditMetadata>? artistIdentities = null)
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
            cancellationToken, artistIdentities);

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
        CancellationToken cancellationToken,
        IReadOnlyList<ArtistCreditMetadata>? artistIdentities = null)
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
        return track;
    }

    private static string CreateVersionEvidence(
        TrackObservation observation,
        TrackResolutionCandidate? candidate,
        TrackVersionFlags selectedFlags,
        string origin) =>
        JsonSerializer.Serialize(new
        {
            origin,
            selectedFlags = selectedFlags.ToString(),
            observationId = observation.Id,
            candidateId = candidate?.Id
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

    private static bool IsProcessable(TrackObservation observation) =>
        ObservationInput.Read(observation, observation.OwnerUserId, DateTimeOffset.UtcNow) != null;

    private static void EnsureProcessable(TrackObservation observation)
    {
        if (!IsProcessable(observation))
            throw new InvalidOperationException("This observation needs current public facts or usable owner-private input before matching.");
    }

    private static string BuildCandidateExplanation(TrackMatchScoredCandidate result)
    {
        var parsedObservation = result.ObservationMetadata;

        var interpretation = string.Empty;
        var semanticExplanation = string.IsNullOrWhiteSpace(result.SemanticExplanation)
            ? string.Empty
            : $" {result.SemanticExplanation}";

        return $"{result.Candidate.Explanation}{interpretation}{semanticExplanation} Title similarity: {result.TitleSimilarity:P0}; artist similarity: {result.ArtistSimilarity:P0}; total confidence: {result.Score:P0}.";
    }

    private static string SerializeCandidateMetadata(TrackMatchScoredCandidate result, TrackMatchCluster? cluster)
    {
        var metadata = new TrackMatchCandidateStoredMetadata
        {
            Matching = new TrackMatchCandidateDiagnostics
            {
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

        return JsonSerializer.Serialize(new { metadata.Matching });
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

    private static ObservationInput Input(TrackObservation observation) =>
        ObservationInput.Read(observation, observation.OwnerUserId, DateTimeOffset.UtcNow)
        ?? throw new InvalidOperationException("Observation input is unavailable.");

    private static void PersistObservationDiagnostics(TrackObservation observation, TrackMatchObservationDiagnostics diagnostics)
    {
        // Job decision text is retained without copying provider facts or search hypotheses.
        observation.ResolutionNotes = diagnostics.DecisionReason;
    }
}
