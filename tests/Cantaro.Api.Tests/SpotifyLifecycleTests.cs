using System.Net;
using System.Text;
using Cantaro.Api.Configuration;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Cantaro.Api.Services.Spotify;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.WebUtilities;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class SpotifyLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAuthorizationUrl_UsesAuthorizationCodeAndMinimumDocumentedScopes()
    {
        await using var scope = await SpotifyTestScope.CreateAsync();

        var authorizationUrl = scope.Service.GetAuthorizationUrl(
            "https://cantaro.example/api/platforms/spotify/callback",
            "protected-state");
        var uri = new Uri(authorizationUrl);
        var query = QueryHelpers.ParseQuery(uri.Query);

        Assert.Equal("accounts.spotify.com", uri.Host);
        Assert.Equal("/authorize", uri.AbsolutePath);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("test-client", query["client_id"]);
        Assert.Equal("protected-state", query["state"]);
        Assert.Equal(
            "playlist-read-private playlist-read-collaborative user-read-private",
            query["scope"]);
        Assert.DoesNotContain("user-read-email", query["scope"].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExchangeCodeAndSaveAsync_StoresStableProfileAndSixMonthRefreshExpiry()
    {
        await using var scope = await SpotifyTestScope.CreateAsync(
            Json(HttpStatusCode.OK, """
                {
                  "access_token": "initial-access",
                  "expires_in": 3600,
                  "refresh_token": "initial-refresh",
                  "scope": "playlist-read-private playlist-read-collaborative user-read-private"
                }
                """),
            Json(HttpStatusCode.OK, """
                {
                  "account_id": "stable-account-id",
                  "id": "legacy-profile-id",
                  "display_name": "Spotify listener",
                  "email": "must-not-be-used@example.test"
                }
                """));

        var account = await scope.Service.ExchangeCodeAndSaveAsync(
            scope.UserId,
            "authorization-code",
            "https://cantaro.example/api/platforms/spotify/callback",
            CancellationToken.None);

        Assert.Equal("stable-account-id", account.ExternalAccountId);
        Assert.Equal("Spotify listener", account.DisplayName);
        Assert.Equal("initial-access", scope.Encryption.Decrypt(account.EncryptedAccessToken!));
        Assert.Equal("initial-refresh", scope.Encryption.Decrypt(account.EncryptedRefreshToken!));
        Assert.Equal(Now.AddHours(1).UtcDateTime, account.TokenExpiresAt);
        Assert.Equal(Now.AddMonths(6).UtcDateTime, account.RefreshTokenExpiresAt);
        Assert.Equal(SpotifyService.AuthorizationScopes, account.Scopes);
        Assert.Equal("connected", account.ConnectionState);
        Assert.Equal(2, scope.Handler.Requests.Count);
    }

    [Fact]
    public async Task GetAccessTokenAsync_ReusesUnexpiredAccessToken()
    {
        await using var scope = await SpotifyTestScope.CreateAsync();
        await scope.AddAccountAsync(
            accessToken: "cached-access",
            accessTokenExpiresAt: Now.AddMinutes(10).UtcDateTime,
            refreshToken: "unused-refresh");

        var accessToken = await scope.TokenManager.GetAccessTokenAsync(
            scope.UserId,
            forceRefresh: false,
            CancellationToken.None);

        Assert.Equal("cached-access", accessToken);
        Assert.Empty(scope.Handler.Requests);
    }

    [Fact]
    public async Task GetAccessTokenAsync_PersistsRotatedRefreshToken()
    {
        await using var scope = await SpotifyTestScope.CreateAsync(
            Json(HttpStatusCode.OK, """
                {
                  "access_token": "new-access",
                  "expires_in": 3600,
                  "refresh_token": "rotated-refresh",
                  "scope": "playlist-read-private"
                }
                """));
        await scope.AddAccountAsync(
            accessToken: "expired-access",
            accessTokenExpiresAt: Now.AddMinutes(-1).UtcDateTime,
            refreshToken: "old-refresh");

        var accessToken = await scope.TokenManager.GetAccessTokenAsync(
            scope.UserId,
            forceRefresh: false,
            CancellationToken.None);

        scope.DbContext.ChangeTracker.Clear();
        var account = await scope.DbContext.ConnectedServiceAccounts.SingleAsync();
        Assert.Equal("new-access", accessToken);
        Assert.Equal("new-access", scope.Encryption.Decrypt(account.EncryptedAccessToken!));
        Assert.Equal("rotated-refresh", scope.Encryption.Decrypt(account.EncryptedRefreshToken!));
        Assert.Equal(Now.AddHours(1).UtcDateTime, account.TokenExpiresAt);
        Assert.Equal("playlist-read-private", account.Scopes);
        Assert.Equal("connected", account.ConnectionState);
    }

    [Fact]
    public async Task GetAccessTokenAsync_ExpiredRefreshTokenPersistsReconnectRequired()
    {
        await using var scope = await SpotifyTestScope.CreateAsync();
        await scope.AddAccountAsync(
            accessToken: "expired-access",
            accessTokenExpiresAt: Now.AddMinutes(-1).UtcDateTime,
            refreshToken: "expired-refresh",
            refreshTokenExpiresAt: Now.AddSeconds(-1).UtcDateTime);

        await Assert.ThrowsAsync<PlatformReconnectRequiredException>(
            () => scope.TokenManager.GetAccessTokenAsync(
                scope.UserId,
                forceRefresh: false,
                CancellationToken.None));

        scope.DbContext.ChangeTracker.Clear();
        var account = await scope.DbContext.ConnectedServiceAccounts.SingleAsync();
        Assert.Equal("reconnect_required", account.ConnectionState);
        Assert.Equal("refresh_token_expired", account.ReconnectReason);
        Assert.Equal(Now.UtcDateTime, account.ReconnectRequiredAt);
        Assert.Null(account.EncryptedAccessToken);
        Assert.Null(account.EncryptedRefreshToken);
        Assert.Empty(scope.Handler.Requests);
    }

    [Fact]
    public async Task GetAccessTokenAsync_InvalidRefreshTokenPersistsReconnectRequired()
    {
        await using var scope = await SpotifyTestScope.CreateAsync(
            Json(HttpStatusCode.BadRequest, """
                {
                  "error": "invalid_grant",
                  "error_description": "Refresh token revoked"
                }
                """));
        await scope.AddAccountAsync(
            accessToken: "expired-access",
            accessTokenExpiresAt: Now.AddMinutes(-1).UtcDateTime,
            refreshToken: "revoked-refresh");

        await Assert.ThrowsAsync<PlatformReconnectRequiredException>(
            () => scope.TokenManager.GetAccessTokenAsync(
                scope.UserId,
                forceRefresh: false,
                CancellationToken.None));

        scope.DbContext.ChangeTracker.Clear();
        var account = await scope.DbContext.ConnectedServiceAccounts.SingleAsync();
        Assert.Equal("reconnect_required", account.ConnectionState);
        Assert.Equal("refresh_token_invalid", account.ReconnectReason);
        Assert.Equal(Now.UtcDateTime, account.ReconnectRequiredAt);
        Assert.Null(account.EncryptedAccessToken);
        Assert.Null(account.EncryptedRefreshToken);
        Assert.Single(scope.Handler.Requests);
    }

    [Fact]
    public async Task GetPlaylistsAsync_SecondUnauthorizedResponsePersistsReconnectRequired()
    {
        await using var scope = await SpotifyTestScope.CreateAsync(
            Json(HttpStatusCode.Unauthorized, """{ "error": { "status": 401, "message": "Expired" } }"""),
            Json(HttpStatusCode.OK, """
                {
                  "access_token": "refreshed-access",
                  "expires_in": 3600,
                  "refresh_token": "rotated-refresh"
                }
                """),
            Json(HttpStatusCode.Unauthorized, """{ "error": { "status": 401, "message": "Still rejected" } }"""));
        await scope.AddAccountAsync(
            accessToken: "cached-access",
            accessTokenExpiresAt: Now.AddMinutes(10).UtcDateTime,
            refreshToken: "old-refresh");

        await Assert.ThrowsAsync<PlatformReconnectRequiredException>(
            () => scope.Service.GetPlaylistsAsync(scope.UserId, CancellationToken.None));

        scope.DbContext.ChangeTracker.Clear();
        var account = await scope.DbContext.ConnectedServiceAccounts.SingleAsync();
        Assert.Equal("reconnect_required", account.ConnectionState);
        Assert.Equal("access_token_rejected", account.ReconnectReason);
        Assert.Equal(Now.UtcDateTime, account.ReconnectRequiredAt);
        Assert.Null(account.EncryptedAccessToken);
        Assert.Null(account.EncryptedRefreshToken);
        Assert.Equal(3, scope.Handler.Requests.Count);
    }

    [Fact]
    public async Task DisconnectAsync_RemovesSpotifyPersonalDataAndPreservesCanonicalTrack()
    {
        await using var scope = await SpotifyTestScope.CreateAsync();
        var account = await scope.AddAccountAsync(
            accessToken: "cached-access",
            accessTokenExpiresAt: Now.AddMinutes(10).UtcDateTime,
            refreshToken: "refresh-token");
        var playlistId = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        var observationId = Guid.NewGuid();
        var candidateId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var mappingId = Guid.NewGuid();

        scope.DbContext.Tracks.Add(new Track
        {
            Id = trackId,
            CanonicalMetadata = """{ "title": "Canonical song" }""",
            CreatedAt = Now,
            UpdatedAt = Now
        });
        scope.DbContext.TrackObservations.Add(new TrackObservation
        {
            Id = observationId,
            SourceType = SpotifyService.ServiceName,
            ExternalId = "spotify-track-1",
            RawMetadata = """{ "provider": "spotify" }""",
            Title = "Provider title",
            Artist = "Provider artist",
            MatchStatus = TrackMatchingStatuses.Matched,
            TrackId = trackId,
            CreatedAt = Now,
            UpdatedAt = Now
        });
        scope.DbContext.TrackResolutionCandidates.Add(new TrackResolutionCandidate
        {
            Id = candidateId,
            TrackObservationId = observationId,
            CandidateSource = "musicbrainz",
            ExternalId = "candidate-1",
            Title = "Candidate title",
            Score = 0.9m,
            CreatedAt = Now
        });
        scope.DbContext.TrackSourceIds.Add(new TrackSourceId
        {
            Id = sourceId,
            TrackId = trackId,
            SourceType = SpotifyService.ServiceName,
            ExternalId = "spotify-track-1"
        });
        scope.DbContext.Playlists.Add(new Playlist
        {
            Id = playlistId,
            UserId = scope.UserId,
            Name = "Imported Spotify playlist",
            ImportedFromService = SpotifyService.ServiceName,
            CreatedAt = Now,
            UpdatedAt = Now,
            Entries =
            [
                new PlaylistEntry
                {
                    Id = Guid.NewGuid(),
                    TrackId = trackId,
                    TrackObservationId = observationId,
                    Position = 0,
                    AddedAt = Now,
                    SourceService = SpotifyService.ServiceName
                }
            ],
            ServiceMappings =
            [
                new ServicePlaylistMapping
                {
                    Id = mappingId,
                    ConnectedServiceAccountId = account.Id,
                    Service = SpotifyService.ServiceName,
                    ServicePlaylistId = "spotify-playlist-1",
                    SyncMode = "import_only"
                }
            ]
        });
        scope.DbContext.MusicSyncJobs.Add(new MusicSyncJob
        {
            Id = Guid.NewGuid(),
            UserId = scope.UserId,
            Service = SpotifyService.ServiceName,
            PlaylistsJson = """[{"id":"spotify-playlist-1","name":"Imported Spotify playlist","songCount":1}]""",
            ResultsJson = """[{"servicePlaylistId":"spotify-playlist-1","playlistName":"Imported Spotify playlist","success":true}]""",
            Status = MusicSyncJobStatuses.Completed,
            PlaylistCount = 1,
            SongCount = 1,
            CreatedAt = Now,
            UpdatedAt = Now
        });
        await scope.DbContext.SaveChangesAsync();

        await scope.Service.DisconnectAsync(scope.UserId, CancellationToken.None);

        scope.DbContext.ChangeTracker.Clear();
        Assert.False(await scope.DbContext.ConnectedServiceAccounts.AnyAsync());
        Assert.False(await scope.DbContext.Playlists.AnyAsync(playlist => playlist.Id == playlistId));
        Assert.False(await scope.DbContext.ServicePlaylistMappings.AnyAsync(mapping => mapping.Id == mappingId));
        Assert.False(await scope.DbContext.TrackObservations.AnyAsync(observation => observation.Id == observationId));
        Assert.False(await scope.DbContext.TrackResolutionCandidates.AnyAsync(candidate => candidate.Id == candidateId));
        Assert.False(await scope.DbContext.TrackSourceIds.AnyAsync(source => source.Id == sourceId));
        Assert.False(await scope.DbContext.MusicSyncJobs.AnyAsync(
            job => job.UserId == scope.UserId && job.Service == SpotifyService.ServiceName));
        Assert.True(await scope.DbContext.Tracks.AnyAsync(track => track.Id == trackId));
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string body)
        => new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private sealed class SpotifyTestScope : IAsyncDisposable
    {
        private SpotifyTestScope(
            SqliteConnection connection,
            ApplicationDbContext dbContext,
            RecordingHandler handler,
            TokenEncryptionService encryption,
            SpotifyTokenManager tokenManager,
            SpotifyService service)
        {
            Connection = connection;
            DbContext = dbContext;
            Handler = handler;
            Encryption = encryption;
            TokenManager = tokenManager;
            Service = service;
        }

        private const int TestUserId = 42;

        private SqliteConnection Connection { get; }
        public int UserId => TestUserId;
        public ApplicationDbContext DbContext { get; }
        public RecordingHandler Handler { get; }
        public TokenEncryptionService Encryption { get; }
        public SpotifyTokenManager TokenManager { get; }
        public SpotifyService Service { get; }

        public static async Task<SpotifyTestScope> CreateAsync(params HttpResponseMessage[] responses)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var dbContext = new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseSqlite(connection)
                    .Options);
            await dbContext.Database.EnsureCreatedAsync();
            dbContext.Users.Add(new User
            {
                Id = TestUserId,
                UserName = "spotify-tests",
                CreatedAt = Now.UtcDateTime,
                UpdatedAt = Now.UtcDateTime
            });
            await dbContext.SaveChangesAsync();

            var handler = new RecordingHandler(responses);
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
            var apiClient = new SpotifyApiClient(httpClient, options, new NoDelay());
            var encryption = new TokenEncryptionService(new EphemeralDataProtectionProvider());
            var timeProvider = new FixedTimeProvider(Now);
            var tokenManager = new SpotifyTokenManager(
                dbContext,
                apiClient,
                encryption,
                new SpotifyTokenRefreshCoordinator(),
                timeProvider);
            var service = new SpotifyService(
                dbContext,
                apiClient,
                tokenManager,
                encryption,
                options,
                timeProvider);
            return new SpotifyTestScope(connection, dbContext, handler, encryption, tokenManager, service);
        }

        public async Task<ConnectedServiceAccount> AddAccountAsync(
            string accessToken,
            DateTime accessTokenExpiresAt,
            string refreshToken,
            DateTime? refreshTokenExpiresAt = null)
        {
            var account = new ConnectedServiceAccount
            {
                UserId = TestUserId,
                Service = SpotifyService.ServiceName,
                ExternalAccountId = "spotify-user",
                EncryptedAccessToken = Encryption.Encrypt(accessToken),
                EncryptedRefreshToken = Encryption.Encrypt(refreshToken),
                TokenExpiresAt = accessTokenExpiresAt,
                RefreshTokenExpiresAt = refreshTokenExpiresAt ?? Now.AddDays(30).UtcDateTime,
                ConnectionState = "connected",
                CreatedAt = Now.UtcDateTime,
                UpdatedAt = Now.UtcDateTime
            };
            DbContext.ConnectedServiceAccounts.Add(account);
            await DbContext.SaveChangesAsync();
            return account;
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    private sealed class RecordingHandler(IEnumerable<HttpResponseMessage> responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class NoDelay : ISpotifyRetryDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
