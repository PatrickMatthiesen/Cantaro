using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Cantaro.Api.Services.Spotify;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Cantaro.Api.Tests;

/// <summary>
/// PostgreSQL transaction regression coverage. The test target must be an explicit
/// loopback connection. Each test creates and drops a private schema inside it.
/// No connected provider in this fixture makes network calls.
/// </summary>
public sealed class PlaylistSyncCoordinatorPostgresTests
{
    private const int UserId = 420042;

    [PostgresFact]
    public async Task SpotifySearchCache_PersistsAndRefreshesEvenIfExpiryCleanupRemovedItsRow()
    {
        await using var schema = await SchemaContext.CreateAsync();
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(schema.Db.Database.GetConnectionString()));
        await using var provider = services.BuildServiceProvider();
        SpotifySearchCache Cache() => new(provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System);
        IReadOnlyList<SpotifyTrackSnapshot> tracks = [new("id", "Song", "Artist", ["Artist"], null, null,
            "https://open.spotify.com/track/id", null, null, 180, null, 0)];
        var requests = 0;
        Task<IReadOnlyList<SpotifyTrackSnapshot>> Search(CancellationToken _) { requests++; return Task.FromResult(tracks); }

        await Cache().GetOrSearchAsync("query", 10, Search, default);
        var reused = await Cache().GetOrSearchAsync("query", 10, Search, default);
        Assert.Equal("id", Assert.Single(reused).Id);
        Assert.Equal(1, requests);
        await schema.Db.SpotifySearchCacheEntries.ExecuteUpdateAsync(setters =>
            setters.SetProperty(item => item.ExpiresAt, DateTimeOffset.UtcNow.AddDays(-1)));

