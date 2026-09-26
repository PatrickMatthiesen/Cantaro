using System.Net;
using System.Text;
using System.Text.Json;
using Cantaro.Api.Configuration;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Cantaro.Api.Services.Spotify;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class SpotifySearchProviderTests
{
    [Fact]
    public async Task SearchAsync_UsesApplicationTokenAndReturnsTrackCandidate()
    {
        var handler = new QueueHandler(
        [
            Json("""{ "access_token": "app-token", "expires_in": 3600, "token_type": "Bearer" }"""),
            Json("""{ "tracks": { "items": [{ "type": "track", "id": "track-1", "name": "Beautiful Now", "duration_ms": 225000, "artists": [{ "name": "Zedd" }, { "name": "Jon Bellion" }], "external_ids": { "isrc": "USUM71504549" } }] } }"""),
            Json("""{ "tracks": { "items": [] } }""")
        ]);
        var options = Options.Create(new SpotifyOptions { ClientId = "client", ClientSecret = "secret" });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.spotify.com") };
        var apiClient = new SpotifyApiClient(httpClient, options, new NoDelay());
        var tokenProvider = new SpotifyCatalogTokenProvider(apiClient, options, TimeProvider.System, new SpotifyCatalogTokenCache());
        var provider = new SpotifySearchProvider(apiClient, tokenProvider);
        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = "video-1",
            Title = "Zedd - Beautiful Now ft. Jon Bellion (Official Music Video)",
            Artist = "Zedd, Jon Bellion", MatchStatus = "pending"
        };

        var candidates = await provider.SearchAsync(observation, CancellationToken.None);

        var candidate = Assert.Single(candidates);
        Assert.Equal("spotify", candidate.CandidateSource);
        Assert.Equal("track-1", candidate.ExternalId);
        Assert.Equal("USUM71504549", candidate.Isrc);
        Assert.Equal(["Zedd", "Jon Bellion"], candidate.ArtistCredits);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal("/api/token", handler.Requests[0].PathAndQuery);
        Assert.Contains("/v1/search?type=track&limit=10&q=", handler.Requests[1].PathAndQuery, StringComparison.Ordinal);
        Assert.Contains("artist%3A%22Zedd%22", handler.Requests[1].PathAndQuery, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchAsync_WhenSpotifyIsUnconfigured_DoesNotCallSpotify()
    {
        var handler = new QueueHandler([]);
        var options = Options.Create(new SpotifyOptions());
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.spotify.com") };
        var apiClient = new SpotifyApiClient(httpClient, options, new NoDelay());
        var provider = new SpotifySearchProvider(
            apiClient,
            new SpotifyCatalogTokenProvider(apiClient, options, TimeProvider.System, new SpotifyCatalogTokenCache()));

        var candidates = await provider.SearchAsync(new TrackObservation
        {
            Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = "video-1",
            Title = "Strangers", Artist = "Sigrid", MatchStatus = "pending"
        }, CancellationToken.None);

        Assert.Empty(candidates);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SearchAsync_UsesNormalizedIsrcFirstAndReturnsOnlyExactIsrcMatches()
    {
        var handler = new QueueHandler(
        [
            Json("""{ "access_token": "app-token", "expires_in": 3600, "token_type": "Bearer" }"""),
            Json("""{ "tracks": { "items": [{ "type": "track", "id": "wrong-track", "name": "Other Song", "duration_ms": 200000, "artists": [{ "name": "Other Artist" }], "external_ids": { "isrc": "GBAYE0601498" } }, { "type": "track", "id": "right-track", "name": "Song", "duration_ms": 180000, "artists": [{ "name": "Artist" }], "external_ids": { "isrc": "USUM71504549" } }] } }""")
        ]);
        var provider = CreateProvider(handler);
        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = "video-1",
            Title = "Song", Artist = "Artist", MatchStatus = "pending",
            RawMetadata = JsonSerializer.Serialize(new TrackObservationMetadata { Isrc = "us-um7-15-04549" })
        };

        var candidates = await provider.SearchAsync(observation, CancellationToken.None);

        var candidate = Assert.Single(candidates);
        Assert.Equal("right-track", candidate.ExternalId);
        Assert.Equal("USUM71504549", candidate.Isrc);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("q=isrc%3AUSUM71504549", handler.Requests[1].PathAndQuery, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchAsync_WhenIsrcSearchHasNoExactMatchFallsBackToTitleAndArtist()
    {
        var handler = new QueueHandler(
        [
            Json("""{ "access_token": "app-token", "expires_in": 3600, "token_type": "Bearer" }"""),
            Json("""{ "tracks": { "items": [{ "type": "track", "id": "wrong-track", "name": "Other Song", "duration_ms": 200000, "artists": [{ "name": "Other Artist" }], "external_ids": { "isrc": "GBAYE0601498" } }] } }"""),
            Json("""{ "tracks": { "items": [{ "type": "track", "id": "title-match", "name": "Song", "duration_ms": 180000, "artists": [{ "name": "Artist" }], "external_ids": { "isrc": "USUM71504549" } }] } }""")
        ]);
        var provider = CreateProvider(handler);
        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = "video-1",
            Title = "Song", Artist = "Artist", MatchStatus = "pending",
            RawMetadata = JsonSerializer.Serialize(new TrackObservationMetadata { Isrc = "USUM71504549" })
        };

        var candidates = await provider.SearchAsync(observation, CancellationToken.None);

        Assert.Equal("title-match", Assert.Single(candidates).ExternalId);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Contains("q=isrc%3AUSUM71504549", handler.Requests[1].PathAndQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("artist%3A%22Artist%22", handler.Requests[2].PathAndQuery, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchAsync_WhenTitleAndArtistSearchIsEmptyRetriesWithTitleOnly()
    {
        var handler = new QueueHandler(
        [
            Json("""{ "access_token": "app-token", "expires_in": 3600, "token_type": "Bearer" }"""),
            Json("""{ "tracks": { "items": [] } }"""),
            Json("""{ "tracks": { "items": [{ "type": "track", "id": "title-only-match", "name": "Strangers", "duration_ms": 210000, "artists": [{ "name": "Sigrid" }] }] } }""")
        ]);
        var provider = CreateProvider(handler);
        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = "video-1",
            Title = "Strangers", Artist = "Unknown uploader", MatchStatus = "pending"
        };

        var candidates = await provider.SearchAsync(observation, CancellationToken.None);

        Assert.Equal("title-only-match", Assert.Single(candidates).ExternalId);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Contains("artist%3A%22Unknown%20uploader%22", handler.Requests[1].PathAndQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("q=track%3A%22Strangers%22", handler.Requests[2].PathAndQuery, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("artist%3A", handler.Requests[2].PathAndQuery, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchAsync_TriesReversedAttributionAndScoresItWithTheSameEvidence()
    {
        var handler = new QueueHandler([
            Json("""{ "access_token": "app-token", "expires_in": 3600, "token_type": "Bearer" }"""),
            Json("""{ "tracks": { "items": [] } }"""),
            Json("""{ "tracks": { "items": [{ "type": "track", "id": "correct", "name": "Everything Goes On", "duration_ms": 160000, "artists": [{ "name": "Porter Robinson" }], "external_ids": { "isrc": "USABC2400001" } }] } }""")
        ]);
        var observation = new TrackObservation
        {
            SourceType = "youtube", ExternalId = "video", Title = "Porter Robinson | Star Guardian 2022",
            Artist = "Everything Goes On", DurationSeconds = 160, MatchStatus = "pending",
            RawMetadata = JsonSerializer.Serialize(new TrackObservationMetadata
            { OriginalTitle = "Everything Goes On - Porter Robinson (Official Music Video) | Star Guardian 2022" })
        };

        var candidate = Assert.Single(await CreateProvider(handler).SearchAsync(observation, CancellationToken.None));

        Assert.Equal(3, handler.Requests.Count);
        Assert.Contains("track%3A%22Everything%20Goes%20On%22", handler.Requests[2].PathAndQuery);
        Assert.Contains("artist%3A%22Porter%20Robinson%22", handler.Requests[2].PathAndQuery);
        var scored = TrackMatchScorer.Score(observation, candidate, new TrackMatchingOptions());
        Assert.True(scored.IsAutoMatchEligible);
        Assert.Equal(1m, scored.Score);
    }

    [Fact]
    public async Task SearchAsync_WeakPrimaryResultsDoNotPreventAnAlternateSearch()
    {
        var handler = new QueueHandler([
            Json("""{ "access_token": "app-token", "expires_in": 3600, "token_type": "Bearer" }"""),
            Json("""{ "tracks": { "items": [{ "type": "track", "id": "wrong", "name": "Addicted - Karaoke", "duration_ms": 153000, "artists": [{ "name": "Karaoke Singer" }] }] } }"""),
            Json("""{ "tracks": { "items": [{ "type": "track", "id": "correct", "name": "Addicted", "duration_ms": 153000, "artists": [{ "name": "Zerb" }, { "name": "The Chainsmokers" }, { "name": "Ink" }], "external_ids": { "isrc": "USABC2400001" } }] } }""")
        ]);
        var observation = new TrackObservation
        {
            SourceType = "youtube", ExternalId = "video", Title = "ADDICTED //// Zerb, The Chainsmokers & Ink",
            Artist = "MrMoMMusic", DurationSeconds = 153, MatchStatus = "pending"
        };
        var candidates = await CreateProvider(handler).SearchAsync(observation, CancellationToken.None);

        Assert.Equal(2, candidates.Count);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Contains("artist%3A%22Zerb%22", handler.Requests[2].PathAndQuery);
        Assert.False(TrackMatchScorer.Score(observation, candidates[0], new TrackMatchingOptions()).IsAutoMatchEligible);
        Assert.True(TrackMatchScorer.Score(observation, candidates[1], new TrackMatchingOptions()).IsAutoMatchEligible);
    }

    [Fact]
    public async Task SearchAsync_UsesDescriptionCreditsToFindAndAcceptMortalsDespiteUploaderArtist()
    {
        var handler = new QueueHandler([
            Json("""{ "access_token": "app-token", "expires_in": 3600, "token_type": "Bearer" }"""),
            Json("""{ "tracks": { "items": [{ "type": "track", "id": "mortals-normal", "name": "Mortals Funk Remix", "duration_ms": 146000, "artists": [{ "name": "LXNGVX" }, { "name": "Warriyo" }] }] } }"""),
            Json("""{ "tracks": { "items": [] } }""")
        ]);
        var provider = CreateProvider(handler);
        var observation = CreateMortalsObservation();

        var candidates = await provider.SearchAsync(observation, CancellationToken.None);

        Assert.Contains("track%3A%22Mortals%20Funk%20Remix%22", handler.Requests[1].PathAndQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("artist%3A%22LXNGVX%22", handler.Requests[1].PathAndQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("MrMoMMusic", observation.Artist);
        var accepted = TrackMatchDecisionEngine.Evaluate(observation, candidates, new()).Decision.AcceptedCandidate;
        Assert.NotNull(accepted);
        Assert.Equal("mortals-normal", accepted.Candidate.ExternalId);
        Assert.Equal(1m, accepted.Score);
    }

    private static SpotifySearchProvider CreateProvider(QueueHandler handler)
    {
        var options = Options.Create(new SpotifyOptions { ClientId = "client", ClientSecret = "secret" });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.spotify.com") };
        var apiClient = new SpotifyApiClient(httpClient, options, new NoDelay());
        var tokenProvider = new SpotifyCatalogTokenProvider(apiClient, options, TimeProvider.System, new SpotifyCatalogTokenCache());
        return new SpotifySearchProvider(apiClient, tokenProvider);
    }

    private static TrackObservation CreateMortalsObservation()
    {
        const string title = "MORTALS FUNK REMIX // Victory Royale B**ch!";
        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = "dKmnejGmcjQ",
            Title = title, Artist = "MrMoMMusic", DurationSeconds = 147, MatchStatus = "pending"
        };
        observation.RawMetadata = JsonSerializer.Serialize(new TrackObservationMetadata
        {
            OriginalTitle = title, OriginalArtist = "MrMoMMusic", ChannelTitle = "MrMoMMusic",
            Description = "LXNGVX, Warriyo - Mortals Funk Remix\n\nAvailable here: https://open.spotify.com/album/example\n\nFollow Lxngvx"
        });
        return observation;
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class QueueHandler(IEnumerable<HttpResponseMessage> responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class NoDelay : ISpotifyRetryDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
