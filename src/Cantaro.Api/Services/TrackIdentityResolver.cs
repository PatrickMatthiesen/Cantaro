using Cantaro.Api.Configuration;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cantaro.Api.Services;

public enum TrackIdentityMatchKind
{
    ExactSource,
    MusicBrainzRecording,
    Isrc,
    Metadata
}

public sealed record TrackIdentityQuery(
    string SourceType,
    string ExternalId,
    string Title,
    string? Artist,
    int? DurationSeconds,
    string? Isrc = null,
    string? MusicBrainzRecordingId = null,
    IReadOnlyList<string>? ArtistCredits = null,
    string? RawMetadata = null);

public sealed record TrackIdentityMatch(
    Track Track,
    TrackIdentityMatchKind Kind,
    string Reason,
    TrackSourceId? ExactSourceMapping = null);

/// <summary>
/// Resolves provider items against existing Cantaro Tracks. Callers decide
/// whether an unresolved item is authoritative enough to create a Track or
/// must fall back to external matching/review.
/// </summary>
public sealed class TrackIdentityResolver
{
    private const int MaximumMetadataCandidates = 100;

    private readonly ApplicationDbContext _dbContext;
    private readonly TrackMatchingOptions _options;

    public TrackIdentityResolver(
        ApplicationDbContext dbContext,
        IOptions<TrackMatchingOptions> options)
    {
        _dbContext = dbContext;
        _options = options.Value;
    }

    public TrackIdentityResolver(ApplicationDbContext dbContext)
        : this(dbContext, Options.Create(new TrackMatchingOptions()))
    {
    }

    public async Task<TrackIdentityMatch?> ResolveExistingAsync(
        TrackIdentityQuery query,
        CancellationToken cancellationToken)
    {
        var exactSource = _dbContext.TrackSourceIds.Local.FirstOrDefault(sourceId =>
            sourceId.SourceType == query.SourceType
            && sourceId.ExternalId == query.ExternalId);
        exactSource ??= await _dbContext.TrackSourceIds.FirstOrDefaultAsync(
            sourceId => sourceId.SourceType == query.SourceType
                && sourceId.ExternalId == query.ExternalId,
            cancellationToken);

        if (exactSource != null)
        {
            var exactTrack = await LoadRequiredTrackAsync(
                exactSource.TrackId,
                cancellationToken);
            var incomingIsrc = NormalizeIsrc(query.Isrc);
            var mappedIsrc = NormalizeIsrc(exactTrack.Isrc);
            if (incomingIsrc != null
                && !string.Equals(incomingIsrc, mappedIsrc, StringComparison.Ordinal))
            {
                var corroboratedTrackId = await FindUniqueIsrcTrackIdAsync(
                    incomingIsrc,
                    cancellationToken);
                if (corroboratedTrackId != null
                    && corroboratedTrackId != exactTrack.Id
                    && await MetadataCorroboratesTrackAsync(
                        query,
                        corroboratedTrackId.Value,
                        cancellationToken))
                {
                    return new TrackIdentityMatch(
                        await LoadRequiredTrackAsync(
                            corroboratedTrackId.Value,
                            cancellationToken),
                        TrackIdentityMatchKind.Isrc,
                        "Corrected a stale source mapping using a unique ISRC and corroborating metadata.",
                        exactSource);
                }
            }

            return new TrackIdentityMatch(
                exactTrack,
                TrackIdentityMatchKind.ExactSource,
                "Matched existing source mapping.",
                exactSource);
        }

        var musicBrainzId = NormalizeStableId(query.MusicBrainzRecordingId);
        if (musicBrainzId != null)
        {
            var musicBrainzTrackId = await FindUniqueMusicBrainzTrackIdAsync(
                musicBrainzId,
                cancellationToken);
            if (musicBrainzTrackId != null)
            {
                return new TrackIdentityMatch(
                    await LoadRequiredTrackAsync(musicBrainzTrackId.Value, cancellationToken),
                    TrackIdentityMatchKind.MusicBrainzRecording,
                    "Matched the unique existing Cantaro Track by MusicBrainz recording ID.");
            }
        }

        var isrc = NormalizeIsrc(query.Isrc);
        if (isrc != null)
        {
            var isrcTrackId = await FindUniqueIsrcTrackIdAsync(isrc, cancellationToken);
            if (isrcTrackId != null)
            {
                return new TrackIdentityMatch(
                    await LoadRequiredTrackAsync(isrcTrackId.Value, cancellationToken),
                    TrackIdentityMatchKind.Isrc,
                    "Matched the unique existing Cantaro Track by ISRC.");
            }
        }

        var metadataTrackId = await FindUniqueMetadataTrackIdAsync(query, cancellationToken);
        if (metadataTrackId == null)
        {
            return null;
        }

        return new TrackIdentityMatch(
            await LoadRequiredTrackAsync(metadataTrackId.Value, cancellationToken),
            TrackIdentityMatchKind.Metadata,
            "Matched the unique existing Cantaro Track by title, artist credits, duration, and version markers.");
    }

