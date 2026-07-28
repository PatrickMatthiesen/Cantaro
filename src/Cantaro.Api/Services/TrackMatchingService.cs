using System.Text.Json;
using Cantaro.Api.Configuration;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services.Spotify;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cantaro.Api.Services;

public class TrackMatchingService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEnumerable<ITrackMetadataSearchProvider> _metadataProviders;
    private readonly ILogger<TrackMatchingService> _logger;
    private readonly TrackMatchingOptions _options;

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
    {
        _dbContext = dbContext;
        _metadataProviders = metadataProviders;
        _logger = logger;
        _options = options.Value;
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
            artistMusicBrainzId: null,
            artistSortName: null,
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
            var mappedTrack = await LoadTrackWithArtistCreditsAsync(exactSourceMatch.TrackId, cancellationToken);
            if (mappedTrack != null)
            {
                await EnsurePrimaryArtistCreditAsync(
                    mappedTrack,
                    ReadCanonicalArtist(mappedTrack) ?? observation.Artist,
                    artistMusicBrainzId: null,
                    artistSortName: null,
                    cancellationToken);
            }

            observation.TrackId = exactSourceMatch.TrackId;
            observation.MatchStatus = TrackMatchingStatuses.Matched;
            observation.ResolutionNotes = "Matched existing source mapping.";
            observation.AcceptedCandidateId = null;
            PersistObservationDiagnostics(
                observation,
                CreateObservationDiagnostics(
                    TrackMetadataParser.Parse(observation.Title, observation.Artist),
                    decisionReason: "Matched existing source mapping.",
                    topScore: null,
                    secondDistinctScore: null,
                    distinctClusterCount: 0));
            await UpdatePlaylistEntriesForObservationAsync(observation.Id, exactSourceMatch.TrackId, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return observation;
        }

        var trustedSpotifyTrackId = await FindUniqueTrustedSpotifyTrackAsync(observation, cancellationToken);
        if (trustedSpotifyTrackId != null)
        {
            if (observation.Candidates.Count > 0)
            {
                _dbContext.TrackResolutionCandidates.RemoveRange(observation.Candidates);
                observation.Candidates.Clear();
            }

            await EnsureSourceMappingAsync(
                trustedSpotifyTrackId.Value,
                observation.SourceType,
                observation.ExternalId,
                cancellationToken);

            observation.TrackId = trustedSpotifyTrackId;
            observation.MatchStatus = TrackMatchingStatuses.Matched;
            observation.ResolutionNotes = "Matched the unique trusted Spotify track by exact title and artist credits with compatible duration.";
            observation.AcceptedCandidateId = null;
            PersistObservationDiagnostics(
                observation,
                CreateObservationDiagnostics(
                    TrackMetadataParser.Parse(observation.Title, observation.Artist),
                    decisionReason: observation.ResolutionNotes,
                    topScore: 1m,
                    secondDistinctScore: null,
                    distinctClusterCount: 1));
            await UpdatePlaylistEntriesForObservationAsync(observation.Id, trustedSpotifyTrackId, cancellationToken);
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

            var scoredCandidates = new List<TrackMatchScoredCandidate>();
            foreach (var provider in _metadataProviders)
            {
                var providerCandidates = await provider.SearchAsync(observation, cancellationToken);
                scoredCandidates.AddRange(providerCandidates.Select(candidate => TrackMatchScorer.Score(observation, candidate, _options)));
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
                    rankedCandidates.FirstOrDefault()?.ObservationMetadata ?? TrackMetadataParser.Parse(observation.Title, observation.Artist),
                    decision.DecisionReason,
                    decision.TopScore,
                    decision.SecondDistinctScore,
                    clusters.Count));
                await UpdatePlaylistEntriesForObservationAsync(observation.Id, null, cancellationToken);
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
            PersistObservationDiagnostics(
                observation,
                CreateObservationDiagnostics(
                    TrackMetadataParser.Parse(observation.Title, observation.Artist),
                    $"Matching failed: {ex.Message}",
                    topScore: null,
                    secondDistinctScore: null,
                    distinctClusterCount: 0));
            await UpdatePlaylistEntriesForObservationAsync(observation.Id, null, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return observation;
        }
    }

    private async Task<Guid?> FindUniqueTrustedSpotifyTrackAsync(
        TrackObservation observation,
        CancellationToken cancellationToken)
    {
        if (observation.SourceType == SpotifyService.ServiceName)
        {
            return null;
        }

        var parsedObservation = TrackMetadataParser.Parse(observation.Title, observation.Artist);
        var normalizedTitle = TrackTextNormalizer.Normalize(parsedObservation.SearchTitle);
        if (string.IsNullOrWhiteSpace(normalizedTitle) || observation.DurationSeconds == null)
        {
            return null;
        }

        var minimumDuration = Math.Max(
            0,
            observation.DurationSeconds.Value - _options.AutoMatchDurationToleranceSeconds);
        var maximumDuration =
            observation.DurationSeconds.Value + _options.AutoMatchDurationToleranceSeconds;
        var candidates = await _dbContext.TrackObservations
            .AsNoTracking()
            .Where(candidate =>
                candidate.SourceType == SpotifyService.ServiceName
                && candidate.TrackId != null
                && candidate.NormalizedTitle == normalizedTitle
                && candidate.DurationSeconds >= minimumDuration
                && candidate.DurationSeconds <= maximumDuration
                && _dbContext.TrackSourceIds.Any(sourceId =>
                    sourceId.SourceType == SpotifyService.ServiceName
                    && sourceId.ExternalId == candidate.ExternalId
                    && sourceId.TrackId == candidate.TrackId
                    && sourceId.IsOfficial == true))
            .Take(25)
            .ToListAsync(cancellationToken);

        var matchingTrackIds = candidates
            .Where(candidate =>
            {
                var parsedCandidate = TrackMetadataParser.Parse(candidate.Title, candidate.Artist);
                return TrackTextNormalizer.AreEquivalentTitles(
                        parsedObservation.SearchTitle,
                        parsedCandidate.SearchTitle)
                    && TrackMetadataParser.HaveEquivalentArtistCredits(
                        parsedObservation,
                        parsedCandidate,
                        parsedCandidate.ArtistCredits);
            })
            .Select(candidate => candidate.TrackId!.Value)
            .Distinct()
            .Take(2)
            .ToArray();

        return matchingTrackIds.Length == 1 ? matchingTrackIds[0] : null;
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
        var existingTrack = await FindExistingTrackAsync(candidate.MbidRecording, candidate.Isrc, cancellationToken);
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
                return await LoadTrackWithArtistCreditsAsync(exactMusicBrainzMapping.TrackId, cancellationToken);
            }

            var mbidTrack = await _dbContext.Tracks
                .Include(track => track.ArtistCredits)
                    .ThenInclude(credit => credit.Artist)
                .FirstOrDefaultAsync(track => track.MbidRecording == mbidRecording, cancellationToken);
            if (mbidTrack != null)
            {
                return mbidTrack;
            }
        }

        if (!string.IsNullOrWhiteSpace(isrc))
        {
            return await _dbContext.Tracks
                .Include(track => track.ArtistCredits)
                    .ThenInclude(credit => credit.Artist)
                .FirstOrDefaultAsync(track => track.Isrc == isrc, cancellationToken);
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
        string? artistMusicBrainzId,
        string? artistSortName,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var song = new Song
        {
            Id = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now
        };
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
            CreatedAt = now,
            UpdatedAt = now
        };
        var membership = new SongTrack
        {
            SongId = song.Id,
            Song = song,
            TrackId = track.Id,
            Track = track
        };

        _dbContext.AddRange(song, track, membership);
        await EnsurePrimaryArtistCreditAsync(
            track,
            artist,
            artistMusicBrainzId,
            artistSortName,
            cancellationToken);
        return track;
    }

    private Task<Track?> LoadTrackWithArtistCreditsAsync(Guid trackId, CancellationToken cancellationToken) =>
        _dbContext.Tracks
            .Include(track => track.ArtistCredits)
                .ThenInclude(credit => credit.Artist)
            .FirstOrDefaultAsync(track => track.Id == trackId, cancellationToken);

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
        var parsedObservation = TrackMetadataParser.Parse(observation.Title, observation.Artist);
        observationMetadata.SearchTitle = parsedObservation.SearchTitle;
        observationMetadata.SearchArtist = parsedObservation.SearchArtist;
        observationMetadata.ThumbnailUrl = observation.ThumbnailUrl;
        observationMetadata.DurationSeconds = observation.DurationSeconds;
        observationMetadata.Matching = diagnostics;

        observation.RawMetadata = JsonSerializer.Serialize(observationMetadata);
    }

    private readonly record struct CandidateArtistIdentity(string? MusicBrainzId, string? SortName);
}
