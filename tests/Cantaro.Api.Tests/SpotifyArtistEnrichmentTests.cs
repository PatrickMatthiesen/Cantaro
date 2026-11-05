using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Cantaro.Api.Configuration;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Cantaro.Api.Services.Spotify;
using Microsoft.Extensions.Options;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class SpotifyArtistEnrichmentTests
{
    private const string Details = """{"id":"playlist","name":"Songs","snapshot_id":"revision","owner":{"id":"owner"},"public":false}""";
    private static readonly object[] Credits = [
        new { id = "first", name = "First credited name", external_urls = new { spotify = "https://open.spotify.com/artist/first" } },
        new { id = "second", name = "Second credited name", external_urls = new { spotify = "https://open.spotify.com/artist/second" } }
    ];

    [Theory]
    [InlineData("search")]
    [InlineData("playlist")]
    [InlineData("sync")]
    public async Task Reads_EnrichFromFullArtistEndpointOncePerIdAfterRequiredReads(string ingress)
    {
        var handler = new RoutingHandler((request, _) => Task.FromResult(
            RequiredRead(request.RequestUri!) ?? Artist(request.RequestUri!.Segments.Last())));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.spotify.com") };
        var api = CreateApi(http);

        var tracks = await ReadAsync(api, ingress);

        Assert.Equal(2, tracks.Count);
        Assert.Equal(["first", "second"], handler.ArtistIds);
        Assert.All(handler.Requests, request => Assert.Equal("Bearer token", request.Authorization));
        Assert.All(tracks, track =>
        {
            Assert.Equal(["First credited name", "Second credited name"], track.ArtistNames);
            Assert.Equal("First credited name, Second credited name", track.Artist);
            Assert.Equal(["first", "second"], track.ArtistIdentities.Select(credit => credit.ExternalId));
            Assert.Equal(track.ArtistNames, track.ArtistIdentities.Select(credit => credit.Name));
            Assert.All(track.ArtistIdentities, credit =>
            {
                Assert.Equal("spotify", credit.Provider);
                Assert.Equal($"https://images/{credit.ExternalId}", credit.ArtworkUrl);
                Assert.Equal($"https://open.spotify.com/artist/{credit.ExternalId}", credit.ProfileUrl);
                using var raw = JsonDocument.Parse(credit.RawMetadata!);
                Assert.Equal(credit.ExternalId, raw.RootElement.GetProperty("artist").GetProperty("id").GetString());
                Assert.Contains(credit.Name, raw.RootElement.GetProperty("credit").GetString());
            });
            Assert.Equal("https://images/album", track.ImageUrl);
        });
        var firstArtistIndex = handler.Requests.FindIndex(request => request.Path.StartsWith("/v1/artists/"));
        Assert.All(handler.Requests.Skip(firstArtistIndex), request => Assert.StartsWith("/v1/artists/", request.Path));
    }

    [Fact]
    public async Task EnrichedSyncSnapshot_RepeatedImportPersistsArtworkAndOrderedCredits()
    {
        var handler = new RoutingHandler((request, _) => Task.FromResult(
            RequiredRead(request.RequestUri!) ?? Artist(request.RequestUri!.Segments.Last())));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.spotify.com") };
        var api = CreateApi(http);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var resolver = new SpotifyTrackResolver(db);
        for (var iteration = 0; iteration < 2; iteration++)
        {
            var snapshot = await api.GetPlaylistSyncReadSnapshotAsync("token", "playlist", CancellationToken.None);
            foreach (var item in snapshot.Items)
                await resolver.ResolveAsync(item.Track!, DateTimeOffset.UtcNow, CancellationToken.None);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }

        Assert.Equal(1, await db.Tracks.CountAsync());
        Assert.Equal(2, await db.Artists.CountAsync());
        var links = await db.ArtistProviderLinks.OrderBy(link => link.ExternalId).ToArrayAsync();
        Assert.Equal(["first", "second"], links.Select(link => link.ExternalId));
        Assert.All(links, link =>
        {
            Assert.Equal($"https://images/{link.ExternalId}", link.ArtworkUrl);
            Assert.Equal($"https://open.spotify.com/artist/{link.ExternalId}", link.ProfileUrl);
            Assert.Contains("images", link.RawMetadata);
        });
        var credits = await db.TrackArtistCredits.OrderBy(credit => credit.Position).ToArrayAsync();
        Assert.Equal(["First credited name", "Second credited name"], credits.Select(credit => credit.CreditedName));
        Assert.All(credits, credit => Assert.Contains("spotify", credit.Provenance));
    }

    [Fact]
    public async Task Enrichment_PreservesRoleJoinPhraseAndDifferentCreditedNamesForOneId()
    {
        var handler = new RoutingHandler((request, _) => Task.FromResult(Artist(request.RequestUri!.Segments.Last())));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.spotify.com") };
        ArtistCreditMetadata[] credits = [
            new() { Provider = "spotify", ExternalId = "first", Name = "Alias", Role = TrackArtistRole.Primary, JoinPhrase = " feat. ", RawMetadata = "original credit" },
            new() { Provider = "spotify", ExternalId = "first", Name = "Guest alias", Role = TrackArtistRole.Featured }
        ];
        var track = new SpotifyTrackSnapshot("track", "Song", "Alias feat. Guest alias", ["Alias", "Guest alias"],
            null, null, "https://open.spotify.com/track/track", null, null, 180, null, 0) { ArtistIdentities = credits };

        var enriched = Assert.Single(await CreateApi(http).EnrichArtistMetadataAsync("token", [track], CancellationToken.None));

        Assert.Single(handler.ArtistIds);
        Assert.Equal(track.Artist, enriched.Artist);
        Assert.Equal(credits.Select(credit => credit.Name), enriched.ArtistIdentities.Select(credit => credit.Name));
        Assert.Equal(credits.Select(credit => credit.Role), enriched.ArtistIdentities.Select(credit => credit.Role));
        Assert.Equal(" feat. ", enriched.ArtistIdentities[0].JoinPhrase);
        Assert.Contains("original credit", enriched.ArtistIdentities[0].RawMetadata);
    }

    [Theory]
    [InlineData(401, 1)]
    [InlineData(403, 1)]
    [InlineData(404, 2)]
    [InlineData(429, 1)]
    [InlineData(503, 1)]
    public async Task OptionalHttpFailures_PreserveCompleteSyncAndCreditsWithoutRepeatedRequests(int status, int lookups)
    {
        var cooldown = new SpotifyCooldown(TimeProvider.System);
        var handler = new RoutingHandler((request, _) =>
        {
            var response = RequiredRead(request.RequestUri!) ?? Json("""{"error":{"message":"Unavailable"}}""", (HttpStatusCode)status);
            if ((int)response.StatusCode == 429)
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(1));
            return Task.FromResult(response);
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.spotify.com") };
        var api = CreateApi(http, cooldown);

        var result = await api.GetPlaylistSyncReadSnapshotAsync("token", "playlist", CancellationToken.None);

        Assert.True(result.IsComplete);
        Assert.Equal([0, 1], result.Items.Select(item => item.Position));
        Assert.Equal(lookups, handler.ArtistIds.Count());
        Assert.All(result.Items, item => Assert.All(item.Track!.ArtistIdentities, credit =>
        {
            Assert.Null(credit.ArtworkUrl);
            Assert.Equal($"https://open.spotify.com/artist/{credit.ExternalId}", credit.ProfileUrl);
        }));
        if (status == 429)
        {
            var requestCount = handler.Requests.Count;
            var failure = await Assert.ThrowsAsync<PlatformApiException>(() => api.SearchTracksAsync("other-token", "Song", 10, CancellationToken.None));
            Assert.Equal("spotify_rate_limited", failure.Code);
            Assert.Equal(requestCount, handler.Requests.Count);
            Assert.True(cooldown.NotBefore > DateTimeOffset.UtcNow.AddMinutes(59));
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("null-images")]
    [InlineData("null-image")]
    [InlineData("wrong-id")]
    [InlineData("malformed")]
    [InlineData("transport")]
    [InlineData("timeout")]
    public async Task OptionalMetadataProblems_DoNotFailSearchOrUseAlbumArtwork(string problem)
    {
        var handler = new RoutingHandler((request, _) =>
        {
            if (RequiredRead(request.RequestUri!) is { } read) return Task.FromResult(read);
            return problem switch
            {
                "missing" => Task.FromResult(Json($$"""{"id":"{{request.RequestUri!.Segments.Last()}}","images":[]}""")),
                "null-images" => Task.FromResult(Json($$"""{"id":"{{request.RequestUri!.Segments.Last()}}","images":null}""")),
                "null-image" => Task.FromResult(Json($$"""{"id":"{{request.RequestUri!.Segments.Last()}}","images":[null]}""")),
                "wrong-id" => Task.FromResult(Artist("unrelated")),
                "malformed" => Task.FromResult(Json("not json")),
                "transport" => Task.FromException<HttpResponseMessage>(new HttpRequestException("Connection failed")),
                _ => Task.FromException<HttpResponseMessage>(new TaskCanceledException("HTTP timeout"))
            };
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.spotify.com") };

        var tracks = await ReadAsync(CreateApi(http), "search");

        Assert.Equal(2, tracks.Count);
        Assert.All(tracks, track => Assert.All(track.ArtistIdentities, credit => Assert.Null(credit.ArtworkUrl)));
        Assert.Equal(problem is "malformed" or "transport" or "timeout" ? 1 : 2, handler.ArtistIds.Count());
    }

    [Theory]
    [InlineData("search")]
    [InlineData("playlist")]
    [InlineData("sync")]
    public async Task CallerCancellationDuringOptionalLookup_Propagates(string ingress)
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new RoutingHandler((request, ct) =>
        {
            if (RequiredRead(request.RequestUri!) is { } response) return Task.FromResult(response);
            cancellation.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Artist("first"));
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.spotify.com") };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReadAsync(CreateApi(http), ingress, cancellation.Token));

        Assert.Single(handler.ArtistIds);
    }

    [Fact]
    public async Task ArtistRequestBudget_DoesNotTruncatePlaylistCredits()
    {
        var tracks = Enumerable.Range(0, 60).Select(index => Track($"track{index}",
            [new { id = $"artist{index}", name = $"Artist {index}" }])).ToArray();
        var handler = new RoutingHandler((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.Contains("/items")
            ? (request.RequestUri.Query.Contains("offset=0") ? Page(tracks.Take(50).ToArray(), "next", tracks.Length)
                : Page(tracks.Skip(50).ToArray(), null, tracks.Length)) : Artist(request.RequestUri.Segments.Last())));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.spotify.com") };

        var result = await CreateApi(http).GetPlaylistItemsAsync("token", "playlist", CancellationToken.None);

        Assert.Equal(60, result.Count);
        Assert.Equal(50, handler.ArtistIds.Count());
        Assert.All(result.Take(50), track => Assert.NotNull(Assert.Single(track.ArtistIdentities).ArtworkUrl));
        Assert.All(result.Skip(50), track => Assert.Null(Assert.Single(track.ArtistIdentities).ArtworkUrl));
    }

    [Fact]
    public async Task ForbiddenLookup_DoesNotPoisonAnotherUsersOperation()
    {
        var handler = new RoutingHandler((request, _) => Task.FromResult(RequiredRead(request.RequestUri!)
            ?? (request.Headers.Authorization!.Parameter == "token" ? Json("{}", HttpStatusCode.Forbidden)
                : Artist(request.RequestUri!.Segments.Last()))));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.spotify.com") };
        var api = CreateApi(http);

        var first = await api.SearchTracksAsync("token", "Song", 10, CancellationToken.None);
        var second = await api.SearchTracksAsync("other-token", "Song", 10, CancellationToken.None);

        Assert.All(first, track => Assert.All(track.ArtistIdentities, credit => Assert.Null(credit.ArtworkUrl)));
        Assert.All(second, track => Assert.All(track.ArtistIdentities, credit => Assert.NotNull(credit.ArtworkUrl)));
        Assert.Equal(["first", "first", "second"], handler.ArtistIds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MatchingSearch_EnrichesAfterAllQueriesAndDeduplicatesAcrossResults(bool rateLimited)
    {
        var searches = 0;
        var handler = new RoutingHandler((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/api/token" => Json("""{"access_token":"token","expires_in":3600}"""),
            "/v1/search" => Json(JsonSerializer.Serialize(new { tracks = new { items = new[] {
                Track($"track{++searches}", Credits, searches == 1 ? "GBABC2400001" : "USABC2400001") } } })),
            _ => rateLimited ? Json("""{"error":{"message":"Wait"}}""", HttpStatusCode.TooManyRequests)
                : Artist(request.RequestUri.Segments.Last())
        }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.spotify.com") };
        var options = Options.Create(new SpotifyOptions { ClientId = "client", ClientSecret = "secret" });
        var api = CreateApi(http);
        var provider = new SpotifySearchProvider(api,
            new SpotifyCatalogTokenProvider(api, options, TimeProvider.System, new SpotifyCatalogTokenCache()));
        var observation = new TrackObservation
        {
            SourceType = "youtube", ExternalId = "video", Title = "Song", Artist = "First credited name, Second credited name", MatchStatus = "pending",
            RawMetadata = JsonSerializer.Serialize(new TrackObservationMetadata { Isrc = "USABC2400001" })
        };

        var candidates = await provider.SearchAsync(observation, CancellationToken.None);

        Assert.Equal(2, searches);
        Assert.NotEmpty(candidates);
        Assert.Equal(rateLimited ? new[] { "first" } : ["first", "second"], handler.ArtistIds);
        Assert.All(candidates, candidate =>
        {
            Assert.Equal(["First credited name", "Second credited name"], candidate.ArtistCredits);
            Assert.Equal(candidate.ArtistCredits, candidate.ArtistIdentities.Select(credit => credit.Name));
            Assert.All(candidate.ArtistIdentities, credit => Assert.Equal(rateLimited ? null : $"https://images/{credit.ExternalId}", credit.ArtworkUrl));
        });
        var lastSearch = handler.Requests.FindLastIndex(request => request.Path == "/v1/search");
        var firstArtist = handler.Requests.FindIndex(request => request.Path.StartsWith("/v1/artists/"));
        Assert.True(firstArtist > lastSearch);
    }

    private static SpotifyApiClient CreateApi(HttpClient http, SpotifyCooldown? cooldown = null)
        => new(http, Options.Create(new SpotifyOptions { ClientId = "client", ClientSecret = "secret" }),
            new NoDelay(), cooldown ?? new SpotifyCooldown(TimeProvider.System));

    private static async Task<IReadOnlyList<SpotifyTrackSnapshot>> ReadAsync(
        SpotifyApiClient api, string ingress, CancellationToken ct = default)
    {
        if (ingress == "search") return await api.SearchTracksAsync("token", "Song", 10, ct);
        if (ingress == "playlist") return await api.GetPlaylistItemsAsync("token", "playlist", ct);
        var snapshot = await api.GetPlaylistSyncReadSnapshotAsync("token", "playlist", ct);
        Assert.True(snapshot.IsComplete);
        Assert.Equal([0, 1], snapshot.Items.Select(item => item.Position));
        return snapshot.Items.Select(item => item.Track!).ToArray();
    }

    private static HttpResponseMessage? RequiredRead(Uri uri)
    {
        if (uri.AbsolutePath == "/v1/search")
            return Json(JsonSerializer.Serialize(new { tracks = new { items = new[] { Track("track1", Credits), Track("track2", Credits) } } }));
        if (uri.AbsolutePath == "/v1/playlists/playlist") return Json(Details);
        if (uri.AbsolutePath == "/v1/playlists/playlist/items")
            return uri.Query.Contains("offset=0") ? Page([Track("track1", Credits)], "next", 2)
                : Page([Track("track2", Credits)], null, 2);
        return null;
    }

    // Track artists deliberately contain only the SimplifiedArtistObject fields.
    private static object Track(string id, object[] artists, string isrc = "USABC2400001") => new
    {
        type = "track", id, name = "Song", artists, duration_ms = 180000,
        external_ids = new { isrc }, album = new { name = "Album", images = new[] { new { url = "https://images/album" } } }
    };

    private static HttpResponseMessage Page(object[] tracks, string? next, int total)
        => Json(JsonSerializer.Serialize(new { items = tracks.Select(item => new { is_local = false, item }), next, total }));

    private static HttpResponseMessage Artist(string id) => Json(JsonSerializer.Serialize(new
    {
        id, name = "Different canonical name", images = new[] { new { url = $"https://images/{id}" } },
        external_urls = new { spotify = $"https://open.spotify.com/artist/{id}" }
    }));

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class RoutingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<(string Path, string? Authorization)> Requests { get; } = [];
        public IEnumerable<string> ArtistIds => Requests.Where(request => request.Path.StartsWith("/v1/artists/"))
            .Select(request => request.Path.Split('/').Last());
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!.AbsolutePath, request.Headers.Authorization?.ToString()));
            return respond(request, cancellationToken);
        }
    }

    private sealed class NoDelay : ISpotifyRetryDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
