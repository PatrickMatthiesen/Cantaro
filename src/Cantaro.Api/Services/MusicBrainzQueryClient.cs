using System.Net.Http.Headers;
using System.Text.Json;
using MetaBrainz.Common;
using MetaBrainz.MusicBrainz;
using MetaBrainz.MusicBrainz.Interfaces.Entities;
using MetaBrainz.MusicBrainz.Interfaces.Searches;

namespace Cantaro.Api.Services;

public class MusicBrainzQueryClient : IMusicBrainzQueryClient
{
    private readonly Query _query;
    private readonly MusicBrainzRequestGate _requestGate;

    public MusicBrainzQueryClient(HttpClient httpClient, MusicBrainzRequestGate requestGate)
    {
        _requestGate = requestGate;
        if (httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Cantaro/1.0 (+https://github.com/PatrickMatthiesen/Cantaro)");
        }

        _query = new Query(httpClient, takeOwnership: false);
    }

    public async Task<IReadOnlyList<MusicBrainzRecordingMatch>> FindRecordingsAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        await _requestGate.WaitAsync(cancellationToken);

        ISearchResults<ISearchResult<IRecording>> searchResults;
        try
        {
            searchResults = await _query.FindRecordingsAsync(query, limit, offset: null, simple: false, cancellationToken);
            _requestGate.RecordSuccess();
        }
        catch (HttpError exception) when (FindServiceUnavailable(exception) is { } unavailable)
        {
            _requestGate.Defer(unavailable.ResponseHeaders?.RetryAfter);
            throw;
        }

        var matches = new List<MusicBrainzRecordingMatch>(limit);

        foreach (var searchResult in searchResults.Results.Take(limit))
        {
            matches.Add(MapMatch(searchResult));
        }

        return matches;
    }

    private static HttpError? FindServiceUnavailable(HttpError exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is HttpError { Status: System.Net.HttpStatusCode.ServiceUnavailable } unavailable)
            {
                return unavailable;
            }
        }

        return null;
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
