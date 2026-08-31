using System.Net;
using System.Text;
using Cantaro.Api.Configuration;
using Cantaro.Api.Models;
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
            Json("""{ "tracks": { "items": [{ "type": "track", "id": "track-1", "name": "Beautiful Now", "duration_ms": 225000, "artists": [{ "name": "Zedd" }, { "name": "Jon Bellion" }], "external_ids": { "isrc": "USUM71504549" } }] } }""")
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
        Assert.Equal(2, handler.Requests.Count);
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
