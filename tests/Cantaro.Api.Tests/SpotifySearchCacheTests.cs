using Cantaro.Api.Data;
using Cantaro.Api.Configuration;
using Cantaro.Api.Models;
using Cantaro.Api.Services.Spotify;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class SpotifySearchCacheTests
{
    [Fact]
    public async Task DifferentMatchingWorkersReuseCatalogueResultsEvenDuringCooldownWithoutTokenRequests()
    {
        await using var fixture = await Fixture.CreateAsync();
        var handler = new CatalogHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.spotify.com") };
        var options = Options.Create(new SpotifyOptions { ClientId = "test-client", ClientSecret = "test-secret" });
        async Task<string> SearchWithFreshWorker(bool cooldownActive)
        {
            var cooldown = new SpotifyCooldown(TimeProvider.System);
            if (cooldownActive) cooldown.Record(new RetryConditionHeaderValue(TimeSpan.FromHours(24)));
            var api = new SpotifyApiClient(http, options, new SpotifyRetryDelay(), cooldown);
            var tokens = new SpotifyCatalogTokenProvider(api, options, TimeProvider.System, new SpotifyCatalogTokenCache());
            var provider = new SpotifySearchProvider(api, tokens, searchCache: fixture.Cache());
            var results = await provider.SearchAsync(new TrackObservation
            {
                Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = Guid.NewGuid().ToString(),
                Title = "Heaven", Artist = "Rival", DurationSeconds = 253, MatchStatus = "pending"
            }, default);
            return Assert.Single(results).ExternalId;
        }
        Assert.Equal("spotify-id", await SearchWithFreshWorker(false));
        Assert.Equal("spotify-id", await SearchWithFreshWorker(true));
        Assert.Equal(1, handler.SearchRequests);
        Assert.Equal(1, handler.TokenRequests);
    }

    [Fact]
    public async Task CompletedQueriesSurviveServiceRestartAndAreSharedAcrossWorkers()
    {
        await using var fixture = await Fixture.CreateAsync();
        var sends = 0;
        Task<IReadOnlyList<SpotifyTrackSnapshot>> Search(CancellationToken _) { sends++; return Task.FromResult(Tracks); }
        var first = await fixture.Cache().GetOrSearchAsync("track:Heaven artist:Rival", 10, Search, default);
        // A new DI container and cache instance still reuse the database result.
        await using var restarted = fixture.NewServices();
        var cache = new SpotifySearchCache(restarted.GetRequiredService<IServiceScopeFactory>(), fixture.Clock);
        var second = await cache.GetOrSearchAsync("track:Heaven artist:Rival", 10, Search, default);
        Assert.Equal(1, sends);
        Assert.Equal(first.Single().Id, second.Single().Id);
        Assert.Equal(Tracks.Single().ArtistNames, second.Single().ArtistNames);
    }

    [Fact]
    public async Task ConcurrentWorkersSendOneSearch()
    {
        await using var fixture = await Fixture.CreateAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sends = 0;
        async Task<IReadOnlyList<SpotifyTrackSnapshot>> Search(CancellationToken _)
        {
            Interlocked.Increment(ref sends);
            started.TrySetResult();
            await release.Task;
            return Tracks;
        }
        var first = fixture.Cache().GetOrSearchAsync("shared-query", 10, Search, default);
        await started.Task;
        var second = fixture.Cache().GetOrSearchAsync("shared-query", 10, Search, default);
        release.SetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(1, sends);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 7)]
    public async Task SuccessfulResultsExpireWithoutRepeatedRequestsBeforeExpiry(bool empty, int days)
    {
        await using var fixture = await Fixture.CreateAsync();
        var sends = 0;
        Task<IReadOnlyList<SpotifyTrackSnapshot>> Search(CancellationToken _)
        { sends++; return Task.FromResult(empty ? (IReadOnlyList<SpotifyTrackSnapshot>)[] : Tracks); }
        await fixture.Cache().GetOrSearchAsync("query", 10, Search, default);
        fixture.Clock.Now += TimeSpan.FromDays(days) - TimeSpan.FromSeconds(1);
        await fixture.Cache().GetOrSearchAsync("query", 10, Search, default);
        Assert.Equal(1, sends);
        fixture.Clock.Now += TimeSpan.FromSeconds(2);
        await fixture.Cache().GetOrSearchAsync("query", 10, Search, default);
        Assert.Equal(2, sends);
    }

    [Fact]
    public async Task FailedAndCancelledSearchesAreNotRecordedAsNoMatch()
    {
        await using var fixture = await Fixture.CreateAsync();
        var cache = fixture.Cache();
        await Assert.ThrowsAsync<HttpRequestException>(() => cache.GetOrSearchAsync("query", 10,
            _ => throw new HttpRequestException("429"), default));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.GetOrSearchAsync("query", 10,
            _ => throw new OperationCanceledException(), default));
        var sends = 0;
        var result = await cache.GetOrSearchAsync("query", 10,
            _ => { sends++; return Task.FromResult(Tracks); }, default);
        Assert.Equal(1, sends);
        Assert.Single(result);
    }

    [Fact]
    public async Task CancellationAfterSuccessfulResponseStillRetainsTheCompletedQuery()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Cache().GetOrSearchAsync("query", 10,
            _ => { cancellation.Cancel(); return Task.FromResult(Tracks); }, cancellation.Token));
        var result = await fixture.Cache().GetOrSearchAsync("query", 10,
            _ => throw new InvalidOperationException("Completed search must not repeat"), default);
        Assert.Single(result);
    }

    [Fact]
    public async Task ExpiredUnrelatedQueriesAreRemovedDuringRefresh()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Cache().GetOrSearchAsync("old query", 10, _ => Task.FromResult(Tracks), default);
        fixture.Clock.Now += TimeSpan.FromDays(8);
        await fixture.Cache().GetOrSearchAsync("new query", 10, _ => Task.FromResult(Tracks), default);
        await using var services = fixture.NewServices();
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.SpotifySearchCacheEntries.CountAsync());
    }

    [Fact]
    public async Task RestartAfterLaterQueryFailsReusesEarlierCompletedQuery()
    {
        await using var fixture = await Fixture.CreateAsync();
        var sends = 0;
        Task<IReadOnlyList<SpotifyTrackSnapshot>> Empty(CancellationToken _) { sends++; return Task.FromResult<IReadOnlyList<SpotifyTrackSnapshot>>([]); }
        await fixture.Cache().GetOrSearchAsync("first hypothesis", 10, Empty, default);
        await Assert.ThrowsAsync<HttpRequestException>(() => fixture.Cache().GetOrSearchAsync("second hypothesis", 10,
            _ => throw new HttpRequestException("quota"), default));
        await fixture.Cache().GetOrSearchAsync("first hypothesis", 10, Empty, default);
        await fixture.Cache().GetOrSearchAsync("second hypothesis", 10, Empty, default);
        Assert.Equal(2, sends);
    }

    [Fact]
    public async Task DifferentQueriesAndResultLimitsDoNotShareResults()
    {
        await using var fixture = await Fixture.CreateAsync();
        var sends = 0;
        Task<IReadOnlyList<SpotifyTrackSnapshot>> Search(CancellationToken _) { sends++; return Task.FromResult(Tracks); }
        await fixture.Cache().GetOrSearchAsync("original", 10, Search, default);
        await fixture.Cache().GetOrSearchAsync("remix", 10, Search, default);
        await fixture.Cache().GetOrSearchAsync("original", 20, Search, default);
        Assert.Equal(3, sends);
    }

    private static readonly IReadOnlyList<SpotifyTrackSnapshot> Tracks =
    [new("spotify-id", "Heaven", "Rival", ["Rival"], null, null,
        "https://open.spotify.com/track/spotify-id", null, null, 253, null, 0)];

    private sealed class CatalogHandler : HttpMessageHandler
    {
        public int SearchRequests { get; private set; }
        public int TokenRequests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string json;
            if (request.RequestUri!.AbsolutePath == "/api/token")
            {
                TokenRequests++;
                json = """{"access_token":"app-token","expires_in":3600,"token_type":"Bearer"}""";
            }
            else
            {
                Assert.Equal("/v1/search", request.RequestUri.AbsolutePath);
                SearchRequests++;
                json = """{"tracks":{"items":[{"type":"track","id":"spotify-id","name":"Heaven","duration_ms":253000,"artists":[{"name":"Rival"}]}]}}""";
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Fixture(string path, ServiceProvider services) : IAsyncDisposable
    {
        public Clock Clock { get; } = new();
        public SpotifySearchCache Cache() => new(services.GetRequiredService<IServiceScopeFactory>(), Clock);
        public ServiceProvider NewServices() => CreateServices(path);

        private static ServiceProvider CreateServices(string path)
            => new ServiceCollection().AddDbContext<ApplicationDbContext>(options => options.UseSqlite($"Data Source={path}"))
                .BuildServiceProvider();

        public static async Task<Fixture> CreateAsync()
        {
            var path = Path.Combine(Path.GetTempPath(), $"cantaro-search-cache-{Guid.NewGuid():N}.db");
            var services = CreateServices(path);
            await using var scope = services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync();
            return new Fixture(path, services);
        }

        public async ValueTask DisposeAsync()
        {
            await services.DisposeAsync();
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-shm", "-wal" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }
}
