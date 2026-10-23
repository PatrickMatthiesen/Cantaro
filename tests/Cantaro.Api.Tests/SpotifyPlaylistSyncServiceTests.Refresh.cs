using System.Net;
using Cantaro.Api.Configuration;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Cantaro.Api.Services.Spotify;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Npgsql;

namespace Cantaro.Api.Tests;

public sealed partial class SpotifyPlaylistSyncServiceTests
{
    public static IEnumerable<object[]> LegacyRefreshCases(bool postgres)
    {
        foreach (var mode in new[] { "valid", "expired", "list401", "items401" })
            yield return [mode, postgres];
    }

    [Theory]
    [MemberData(nameof(LegacyRefreshCases), false)]
    public Task LegacyRefresh_ImportsWithTheTokenGenerationUsedByTheRead(string mode, bool postgres)
        => CheckLegacyRefreshAsync(mode, postgres);

    [PostgresTheory]
    [MemberData(nameof(LegacyRefreshCases), true)]
    public Task LegacyRefresh_PostgresImportsWithTheTokenGenerationUsedByTheRead(string mode, bool postgres)
        => CheckLegacyRefreshAsync(mode, postgres);

    private static async Task CheckLegacyRefreshAsync(string mode, bool postgres)
    {
        await using var fixture = await LegacyRefreshFixture.CreateAsync(mode, postgres);
        var playlistId = await fixture.ImportAsync();
        await fixture.AssertCallerEditsAsync();
        // Reuse the same caller/service after a refresh. Its tracked token may
        // still contain the prior, unexpired generation after a forced refresh.
        var repeatedId = await fixture.ImportAsync();
        Assert.Equal(playlistId, repeatedId);
        await fixture.AssertCallerEditsAsync();
        await using var verify = fixture.NewContext();
        var account = await verify.ConnectedServiceAccounts.SingleAsync();
        Assert.Equal(mode == "valid" ? 0 : 1, account.TokenVersion);
        Assert.Equal("connected", account.ConnectionState);
        Assert.Equal("spotify-account", account.ExternalAccountId);
        var playlist = await verify.Playlists.SingleAsync();
        Assert.Equal(playlistId, playlist.Id);
        // An item 401 must discard the playlist fetched with the rejected token.
        Assert.Equal(mode == "valid" ? "cached-access" : "refreshed-access", playlist.Name);
        Assert.Equal(1, await verify.PlaylistEntries.CountAsync());
        Assert.Equal(2, await verify.TrackObservations.CountAsync());
        Assert.Equal(1, await verify.Tracks.CountAsync());
        Assert.Equal(2, await verify.TrackSourceIds.CountAsync());
        Assert.Equal(mode == "valid" ? 0 : 1, fixture.Handler.TokenRequests);
        Assert.All(fixture.Handler.ItemTokens.Where(token => token != "cached-access" || mode == "valid"),
            token => Assert.Equal(mode == "valid" ? "cached-access" : "refreshed-access", token));
        Assert.Equal(mode == "items401" ? 3 : 2, fixture.Handler.ListRequests - (mode == "list401" ? 1 : 0));
    }

    public static IEnumerable<object[]> LegacyRefreshAccountChanges(bool postgres)
    {
        foreach (var mode in new[] { "valid", "expired", "list401", "items401" })
        foreach (var change in new[] { "replace", "disconnect", "token" })
        foreach (var phase in new[] { "list", "items" })
            yield return [mode, change, phase, postgres];
    }

    [Theory]
    [MemberData(nameof(LegacyRefreshAccountChanges), false)]
    public Task LegacyRefresh_RejectsAccountChangesAfterReadTokenAcquisition(string mode, string change, string phase, bool postgres)
        => CheckLegacyAccountChangeAsync(mode, change, phase, postgres);

    [PostgresTheory]
    [MemberData(nameof(LegacyRefreshAccountChanges), true)]
    public Task LegacyRefresh_PostgresRejectsAccountChangesAfterReadTokenAcquisition(string mode, string change, string phase, bool postgres)
        => CheckLegacyAccountChangeAsync(mode, change, phase, postgres);

