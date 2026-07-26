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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class SpotifyPlaylistSyncServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SyncPlaylistAsync_FetchesBeforeTransactionAndRefreshesProviderObservation()
    {
        var connectionString = $"Data Source=spotify-sync-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        var dbContextOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connectionString)
            .Options;
        await using var dbContext = new ApplicationDbContext(dbContextOptions);
        await dbContext.Database.EnsureCreatedAsync();

        const int userId = 73;
        dbContext.Users.Add(new User
        {
            Id = userId,
            UserName = "spotify-sync-test",
            CreatedAt = Now.UtcDateTime,
            UpdatedAt = Now.UtcDateTime
        });
        var encryption = new TokenEncryptionService(new EphemeralDataProtectionProvider());
        dbContext.ConnectedServiceAccounts.Add(new ConnectedServiceAccount
        {
            UserId = userId,
            Service = SpotifyService.ServiceName,
            ExternalAccountId = "spotify-account",
            EncryptedAccessToken = encryption.Encrypt("cached-access"),
            EncryptedRefreshToken = encryption.Encrypt("refresh-token"),
            TokenExpiresAt = Now.AddMinutes(30).UtcDateTime,
            RefreshTokenExpiresAt = Now.AddDays(30).UtcDateTime,
            ConnectionState = "connected",
            CreatedAt = Now.UtcDateTime,
            UpdatedAt = Now.UtcDateTime
        });
        await dbContext.SaveChangesAsync();

        var handler = new TransactionCheckingHandler(
            () => dbContext.Database.CurrentTransaction is not null,
            PlaylistPage("First playlist name", "First description"),
            ItemPage("Original provider title", "Original artist"),
            PlaylistPage("Refreshed playlist name", "Refreshed description"),
            ItemPage("Refreshed provider title", "Refreshed artist"));
        var options = Options.Create(new SpotifyOptions
        {
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RedirectUri = "https://cantaro.example/api/platforms/spotify/callback"
        });
        var apiClient = new SpotifyApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.spotify.com") },
            options,
            new NoDelay());
        var timeProvider = new FixedTimeProvider(Now);
        var tokenManager = new SpotifyTokenManager(
            dbContext,
            apiClient,
            encryption,
            timeProvider,
            new SpotifyTestServiceScopeFactory(dbContextOptions));
        var spotifyService = new SpotifyService(
            dbContext,
            apiClient,
            tokenManager,
            encryption,
            options,
            timeProvider);
        var matchingQueue = new TrackMatchingQueue();
        var syncService = new SpotifyPlaylistSyncService(
            dbContext,
            spotifyService,
            matchingQueue,
            timeProvider,
            NullLogger<SpotifyPlaylistSyncService>.Instance);

        var playlistId = await syncService.SyncPlaylistAsync(userId, "playlist1", CancellationToken.None);
        var firstObservationId = await dbContext.PlaylistEntries
            .Where(entry => entry.PlaylistId == playlistId)
            .Select(entry => entry.TrackObservationId)
            .SingleAsync();
        matchingQueue.Complete((await matchingQueue.DequeueAsync(CancellationToken.None)));

        var refreshedPlaylistId = await syncService.SyncPlaylistAsync(
            userId,
            "playlist1",
            CancellationToken.None);

        dbContext.ChangeTracker.Clear();
        var playlist = await dbContext.Playlists
            .Include(candidate => candidate.ServiceMappings)
            .Include(candidate => candidate.Entries)
                .ThenInclude(entry => entry.TrackObservation)
            .SingleAsync(candidate => candidate.Id == playlistId);
        var mapping = Assert.Single(playlist.ServiceMappings);
        var entry = Assert.Single(playlist.Entries);
        Assert.NotNull(entry.TrackObservation);
        var observation = entry.TrackObservation;

        Assert.Equal(playlistId, refreshedPlaylistId);
        Assert.Equal("Refreshed playlist name", playlist.Name);
        Assert.Equal("Refreshed description", playlist.Description);
        Assert.Equal(SpotifyService.ServiceName, playlist.ImportedFromService);
        Assert.Equal("import_only", mapping.SyncMode);
        Assert.Equal("success", mapping.LastSyncStatus);
        Assert.Equal(firstObservationId, observation.Id);
        Assert.Equal("Refreshed provider title", observation.Title);
        Assert.Equal("Refreshed artist", observation.Artist);
        Assert.Null(observation.ThumbnailUrl);
        Assert.Equal(TrackMatchingStatuses.Pending, observation.MatchStatus);
        Assert.Null(entry.TrackId);
        Assert.Equal(4, handler.RequestCount);
        Assert.False(handler.SawOpenTransaction);
    }

    private static HttpResponseMessage PlaylistPage(string name, string description)
        => Json(HttpStatusCode.OK, $$"""
            {
              "items": [{
                "id": "playlist1",
                "name": "{{name}}",
                "description": "{{description}}",
                "images": [{ "url": "https://i.scdn.co/image/temporary-playlist" }],
                "external_urls": { "spotify": "https://open.spotify.com/playlist/playlist1" },
                "snapshot_id": "snapshot1",
                "items": { "total": 1 }
              }],
              "next": null
            }
            """);

    private static HttpResponseMessage ItemPage(string title, string artist)
        => Json(HttpStatusCode.OK, $$"""
            {
              "items": [{
                "added_at": "2026-07-26T12:30:00Z",
                "is_local": false,
                "item": {
                  "type": "track",
                  "id": "track1",
                  "name": "{{title}}",
                  "duration_ms": 180000,
                  "artists": [{ "name": "{{artist}}" }],
                  "album": {
                    "name": "Album",
                    "images": [{ "url": "https://i.scdn.co/image/temporary-album" }],
                    "external_urls": { "spotify": "https://open.spotify.com/album/album1" }
                  },
                  "external_urls": { "spotify": "https://open.spotify.com/track/track1" }
                }
              }],
              "next": null
            }
            """);

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string body)
        => new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private sealed class TransactionCheckingHandler(
        Func<bool> hasOpenTransaction,
        params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public int RequestCount { get; private set; }
        public bool SawOpenTransaction { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            SawOpenTransaction |= hasOpenTransaction();
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