        await Cache().GetOrSearchAsync("query", 10, async ct =>
        {
            // Another process prunes the expired row while this query refreshes.
            await schema.Db.SpotifySearchCacheEntries.ExecuteDeleteAsync(ct);
            return await Search(ct);
        }, default);
        Assert.Equal(2, requests);
        Assert.Equal(1, await schema.Db.SpotifySearchCacheEntries.CountAsync());
        await Cache().GetOrSearchAsync("query", 10, Search, default);
        Assert.Equal(2, requests);
    }

    [PostgresFact]
    public async Task RunAsync_WorksWithRetryingExecutionStrategy()
    {
        await using var scenario = await Scenario.CreateAsync();
        var trackA = scenario.AddTrack("A");
        var trackB = scenario.AddTrack("B");
        var playlist = scenario.AddPlaylist("Road songs", [trackA]);
        var link = scenario.AddLink(playlist, "youtube", [trackA]);
        scenario.Provider("youtube").SetTracks(("A", trackA), ("B", trackB));
        scenario.Provider("youtube").SetRemoteItems("B", "A");
        await scenario.SaveAsync();

        await scenario.Coordinator.RunAsync(UserId, playlist.Id, "youtube", CancellationToken.None);

        Assert.Equal(new[] { "B", "A" }, await scenario.CanonicalTrackNamesAsync(playlist.Id));
        Assert.Equal(new[] { "B", "A" }, scenario.Provider("youtube").RemoteIds);
        Assert.Equal("success", (await scenario.ReloadLinkAsync(link.Id)).LastSyncStatus);
    }

    [PostgresFact]
    public async Task RunAsync_ReorderAndMembershipChangePreservesRetainedEntryIdsAndAddedAt()
    {
        await using var scenario = await Scenario.CreateAsync();
        var trackA = scenario.AddTrack("A");
        var trackB = scenario.AddTrack("B");
        var trackC = scenario.AddTrack("C");
        var trackD = scenario.AddTrack("D");
        var playlist = scenario.AddPlaylist("Road songs", [trackA, trackB, trackC]);
        scenario.AddLink(playlist, "youtube", [trackA, trackB, trackC]);
        scenario.Provider("youtube").SetTracks(("D", trackD));
        scenario.Provider("youtube").SetRemoteItems("B", "A", "D");
        var originalAddedAt = DateTimeOffset.UtcNow.AddDays(-4);
        foreach (var entry in scenario.Db.PlaylistEntries.Local.Where(entry => entry.PlaylistId == playlist.Id))
            entry.AddedAt = originalAddedAt.AddHours(entry.Position);
        await scenario.SaveAsync();
        var before = await scenario.Db.PlaylistEntries.AsNoTracking()
            .Where(entry => entry.PlaylistId == playlist.Id).ToDictionaryAsync(entry => entry.TrackId!.Value);
        scenario.Db.ChangeTracker.Clear();

        await scenario.Coordinator.RunAsync(UserId, playlist.Id, "youtube", CancellationToken.None);

        var after = await scenario.Db.PlaylistEntries.AsNoTracking()
            .Where(entry => entry.PlaylistId == playlist.Id).OrderBy(entry => entry.Position).ToListAsync();
        Assert.Equal(new[] { trackB, trackA, trackD }, after.Select(entry => entry.TrackId!.Value));
        Assert.Equal((before[trackB].Id, before[trackB].AddedAt), (after[0].Id, after[0].AddedAt));
        Assert.Equal((before[trackA].Id, before[trackA].AddedAt), (after[1].Id, after[1].AddedAt));
        Assert.DoesNotContain(after, entry => entry.Id == before[trackC].Id);
        Assert.DoesNotContain(before.Values, entry => entry.Id == after[2].Id);
    }

    [PostgresFact]
    public async Task ImportAsync_CommitsCanonicalPlaylistMappingAndEntries()
    {
        await using var scenario = await Scenario.CreateAsync();
        var trackA = scenario.AddTrack("A");
        scenario.Provider("spotify").SetTracks(("remote-A", trackA));
        scenario.Provider("spotify").SetRemoteItems("remote-A");
        await scenario.SaveAsync();

        var playlistId = await scenario.Coordinator.ImportAsync(
            new PlatformAccountContext(UserId, 8, "spotify-owner"), "spotify", "remote-playlist", CancellationToken.None);

        var playlist = await scenario.Db.Playlists.AsNoTracking().SingleAsync(item => item.Id == playlistId);
        Assert.Equal("Road songs", playlist.Name);
        Assert.Equal(new[] { "A" }, await scenario.CanonicalTrackNamesAsync(playlistId));
        var mapping = await scenario.Db.ServicePlaylistMappings.AsNoTracking()
            .SingleAsync(item => item.PlaylistId == playlistId);
        Assert.Equal("remote-playlist", mapping.ServicePlaylistId);
        Assert.Equal("success", mapping.LastSyncStatus);
    }

    [PostgresFact]
    public async Task InitializeLinkAsync_CombinesRemoteItemsAndActivatesLink()
    {
        await using var scenario = await Scenario.CreateAsync();
        var trackA = scenario.AddTrack("A");
        var trackB = scenario.AddTrack("B");
        var playlist = scenario.AddPlaylist("Road songs", [trackA]);
        var link = scenario.AddLink(playlist, "youtube", [trackB], state: "paused");
        link.InitialMode = "combine";
        scenario.Provider("youtube").SetTracks(("A", trackA), ("B", trackB));
        scenario.Provider("youtube").SetRemoteItems("B");
        await scenario.SaveAsync();

        await scenario.Coordinator.InitializeLinkAsync(UserId, playlist.Id, link.Id, "combine", CancellationToken.None);

        Assert.Equal(new[] { "A", "B" }, await scenario.CanonicalTrackNamesAsync(playlist.Id));
        var savedLink = await scenario.ReloadLinkAsync(link.Id);
        Assert.Equal("active", savedLink.State);
        Assert.Null(savedLink.InitialMode);
        Assert.NotNull(savedLink.BaselineJson);
    }

    [PostgresFact]
    public async Task InitializeLinkAsync_RollsBackDeletedEntriesWhenReplacementViolatesForeignKey()
    {
        await using var scenario = await Scenario.CreateAsync();
        var originalTrackId = scenario.AddTrack("A");
        var playlistId = scenario.AddPlaylist("Road songs", [originalTrackId]).Id;
        var remoteTrackId = Guid.NewGuid(); // Deliberately absent from Tracks, so PostgreSQL rejects the insert.
        var link = scenario.AddLink(scenario.Playlist(playlistId), "youtube", [originalTrackId], state: "paused");
        var originalBaseline = link.BaselineJson;
        scenario.Provider("youtube").SetTracks(("invalid-track", remoteTrackId));
        scenario.Provider("youtube").SetRemoteItems("invalid-track");
        await scenario.SaveAsync();

        await Assert.ThrowsAsync<DbUpdateException>(() => scenario.Coordinator.InitializeLinkAsync(
            UserId, playlistId, link.Id, "platform", CancellationToken.None));

        scenario.Db.ChangeTracker.Clear();
        var entries = await scenario.Db.PlaylistEntries.AsNoTracking()
            .Where(entry => entry.PlaylistId == playlistId).ToListAsync();
        Assert.Equal(originalTrackId, Assert.Single(entries).TrackId);
        var mapping = await scenario.Db.ServicePlaylistMappings.AsNoTracking().SingleAsync(item => item.Id == link.Id);
        Assert.Equal("paused", mapping.State);
        Assert.Equal(originalBaseline, mapping.BaselineJson);
    }

    [PostgresFact]
    public async Task ResolveOrderAsync_CommitsChosenPlatformOrder()
    {
        await using var scenario = await Scenario.CreateAsync();
        var trackA = scenario.AddTrack("A");
        var trackB = scenario.AddTrack("B");
        var trackC = scenario.AddTrack("C");
        var playlist = scenario.AddPlaylist("Road songs", [trackA, trackB, trackC]);
        var link = scenario.AddLink(playlist, "spotify", [trackA, trackB, trackC]);
        link.LastSyncStatus = "order_conflict";
        scenario.Provider("spotify").SetTracks(("A", trackA), ("B", trackB), ("C", trackC));
        scenario.Provider("spotify").SetRemoteItems("B", "A", "C");
        await scenario.SaveAsync();

        await scenario.Coordinator.ResolveOrderAsync(UserId, playlist.Id, link.Id, "platform", CancellationToken.None);

        Assert.Equal(new[] { "B", "A", "C" }, await scenario.CanonicalTrackNamesAsync(playlist.Id));
        var savedLink = await scenario.ReloadLinkAsync(link.Id);
        Assert.Equal("pending", savedLink.LastSyncStatus);
        Assert.NotNull(savedLink.NextAttemptAt);
    }

    private sealed class Scenario : IAsyncDisposable
    {
        private readonly Dictionary<string, Guid> _tracks = new(StringComparer.Ordinal);
        private readonly Dictionary<string, FakeProvider> _providers = new(StringComparer.Ordinal);

        private Scenario(SchemaContext schema)
        {
            Schema = schema;
            Db = schema.Db;
            foreach (var service in new[] { "youtube", "spotify" })
                _providers.Add(service, new FakeProvider(service, _tracks));
            Coordinator = new PlaylistSyncCoordinator(Db, _providers.Values,
                _providers.Values.Select(provider => (IPlaylistWriter)new FakeWriter(provider)),
                TimeProvider.System, NullLogger<PlaylistSyncCoordinator>.Instance);
        }

        public SchemaContext Schema { get; }
        public ApplicationDbContext Db { get; }
        public PlaylistSyncCoordinator Coordinator { get; }
        public FakeProvider Provider(string service) => _providers[service];

        public Guid AddTrack(string name)
        {
            var id = Guid.NewGuid();
            _tracks.Add(name, id);
            Db.Tracks.Add(new Track { Id = id, SearchTitle = name });
            return id;
        }

        public Playlist AddPlaylist(string name, IReadOnlyList<Guid> trackIds)
        {
            var playlist = new Playlist
            {
                Id = Guid.NewGuid(), UserId = UserId, Name = name,
                SyncEnabled = true, NextSyncAt = DateTimeOffset.UtcNow
            };
            Db.Playlists.Add(playlist);
            for (var index = 0; index < trackIds.Count; index++)
                Db.PlaylistEntries.Add(new PlaylistEntry
                {
                    Id = Guid.NewGuid(), PlaylistId = playlist.Id, TrackId = trackIds[index], Position = index
                });
            return playlist;
        }

        public Playlist Playlist(Guid id) => Db.Playlists.Local.Single(item => item.Id == id);

        public ServicePlaylistMapping AddLink(Playlist playlist, string service, IReadOnlyList<Guid> baselineTrackIds,
            string state = "active")
        {
            var accountId = service == "youtube" ? 7 : 8;
            var externalIds = baselineTrackIds.Select(trackId => _tracks.Single(item => item.Value == trackId).Key).ToArray();
            var provider = Provider(service);
            provider.SetTracks(externalIds.Select(id => (id, _tracks[id])).ToArray());
            provider.SetRemoteItems(externalIds);
            var mapping = new ServicePlaylistMapping
            {
                Id = Guid.NewGuid(), PlaylistId = playlist.Id, UserId = UserId,
                ConnectedServiceAccountId = accountId, ExternalAccountId = $"{service}-owner",
                Service = service, ServicePlaylistId = $"{service}-playlist", SyncMode = "bidirectional",
                State = state, BaselineName = playlist.Name,
                BaselineJson = new PlaylistSyncBaseline(externalIds.Select(provider.SyncItem).ToList()).Serialize()
            };
            Db.ServicePlaylistMappings.Add(mapping);
            return mapping;
        }

        public Task SaveAsync() => Db.SaveChangesAsync();

        public async Task<string[]> CanonicalTrackNamesAsync(Guid playlistId)
        {
            var entries = await Db.PlaylistEntries.AsNoTracking().Where(item => item.PlaylistId == playlistId)
                .OrderBy(item => item.Position).ToListAsync();
            return entries.Select(entry => _tracks.Single(track => track.Value == entry.TrackId).Key).ToArray();
        }

        public Task<ServicePlaylistMapping> ReloadLinkAsync(Guid id)
            => Db.ServicePlaylistMappings.AsNoTracking().SingleAsync(item => item.Id == id);

        public static async Task<Scenario> CreateAsync()
        {
            var schema = await SchemaContext.CreateAsync();
            var scenario = new Scenario(schema);
            schema.Db.Users.Add(new User
            {
                Id = UserId, UserName = "playlist-sync-test", NormalizedUserName = "PLAYLIST-SYNC-TEST",
                Email = "playlist-sync-test@example.invalid", NormalizedEmail = "PLAYLIST-SYNC-TEST@EXAMPLE.INVALID"
            });
            foreach (var (service, id) in new[] { ("youtube", 7), ("spotify", 8) })
                schema.Db.ConnectedServiceAccounts.Add(new ConnectedServiceAccount
                {
                    Id = id, UserId = UserId, Service = service, ExternalAccountId = $"{service}-owner",
                    ConnectionState = "connected"
                });
            await schema.Db.SaveChangesAsync();
            return scenario;
        }

        public async ValueTask DisposeAsync() => await Schema.DisposeAsync();
    }

    private sealed class FakeProvider(string service, Dictionary<string, Guid> tracks) : IPlaylistSyncProvider
    {
        private readonly Dictionary<string, Guid> _remoteTracks = new(StringComparer.Ordinal);
        public string PlatformId => service;
        public Task<IReadOnlyList<PlaylistRemoteCatalogItem>> ListPlaylistsAsync(
            PlatformAccountContext account, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<PlaylistRemoteCatalogItem>>([]);
        public string Name { get; private set; } = "Road songs";
        public IReadOnlyList<string> RemoteIds { get; private set; } = [];
        private int _revision;

        public void SetTracks(params (string ExternalId, Guid TrackId)[] items)
        {
            foreach (var item in items) _remoteTracks[item.ExternalId] = item.TrackId;
        }

        public void SetRemoteItems(params string[] ids)
        {
            RemoteIds = ids;
            _revision++;
        }

        public PlaylistSyncItem SyncItem(string externalId)
            => new(_remoteTracks.GetValueOrDefault(externalId), null, externalId, service);

        public Task<PlaylistRemoteSnapshot> ReadAsync(PlatformAccountContext account, string playlistId,
            CancellationToken cancellationToken)
        {
            var items = RemoteIds.Select((id, position) => new PlaylistRemoteTrack(id,
                _remoteTracks.GetValueOrDefault(id), null, id, position)).ToArray();
            return Task.FromResult(new PlaylistRemoteSnapshot(playlistId, Name, items, $"revision-{_revision}"));
        }

        public Task<string?> ResolveAsync(PlatformAccountContext account, PlaylistEntry entry,
            CancellationToken cancellationToken)
        {
            if (entry.TrackObservation?.SourceType == service) return Task.FromResult<string?>(entry.TrackObservation.ExternalId);
            var resolved = entry.TrackId is { } trackId
                ? tracks.FirstOrDefault(item => item.Value == trackId).Key : null;
            return Task.FromResult(resolved);
        }

        public Task RenameAsync(PlatformAccountContext account, string playlistId, string name, CancellationToken cancellationToken)
        {
            Name = name;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(PlatformAccountContext account, string playlistId, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class FakeWriter(FakeProvider provider) : IPlaylistWriter
    {
        public string PlatformId => provider.PlatformId;
        public Task ValidateCreationAsync(PlatformAccountContext account, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<string> CreatePlaylistAsync(PlatformAccountContext account, string name, CancellationToken cancellationToken)
            => Task.FromResult($"{PlatformId}-created");
        public Task ValidateDestinationAsync(PlatformAccountContext account, string playlistId, CancellationToken cancellationToken)
            => Task.CompletedTask;
        public Task ReconcileAsync(PlatformAccountContext account, string playlistId, IReadOnlyList<string> trackIds,
            CancellationToken cancellationToken)
        {
            provider.SetRemoteItems(trackIds.ToArray());
            return Task.CompletedTask;
        }
    }

    private sealed class SchemaContext : IAsyncDisposable
    {
        private readonly string _adminConnectionString;
        private readonly string _schema;
        private bool _disposed;

        private SchemaContext(string adminConnectionString, string schema, ApplicationDbContext db)
        {
            _adminConnectionString = adminConnectionString;
            _schema = schema;
            Db = db;
        }

        public ApplicationDbContext Db { get; }

        public static async Task<SchemaContext> CreateAsync()
        {
            var raw = Environment.GetEnvironmentVariable("CANTARO_TEST_POSTGRES");
            if (string.IsNullOrWhiteSpace(raw)) throw new InvalidOperationException("CANTARO_TEST_POSTGRES is required.");
            var admin = new NpgsqlConnectionStringBuilder(raw);
            if (admin.Host is not ("localhost" or "127.0.0.1" or "::1"))
                throw new InvalidOperationException("CANTARO_TEST_POSTGRES must point to a loopback PostgreSQL server.");
            if (string.IsNullOrWhiteSpace(admin.Database)
                || !admin.Database.Contains("test", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("CANTARO_TEST_POSTGRES must use a database whose name contains 'test'.");

            var schema = $"cantaro_sync_test_{Guid.NewGuid():N}";
            var adminConnectionString = admin.ConnectionString;
            await using (var connection = new NpgsqlConnection(adminConnectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"CREATE SCHEMA \"{schema}\"";
                await command.ExecuteNonQueryAsync();
            }

            var scoped = new NpgsqlConnectionStringBuilder(adminConnectionString) { SearchPath = schema };
            var testConnectionString = scoped.ConnectionString;
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(testConnectionString, options => options.EnableRetryOnFailure(maxRetryCount: 1))
                .Options);
            var context = new SchemaContext(adminConnectionString, schema, db);
            try
            {
                await db.Database.EnsureCreatedAsync();
                return context;
            }
            catch
            {
                await context.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            await Db.DisposeAsync();
            await using var connection = new NpgsqlConnection(_adminConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP SCHEMA IF EXISTS \"{_schema}\" CASCADE";
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CANTARO_TEST_POSTGRES")))
                Skip = "Set CANTARO_TEST_POSTGRES to a loopback database whose name contains 'test'.";
        }
    }
}
