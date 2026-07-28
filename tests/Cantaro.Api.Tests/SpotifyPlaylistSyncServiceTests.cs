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
            ClientSecret = "client-secret"
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
        var trackResolver = new SpotifyTrackResolver(dbContext);
        var syncService = new SpotifyPlaylistSyncService(
            dbContext,
            spotifyService,
            trackResolver,
            timeProvider,
            NullLogger<SpotifyPlaylistSyncService>.Instance);

        var playlistId = await syncService.SyncPlaylistAsync(userId, "playlist1", CancellationToken.None);
        var firstResolution = await dbContext.PlaylistEntries
            .Where(entry => entry.PlaylistId == playlistId)
            .Select(entry => new { entry.TrackObservationId, entry.TrackId })
            .SingleAsync();

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
        Assert.Equal(firstResolution.TrackObservationId, observation.Id);
        Assert.Equal(firstResolution.TrackId, observation.TrackId);
        Assert.Equal("Refreshed provider title", observation.Title);
        Assert.Equal("Refreshed artist", observation.Artist);
        Assert.Equal("https://i.scdn.co/image/temporary-album", observation.ThumbnailUrl);
        var observationMetadata = JsonSerializer.Deserialize<TrackObservationMetadata>(
            observation.RawMetadata!);
        Assert.Equal(
            "https://i.scdn.co/image/temporary-album",
            observationMetadata?.ThumbnailUrl);
        Assert.Equal(TrackMatchingStatuses.Matched, observation.MatchStatus);
        Assert.NotNull(entry.TrackId);
        Assert.Equal(observation.TrackId, entry.TrackId);
        Assert.Contains("authoritative Spotify", observation.ResolutionNotes);
        Assert.Equal(1, await dbContext.Tracks.CountAsync());
        Assert.Equal(1, await dbContext.Songs.CountAsync());
        var sourceIds = await dbContext.TrackSourceIds
            .OrderBy(sourceId => sourceId.ExternalId)
            .ToListAsync();
        Assert.Equal(2, sourceIds.Count);
        Assert.All(sourceIds, sourceId =>
        {
            Assert.Equal(SpotifyService.ServiceName, sourceId.SourceType);
            Assert.Equal(entry.TrackId, sourceId.TrackId);
        });
        Assert.Equal(["track1", "track2"], sourceIds.Select(sourceId => sourceId.ExternalId).ToArray());
        Assert.Equal(2, await dbContext.TrackObservations.CountAsync());
        Assert.Equal("USSP02600001", await dbContext.Tracks.Select(track => track.Isrc).SingleAsync());
        var canonicalMetadata = JsonSerializer.Deserialize<TrackCanonicalMetadata>(
            await dbContext.Tracks.Select(track => track.CanonicalMetadata).SingleAsync()
                ?? throw new InvalidOperationException("Expected canonical track metadata."));
        Assert.Equal(
            "https://i.scdn.co/image/temporary-album",
            canonicalMetadata?.ThumbnailUrl);
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
                  "external_ids": { "isrc": "USSP02600001" },
                  "album": {
                    "name": "Album",
                    "images": [{ "url": "https://i.scdn.co/image/temporary-album" }],
                    "external_urls": { "spotify": "https://open.spotify.com/album/album1" }
                  },
                  "external_urls": { "spotify": "https://open.spotify.com/track/track1" }
                }
              }, {
                "added_at": "2026-07-26T12:31:00Z",
                "is_local": false,
                "item": {
                  "type": "track",
                  "id": "track2",
                  "name": "Same recording on another release",
                  "duration_ms": 180000,
                  "artists": [{ "name": "Artist from another release" }],
                  "external_ids": { "isrc": "USSP02600001" },
                  "album": {
                    "name": "Another album",
                    "images": [],
                    "external_urls": { "spotify": "https://open.spotify.com/album/album2" }
                  },
                  "external_urls": { "spotify": "https://open.spotify.com/track/track2" }
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
