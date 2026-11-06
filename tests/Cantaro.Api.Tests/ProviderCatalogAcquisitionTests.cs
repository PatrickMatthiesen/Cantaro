using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cantaro.Api.Configuration;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Cantaro.Api.Services.Spotify;
using Google.Apis.Http;
using Google.Apis.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed partial class ProviderCatalogAcquisitionTests
{
    [Fact]
    public async Task SpotifyAppSearchAndExactResponsesCreateReusablePublicCatalogRecords()
    {
        await using var fixture = await Fixture.CreateAsync();
        var provider = fixture.SpotifyProvider();
        var candidates = await provider.SearchAsync(new TrackObservation
        {
            Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = "source-video",
            Title = "Song (Live)", Artist = "Artist", MatchStatus = TrackMatchingStatuses.Pending
        }, default);
        var candidate = Assert.Single(candidates);
        Assert.NotNull(candidate.ProviderCatalogItemId);
        Assert.NotNull(candidate.ProviderCatalogRevision);
        var item = await fixture.Store.FindAsync("spotify", "spotify-id", default);
        Assert.Equal(candidate.ProviderCatalogItemId, item!.Id);
        var facts = ProviderCatalogPolicy.ReadFacts(item, DateTimeOffset.UtcNow);
        Assert.Equal("Song (Live)", facts!.Title);
        Assert.Equal(TrackVersionFlags.Live, facts.VersionFlags);
        Assert.Empty(await fixture.ReadSourcesAsync());

        var exact = await fixture.Acquisition.EnsureSpotifyAsync("exact-id", default);
        Assert.NotNull(exact);
        Assert.Equal("exact-id", exact.ExternalId);
        Assert.Equal(CatalogVisibility.Public, exact.VisibilityState);
        Assert.Equal(TrackVersionFlags.Remix,
            ProviderCatalogPolicy.ReadFacts(exact, DateTimeOffset.UtcNow)!.VersionFlags);
        Assert.Empty(await fixture.ReadSourcesAsync());
    }

    [Fact]
    public async Task LegacySpotifyCachePayloadCannotMintProofOrRenewExpiredCatalogLease()
    {
        await using var fixture = await Fixture.CreateAsync();
        var oldTime = DateTimeOffset.UtcNow.AddDays(-60);
        var oldItem = new ProviderCatalogItem
        {
            Id = Guid.NewGuid(), SourceType = "spotify", ExternalId = "legacy-id",
            FactsJson = "{\"Title\":\"Old private-looking cache value\"}",
            VerificationMethod = CatalogVerificationMethods.SpotifyApplication,
            VerifiedAt = oldTime, RevalidateAfter = oldTime.AddDays(30),
            VisibilityState = CatalogVisibility.Public, Revision = 2
        };
        await fixture.AddCatalogItemAsync(oldItem);
        var query = "track:\"Legacy\" artist:\"Artist\"";
        var oldKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"catalog-v1\n10\n{query}")));
        await fixture.AddSearchCacheAsync(oldKey, JsonSerializer.Serialize(new[]
        {
            new SpotifyTrackSnapshot("legacy-id", "Legacy", "Artist", ["Artist"], null, null,
                "https://open.spotify.com/track/legacy-id", null, null, 100, null, 0)
        }));
        fixture.Handler.EmptySearchResults = true;

        var result = await fixture.SpotifyProvider().SearchAsync(new TrackObservation
        {
            Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = "source-video",
            Title = "Legacy", Artist = "Artist", MatchStatus = TrackMatchingStatuses.Pending
        }, default);

        Assert.Empty(result);
        Assert.Equal(2, fixture.Handler.SearchRequests);
        var legacyPayload = JsonSerializer.Deserialize<SpotifyTrackSnapshot[]>(
            (await fixture.ReadSearchCacheAsync(oldKey))!.ResultsJson)!;
        Assert.Null(Assert.Single(legacyPayload).ProviderCatalogItemId);
        var after = await fixture.Store.FindAsync("spotify", "legacy-id", default);
        Assert.Equal(oldTime, after!.VerifiedAt);
        Assert.Equal(oldTime.AddDays(30), after.RevalidateAfter);
        Assert.Equal(2, after.Revision);
    }

    [Fact]
    public async Task SpotifySearchCacheDropsReferencesAfterCatalogWithdrawal()
    {
        await using var fixture = await Fixture.CreateAsync();
        var provider = fixture.SpotifyProvider();
        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = "source-video",
            Title = "Song (Live)", Artist = "Artist", MatchStatus = TrackMatchingStatuses.Pending
        };
        var first = Assert.Single(await provider.SearchAsync(observation, default));
        var before = await fixture.Store.FindAsync("spotify", first.ExternalId, default);
        Assert.NotNull(before);
        Assert.Equal(before.Id, first.ProviderCatalogItemId);

        await fixture.Store.MarkWithdrawnAsync("spotify", first.ExternalId, default);
        fixture.Handler.EmptySearchResults = true;
        var after = await provider.SearchAsync(observation, default);

        Assert.Empty(after);
        Assert.Equal(2, fixture.Handler.SearchRequests);
        var withdrawn = await fixture.Store.FindAsync("spotify", first.ExternalId, default);
        Assert.Equal(CatalogVisibility.Withdrawn, withdrawn!.VisibilityState);
        Assert.True(withdrawn.Revision > before.Revision);
        Assert.Equal(before.VerifiedAt, withdrawn.VerifiedAt);
        Assert.Null(ProviderCatalogPolicy.ReadFacts(withdrawn, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task CachedSearchUsesRefreshedPublicFactsWithoutSearchingOrRenewingTheLease()
    {
        await using var fixture = await Fixture.CreateAsync();
        var provider = fixture.SpotifyProvider();
        var observation = new TrackObservation { Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = "source-video",
            Title = "Song (Live)", Artist = "Artist", MatchStatus = TrackMatchingStatuses.Pending };
        var first = Assert.Single(await provider.SearchAsync(observation, default));
        var original = (await fixture.Store.FindAsync("spotify", first.ExternalId, default))!;
        var facts = ProviderCatalogPolicy.ReadFacts(original, DateTimeOffset.UtcNow)!;
        var refreshed = await fixture.Store.RecordVerifiedAsync("spotify", first.ExternalId,
            CatalogVerificationMethods.SpotifyApplication, new ProviderCatalogFacts
            {
                Title = facts.Title, Artist = facts.Artist, Isrc = facts.Isrc, DurationSeconds = facts.DurationSeconds,
                ArtistCredits = facts.ArtistCredits, VersionFlags = facts.VersionFlags, ThumbnailUrl = "https://public.example/new-art.jpg"
            }, default);
        var requests = fixture.Handler.SearchRequests;
        fixture.Handler.EmptySearchResults = true;

        var current = Assert.Single(await provider.SearchAsync(observation, default));

        Assert.Equal(requests, fixture.Handler.SearchRequests);
        Assert.Equal(refreshed.Revision, current.ProviderCatalogRevision);
        var stored = (await fixture.Store.FindAsync("spotify", first.ExternalId, default))!;
        Assert.Equal(refreshed.VerifiedAt, stored.VerifiedAt);
        Assert.Equal(refreshed.RevalidateAfter, stored.RevalidateAfter);
        Assert.Equal("https://public.example/new-art.jpg", ProviderCatalogPolicy.ReadFacts(stored, DateTimeOffset.UtcNow)!.ThumbnailUrl);
    }

    [Theory]
    [InlineData("unlisted")]
    [InlineData("private")]
    [InlineData("absent")]
    public async Task YouTubeExactCheckWithdrawsPublicRecordWhenStatusIsNotExplicitlyPublic(string nextStatus)
    {
        await using var fixture = await Fixture.CreateAsync();
        var account = await fixture.AddYouTubeAccountAsync();
        var handler = new YouTubeVideosHandler("public");
        using var serviceScope = fixture.OpenScope();
        var service = fixture.YouTubeService(
            serviceScope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), handler);

        var first = await service.GetPublicCatalogItemAsync(account, "youtube-id", default);
        Assert.NotNull(first);
        Assert.Equal(CatalogVisibility.Public, first.VisibilityState);
        Assert.Equal("https://www.youtube.com/watch?v=youtube-id",
            ProviderCatalogPolicy.ReadFacts(first, DateTimeOffset.UtcNow)!.ProviderUrl);

        handler.Status = nextStatus;
        var result = await service.GetPublicCatalogItemAsync(account, "youtube-id", default);

        Assert.Null(result);
        var withdrawn = await fixture.Store.FindAsync("youtube", "youtube-id", default);
        Assert.Equal(CatalogVisibility.Withdrawn, withdrawn!.VisibilityState);
        Assert.True(withdrawn.Revision > first.Revision);
        Assert.False(ProviderCatalogPolicy.IsUsable(withdrawn, DateTimeOffset.UtcNow));
        Assert.Equal("A song (Live)", JsonSerializer.Deserialize<ProviderCatalogFacts>(withdrawn.FactsJson)!.Title);
    }

    private sealed class Fixture(string path, ServiceProvider services, CatalogHandler handler) : IAsyncDisposable
    {
        public CatalogHandler Handler { get; } = handler;
        public ProviderCatalogStore Store => services.GetRequiredService<ProviderCatalogStore>();
        public ProviderCatalogAcquisition Acquisition => services.GetRequiredService<ProviderCatalogAcquisition>();

        public SpotifySearchProvider SpotifyProvider()
        {
            var api = services.GetRequiredService<SpotifyApiClient>();
            return new SpotifySearchProvider(api, services.GetRequiredService<SpotifyCatalogTokenProvider>(),
                searchCache: services.GetRequiredService<SpotifySearchCache>(),
                catalogAcquisition: Acquisition);
        }

        public async Task AddCatalogItemAsync(ProviderCatalogItem item)
        {
            await using var scope = services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().ProviderCatalogItems.Add(item);
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().SaveChangesAsync();
        }

        public async Task AddSearchCacheAsync(string key, string json)
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.SpotifySearchCacheEntries.Add(new SpotifySearchCacheEntry
            { Key = key, ResultsJson = json, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) });
            await db.SaveChangesAsync();
        }

        public async Task<SpotifySearchCacheEntry?> ReadSearchCacheAsync(string key)
        {
            await using var scope = services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                .SpotifySearchCacheEntries.AsNoTracking().SingleOrDefaultAsync(item => item.Key == key);
        }

        public IServiceScope OpenScope() => services.CreateScope();

        public async Task<TrackSourceId[]> ReadSourcesAsync()
        {
            await using var scope = services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().TrackSourceIds.ToArrayAsync();
        }

        public async Task<PlatformAccountContext> AddYouTubeAccountAsync(int userId = 8901, int accountId = 89)
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = TestUserFactory.Create(userId, $"catalog-youtube-{userId}@example.test");
            var account = new ConnectedServiceAccount
            {
                Id = accountId, UserId = user.Id, Service = "youtube", ExternalAccountId = "channel",
                ConnectionState = "connected"
            };
            db.AddRange(user, account);
            await db.SaveChangesAsync();
            return new PlatformAccountContext(user.Id, account.Id, account.ExternalAccountId);
        }

        public YouTubeService YouTubeService(ApplicationDbContext db, Google.Apis.Http.IHttpClientFactory youtubeHandler)
        {
            return new YouTubeService(new ConfigurationBuilder().Build(), db,
                new TokenEncryptionService(new EphemeralDataProtectionProvider()), NullLogger<YouTubeService>.Instance,
                (_, _) => Task.FromResult(new Google.Apis.YouTube.v3.YouTubeService(new BaseClientService.Initializer
                { ApplicationName = "Catalog acquisition tests", HttpClientFactory = youtubeHandler })), Store);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var path = Path.Combine(Path.GetTempPath(), $"cantaro-catalog-acquisition-{Guid.NewGuid():N}.db");
            var handler = new CatalogHandler();
            var options = Options.Create(new SpotifyOptions { ClientId = "client", ClientSecret = "secret" });
            var services = new ServiceCollection()
                .AddDbContext<ApplicationDbContext>(builder => builder.UseSqlite($"Data Source={path}"))
                .AddSingleton(TimeProvider.System)
                .AddSingleton<ISpotifyRetryDelay, SpotifyRetryDelay>()
                .AddSingleton(new SpotifyCooldown(TimeProvider.System))
                .AddSingleton(new HttpClient(handler) { BaseAddress = new Uri("https://api.spotify.com") })
                .AddSingleton(options)
                .AddSingleton(options.Value)
                .AddSingleton(Options.Create(new TrackMatchingOptions()))
                .AddSingleton<SpotifyCatalogTokenCache>()
                .AddSingleton<SpotifySearchCache>()
                .AddSingleton<SpotifyApiClient>(provider => new SpotifyApiClient(
                    provider.GetRequiredService<HttpClient>(), options,
                    provider.GetRequiredService<ISpotifyRetryDelay>(), provider.GetRequiredService<SpotifyCooldown>()))
                .AddScoped<SpotifyCatalogTokenProvider>()
                .AddScoped<ProviderCatalogStore>()
                .AddScoped<ProviderCatalogAcquisition>()
                .BuildServiceProvider();
            await using (var scope = services.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync();
            return new Fixture(path, services, handler);
        }

        public async ValueTask DisposeAsync()
        {
            await services.DisposeAsync();
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-shm", "-wal" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    private sealed class CatalogHandler : HttpMessageHandler
    {
        public int SearchRequests { get; private set; }
        public bool EmptySearchResults { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            string json;
            if (path == "/api/token")
                json = """{"access_token":"app-token","expires_in":3600,"token_type":"Bearer"}""";
            else if (path == "/v1/search")
            {
                SearchRequests++;
                json = EmptySearchResults
                    ? """{"tracks":{"items":[]}}"""
                    : """{"tracks":{"items":[{"type":"track","id":"spotify-id","name":"Song (Live)","duration_ms":123000,"artists":[{"name":"Artist"}],"album":{"name":"Album","images":[{"url":"https://image.invalid/cover"}]},"external_urls":{"spotify":"https://open.spotify.com/track/spotify-id"}}]}}""";
            }
            else if (path == "/v1/tracks/exact-id")
                json = """{"type":"track","id":"exact-id","name":"Song (Remix)","duration_ms":123000,"artists":[{"name":"Artist"}]}""";
            else if (path.StartsWith("/v1/artists/", StringComparison.Ordinal))
                json = """{"id":"artist","name":"Artist"}""";
            else
                throw new InvalidOperationException($"Unexpected Spotify request: {request.RequestUri}");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class YouTubeVideosHandler(string status) : HttpMessageHandler, Google.Apis.Http.IHttpClientFactory
    {
        public string Status { get; set; } = status;
        public ConfigurableHttpClient CreateHttpClient(CreateHttpClientArgs args)
            => new(new ConfigurableMessageHandler(this));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.EndsWith("/videos", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
            var items = Status == "absent" ? Array.Empty<object>() : new object[]
            {
                new
                {
                    id = "youtube-id",
                    snippet = new { title = "Artist - A song (Live)", channelTitle = "Artist", description = "Must not persist" },
                    contentDetails = new { duration = "PT2M" },
                    status = new { privacyStatus = Status }
                }
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(JsonSerializer.Serialize(new { items }), Encoding.UTF8, "application/json") });
        }
    }
}
