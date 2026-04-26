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
            var candidates = await GenerateCandidatesAsync(observation, cancellationToken);

            // Remove stale candidates from previous runs.
            _dbContext.MediaObservationCandidates.RemoveRange(observation.Candidates);
            observation.Candidates.Clear();

            foreach (var candidate in candidates)
            {
                _dbContext.MediaObservationCandidates.Add(candidate);
            }

            ApplyMatchDecision(observation, candidates);
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
        if (string.IsNullOrWhiteSpace(observation.ObservedTitle))
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

        return ScoreTitleCandidates(
            observation,
            libraryTitles,
            MediaObservationCandidateSources.LibraryTitleSearch);
    }

    private async Task<List<MediaObservationCandidate>> FindByCatalogTitleAsync(
        MediaObservation observation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(observation.ObservedTitle))
        {
            return [];
        }

        var normalizedQuery = NormalizeTitle(observation.ObservedTitle);
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return [];
        }

        // Simple substring pre-filter at the database level, then score in-memory.
        // For MVP this is acceptable; a full-text index can replace this later.
        var queryWords = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (queryWords.Length == 0)
        {
            return [];
        }

        // Filter by the first meaningful word as a DB-level pre-filter.
        var leadWord = queryWords[0];
        var candidates = await _dbContext.MediaTitles
            .Where(t =>
                EF.Functions.Like(t.CanonicalTitle.ToLower(), $"%{leadWord}%") ||
                (t.OriginalTitle != null && EF.Functions.Like(t.OriginalTitle.ToLower(), $"%{leadWord}%")))
            .Take(50)
            .ToListAsync(cancellationToken);

        return ScoreTitleCandidates(
            observation,
            candidates,
            MediaObservationCandidateSources.CatalogTitleSearch);
    }

    private static List<MediaObservationCandidate> ScoreTitleCandidates(
        MediaObservation observation,
        IEnumerable<MediaTitle> titles,
        string source)
    {
        var observedNorm = NormalizeTitle(observation.ObservedTitle);
        var result = new List<MediaObservationCandidate>();

        foreach (var title in titles)
        {
            var score = ComputeTitleScore(observedNorm, title);
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
}
