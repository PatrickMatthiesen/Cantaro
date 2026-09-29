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

public sealed class SpotifyPlaylistSyncProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PlaylistOperations_ReuseProfileForSameAccountAndToken()
    {
        await using var scope = await TestScope.CreateAsync();

        Assert.Single(await scope.Provider.ListPlaylistsAsync(scope.Account, CancellationToken.None));
        var snapshot = await scope.Provider.ReadAsync(scope.Account, "playlist1", CancellationToken.None);
        await scope.Provider.RenameAsync(scope.Account, "playlist1", "Renamed", CancellationToken.None);

        Assert.Equal("playlist1", snapshot.Id);
        Assert.Single(scope.Handler.ProfileRequests);
        Assert.Single(scope.Handler.Requests, request => request.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task ProfileValidation_RechecksWhenTokenOrLinkedAccountChanges()
    {
        await using var scope = await TestScope.CreateAsync();

        await scope.Provider.ListPlaylistsAsync(scope.Account, CancellationToken.None);
        await scope.ChangeTokenAsync("second-token", 1);
        await scope.Provider.ListPlaylistsAsync(scope.Account, CancellationToken.None);
        Assert.Equal(["first-token", "second-token"], scope.Handler.ProfileRequests);

        await scope.ChangeExternalAccountIdAsync("new-account");
        scope.Handler.ProfileId = "new-account";
        await scope.Provider.ListPlaylistsAsync(scope.Account, CancellationToken.None);
        Assert.Equal(["first-token", "second-token", "second-token"], scope.Handler.ProfileRequests);

        await scope.ChangeTokenAsync("third-token", 2);
        scope.Handler.ProfileId = "wrong-account";
        var error = await Assert.ThrowsAsync<PlatformApiException>(() =>
            scope.Provider.ListPlaylistsAsync(scope.Account, CancellationToken.None));
        Assert.Equal("spotify_account_changed", error.Code);
        Assert.Equal(4, scope.Handler.ProfileRequests.Count);
        Assert.Equal(3, scope.Handler.Requests.Count(request => request.Path == "/v1/me/playlists"));
    }

    [Fact]
    public async Task ProfileValidation_RechecksWhenProviderInstanceSwitchesAccounts()
    {
        await using var scope = await TestScope.CreateAsync();
        await scope.Provider.ListPlaylistsAsync(scope.Account, CancellationToken.None);

        var otherAccount = await scope.AddSecondAccountAsync();
        scope.Handler.ProfileId = "other-user";
        await scope.Provider.ListPlaylistsAsync(otherAccount, CancellationToken.None);

        Assert.Equal(2, scope.Handler.ProfileRequests.Count);
        Assert.Equal(["first-token", "first-token"], scope.Handler.ProfileRequests);

        scope.Handler.ProfileId = "spotify-user";
        await scope.Provider.ListPlaylistsAsync(scope.Account, CancellationToken.None);
        Assert.Equal(3, scope.Handler.ProfileRequests.Count);
    }

    [Fact]
    public async Task DisconnectedAccount_CannotReuseValidatedProfile()
    {
        await using var scope = await TestScope.CreateAsync();
        await scope.Provider.ListPlaylistsAsync(scope.Account, CancellationToken.None);

        await scope.ChangeConnectionStateAsync("reconnect_required");
        var error = await Assert.ThrowsAsync<PlatformApiException>(() =>
            scope.Provider.ListPlaylistsAsync(scope.Account, CancellationToken.None));

        Assert.Equal("spotify_account_changed", error.Code);
        Assert.Single(scope.Handler.ProfileRequests);
        Assert.Single(scope.Handler.Requests, request => request.Path == "/v1/me/playlists");
    }

    [Fact]
    public async Task CatalogueResolution_UsesLocalConnectionWithoutProfileRequestPerTrack()
    {
        await using var scope = await TestScope.CreateAsync();
        var entries = await scope.AddUnmatchedEntriesAsync(3);

        foreach (var entry in entries)
            Assert.Null(await scope.Provider.ResolveAsync(scope.Account, entry, CancellationToken.None));

        Assert.Contains(scope.Handler.Requests, request => request.Path == "/v1/search");
        Assert.Empty(scope.Handler.ProfileRequests);
        await scope.ChangeConnectionStateAsync("reconnect_required");
        var error = await Assert.ThrowsAsync<PlatformApiException>(() =>
            scope.Provider.ResolveAsync(scope.Account, entries[0], CancellationToken.None));
        Assert.Equal("spotify_account_changed", error.Code);
        Assert.Empty(scope.Handler.ProfileRequests);
    }

    private sealed class TestScope : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly HttpClient _httpClient;
        private readonly ApplicationDbContext _dbContext;
        private readonly TokenEncryptionService _encryption;

        private TestScope(SqliteConnection connection, HttpClient httpClient,
            ApplicationDbContext dbContext, TokenEncryptionService encryption,
            PlaylistHandler handler, SpotifyPlaylistSyncProvider provider, PlatformAccountContext account)
        {
            _connection = connection;
            _httpClient = httpClient;
            _dbContext = dbContext;
            _encryption = encryption;
            Handler = handler;
            Provider = provider;
            Account = account;
        }

        public PlaylistHandler Handler { get; }
        public SpotifyPlaylistSyncProvider Provider { get; }
        public PlatformAccountContext Account { get; }

        public Task ChangeTokenAsync(string token, long version)
        {
            var encryptedToken = _encryption.Encrypt(token);
            return _dbContext.ConnectedServiceAccounts
                .Where(account => account.Id == Account.ConnectedServiceAccountId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(account => account.EncryptedAccessToken, encryptedToken)
                    .SetProperty(account => account.TokenVersion, version));
        }

        public Task ChangeExternalAccountIdAsync(string externalAccountId)
            => _dbContext.ConnectedServiceAccounts
                .Where(account => account.Id == Account.ConnectedServiceAccountId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(account => account.ExternalAccountId, externalAccountId));

        public Task ChangeConnectionStateAsync(string state)
            => _dbContext.ConnectedServiceAccounts
                .Where(account => account.Id == Account.ConnectedServiceAccountId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(account => account.ConnectionState, state));

        public async Task<PlatformAccountContext> AddSecondAccountAsync()
        {
            const int userId = 88;
            _dbContext.Users.Add(new User
            {
                Id = userId, UserName = "other-spotify-provider-test",
                CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime
            });
            var account = new ConnectedServiceAccount
            {
                UserId = userId, Service = SpotifyService.ServiceName,
                ExternalAccountId = "other-user", ConnectionState = "connected",
                EncryptedAccessToken = _encryption.Encrypt("first-token"),
                EncryptedRefreshToken = _encryption.Encrypt("refresh-token"),
                TokenExpiresAt = Now.AddHours(1).UtcDateTime,
                RefreshTokenExpiresAt = Now.AddDays(1).UtcDateTime,
                Scopes = "playlist-read-private playlist-modify-private",
                CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime
            };
            _dbContext.ConnectedServiceAccounts.Add(account);
            await _dbContext.SaveChangesAsync();
            return new PlatformAccountContext(userId, account.Id);
        }

        public async Task<PlaylistEntry[]> AddUnmatchedEntriesAsync(int count)
        {
            var playlist = new Playlist
            {
                Id = Guid.NewGuid(), UserId = Account.UserId, Name = "Source",
                CreatedAt = Now, UpdatedAt = Now
            };
            _dbContext.Playlists.Add(playlist);
            var entries = Enumerable.Range(0, count).Select(index =>
            {
                var observation = new TrackObservation
                {
                    Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = $"video-{index}",
                    Title = $"Song {index}", Artist = "Artist", MatchStatus = TrackMatchingStatuses.Pending
                };
                _dbContext.TrackObservations.Add(observation);
                return new PlaylistEntry
                {
                    Id = Guid.NewGuid(), PlaylistId = playlist.Id, TrackObservationId = observation.Id,
                    Position = index, AddedAt = Now
                };
            }).ToArray();
            _dbContext.PlaylistEntries.AddRange(entries);
            await _dbContext.SaveChangesAsync();
            return entries;
        }

        public static async Task<TestScope> CreateAsync()
        {
            var connectionString = $"Data Source=spotify-sync-provider-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connectionString).Options;
            var dbContext = new ApplicationDbContext(dbOptions);
            await dbContext.Database.EnsureCreatedAsync();
            const int userId = 87;
            dbContext.Users.Add(new User
            {
                Id = userId, UserName = "spotify-provider-test",
                CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime
            });
            var encryption = new TokenEncryptionService(new EphemeralDataProtectionProvider());
            var account = new ConnectedServiceAccount
            {
                UserId = userId, Service = SpotifyService.ServiceName,
                ExternalAccountId = "spotify-user", ConnectionState = "connected",
                EncryptedAccessToken = encryption.Encrypt("first-token"),
                EncryptedRefreshToken = encryption.Encrypt("refresh-token"),
                TokenExpiresAt = Now.AddHours(1).UtcDateTime,
                RefreshTokenExpiresAt = Now.AddDays(1).UtcDateTime,
                Scopes = "playlist-read-private playlist-modify-private",
                CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime
            };
            dbContext.ConnectedServiceAccounts.Add(account);
            await dbContext.SaveChangesAsync();

            var handler = new PlaylistHandler();
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.spotify.com") };
            var spotifyOptions = Options.Create(new SpotifyOptions
            {
                ClientId = "client", ClientSecret = "secret"
            });
            var apiClient = new SpotifyApiClient(httpClient, spotifyOptions, new NoDelay());
            var tokenManager = new SpotifyTokenManager(dbContext, apiClient, encryption,
                new FixedTimeProvider(), new SpotifyTestServiceScopeFactory(dbOptions));
            var searchProvider = new SpotifySearchProvider(apiClient,
                new SpotifyCatalogTokenProvider(apiClient, spotifyOptions, TimeProvider.System,
                    new SpotifyCatalogTokenCache()));
            var provider = new SpotifyPlaylistSyncProvider(dbContext, apiClient, tokenManager,
                new SpotifyTrackResolver(dbContext), [searchProvider], null!, null!,
                Options.Create(new TrackMatchingOptions()));
            return new TestScope(connection, httpClient, dbContext, encryption, handler, provider,
                new PlatformAccountContext(userId, account.Id));
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
        public string ProfileId { get; set; } = "spotify-user";
        public List<string> ProfileRequests { get; } = [];
        public List<(HttpMethod Method, string Path)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add((request.Method, path));
            if (request.Method == HttpMethod.Get && path == "/v1/me")
            {
                ProfileRequests.Add(request.Headers.Authorization?.Parameter ?? "");
                return Task.FromResult(Json(HttpStatusCode.OK, new { id = ProfileId }));
            }
            if (request.Method == HttpMethod.Post && path == "/api/token")
                return Task.FromResult(Json(HttpStatusCode.OK,
                    new { access_token = "catalogue-token", expires_in = 3600, token_type = "Bearer" }));
            if (request.Method == HttpMethod.Get && path == "/v1/search")
                return Task.FromResult(Json(HttpStatusCode.OK,
                    new { tracks = new { items = Array.Empty<object>() } }));
            if (request.Method == HttpMethod.Get && path == "/v1/me/playlists")
                return Task.FromResult(Json(HttpStatusCode.OK,
                    new { items = new[] { new { id = "playlist1", name = "Playlist" } }, next = (string?)null }));
            if (request.Method == HttpMethod.Get && path == "/v1/playlists/playlist1")
                return Task.FromResult(Json(HttpStatusCode.OK, new
                {
                    id = "playlist1", name = "Playlist", snapshot_id = "snapshot1",
                    owner = new { id = ProfileId }, @public = false
                }));
            if (request.Method == HttpMethod.Get && path == "/v1/playlists/playlist1/items")
                return Task.FromResult(Json(HttpStatusCode.OK,
                    new { items = Array.Empty<object>(), total = 0, next = (string?)null }));
            if (request.Method == HttpMethod.Put && path == "/v1/playlists/playlist1")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            throw new InvalidOperationException($"Unexpected Spotify request: {request.Method} {request.RequestUri}");
        }

        private static HttpResponseMessage Json(HttpStatusCode status, object body)
            => new(status)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };
    }

    private sealed class NoDelay : ISpotifyRetryDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
