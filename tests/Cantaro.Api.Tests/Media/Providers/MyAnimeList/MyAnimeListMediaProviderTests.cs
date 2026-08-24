using System.Net;
using System.Text;
using Cantaro.Api.Configuration;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class MyAnimeListMediaProviderTests
{
    [Fact]
    public async Task SearchAsync_QualifiesCollidingAnimeAndMangaIds()
    {
        await using var fixture = await ProviderFixture.CreateAsync(request =>
            request.RequestUri!.AbsolutePath == "/v2/anime"
                ? JsonResponse(SearchResponse(42, "Anime title", "tv", 12))
                : JsonResponse(SearchResponse(42, "Manga title", "manga", 0, 100, 10)));

        var results = await fixture.Provider.SearchAsync(
            1,
            new MediaCatalogSearchRequest { Query = "title", Limit = 10 },
            CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, result => result.ProviderMediaId == "anime:42" && result.MediaKind == MediaKinds.Anime);
        Assert.Contains(results, result => result.ProviderMediaId == "manga:42" && result.MediaKind == MediaKinds.Manga);
    }

    [Fact]
    public async Task ImportLibraryAsync_MapsAnimeAndMangaStateWithoutRawMetadata()
    {
        await using var fixture = await ProviderFixture.CreateAsync(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/animelist", StringComparison.Ordinal))
            {
                return JsonResponse("""
                    {"data":[{"node":{"id":1,"title":"Anime","alternative_titles":{"en":"Anime EN","ja":"アニメ"},"media_type":"tv","status":"finished_airing","num_episodes":12},"list_status":{"status":"watching","score":8,"num_episodes_watched":3,"is_rewatching":true,"updated_at":"2026-08-23T10:00:00Z"}}],"paging":{}}
                    """);
            }

            return JsonResponse("""
                {"data":[{"node":{"id":1,"title":"Manga","media_type":"manga","status":"currently_publishing","num_chapters":50,"num_volumes":7},"list_status":{"status":"reading","score":7,"num_chapters_read":11,"num_volumes_read":2,"is_rereading":false,"updated_at":"2026-08-23T11:00:00Z"}}],"paging":{}}
                """);
        }, connectedUserId: 101);

        var import = await fixture.Provider.ImportLibraryAsync(101, CancellationToken.None);

        Assert.Equal(2, import.Items.Count);
        var anime = Assert.Single(import.Items, item => item.ProviderMediaId == "anime:1");
        Assert.Equal(MediaLibraryStatuses.Repeating, anime.Status);
        Assert.Equal(80m, anime.Score);
        Assert.Equal(12, anime.ReleasedCount);
        Assert.Equal(12, anime.TotalKnownCount);
        var manga = Assert.Single(import.Items, item => item.ProviderMediaId == "manga:1");
        Assert.Equal(MediaLibraryStatuses.Current, manga.Status);
        Assert.Equal(11, manga.ProgressChapters);
        Assert.Equal(2, manga.ProgressVolumes);
        Assert.Null(manga.ReleasedCount);
        Assert.Equal(50, manga.TotalKnownCount);
    }

    [Fact]
    public async Task UpdateStatusAsync_ClearsRepeatFlagForNormalStatus()
    {
        await using var fixture = await ProviderFixture.CreateAsync(
            _ => JsonResponse("{\"updated_at\":\"2026-08-23T12:00:00Z\"}"),
            connectedUserId: 102);

        var result = await fixture.Provider.UpdateStatusAsync(
            102,
            new MediaStatusUpdateRequest
            {
                ProviderMediaId = "anime:42",
                Status = MediaLibraryStatuses.Current
            },
            CancellationToken.None);

        Assert.Equal("anime:42", result.ProviderMediaId);
        Assert.Equal(HttpMethod.Put, Assert.Single(fixture.Handler.Methods));
        var body = Assert.Single(fixture.Handler.Bodies);
        Assert.Contains("status=watching", body);
        Assert.Contains("is_rewatching=false", body);
    }

    [Fact]
    public async Task UpdateScoreAsync_ConvertsCanonicalScoreToMalTenPointScale()
    {
        await using var fixture = await ProviderFixture.CreateAsync(
            _ => JsonResponse("{}"),
            connectedUserId: 103);

        await fixture.Provider.UpdateScoreAsync(
            103,
            new MediaScoreUpdateRequest { ProviderMediaId = "manga:9", Score = 84m },
            CancellationToken.None);

        Assert.Contains("score=8", Assert.Single(fixture.Handler.Bodies));
    }

    [Fact]
    public async Task SyncLibraryStateAsync_UpsertsCompleteAnimeStateWithOneMediaIdRequest()
    {
        await using var fixture = await ProviderFixture.CreateAsync(
            _ => JsonResponse("{\"updated_at\":\"2026-08-23T12:00:00Z\"}"),
            connectedUserId: 104);

        var result = await fixture.Provider.SyncLibraryStateAsync(
            104,
            new MediaLibraryStateSyncRequest
            {
                ProviderMediaId = "anime:52991",
                Status = MediaLibraryStatuses.Repeating,
                Score = 84m,
                ProgressEpisodes = 17
            },
            CancellationToken.None);

        Assert.Equal("anime:52991", result.ProviderMediaId);
        Assert.Equal(HttpMethod.Put, Assert.Single(fixture.Handler.Methods));
        Assert.Equal("/v2/anime/52991/my_list_status", Assert.Single(fixture.Handler.Paths));
        var body = Assert.Single(fixture.Handler.Bodies);
        Assert.Contains("status=watching", body);
        Assert.Contains("is_rewatching=true", body);
        Assert.Contains("score=8", body);
        Assert.Contains("num_watched_episodes=17", body);
        Assert.DoesNotContain("num_chapters_read", body);
        Assert.DoesNotContain("num_volumes_read", body);
    }

    [Fact]
    public async Task SyncLibraryStateAsync_ClearsMangaScoreAndWritesBothProgressDimensions()
    {
        await using var fixture = await ProviderFixture.CreateAsync(
            _ => JsonResponse("{\"updated_at\":\"2026-08-23T12:00:00Z\"}"),
            connectedUserId: 105);

        await fixture.Provider.SyncLibraryStateAsync(
            105,
            new MediaLibraryStateSyncRequest
            {
                ProviderMediaId = "manga:87610",
                Status = MediaLibraryStatuses.Current,
                Score = null,
                ProgressChapters = 168,
                ProgressVolumes = 23
            },
            CancellationToken.None);

        Assert.Equal("/v2/manga/87610/my_list_status", Assert.Single(fixture.Handler.Paths));
        var body = Assert.Single(fixture.Handler.Bodies);
        Assert.Contains("status=reading", body);
        Assert.Contains("is_rereading=false", body);
        Assert.Contains("score=0", body);
        Assert.Contains("num_chapters_read=168", body);
        Assert.Contains("num_volumes_read=23", body);
        Assert.DoesNotContain("num_watched_episodes", body);
    }

    [Fact]
    public async Task GetTitleDetailsAsync_FinishedAnimeReturnsNormalizedReleaseCounts()
    {
        await using var fixture = await ProviderFixture.CreateAsync(_ => JsonResponse("""
            {"id":52991,"title":"Frieren","alternative_titles":{"en":"Frieren: Beyond Journey's End","ja":"葬送のフリーレン"},"start_date":"2023-09-29","synopsis":"An elf's journey.","media_type":"tv","status":"finished_airing","num_episodes":28,"main_picture":{"large":"https://cdn.example/poster.jpg"}}
            """));

        var details = await fixture.Provider.GetTitleDetailsAsync(1, "anime:52991", CancellationToken.None);

        Assert.NotNull(details);
        Assert.Equal("anime:52991", details.ProviderMediaId);
        Assert.Equal(28, details.ReleasedCount);
        Assert.Equal(28, details.TotalKnownCount);
        Assert.Null(details.NextReleaseAt);
    }

    private static string SearchResponse(
        int id,
        string title,
        string mediaType,
        int episodes,
        int chapters = 0,
        int volumes = 0)
        => $"{{\"data\":[{{\"node\":{{\"id\":{id},\"title\":\"{title}\",\"media_type\":\"{mediaType}\",\"num_episodes\":{episodes},\"num_chapters\":{chapters},\"num_volumes\":{volumes}}}}}],\"paging\":{{}}}}";

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class ProviderFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly string _dataProtectionDirectory;

        private ProviderFixture(
            SqliteConnection connection,
            ApplicationDbContext db,
            MyAnimeListMediaProvider provider,
            RoutingHandler handler,
            string dataProtectionDirectory)
        {
            _connection = connection;
            Db = db;
            Provider = provider;
            Handler = handler;
            _dataProtectionDirectory = dataProtectionDirectory;
        }

        public ApplicationDbContext Db { get; }
        public MyAnimeListMediaProvider Provider { get; }
        public RoutingHandler Handler { get; }

        public static async Task<ProviderFixture> CreateAsync(
            Func<HttpRequestMessage, HttpResponseMessage> responseFactory,
            int? connectedUserId = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
            var db = new ApplicationDbContext(options);
            await db.Database.EnsureCreatedAsync();
            var dataProtectionDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var dataProtection = DataProtectionProvider.Create(new DirectoryInfo(dataProtectionDirectory));
            var encryption = new TokenEncryptionService(dataProtection);
            if (connectedUserId is { } userId)
            {
                var now = DateTime.UtcNow;
                db.Users.Add(TestUserFactory.Create(userId, $"mal-{userId}@example.com"));
                db.ConnectedServiceAccounts.Add(new ConnectedServiceAccount
                {
                    UserId = userId,
                    Service = "myanimelist",
                    ExternalAccountId = userId.ToString(),
                    EncryptedAccessToken = encryption.Encrypt("access-token"),
                    EncryptedRefreshToken = encryption.Encrypt("refresh-token"),
                    TokenExpiresAt = now.AddHours(1),
                    RefreshTokenExpiresAt = now.AddDays(20),
                    CreatedAt = now,
                    UpdatedAt = now
                });
                await db.SaveChangesAsync();
            }

            var handler = new RoutingHandler(responseFactory);
            var apiClient = new MyAnimeListApiClient(
                new HttpClient(handler),
                Options.Create(new MyAnimeListOptions
                {
                    ClientId = "client-id",
                    ClientSecret = "client-secret"
                }),
                NullLogger<MyAnimeListApiClient>.Instance);
            var provider = new MyAnimeListMediaProvider(
                db,
                apiClient,
                encryption,
                new MyAnimeListTokenRefreshGate(),
                NullLogger<MyAnimeListMediaProvider>.Instance);
            return new ProviderFixture(connection, db, provider, handler, dataProtectionDirectory);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
            if (Directory.Exists(_dataProtectionDirectory))
            {
                Directory.Delete(_dataProtectionDirectory, recursive: true);
            }
        }
    }

    public sealed class RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<HttpMethod> Methods { get; } = [];
        public List<string> Bodies { get; } = [];
        public List<string> Paths { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Methods.Add(request.Method);
            Paths.Add(request.RequestUri!.AbsolutePath);
            Bodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return responseFactory(request);
        }
    }
}
