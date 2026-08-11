using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

/// <summary>
/// Generates and ranks MediaTitle candidates for a MediaObservation.
/// Conservative by design: prefers explicit no-match over false positives.
/// </summary>
public class MediaObservationMatchingService(
    ApplicationDbContext dbContext,
    ILogger<MediaObservationMatchingService> logger)
{
    private const decimal HighConfidenceThreshold = 0.85m;
    private const decimal LowConfidenceThreshold = 0.40m;

    private static readonly JsonSerializerOptions RawPayloadJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly ILogger<MediaObservationMatchingService> _logger = logger;

    /// <summary>
    /// Runs candidate generation for the given observation and updates its
    /// MatchStatus, Candidates, and related fields. Saves changes.
    /// </summary>
    public async Task<MediaObservation> ProcessObservationAsync(
        Guid observationId,
        CancellationToken cancellationToken)
    {
        var observation = await _dbContext.MediaObservations
            .Include(o => o.Candidates)
            .FirstOrDefaultAsync(o => o.Id == observationId, cancellationToken)
            ?? throw new InvalidOperationException($"MediaObservation {observationId} was not found.");

        return await ProcessObservationAsync(observation, cancellationToken);
    }

    internal async Task<MediaObservation> ProcessObservationAsync(
        MediaObservation observation,
        CancellationToken cancellationToken)
    {
        observation.MatchAttemptCount++;
        observation.LastMatchAttemptedAt = DateTimeOffset.UtcNow;
        observation.LastMatchError = null;

        try
        {
            if (!await TryApplyProviderEpisodeIdentityCoreAsync(observation, cancellationToken))
            {
                var candidates = await GenerateCandidatesAsync(observation, cancellationToken);

                // Remove stale candidates from previous runs.
                _dbContext.MediaObservationCandidates.RemoveRange(observation.Candidates);
                observation.Candidates.Clear();

                foreach (var candidate in candidates)
                {
                    _dbContext.MediaObservationCandidates.Add(candidate);
                }

                ApplyMatchDecision(observation, candidates);
                if (observation.MatchStatus == MediaObservationStatuses.Matched
                    && observation.MediaTitleId is { } matchedMediaTitleId)
                {
                    var inferredEpisodeOffset = await InferCumulativeEpisodeOffsetAsync(
                        observation,
                        matchedMediaTitleId,
                        cancellationToken);
                    if (inferredEpisodeOffset.HasValue || observation.EpisodeOffset is null)
                    {
                        observation.EpisodeOffset = inferredEpisodeOffset;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Candidate generation failed for MediaObservation {ObservationId}.", observation.Id);
            observation.LastMatchError = ex.Message;
            observation.MatchStatus = MediaObservationStatuses.Pending;
        }

        observation.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Processed MediaObservation {ObservationId}: status={Status}, candidates={CandidateCount}.",
            observation.Id,
            observation.MatchStatus,
            observation.Candidates.Count);

        return observation;
    }

    public async Task<bool> TryApplyProviderEpisodeIdentityAsync(
        MediaObservation observation,
        CancellationToken cancellationToken)
    {
        var applied = await TryApplyProviderEpisodeIdentityCoreAsync(observation, cancellationToken);
        if (!applied)
        {
            return false;
        }

        observation.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<bool> TryApplyProviderEpisodeIdentityCoreAsync(
        MediaObservation observation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(observation.SiteMediaId))
        {
            return false;
        }

        var provider = observation.SiteIdentifier.Trim().ToLowerInvariant();
        var providerEpisodeId = provider == MediaObservationSiteIdentifiers.Crunchyroll
            ? observation.SiteMediaId.Trim().ToUpperInvariant()
            : observation.SiteMediaId.Trim();
        var identity = await _dbContext.MediaEpisodeProviderIdentities
            .Include(item => item.MediaEpisode)
                .ThenInclude(episode => episode!.MediaTitle)
            .FirstOrDefaultAsync(item =>
                item.Provider == provider
                && item.ProviderEpisodeId == providerEpisodeId,
                cancellationToken);
        var episode = identity?.MediaEpisode;
        var title = episode?.MediaTitle;
        if (identity is null || episode is null || title is null
            || !IsCompatibleEpisodeIdentity(identity, observation.RawPayload))
        {
            return false;
        }

        _dbContext.MediaObservationCandidates.RemoveRange(observation.Candidates);
        observation.Candidates.Clear();
        var candidate = new MediaObservationCandidate
        {
            Id = Guid.NewGuid(),
            MediaObservationId = observation.Id,
            CandidateSource = MediaObservationCandidateSources.ProviderEpisodeIdentityExact,
            MediaTitleId = title.Id,
            Title = title.CanonicalTitle,
            MediaKind = title.MediaKind,
            Score = 1m,
            Explanation = $"Exact provider episode identity: {provider}/{providerEpisodeId}",
            IsAccepted = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.MediaObservationCandidates.Add(candidate);
        observation.MatchStatus = MediaObservationStatuses.Matched;
        observation.MediaTitleId = title.Id;
        observation.MediaTitle = title;
        observation.AcceptedCandidateId = candidate.Id;
        observation.ResolutionNotes = $"Automatically matched by exact provider episode identity: {provider}/{providerEpisodeId}.";
        if (MediaObservationProgressService.TryParseProgressHint(observation.ProgressHint, out var observedProgress))
        {
            observation.EpisodeOffset = episode.EpisodeNumber - observedProgress;
            observation.ResolvedProgress = episode.EpisodeNumber;
        }
        identity.HasConflict = false;

        _logger.LogInformation(
            "Matched MediaObservation {ObservationId} through provider episode identity {Provider}/{ProviderEpisodeId}: title {MediaTitleId}, episode {EpisodeNumber}.",
            observation.Id,
            provider,
            providerEpisodeId,
            title.Id,
            episode.EpisodeNumber);
        return true;
    }

    private static bool IsCompatibleEpisodeIdentity(
        MediaEpisodeProviderIdentity identity,
        string? rawPayload)
    {
        if (!identity.HasConflict)
        {
            return true;
        }

        var payload = DeserializeRawPayload(rawPayload);
        if (payload is null)
        {
            return false;
        }

        var seriesMatches = !string.IsNullOrWhiteSpace(identity.ProviderSeriesId)
            && string.Equals(
                identity.ProviderSeriesId,
                payload.ProviderSeriesId,
                StringComparison.OrdinalIgnoreCase);
        var episodeMatches = identity.ProviderEpisodeNumber is > 0
            && identity.ProviderEpisodeNumber == payload.EpisodeNumber;
        return seriesMatches && episodeMatches;
    }

    private static ObservationRawPayload? DeserializeRawPayload(string? rawPayload)
    {
        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ObservationRawPayload>(rawPayload, RawPayloadJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<List<MediaObservationCandidate>> GenerateCandidatesAsync(
        MediaObservation observation,
        CancellationToken cancellationToken)
    {
        var candidates = new List<MediaObservationCandidate>();

        // --- Strategy 1: exact provider link lookup via SiteMediaId ---
        if (!string.IsNullOrWhiteSpace(observation.SiteMediaId))
        {
            var exactCandidates = await FindByProviderLinkAsync(
                observation, observation.SiteMediaId, cancellationToken);
            candidates.AddRange(exactCandidates);
        }

        // If we already have a high-confidence exact match, skip fuzzy search.
        if (candidates.Any(c => c.Score >= HighConfidenceThreshold))
        {
            return [.. candidates.OrderByDescending(c => c.Score)];
        }

        // --- Strategy 2: fuzzy title search against user library entries ---
        var libraryCandidates = await FindByUserLibraryTitleAsync(
            observation, cancellationToken);
        MergeCandidates(candidates, libraryCandidates);

        // --- Strategy 3: fuzzy title search against full catalog ---
        var catalogCandidates = await FindByCatalogTitleAsync(
            observation, cancellationToken);
        MergeCandidates(candidates, catalogCandidates);

        return [.. candidates
            .Where(c => c.Score >= LowConfidenceThreshold)
            .OrderByDescending(c => c.Score)
            .Take(10)];
    }

    private async Task<List<MediaObservationCandidate>> FindByProviderLinkAsync(
        MediaObservation observation,
        string siteMediaId,
        CancellationToken cancellationToken)
    {
        // Map site identifier → provider ID convention.
        var provider = MapSiteToProvider(observation.SiteIdentifier);
        if (provider is null)
        {
            return [];
        }

        var link = await _dbContext.MediaProviderLinks
            .Include(l => l.MediaTitle)
            .FirstOrDefaultAsync(
                l => l.Provider == provider && l.ExternalId == siteMediaId,
                cancellationToken);

        if (link?.MediaTitle is null)
        {
            return [];
        }

        _logger.LogDebug(
            "Exact provider link match for observation {ObservationId}: {Provider}/{SiteMediaId} → MediaTitle {MediaTitleId}.",
            observation.Id, provider, siteMediaId, link.MediaTitleId);

        return
        [
            new MediaObservationCandidate
            {
                Id = Guid.NewGuid(),
                MediaObservationId = observation.Id,
                CandidateSource = MediaObservationCandidateSources.ProviderLinkExact,
                MediaTitleId = link.MediaTitleId,
                Provider = provider,
                ProviderMediaId = link.ExternalId,
                Title = link.MediaTitle.CanonicalTitle,
                MediaKind = link.MediaTitle.MediaKind,
                Score = 0.97m,
                Explanation = $"Exact provider link match: {provider}/{siteMediaId}",
                CreatedAt = DateTimeOffset.UtcNow
            }
        ];
    }

    private async Task<List<MediaObservationCandidate>> FindByUserLibraryTitleAsync(
        MediaObservation observation,
        CancellationToken cancellationToken)
    {
        var queryTitles = GetCandidateQueryTitles(observation);
        if (queryTitles.Count == 0)
        {
            return [];
        }

        // Load the user's library entries with their titles for in-memory scoring.
        var libraryTitles = await _dbContext.MediaLibraryEntries
            .Where(e => e.UserId == observation.UserId)
            .Include(e => e.MediaTitle)
            .Select(e => e.MediaTitle!)
            .Distinct()
            .ToListAsync(cancellationToken);

        var ordinalSeasonTargetId = await FindOrdinalSeasonTargetAsync(
            libraryTitles,
            ReadEpisodeEvidence(observation),
            cancellationToken);

        return ScoreTitleCandidates(
            observation,
            libraryTitles,
            queryTitles,
            MediaObservationCandidateSources.LibraryTitleSearch,
            ordinalSeasonTargetId);
    }

    private async Task<List<MediaObservationCandidate>> FindByCatalogTitleAsync(
        MediaObservation observation,
        CancellationToken cancellationToken)
    {
        var queryTitles = GetCandidateQueryTitles(observation);
        if (queryTitles.Count == 0)
        {
            return [];
        }

        // Simple substring pre-filter at the database level, then score in-memory.
        // For MVP this is acceptable; a full-text index can replace this later.
        var leadWords = queryTitles
            .Select(title => NormalizeTitle(title).Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
            .Where(word => !string.IsNullOrWhiteSpace(word))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var candidates = new List<MediaTitle>();
        foreach (var leadWord in leadWords)
        {
            candidates.AddRange(await _dbContext.MediaTitles
                .Where(t =>
                    EF.Functions.Like(t.CanonicalTitle.ToLower(), $"%{leadWord}%") ||
                    (t.OriginalTitle != null && EF.Functions.Like(t.OriginalTitle.ToLower(), $"%{leadWord}%")))
                .Take(50)
                .ToListAsync(cancellationToken));
        }

        var distinctCandidates = candidates.DistinctBy(title => title.Id).ToList();
        var ordinalSeasonTargetId = await FindOrdinalSeasonTargetAsync(
            distinctCandidates,
            ReadEpisodeEvidence(observation),
            cancellationToken);

        return ScoreTitleCandidates(
            observation,
            distinctCandidates,
            queryTitles,
            MediaObservationCandidateSources.CatalogTitleSearch,
            ordinalSeasonTargetId);
    }

    private static List<MediaObservationCandidate> ScoreTitleCandidates(
        MediaObservation observation,
        IEnumerable<MediaTitle> titles,
        IReadOnlyList<string> queryTitles,
        string source,
        Guid? ordinalSeasonTargetId)
    {
        var result = new List<MediaObservationCandidate>();
        var candidateTitles = titles.DistinctBy(title => title.Id).ToList();
        var normalizedQueries = queryTitles
            .Select(NormalizeTitle)
            .Where(query => !string.IsNullOrWhiteSpace(query))
            .ToList();
        var catalogEvidence = ReadEpisodeEvidence(observation);
        foreach (var title in candidateTitles)
        {
            if (title.Id != ordinalSeasonTargetId
                && catalogEvidence?.HighestEpisodeNumber is int highestEpisodeNumber
                && title.EpisodeCount is int episodeCount
                && episodeCount < highestEpisodeNumber)
            {
                // A rendered provider episode is direct evidence that this
                // observation cannot belong to a shorter AniList split title.
                continue;
            }

            var score = normalizedQueries
                .Select(query => ComputeTitleScore(query, title))
                .DefaultIfEmpty(0m)
                .Max();

            if (ordinalSeasonTargetId is not null)
            {
                // The provider's explicit season ordinal is stronger than the
                // aggregate series title, which otherwise makes season one an
                // exact-title winner for every Crunchyroll season.
                score = title.Id == ordinalSeasonTargetId
                    ? 0.98m
                    : Math.Min(score, 0.94m);
            }

            if (catalogEvidence?.HighestEpisodeNumber is int observedEpisodeNumber
                && title.EpisodeCount is int candidateEpisodeCount
                && candidateEpisodeCount >= observedEpisodeNumber
                && ordinalSeasonTargetId is null
                && score >= LowConfidenceThreshold)
            {
                // Crunchyroll commonly groups several seasons under one series
                // title while AniList stores each cour/season separately. Once
                // the observed episode number rules out the exact base-title
                // match, a related title that can actually contain the episode
                // is materially stronger than title similarity alone.
                score = Math.Max(score, Math.Min(0.94m, score + 0.45m));
            }

            if (score < 0.01m)
            {
                continue;
            }

            result.Add(new MediaObservationCandidate
            {
                Id = Guid.NewGuid(),
                MediaObservationId = observation.Id,
                CandidateSource = source,
                MediaTitleId = title.Id,
                Title = title.CanonicalTitle,
                MediaKind = title.MediaKind,
                Score = score,
                Explanation = $"Title similarity ({source}): {score:P0}",
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        return result;
    }

    private static List<string> GetCandidateQueryTitles(MediaObservation observation)
    {
        var titles = new List<string>();
        AddTitle(titles, TryReadRawSeriesTitle(observation.RawPayload));
        AddTitle(titles, observation.ObservedTitle);
        return titles;
    }

    private static string? TryReadRawSeriesTitle(string? rawPayload)
    {
        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<ObservationRawPayload>(rawPayload, RawPayloadJsonOptions);
            return payload?.SeriesTitle;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static CatalogEvidence? ReadEpisodeEvidence(MediaObservation observation)
    {
        if (string.IsNullOrWhiteSpace(observation.RawPayload)
            || !string.Equals(
                observation.SiteIdentifier,
                MediaObservationSiteIdentifiers.Crunchyroll,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<ObservationRawPayload>(
                observation.RawPayload,
                RawPayloadJsonOptions);
            var episodeNumbers = payload?.Episodes?
                .Where(episode => episode.EpisodeNumber > 0)
                .Select(episode => episode.EpisodeNumber)
                .ToList() ?? [];
            if (payload?.EpisodeNumber is > 0)
            {
                episodeNumbers.Add(payload.EpisodeNumber.Value);
            }

            var lowestEpisodeNumber = episodeNumbers.Count > 0 ? episodeNumbers.Min() : (int?)null;
            var highestEpisodeNumber = episodeNumbers.Count > 0 ? episodeNumbers.Max() : (int?)null;
            return highestEpisodeNumber > 0 || payload?.SeasonNumber is > 0
                ? new CatalogEvidence(payload?.SeasonNumber, lowestEpisodeNumber, highestEpisodeNumber)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<int?> InferCumulativeEpisodeOffsetAsync(
        MediaObservation observation,
        Guid matchedMediaTitleId,
        CancellationToken cancellationToken)
    {
        var evidence = ReadEpisodeEvidence(observation);
        if (evidence?.SeasonNumber is not > 1
            || evidence.LowestEpisodeNumber is not > 0
            || evidence.HighestEpisodeNumber is not > 0)
        {
            return null;
        }

        var continuity = await LoadContinuityAsync([matchedMediaTitleId], cancellationToken);
        if (!continuity.IsComplete
            || !continuity.OrderedTitleIds.Contains(matchedMediaTitleId)
            || !continuity.Titles.TryGetValue(matchedMediaTitleId, out var matchedTitle)
            || matchedTitle.EpisodeCount is not > 0)
        {
            return null;
        }

        var seasonIndex = evidence.SeasonNumber.Value - 1;
        if (seasonIndex >= continuity.OrderedTitleIds.Count
            || continuity.OrderedTitleIds[seasonIndex] != matchedMediaTitleId)
        {
            return null;
        }

        var previousEpisodeCounts = continuity.OrderedTitleIds
            .Take(seasonIndex)
            .Select(titleId => continuity.Titles[titleId].EpisodeCount)
            .ToList();
        if (previousEpisodeCounts.Any(count => count is not > 0))
        {
            return null;
        }

        var precedingEpisodeCount = previousEpisodeCounts.Sum(count => count!.Value);
        var targetEpisodeCount = matchedTitle.EpisodeCount.Value;
        if (evidence.HighestEpisodeNumber <= targetEpisodeCount)
        {
            return 0;
        }

        var localLowest = evidence.LowestEpisodeNumber.Value - precedingEpisodeCount;
        var localHighest = evidence.HighestEpisodeNumber.Value - precedingEpisodeCount;
        return localLowest > 0 && localHighest <= targetEpisodeCount
            ? -precedingEpisodeCount
            : null;
    }

    private async Task<Guid?> FindOrdinalSeasonTargetAsync(
        IReadOnlyCollection<MediaTitle> titles,
        CatalogEvidence? evidence,
        CancellationToken cancellationToken)
    {
        if (evidence?.SeasonNumber is not > 0 || titles.Count == 0)
        {
            return null;
        }

        var candidateIds = titles.Select(title => title.Id).ToHashSet();
        var seasonIndex = evidence.SeasonNumber.Value - 1;
        var targets = new HashSet<Guid>();
        foreach (var candidateId in candidateIds)
        {
            var continuity = await LoadContinuityAsync([candidateId], cancellationToken);
            if (continuity.IsComplete && seasonIndex < continuity.OrderedTitleIds.Count)
            {
                var target = continuity.OrderedTitleIds[seasonIndex];
                if (candidateIds.Contains(target))
                {
                    targets.Add(target);
                }
            }
        }

        return targets.Count == 1 ? targets.Single() : null;
    }

    private async Task<ContinuityComponent> LoadContinuityAsync(
        IReadOnlyCollection<Guid> seedTitleIds,
        CancellationToken cancellationToken)
    {
        const int maxTitles = 50;
        var titleIds = seedTitleIds.Take(maxTitles).ToHashSet();
        var frontier = titleIds.ToHashSet();
        var relationsById = new Dictionary<Guid, MediaTitleRelation>();

        while (frontier.Count > 0 && titleIds.Count < maxTitles)
        {
            var frontierIds = frontier.ToArray();
            var relations = await _dbContext.MediaTitleRelations
                .AsNoTracking()
                .Where(relation => relation.SourceProvider == "anilist"
                    && (relation.RelationType == MediaRelationTypes.Prequel
                        || relation.RelationType == MediaRelationTypes.Sequel)
                    && (frontierIds.Contains(relation.MediaTitleId)
                        || frontierIds.Contains(relation.RelatedMediaTitleId)))
                .ToListAsync(cancellationToken);
            frontier.Clear();
            foreach (var relation in relations)
            {
                relationsById[relation.Id] = relation;
                if (titleIds.Count < maxTitles && titleIds.Add(relation.MediaTitleId))
                {
                    frontier.Add(relation.MediaTitleId);
                }
                if (titleIds.Count < maxTitles && titleIds.Add(relation.RelatedMediaTitleId))
                {
                    frontier.Add(relation.RelatedMediaTitleId);
                }
            }
        }

        var titles = await _dbContext.MediaTitles
            .AsNoTracking()
            .Where(title => titleIds.Contains(title.Id))
            .ToDictionaryAsync(title => title.Id, cancellationToken);
        var edges = relationsById.Values
            .Where(relation => IsTvAnime(titles.GetValueOrDefault(relation.MediaTitleId))
                && IsTvAnime(titles.GetValueOrDefault(relation.RelatedMediaTitleId)))
            .Select(relation => relation.RelationType == MediaRelationTypes.Prequel
                ? (Earlier: relation.RelatedMediaTitleId, Later: relation.MediaTitleId)
                : (Earlier: relation.MediaTitleId, Later: relation.RelatedMediaTitleId))
            .Distinct()
            .ToList();
        if (edges.Count == 0)
        {
            return new ContinuityComponent(
                seedTitleIds.Count == 1 ? [seedTitleIds.First()] : [],
                titles,
                seedTitleIds.Count == 1);
        }

        var previousByNode = edges.GroupBy(edge => edge.Later)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.Earlier).Distinct().ToList());
        var nextByNode = edges.GroupBy(edge => edge.Earlier)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.Later).Distinct().ToList());
        if (previousByNode.Values.Any(values => values.Count != 1)
            || nextByNode.Values.Any(values => values.Count != 1))
        {
            return new ContinuityComponent([], titles, false);
        }

        var allEdgeTitleIds = edges.SelectMany(edge => new[] { edge.Earlier, edge.Later }).ToHashSet();
        var starts = allEdgeTitleIds.Where(id => !previousByNode.ContainsKey(id)).ToList();
        if (starts.Count != 1)
        {
            return new ContinuityComponent([], titles, false);
        }

        var ordered = new List<Guid>();
        var seen = new HashSet<Guid>();
        var current = starts[0];
        while (seen.Add(current))
        {
            ordered.Add(current);
            if (!nextByNode.TryGetValue(current, out var next))
            {
                break;
            }
            current = next[0];
        }

        var complete = seen.Count == allEdgeTitleIds.Count && titleIds.Count < maxTitles;
        return new ContinuityComponent(ordered, titles, complete);
    }

    private static bool IsTvAnime(MediaTitle? title)
        => title is not null
            && string.Equals(title.MediaKind, MediaKinds.Anime, StringComparison.Ordinal)
            && string.Equals(title.Format, "TV", StringComparison.OrdinalIgnoreCase);

    private static void AddTitle(List<string> titles, string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        var normalized = title.Trim();
        if (!titles.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            titles.Add(normalized);
        }
    }

    private static decimal ComputeTitleScore(string observedNorm, MediaTitle title)
    {
        var canonicalNorm = NormalizeTitle(title.CanonicalTitle);
        var originalNorm = title.OriginalTitle is not null
            ? NormalizeTitle(title.OriginalTitle)
            : null;

        var bestScore = JaccardWordSimilarity(observedNorm, canonicalNorm);

        if (originalNorm is not null)
        {
            var origScore = JaccardWordSimilarity(observedNorm, originalNorm);
            if (origScore > bestScore)
            {
                bestScore = origScore;
            }
        }

        // Exact match bonus.
        if (string.Equals(observedNorm, canonicalNorm, StringComparison.Ordinal) ||
            (originalNorm is not null && string.Equals(observedNorm, originalNorm, StringComparison.Ordinal)))
        {
            bestScore = Math.Min(1.0m, bestScore + 0.05m);
        }

        return Math.Round(bestScore, 4);
    }

    /// <summary>
    /// Jaccard similarity on word sets: |A ∩ B| / |A ∪ B|.
    /// </summary>
    private static decimal JaccardWordSimilarity(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return 0m;
        }

        var setA = new HashSet<string>(a.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
        var setB = new HashSet<string>(b.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);

        var intersection = setA.Intersect(setB).Count();
        var union = setA.Union(setB).Count();

        return union == 0 ? 0m : (decimal)intersection / union;
    }

    private static string NormalizeTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        // Lowercase, collapse whitespace, strip punctuation commonly used in
        // anime/manga titles to improve matching across romanization variants.
        var chars = title.ToLowerInvariant()
            .Replace(":", " ", StringComparison.Ordinal)
            .Replace(".", " ", StringComparison.Ordinal)
            .Replace("-", " ", StringComparison.Ordinal)
            .Replace("'", string.Empty, StringComparison.Ordinal)
            .Replace("\"", string.Empty, StringComparison.Ordinal);

        return string.Join(' ', chars.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static void MergeCandidates(
        List<MediaObservationCandidate> existing,
        List<MediaObservationCandidate> incoming)
    {
        var existingTitleIds = existing
            .Select(c => c.MediaTitleId)
            .ToHashSet();

        foreach (var candidate in incoming)
        {
            if (!existingTitleIds.Contains(candidate.MediaTitleId))
            {
                existing.Add(candidate);
                existingTitleIds.Add(candidate.MediaTitleId);
            }
        }
    }

    private static void ApplyMatchDecision(
        MediaObservation observation,
        List<MediaObservationCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            observation.MatchStatus = MediaObservationStatuses.NoMatch;
            observation.ResolutionNotes = "No candidates found.";
            observation.MediaTitleId = null;
            observation.AcceptedCandidateId = null;
            return;
        }

        var top = candidates[0];

        if (top.Score >= HighConfidenceThreshold)
        {
            // High-confidence single match: auto-accept.
            top.IsAccepted = true;
            observation.MatchStatus = MediaObservationStatuses.Matched;
            observation.MediaTitleId = top.MediaTitleId;
            observation.AcceptedCandidateId = top.Id;
            observation.ResolutionNotes = $"Automatically matched (score {top.Score:P0}): {top.Explanation}";
            return;
        }

        if (candidates.Count > 1)
        {
            observation.MatchStatus = MediaObservationStatuses.Ambiguous;
            observation.ResolutionNotes = $"Multiple candidates; top score {top.Score:P0}. Review required.";
        }
        else
        {
            observation.MatchStatus = MediaObservationStatuses.Ambiguous;
            observation.ResolutionNotes = $"Single low-confidence candidate (score {top.Score:P0}). Review required.";
        }

        observation.MediaTitleId = null;
        observation.AcceptedCandidateId = null;
    }

    private static string? MapSiteToProvider(string siteIdentifier)
    {
        return siteIdentifier.ToLowerInvariant() switch
        {
            MediaObservationSiteIdentifiers.AniList => "anilist",
            MediaObservationSiteIdentifiers.MyAnimeList => "myanimelist",
            _ => null
        };
    }

    private sealed class ObservationRawPayload
    {
        public string? SeriesTitle { get; set; }

        public string? ProviderSeriesId { get; set; }

        public int? SeasonNumber { get; set; }

        public int? EpisodeNumber { get; set; }

        public List<ObservationRawEpisode>? Episodes { get; set; }
    }

    private sealed class ObservationRawEpisode
    {
        public int EpisodeNumber { get; set; }
    }

    private sealed record CatalogEvidence(
        int? SeasonNumber,
        int? LowestEpisodeNumber,
        int? HighestEpisodeNumber);

    private sealed record ContinuityComponent(
        IReadOnlyList<Guid> OrderedTitleIds,
        IReadOnlyDictionary<Guid, MediaTitle> Titles,
        bool IsComplete);
}
