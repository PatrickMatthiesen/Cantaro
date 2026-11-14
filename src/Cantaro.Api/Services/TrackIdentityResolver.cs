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
    ProviderCatalogItem? ExactSourceMapping = null);

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
        var exactSource = _dbContext.ProviderCatalogItems.Local.FirstOrDefault(sourceId =>
            sourceId.SourceType == query.SourceType
            && sourceId.ExternalId == query.ExternalId
            && ProviderCatalogPolicy.IsVerified(sourceId, now));
        exactSource ??= await _dbContext.ProviderCatalogItems.FirstOrDefaultAsync(
            sourceId => sourceId.SourceType == query.SourceType
                && sourceId.ExternalId == query.ExternalId
                && sourceId.AssociationState == RecordingAssociationState.Verified
                && sourceId.TrackId != null,
            cancellationToken);

        if (exactSource != null && ProviderCatalogPolicy.IsVerified(exactSource, now))
        {
            var exactTrack = await LoadRequiredTrackAsync(
                exactSource.TrackId!.Value,
                cancellationToken);
            return new TrackIdentityMatch(
                exactTrack,
                TrackIdentityMatchKind.ExactSource,
                "Matched a current verified public provider association.",
                exactSource);
        }

        // An owner-specific processing result can reuse this exact provider ID
        // without establishing a shared recording association.
        if (query.OwnerUserId is { } ownerId)
        {
            var owned = _dbContext.TrackObservations.Local.FirstOrDefault(observation =>
                observation.SourceType == query.SourceType && observation.ExternalId == query.ExternalId
                && observation.InputKind != ObservationInputKind.Reference && observation.TrackId != null && ProviderCatalogPolicy.HasOwnedInput(observation, ownerId));
            owned ??= await ObservationInput.Eligible(_dbContext.TrackObservations.AsNoTracking(), now).Include(o => o.ProviderCatalogItem).FirstOrDefaultAsync(observation =>
                observation.SourceType == query.SourceType && observation.ExternalId == query.ExternalId
                && observation.InputKind != ObservationInputKind.Reference && observation.TrackId != null && observation.OwnerUserId == ownerId
, cancellationToken);
            // Fresh provider metadata cannot renew an automatic recording result
            // when the association that produced it no longer validates.
            var invalidAutomaticResult = owned is
                { InputKind: ObservationInputKind.Public, ProviderCatalogItem: { } catalog }
                && catalog.TrackId == owned.TrackId && !ProviderCatalogPolicy.IsVerified(catalog, now)
                && !ObservationInput.HasReviewedResult(owned);
            if (!invalidAutomaticResult && owned?.TrackId is { } ownedTrackId
                && ProviderCatalogPolicy.HasOwnedInput(owned, ownerId, now))
            {
                return new TrackIdentityMatch(
                    await LoadRequiredTrackAsync(ownedTrackId, cancellationToken),
                    TrackIdentityMatchKind.ExactSource,
                    "Matched this user's existing source observation.");
            }
        }

        // Shared matching uses only current catalog items whose
        // recording association has been independently verified.
        var normalizedIsrc = NormalizeIsrc(query.Isrc);
        var normalizedMusicBrainzId = TrackTextNormalizer.Normalize(query.MusicBrainzRecordingId);
        var normalizedTitle = TrackTextNormalizer.Normalize(query.Title);
        var normalizedArtist = TrackTextNormalizer.Normalize(query.Artist);
        var sourceRows = await ProviderCatalogPolicy.CurrentSources(_dbContext.ProviderCatalogItems.AsNoTracking(), now)
            .Where(source => true
                && (source.SourceType == query.SourceType && source.ExternalId == query.ExternalId
                    || normalizedMusicBrainzId.Length > 0
                        && source.NormalizedMusicBrainzRecordingId == normalizedMusicBrainzId
                    || normalizedIsrc != null && source.NormalizedIsrc == normalizedIsrc
                    || normalizedTitle.Length > 0 && normalizedArtist.Length > 0
                        && source.NormalizedTitle == normalizedTitle
                        && source.NormalizedArtist == normalizedArtist))
            .OrderBy(source => source.SourceType).ThenBy(source => source.ExternalId)
            .Take(MaximumMetadataCandidates + 1).ToListAsync(cancellationToken);
        var rows = sourceRows.Concat(_dbContext.ProviderCatalogItems.Local)
            .DistinctBy(source => source.Id)
            .Where(source => MatchesPublicCatalogQuery(source, query, normalizedIsrc,
                normalizedMusicBrainzId, normalizedTitle, normalizedArtist, now)).ToArray();
        // Private metadata is a last resort when no shared recording matches the
        // query. It must not override a shared match or resolve shared ambiguity.
        if (rows.Length == 0)
        {
            var ownedTrackId = await FindUniqueMetadataTrackIdAsync(query, cancellationToken);
            return ownedTrackId is { } trackId
                ? new TrackIdentityMatch(await LoadRequiredTrackAsync(trackId, cancellationToken),
                    TrackIdentityMatchKind.Metadata, "Matched this user's existing observation metadata.")
                : null;
        }
        if (rows.Length > MaximumMetadataCandidates) return null;
        var trackIds = rows.Select(source => source.TrackId!.Value).Distinct().ToArray();
        var tracks = await _dbContext.Tracks.AsNoTracking().Include(track => track.ArtistCredits)
            .Where(track => trackIds.Contains(track.Id)).ToListAsync(cancellationToken);
        var trackById = tracks.Concat(_dbContext.Tracks.Local).DistinctBy(track => track.Id)
            .ToDictionary(track => track.Id);
        var queryObservation = CreateQueryObservation(query);
        var candidates = rows.Where(source => trackById.ContainsKey(source.TrackId!.Value))
            .Select(source =>
            {
                var facts = ProviderCatalogPolicy.ReadFacts(source, now);
                return facts is null ? null : ProviderCatalogPolicy.Candidate(source, facts);
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
        if (acceptedRow is null || !trackById.ContainsKey(acceptedRow.TrackId!.Value)) return null;
        return new TrackIdentityMatch(await LoadRequiredTrackAsync(acceptedRow.TrackId!.Value, cancellationToken), TrackIdentityMatchKind.Metadata,
            "Matched against a current verified public provider record.", acceptedRow);
    }

    private static bool MatchesPublicCatalogQuery(
        ProviderCatalogItem source,
        TrackIdentityQuery query,
        string? normalizedIsrc,
        string normalizedMusicBrainzId,
        string normalizedTitle,
        string normalizedArtist,
        DateTimeOffset now)
    {
        if (!ProviderCatalogPolicy.IsVerified(source, now))
            return false;
        var item = source;
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
        var scoredCandidates = new List<TrackMatchScoredCandidate>();
        var byIdentity = new Dictionary<(string Source, string Id), Guid>();
        var seen = new HashSet<Guid>();
        var now = DateTimeOffset.UtcNow;
        void Consider(TrackObservation candidate)
        {
            if (!seen.Add(candidate.Id) || candidate.InputKind != ObservationInputKind.Private
                || candidate.TrackId is not { } trackId
                || ObservationInput.Read(candidate, ownerId, now) is not { } input) return;
            var scored = TrackMatchScorer.Score(queryObservation, ToSearchCandidate(input), _options);
            if (scored.Score < _options.MinimumCandidateScore) return;
            byIdentity[(candidate.SourceType, candidate.ExternalId)] = trackId;
            scoredCandidates.Add(scored);
        }
        // Read owner-only input in bounded batches, without recording/credit collections.
        // Unrelated rows cannot consume a fixed library-wide candidate allowance.
        foreach (var candidate in _dbContext.TrackObservations.Local) Consider(candidate);
        var storedInputs = _dbContext.TrackObservations.AsNoTracking()
            .Where(candidate => candidate.OwnerUserId == ownerId
                && candidate.InputKind == ObservationInputKind.Private && candidate.TrackId != null
                && candidate.Title != null && candidate.Title.Trim() != "");
        Guid? afterId = null;
        while (true)
        {
            var page = afterId is { } previousId
                ? storedInputs.Where(candidate => candidate.Id.CompareTo(previousId) > 0) : storedInputs;
            var batch = await page.OrderBy(candidate => candidate.Id).Take(MaximumMetadataCandidates)
                .ToListAsync(cancellationToken);
            foreach (var candidate in batch) Consider(candidate);
            if (batch.Count < MaximumMetadataCandidates) break;
            afterId = batch[^1].Id;
        }
        var assessment = TrackMatchDecisionEngine.EvaluateScored(scoredCandidates, _options);
        if (assessment.Decision.AcceptedCandidate is not { } accepted) return null;
        // Several stored source observations can support one Track. Never choose
        // between distinct Tracks just because one provider snapshot scores higher.
        // Ineligible alternatives, such as a Live version, do not make an otherwise
        // accepted identity ambiguous.
        var credibleTrackIds = assessment.Ranked
            .Where(result => result.IsAutoMatchEligible
                && result.Score >= _options.AmbiguousThreshold)
            .Select(result => byIdentity.TryGetValue(
                (result.Candidate.CandidateSource, result.Candidate.ExternalId), out var observation)
                    ? (Guid?)observation
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
            && acceptedObservation == credibleTrackIds[0]
                ? (Guid?)acceptedObservation
                : null;
    }

    private static ObservationInput CreateQueryObservation(TrackIdentityQuery query)
    {
        var metadata = ObservationInput.ReadOperationMetadata(query.RawMetadata);
        metadata.Isrc = query.Isrc ?? metadata.Isrc;
        metadata.MbidRecording = query.MusicBrainzRecordingId ?? metadata.MbidRecording;
        return new ObservationInput
        {
            SourceType = query.SourceType, ExternalId = query.ExternalId, Title = query.Title,
            Artist = query.ArtistCredits is { Count: > 0 } ? string.Join(", ", query.ArtistCredits) : query.Artist,
            DurationSeconds = query.DurationSeconds, Metadata = metadata
        };
    }

    private static TrackMatchSearchCandidate ToSearchCandidate(ObservationInput input)
    {
        return new TrackMatchSearchCandidate
        {
            CandidateSource = input.SourceType, ExternalId = input.ExternalId,
            Title = input.Title, Artist = input.Artist, DurationSeconds = input.DurationSeconds,
            ArtistCredits = input.Metadata.ArtistIdentities.Select(credit => credit.Name).ToArray(),
            ArtistIdentities = input.Metadata.ArtistIdentities,
            Isrc = input.Metadata.Isrc, MbidRecording = input.Metadata.MbidRecording,
            RawMetadata = System.Text.Json.JsonSerializer.Serialize(input.Metadata)
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
            var sources = _dbContext.Entry(localTrack).Collection(track => track.ProviderItems);
            if (_dbContext.Entry(localTrack).State != EntityState.Added && !sources.IsLoaded)
            {
                await sources.Query().LoadAsync(cancellationToken);
            }

            return localTrack;
        }

        return await _dbContext.Tracks
            .Include(track => track.ArtistCredits)
                .ThenInclude(credit => credit.Artist)
            .Include(track => track.ProviderItems)

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
