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
    MediaProviderSeasonMappingService seasonMappingService,
    MediaRelationGraphRefreshQueue relationGraphRefreshQueue,
    ILogger<MediaObservationMatchingService> logger)
{
    private const decimal HighConfidenceThreshold = 0.85m;
    private const decimal LowConfidenceThreshold = 0.40m;

    private static readonly JsonSerializerOptions RawPayloadJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly MediaProviderSeasonMappingService _seasonMappingService = seasonMappingService;
    private readonly MediaRelationGraphRefreshQueue _relationGraphRefreshQueue = relationGraphRefreshQueue;
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
        observation.EpisodeOffset = null;
        observation.ResolvedProgress = null;

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
                    if (observation.EpisodeOffset is null)
                    {
                        observation.EpisodeOffset = await InferCumulativeEpisodeOffsetAsync(
                            observation,
                            matchedMediaTitleId,
                            cancellationToken);
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
        if (identity is null || !identity.IsTrusted || episode is null || title is null
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

        // --- Strategy 1: an established provider season/container mapping ---
        var mappedCandidates = await FindByProviderSeasonMappingAsync(observation, cancellationToken);
        candidates.AddRange(mappedCandidates);
        if (mappedCandidates.Count > 0)
        {
            return mappedCandidates;
        }

        // --- Strategy 2: exact provider link lookup via SiteMediaId ---
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

        // --- Strategy 3: fuzzy title search against user library entries ---
        var libraryCandidates = await FindByUserLibraryTitleAsync(
            observation, cancellationToken);
        MergeCandidates(candidates, libraryCandidates);

        // --- Strategy 4: fuzzy title search against full catalog ---
        var catalogCandidates = await FindByCatalogTitleAsync(
            observation, cancellationToken);
        MergeCandidates(candidates, catalogCandidates);

        return [.. candidates
            .Where(c => c.Score >= LowConfidenceThreshold)
            .OrderByDescending(c => c.Score)
            .Take(10)];
    }

    private async Task<List<MediaObservationCandidate>> FindByProviderSeasonMappingAsync(
        MediaObservation observation,
        CancellationToken cancellationToken)
    {
        var payload = DeserializeRawPayload(observation.RawPayload);
        if (payload is null)
        {
            return [];
        }

        var mapping = await _seasonMappingService.FindAsync(
            observation.SiteIdentifier,
            payload.ProviderSeriesId,
            payload.ProviderSeasonId,
            payload.SeasonNumber,
            cancellationToken);
        if (mapping?.MediaTitle is null)
        {
            return [];
        }

        observation.EpisodeOffset = mapping.EpisodeOffset;
        return
        [
            new MediaObservationCandidate
            {
                Id = Guid.NewGuid(),
                MediaObservationId = observation.Id,
                CandidateSource = MediaObservationCandidateSources.ProviderSeasonMappingExact,
                MediaTitleId = mapping.MediaTitleId,
                Title = mapping.MediaTitle.CanonicalTitle,
                MediaKind = mapping.MediaTitle.MediaKind,
                Score = 1m,
                Explanation = $"Established provider season mapping: {mapping.Provider}/{mapping.ProviderSeriesId}",
                CreatedAt = DateTimeOffset.UtcNow
            }
        ];
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

        var providerMediaIds = GetProviderMediaIdCandidates(provider, siteMediaId, observation.ObservedUrl);
        var links = await _dbContext.MediaProviderLinks
            .Include(l => l.MediaTitle)
            .Where(link => link.Provider == provider && providerMediaIds.Contains(link.ExternalId))
            .ToListAsync(cancellationToken);

        return links
            .Where(link => link.MediaTitle is not null)
            .Select(link =>
            {
                _logger.LogDebug(
                    "Exact provider link match for observation {ObservationId}: {Provider}/{ProviderMediaId} → MediaTitle {MediaTitleId}.",
                    observation.Id,
                    provider,
                    link.ExternalId,
                    link.MediaTitleId);
                return new MediaObservationCandidate
                {
                    Id = Guid.NewGuid(),
                    MediaObservationId = observation.Id,
                    CandidateSource = MediaObservationCandidateSources.ProviderLinkExact,
                    MediaTitleId = link.MediaTitleId,
                    Provider = provider,
                    ProviderMediaId = link.ExternalId,
                    Title = link.MediaTitle!.CanonicalTitle,
                    MediaKind = link.MediaTitle.MediaKind,
                    Score = 0.97m,
                    Explanation = $"Exact provider link match: {provider}/{link.ExternalId}",
                    CreatedAt = DateTimeOffset.UtcNow
                };
            })
            .ToList();
    }

    internal static IReadOnlyList<string> GetProviderMediaIdCandidates(
        string provider,
        string siteMediaId,
        string observedUrl)
    {
        var normalizedId = siteMediaId.Trim();
        if (provider != MediaObservationSiteIdentifiers.MyAnimeList)
        {
            return [normalizedId];
        }

        if (normalizedId.StartsWith("anime:", StringComparison.OrdinalIgnoreCase))
        {
            return [$"anime:{normalizedId[6..]}" ];
        }

        if (normalizedId.StartsWith("manga:", StringComparison.OrdinalIgnoreCase))
        {
            return [$"manga:{normalizedId[6..]}" ];
        }

        if (Uri.TryCreate(observedUrl, UriKind.Absolute, out var uri))
        {
            var firstSegment = uri.AbsolutePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();
            if (firstSegment is "anime" or "manga")
            {
                return [$"{firstSegment}:{normalizedId}"];
            }
        }

        // A bare MAL number is ambiguous because anime and manga use separate ID
        // namespaces. Query both; equal exact matches remain ambiguous for review.
        return [$"anime:{normalizedId}", $"manga:{normalizedId}"];
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
        libraryTitles = libraryTitles
            .Where(title => IsCompatibleWithObservationSource(observation, title))
            .Select(title => new
            {
                Title = title,
                Score = queryTitles.Select(NormalizeTitle)
                    .Select(query => ComputeTitleScore(query, title))
                    .DefaultIfEmpty(0m)
                    .Max()
            })
            .Where(item => item.Score >= LowConfidenceThreshold)
            .OrderByDescending(item => item.Score)
            .Take(50)
            .Select(item => item.Title)
            .ToList();

        return await ScoreTitleCandidatesAsync(
            observation,
            libraryTitles,
            queryTitles,
            MediaObservationCandidateSources.LibraryTitleSearch,
            cancellationToken);
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
                    (t.OriginalTitle != null && EF.Functions.Like(t.OriginalTitle.ToLower(), $"%{leadWord}%")) ||
                    t.Synonyms.Any(synonym => EF.Functions.Like(synonym.ToLower(), $"%{leadWord}%")))
                .Take(50)
                .ToListAsync(cancellationToken));
        }

        var distinctCandidates = candidates
            .DistinctBy(title => title.Id)
            .Where(title => IsCompatibleWithObservationSource(observation, title))
            .ToList();
        var anchorIds = distinctCandidates
            .Select(title => new
            {
                title.Id,
                Score = queryTitles.Select(NormalizeTitle)
                    .Select(query => ComputeTitleScore(query, title))
                    .DefaultIfEmpty(0m)
                    .Max()
            })
            .Where(item => item.Score >= LowConfidenceThreshold)
            .OrderByDescending(item => item.Score)
            .Take(5)
            .Select(item => item.Id)
            .ToList();
        if (anchorIds.Count > 0)
        {
            var relatedComponents = await LoadContinuitiesAsync(anchorIds, cancellationToken);
            distinctCandidates = distinctCandidates
                .Concat(relatedComponents.Values.SelectMany(component => component.Titles.Values))
                .DistinctBy(title => title.Id)
                .Take(50)
                .ToList();
        }
        return await ScoreTitleCandidatesAsync(
            observation,
            distinctCandidates,
            queryTitles,
            MediaObservationCandidateSources.CatalogTitleSearch,
            cancellationToken);
    }

    private async Task<List<MediaObservationCandidate>> ScoreTitleCandidatesAsync(
        MediaObservation observation,
        IEnumerable<MediaTitle> titles,
        IReadOnlyList<string> queryTitles,
        string source,
        CancellationToken cancellationToken)
    {
        var result = new List<MediaObservationCandidate>();
        var candidateTitles = titles.DistinctBy(title => title.Id).ToList();
        var normalizedQueries = queryTitles
            .Select(NormalizeTitle)
            .Where(query => !string.IsNullOrWhiteSpace(query))
            .ToList();
        var catalogEvidence = ReadEpisodeEvidence(observation);
        var rawPayload = DeserializeRawPayload(observation.RawPayload);
        var hasProviderSeasonContext = !string.IsNullOrWhiteSpace(rawPayload?.ProviderSeasonId)
            || rawPayload?.SeasonNumber is > 0;
        var normalizedObservedTitle = NormalizeTitle(observation.ObservedTitle);
        var normalizedSeriesTitle = NormalizeTitle(rawPayload?.SeriesTitle ?? string.Empty);
        var hasSeasonSpecificObservedTitle = hasProviderSeasonContext
            && !string.IsNullOrWhiteSpace(normalizedObservedTitle)
            && (string.IsNullOrWhiteSpace(normalizedSeriesTitle)
                ? HasExplicitSeasonQualifier(normalizedObservedTitle)
                : !string.Equals(normalizedObservedTitle, normalizedSeriesTitle, StringComparison.Ordinal));
        var strongSeasonTitleIds = candidateTitles
            .Where(title => IsCompatibleWithObservationSource(observation, title))
            .Where(title => HasStrongSeasonTitleEvidence(
                hasProviderSeasonContext,
                hasSeasonSpecificObservedTitle,
                rawPayload,
                normalizedObservedTitle,
                normalizedSeriesTitle,
                title,
                catalogEvidence))
            .Select(title => title.Id)
            .ToHashSet();
        var candidateOffsets = await FindCandidateEpisodeOffsetsAsync(
            observation.UserId,
            candidateTitles.Select(title => title.Id).ToList(),
            catalogEvidence,
            cancellationToken);
        var ambiguousContinuityTitleIds = candidateOffsets
            .Where(item => item.Value.IsAmbiguous)
            .Select(item => item.Key)
            .ToHashSet();
        foreach (var title in candidateTitles)
        {
            if (!IsCompatibleWithObservationSource(observation, title))
            {
                continue;
            }

            if (catalogEvidence?.HighestEpisodeNumber is > 0
                && title.EpisodeCount is > 0
                && !candidateOffsets.ContainsKey(title.Id))
            {
                continue;
            }

            var score = normalizedQueries
                .Select(query => ComputeTitleScore(query, title))
                .DefaultIfEmpty(0m)
                .Max();
            var hasStrongSeasonTitleEvidence = strongSeasonTitleIds.Contains(title.Id);

            if (hasStrongSeasonTitleEvidence)
            {
                // Strong evidence is an exact provider-to-catalog equivalence,
                // even when their surface forms differ (for example,
                // "Dr. STONE Season 2" and the AniList synonym "Dr. STONE 2").
                score = Math.Max(score, 0.95m);
            }

            if (catalogEvidence?.HighestEpisodeNumber is > 0
                && candidateOffsets.TryGetValue(title.Id, out var offsetEvidence)
                && offsetEvidence.EpisodeOffset < 0
                && score >= LowConfidenceThreshold)
            {
                score = Math.Max(score, Math.Min(0.94m, score + 0.45m));
            }

            if (ambiguousContinuityTitleIds.Contains(title.Id)
                && !hasStrongSeasonTitleEvidence)
            {
                // Title text may discover a franchise candidate, but it cannot
                // establish which provider season maps to which graph node.
                score = Math.Min(score, HighConfidenceThreshold - 0.01m);
            }

            if (hasProviderSeasonContext
                && catalogEvidence?.HighestEpisodeNumber is > 0
                && title.EpisodeCount is null
                && !hasStrongSeasonTitleEvidence)
            {
                // Unknown bounds cannot prove that provider episode numbers map
                // into this canonical title, so do not establish a mapping yet.
                score = Math.Min(score, HighConfidenceThreshold - 0.01m);
            }

            if (hasProviderSeasonContext
                && strongSeasonTitleIds.Count > 0
                && !hasStrongSeasonTitleEvidence)
            {
                // A provider's base series title is useful for discovering the
                // franchise, but it must not outrank the season-specific title.
                // Season ordinals alone are not stable enough to identify a
                // canonical graph node.
                score = Math.Min(score, HighConfidenceThreshold - 0.01m);
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
                Explanation = candidateOffsets.TryGetValue(title.Id, out var episodeEvidence)
                    ? $"Title and episode-range evidence ({source}, offset {episodeEvidence.EpisodeOffset}): {score:P0}"
                    : $"Title similarity ({source}): {score:P0}",
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
            var episodeNumbers = (payload?.ObservedEpisodes ?? payload?.Episodes)?
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
        if (evidence?.LowestEpisodeNumber is not > 0
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

        var matchedIndex = continuity.OrderedTitleIds.ToList().IndexOf(matchedMediaTitleId);
        var previousEpisodeCounts = continuity.OrderedTitleIds
            .Take(matchedIndex)
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

    private async Task<Dictionary<Guid, CandidateOffsetEvidence>> FindCandidateEpisodeOffsetsAsync(
        int userId,
        IReadOnlyCollection<Guid> candidateTitleIds,
        CatalogEvidence? evidence,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, CandidateOffsetEvidence>();
        if (evidence?.LowestEpisodeNumber is not > 0
            || evidence.HighestEpisodeNumber is not > 0
            || candidateTitleIds.Count == 0)
        {
            return result;
        }

        await QueueMissingRelationGraphsAsync(userId, candidateTitleIds, cancellationToken);
        var continuities = await LoadContinuitiesAsync(candidateTitleIds, cancellationToken);
        foreach (var candidateId in candidateTitleIds)
        {
            if (!continuities.TryGetValue(candidateId, out var continuity)
                || !continuity.Titles.TryGetValue(candidateId, out var title)
                || title.EpisodeCount is not > 0)
            {
                continue;
            }

            var directFits = evidence.LowestEpisodeNumber.Value > 0
                && evidence.HighestEpisodeNumber.Value <= title.EpisodeCount.Value;
            var offset = directFits
                ? 0
                : TryGetCumulativeOffset(continuity, candidateId, evidence);
            if (offset is null)
            {
                continue;
            }

            var viableInComponent = continuity.Titles.Keys.Count(titleId =>
                continuity.Titles[titleId].EpisodeCount is > 0
                && evidence.HighestEpisodeNumber.Value <= continuity.Titles[titleId].EpisodeCount!.Value);
            result[candidateId] = new CandidateOffsetEvidence(
                offset.Value,
                !continuity.IsComplete || (directFits && viableInComponent > 1));
        }

        return result;
    }

    private async Task QueueMissingRelationGraphsAsync(
        int userId,
        IReadOnlyCollection<Guid> mediaTitleIds,
        CancellationToken cancellationToken)
    {
        var missingLinks = await _dbContext.MediaProviderLinks
            .AsNoTracking()
            .Where(link => mediaTitleIds.Contains(link.MediaTitleId)
                && link.Provider == "anilist"
                && link.RelationsLastVerifiedAt == null)
            .Select(link => link.ExternalId)
            .ToListAsync(cancellationToken);
        foreach (var providerMediaId in missingLinks)
        {
            _relationGraphRefreshQueue.Enqueue(userId, "anilist", providerMediaId);
        }
    }

    private static int? TryGetCumulativeOffset(
        ContinuityComponent continuity,
        Guid candidateId,
        CatalogEvidence evidence)
    {
        if (!continuity.IsComplete)
        {
            return null;
        }

        var index = continuity.OrderedTitleIds.ToList().IndexOf(candidateId);
        if (index < 0)
        {
            return null;
        }

        var precedingCounts = continuity.OrderedTitleIds
            .Take(index)
            .Select(titleId => continuity.Titles[titleId].EpisodeCount)
            .ToList();
        if (precedingCounts.Any(count => count is not > 0))
        {
            return null;
        }

        var precedingCount = precedingCounts.Sum(count => count!.Value);
        var localLowest = evidence.LowestEpisodeNumber!.Value - precedingCount;
        var localHighest = evidence.HighestEpisodeNumber!.Value - precedingCount;
        var candidateCount = continuity.Titles[candidateId].EpisodeCount!.Value;
        return localLowest > 0 && localHighest <= candidateCount ? -precedingCount : null;
    }

    private async Task<ContinuityComponent> LoadContinuityAsync(
        IReadOnlyCollection<Guid> seedTitleIds,
        CancellationToken cancellationToken)
    {
        var components = await LoadContinuitiesAsync(seedTitleIds, cancellationToken);
        return seedTitleIds.Select(id => components.GetValueOrDefault(id)).FirstOrDefault(component => component is not null)
            ?? new ContinuityComponent([], new Dictionary<Guid, MediaTitle>(), false);
    }

    private async Task<Dictionary<Guid, ContinuityComponent>> LoadContinuitiesAsync(
        IReadOnlyCollection<Guid> seedTitleIds,
        CancellationToken cancellationToken)
    {
        var maxTitles = Math.Min(100, Math.Max(50, seedTitleIds.Count + 50));
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
        var aniListLinks = await _dbContext.MediaProviderLinks
            .AsNoTracking()
            .Where(link => titleIds.Contains(link.MediaTitleId)
                && link.Provider == "anilist")
            .Select(link => new
            {
                link.MediaTitleId,
                link.RelationsLastVerifiedAt,
                link.RelationsSnapshotId
            })
            .ToListAsync(cancellationToken);
        var linkedTitleIds = aniListLinks.Select(link => link.MediaTitleId).ToHashSet();
        var edges = relationsById.Values
            .Where(relation => IsTvAnime(titles.GetValueOrDefault(relation.MediaTitleId))
                && IsTvAnime(titles.GetValueOrDefault(relation.RelatedMediaTitleId)))
            .Select(relation => relation.RelationType == MediaRelationTypes.Prequel
                ? (Earlier: relation.RelatedMediaTitleId, Later: relation.MediaTitleId)
                : (Earlier: relation.MediaTitleId, Later: relation.RelatedMediaTitleId))
            .Distinct()
            .ToList();
        var result = new Dictionary<Guid, ContinuityComponent>();
        var remaining = titleIds.ToHashSet();
        while (remaining.Count > 0)
        {
            var componentIds = new HashSet<Guid> { remaining.First() };
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var edge in edges)
                {
                    if ((componentIds.Contains(edge.Earlier) && componentIds.Add(edge.Later))
                        || (componentIds.Contains(edge.Later) && componentIds.Add(edge.Earlier)))
                    {
                        changed = true;
                    }
                }
            }
            remaining.ExceptWith(componentIds);
            var componentEdges = edges
                .Where(edge => componentIds.Contains(edge.Earlier) && componentIds.Contains(edge.Later))
                .ToList();
            var componentTitles = titles
                .Where(item => componentIds.Contains(item.Key))
                .ToDictionary(item => item.Key, item => item.Value);
            var componentSnapshotIds = aniListLinks
                .Where(link => componentIds.Contains(link.MediaTitleId)
                    && link.RelationsLastVerifiedAt is not null
                    && link.RelationsSnapshotId is not null)
                .Select(link => link.RelationsSnapshotId!.Value)
                .Distinct()
                .ToList();
            var sourcesShareSnapshot = componentSnapshotIds.Count == 1
                && componentIds.All(id => !linkedTitleIds.Contains(id)
                    || aniListLinks.Any(link => link.MediaTitleId == id
                        && link.RelationsSnapshotId == componentSnapshotIds[0]));
            var component = BuildContinuityComponent(
                componentIds,
                componentEdges,
                componentTitles,
                titleIds.Count < maxTitles && sourcesShareSnapshot);
            foreach (var id in componentIds)
            {
                result[id] = component;
            }
        }

        return result;
    }

    private static ContinuityComponent BuildContinuityComponent(
        IReadOnlyCollection<Guid> componentIds,
        IReadOnlyCollection<(Guid Earlier, Guid Later)> edges,
        IReadOnlyDictionary<Guid, MediaTitle> titles,
        bool sourcesVerified)
    {
        if (edges.Count == 0)
        {
            return new ContinuityComponent([componentIds.Single()], titles, true);
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

        var complete = seen.Count == allEdgeTitleIds.Count && sourcesVerified;
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
        var normalizedTitles = GetNormalizedTitles(title);
        var bestScore = normalizedTitles
            .Select(candidate => JaccardWordSimilarity(observedNorm, candidate))
            .DefaultIfEmpty(0m)
            .Max();

        // Exact match bonus.
        if (normalizedTitles.Contains(observedNorm, StringComparer.Ordinal))
        {
            bestScore = Math.Min(1.0m, bestScore + 0.05m);
        }

        return Math.Round(bestScore, 4);
    }

    private static bool IsExactTitleMatch(string observedNorm, MediaTitle title)
        => GetNormalizedTitles(title).Contains(observedNorm, StringComparer.Ordinal);

    private static bool HasStrongSeasonTitleEvidence(
        bool hasProviderSeasonContext,
        bool hasSeasonSpecificObservedTitle,
        ObservationRawPayload? rawPayload,
        string normalizedObservedTitle,
        string normalizedSeriesTitle,
        MediaTitle title,
        CatalogEvidence? catalogEvidence)
        => hasProviderSeasonContext
            && ((hasSeasonSpecificObservedTitle && IsExactTitleMatch(normalizedObservedTitle, title))
                || IsFirstSeasonBaseTitleMatch(
                    rawPayload,
                    normalizedObservedTitle,
                    normalizedSeriesTitle,
                    title)
                || IsExplicitNumberedSeasonAliasMatch(
                    rawPayload,
                    normalizedObservedTitle,
                    normalizedSeriesTitle,
                    title)
                || IsEpisodeRangeSegmentMatch(
                    rawPayload,
                    normalizedObservedTitle,
                    normalizedSeriesTitle,
                    title,
                    catalogEvidence));

    private static bool IsFirstSeasonBaseTitleMatch(
        ObservationRawPayload? rawPayload,
        string normalizedObservedTitle,
        string normalizedSeriesTitle,
        MediaTitle title)
    {
        if (rawPayload?.SeasonNumber != 1
            || !IsTvAnime(title)
            || string.IsNullOrWhiteSpace(normalizedSeriesTitle)
            || !IsExactTitleMatch(normalizedSeriesTitle, title))
        {
            return false;
        }

        return TryRemoveSeasonOneSuffix(normalizedObservedTitle, out var observedBaseTitle)
            && string.Equals(observedBaseTitle, normalizedSeriesTitle, StringComparison.Ordinal);
    }

    private static bool HasExplicitSeasonQualifier(string normalizedTitle)
        => normalizedTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains("season", StringComparer.Ordinal);

    private static bool IsExplicitNumberedSeasonAliasMatch(
        ObservationRawPayload? rawPayload,
        string normalizedObservedTitle,
        string normalizedSeriesTitle,
        MediaTitle title)
    {
        if (rawPayload?.SeasonNumber is not > 1
            || !IsTvAnime(title)
            || string.IsNullOrWhiteSpace(normalizedSeriesTitle))
        {
            return false;
        }

        var seasonNumber = rawPayload.SeasonNumber.Value;
        var providerSeasonTitle = $"{normalizedSeriesTitle} season {seasonNumber}";
        if (!string.Equals(normalizedObservedTitle, providerSeasonTitle, StringComparison.Ordinal))
        {
            return false;
        }

        var numberedCatalogAlias = $"{normalizedSeriesTitle} {seasonNumber}";
        return GetNormalizedTitles(title).Contains(numberedCatalogAlias, StringComparer.Ordinal);
    }

    private static bool IsEpisodeRangeSegmentMatch(
        ObservationRawPayload? rawPayload,
        string normalizedObservedTitle,
        string normalizedSeriesTitle,
        MediaTitle title,
        CatalogEvidence? catalogEvidence)
    {
        if (string.IsNullOrWhiteSpace(rawPayload?.ProviderSeasonId)
            || string.IsNullOrWhiteSpace(rawPayload.SeasonTitle)
            || catalogEvidence?.LowestEpisodeNumber is not > 0
            || catalogEvidence.HighestEpisodeNumber is not > 0
            || !IsTvAnime(title)
            || string.IsNullOrWhiteSpace(normalizedSeriesTitle)
            || !IsExactTitleMatch(normalizedSeriesTitle, title))
        {
            return false;
        }

        var reconstructedObservedTitle = NormalizeTitle(
            $"{rawPayload.SeriesTitle} {rawPayload.SeasonTitle}");
        if (!string.Equals(normalizedObservedTitle, reconstructedObservedTitle, StringComparison.Ordinal)
            || !TryReadEpisodeRange(rawPayload.SeasonTitle, out var rangeStart, out var rangeEnd))
        {
            return false;
        }

        return catalogEvidence.LowestEpisodeNumber.Value >= rangeStart
            && (rangeEnd is null || catalogEvidence.HighestEpisodeNumber.Value <= rangeEnd.Value);
    }

    private static bool TryReadEpisodeRange(
        string seasonTitle,
        out int rangeStart,
        out int? rangeEnd)
    {
        rangeStart = 0;
        rangeEnd = null;

        var openParen = seasonTitle.LastIndexOf('(');
        var closeParen = seasonTitle.LastIndexOf(')');
        if (openParen < 0 || closeParen != seasonTitle.Length - 1 || closeParen <= openParen + 1)
        {
            return false;
        }

        var rangeParts = seasonTitle[(openParen + 1)..closeParen]
            .Split('-', 2, StringSplitOptions.TrimEntries);
        if (rangeParts.Length != 2
            || !int.TryParse(rangeParts[0], out rangeStart)
            || rangeStart <= 0)
        {
            return false;
        }

        if (string.Equals(rangeParts[1], "current", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!int.TryParse(rangeParts[1], out var parsedEnd) || parsedEnd < rangeStart)
        {
            rangeStart = 0;
            return false;
        }

        rangeEnd = parsedEnd;
        return true;
    }

    private static bool TryRemoveSeasonOneSuffix(string normalizedTitle, out string baseTitle)
    {
        const string seasonOneSuffix = " season 1";
        if (normalizedTitle.EndsWith(seasonOneSuffix, StringComparison.Ordinal))
        {
            baseTitle = normalizedTitle[..^seasonOneSuffix.Length].TrimEnd();
            return !string.IsNullOrWhiteSpace(baseTitle);
        }

        baseTitle = string.Empty;
        return false;
    }

    private static IReadOnlyList<string> GetNormalizedTitles(MediaTitle title)
    {
        var titles = new List<string>();
        AddTitle(titles, title.CanonicalTitle);
        AddTitle(titles, title.OriginalTitle);
        foreach (var synonym in title.Synonyms)
        {
            AddTitle(titles, synonym);
        }

        return titles.Select(NormalizeTitle).ToList();
    }

    private static bool IsCompatibleWithObservationSource(
        MediaObservation observation,
        MediaTitle title)
        => !MediaObservationSiteIdentifiers.IsStreamingService(observation.SiteIdentifier)
            || !(string.Equals(title.MediaKind, MediaKinds.Manga, StringComparison.OrdinalIgnoreCase)
                || string.Equals(title.Format, MediaFormats.Manga, StringComparison.OrdinalIgnoreCase)
                || string.Equals(title.Format, MediaFormats.Novel, StringComparison.OrdinalIgnoreCase)
                || string.Equals(title.Format, MediaFormats.OneShot, StringComparison.OrdinalIgnoreCase)
                || string.Equals(title.PrimaryProgressDimension, MediaProgressDimensions.Chapter, StringComparison.OrdinalIgnoreCase)
                || string.Equals(title.PrimaryProgressDimension, MediaProgressDimensions.Volume, StringComparison.OrdinalIgnoreCase));

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
            .Replace("—", " ", StringComparison.Ordinal)
            .Replace("–", " ", StringComparison.Ordinal)
            .Replace("'", string.Empty, StringComparison.Ordinal)
            .Replace("’", string.Empty, StringComparison.Ordinal)
            .Replace("‘", string.Empty, StringComparison.Ordinal)
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

        public string? ProviderSeasonId { get; set; }

        public int? SeasonNumber { get; set; }

        public string? SeasonTitle { get; set; }

        public int? EpisodeNumber { get; set; }

        public List<ObservationRawEpisode>? Episodes { get; set; }

        public List<ObservationRawEpisode>? ObservedEpisodes { get; set; }
    }

    private sealed class ObservationRawEpisode
    {
        public int EpisodeNumber { get; set; }
    }

    private sealed record CatalogEvidence(
        int? SeasonNumber,
        int? LowestEpisodeNumber,
        int? HighestEpisodeNumber);

    private sealed record CandidateOffsetEvidence(int EpisodeOffset, bool IsAmbiguous);

    private sealed record ContinuityComponent(
        IReadOnlyList<Guid> OrderedTitleIds,
        IReadOnlyDictionary<Guid, MediaTitle> Titles,
        bool IsComplete);
}
