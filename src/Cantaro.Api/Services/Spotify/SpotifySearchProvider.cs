using Cantaro.Api.Configuration;
using Cantaro.Api.Models;
using Microsoft.Extensions.Options;

namespace Cantaro.Api.Services.Spotify;

public sealed class SpotifySearchProvider(
    SpotifyApiClient apiClient,
    SpotifyCatalogTokenProvider tokenProvider,
    IOptions<TrackMatchingOptions>? matchingOptions = null,
    SpotifySearchCache? searchCache = null) : ITrackMetadataSearchProvider
{
    private const int ResultLimit = 10;
    private readonly TrackMatchingOptions _matchingOptions = matchingOptions?.Value ?? new();

    public async Task<IReadOnlyList<TrackMatchSearchCandidate>> SearchAsync(
        TrackObservation observation,
        CancellationToken cancellationToken)
    {
        var hypotheses = TrackObservationParser.ParseSearchHypotheses(observation);
        var parsed = hypotheses[0];
        if (!tokenProvider.IsConfigured)
        {
            return [];
        }

        var metadata = TrackObservationParser.ReadMetadata(observation);
        var isrc = TrackIdentityResolver.NormalizeIsrc(metadata?.Isrc);
        if (!string.IsNullOrWhiteSpace(isrc))
        {
            var isrcTracks = await SearchTracksAsync($"isrc:{isrc}", cancellationToken);
            var isrcMatches = isrcTracks
                .Where(track => string.Equals(
                    TrackIdentityResolver.NormalizeIsrc(track.Isrc), isrc, StringComparison.Ordinal))
                .ToArray();
            if (isrcMatches.Length > 0)
            {
                return ToCandidates(isrcMatches);
            }
        }

        var searchTitle = parsed.SearchTitle ?? observation.Title;
        if (string.IsNullOrWhiteSpace(searchTitle))
        {
            return [];
        }

        var tracks = new List<SpotifyTrackSnapshot>();
        var queries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var searchHypotheses = hypotheses.Take(4).ToArray();
        for (var index = 0; index < searchHypotheses.Length; index++)
        {
            var hypothesis = searchHypotheses[index];
            var query = $"track:{Quote(hypothesis.SearchTitle)}";
            var hypothesisArtist = GetPrimaryArtist(hypothesis);
            if (!string.IsNullOrWhiteSpace(hypothesisArtist)) query += $" artist:{Quote(hypothesisArtist)}";
            if (!queries.Add(query)) continue;
            tracks.AddRange(await SearchTracksAsync(query, cancellationToken));
            var candidates = ToCandidates(tracks.DistinctBy(track => track.Id));
            var accepted = TrackMatchDecisionEngine.Evaluate(observation, candidates, _matchingOptions)
                .Decision.AcceptedCandidate;
            if (accepted is not null && CanStopBeforeRemainingQueries(
                observation, metadata, isrc, searchHypotheses, index, accepted))
            {
                return candidates;
            }
        }

        if (tracks.Count == 0 && !string.IsNullOrWhiteSpace(GetPrimaryArtist(parsed)))
        {
            var fallbackQuery = $"track:{Quote(searchTitle)}";
            if (queries.Add(fallbackQuery))
            {
                tracks.AddRange(await SearchTracksAsync(fallbackQuery, cancellationToken));
            }
        }

        return ToCandidates(tracks.DistinctBy(track => track.Id));
    }

    private static bool CanStopBeforeRemainingQueries(
        TrackObservation observation,
        TrackObservationMetadata? metadata,
        string? isrc,
        IReadOnlyList<ParsedTrackMetadata> hypotheses,
        int currentIndex,
        TrackMatchScoredCandidate accepted)
    {
        var remaining = hypotheses.Skip(currentIndex + 1).ToArray();
        if (remaining.Length == 0 || remaining.All(other =>
            TrackTextNormalizer.AreEquivalentTitles(hypotheses[currentIndex].SearchTitle, other.SearchTitle)
            && hypotheses[currentIndex].ArtistCredits.SequenceEqual(other.ArtistCredits, StringComparer.Ordinal)))
        {
            return true;
        }

        if (hypotheses[currentIndex].IsDescriptionArtistEvidence
            || (isrc is not null && isrc == TrackIdentityResolver.NormalizeIsrc(accepted.Candidate.Isrc)))
        {
            return true;
        }

        var sourceArtist = metadata?.SearchArtist ?? observation.Artist;
        return !string.IsNullOrWhiteSpace(sourceArtist)
            && TrackTextNormalizer.Normalize(sourceArtist) == TrackTextNormalizer.Normalize(accepted.Candidate.Artist)
            && remaining.All(other => !TrackTextNormalizer.AreEquivalentTitles(sourceArtist, other.SearchTitle));
    }

    private async Task<IReadOnlyList<SpotifyTrackSnapshot>> SearchTracksAsync(
        string query, CancellationToken cancellationToken)
    {
        async Task<IReadOnlyList<SpotifyTrackSnapshot>> SearchCatalogAsync(CancellationToken ct)
        {
            var token = await tokenProvider.GetAccessTokenAsync(ct)
                ?? throw new InvalidOperationException("Spotify catalog token is unavailable.");
            return await apiClient.SearchTracksAsync(token, query, ResultLimit, ct);
        }

        return searchCache is null
            ? await SearchCatalogAsync(cancellationToken)
            : await searchCache.GetOrSearchAsync(query, ResultLimit, SearchCatalogAsync, cancellationToken);
    }

    private static IReadOnlyList<TrackMatchSearchCandidate> ToCandidates(
        IEnumerable<SpotifyTrackSnapshot> tracks) => tracks.Select(track => new TrackMatchSearchCandidate
    {
        CandidateSource = SpotifyService.ServiceName,
        ExternalId = track.Id,
        Title = track.Name,
        Artist = track.Artist,
        ArtistCredits = track.ArtistNames,
        Isrc = track.Isrc,
        DurationSeconds = track.DurationSeconds,
        Explanation = "Suggested by Spotify catalog search."
    }).ToArray();

    private static string Quote(string value) => $"\"{value.Replace("\"", string.Empty, StringComparison.Ordinal).Trim()}\"";

    private static string? GetPrimaryArtist(ParsedTrackMetadata parsed)
    {
        var displayArtist = parsed.DisplayArtist ?? parsed.SearchArtist;
        if (string.IsNullOrWhiteSpace(displayArtist))
        {
            return null;
        }

        return displayArtist
            .Split([",", " & ", " and ", " × ", " x "], 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
    }
}