    private async Task<Guid?> FindUniqueMusicBrainzTrackIdAsync(
        string musicBrainzId,
        CancellationToken cancellationToken)
    {
        var trackIds = _dbContext.TrackSourceIds.Local
            .Where(sourceId => sourceId.SourceType == "musicbrainz"
                && NormalizeStableId(sourceId.ExternalId) == musicBrainzId)
            .Select(sourceId => sourceId.TrackId)
            .Concat(_dbContext.Tracks.Local
                .Where(track => NormalizeStableId(track.MbidRecording) == musicBrainzId)
                .Select(track => track.Id))
            .Distinct()
            .Take(2)
            .ToHashSet();

        if (trackIds.Count < 2)
        {
            var stableIdVariants = StableIdVariants(musicBrainzId);
            var storedIds = await _dbContext.Tracks
                .Where(track => track.MbidRecording != null
                    && stableIdVariants.Contains(track.MbidRecording))
                .Select(track => track.Id)
                .Concat(_dbContext.TrackSourceIds
                    .Where(sourceId => sourceId.SourceType == "musicbrainz"
                        && stableIdVariants.Contains(sourceId.ExternalId))
                    .Select(sourceId => sourceId.TrackId))
                .Distinct()
                .Take(2)
                .ToListAsync(cancellationToken);
            trackIds.UnionWith(storedIds);
        }

        return trackIds.Count == 1 ? trackIds.Single() : null;
    }

    private async Task<Guid?> FindUniqueIsrcTrackIdAsync(
        string isrc,
        CancellationToken cancellationToken)
    {
        var trackIds = _dbContext.Tracks.Local
            .Where(track => NormalizeIsrc(track.Isrc) == isrc)
            .Select(track => track.Id)
            .Distinct()
            .Take(2)
            .ToHashSet();

        if (trackIds.Count < 2)
        {
            var isrcVariants = IsrcVariants(isrc);
            var storedIds = await _dbContext.Tracks
                .Where(track => track.Isrc != null
                    && isrcVariants.Contains(track.Isrc))
                .Select(track => track.Id)
                .Distinct()
                .Take(2)
                .ToListAsync(cancellationToken);
            trackIds.UnionWith(storedIds);
        }

        return trackIds.Count == 1 ? trackIds.Single() : null;
    }

