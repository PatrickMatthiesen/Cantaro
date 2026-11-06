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
    string? RawMetadata = null,
    int? OwnerUserId = null);

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
        var now = DateTimeOffset.UtcNow;
        var exactSource = _dbContext.TrackSourceIds.Local.FirstOrDefault(sourceId =>
            sourceId.SourceType == query.SourceType
            && sourceId.ExternalId == query.ExternalId
            && ProviderCatalogPolicy.IsVerified(sourceId, now));
        exactSource ??= await _dbContext.TrackSourceIds.Include(source => source.ProviderCatalogItem).FirstOrDefaultAsync(
            sourceId => sourceId.SourceType == query.SourceType
                && sourceId.ExternalId == query.ExternalId
                && sourceId.AssociationState == RecordingAssociationState.Verified
                && sourceId.ProviderCatalogItemId != null,
            cancellationToken);

        if (exactSource != null && ProviderCatalogPolicy.IsVerified(exactSource, now))
        {
            var exactTrack = await LoadRequiredTrackAsync(
                exactSource.TrackId,
                cancellationToken);
            return new TrackIdentityMatch(
                exactTrack,
                TrackIdentityMatchKind.ExactSource,
                "Matched a current verified public provider association.",
                exactSource);
        }

        // An imported provider ID and its payload are private evidence. Reuse a
        // previously linked observation only for the explicitly supplied owner.
        if (query.OwnerUserId is { } ownerId)
        {
            var owned = _dbContext.TrackObservations.Local.FirstOrDefault(observation =>
                observation.SourceType == query.SourceType && observation.ExternalId == query.ExternalId
                && observation.TrackId != null && ProviderCatalogPolicy.HasOwnedSnapshot(observation, ownerId));
            owned ??= await _dbContext.TrackObservations.AsNoTracking().FirstOrDefaultAsync(observation =>
                observation.SourceType == query.SourceType && observation.ExternalId == query.ExternalId
                && observation.TrackId != null && observation.OwnerUserId == ownerId
                && observation.Title.Trim() != "", cancellationToken);
            if (owned?.TrackId is { } ownedTrackId)
            {
                return new TrackIdentityMatch(
                    await LoadRequiredTrackAsync(ownedTrackId, cancellationToken),
                    TrackIdentityMatchKind.ExactSource,
                    "Matched this user's existing source observation.");
            }

            var ownedMetadataTrackId = await FindUniqueMetadataTrackIdAsync(query, cancellationToken);
            if (ownedMetadataTrackId is { } privateTrackId)
                return new TrackIdentityMatch(await LoadRequiredTrackAsync(privateTrackId, cancellationToken),
                    TrackIdentityMatchKind.Metadata, "Matched this user's existing observation metadata.");
        }

        // Shared fallback is built only from current catalog items whose
        // recording association has been independently verified.
        var normalizedIsrc = NormalizeIsrc(query.Isrc);
        var normalizedMusicBrainzId = TrackTextNormalizer.Normalize(query.MusicBrainzRecordingId);
        var normalizedTitle = TrackTextNormalizer.Normalize(query.Title);
        var normalizedArtist = TrackTextNormalizer.Normalize(query.Artist);
        var sourceRows = await _dbContext.TrackSourceIds.AsNoTracking()
            .Include(source => source.ProviderCatalogItem)
            .Where(source => source.AssociationState == RecordingAssociationState.Verified
                && source.ProviderCatalogItemId != null
                && source.CatalogRevision == source.ProviderCatalogItem!.Revision
                && source.SourceType == source.ProviderCatalogItem.SourceType
                && source.ExternalId == source.ProviderCatalogItem.ExternalId
                && source.ProviderCatalogItem.VisibilityState == CatalogVisibility.Public
                && source.ProviderCatalogItem.VerifiedAt != null
                && source.ProviderCatalogItem.VerifiedAt <= now
                && source.ProviderCatalogItem.RevalidateAfter != null
                && source.ProviderCatalogItem.RevalidateAfter > now
                && ((source.SourceType == "spotify"
                        && source.ProviderCatalogItem.VerificationMethod == CatalogVerificationMethods.SpotifyApplication)
                    || (source.SourceType == "youtube"
                        && source.ProviderCatalogItem.VerificationMethod == CatalogVerificationMethods.YouTubePublicVideo)
                    || (source.SourceType == "musicbrainz"
                        && source.ProviderCatalogItem.VerificationMethod == CatalogVerificationMethods.MusicBrainz))
                && (source.SourceType == query.SourceType && source.ExternalId == query.ExternalId
                    || normalizedMusicBrainzId.Length > 0
                        && source.ProviderCatalogItem.NormalizedMusicBrainzRecordingId == normalizedMusicBrainzId
                    || normalizedIsrc != null && source.ProviderCatalogItem.NormalizedIsrc == normalizedIsrc
                    || normalizedTitle.Length > 0 && normalizedArtist.Length > 0
                        && source.ProviderCatalogItem.NormalizedTitle == normalizedTitle
                        && source.ProviderCatalogItem.NormalizedArtist == normalizedArtist))
            .OrderBy(source => source.SourceType).ThenBy(source => source.ExternalId)
            .Take(MaximumMetadataCandidates + 1).ToListAsync(cancellationToken);
        var rows = sourceRows.Concat(_dbContext.TrackSourceIds.Local)
            .DistinctBy(source => source.Id)
            .Where(source => MatchesPublicCatalogQuery(source, query, normalizedIsrc,
                normalizedMusicBrainzId, normalizedTitle, normalizedArtist, now)).ToArray();
        if (rows.Length > MaximumMetadataCandidates) return null;
        var trackIds = rows.Select(source => source.TrackId).Distinct().ToArray();
        var tracks = await _dbContext.Tracks.AsNoTracking().Include(track => track.ArtistCredits)
            .Where(track => trackIds.Contains(track.Id)).ToListAsync(cancellationToken);
        var trackById = tracks.Concat(_dbContext.Tracks.Local).DistinctBy(track => track.Id)
            .ToDictionary(track => track.Id);
        var queryObservation = CreateQueryObservation(query);
        var candidates = rows.Where(source => trackById.ContainsKey(source.TrackId))
            .Select(source =>
            {
                var facts = ProviderCatalogPolicy.ReadFacts(source.ProviderCatalogItem, now);
                return facts is null ? null : ProviderCatalogPolicy.Candidate(source.ProviderCatalogItem!, facts);
            }).Where(candidate => candidate is not null).Cast<TrackMatchSearchCandidate>().ToArray();
        if (candidates.Length == 0) return null;
        var assessment = TrackMatchDecisionEngine.Evaluate(queryObservation, candidates, _options);
        if (assessment.Decision.AcceptedCandidate is not { } accepted) return null;
        var credibleTrackIds = assessment.Ranked.Where(result => result.IsAutoMatchEligible
                && result.Score >= _options.AmbiguousThreshold)
            .Select(result => rows.FirstOrDefault(source => source.SourceType == result.Candidate.CandidateSource
                && source.ExternalId == result.Candidate.ExternalId)?.TrackId)
            .Where(id => id.HasValue).Select(id => id!.Value).Distinct().Take(2).ToArray();
        if (credibleTrackIds.Length != 1) return null;
        var acceptedRow = rows.FirstOrDefault(source => source.SourceType == accepted.Candidate.CandidateSource
            && source.ExternalId == accepted.Candidate.ExternalId
            && ProviderCatalogPolicy.IsVerified(source, now)
            && source.TrackId == credibleTrackIds[0]);
        if (acceptedRow is null || !trackById.ContainsKey(acceptedRow.TrackId)) return null;
        return new TrackIdentityMatch(await LoadRequiredTrackAsync(acceptedRow.TrackId, cancellationToken), TrackIdentityMatchKind.Metadata,
            "Matched against a current verified public provider record.", acceptedRow);
    }

    private static bool MatchesPublicCatalogQuery(
        TrackSourceId source,
        TrackIdentityQuery query,
        string? normalizedIsrc,
        string normalizedMusicBrainzId,
        string normalizedTitle,
        string normalizedArtist,
        DateTimeOffset now)
    {
        if (!ProviderCatalogPolicy.IsVerified(source, now) || source.ProviderCatalogItem is not { } item)
            return false;
        var normalizedFacts = ProviderCatalogNormalizedFacts.FromJson(item.FactsJson);
        var title = item.NormalizedTitle ?? normalizedFacts.Title;
        var artist = item.NormalizedArtist ?? normalizedFacts.Artist;
        var isrc = item.NormalizedIsrc ?? normalizedFacts.Isrc;
        var musicBrainzId = item.NormalizedMusicBrainzRecordingId ?? normalizedFacts.MusicBrainzRecordingId;
        return source.SourceType == query.SourceType && source.ExternalId == query.ExternalId
            || normalizedMusicBrainzId.Length > 0 && musicBrainzId == normalizedMusicBrainzId
            || normalizedIsrc is not null && isrc == normalizedIsrc
            || normalizedTitle.Length > 0 && normalizedArtist.Length > 0
                && title == normalizedTitle && artist == normalizedArtist;
    }

    private async Task<Guid?> FindUniqueMetadataTrackIdAsync(
        TrackIdentityQuery query,
        CancellationToken cancellationToken)
    {
        if (query.OwnerUserId is not { } ownerId) return null;
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
                && candidate.OwnerUserId == ownerId
                && candidate.Title.Trim() != ""
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
                && candidate.OwnerUserId == ownerId
                && !string.IsNullOrWhiteSpace(candidate.Title)
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
                && candidateTracks.ContainsKey(trackId))
            .ToArray();
        if (matchedObservations.Length == 0)
            return null;

        var byIdentity = matchedObservations.ToDictionary(
            candidate => (candidate.SourceType, candidate.ExternalId));
        var searchCandidates = matchedObservations.Select(candidate => ToSearchCandidate(
            candidate, candidateTracks[candidate.TrackId!.Value], includeTrackFacts: false)).ToArray();
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

    private static TrackObservation CreateQueryObservation(TrackIdentityQuery query)
    {
        var artist = query.ArtistCredits is { Count: > 0 }
            ? string.Join(", ", query.ArtistCredits)
            : query.Artist;
        var observation = new TrackObservation
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
        if (!string.IsNullOrWhiteSpace(query.Isrc) || !string.IsNullOrWhiteSpace(query.MusicBrainzRecordingId))
        {
            var metadata = TrackObservationParser.ReadMetadata(observation) ?? new TrackObservationMetadata();
            metadata.Isrc = query.Isrc ?? metadata.Isrc;
            metadata.MbidRecording = query.MusicBrainzRecordingId ?? metadata.MbidRecording;
            observation.RawMetadata = System.Text.Json.JsonSerializer.Serialize(metadata);
        }
        return observation;
    }

    private static TrackMatchSearchCandidate ToSearchCandidate(
        TrackObservation observation,
        Track track,
        bool includeTrackFacts = true)
    {
        return new TrackMatchSearchCandidate
        {
            CandidateSource = observation.SourceType,
            ExternalId = observation.ExternalId,
            Title = observation.Title,
            Artist = observation.Artist,
            ArtistCredits = includeTrackFacts ? track?.ArtistCredits
                .OrderBy(credit => credit.Position)
                .Select(credit => credit.CreditedName).ToArray() ?? [] : [],
            Isrc = includeTrackFacts ? track?.Isrc : TrackObservationParser.ReadMetadata(observation)?.Isrc,
            MbidRecording = includeTrackFacts ? track?.MbidRecording : TrackObservationParser.ReadMetadata(observation)?.MbidRecording,
            DurationSeconds = observation.DurationSeconds,
            RawMetadata = observation.RawMetadata
        };
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
            var sources = _dbContext.Entry(localTrack).Collection(track => track.SourceIds);
            if (_dbContext.Entry(localTrack).State != EntityState.Added && !sources.IsLoaded)
            {
                await sources.Query().Include(source => source.ProviderCatalogItem).LoadAsync(cancellationToken);
            }
            else
            {
                foreach (var source in localTrack.SourceIds)
                {
                    var catalogItem = _dbContext.Entry(source).Reference(item => item.ProviderCatalogItem);
                    if (_dbContext.Entry(source).State != EntityState.Added && !catalogItem.IsLoaded)
                        await catalogItem.LoadAsync(cancellationToken);
                }
            }

            return localTrack;
        }

        return await _dbContext.Tracks
            .Include(track => track.ArtistCredits)
                .ThenInclude(credit => credit.Artist)
            .Include(track => track.SourceIds)
                .ThenInclude(source => source.ProviderCatalogItem)
            .SingleOrDefaultAsync(track => track.Id == trackId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Track identity mapping references missing track {trackId}.");
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
