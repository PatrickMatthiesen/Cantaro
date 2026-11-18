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
        var anime = Assert.Single(results, result => result.ProviderMediaId == "anime:42" && result.MediaKind == MediaKinds.Anime);
        Assert.Equal("https://myanimelist.net/anime/42", anime.ExternalUrl);
        var manga = Assert.Single(results, result => result.ProviderMediaId == "manga:42" && result.MediaKind == MediaKinds.Manga);
        Assert.Equal("https://myanimelist.net/manga/42", manga.ExternalUrl);
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
        Assert.Equal("https://myanimelist.net/anime/52991", details.ExternalUrl);
        Assert.Equal(28, details.ReleasedCount);
        Assert.Equal(28, details.TotalKnownCount);
        Assert.Null(details.NextReleaseAt);
    }

    [Theory]
    [InlineData(400, "invalid_grant", true)]
    [InlineData(401, "", true)]
    [InlineData(400, "invalid_client", false)]
    [InlineData(429, "invalid_grant", false)]
    [InlineData(503, "unavailable", false)]
    public async Task ExpiredAccess_PermanentRefreshRejectionRequiresReconnect(int status, string error, bool reconnect)
    {
        await using var fixture = await ProviderFixture.CreateAsync(_ => new HttpResponseMessage((HttpStatusCode)status)
        { Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new { error })) }, 601);
        var account = await fixture.Db.ConnectedServiceAccounts.SingleAsync();
        account.TokenExpiresAt = DateTime.UtcNow.AddHours(-1);
        await fixture.Db.SaveChangesAsync();
        var failure = await Record.ExceptionAsync(() => fixture.Provider.ImportLibraryAsync(601, default));
        Assert.NotNull(failure);
        await fixture.Db.Entry(account).ReloadAsync();
        Assert.Equal(reconnect ? "reconnect_required" : "connected", account.ConnectionState);
        Assert.Equal(reconnect, failure is PlatformReconnectRequiredException);
        var calls = fixture.Handler.Methods.Count;
        if (reconnect)
        {
            await Assert.ThrowsAsync<PlatformReconnectRequiredException>(() => fixture.Provider.ImportLibraryAsync(601, default));
            Assert.Equal(calls, fixture.Handler.Methods.Count);
        }
    }

    [Fact]
    public async Task KnownRefreshExpiry_PausesWithoutHttpAndSameAccountOAuthResumes()
    {
        await using var fixture = await ProviderFixture.CreateAsync(request =>
            request.RequestUri!.AbsolutePath.Contains("token", StringComparison.Ordinal)
                ? JsonResponse("{\"access_token\":\"new-access\",\"refresh_token\":\"new-refresh\",\"expires_in\":3600}")
                : request.RequestUri.AbsolutePath.EndsWith("@me", StringComparison.Ordinal)
                    ? JsonResponse("{\"id\":602,\"name\":\"same user\"}")
                    : JsonResponse("{\"data\":[],\"paging\":{}}"), 602);
        var account = await fixture.Db.ConnectedServiceAccounts.SingleAsync();
        var id = account.Id;
        account.TokenExpiresAt = DateTime.UtcNow.AddHours(-1);
        account.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(-1);
        await fixture.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<PlatformReconnectRequiredException>(() => fixture.Provider.ImportLibraryAsync(602, default));
        Assert.Empty(fixture.Handler.Methods);
        await fixture.Db.Entry(account).ReloadAsync();
        Assert.Equal("reconnect_required", account.ConnectionState);
        await fixture.Provider.ExchangeCodeAndSaveAsync(602, "code", "https://localhost/callback", "verifier", default);
        var result = await fixture.Provider.ImportLibraryAsync(602, default);
        Assert.Equal(id, account.Id);
        Assert.Equal("connected", account.ConnectionState);
        Assert.Empty(result.Items);
        Assert.Null(account.ReconnectReason);
    }

    [Fact]
    public async Task RefreshCancellation_DoesNotInvalidateAccount()
    {
        await using var fixture = await ProviderFixture.CreateAsync(_ => throw new OperationCanceledException(), 603);
        var account = await fixture.Db.ConnectedServiceAccounts.SingleAsync();
        account.TokenExpiresAt = DateTime.UtcNow.AddHours(-1);
        await fixture.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Provider.ImportLibraryAsync(603, default));
        await fixture.Db.Entry(account).ReloadAsync();
        Assert.Equal("connected", account.ConnectionState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnauthorizedAccess_RefreshesBeforeRequiringReconnect(bool rejectReplacement)
    {
        var reads = 0;
        await using var fixture = await ProviderFixture.CreateAsync(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("token", StringComparison.Ordinal))
                return JsonResponse("{\"access_token\":\"new-access\",\"refresh_token\":\"new-refresh\",\"expires_in\":3600}");
            if (++reads == 1 || rejectReplacement)
                return new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{}") };
            return JsonResponse("{\"data\":[],\"paging\":{}}");
        }, 604);
        if (rejectReplacement)
            await Assert.ThrowsAsync<PlatformReconnectRequiredException>(() => fixture.Provider.ImportLibraryAsync(604, default));
        else
            await fixture.Provider.ImportLibraryAsync(604, default);
        var persisted = await fixture.Db.ConnectedServiceAccounts.AsNoTracking().SingleAsync();
        Assert.Equal(rejectReplacement ? "reconnect_required" : "connected", persisted.ConnectionState);
        Assert.Single(fixture.Handler.Paths, path => path.Contains("token", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OldRefreshFailure_CannotOverwriteNewOAuthSuccess()
    {
        ProviderFixture? current = null;
        await using var fixture = await ProviderFixture.CreateAsync(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("token", StringComparison.Ordinal))
            {
                using var oauth = PlaylistReadIngestion.CreateContext(current!.Db);
                oauth.ConnectedServiceAccounts.ExecuteUpdate(set => set
                    .SetProperty(item => item.TokenVersion, item => item.TokenVersion + 1)
                    .SetProperty(item => item.TokenExpiresAt, DateTime.UtcNow.AddHours(1)));
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                { Content = new StringContent("{\"error\":\"invalid_grant\"}") };
            }
            return JsonResponse("{\"data\":[],\"paging\":{}}");
        }, 605);
        current = fixture;
        var account = await fixture.Db.ConnectedServiceAccounts.SingleAsync();
        account.TokenExpiresAt = DateTime.UtcNow.AddHours(-1);
        await fixture.Db.SaveChangesAsync();
        await fixture.Provider.ImportLibraryAsync(605, default);
        await fixture.Db.Entry(account).ReloadAsync();
        Assert.Equal("connected", account.ConnectionState);
        Assert.Equal(1, account.TokenVersion);
        Assert.Null(account.ReconnectRequiredAt);
    }

    [Theory]
    [InlineData("already_rejected", true)]
    [InlineData("recovered", false)]
    [InlineData("replacement", false)]
    public async Task ConcurrentReadRejection_PreservesReconnectClassificationAndNewCredentialFence(string state, bool reconnect)
    {
        ProviderFixture? current = null;
        var reads = 0;
        await using var fixture = await ProviderFixture.CreateAsync(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("token", StringComparison.Ordinal))
                return JsonResponse("{\"access_token\":\"new-access\",\"refresh_token\":\"new-refresh\",\"expires_in\":3600}");
            if (++reads == 2)
            {
                using var concurrent = PlaylistReadIngestion.CreateContext(current!.Db);
                concurrent.ConnectedServiceAccounts.ExecuteUpdate(set => set
                    .SetProperty(item => item.ConnectionState, state == "recovered" ? "connected" : "reconnect_required")
                    .SetProperty(item => item.ExternalAccountId, state == "replacement" ? "replacement" : "612")
                    .SetProperty(item => item.TokenVersion, item => item.TokenVersion + 1));
            }
            return reads <= 2 ? new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{}") }
                : JsonResponse("{\"data\":[],\"paging\":{}}");
        }, 612);
        current = fixture;
        var failure = await Record.ExceptionAsync(() => fixture.Provider.ImportLibraryAsync(612, default));
        Assert.Equal(reconnect, failure is PlatformReconnectRequiredException);
        if (!reconnect) Assert.IsType<MyAnimeListRequestException>(failure);
        var account = await fixture.Db.ConnectedServiceAccounts.AsNoTracking().SingleAsync();
        Assert.Equal(2, account.TokenVersion);
        Assert.Equal(state == "recovered" ? "connected" : "reconnect_required", account.ConnectionState);
        Assert.Equal(state == "replacement" ? "replacement" : "612", account.ExternalAccountId);
        Assert.Equal(2, reads);
        if (reconnect)
        {
            await fixture.Db.ConnectedServiceAccounts.ExecuteUpdateAsync(set => set
                .SetProperty(item => item.ConnectionState, "connected").SetProperty(item => item.TokenVersion, item => item.TokenVersion + 1));
            Assert.Empty((await fixture.Provider.ImportLibraryAsync(612, default)).Items);
        }
    }

    [Theory]
    [InlineData("connected", false)]
    [InlineData("reconnect_required", true)]
    public async Task OAuthWithoutReplacementRefresh_CannotReviveUnusableCredential(string state, bool rejected)
    {
        await using var fixture = await ProviderFixture.CreateAsync(request =>
            request.RequestUri!.AbsolutePath.Contains("token", StringComparison.Ordinal)
                ? JsonResponse("{\"access_token\":\"oauth-access\",\"expires_in\":3600}")
                : JsonResponse("{\"id\":606,\"name\":\"same account\"}"), 606);
        var account = await fixture.Db.ConnectedServiceAccounts.SingleAsync();
        account.ConnectionState = state;
        await fixture.Db.SaveChangesAsync();
        var failure = await Record.ExceptionAsync(() => fixture.Provider.ExchangeCodeAndSaveAsync(
            606, "code", "https://localhost/callback", "verifier", default));
        Assert.Equal(rejected, failure is PlatformApiException);
        await fixture.Db.Entry(account).ReloadAsync();
        Assert.Equal(state, account.ConnectionState);
    }

    [Fact]
    public async Task OAuthSuccess_AdvancesDatabaseVersionEvenWithStaleTrackedAccount()
    {
        await using var fixture = await ProviderFixture.CreateAsync(request =>
            request.RequestUri!.AbsolutePath.Contains("token", StringComparison.Ordinal)
                ? JsonResponse("{\"access_token\":\"oauth-access\",\"refresh_token\":\"oauth-refresh\",\"expires_in\":3600}")
                : JsonResponse("{\"id\":607,\"name\":\"same account\"}"), 607);
        var account = await fixture.Db.ConnectedServiceAccounts.SingleAsync();
        using var concurrent = PlaylistReadIngestion.CreateContext(fixture.Db);
        await concurrent.ConnectedServiceAccounts.ExecuteUpdateAsync(set => set.SetProperty(item => item.TokenVersion, 3));
        await fixture.Provider.ExchangeCodeAndSaveAsync(607, "code", "https://localhost/callback", "verifier", default);
        Assert.Equal(4, account.TokenVersion);
        Assert.Equal("connected", account.ConnectionState);
    }

    [Fact]
    public async Task FirstStatusRead_MarksKnownExpiredRefreshAndRejectsOAuthWithoutReplacement()
    {
        await using var fixture = await ProviderFixture.CreateAsync(request =>
            request.RequestUri!.AbsolutePath.Contains("token", StringComparison.Ordinal)
                ? JsonResponse("{\"access_token\":\"new-access\",\"expires_in\":3600}")
                : JsonResponse("{\"id\":608,\"name\":\"same account\"}"), 608);
        var account = await fixture.Db.ConnectedServiceAccounts.SingleAsync();
        account.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(-1);
        await fixture.Db.SaveChangesAsync();
        var status = await fixture.Provider.GetConnectedAccountAsync(608);
        Assert.Equal("reconnect_required", status!.ConnectionState);
        Assert.Empty(fixture.Handler.Methods);
        await Assert.ThrowsAsync<PlatformApiException>(() => fixture.Provider.ExchangeCodeAndSaveAsync(
            608, "code", "https://localhost/callback", "verifier", default));
        await fixture.Db.Entry(account).ReloadAsync();
        Assert.Equal("reconnect_required", account.ConnectionState);
    }

    [Fact]
    public async Task RetainedAccount_RejectsDifferentViewerAndResumesScoreForOriginalViewer()
    {
        var viewer = 610;
        await using var fixture = await ProviderFixture.CreateAsync(request =>
            request.RequestUri!.AbsolutePath.Contains("token", StringComparison.Ordinal)
                ? JsonResponse("{\"access_token\":\"new-access\",\"refresh_token\":\"new-refresh\",\"expires_in\":3600}")
                : request.Method == HttpMethod.Put ? JsonResponse("{\"score\":8}")
                : JsonResponse($"{{\"id\":{viewer},\"name\":\"account\"}}"), 609);
        var account = await fixture.Db.ConnectedServiceAccounts.SingleAsync();
        account.ConnectionState = "reconnect_required";
        await fixture.Db.SaveChangesAsync();
        var pending = new MediaScoreUpdateRequest { ProviderMediaId = "anime:9", Score = 80m };
        var failure = await Assert.ThrowsAsync<PlatformApiException>(() => fixture.Provider.ExchangeCodeAndSaveAsync(
            609, "code", "https://localhost/callback", "verifier", default));
        Assert.Equal("platform_account_changed", failure.Code);
        await Assert.ThrowsAsync<PlatformReconnectRequiredException>(() => fixture.Provider.UpdateScoreAsync(609, pending, default));
        Assert.DoesNotContain(HttpMethod.Put, fixture.Handler.Methods);
        await fixture.Db.Entry(account).ReloadAsync();
        Assert.Equal("609", account.ExternalAccountId);
        viewer = 609;
        await fixture.Provider.ExchangeCodeAndSaveAsync(609, "code", "https://localhost/callback", "verifier", default);
        await fixture.Provider.UpdateScoreAsync(609, pending, default);
        Assert.Single(fixture.Handler.Methods, method => method == HttpMethod.Put);
        Assert.Equal("609", account.ExternalAccountId);
    }

    [Fact]
    public async Task UnauthorizedMutation_RefreshesCredentialsWithoutReplayingPut()
    {
        await using var fixture = await ProviderFixture.CreateAsync(request =>
            request.RequestUri!.AbsolutePath.Contains("token", StringComparison.Ordinal)
                ? JsonResponse("{\"access_token\":\"new-access\",\"refresh_token\":\"new-refresh\",\"expires_in\":3600}")
                : new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{}") }, 611);
        var failure = await Assert.ThrowsAsync<PlatformApiException>(() => fixture.Provider.UpdateScoreAsync(
            611, new MediaScoreUpdateRequest { ProviderMediaId = "anime:9", Score = 80m }, default));
        Assert.Equal("provider_authorization_refreshed", failure.Code);
        Assert.Single(fixture.Handler.Methods, method => method == HttpMethod.Put);
        Assert.Single(fixture.Handler.Paths, path => path.Contains("token", StringComparison.Ordinal));
        Assert.Equal("connected", (await fixture.Provider.GetConnectedAccountAsync(611))!.ConnectionState);
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
                new MyAnimeListRequestGate(TimeProvider.System, TimeSpan.Zero),
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
