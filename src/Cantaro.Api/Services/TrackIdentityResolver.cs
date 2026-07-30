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
    IReadOnlyList<string>? ArtistCredits = null);

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
        var parsedQuery = TrackMetadataParser.Parse(query.Title, query.Artist);
        var normalizedTitle = TrackTextNormalizer.Normalize(parsedQuery.SearchTitle);
        if (normalizedTitle.Length == 0 || query.DurationSeconds == null)
        {
            return null;
        }

        var minimumDuration = Math.Max(
            0,
            query.DurationSeconds.Value - _options.AutoMatchDurationToleranceSeconds);
        var maximumDuration =
            query.DurationSeconds.Value + _options.AutoMatchDurationToleranceSeconds;

        var storedCandidates = await _dbContext.TrackObservations
            .AsNoTracking()
            .Include(candidate => candidate.Track)
            .Where(candidate =>
                candidate.TrackId != null
                && (candidate.NormalizedTitle == normalizedTitle
                    || candidate.NormalizedTitle == null
                    || candidate.NormalizedTitle == string.Empty)
                && candidate.DurationSeconds >= minimumDuration
                && candidate.DurationSeconds <= maximumDuration)
            .Take(MaximumMetadataCandidates + 1)
            .ToListAsync(cancellationToken);

        if (storedCandidates.Count > MaximumMetadataCandidates)
        {
            return null;
        }

        var candidates = _dbContext.TrackObservations.Local
            .Where(candidate =>
                candidate.TrackId != null
                && (candidate.NormalizedTitle == normalizedTitle
                    || string.IsNullOrWhiteSpace(candidate.NormalizedTitle))
                && candidate.DurationSeconds >= minimumDuration
                && candidate.DurationSeconds <= maximumDuration)
            .Concat(storedCandidates)
            .DistinctBy(candidate => candidate.Id);

        var incomingCredits = TrackMetadataParser.NormalizeArtistCredits(
            query.ArtistCredits is { Count: > 0 }
                ? query.ArtistCredits
                : parsedQuery.ArtistCredits);

        var metadataMatchingTrackIds = candidates
            .Where(candidate => MetadataMatches(parsedQuery, incomingCredits, candidate))
            .Select(candidate => candidate.TrackId!.Value)
            .Distinct()
            .ToArray();

        if (metadataMatchingTrackIds.Length == 0)
        {
            return null;
        }

        var compatibleTrackIds = _dbContext.Tracks.Local
            .Where(track => metadataMatchingTrackIds.Contains(track.Id))
            .Concat(storedCandidates
                .Where(candidate => candidate.Track != null
                    && metadataMatchingTrackIds.Contains(candidate.Track.Id))
                .Select(candidate => candidate.Track!))
            .DistinctBy(track => track.Id)
            .Where(track => StableIdentitiesAreCompatible(query, track))
            .Select(track => track.Id)
            .Take(2)
            .ToArray();

        return compatibleTrackIds.Length == 1 ? compatibleTrackIds[0] : null;
    }

    private async Task<bool> MetadataCorroboratesTrackAsync(
        TrackIdentityQuery query,
        Guid trackId,
        CancellationToken cancellationToken)
    {
        if (query.DurationSeconds == null)
        {
            return false;
        }

        var parsedQuery = TrackMetadataParser.Parse(query.Title, query.Artist);
        var incomingCredits = TrackMetadataParser.NormalizeArtistCredits(
            query.ArtistCredits is { Count: > 0 }
                ? query.ArtistCredits
                : parsedQuery.ArtistCredits);
        var minimumDuration = Math.Max(
            0,
            query.DurationSeconds.Value - _options.AutoMatchDurationToleranceSeconds);
        var maximumDuration =
            query.DurationSeconds.Value + _options.AutoMatchDurationToleranceSeconds;

        var observations = _dbContext.TrackObservations.Local
            .Where(observation => observation.TrackId == trackId
                && observation.DurationSeconds >= minimumDuration
                && observation.DurationSeconds <= maximumDuration)
            .Concat(await _dbContext.TrackObservations
                .AsNoTracking()
                .Where(observation => observation.TrackId == trackId
                    && observation.DurationSeconds >= minimumDuration
                    && observation.DurationSeconds <= maximumDuration)
                .ToListAsync(cancellationToken))
            .DistinctBy(observation => observation.Id);

        return observations.Any(observation =>
            MetadataMatches(parsedQuery, incomingCredits, observation));
    }

    private static bool MetadataMatches(
        ParsedTrackMetadata parsedQuery,
        IReadOnlyList<string> incomingCredits,
        TrackObservation candidate)
    {
        var parsedCandidate = TrackMetadataParser.Parse(candidate.Title, candidate.Artist);
        var candidateCredits = TrackMetadataParser.NormalizeArtistCredits(parsedCandidate.ArtistCredits);
        var artistCreditsMatch = incomingCredits.Count > 0 && candidateCredits.Count > 0
            ? incomingCredits.SequenceEqual(candidateCredits, StringComparer.Ordinal)
            : string.Equals(
                TrackTextNormalizer.Normalize(parsedQuery.SearchArtist),
                TrackTextNormalizer.Normalize(parsedCandidate.SearchArtist),
                StringComparison.Ordinal);

        return TrackTextNormalizer.AreEquivalentTitles(
                parsedQuery.SearchTitle,
                parsedCandidate.SearchTitle)
            && artistCreditsMatch
            && parsedQuery.VersionMarkers.SequenceEqual(
                parsedCandidate.VersionMarkers,
                StringComparer.Ordinal)
            && parsedQuery.PlaybackModifiers.SequenceEqual(
                parsedCandidate.PlaybackModifiers,
                StringComparer.Ordinal);
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
