using System.Net.Http.Headers;
using System.Text.Json;
using System.Collections;
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
        int? durationSeconds = recording.Length.HasValue
            ? (int)Math.Round(recording.Length.Value.TotalSeconds, MidpointRounding.AwayFromZero)
            : null;

        return new MusicBrainzRecordingMatch
        {
            ExternalId = recording.Id.ToString(),
            Title = recording.Title,
            Artist = string.IsNullOrWhiteSpace(artist) ? null : artist,
            MbidRecording = recording.Id.ToString(),
            Isrc = recording.Isrcs?.FirstOrDefault(),
            DurationSeconds = durationSeconds,
            SearchScore = searchResult.Score,
            RawMetadata = JsonSerializer.Serialize(new
            {
                Id = recording.Id,
                recording.Title,
                Artist = artist,
                recording.Isrcs,
                Length = recording.Length,
                searchResult.Score
            })
        };
    }

    private static string? FormatArtistCredit(object? artistCredit)
    {
        if (artistCredit is not IEnumerable credits)
        {
            return artistCredit?.ToString();
        }

        var names = new List<string>();
        foreach (var credit in credits)
        {
            if (credit == null)
            {
                continue;
            }

            var name = credit.GetType().GetProperty("Name")?.GetValue(credit)?.ToString();
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name);
            }
        }

        return names.Count > 0 ? string.Join(", ", names) : artistCredit.ToString();
    }
}