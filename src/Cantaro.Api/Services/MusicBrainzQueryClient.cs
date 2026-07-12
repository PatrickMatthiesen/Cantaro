using System.Net.Http.Headers;
using System.Text.Json;
using MetaBrainz.MusicBrainz;
using MetaBrainz.MusicBrainz.Interfaces.Entities;
using MetaBrainz.MusicBrainz.Interfaces.Searches;

namespace Cantaro.Api.Services;

public class MusicBrainzQueryClient : IMusicBrainzQueryClient
{
    private readonly Query _query;

    public MusicBrainzQueryClient(HttpClient httpClient)
    {
        if (httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Cantaro", "0.1.0"));
        }

        _query = new Query(httpClient, takeOwnership: false);
    }

    public async Task<IReadOnlyList<MusicBrainzRecordingMatch>> FindRecordingsAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        var searchResults = await _query.FindRecordingsAsync(query, limit, offset: null, simple: false, cancellationToken);
        var matches = new List<MusicBrainzRecordingMatch>(limit);

        foreach (var searchResult in searchResults.Results.Take(limit))
        {
            matches.Add(MapMatch(searchResult));
        }

        return matches;
    }

    private static MusicBrainzRecordingMatch MapMatch(ISearchResult<IRecording> searchResult)
    {
        var recording = searchResult.Item;
        var artist = FormatArtistCredit(recording.ArtistCredit);
        var singleArtist = recording.ArtistCredit.Count == 1
            ? recording.ArtistCredit[0].Artist
            : null;
        int? durationSeconds = recording.Length.HasValue
            ? (int)Math.Round(recording.Length.Value.TotalSeconds, MidpointRounding.AwayFromZero)
            : null;

        return new MusicBrainzRecordingMatch
        {
            ExternalId = recording.Id.ToString(),
            Title = recording.Title,
            Artist = string.IsNullOrWhiteSpace(artist) ? null : artist,
            ArtistCredits = recording.ArtistCredit
                .Select(credit => credit.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .ToArray(),
            // An aggregate display string cannot safely inherit one member's
            // identity. Only propagate an artist MBID for a single credited
            // MusicBrainz artist.
            ArtistMusicBrainzId = singleArtist?.Id.ToString(),
            ArtistSortName = singleArtist?.SortName,
            MbidRecording = recording.Id.ToString(),
            Isrc = recording.Isrcs?.FirstOrDefault(),
            DurationSeconds = durationSeconds,
            SearchScore = searchResult.Score,
            RawMetadata = JsonSerializer.Serialize(new
            {
                Id = recording.Id,
                recording.Title,
                Artist = artist,
                ArtistCredits = recording.ArtistCredit.Select(credit => new
                {
                    credit.Name,
                    credit.JoinPhrase,
                    MusicBrainzArtistId = credit.Artist?.Id,
                    SortName = credit.Artist?.SortName
                }),
                recording.Isrcs,
                Length = recording.Length,
                searchResult.Score
            })
        };
    }

    private static string? FormatArtistCredit(IReadOnlyList<INameCredit> artistCredit)
    {
        if (artistCredit.Count == 0)
        {
            return null;
        }

        var display = string.Concat(artistCredit.Select(credit => $"{credit.Name}{credit.JoinPhrase}"));
        return string.IsNullOrWhiteSpace(display) ? null : display.Trim();
    }
}
