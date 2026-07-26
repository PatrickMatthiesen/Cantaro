using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Cantaro.Api.Configuration;
using Cantaro.Api.Services;
using Cantaro.Api.Services.Spotify;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class SpotifyApiClientTests
{
    [Fact]
    public async Task GetPlaylistsAsync_UsesCurrentItemsSummaryAndOwnerSchema()
    {
        var handler = new StubHandler(
        [
            Json(HttpStatusCode.OK, """
                {
                  "items": [{
                    "id": "playlist-1",
                    "name": "Playlist one",
                    "description": "Description",
                    "owner": { "display_name": "Playlist owner" },
                    "images": [{ "url": "https://i.scdn.co/image/temporary" }],
                    "external_urls": { "spotify": "https://open.spotify.com/playlist/playlist-1" },
                    "snapshot_id": "snapshot-1",
                    "items": { "total": 12 }
                  }],
                  "next": null
                }
                """)
        ]);
        using var client = CreateClient(handler);

        var playlists = await client.Api.GetPlaylistsAsync("access-token", CancellationToken.None);

        var playlist = Assert.Single(playlists);
        Assert.Equal("Playlist owner", playlist.OwnerName);
        Assert.Equal(12, playlist.ItemCount);
        Assert.Equal("https://open.spotify.com/playlist/playlist-1", playlist.ExternalUrl);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/v1/me/playlists?limit=50&offset=0", request.PathAndQuery);
        Assert.Equal("Bearer", request.Authorization?.Scheme);
    }

    [Fact]
    public async Task GetPlaylistItemsAsync_UsesCurrentItemsPathAndPaginatesOnlySupportedTracks()
    {
        var handler = new StubHandler(
        [
            Json(HttpStatusCode.OK, """
                {
                  "items": [
                    {
                      "added_at": "2026-07-26T10:00:00Z",
                      "is_local": false,
                      "item": {
                        "type": "track",
                        "id": "track-1",
                        "name": "Track one",
                        "duration_ms": 245000,
                        "artists": [{ "name": "Artist one" }],
                        "album": {
                          "name": "Album one",
                          "images": [{ "url": "https://images.spotify.test/album.jpg" }],
                          "external_urls": { "spotify": "https://open.spotify.com/album/album-1" }
                        },
                        "external_urls": { "spotify": "https://open.spotify.com/track/track-1" },
                        "external_ids": { "isrc": "TESTISRC1" }
                      }
                    },
                    {
                      "is_local": false,
                      "item": { "type": "episode", "id": "episode-1", "name": "Episode one" }
                    },
                    { "is_local": false, "item": null },
                    {
                      "is_local": true,
                      "item": { "type": "track", "id": "local-track", "name": "Local track" }
                    }
                  ],
                  "next": "https://api.spotify.com/v1/playlists/playlist-1/items?limit=50&offset=50"
                }
                """),
            Json(HttpStatusCode.OK, """{ "items": [], "next": null }""")
        ]);
        using var client = CreateClient(handler);

        var tracks = await client.Api.GetPlaylistItemsAsync("access-token", "playlist/one", CancellationToken.None);

        var track = Assert.Single(tracks);
        Assert.Equal("track-1", track.Id);
        Assert.Equal("Artist one", track.Artist);
        Assert.Equal(245, track.DurationSeconds);
        Assert.Equal(0, track.Position);

        Assert.Collection(
            handler.Requests,
            request => Assert.Equal("/v1/playlists/playlist%2Fone/items?limit=50&offset=0", request.PathAndQuery),
            request => Assert.Equal("/v1/playlists/playlist%2Fone/items?limit=50&offset=50", request.PathAndQuery));
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal("Bearer", request.Authorization?.Scheme);
            Assert.Equal("access-token", request.Authorization?.Parameter);
            Assert.DoesNotContain("/tracks", request.PathAndQuery, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task GetProfileAsync_RetriesRateLimitsWithRetryAfterAndExponentialBackoffWithoutSleeping()
    {
        var handler = new StubHandler(
        [
            Json(HttpStatusCode.TooManyRequests, """{ "error": { "status": 429, "message": "Slow down" } }""", retryAfterSeconds: 5),
            Json(HttpStatusCode.TooManyRequests, """{ "error": { "status": 429, "message": "Slow down" } }"""),
            Json(HttpStatusCode.OK, """{ "account_id": "account-1", "id": "user-1", "display_name": "Test user" }""")
        ]);
        var delay = new RecordingDelay();
        using var client = CreateClient(handler, delay);

        var profile = await client.Api.GetProfileAsync("access-token", CancellationToken.None);

        Assert.Equal("account-1", profile.AccountId);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2)], delay.Delays);
    }

    [Fact]
    public async Task GetProfileAsync_SurfacesQuotaExceededAfterBoundedRetries()
    {
        var handler = new StubHandler(
        Enumerable.Range(0, 4)
            .Select(_ => Json(
                HttpStatusCode.TooManyRequests,
                """{ "error": { "status": 429, "message": "Too many requests", "reason": "QUOTA_EXCEEDED" } }""",
                retryAfterSeconds: 7))
            .ToArray());
        var delay = new RecordingDelay();
        using var client = CreateClient(handler, delay);

        var exception = await Assert.ThrowsAsync<PlatformApiException>(
            () => client.Api.GetProfileAsync("access-token", CancellationToken.None));

        Assert.Equal("spotify_quota_exceeded", exception.Code);
        Assert.Equal(429, exception.StatusCode);
        Assert.Equal("Too many requests", exception.Message);
        Assert.Equal(TimeSpan.FromSeconds(8), exception.RetryAfter);
        Assert.Equal(4, handler.Requests.Count);
        Assert.Equal(3, delay.Delays.Count);
    }

    [Fact]
    public async Task ExchangeCodeAsync_SendsOfficialTokenRequestShape()
    {
        var handler = new StubHandler(
        [Json(HttpStatusCode.OK, """{ "access_token": "new-access-token", "expires_in": 3600, "token_type": "Bearer" }""")]);
        using var client = CreateClient(handler);

        var token = await client.Api.ExchangeCodeAsync(
            "authorization-code",
            "https://cantaro.example/api/platforms/spotify/callback",
            CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://accounts.spotify.com/api/token", request.Uri.AbsoluteUri);
        Assert.Equal("Basic", request.Authorization?.Scheme);
        Assert.Equal("test-client:test-secret", Encoding.UTF8.GetString(Convert.FromBase64String(request.Authorization!.Parameter!)));
        Assert.Equal("application/x-www-form-urlencoded", request.ContentType?.MediaType);
        Assert.Equal(
            "grant_type=authorization_code&code=authorization-code&redirect_uri=https%3A%2F%2Fcantaro.example%2Fapi%2Fplatforms%2Fspotify%2Fcallback",
            request.Body);
        Assert.Equal("new-access-token", token.AccessToken);
    }

    private static ClientScope CreateClient(StubHandler handler, ISpotifyRetryDelay? retryDelay = null)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.spotify.com")
        };
        var options = Options.Create(new SpotifyOptions
        {
            ClientId = "test-client",
            ClientSecret = "test-secret",
            RedirectUri = "https://cantaro.example/api/platforms/spotify/callback"
        });
        return new ClientScope(new SpotifyApiClient(httpClient, options, retryDelay ?? new RecordingDelay()), httpClient);
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string body, int? retryAfterSeconds = null)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        if (retryAfterSeconds is { } seconds)
        {
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
        }

        return response;
    }

    private sealed class StubHandler(IEnumerable<HttpResponseMessage> responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization,
                request.Content?.Headers.ContentType,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return _responses.Dequeue();
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri Uri,
        AuthenticationHeaderValue? Authorization,
        MediaTypeHeaderValue? ContentType,
        string? Body)
    {
        public string PathAndQuery => Uri.PathAndQuery;
    }

    private sealed class RecordingDelay : ISpotifyRetryDelay
    {
        public List<TimeSpan> Delays { get; } = [];

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delays.Add(delay);
            return Task.CompletedTask;
        }
    }

    private sealed class ClientScope(SpotifyApiClient api, HttpClient httpClient) : IDisposable
    {
        public SpotifyApiClient Api { get; } = api;

        public void Dispose() => httpClient.Dispose();
    }
}
