using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Cantaro.Api.Services.Spotify;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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
    public async Task NewLinkExport_RealWriterSecondValidationReconnectPausesBeforeAnyCreationRequest()
    {
        await using var scenario = await Scenario.CreateAsync();
        var playlist = scenario.AddPlaylist("Road songs", []);
        var client = new CreationClient();
        client.BeforeValidation = async account =>
        {
            Assert.Equal("youtube-owner", account.ExpectedExternalAccountId);
            if (client.Validations != 2) return;
            await scenario.Db.ConnectedServiceAccounts.Where(item => item.Id == 7)
                .ExecuteUpdateAsync(set => set.SetProperty(item => item.ConnectionState, "reconnect_required"));
            throw new PlatformReconnectRequiredException("Reconnect YouTube to continue.");
        };
        var writer = new YouTubePlaylistWriter(client);
        var coordinator = new PlaylistSyncCoordinator(scenario.Db, [scenario.Provider("youtube")], [writer],
            TimeProvider.System, NullLogger<PlaylistSyncCoordinator>.Instance);
        scenario.Db.MusicSyncJobs.Add(new MusicSyncJob
        {
            Id = Guid.NewGuid(), UserId = UserId, ConnectedServiceAccountId = 7, Service = "youtube",
            Status = MusicSyncJobStatuses.Queued, PlaylistCount = 1,
            PlaylistsJson = System.Text.Json.JsonSerializer.Serialize(new[] {
                new MusicSyncJobPlaylist("", playlist.Name, 0, playlist.Id, "youtube-owner") }),
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });
        await scenario.SaveAsync();
        var processor = new MusicSyncJobProcessor(scenario.Db,
            new PlatformRegistry([new MusicSyncJobProcessorTests.StubPlatformService("youtube")]), new MusicSyncThrottleService(),
            NullLogger<MusicSyncJobProcessor>.Instance, new OutboundPlaylistSyncService(scenario.Db, [writer], coordinator));
        Assert.True(await processor.ProcessNextAsync(default));
        var paused = await scenario.Db.MusicSyncJobs.AsNoTracking().SingleAsync();
        Assert.Equal(MusicSyncJobStatuses.Queued, paused.Status);
        Assert.Equal(0, paused.ProcessedPlaylistCount);
        Assert.Null(paused.ResultsJson);
        Assert.Equal(2, client.Validations);
        Assert.Equal(0, client.Creations);
        Assert.Empty(await scenario.Db.ServicePlaylistMappings.ToListAsync());
        Assert.False(await processor.ProcessNextAsync(default));
        await scenario.Db.ConnectedServiceAccounts.Where(item => item.Id == 7)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.ConnectionState, "connected"));
        Assert.True(await processor.ProcessNextAsync(default));
        var resumed = await scenario.Db.MusicSyncJobs.AsNoTracking().SingleAsync();
        Assert.Equal(MusicSyncJobStatuses.Completed, resumed.Status);
        Assert.Equal(1, resumed.ProcessedPlaylistCount);
        Assert.Equal(1, client.Creations);
        Assert.Equal(4, client.Validations);
        Assert.Equal("youtube-created", (await scenario.Db.ServicePlaylistMappings.SingleAsync()).ServicePlaylistId);
    }

    [PostgresFact]
    public async Task InboundImport_ReconnectAfterPrecheckKeepsCheckpointAndResumesThroughCoordinator()
    {
        await using var scenario = await Scenario.CreateAsync();
        var track = scenario.AddTrack("A");
        scenario.Provider("youtube").SetTracks(("A", track));
        scenario.Provider("youtube").SetRemoteItems("A");
        var job = new MusicSyncJob
        {
            Id = Guid.NewGuid(), UserId = UserId, ConnectedServiceAccountId = 7, Service = "youtube",
            Status = MusicSyncJobStatuses.Queued, PlaylistCount = 1, SongCount = 1,
            PlaylistsJson = System.Text.Json.JsonSerializer.Serialize(new[] {
                new MusicSyncJobPlaylist("youtube-playlist", "Road songs", 1, ExternalAccountId: "youtube-owner") }),
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        scenario.Db.MusicSyncJobs.Add(job);
        await scenario.SaveAsync();
        var saves = 0;
        scenario.Db.SavedChanges += (_, _) =>
        {
            if (++saves == 2) scenario.Db.ConnectedServiceAccounts.Where(item => item.Id == 7)
                .ExecuteUpdate(set => set.SetProperty(item => item.ConnectionState, "reconnect_required"));
        };
        var platform = new YouTubePlatformService(new YouTubeService(new ConfigurationBuilder().Build(), scenario.Db,
            new TokenEncryptionService(new EphemeralDataProtectionProvider()), NullLogger<YouTubeService>.Instance),
            new YouTubePlaylistSyncService(scenario.Coordinator));
        var processor = new MusicSyncJobProcessor(scenario.Db, new PlatformRegistry([platform]), new MusicSyncThrottleService(),
            NullLogger<MusicSyncJobProcessor>.Instance, new OutboundPlaylistSyncService(scenario.Db, [], scenario.Coordinator));
        Assert.True(await processor.ProcessNextAsync(default));
        var paused = await scenario.Db.MusicSyncJobs.AsNoTracking().SingleAsync();
        Assert.Equal(MusicSyncJobStatuses.Queued, paused.Status);
        Assert.Equal(0, paused.ProcessedPlaylistCount);
        Assert.Null(paused.ResultsJson);
        Assert.Empty(await scenario.Db.Playlists.ToListAsync());
        Assert.False(await processor.ProcessNextAsync(default));
        await scenario.Db.ConnectedServiceAccounts.Where(item => item.Id == 7)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.ConnectionState, "connected"));
        Assert.True(await processor.ProcessNextAsync(default));
        var resumed = await scenario.Db.MusicSyncJobs.AsNoTracking().SingleAsync();
        Assert.Equal(MusicSyncJobStatuses.Completed, resumed.Status);
        Assert.Equal(1, resumed.ProcessedPlaylistCount);
        Assert.Equal(track, (await scenario.Db.PlaylistEntries.SingleAsync()).TrackId);
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitCreation_ChecksPinnedIdentityBeforeReconnectState(bool replacement)
    {
        await using var scenario = await Scenario.CreateAsync();
        var playlist = scenario.AddPlaylist("Road songs", []);
        await scenario.SaveAsync();
        await scenario.Db.ConnectedServiceAccounts.Where(item => item.Id == 7).ExecuteUpdateAsync(set => set
            .SetProperty(item => item.ConnectionState, "reconnect_required")
            .SetProperty(item => item.ExternalAccountId, replacement ? "replacement" : "youtube-owner"));
        var account = new PlatformAccountContext(UserId, 7, "youtube-owner");
        var error = await Record.ExceptionAsync(() => scenario.Coordinator.CreateLinkAsync(account, playlist.Id, "youtube", default));
        if (replacement) Assert.Equal("account_changed", Assert.IsType<PlatformApiException>(error).Code);
        else Assert.IsType<PlatformReconnectRequiredException>(error);
        Assert.Empty(await scenario.Db.ServicePlaylistMappings.ToListAsync());
        Assert.Equal(0, scenario.Provider("youtube").Creations);
    }

    [PostgresTheory]
    [InlineData("account_recheck")]
    [InlineData("duplicate_lookup")]
    [InlineData("validation")]
    [InlineData("after_create")]
    [InlineData("uncertain_post")]
    public async Task NewLinkExport_ReconnectPreservesWorkUntilPostOutcomeIsUnknown(string phase)
    {
        await using var scenario = await Scenario.CreateAsync();
        var playlist = scenario.AddPlaylist("Road songs", []);
        var remote = scenario.Provider("youtube");
        var job = new MusicSyncJob
        {
            Id = Guid.NewGuid(), UserId = UserId, ConnectedServiceAccountId = 7, Service = "youtube",
            Status = MusicSyncJobStatuses.Queued, PlaylistCount = 1,
            PlaylistsJson = System.Text.Json.JsonSerializer.Serialize(new[] {
                new MusicSyncJobPlaylist("", playlist.Name, 0, playlist.Id, "youtube-owner") }),
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        scenario.Db.MusicSyncJobs.Add(job);
        await scenario.SaveAsync();
        async Task Reconnect()
        {
            await scenario.Db.ConnectedServiceAccounts.Where(item => item.Id == 7)
                .ExecuteUpdateAsync(set => set.SetProperty(item => item.ConnectionState, "reconnect_required"));
            throw new PlatformReconnectRequiredException("Reconnect YouTube to continue.");
        }
        if (phase == "duplicate_lookup") remote.BeforeList = Reconnect;
        if (phase == "validation") remote.BeforeCreateValidation = Reconnect;
        if (phase == "after_create") remote.BeforeRead = Reconnect;
        if (phase == "uncertain_post") remote.BeforeCreate = Reconnect;
        var saves = 0;
        scenario.Db.SavedChanges += (_, _) =>
        {
            if (phase == "account_recheck" && ++saves == 3)
                scenario.Db.ConnectedServiceAccounts.Where(item => item.Id == 7)
                    .ExecuteUpdate(set => set.SetProperty(item => item.ConnectionState, "reconnect_required"));
        };
        var processor = new MusicSyncJobProcessor(scenario.Db,
            new PlatformRegistry([new MusicSyncJobProcessorTests.StubPlatformService("youtube")]), new MusicSyncThrottleService(),
            NullLogger<MusicSyncJobProcessor>.Instance,
            new OutboundPlaylistSyncService(scenario.Db, [new FakeWriter(remote)], scenario.Coordinator));
        Assert.True(await processor.ProcessNextAsync(default));
        var paused = await scenario.Db.MusicSyncJobs.AsNoTracking().SingleAsync();
        if (phase == "uncertain_post")
        {
            Assert.Equal(MusicSyncJobStatuses.Failed, paused.Status);
            Assert.Equal("creation_uncertain", (await scenario.Db.ServicePlaylistMappings.SingleAsync()).State);
            Assert.Equal(1, remote.Creations);
            return;
        }
        Assert.Equal(MusicSyncJobStatuses.Queued, paused.Status);
        Assert.Equal(0, paused.ProcessedPlaylistCount);
        Assert.Null(paused.ResultsJson);
        Assert.Equal(phase == "after_create" ? 1 : 0, remote.Creations);
        Assert.False(await processor.ProcessNextAsync(default));
        await scenario.Db.ConnectedServiceAccounts.Where(item => item.Id == 7)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.ConnectionState, "connected"));
        remote.BeforeList = remote.BeforeRead = remote.BeforeCreateValidation = null;
        Assert.True(await processor.ProcessNextAsync(default));
        var resumed = await scenario.Db.MusicSyncJobs.AsNoTracking().SingleAsync();
        Assert.Equal(MusicSyncJobStatuses.Completed, resumed.Status);
        Assert.Equal(1, resumed.ProcessedPlaylistCount);
        Assert.Equal(1, remote.Creations);
        Assert.Equal("youtube-owner", (await scenario.Db.ServicePlaylistMappings.AsNoTracking().SingleAsync()).ExternalAccountId);
    }

    [PostgresTheory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ExistingLinkExport_ReconnectKeepsJobCheckpointAndResumesSamePlaylist(int reconnectAfterSave)
    {
        await using var scenario = await Scenario.CreateAsync();
        var a = scenario.AddTrack("A");
        var playlist = scenario.AddPlaylist("Road songs", [a]);
        var link = scenario.AddLink(playlist, "youtube", [a]);
        link.SyncMode = "from_cantaro";
        link.PendingWriteJson = link.BaselineJson;
        var pending = link.PendingWriteJson;
        var job = new MusicSyncJob
        {
            Id = Guid.NewGuid(), UserId = UserId, ConnectedServiceAccountId = 7, Service = "youtube",
            Status = MusicSyncJobStatuses.Queued, PlaylistCount = 1, SongCount = 1,
            PlaylistsJson = System.Text.Json.JsonSerializer.Serialize(new[] {
                new MusicSyncJobPlaylist(link.ServicePlaylistId, playlist.Name, 1, playlist.Id, link.ExternalAccountId) }),
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        scenario.Db.MusicSyncJobs.Add(job);
        var remote = scenario.Provider("youtube");
        var triggered = false;
        if (reconnectAfterSave == 0) remote.BeforeRead = async () =>
        {
            triggered = true;
            await scenario.Db.ConnectedServiceAccounts.Where(item => item.Id == 7)
                .ExecuteUpdateAsync(set => set.SetProperty(item => item.ConnectionState, "reconnect_required"));
            throw new PlatformReconnectRequiredException("Reconnect YouTube to continue.");
        };
        await scenario.SaveAsync();
        var saves = 0;
        scenario.Db.SavedChanges += (_, _) =>
        {
            if (++saves != reconnectAfterSave) return;
            triggered = true;
            scenario.Db.ConnectedServiceAccounts.Where(item => item.Id == 7)
                .ExecuteUpdate(set => set.SetProperty(item => item.ConnectionState, "reconnect_required"));
        };
        var outbound = new OutboundPlaylistSyncService(scenario.Db, [new FakeWriter(remote)], scenario.Coordinator);
        var processor = new MusicSyncJobProcessor(scenario.Db,
            new PlatformRegistry([new MusicSyncJobProcessorTests.StubPlatformService("youtube")]), new MusicSyncThrottleService(),
            NullLogger<MusicSyncJobProcessor>.Instance, outbound);
        Assert.True(await processor.ProcessNextAsync(default));
        Assert.True(triggered);
        var paused = await scenario.Db.MusicSyncJobs.AsNoTracking().SingleAsync();
        Assert.Equal(MusicSyncJobStatuses.Queued, paused.Status);
        Assert.Equal(0, paused.ProcessedPlaylistCount);
        Assert.Null(paused.ResultsJson);
        Assert.Equal(pending, (await scenario.ReloadLinkAsync(link.Id)).PendingWriteJson);
        Assert.False(await processor.ProcessNextAsync(default));
        await scenario.Db.ConnectedServiceAccounts.Where(item => item.Id == 7)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.ConnectionState, "connected"));
        remote.BeforeRead = null;
        Assert.True(await processor.ProcessNextAsync(default));
        var resumed = await scenario.Db.MusicSyncJobs.AsNoTracking().SingleAsync();
        Assert.Equal(MusicSyncJobStatuses.Completed, resumed.Status);
        Assert.Equal(1, resumed.ProcessedPlaylistCount);
    }

    [PostgresFact]
    public async Task PausedInitialization_RateLimitThenReconnectRemainsDueAfterRecovery()
    {
        await using var scenario = await Scenario.CreateAsync();
        var a = scenario.AddTrack("A");
        var playlist = scenario.AddPlaylist("Road songs", [a]);
        var link = scenario.AddLink(playlist, "youtube", [a], state: "paused");
        link.InitialMode = "combine";
        link.LastSyncStatus = "rate_limited";
        link.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        scenario.Provider("youtube").BeforeRead = async () =>
        {
            await scenario.Db.ConnectedServiceAccounts.Where(item => item.Id == 7)
                .ExecuteUpdateAsync(set => set.SetProperty(item => item.ConnectionState, "reconnect_required"));
            throw new PlatformReconnectRequiredException("Reconnect YouTube to continue.");
        };
        await scenario.SaveAsync();
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(scenario.Db.Database.GetConnectionString()));
        services.AddScoped(_ => scenario.Coordinator);
        await using var provider = services.BuildServiceProvider();
        var scheduler = new PlaylistSyncScheduler(provider.GetRequiredService<IServiceScopeFactory>(),
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["MusicPlaylistSync:AutomaticEnabled"] = "false" }).Build(), TimeProvider.System,
            NullLogger<PlaylistSyncScheduler>.Instance);
        Assert.True(await scheduler.ProcessNextAsync(default));
        Assert.Equal("reconnect_required", (await scenario.ReloadLinkAsync(link.Id)).LastSyncStatus);
        Assert.False(await scheduler.ProcessNextAsync(default));
        await scenario.Db.ConnectedServiceAccounts.Where(item => item.Id == 7)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.ConnectionState, "connected"));
        scenario.Provider("youtube").BeforeRead = null;
        Assert.True(await scheduler.ProcessNextAsync(default));
        Assert.Equal("active", (await scenario.ReloadLinkAsync(link.Id)).State);
    }

    [PostgresFact]
    public async Task DailyScheduler_SkipsBlockedPlaylistPreservesReceiptsAndProcessesHealthyPlaylist()
    {
        await using var scenario = await Scenario.CreateAsync();
        var a = scenario.AddTrack("A");
        var blocked = scenario.AddPlaylist("Blocked", [a, a]);
        blocked.AllowDuplicateTracks = true;
        var blockedLink = scenario.AddLink(blocked, "youtube", [a, a]);
        blockedLink.PendingWriteJson = blockedLink.BaselineJson;
        var receipt = blockedLink.PendingWriteJson;
        var healthy = scenario.AddPlaylist("Healthy", [a]);
        var healthyLink = scenario.AddLink(healthy, "spotify", [a]);
        scenario.Db.ConnectedServiceAccounts.Local.Single(item => item.Service == "youtube").ConnectionState = "reconnect_required";
        await scenario.SaveAsync();
        var due = (await scenario.Db.Playlists.AsNoTracking().SingleAsync(item => item.Id == blocked.Id)).NextSyncAt;
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(scenario.Db.Database.GetConnectionString()));
        services.AddScoped(_ => scenario.Coordinator);
        await using var provider = services.BuildServiceProvider();
        var scheduler = new PlaylistSyncScheduler(provider.GetRequiredService<IServiceScopeFactory>(),
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), TimeProvider.System,
            NullLogger<PlaylistSyncScheduler>.Instance);
        Assert.True(await scheduler.ProcessNextAsync(default));
        Assert.False(await scheduler.ProcessNextAsync(default));
        Assert.Equal("success", (await scenario.ReloadLinkAsync(healthyLink.Id)).LastSyncStatus);
        Assert.Equal(receipt, (await scenario.ReloadLinkAsync(blockedLink.Id)).PendingWriteJson);
        Assert.Equal(due, (await scenario.Db.Playlists.AsNoTracking().SingleAsync(item => item.Id == blocked.Id)).NextSyncAt);
        Assert.Equal(new[] { "A", "A" }, await scenario.CanonicalTrackNamesAsync(blocked.Id));
        var account = scenario.Db.ConnectedServiceAccounts.Local.Single(item => item.Service == "youtube");
        account.ConnectionState = "connected";
        account.TokenVersion++;
        await scenario.SaveAsync();
        Assert.True(await scheduler.ProcessNextAsync(default));
        Assert.Equal("success", (await scenario.ReloadLinkAsync(blockedLink.Id)).LastSyncStatus);
        Assert.Equal(new[] { "A", "A" }, await scenario.CanonicalTrackNamesAsync(blocked.Id));
    }

    [PostgresFact]
    public async Task SpotifySearchCache_PersistsAndRefreshesEvenIfExpiryCleanupRemovedItsRow()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
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

        private Scenario(PostgresTestSchema schema)
        {
            Schema = schema;
            Db = schema.Db;
            foreach (var service in new[] { "youtube", "spotify" })
                _providers.Add(service, new FakeProvider(service, _tracks));
            Coordinator = new PlaylistSyncCoordinator(Db, _providers.Values,
                _providers.Values.Select(provider => (IPlaylistWriter)new FakeWriter(provider)),
                TimeProvider.System, NullLogger<PlaylistSyncCoordinator>.Instance);
        }

        public PostgresTestSchema Schema { get; }
        public ApplicationDbContext Db { get; }
        public PlaylistSyncCoordinator Coordinator { get; }
        public FakeProvider Provider(string service) => _providers[service];

        public Guid AddTrack(string name)
        {
            var id = Guid.NewGuid();
            _tracks.Add(name, id);
            Db.Tracks.Add(new Track {  Id = id });
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
                Db.PlaylistEntries.Add(new PlaylistEntry { OwnerUserId = playlist.UserId,
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
            var schema = await PostgresTestSchema.CreateAsync();
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
        public Func<Task>? BeforeRead { get; set; }
        public Func<Task>? BeforeList { get; set; }
        public Func<Task>? BeforeCreateValidation { get; set; }
        public Func<Task>? BeforeCreate { get; set; }
        public int Creations { get; set; }
        private readonly Dictionary<string, Guid> _remoteTracks = new(StringComparer.Ordinal);
        public string PlatformId => service;
        public async Task<IReadOnlyList<PlaylistRemoteCatalogItem>> ListPlaylistsAsync(
            PlatformAccountContext account, CancellationToken cancellationToken)
        {
            if (BeforeList is { } beforeList) await beforeList();
            return [];
        }
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

        public async Task<PlaylistRemoteSnapshot> ReadAsync(PlatformAccountContext account, string playlistId,
            CancellationToken cancellationToken)
        {
            if (BeforeRead is { } beforeRead) await beforeRead();
            var items = RemoteIds.Select((id, position) => new PlaylistRemoteTrack(id,
                _remoteTracks.GetValueOrDefault(id), null, id, position)).ToArray();
            return new PlaylistRemoteSnapshot(playlistId, Name, items, $"revision-{_revision}");
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

    private sealed class CreationClient : IYouTubePlaylistClient
    {
        public Func<PlatformAccountContext, Task>? BeforeValidation { get; set; }
        public int Validations { get; private set; }
        public int Creations { get; private set; }
        public async Task ValidatePlaylistCreationAsync(PlatformAccountContext account, CancellationToken ct)
        {
            Validations++;
            if (BeforeValidation is { } beforeValidation) await beforeValidation(account);
        }
        public Task<string> CreatePrivatePlaylistAsync(PlatformAccountContext account, string name, CancellationToken ct)
        {
            Creations++;
            return Task.FromResult("youtube-created");
        }
        public Task ValidateWritablePlaylistAsync(PlatformAccountContext account, string playlistId, CancellationToken ct)
            => Task.CompletedTask;
        public Task<IReadOnlyList<YouTubeWritablePlaylistItem>> GetRawPlaylistItemsAsync(
            PlatformAccountContext account, string playlistId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<YouTubeWritablePlaylistItem>>([]);
        public Task<string> InsertPlaylistItemAsync(PlatformAccountContext account, string playlistId, string videoId, long position, CancellationToken ct)
            => throw new NotSupportedException();
        public Task MovePlaylistItemAsync(PlatformAccountContext account, string playlistId, YouTubeWritablePlaylistItem item, long position, CancellationToken ct)
            => throw new NotSupportedException();
        public Task DeletePlaylistItemAsync(PlatformAccountContext account, string itemId, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class FakeWriter(FakeProvider provider) : IPlaylistWriter
    {
        public string PlatformId => provider.PlatformId;
        public Task ValidateCreationAsync(PlatformAccountContext account, CancellationToken cancellationToken)
            => provider.BeforeCreateValidation?.Invoke() ?? Task.CompletedTask;
        public async Task<string> CreatePlaylistAsync(PlatformAccountContext account, string name, CancellationToken cancellationToken)
        {
            provider.Creations++;
            if (provider.BeforeCreate is { } beforeCreate) await beforeCreate();
            return $"{PlatformId}-created";
        }
        public Task ValidateDestinationAsync(PlatformAccountContext account, string playlistId, CancellationToken cancellationToken)
            => Task.CompletedTask;
        public Task ReconcileAsync(PlatformAccountContext account, string playlistId, IReadOnlyList<string> trackIds,
            CancellationToken cancellationToken)
        {
            provider.SetRemoteItems(trackIds.ToArray());
            return Task.CompletedTask;
        }
    }
}
