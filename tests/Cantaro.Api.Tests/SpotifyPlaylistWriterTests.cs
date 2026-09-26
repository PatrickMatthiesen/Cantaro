using System.Net;
using System.Text;
using System.Text.Json;
using Cantaro.Api.Configuration;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Cantaro.Api.Services.Spotify;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class SpotifyPlaylistWriterTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReconcileAsync_ReplacesOrderAndBatchesThenSkipsMatchingPlaylist()
    {
        await using var scope = await TestScope.CreateAsync();
        var desired = Enumerable.Range(0, 205).Select(index => $"track{index % 199}").ToArray();
        scope.Handler.TrackIds.AddRange(["old", "old"]);

        await scope.Writer.ReconcileAsync(scope.Account, "playlist1", desired, CancellationToken.None);
        await scope.Writer.ReconcileAsync(scope.Account, "playlist1", desired, CancellationToken.None);

        Assert.Equal(desired, scope.Handler.TrackIds);
        Assert.Equal(1, scope.Handler.Requests.Count(request => request.Method == HttpMethod.Put));
        Assert.Equal(2, scope.Handler.Requests.Count(request => request.Method == HttpMethod.Post));
        Assert.Equal(
            [100, 100, 5],
            scope.Handler.Requests
                .Where(request => request.Method is { } method && (method == HttpMethod.Put || method == HttpMethod.Post))
                .Select(request => request.TrackIds.Length)
                .ToArray());
        Assert.All(scope.Handler.Requests, request => Assert.Equal("Bearer", request.AuthorizationScheme));
        Assert.All(
            scope.Handler.Requests.Where(request => request.Method == HttpMethod.Put || request.Method == HttpMethod.Post),
            request => Assert.Equal("/v1/playlists/playlist1/items", request.Path));
    }

    [Fact]
    public async Task ReconcileAsync_RereadsAfterAppliedAppendHasAmbiguousFailure()
    {
        await using var scope = await TestScope.CreateAsync();
        var desired = Enumerable.Range(0, 250).Select(index => $"track{index}").ToArray();
        scope.Handler.FailFirstAppendAfterApplying = true;

        var failure = await Assert.ThrowsAsync<PlatformApiException>(() =>
            scope.Writer.ReconcileAsync(scope.Account, "playlist1", desired, CancellationToken.None));
        Assert.Equal(429, failure.StatusCode);
        Assert.Equal("spotify_rate_limited", failure.Code);
        Assert.Equal(TimeSpan.FromSeconds(30), failure.RetryAfter);
        Assert.Equal(desired.Take(200), scope.Handler.TrackIds);

        var requestCount = scope.Handler.Requests.Count;
        var blockedRetry = await Assert.ThrowsAsync<PlatformApiException>(() =>
            scope.Writer.ReconcileAsync(scope.Account, "playlist1", desired, CancellationToken.None));
        Assert.Equal("spotify_rate_limited", blockedRetry.Code);
        Assert.Equal(TimeSpan.FromSeconds(30), blockedRetry.RetryAfter);
        Assert.Equal(requestCount, scope.Handler.Requests.Count);

        scope.Time.Advance(TimeSpan.FromSeconds(30));
        await scope.Writer.ReconcileAsync(scope.Account, "playlist1", desired, CancellationToken.None);

        Assert.Equal(desired, scope.Handler.TrackIds);
        Assert.Equal(2, scope.Handler.Requests.Count(request => request.Method == HttpMethod.Put));
        Assert.Equal(3, scope.Handler.Requests.Count(request => request.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task ReconcileAsync_VerifiesProviderOrderAfterWriteAndRetriesFromFreshState()
    {
        await using var scope = await TestScope.CreateAsync();
        var desired = Enumerable.Range(0, 150).Select(index => $"track{index}").ToArray();
        scope.Handler.IgnoreFirstAppend = true;

        var failure = await Assert.ThrowsAsync<PlatformApiException>(() =>
            scope.Writer.ReconcileAsync(scope.Account, "playlist1", desired, CancellationToken.None));
        Assert.Equal("spotify_playlist_verification_failed", failure.Code);
        Assert.Equal(desired.Take(100), scope.Handler.TrackIds);

        await scope.Writer.ReconcileAsync(scope.Account, "playlist1", desired, CancellationToken.None);

        Assert.Equal(desired, scope.Handler.TrackIds);
        Assert.Equal(2, scope.Handler.Requests.Count(request => request.Method == HttpMethod.Put));
        Assert.Equal(2, scope.Handler.Requests.Count(request => request.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task ReconcileAsync_RejectsReadOnlyScopeWithoutWriting()
    {
        await using var scope = await TestScope.CreateAsync(scopes: "playlist-read-private user-read-private");

        var error = await Assert.ThrowsAsync<PlatformApiException>(() =>
            scope.Writer.ReconcileAsync(scope.Account, "playlist1", ["track1"], CancellationToken.None));

        Assert.Equal("spotify_write_scope_required", error.Code);
        Assert.Contains("Reconnect Spotify", error.Message);
        Assert.DoesNotContain(scope.Handler.Requests, request => request.Method != HttpMethod.Get);
        Assert.Equal("connected", await scope.GetConnectionStateAsync());
    }

    [Fact]
    public async Task ValidateDestinationAsync_RequiresPublicWriteScopeForPublicPlaylist()
    {
        await using var scope = await TestScope.CreateAsync(scopes: "playlist-read-private playlist-modify-private user-read-private");
        scope.Handler.IsPublic = true;

        var error = await Assert.ThrowsAsync<PlatformApiException>(() =>
            scope.Writer.ValidateDestinationAsync(scope.Account, "playlist1", CancellationToken.None));

        Assert.Equal("spotify_write_scope_required", error.Code);
        Assert.Contains("playlist-modify-public", error.Message);
        Assert.Equal("connected", await scope.GetConnectionStateAsync());
    }

    [Fact]
    public async Task ValidateDestinationAsync_RejectsOtherOwnerAndWrongPinnedAccount()
    {
        await using var scope = await TestScope.CreateAsync();
        scope.Handler.OwnerId = "someone-else";
        var ownershipError = await Assert.ThrowsAsync<PlatformApiException>(() =>
            scope.Writer.ValidateDestinationAsync(scope.Account, "playlist1", CancellationToken.None));
        Assert.Equal("spotify_playlist_not_writable", ownershipError.Code);

        var requestCount = scope.Handler.Requests.Count;
        var accountError = await Assert.ThrowsAsync<PlatformApiException>(() =>
            scope.Writer.ValidateDestinationAsync(
                scope.Account with { ConnectedServiceAccountId = scope.Account.ConnectedServiceAccountId + 1 },
                "playlist1",
                CancellationToken.None));
        Assert.Equal("spotify_not_connected", accountError.Code);
        Assert.Equal(requestCount, scope.Handler.Requests.Count);
    }

    [Fact]
    public async Task ValidateDestinationAsync_RejectsAccountChangedInPlaceBeforeProviderRead()
    {
        await using var scope = await TestScope.CreateAsync();
        await scope.ChangeExternalAccountIdAsync("replacement-user");

        var error = await Assert.ThrowsAsync<PlatformApiException>(() =>
            scope.Writer.ValidateDestinationAsync(
                scope.Account with { ExpectedExternalAccountId = "spotify-user" },
                "playlist1",
                CancellationToken.None));

        Assert.Equal("spotify_account_changed", error.Code);
        Assert.Empty(scope.Handler.Requests);
    }

    [Fact]
    public async Task ReconcileAsync_RemovesUnsupportedExistingItem()
    {
        await using var scope = await TestScope.CreateAsync();
        scope.Handler.IncludeLocalItem = true;

        await scope.Writer.ReconcileAsync(scope.Account, "playlist1", ["track1"], CancellationToken.None);

        Assert.Equal(["track1"], scope.Handler.TrackIds);
        Assert.Single(scope.Handler.Requests, request => request.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task CreatePlaylistAsync_CreatesPrivateNamedPlaylistForPinnedAccount()
    {
        await using var scope = await TestScope.CreateAsync();

        var id = await scope.Writer.CreatePlaylistAsync(
            scope.Account with { ExpectedExternalAccountId = "spotify-user" },
            "Road songs", CancellationToken.None);

        Assert.Equal("newplaylist", id);
        Assert.Equal(1, scope.Handler.CreateCount);
        Assert.Equal("Road songs", scope.Handler.CreatedName);
        Assert.False(scope.Handler.CreatedPublic);
        Assert.Single(scope.Handler.Requests, request =>
            request.Method == HttpMethod.Post && request.Path == "/v1/me/playlists");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public async Task CreatePlaylistAsync_AcceptsAnyReturnedPublicValueButRequestsPrivate(bool? responsePublic)
    {
        await using var scope = await TestScope.CreateAsync();
        scope.Handler.CreationPublicResponse = responsePublic;

        var id = await scope.Writer.CreatePlaylistAsync(
            scope.Account, "Road songs", CancellationToken.None);

        Assert.Equal("newplaylist", id);
        Assert.False(scope.Handler.CreatedPublic);
        Assert.Equal(1, scope.Handler.CreateCount);
    }

    [Fact]
    public async Task PlaylistOwnership_UsesLegacyOwnerIdWhileAccountLinkUsesStableAccountId()
    {
        await using var scope = await TestScope.CreateAsync();
        await scope.ChangeExternalAccountIdAsync("stable-account-id");
        scope.Handler.ProfileAccountId = "stable-account-id";
        scope.Handler.ProfileId = "legacy-user-id";
        scope.Handler.OwnerId = "legacy-user-id";
        scope.Handler.CreatedOwnerId = "legacy-user-id";
        var account = scope.Account with { ExpectedExternalAccountId = "stable-account-id" };

        var createdId = await scope.Writer.CreatePlaylistAsync(account, "Road songs", CancellationToken.None);
        await scope.Writer.ValidateDestinationAsync(account, "playlist1", CancellationToken.None);

        Assert.Equal("newplaylist", createdId);
        Assert.Equal("stable-account-id", await scope.GetExternalAccountIdAsync());
    }

    [Fact]
    public async Task CreatePlaylistAsync_RequiresPrivateWriteScopeBeforePosting()
    {
        await using var scope = await TestScope.CreateAsync(scopes: "playlist-modify-public user-read-private");

        var failure = await Assert.ThrowsAsync<PlatformApiException>(() =>
            scope.Writer.CreatePlaylistAsync(scope.Account, "Road songs", CancellationToken.None));

        Assert.Equal("spotify_playlist_creation_rejected", failure.Code);
        Assert.Equal(0, scope.Handler.CreateCount);
    }

    [Fact]
    public async Task CreatePlaylistAsync_ChangedPinnedAccountFailsBeforePost()
    {
        await using var scope = await TestScope.CreateAsync();

        var failure = await Assert.ThrowsAsync<PlatformApiException>(() =>
            scope.Writer.CreatePlaylistAsync(
                scope.Account with { ExpectedExternalAccountId = "other-user" },
                "Road songs", CancellationToken.None));

        Assert.Equal("spotify_playlist_creation_rejected", failure.Code);
        Assert.Equal(0, scope.Handler.CreateCount);
    }

    [Fact]
    public async Task CreatePlaylistAsync_AmbiguousNetworkFailureDoesNotRetryPost()
    {
        await using var scope = await TestScope.CreateAsync();
        scope.Handler.FailCreationAfterApplying = true;

        var failure = await Assert.ThrowsAsync<PlatformApiException>(() =>
            scope.Writer.CreatePlaylistAsync(scope.Account, "Road songs", CancellationToken.None));

        Assert.Equal("spotify_playlist_creation_unknown", failure.Code);
        Assert.False(MusicSyncFailureClassifier.Classify(failure).Retryable);
        Assert.Equal(1, scope.Handler.CreateCount);
    }

    [Fact]
    public async Task CreatePlaylistAsync_ExplicitProviderRejectionIsDistinctFromUnknownOutcome()
    {
        await using var scope = await TestScope.CreateAsync();
        scope.Handler.CreationResponseStatus = HttpStatusCode.Forbidden;

        var failure = await Assert.ThrowsAsync<PlatformApiException>(() =>
            scope.Writer.CreatePlaylistAsync(scope.Account, "Road songs", CancellationToken.None));

        Assert.Equal("spotify_playlist_creation_rejected", failure.Code);
        Assert.Equal(403, failure.StatusCode);
        Assert.Equal(1, scope.Handler.CreateCount);
    }

    [Fact]
    public async Task CreatePlaylistAsync_PreservesQuotaRejectionAndRetryAfter()
    {
        await using var scope = await TestScope.CreateAsync();
        scope.Handler.CreationResponseStatus = HttpStatusCode.TooManyRequests;
        scope.Handler.CreationQuotaExceeded = true;
        scope.Handler.CreationRetryAfter = TimeSpan.FromHours(3);

        var failure = await Assert.ThrowsAsync<PlatformApiException>(() =>
            scope.Writer.CreatePlaylistAsync(scope.Account, "Road songs", CancellationToken.None));

        Assert.Equal("spotify_quota_exceeded", failure.Code);
        Assert.Equal(429, failure.StatusCode);
        Assert.Equal(TimeSpan.FromHours(3), failure.RetryAfter);
        Assert.Equal(1, scope.Handler.CreateCount);
    }

    [Fact]
    public async Task CreatePlaylistAsync_TimeoutResponseHasUnknownOutcome()
    {
        await using var scope = await TestScope.CreateAsync();
        scope.Handler.CreationResponseStatus = HttpStatusCode.RequestTimeout;

        var failure = await Assert.ThrowsAsync<PlatformApiException>(() =>
            scope.Writer.CreatePlaylistAsync(scope.Account, "Road songs", CancellationToken.None));

        Assert.Equal("spotify_playlist_creation_unknown", failure.Code);
        Assert.Equal(1, scope.Handler.CreateCount);
    }

    private sealed class TestScope : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly HttpClient _httpClient;
        private readonly ApplicationDbContext _dbContext;

        private TestScope(
            SqliteConnection connection,
            HttpClient httpClient,
            ApplicationDbContext dbContext,
            PlaylistHandler handler,
            SpotifyPlaylistWriter writer,
            PlatformAccountContext account,
            MutableTimeProvider time)
        {
            _connection = connection;
            _httpClient = httpClient;
            _dbContext = dbContext;
            Handler = handler;
            Writer = writer;
            Account = account;
            Time = time;
        }

        public PlaylistHandler Handler { get; }
        public SpotifyPlaylistWriter Writer { get; }
        public PlatformAccountContext Account { get; }
        public MutableTimeProvider Time { get; }

        public Task<string> GetConnectionStateAsync()
            => _dbContext.ConnectedServiceAccounts
                .AsNoTracking()
                .Where(account => account.Id == Account.ConnectedServiceAccountId)
                .Select(account => account.ConnectionState)
                .SingleAsync();

        public Task<string> GetExternalAccountIdAsync()
            => _dbContext.ConnectedServiceAccounts
                .AsNoTracking()
                .Where(account => account.Id == Account.ConnectedServiceAccountId)
                .Select(account => account.ExternalAccountId)
                .SingleAsync();

        public Task ChangeExternalAccountIdAsync(string externalAccountId)
            => _dbContext.ConnectedServiceAccounts
                .Where(account => account.Id == Account.ConnectedServiceAccountId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(account => account.ExternalAccountId, externalAccountId));

        public static async Task<TestScope> CreateAsync(string scopes = "playlist-read-private playlist-modify-private playlist-modify-public user-read-private")
        {
            var connectionString = $"Data Source=spotify-writer-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connectionString)
                .Options;
            var dbContext = new ApplicationDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();
            const int userId = 77;
            dbContext.Users.Add(new User
            {
                Id = userId,
                UserName = "spotify-writer-test",
                CreatedAt = Now.UtcDateTime,
                UpdatedAt = Now.UtcDateTime
            });
            var encryption = new TokenEncryptionService(new EphemeralDataProtectionProvider());
            var account = new ConnectedServiceAccount
            {
                UserId = userId,
                Service = SpotifyService.ServiceName,
                ExternalAccountId = "spotify-user",
                EncryptedAccessToken = encryption.Encrypt("access-token"),
                EncryptedRefreshToken = encryption.Encrypt("refresh-token"),
                TokenExpiresAt = Now.AddHours(1).UtcDateTime,
                RefreshTokenExpiresAt = Now.AddDays(30).UtcDateTime,
                ConnectionState = "connected",
                Scopes = scopes,
                CreatedAt = Now.UtcDateTime,
                UpdatedAt = Now.UtcDateTime
            };
            dbContext.ConnectedServiceAccounts.Add(account);
            await dbContext.SaveChangesAsync();

            var handler = new PlaylistHandler();
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.spotify.com") };
            var time = new MutableTimeProvider();
            var apiClient = new SpotifyApiClient(
                httpClient,
                Options.Create(new SpotifyOptions { ClientId = "client", ClientSecret = "secret" }),
                new NoDelay(),
                new SpotifyCooldown(time));
            var tokenManager = new SpotifyTokenManager(
                dbContext,
                apiClient,
                encryption,
                time,
                new SpotifyTestServiceScopeFactory(options));
            var writer = new SpotifyPlaylistWriter(dbContext, apiClient, tokenManager);
            return new TestScope(
                connection,
                httpClient,
                dbContext,
                handler,
                writer,
                new PlatformAccountContext(userId, account.Id),
                time);
        }

        public async ValueTask DisposeAsync()
        {
            _httpClient.Dispose();
            await _dbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class PlaylistHandler : HttpMessageHandler
    {
        private bool _failedAppend;
        private bool _ignoredAppend;

        public List<string> TrackIds { get; } = [];
        public List<RecordedRequest> Requests { get; } = [];
        public string OwnerId { get; set; } = "spotify-user";
        public bool IsPublic { get; set; }
        public bool IncludeLocalItem { get; set; }
        public bool FailFirstAppendAfterApplying { get; set; }
        public bool IgnoreFirstAppend { get; set; }
        public bool FailCreationAfterApplying { get; set; }
        public HttpStatusCode? CreationResponseStatus { get; set; }
        public bool CreationQuotaExceeded { get; set; }
        public TimeSpan? CreationRetryAfter { get; set; }
        public bool? CreationPublicResponse { get; set; } = false;
        public string ProfileId { get; set; } = "spotify-user";
        public string? ProfileAccountId { get; set; }
        public string? CreatedOwnerId { get; set; }
        public int CreateCount { get; private set; }
        public string? CreatedName { get; private set; }
        public bool? CreatedPublic { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var writeIds = Array.Empty<string>();
            if (body is not null && path.EndsWith("/items", StringComparison.Ordinal))
            {
                using var parsed = JsonDocument.Parse(body);
                writeIds = parsed.RootElement.GetProperty("uris")
                    .EnumerateArray()
                    .Select(uri => uri.GetString()!["spotify:track:".Length..])
                    .ToArray();
            }

            Requests.Add(new RecordedRequest(
                request.Method,
                path,
                request.Headers.Authorization?.Scheme,
                writeIds));
            if (request.Method == HttpMethod.Get && path == "/v1/me")
            {
                return Json(HttpStatusCode.OK, new { id = ProfileId, account_id = ProfileAccountId });
            }

            if (request.Method == HttpMethod.Post && path == "/v1/me/playlists")
            {
                using var parsed = JsonDocument.Parse(body!);
                CreatedName = parsed.RootElement.GetProperty("name").GetString();
                CreatedPublic = parsed.RootElement.GetProperty("public").GetBoolean();
                CreateCount++;
                if (FailCreationAfterApplying)
                    throw new HttpRequestException("Response lost after playlist creation.");
                if (CreationResponseStatus is { } status)
                {
                    var response = Json(status, new
                    {
                        error = new { status = (int)status, message = "Creation rejected", reason = CreationQuotaExceeded ? "QUOTA_EXCEEDED" : null }
                    });
                    if (CreationRetryAfter is { } retryAfter)
                        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(retryAfter);
                    return response;
                }
                return Json(HttpStatusCode.Created, new
                {
                    id = "newplaylist", owner = new { id = CreatedOwnerId ?? ProfileId }, @public = CreationPublicResponse
                });
            }

            if (request.Method == HttpMethod.Get && path == "/v1/playlists/playlist1")
            {
                return Json(HttpStatusCode.OK, new
                {
                    id = "playlist1",
                    owner = new { id = OwnerId },
                    @public = IsPublic,
                    collaborative = false
                });
            }

            if (request.Method == HttpMethod.Get && path == "/v1/playlists/playlist1/items")
            {
                var parameters = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(request.RequestUri.Query);
                var offset = int.Parse(parameters["offset"]!);
                var items = TrackIds
                    .Skip(offset)
                    .Take(50)
                    .Select(id => (object)new { is_local = false, item = new { type = "track", id } })
                    .ToList();
                if (IncludeLocalItem && offset == 0)
                {
                    items.Add(new { is_local = true, item = new { type = "track", id = "local" } });
                }

                return Json(HttpStatusCode.OK, new
                {
                    items,
                    next = offset + 50 < TrackIds.Count ? "next-page" : null,
                    total = TrackIds.Count + (IncludeLocalItem ? 1 : 0)
                });
            }

            if (request.Method == HttpMethod.Put && path == "/v1/playlists/playlist1/items")
            {
                TrackIds.Clear();
                TrackIds.AddRange(writeIds);
                IncludeLocalItem = false;
                return Json(HttpStatusCode.OK, new { snapshot_id = "new" });
            }

            if (request.Method == HttpMethod.Post && path == "/v1/playlists/playlist1/items")
            {
                if (IgnoreFirstAppend && !_ignoredAppend)
                {
                    _ignoredAppend = true;
                    return Json(HttpStatusCode.Created, new { snapshot_id = "unchanged" });
                }

                TrackIds.AddRange(writeIds);
                if (FailFirstAppendAfterApplying && !_failedAppend)
                {
                    _failedAppend = true;
                    return Json(HttpStatusCode.TooManyRequests, new { error = new { status = 429, message = "Slow down" } });
                }

                return Json(HttpStatusCode.Created, new { snapshot_id = "new" });
            }

            throw new InvalidOperationException($"Unexpected Spotify request: {request.Method} {request.RequestUri}");
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object body)
        => new(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };

    private sealed record RecordedRequest(HttpMethod Method, string Path, string? AuthorizationScheme, string[] TrackIds);

    private sealed class NoDelay : ISpotifyRetryDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = Now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now += duration;
    }
}