    private async Task<Guid?> FindUniqueMetadataTrackIdAsync(
        TrackIdentityQuery query,
        CancellationToken cancellationToken)
    {
        var queryObservation = CreateQueryObservation(query);
        var queryHypotheses = TrackObservationParser.ParseSearchHypotheses(queryObservation);
        var normalizedTitles = queryHypotheses.Select(hypothesis =>
                TrackTextNormalizer.Normalize(hypothesis.SearchTitle))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (normalizedTitles.Length == 0)
        {
            return null;
        }
        var normalizedArtists = queryHypotheses.Select(hypothesis =>
                TrackTextNormalizer.Normalize(hypothesis.SearchArtist))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var storedCandidates = await _dbContext.TrackObservations
            .AsNoTracking()
            .Include(candidate => candidate.Track)!
                .ThenInclude(track => track!.ArtistCredits)
            .Where(candidate =>
                candidate.TrackId != null
                && (normalizedTitles.Contains(candidate.NormalizedTitle!)
                    || candidate.NormalizedTitle == null
                    || candidate.NormalizedTitle == string.Empty)
                && (candidate.SourceType == "youtube"
                    || normalizedArtists.Length == 0
                    || normalizedArtists.Contains(candidate.NormalizedArtist!)
                    || candidate.NormalizedArtist == null
                    || candidate.NormalizedArtist == string.Empty))
            .Take(MaximumMetadataCandidates + 1)
            .ToListAsync(cancellationToken);

        if (storedCandidates.Count > MaximumMetadataCandidates)
        {
            return null;
        }

        var candidates = _dbContext.TrackObservations.Local
            .Where(candidate =>
                candidate.TrackId != null
                && (normalizedTitles.Contains(candidate.NormalizedTitle ?? string.Empty)
                    || string.IsNullOrWhiteSpace(candidate.NormalizedTitle))
                && (candidate.SourceType == "youtube"
                    || normalizedArtists.Contains(TrackTextNormalizer.Normalize(candidate.NormalizedArtist))
                    || string.IsNullOrWhiteSpace(candidate.NormalizedArtist)))
            .Concat(storedCandidates)
            .DistinctBy(candidate => candidate.Id);
        var candidateObservations = candidates.Where(candidate => candidate.TrackId is not null).ToArray();
        if (candidateObservations.Length == 0)
            return null;

        var candidateTrackIds = candidateObservations.Select(candidate => candidate.TrackId!.Value)
            .Distinct().ToArray();
        var candidateTracks = candidateObservations.Where(candidate => candidate.Track is not null)
            .Select(candidate => candidate.Track!)
            .Concat(_dbContext.Tracks.Local.Where(track => candidateTrackIds.Contains(track.Id)))
            .DistinctBy(track => track.Id)
            .ToDictionary(track => track.Id);
        var missingTrackIds = candidateTrackIds.Where(trackId => !candidateTracks.ContainsKey(trackId)).ToArray();
        if (missingTrackIds.Length > 0)
        {
            var missingTracks = await _dbContext.Tracks.AsNoTracking()
                .Include(track => track.ArtistCredits)
                .Where(track => missingTrackIds.Contains(track.Id))
                .ToListAsync(cancellationToken);
            foreach (var track in missingTracks)
                candidateTracks.TryAdd(track.Id, track);
        }

        var matchedObservations = candidateObservations
            .Where(candidate => candidate.TrackId is { } trackId
                && candidateTracks.TryGetValue(trackId, out var track)
                && StableIdentitiesAreCompatible(query, track))
            .ToArray();
        if (matchedObservations.Length == 0)
            return null;

        var byIdentity = matchedObservations.ToDictionary(
            candidate => (candidate.SourceType, candidate.ExternalId));
        var searchCandidates = matchedObservations.Select(candidate => ToSearchCandidate(
            candidate, candidateTracks[candidate.TrackId!.Value])).ToArray();
        var assessment = TrackMatchDecisionEngine.Evaluate(queryObservation, searchCandidates, _options);
        if (assessment.Decision.AcceptedCandidate is not { } accepted)
            return null;

        // Several stored source observations can support one Track. Never choose
        // between distinct Tracks just because one provider snapshot scores higher.
        // Ineligible alternatives, such as a Live version, do not make an otherwise
        // accepted identity ambiguous.
        var credibleTrackIds = assessment.Ranked
            .Where(result => result.IsAutoMatchEligible
                && result.Score >= _options.AmbiguousThreshold)
            .Select(result => byIdentity.TryGetValue(
                (result.Candidate.CandidateSource, result.Candidate.ExternalId), out var observation)
                    ? observation.TrackId
                    : null)
            .Where(trackId => trackId.HasValue)
            .Select(trackId => trackId!.Value)
            .Distinct()
            .Take(2)
            .ToArray();
        if (credibleTrackIds.Length != 1)
            return null;

        return byIdentity.TryGetValue(
                (accepted.Candidate.CandidateSource, accepted.Candidate.ExternalId), out var acceptedObservation)
            && acceptedObservation.TrackId == credibleTrackIds[0]
                ? acceptedObservation.TrackId
                : null;
    }