    private static async Task CheckLegacyAccountChangeAsync(string mode, string change, string phase, bool postgres)
    {
        await using var fixture = await LegacyRefreshFixture.CreateAsync(mode, postgres);
        var changed = false;
        fixture.Handler.BeforeResponse = async (requestPhase, token) =>
        {
            // Exercise the refreshed generation as well as an unchanged token.
            if (changed || requestPhase != phase || (mode != "valid" && token != "refreshed-access")) return;
            changed = true;
            await using var other = fixture.NewContext();
            var account = await other.ConnectedServiceAccounts.SingleAsync();
            if (change == "replace") account.ExternalAccountId = "replacement-account";
            if (change == "disconnect") account.ConnectionState = "disconnected";
            if (change == "token") account.EncryptedAccessToken = fixture.Encryption.Encrypt("concurrent-token");
            account.TokenVersion++;
            await other.SaveChangesAsync();
        };
        var error = await Assert.ThrowsAsync<PlatformApiException>(() => fixture.ImportAsync());
        Assert.True(changed);
        Assert.Equal("spotify_account_changed", error.Code);
        Assert.Equal(409, error.StatusCode);
        await fixture.AssertRejectedAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task LegacyRefresh_RejectsTokenChangeDuringUnauthorizedResponse(bool items)
        => CheckLegacyUnauthorizedChangeAsync(items, false);

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public Task LegacyRefresh_PostgresRejectsTokenChangeDuringUnauthorizedResponse(bool items)
        => CheckLegacyUnauthorizedChangeAsync(items, true);

    private static async Task CheckLegacyUnauthorizedChangeAsync(bool items, bool postgres)
    {
        await using var fixture = await LegacyRefreshFixture.CreateAsync(items ? "items401" : "list401", postgres);
        fixture.Handler.BeforeResponse = async (phase, _) =>
        {
            if (phase != (items ? "items" : "list")) return;
            await using var other = fixture.NewContext();
            await other.ConnectedServiceAccounts.ExecuteUpdateAsync(set => set
                .SetProperty(a => a.TokenVersion, a => a.TokenVersion + 1));
        };
        var error = await Assert.ThrowsAsync<PlatformApiException>(() => fixture.ImportAsync());
        Assert.Equal("spotify_account_changed", error.Code);
        Assert.Equal(0, fixture.Handler.TokenRequests);
        await fixture.AssertRejectedAsync();
    }

    public static IEnumerable<object[]> LegacyRefreshFailures(bool postgres)
    {
        foreach (var failure in new[] { "invalid_grant", "cancel-refresh", "cancel-items", "twice401" })
            yield return [failure, postgres];
    }

    [Theory]
    [MemberData(nameof(LegacyRefreshFailures), false)]
    public Task LegacyRefresh_FailedRefreshAndCancellationDoNotIngest(string failure, bool postgres)
        => CheckLegacyRefreshFailureAsync(failure, postgres);

    [PostgresTheory]
    [MemberData(nameof(LegacyRefreshFailures), true)]
    public Task LegacyRefresh_PostgresFailedRefreshAndCancellationDoNotIngest(string failure, bool postgres)
        => CheckLegacyRefreshFailureAsync(failure, postgres);

    private static async Task CheckLegacyRefreshFailureAsync(string failure, bool postgres)
    {
        await using var fixture = await LegacyRefreshFixture.CreateAsync(failure == "twice401" ? "twice401" : "expired", postgres);
        using var cancellation = new CancellationTokenSource();
        fixture.Handler.BeforeResponse = (phase, _) =>
        {
            if (phase == (failure == "cancel-refresh" ? "refresh" : "items") && failure.StartsWith("cancel-", StringComparison.Ordinal))
                cancellation.Cancel();
            return Task.CompletedTask;
        };
        fixture.Handler.InvalidGrant = failure == "invalid_grant";
        if (failure.StartsWith("cancel-", StringComparison.Ordinal))
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.ImportAsync(cancellation.Token));
        else
            await Assert.ThrowsAsync<PlatformReconnectRequiredException>(() => fixture.ImportAsync());
        await fixture.AssertRejectedAsync();
        await using var verify = fixture.NewContext();
        var account = await verify.ConnectedServiceAccounts.SingleAsync();
        Assert.Null(account.TokenRefreshLeaseId);
        Assert.Null(account.TokenRefreshLeaseExpiresAt);
        Assert.Equal(failure is "invalid_grant" or "twice401" ? "reconnect_required" : "connected", account.ConnectionState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task LegacyRefresh_AcceptsWinningRefreshBeforeReadButRejectsAccountReplacement(bool replace)
        => CheckLegacyRefreshOwnershipAsync(replace, false);

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public Task LegacyRefresh_PostgresAcceptsWinningRefreshBeforeReadButRejectsAccountReplacement(bool replace)
        => CheckLegacyRefreshOwnershipAsync(replace, true);

    private static async Task CheckLegacyRefreshOwnershipAsync(bool replace, bool postgres)
    {
        await using var fixture = await LegacyRefreshFixture.CreateAsync("expired", postgres);
        fixture.Handler.BeforeResponse = async (phase, _) =>
        {
            if (phase != "refresh") return;
            await using var other = fixture.NewContext();
            var account = await other.ConnectedServiceAccounts.SingleAsync();
            account.EncryptedAccessToken = fixture.Encryption.Encrypt("winning-access");
            account.TokenExpiresAt = Now.AddHours(1).UtcDateTime;
            account.TokenVersion++;
            account.TokenRefreshLeaseId = null;
            account.TokenRefreshLeaseExpiresAt = null;
            if (replace) account.ExternalAccountId = "replacement-account";
            await other.SaveChangesAsync();
        };
        if (replace)
        {
            var error = await Assert.ThrowsAsync<PlatformApiException>(() => fixture.ImportAsync());
            Assert.Equal("spotify_account_changed", error.Code);
            Assert.Equal(0, fixture.Handler.ListRequests);
            await fixture.AssertRejectedAsync();
        }
        else
        {
            await fixture.ImportAsync();
            Assert.All(fixture.Handler.ListTokens.Concat(fixture.Handler.ItemTokens), token => Assert.Equal("winning-access", token));
            await fixture.AssertCallerEditsAsync();
        }
        Assert.Equal(1, fixture.Handler.TokenRequests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task LegacyRefresh_RetainedExportRejectsBeforeHttpOrAfterRead(bool duringRead)
        => CheckLegacyRefreshExportGuardAsync(duringRead, false);

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public Task LegacyRefresh_PostgresRetainedExportRejectsBeforeHttpOrAfterRead(bool duringRead)
        => CheckLegacyRefreshExportGuardAsync(duringRead, true);

    private static async Task CheckLegacyRefreshExportGuardAsync(bool duringRead, bool postgres)
    {
        await using var fixture = await LegacyRefreshFixture.CreateAsync("expired", postgres);
        async Task RecordAsync()
        {
            await using var other = fixture.NewContext();
            other.PlaylistExportAttempts.Add(new PlaylistExportAttempt { UserId = 73, Service = "spotify",
                ExternalAccountId = "spotify-account", ServicePlaylistId = "playlist1", ExternalTrackId = "track1" });
            await other.SaveChangesAsync();
        }
        if (duringRead) fixture.Handler.BeforeResponse = (phase, _) => phase == "items" ? RecordAsync() : Task.CompletedTask;
        else await RecordAsync();
        var error = await Assert.ThrowsAsync<PlatformApiException>(() => fixture.ImportAsync());
        Assert.Equal("outbound_mapping", error.Code);
        Assert.Equal(duringRead ? 1 : 0, fixture.Handler.TokenRequests);
        Assert.Equal(duringRead ? 1 : 0, fixture.Handler.ListRequests);
        await fixture.AssertRejectedAsync();
    }

    [PostgresTheory]
    [InlineData("replace")]
    [InlineData("disconnect")]
    [InlineData("token")]
    [InlineData("export")]
    public async Task LegacyRefresh_PostgresFinalGuardRollsBackAlreadySavedMaterialization(string change)
    {
        var interception = new LegacyMaterializationInterception();
        await using var fixture = await LegacyRefreshFixture.CreateAsync("valid", true, interception);
        interception.AfterSave = async () =>
        {
            await using var other = fixture.NewContext();
            if (change == "export")
                other.PlaylistExportAttempts.Add(new PlaylistExportAttempt { UserId = 73, Service = "spotify",
                    ExternalAccountId = "spotify-account", ServicePlaylistId = "playlist1", ExternalTrackId = "track1" });
            else
            {
                var account = await other.ConnectedServiceAccounts.SingleAsync();
                if (change == "replace") account.ExternalAccountId = "replacement-account";
                if (change == "disconnect") account.ConnectionState = "disconnected";
                if (change == "token") account.EncryptedAccessToken = fixture.Encryption.Encrypt("concurrent-token");
                account.TokenVersion++;
            }
            await other.SaveChangesAsync();
        };
        var error = await Assert.ThrowsAsync<PlatformApiException>(() => fixture.ImportAsync());
        Assert.Equal(change == "export" ? "outbound_mapping" : "spotify_account_changed", error.Code);
        Assert.True(interception.Invoked);
        await fixture.AssertRejectedAsync();
        await using var verify = fixture.NewContext();
        if (change == "export") Assert.Single(await verify.PlaylistExportAttempts.ToArrayAsync());
        else Assert.Equal(1, (await verify.ConnectedServiceAccounts.SingleAsync()).TokenVersion);
    }

    private sealed class LegacyMaterializationInterception : SaveChangesInterceptor
    {
        public Func<Task>? AfterSave { get; set; }
        public bool Invoked { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (!Invoked && AfterSave is not null && eventData.Context!.Database.CurrentTransaction is not null)
            {
                Invoked = true;
                await AfterSave();
            }
            return result;
        }
    }

    private sealed class LegacyRefreshFixture : IAsyncDisposable
    {
        private readonly DbContextOptions<ApplicationDbContext> _options;
        private readonly IAsyncDisposable _database;
        public ApplicationDbContext Db { get; }
        public TokenEncryptionService Encryption { get; } = new(new EphemeralDataProtectionProvider());
        public LegacyRefreshHandler Handler { get; }
        private readonly HttpClient _http;
        private readonly SpotifyPlaylistSyncService _importer;
        private readonly PlatformAccountContext _account;

        private LegacyRefreshFixture(DbContextOptions<ApplicationDbContext> options, IAsyncDisposable database, string mode, int accountId)
        {
            _options = options;
            _database = database;
            Db = NewContext();
            Handler = new(mode);
            _http = new HttpClient(Handler) { BaseAddress = new Uri("https://api.spotify.com") };
            var spotifyOptions = Options.Create(new SpotifyOptions { ClientId = "client-id", ClientSecret = "client-secret" });
            var time = new FixedTimeProvider(Now);
            var api = new SpotifyApiClient(_http, spotifyOptions, new NoDelay());
            var manager = new SpotifyTokenManager(Db, api, Encryption, time, new SpotifyTestServiceScopeFactory(options));
            var service = new SpotifyService(Db, api, manager, Encryption, spotifyOptions, time);
            _importer = new(Db, service, new SpotifyTrackResolver(Db), new PlaylistCanonicalReconciliationService(Db), time,
                NullLogger<SpotifyPlaylistSyncService>.Instance);
            _account = new(73, accountId);
        }

        public static async Task<LegacyRefreshFixture> CreateAsync(string mode, bool postgres, params IInterceptor[] interceptors)
        {
            DbContextOptions<ApplicationDbContext> options;
            IAsyncDisposable database;
            if (postgres)
            {
                var schema = await PostgresTestSchema.CreateAsync();
                database = schema;
                options = (DbContextOptions<ApplicationDbContext>)schema.Db.GetService<IDbContextOptions>();
            }
            else
            {
                var connectionString = $"Data Source=legacy-refresh-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
                var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync();
                database = connection;
                options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connectionString).Options;
                await using var create = new ApplicationDbContext(options);
                await create.Database.EnsureCreatedAsync();
            }
            options = new DbContextOptionsBuilder<ApplicationDbContext>(options).AddInterceptors(interceptors).Options;
            var fixture = new LegacyRefreshFixture(options, database, mode, 1);
            fixture.Db.Users.Add(new User { Id = 73, UserName = "before", CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime });
            fixture.Db.ConnectedServiceAccounts.Add(new ConnectedServiceAccount { Id = 1, UserId = 73, Service = "spotify",
                ExternalAccountId = "spotify-account", ConnectionState = "connected", TokenVersion = 0,
                EncryptedAccessToken = fixture.Encryption.Encrypt("cached-access"),
                EncryptedRefreshToken = fixture.Encryption.Encrypt("refresh-token"),
                TokenExpiresAt = Now.AddMinutes(mode == "expired" ? -1 : 30).UtcDateTime,
                RefreshTokenExpiresAt = Now.AddDays(30).UtcDateTime, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime });
            await fixture.Db.SaveChangesAsync();
            (await fixture.Db.Users.SingleAsync()).UserName = "caller pending edit";
            fixture.Db.ConnectedServiceAccounts.Local.Single().DisplayName = "caller pending account edit";
            return fixture;
        }

        public ApplicationDbContext NewContext() => new(_options);
        public Task<Guid> ImportAsync(CancellationToken ct = default) => _importer.SyncPlaylistAsync(_account, "playlist1", ct);

        public async Task AssertCallerEditsAsync()
        {
            var user = Db.Users.Local.Single();
            Assert.Equal("caller pending edit", user.UserName);
            Assert.Equal(EntityState.Modified, Db.Entry(user).State);
            var account = Db.ConnectedServiceAccounts.Local.Single();
            Assert.Equal("caller pending account edit", account.DisplayName);
            Assert.Equal(EntityState.Modified, Db.Entry(account).State);
            await using var fresh = NewContext();
            Assert.Equal("before", (await fresh.Users.SingleAsync()).UserName);
            Assert.Null((await fresh.ConnectedServiceAccounts.SingleAsync()).DisplayName);
        }

        public async Task AssertRejectedAsync()
        {
            await AssertCallerEditsAsync();
            await using (var fresh = NewContext()) await AssertNoIngestionAsync(fresh);
            // A later caller save must not resurrect materialization from a rejected import.
            await Db.SaveChangesAsync();
            await using var verify = NewContext();
            Assert.Equal("caller pending edit", (await verify.Users.SingleAsync()).UserName);
            await AssertNoIngestionAsync(verify);
        }

        private static async Task AssertNoIngestionAsync(ApplicationDbContext db)
        {
            Assert.Empty(await db.Playlists.ToArrayAsync());
            Assert.Empty(await db.ServicePlaylistMappings.ToArrayAsync());
            Assert.Empty(await db.PlaylistEntries.ToArrayAsync());
            Assert.Empty(await db.TrackObservations.ToArrayAsync());
            Assert.Empty(await db.TrackSourceIds.ToArrayAsync());
            Assert.Empty(await db.Tracks.ToArrayAsync());
            Assert.Empty(await db.Songs.ToArrayAsync());
            Assert.Empty(await db.SongTracks.ToArrayAsync());
            Assert.Empty(await db.Artists.ToArrayAsync());
            Assert.Empty(await db.TrackArtistCredits.ToArrayAsync());
            Assert.Empty(await db.TrackMatchQueueItems.ToArrayAsync());
        }

        public async ValueTask DisposeAsync()
        {
            _http.Dispose();
            var postgresConnectionString = Db.Database.IsNpgsql() ? Db.Database.GetConnectionString() : null;
            await Db.DisposeAsync();
            await _database.DisposeAsync();
            if (postgresConnectionString is not null)
            {
                // Each private schema has its own pool. Release only this
                // fixture's idle connections before the next test creates one.
                using var pool = new NpgsqlConnection(postgresConnectionString);
                NpgsqlConnection.ClearPool(pool);
            }
        }
    }

    private sealed class LegacyRefreshHandler(string mode) : HttpMessageHandler
    {
        public Func<string, string, Task>? BeforeResponse { get; set; }
        public bool InvalidGrant { get; set; }
        public int TokenRequests { get; private set; }
        public int ListRequests => ListTokens.Count;
        public List<string> ListTokens { get; } = [];
        public List<string> ItemTokens { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var phase = request.RequestUri!.Host == "accounts.spotify.com" ? "refresh"
                : request.RequestUri.AbsolutePath == "/v1/me/playlists" ? "list" : "items";
            var token = request.Headers.Authorization?.Parameter ?? "";
            if (phase == "refresh")
            {
                TokenRequests++;
                Assert.Contains("refresh_token=refresh-token", await request.Content!.ReadAsStringAsync(ct));
            }
            else if (phase == "list") ListTokens.Add(token);
            else ItemTokens.Add(token);
            if (BeforeResponse is not null) await BeforeResponse(phase, token);
            ct.ThrowIfCancellationRequested();
            if (phase == "refresh") return InvalidGrant
                ? Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""")
                : Json(HttpStatusCode.OK, """{"access_token":"refreshed-access","refresh_token":"next-refresh","expires_in":3600}""");
            if ((token == "cached-access" && (mode == "list401" && phase == "list" || mode == "items401" && phase == "items")) || mode == "twice401")
                return Json(HttpStatusCode.Unauthorized, """{"error":{"status":401,"message":"Expired"}}""");
            return phase == "list" ? PlaylistPage(token, "Read with the same generation as its items") : ItemPage("Provider title", "Provider artist");
        }
    }
}
