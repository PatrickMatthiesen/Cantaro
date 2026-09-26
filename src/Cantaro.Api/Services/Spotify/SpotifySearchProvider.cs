using Cantaro.Api.Models;

namespace Cantaro.Api.Services.Spotify;

public sealed class SpotifySearchProvider(
    SpotifyApiClient apiClient,
    SpotifyCatalogTokenProvider tokenProvider) : ITrackMetadataSearchProvider
{
    private const int ResultLimit = 10;

    public async Task<IReadOnlyList<TrackMatchSearchCandidate>> SearchAsync(
        TrackObservation observation,
        CancellationToken cancellationToken)
    {
        var hypotheses = TrackObservationParser.ParseSearchHypotheses(observation);
        var parsed = hypotheses[0];
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (token == null)
        {
            return [];
        }

        var metadata = TrackObservationParser.ReadMetadata(observation);
        var isrc = TrackIdentityResolver.NormalizeIsrc(metadata?.Isrc);
        if (!string.IsNullOrWhiteSpace(isrc))
        {
            var isrcTracks = await apiClient.SearchTracksAsync(token, $"isrc:{isrc}", ResultLimit, cancellationToken);
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
        foreach (var hypothesis in hypotheses.Take(4))
        {
            var query = $"track:{Quote(hypothesis.SearchTitle)}";
            var hypothesisArtist = GetPrimaryArtist(hypothesis);
            if (!string.IsNullOrWhiteSpace(hypothesisArtist)) query += $" artist:{Quote(hypothesisArtist)}";
            if (!queries.Add(query)) continue;
            tracks.AddRange(await apiClient.SearchTracksAsync(token, query, ResultLimit, cancellationToken));
        }

        if (tracks.Count == 0 && !string.IsNullOrWhiteSpace(GetPrimaryArtist(parsed)))
        {
            tracks.AddRange(await apiClient.SearchTracksAsync(
                token, $"track:{Quote(searchTitle)}", ResultLimit, cancellationToken));
        }

        return ToCandidates(tracks.DistinctBy(track => track.Id));
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