    private async Task<bool> MetadataCorroboratesTrackAsync(
        TrackIdentityQuery query,
        Guid trackId,
        CancellationToken cancellationToken)
    {
        var track = await LoadRequiredTrackAsync(trackId, cancellationToken);
        if (!StableIdentitiesAreCompatible(query, track))
            return false;

        var observations = _dbContext.TrackObservations.Local
            .Where(observation => observation.TrackId == trackId)
            .Concat(await _dbContext.TrackObservations
                .AsNoTracking()
                .Include(observation => observation.Track)!
                    .ThenInclude(candidateTrack => candidateTrack!.ArtistCredits)
                .Where(observation => observation.TrackId == trackId)
                .Take(MaximumMetadataCandidates + 1)
                .ToListAsync(cancellationToken))
            .DistinctBy(observation => observation.Id);
        var storedObservations = observations.Take(MaximumMetadataCandidates + 1).ToArray();
        if (storedObservations.Length == 0 || storedObservations.Length > MaximumMetadataCandidates)
            return false;

        var queryObservation = CreateQueryObservation(query);
        var candidates = storedObservations
            .Select(observation => ToSearchCandidate(observation, track))
            .ToArray();
        var assessment = TrackMatchDecisionEngine.Evaluate(queryObservation, candidates, _options);
        return assessment.Decision.AcceptedCandidate is not null;
    }

    private static TrackObservation CreateQueryObservation(TrackIdentityQuery query)
    {
        var artist = query.ArtistCredits is { Count: > 0 }
            ? string.Join(", ", query.ArtistCredits)
            : query.Artist;
        return new TrackObservation
        {
            Id = Guid.Empty,
            SourceType = query.SourceType,
            ExternalId = query.ExternalId,
            Title = query.Title,
            Artist = artist,
            RawMetadata = query.RawMetadata,
            DurationSeconds = query.DurationSeconds,
            MatchStatus = TrackMatchingStatuses.Pending
        };
    }

    private static TrackMatchSearchCandidate ToSearchCandidate(
        TrackObservation observation,
        Track track)
    {
        return new TrackMatchSearchCandidate
        {
            CandidateSource = observation.SourceType,
            ExternalId = observation.ExternalId,
            Title = observation.Title,
            Artist = observation.Artist,
            ArtistCredits = track?.ArtistCredits
                .OrderBy(credit => credit.Position)
                .Select(credit => credit.CreditedName).ToArray() ?? [],
            Isrc = track?.Isrc,
            MbidRecording = track?.MbidRecording,
            DurationSeconds = observation.DurationSeconds,
            RawMetadata = observation.RawMetadata
        };
    }

    private static bool StableIdentitiesAreCompatible(
        TrackIdentityQuery query,
        Track track)
    {
        var queryIsrc = NormalizeIsrc(query.Isrc);
        var trackIsrc = NormalizeIsrc(track.Isrc);
        if (queryIsrc != null
            && trackIsrc != null
            && !string.Equals(queryIsrc, trackIsrc, StringComparison.Ordinal))
        {
            return false;
        }

        var queryMusicBrainzId = NormalizeStableId(query.MusicBrainzRecordingId);
        var trackMusicBrainzId = NormalizeStableId(track.MbidRecording);
        return queryMusicBrainzId == null
            || trackMusicBrainzId == null
            || string.Equals(
                queryMusicBrainzId,
                trackMusicBrainzId,
                StringComparison.Ordinal);
    }

    private async Task<Track> LoadRequiredTrackAsync(
        Guid trackId,
        CancellationToken cancellationToken)
    {
        var localTrack = _dbContext.Tracks.Local.FirstOrDefault(track => track.Id == trackId);
        if (localTrack != null)
        {
            var credits = _dbContext.Entry(localTrack).Collection(track => track.ArtistCredits);
            if (_dbContext.Entry(localTrack).State != EntityState.Added && !credits.IsLoaded)
            {
                await credits.Query()
                    .Include(credit => credit.Artist)
                    .LoadAsync(cancellationToken);
            }

            return localTrack;
        }

        return await _dbContext.Tracks
            .Include(track => track.ArtistCredits)
                .ThenInclude(credit => credit.Artist)
            .SingleOrDefaultAsync(track => track.Id == trackId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Track identity mapping references missing track {trackId}.");
    }

    private static string? NormalizeStableId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static string[] StableIdVariants(string stableId) =>
    [
        stableId,
        stableId.ToUpperInvariant()
    ];

    private static string[] IsrcVariants(string isrc)
    {
        if (isrc.Length != 12)
        {
            return [isrc, isrc.ToLowerInvariant()];
        }

        var formatted = $"{isrc[..2]}-{isrc[2..5]}-{isrc[5..7]}-{isrc[7..]}";
        return
        [
            isrc,
            isrc.ToLowerInvariant(),
            formatted,
            formatted.ToLowerInvariant()
        ];
    }

    public static string? NormalizeIsrc(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return string.Concat(value.Where(char.IsLetterOrDigit)).ToUpperInvariant();
    }
}
