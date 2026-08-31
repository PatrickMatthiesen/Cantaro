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
        if (string.IsNullOrWhiteSpace(observation.Title))
        {
            return [];
        }

        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (token == null)
        {
            return [];
        }

        var parsed = TrackObservationParser.Parse(observation);
        var query = $"track:{Quote(parsed.SearchTitle ?? observation.Title)}";
        var artist = GetPrimaryArtist(parsed);
        if (!string.IsNullOrWhiteSpace(artist))
        {
            query += $" artist:{Quote(artist)}";
        }

        var tracks = await apiClient.SearchTracksAsync(token, query, ResultLimit, cancellationToken);
        return tracks.Select(track => new TrackMatchSearchCandidate
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
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", string.Empty, StringComparison.Ordinal).Trim()}\"";

    private static string? GetPrimaryArtist(ParsedTrackMetadata parsed)
    {
        var displayArtist = parsed.DisplayArtist ?? parsed.SearchArtist;
        if (string.IsNullOrWhiteSpace(displayArtist))
        {
            return null;
        }

        return displayArtist
            .Split([",", " & ", " and "], 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
    }
}
