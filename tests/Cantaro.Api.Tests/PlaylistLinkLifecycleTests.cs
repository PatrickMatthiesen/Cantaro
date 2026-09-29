using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class PlaylistLinkLifecycleTests
{
    [Fact]
    public async Task Attach_RejectsChangedPreviewBeforeCreatingMapping()
    {
        await using var scope = await Scope.CreateAsync("youtube");
        var preview = await scope.Lifecycle.PreviewAttachAsync(scope.UserId, scope.Playlist.Id,
            "youtube", "remote-playlist", default);
        scope.Provider.Name = "Renamed outside Cantaro";

        var error = await Assert.ThrowsAsync<PlatformApiException>(() => scope.Lifecycle.AttachAsync(
            scope.UserId, scope.Playlist.Id, "youtube", "remote-playlist",
            "bidirectional", "combine", preview.PreviewToken, default));

        Assert.Equal("stale_preview", error.Code);
        Assert.Empty(await scope.Db.ServicePlaylistMappings.ToListAsync());
        Assert.Equal(0, scope.Writer.Creates);
        Assert.Equal(0, scope.Writer.Writes);
    }

    [Fact]
    public async Task CreatePreview_ShowsSameNameOverlapAndLinksOwnedByOtherCanonicalPlaylist()
    {
        await using var scope = await Scope.CreateAsync("youtube");
        scope.Playlist.Name = "  Café songs ";
        scope.Provider.Name = "Café songs";
        scope.Provider.Playlists = [new PlatformPlaylistDto
        {
            Id = "remote-playlist", Title = "CAFÉ SONGS", ItemCount = 2
        }];
        var observation = new TrackObservation { Id = Guid.NewGuid(), SourceType = "youtube",
            ExternalId = "song-1", Title = "Song", MatchStatus = TrackMatchingStatuses.Pending };
        scope.Db.TrackObservations.Add(observation);
        scope.Db.PlaylistEntries.Add(new PlaylistEntry
        {
            Id = Guid.NewGuid(), PlaylistId = scope.Playlist.Id,
            TrackObservationId = observation.Id, Position = 0
        });
        var other = new Playlist { Id = Guid.NewGuid(), UserId = scope.UserId,
            Name = "Other", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        scope.Db.Playlists.Add(other);
        var linked = scope.AddMapping();
        linked.PlaylistId = other.Id;
        await scope.Db.SaveChangesAsync();
        scope.Provider.SnapshotOverride = new PlaylistRemoteSnapshot("remote-playlist", "Café songs",
            [new PlaylistRemoteTrack("song-1", null, observation.Id, "Song", 0)]);

        var preview = await scope.Lifecycle.PreviewCreateAsync(scope.UserId,
            scope.Playlist.Id, "youtube", default);

        var candidate = Assert.Single(preview.Candidates);
        Assert.Equal(other.Id, candidate.LinkedPlaylistId);
        Assert.Equal(1, candidate.SharedTrackCount);
        Assert.Equal(1, candidate.RemoteTrackCount);
        Assert.Null(candidate.ComparisonError);
        Assert.Equal(0, scope.Writer.Creates);
    }

    [Fact]
    public async Task Create_RequiresReviewForSameNameAndRejectsStaleAccount()
    {
        await using var scope = await Scope.CreateAsync("youtube");
        scope.Provider.Playlists = [new PlatformPlaylistDto
        {
            Id = "remote-playlist", Title = scope.Playlist.Name, ItemCount = 0
        }];
        var missingReview = await Assert.ThrowsAsync<PlatformApiException>(() =>
            scope.Lifecycle.CreateLinkAsync(scope.UserId, scope.Playlist.Id, "youtube", null, default));
        Assert.Equal("create_preview_required", missingReview.Code);
        var preview = await scope.Lifecycle.PreviewCreateAsync(scope.UserId,
            scope.Playlist.Id, "youtube", default);
        scope.Account.ExternalAccountId = "changed-account";
        await scope.Db.SaveChangesAsync();

        var stale = await Assert.ThrowsAsync<PlatformApiException>(() =>
            scope.Lifecycle.CreateLinkAsync(scope.UserId, scope.Playlist.Id,
                "youtube", preview.PreviewToken, default));
        Assert.Equal("stale_preview", stale.Code);
        Assert.Equal(0, scope.Writer.Creates);
    }

    [Fact]
    public async Task CreatePreview_KeepsSameNameCandidateWhenTrackComparisonFails()
    {
        await using var scope = await Scope.CreateAsync("youtube");
        scope.Provider.Playlists = [new PlatformPlaylistDto
        {
            Id = "remote-playlist", Title = scope.Playlist.Name, ItemCount = 3
        }];
        scope.Provider.FailReadNumber = 1;

        var preview = await scope.Lifecycle.PreviewCreateAsync(scope.UserId,
            scope.Playlist.Id, "youtube", default);

        var candidate = Assert.Single(preview.Candidates);
        Assert.Equal(3, candidate.RemoteTrackCount);
        Assert.Null(candidate.SharedTrackCount);
        Assert.NotNull(candidate.ComparisonError);
        Assert.Equal(0, scope.Writer.Creates);
    }

    [Fact]
    public async Task Create_QueuesFirstSyncAndDoesNotCreateAgainForActiveLink()
    {
        await using var scope = await Scope.CreateAsync("youtube");
        var observation = new TrackObservation { Id = Guid.NewGuid(), SourceType = "youtube",
            ExternalId = "song-1", Title = "Song", MatchStatus = TrackMatchingStatuses.Pending };
        scope.Db.TrackObservations.Add(observation);
        scope.Db.PlaylistEntries.Add(new PlaylistEntry
        {
            Id = Guid.NewGuid(), PlaylistId = scope.Playlist.Id,
            TrackObservationId = observation.Id, Position = 0
        });
        await scope.Db.SaveChangesAsync();
        scope.Provider.ResolvedId = "song-1";
        scope.Writer.OnReconcile = ids => scope.Provider.SnapshotOverride = new PlaylistRemoteSnapshot(
            "created-playlist", scope.Provider.Name,
            ids.Select((id, position) => new PlaylistRemoteTrack(id, null,
                observation.Id, "Song", position)).ToList());
        var preview = await scope.Lifecycle.PreviewCreateAsync(scope.UserId,
            scope.Playlist.Id, "youtube", default);

        var first = await scope.Lifecycle.CreateLinkAsync(scope.UserId,
            scope.Playlist.Id, "youtube", preview.PreviewToken, default);
        var link = Assert.Single(first.Links);
        Assert.Equal("active", link.State);
        Assert.Equal("pending", link.LastSyncStatus);
        Assert.NotNull(link.NextAttemptAt);
        Assert.Null(link.LastSyncedAt);
        Assert.Equal(1, scope.Writer.Creates);
        Assert.Equal(0, scope.Writer.Writes);

        using var services = new ServiceCollection()
            .AddSingleton(scope.Db)
            .AddSingleton(scope.Coordinator)
            .BuildServiceProvider();
        var scheduler = new PlaylistSyncScheduler(services.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().Build(), TimeProvider.System,
            NullLogger<PlaylistSyncScheduler>.Instance);
        Assert.True(await scheduler.ProcessNextAsync(default));
        Assert.Equal("success", (await scope.Db.ServicePlaylistMappings.SingleAsync()).LastSyncStatus);
        Assert.Equal(1, scope.Writer.Writes);
        Assert.Equal(new[] { "song-1" }, scope.Writer.LastWrittenIds);

        await scope.Lifecycle.CreateLinkAsync(scope.UserId, scope.Playlist.Id,
            "youtube", null, default);
        Assert.Equal(1, scope.Writer.Creates);
        Assert.Equal(1, scope.Writer.Writes);
        Assert.Single(await scope.Db.ServicePlaylistMappings.ToListAsync());
    }

    [Fact]
    public async Task Run_QueuesSyncWithoutWaitingForProviderRead()
    {
        await using var scope = await Scope.CreateAsync("youtube");
        var mapping = scope.AddMapping();
        await scope.Db.SaveChangesAsync();

        var status = await scope.Lifecycle.RunAsync(scope.UserId, scope.Playlist.Id, "youtube", default);

        Assert.Equal(0, scope.Provider.Reads);
        Assert.NotNull(mapping.NextAttemptAt);
        Assert.Equal(mapping.NextAttemptAt, Assert.Single(status.Links).NextAttemptAt);
    }

    [Fact]
    public async Task ProviderRateLimit_DoesNotConsumeFailureAttempts()
    {
        await using var scope = await Scope.CreateAsync("youtube");
        var mapping = scope.AddMapping();
        mapping.AttemptCount = 4;
        scope.Provider.ReadFailure = new PlatformApiException("rate_limited", "Try later.", 429,
            TimeSpan.FromMinutes(12));
        await scope.Db.SaveChangesAsync();
        var before = DateTimeOffset.UtcNow;

        await scope.Coordinator.RunAsync(scope.UserId, scope.Playlist.Id, "youtube", default);

        Assert.Equal("rate_limited", mapping.LastSyncStatus);
        Assert.Null(mapping.LastError);
        Assert.Equal(4, mapping.AttemptCount);
        Assert.True(mapping.NextAttemptAt >= before.AddMinutes(12));
    }

    [Fact]
    public async Task AttachQuotaLimit_PreservesApprovedChoicesAndSchedulerResumesInitialization()
    {
        await using var scope = await Scope.CreateAsync("spotify");
        var preview = await scope.Lifecycle.PreviewAttachAsync(scope.UserId, scope.Playlist.Id,
            "spotify", "remote-playlist", default);
        scope.Provider.ReadFailureNumber = 3;
        scope.Provider.ReadFailureAtNumber = new PlatformApiException("spotify_quota_exceeded", "Quota reached", 429,
            TimeSpan.FromHours(3));

        await Assert.ThrowsAsync<PlatformApiException>(() => scope.Lifecycle.AttachAsync(
            scope.UserId, scope.Playlist.Id, "spotify", "remote-playlist",
            "import_only", "platform", preview.PreviewToken, default));

        var mapping = await scope.Db.ServicePlaylistMappings.SingleAsync();
        Assert.Equal("paused", mapping.State);
        Assert.Equal("import_only", mapping.SyncMode);
        Assert.Equal("platform", mapping.InitialMode);
        Assert.Equal("quota_limited", mapping.LastSyncStatus);
        Assert.Null(mapping.LastError);
        Assert.True(mapping.NextAttemptAt > DateTimeOffset.UtcNow.AddHours(2));

        mapping.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        await scope.Db.SaveChangesAsync();
        using var services = new ServiceCollection()
            .AddSingleton(scope.Db)
            .AddSingleton(scope.Coordinator)
            .BuildServiceProvider();
        var scheduler = new PlaylistSyncScheduler(services.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().Build(), TimeProvider.System,
            NullLogger<PlaylistSyncScheduler>.Instance);

        Assert.True(await scheduler.ProcessNextAsync(default));

        Assert.Equal("active", mapping.State);
        Assert.Equal("import_only", mapping.SyncMode);
        Assert.Null(mapping.InitialMode);
        Assert.Equal("success", mapping.LastSyncStatus);
    }

    [Fact]
    public async Task Scheduler_DoesNotResumePausedLinksWithoutPendingInitialization()
    {
        await using var scope = await Scope.CreateAsync("youtube");
        var mapping = scope.AddMapping();
        mapping.State = "paused";
        mapping.LastSyncStatus = "quota_limited";
        mapping.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await scope.Db.SaveChangesAsync();
        using var services = new ServiceCollection()
            .AddSingleton(scope.Db)
            .AddSingleton(scope.Coordinator)
            .BuildServiceProvider();
        var scheduler = new PlaylistSyncScheduler(services.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().Build(), TimeProvider.System,
            NullLogger<PlaylistSyncScheduler>.Instance);

        Assert.False(await scheduler.ProcessNextAsync(default));
        Assert.Equal("paused", mapping.State);
        Assert.Equal(0, scope.Provider.Reads);
    }

    [Fact]
    public async Task Scheduler_RateLimitRetryStartsAtFailureTime()
    {
        await using var scope = await Scope.CreateAsync("youtube");
        var start = DateTimeOffset.UtcNow;
        var clock = new ManualTimeProvider(start);
        var mapping = scope.AddMapping();
        mapping.InitialMode = "combine";
        mapping.NextAttemptAt = start.AddMinutes(-1);
        mapping.AttemptCount = 4;
        scope.Provider.OnRead = () => clock.Advance(TimeSpan.FromMinutes(20));
        scope.Provider.ReadFailure = new PlatformApiException("rate_limited", "Try later.", 429,
            TimeSpan.FromMinutes(12));
        await scope.Db.SaveChangesAsync();
        using var services = new ServiceCollection()
            .AddSingleton(scope.Db)
            .AddSingleton(scope.Coordinator)
            .BuildServiceProvider();
        var scheduler = new PlaylistSyncScheduler(services.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().Build(), clock, NullLogger<PlaylistSyncScheduler>.Instance);

        Assert.True(await scheduler.ProcessNextAsync(default));

        var saved = await scope.Db.ServicePlaylistMappings.AsNoTracking().SingleAsync();
        Assert.Equal("rate_limited", saved.LastSyncStatus);
        Assert.Null(saved.LastError);
        Assert.Equal(4, saved.AttemptCount);
        Assert.Equal(start.AddMinutes(32), saved.NextAttemptAt);
    }

    [Fact]
    public async Task Scheduler_TransientRetryStartsAtFailureTime()
    {
        await using var scope = await Scope.CreateAsync("youtube");
        var start = DateTimeOffset.UtcNow;
        var clock = new ManualTimeProvider(start);
        var mapping = scope.AddMapping();
        mapping.InitialMode = "combine";
        mapping.NextAttemptAt = start.AddMinutes(-1);
        scope.Provider.OnRead = () => clock.Advance(TimeSpan.FromMinutes(20));
        scope.Provider.ReadFailure = new PlatformApiException("provider_unavailable", "Try again.", 503);
        await scope.Db.SaveChangesAsync();
        using var services = new ServiceCollection()
            .AddSingleton(scope.Db)
            .AddSingleton(scope.Coordinator)
            .BuildServiceProvider();
        var scheduler = new PlaylistSyncScheduler(services.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().Build(), clock, NullLogger<PlaylistSyncScheduler>.Instance);

        Assert.True(await scheduler.ProcessNextAsync(default));

        var saved = await scope.Db.ServicePlaylistMappings.AsNoTracking().SingleAsync();
        Assert.Equal("error", saved.LastSyncStatus);
        Assert.Equal(1, saved.AttemptCount);
        Assert.Equal(start.AddMinutes(22), saved.NextAttemptAt);
    }

    [Fact]
    public async Task Create_WithSameNameReviewExplicitlyCreatesAnotherCopy()
    {
        await using var scope = await Scope.CreateAsync("youtube");
        scope.Provider.Playlists = [new PlatformPlaylistDto
        {
            Id = "remote-playlist", Title = scope.Playlist.Name
        }];
        var preview = await scope.Lifecycle.PreviewCreateAsync(scope.UserId,
            scope.Playlist.Id, "youtube", default);
        Assert.Single(preview.Candidates);

        var status = await scope.Lifecycle.CreateLinkAsync(scope.UserId,
            scope.Playlist.Id, "youtube", preview.PreviewToken, default);

        Assert.Equal("active", Assert.Single(status.Links).State);
        Assert.Equal(1, scope.Writer.Creates);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public async Task AutomaticFanout_OnlyCreatesWhenNoSameNameCopyExists(
        bool sameName, int expectedCreates)
    {
        await using var scope = await Scope.CreateAsync("youtube");
        scope.Playlist.SyncEnabled = true;
        scope.Provider.Playlists = [new PlatformPlaylistDto
        {
            Id = "another-playlist", Title = sameName ? scope.Playlist.Name : "Different name"
        }];
        await scope.Db.SaveChangesAsync();
        var coordinator = new PlaylistSyncCoordinator(scope.Db, [scope.Provider], [scope.Writer],
            TimeProvider.System, NullLogger<PlaylistSyncCoordinator>.Instance);

        await coordinator.RunAsync(scope.UserId, scope.Playlist.Id, null, default);

        Assert.Equal(expectedCreates, scope.Writer.Creates);
        Assert.Equal(expectedCreates, await scope.Db.ServicePlaylistMappings.CountAsync());
    }

    [Fact]
    public async Task Status_DoesNotClaimLegacySuccessBeforeFirstBaseline()
    {
        await using var scope = await Scope.CreateAsync("youtube");
        var mapping = scope.AddMapping();
        mapping.LastSyncStatus = "success";
        mapping.LastSyncedAt = DateTimeOffset.UtcNow.AddDays(-1);
        await scope.Db.SaveChangesAsync();

        var link = Assert.Single((await scope.Lifecycle.GetStatusAsync(scope.UserId,
            scope.Playlist.Id, default)).Links);

        Assert.Equal("awaiting_first_sync", link.LastSyncStatus);
        Assert.Null(link.LastSyncedAt);
        Assert.Equal(0, link.ResolvedCount);
    }

    [Theory]
    [InlineData("playlist-read-private")]
    [InlineData("playlist-modify-private")]
    [InlineData("")]
    public async Task Status_RequiresReconnectForFailedSpotifyCreationWhenPrivateScopesAreMissing(string scopes)
    {
        await using var scope = await Scope.CreateAsync("spotify");
        scope.Account.Scopes = scopes;
        var mapping = scope.AddMapping();
        mapping.State = "creation_failed";
        mapping.ServicePlaylistId = "pending:creation";
        mapping.LastError = "Reconnect Spotify and grant playlist-read-private and playlist-modify-private.";
        await scope.Db.SaveChangesAsync();

        var before = await scope.Db.ServicePlaylistMappings.AsNoTracking().SingleAsync();
        var link = Assert.Single((await scope.Lifecycle.GetStatusAsync(scope.UserId,
            scope.Playlist.Id, default)).Links);

        Assert.True(link.ReconnectRequired);
        Assert.Equal(before.LastError, link.LastError);
        Assert.Equal(before.State, (await scope.Db.ServicePlaylistMappings.AsNoTracking().SingleAsync()).State);
    }

    [Fact]
    public async Task Status_DoesNotInferReconnectFromPersistentErrorWhenSpotifyScopesArePresent()
    {
        await using var scope = await Scope.CreateAsync("spotify");
        scope.Account.Scopes = "playlist-read-private playlist-modify-private";
        var mapping = scope.AddMapping();
        mapping.State = "creation_failed";
        mapping.ServicePlaylistId = "pending:creation";
        mapping.LastError = "Reconnect Spotify and grant playlist-read-private and playlist-modify-private.";
        await scope.Db.SaveChangesAsync();

        var link = Assert.Single((await scope.Lifecycle.GetStatusAsync(scope.UserId,
            scope.Playlist.Id, default)).Links);

        Assert.False(link.ReconnectRequired);
        Assert.NotNull(link.LastError);
    }

    [Fact]
    public async Task Status_DoesNotRequirePrivateWriteScopesForImportOnlySpotifyLink()
    {
        await using var scope = await Scope.CreateAsync("spotify");
        scope.Account.Scopes = "playlist-read-private";
        var mapping = scope.AddMapping();
        mapping.SyncMode = "import_only";
        mapping.LastError = "A prior write failed.";
        await scope.Db.SaveChangesAsync();

        var link = Assert.Single((await scope.Lifecycle.GetStatusAsync(scope.UserId,
            scope.Playlist.Id, default)).Links);

        Assert.False(link.ReconnectRequired);
    }

    [Theory]
    [InlineData("disconnected", "remote-account", "active")]
    [InlineData("connected", "another-account", "active")]
    [InlineData("connected", "remote-account", "reconnect_required")]
    public async Task Status_RequiresReconnectForUnavailableOrMismatchedAccount(
        string accountState, string accountId, string linkState)
    {
        await using var scope = await Scope.CreateAsync("spotify");
        var mapping = scope.AddMapping();
        scope.Account.ConnectionState = accountState;
        scope.Account.ExternalAccountId = accountId;
        mapping.State = linkState;
        await scope.Db.SaveChangesAsync();

        var link = Assert.Single((await scope.Lifecycle.GetStatusAsync(scope.UserId,
            scope.Playlist.Id, default)).Links);

        Assert.True(link.ReconnectRequired);
    }

    [Fact]
    public async Task Attach_InitializesExistingProviderCopyOnCanonicalPlaylist()
    {
        await using var scope = await Scope.CreateAsync("youtube");
        var preview = await scope.Lifecycle.PreviewAttachAsync(scope.UserId, scope.Playlist.Id,
            "youtube", "remote-playlist", default);

        var status = await scope.Lifecycle.AttachAsync(scope.UserId, scope.Playlist.Id,
            "youtube", "remote-playlist", "bidirectional", "combine", preview.PreviewToken, default);

        var link = Assert.Single(status.Links);
        Assert.Equal(scope.Playlist.Id, status.PlaylistId);
        Assert.Equal("active", link.State);
        Assert.Equal("remote-playlist", link.ServicePlaylistId);
        var stored = await scope.Db.ServicePlaylistMappings.SingleAsync();
        Assert.Equal(scope.Playlist.Id, stored.PlaylistId);
        Assert.Equal(scope.Account.ExternalAccountId, stored.ExternalAccountId);
        Assert.Null(stored.InitialMode);
        Assert.NotNull(stored.BaselineJson);
        Assert.Equal(0, scope.Writer.Creates);
        Assert.Equal(0, scope.Writer.Writes);
    }

    [Fact]
    public async Task Attach_UnavailableVideoDoesNotBlockReadableTracks()
    {
        await using var scope = await Scope.CreateAsync("youtube");
        var observation = new TrackObservation { Id = Guid.NewGuid(), SourceType = "youtube",
            ExternalId = "readable", Title = "Song", MatchStatus = TrackMatchingStatuses.Pending };
        scope.Db.TrackObservations.Add(observation);
        await scope.Db.SaveChangesAsync();
        scope.Provider.SnapshotOverride = new PlaylistRemoteSnapshot("remote-playlist", "Remote songs",
            [new PlaylistRemoteTrack("unavailable-video", null, null, "Unavailable item", 0, IsAvailable: false),
             new PlaylistRemoteTrack("readable", null, observation.Id, "Song", 1)],
            UnavailableItemCount: 1);

        var preview = await scope.Lifecycle.PreviewAttachAsync(
            scope.UserId, scope.Playlist.Id, "youtube", "remote-playlist", default);
        Assert.Equal(1, preview.UnavailableItemCount);
        Assert.Equal(1, preview.Additions);
        await scope.Lifecycle.AttachAsync(scope.UserId, scope.Playlist.Id,
            "youtube", "remote-playlist", "bidirectional", "combine", preview.PreviewToken, default);

        Assert.Equal(observation.Id, (await scope.Db.PlaylistEntries.SingleAsync()).TrackObservationId);
        Assert.Equal("active", (await scope.Db.ServicePlaylistMappings.SingleAsync()).State);
        Assert.Equal(0, scope.Writer.Writes);
    }

    [Fact]
    public async Task PreviewAttach_StillRejectsPartialReadsWithoutUnavailableItems()
    {
        await using var scope = await Scope.CreateAsync("youtube");
        scope.Provider.SnapshotOverride = new PlaylistRemoteSnapshot("remote-playlist", "Remote songs",
            [], IsComplete: false);

        var error = await Assert.ThrowsAsync<PlatformApiException>(() => scope.Lifecycle.PreviewAttachAsync(
            scope.UserId, scope.Playlist.Id, "youtube", "remote-playlist", default));

        Assert.Equal("incomplete_playlist", error.Code);
        Assert.Contains("Refresh", error.Message);
    }

    [Fact]
    public async Task Attach_ReusesOwnUnlinkedCopyAndDropsDifferentOldTombstone()
    {
        await using var scope = await Scope.CreateAsync("youtube");
        var old = scope.AddMapping();
        old.State = "unlinked";
        old.ServicePlaylistId = "old-copy";
        var same = scope.AddMapping();
        same.State = "unlinked";
        await scope.Db.SaveChangesAsync();

        var preview = await scope.Lifecycle.PreviewAttachAsync(scope.UserId, scope.Playlist.Id,
            "youtube", "remote-playlist", default);
        var status = await scope.Lifecycle.AttachAsync(scope.UserId, scope.Playlist.Id,
            "youtube", "remote-playlist", "bidirectional", "combine", preview.PreviewToken, default);

        Assert.Equal(same.Id, Assert.Single(status.Links).MappingId);
        Assert.Equal("active", (await scope.Db.ServicePlaylistMappings.SingleAsync()).State);
        Assert.Equal(0, scope.Writer.Creates);
    }

    [Fact]
    public async Task Attach_AllowsPreviouslyUnlinkedCopyToBelongToAnotherCanonicalPlaylist()
    {
        await using var scope = await Scope.CreateAsync("youtube");
        var other = new Playlist
        {
            Id = Guid.NewGuid(), UserId = scope.UserId, Name = "Old collection",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        scope.Db.Playlists.Add(other);
        var obsolete = scope.AddMapping();
        obsolete.PlaylistId = other.Id;
        obsolete.State = "unlinked";
        await scope.Db.SaveChangesAsync();

        var preview = await scope.Lifecycle.PreviewAttachAsync(scope.UserId, scope.Playlist.Id,
            "youtube", "remote-playlist", default);
        var status = await scope.Lifecycle.AttachAsync(scope.UserId, scope.Playlist.Id,
            "youtube", "remote-playlist", "bidirectional", "combine", preview.PreviewToken, default);

        Assert.Equal(scope.Playlist.Id, status.PlaylistId);
        Assert.Equal(2, await scope.Db.ServicePlaylistMappings.CountAsync());
        Assert.Equal(other.Id, obsolete.PlaylistId);
        Assert.Equal("unlinked", obsolete.State);
    }

    [Fact]
    public async Task Attach_FailedInitializationCanRetryWithFreshPreviewWithoutNewMapping()
    {
        await using var scope = await Scope.CreateAsync("youtube");
        var preview = await scope.Lifecycle.PreviewAttachAsync(scope.UserId, scope.Playlist.Id,
            "youtube", "remote-playlist", default);
        scope.Provider.FailReadNumber = 3;

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Lifecycle.AttachAsync(
            scope.UserId, scope.Playlist.Id, "youtube", "remote-playlist",
            "bidirectional", "combine", preview.PreviewToken, default));
        var pending = Assert.Single(await scope.Db.ServicePlaylistMappings.ToListAsync());
        Assert.Equal("initialization_pending", pending.LastSyncStatus);
        Assert.Equal("paused", pending.State);

        var retry = await scope.Lifecycle.PreviewPendingInitializationAsync(scope.UserId,
            scope.Playlist.Id, pending.Id, default);
        var status = await scope.Lifecycle.RetryPendingInitializationAsync(scope.UserId,
            scope.Playlist.Id, pending.Id, retry.PreviewToken, default);

        Assert.Equal(pending.Id, Assert.Single(status.Links).MappingId);
        Assert.Equal("active", (await scope.Db.ServicePlaylistMappings.SingleAsync()).State);
        Assert.Single(await scope.Db.ServicePlaylistMappings.ToListAsync());
        Assert.Equal(0, scope.Writer.Creates);
    }

    [Fact]
    public async Task Attach_ClaimsUncertainCreationAfterPreviewButNotActiveCreationLease()
    {
        await using var scope = await Scope.CreateAsync("youtube");
        var marker = scope.AddMapping();
        marker.ServicePlaylistId = "pending:lost-response";
        marker.State = "creation_uncertain";
        await scope.Db.SaveChangesAsync();
        var preview = await scope.Lifecycle.PreviewAttachAsync(scope.UserId, scope.Playlist.Id,
            "youtube", "remote-playlist", default);

        scope.Playlist.SyncLeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        marker.State = "creating";
        await scope.Db.SaveChangesAsync();
        var busy = await Assert.ThrowsAsync<PlatformApiException>(() => scope.Lifecycle.AttachAsync(
            scope.UserId, scope.Playlist.Id, "youtube", "remote-playlist",
            "bidirectional", "combine", preview.PreviewToken, default));
        Assert.Equal("sync_in_progress", busy.Code);

        scope.Playlist.SyncLeaseExpiresAt = null;
        marker.State = "creation_uncertain";
        await scope.Db.SaveChangesAsync();
        var status = await scope.Lifecycle.AttachAsync(scope.UserId, scope.Playlist.Id,
            "youtube", "remote-playlist", "bidirectional", "combine", preview.PreviewToken, default);
        Assert.Equal(marker.Id, Assert.Single(status.Links).MappingId);
        Assert.Equal("remote-playlist", marker.ServicePlaylistId);
        Assert.Equal("active", marker.State);
        Assert.Single(await scope.Db.ServicePlaylistMappings.ToListAsync());
        Assert.Equal(0, scope.Writer.Creates);
    }

    [Fact]
    public async Task Unlink_RejectsUnsupportedRemoteDeleteBeforeChangingMapping()
    {
        await using var scope = await Scope.CreateAsync("spotify");
        var mapping = scope.AddMapping();
        await scope.Db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<PlatformApiException>(() => scope.Lifecycle.UnlinkAsync(
            scope.UserId, scope.Playlist.Id, mapping.Id, deleteRemote: true,
            deleteCanonicalIfLast: false, default));

        Assert.Equal("remote_delete_unsupported", error.Code);
        Assert.Equal("active", (await scope.Db.ServicePlaylistMappings.SingleAsync()).State);
        Assert.Equal(0, scope.Provider.Deletes);
    }

    [Fact]
    public async Task RemoteDeletion_RejectsAccountChangedInPlaceBeforeProviderCall()
    {
        await using var scope = await Scope.CreateAsync("youtube");
        var mapping = scope.AddMapping();
        await scope.Db.SaveChangesAsync();
        scope.Account.ExternalAccountId = "another-account";
        await scope.Db.SaveChangesAsync();

        var preview = await scope.Lifecycle.PreviewDisconnectAsync(scope.UserId, "youtube", default);
        Assert.False(Assert.Single(preview.Links).CanDeleteRemote);
        var error = await Assert.ThrowsAsync<PlatformApiException>(() => scope.Lifecycle.UnlinkAsync(
            scope.UserId, scope.Playlist.Id, mapping.Id, deleteRemote: true,
            deleteCanonicalIfLast: false, default));
        Assert.Equal("account_changed", error.Code);
        Assert.Equal("active", mapping.State);
        Assert.Equal(0, scope.Provider.Deletes);
    }

    [Theory]
    [InlineData("creation_uncertain", "connected")]
    [InlineData("paused", "reconnect_required")]
    public async Task Unlink_CannotDeleteCanonicalWhileAnotherAccountRetainsMapping(
        string otherState, string otherConnectionState)
    {
        await using var scope = await Scope.CreateAsync("youtube");
        var mapping = scope.AddMapping();
        scope.Db.ConnectedServiceAccounts.Add(new ConnectedServiceAccount
        {
            Id = 302, UserId = scope.UserId, Service = "spotify", ExternalAccountId = "spotify-account",
            ConnectionState = otherConnectionState
        });
        scope.Db.ServicePlaylistMappings.Add(new ServicePlaylistMapping
        {
            Id = Guid.NewGuid(), PlaylistId = scope.Playlist.Id, UserId = scope.UserId,
            ConnectedServiceAccountId = 302, ExternalAccountId = "spotify-account",
            Service = "spotify", ServicePlaylistId = "pending:maybe-created",
            SyncMode = "bidirectional", State = otherState
        });
        await scope.Db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<PlatformApiException>(() => scope.Lifecycle.UnlinkAsync(
            scope.UserId, scope.Playlist.Id, mapping.Id, deleteRemote: false,
            deleteCanonicalIfLast: true, default));
        Assert.Equal("other_platforms_remain", error.Code);
        Assert.True(await scope.Db.Playlists.AnyAsync(item => item.Id == scope.Playlist.Id));
        Assert.Equal("active", mapping.State);
    }

    [Fact]
    public async Task Disconnect_RequiresReviewOfEveryLinkAndPreservesCanonicalByDefault()
    {
        await using var scope = await Scope.CreateAsync("youtube");
        var mapping = scope.AddMapping();
        await scope.Db.SaveChangesAsync();
        var preview = await scope.Lifecycle.PreviewDisconnectAsync(scope.UserId, "youtube", default);
        Assert.Equal(mapping.Id, Assert.Single(preview.Links).MappingId);

        var error = await Assert.ThrowsAsync<PlatformApiException>(() =>
            scope.Lifecycle.DisconnectAsync(scope.UserId, "youtube", [], default));
        Assert.Equal("disconnect_review_stale", error.Code);
        Assert.Equal("connected", (await scope.Db.ConnectedServiceAccounts.SingleAsync()).ConnectionState);

        await scope.Lifecycle.DisconnectAsync(scope.UserId, "youtube",
            [new(mapping.Id, DeleteRemote: false, DeleteCanonicalIfLast: false)], default);
        Assert.True(await scope.Db.Playlists.AnyAsync(item => item.Id == scope.Playlist.Id));
        Assert.Equal("paused", (await scope.Db.ServicePlaylistMappings.SingleAsync()).State);
        Assert.Equal("disconnected", (await scope.Db.ConnectedServiceAccounts.SingleAsync()).ConnectionState);
        Assert.Equal(0, scope.Provider.Deletes);
    }

    [Fact]
    public async Task Rename_RejectsProviderChangeAfterPreview()
    {
        await using var scope = await Scope.CreateAsync("youtube");
        scope.AddMapping();
        await scope.Db.SaveChangesAsync();
        var preview = await scope.Lifecycle.PreviewRenameAsync(scope.UserId, scope.Playlist.Id,
            "New title", default);
        scope.Provider.Name = "Changed after preview";

        var error = await Assert.ThrowsAsync<PlatformApiException>(() => scope.Lifecycle.ConfirmRenameAsync(
            scope.UserId, scope.Playlist.Id, "New title", preview.PreviewToken, default));

        Assert.Equal("stale_preview", error.Code);
        Assert.Equal("Local songs", (await scope.Db.Playlists.SingleAsync()).Name);
        Assert.Null((await scope.Db.ServicePlaylistMappings.SingleAsync()).DesiredName);
    }

    [Theory]
    [InlineData("paused")]
    [InlineData("missing")]
    [InlineData("creation_uncertain")]
    public async Task Rename_RejectsUnavailableRetainedCopyBeforeReadingProviders(string state)
    {
        await using var scope = await Scope.CreateAsync("youtube");
        var mapping = scope.AddMapping();
        mapping.State = state;
        mapping.PendingName = "External rename";
        await scope.Db.SaveChangesAsync();

        var rename = await Assert.ThrowsAsync<PlatformApiException>(() =>
            scope.Lifecycle.PreviewRenameAsync(scope.UserId, scope.Playlist.Id, "New title", default));
        var proposal = await Assert.ThrowsAsync<PlatformApiException>(() =>
            scope.Lifecycle.PreviewRenameProposalAsync(scope.UserId, scope.Playlist.Id,
                mapping.Id, default));

        Assert.Equal("rename_links_unavailable", rename.Code);
        Assert.Equal("rename_links_unavailable", proposal.Code);
        Assert.Equal(0, scope.Provider.Reads);
        Assert.Equal("Local songs", (await scope.Db.Playlists.SingleAsync()).Name);
    }

    private sealed class Scope : IAsyncDisposable
    {
        private Scope(ApplicationDbContext db, Playlist playlist, ConnectedServiceAccount account,
            FakePlatform platform, FakeWriter writer, PlaylistLinkLifecycleService lifecycle)
        {
            Db = db;
            Playlist = playlist;
            Account = account;
            Provider = platform;
            Writer = writer;
            Lifecycle = lifecycle;
        }

        public int UserId => 700;
        public ApplicationDbContext Db { get; }
        public Playlist Playlist { get; }
        public ConnectedServiceAccount Account { get; }
        public FakePlatform Provider { get; }
        public FakeWriter Writer { get; }
        public PlaylistLinkLifecycleService Lifecycle { get; }
        public PlaylistSyncCoordinator Coordinator { get; private set; } = null!;

        public static async Task<Scope> CreateAsync(string service)
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var playlist = new Playlist
            {
                Id = Guid.NewGuid(), UserId = 700, Name = "Local songs",
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
            };
            var account = new ConnectedServiceAccount
            {
                Id = 301, UserId = 700, Service = service, ExternalAccountId = "remote-account",
                ConnectionState = "connected"
            };
            db.Playlists.Add(playlist);
            db.ConnectedServiceAccounts.Add(account);
            await db.SaveChangesAsync();
            var platform = new FakePlatform(db, account);
            var writer = new FakeWriter(service);
            var registry = new PlatformRegistry([platform]);
            var coordinator = new PlaylistSyncCoordinator(db, [platform], [writer], TimeProvider.System,
                NullLogger<PlaylistSyncCoordinator>.Instance);
            var lifecycle = new PlaylistLinkLifecycleService(db, registry, [platform], coordinator,
                DataProtectionProvider.Create("Cantaro.Lifecycle.Tests"));
            return new Scope(db, playlist, account, platform, writer, lifecycle)
            {
                Coordinator = coordinator
            };
        }

        public ServicePlaylistMapping AddMapping()
        {
            var mapping = new ServicePlaylistMapping
            {
                Id = Guid.NewGuid(), PlaylistId = Playlist.Id, UserId = UserId,
                ConnectedServiceAccountId = Account.Id, ExternalAccountId = Account.ExternalAccountId,
                Service = Account.Service, ServicePlaylistId = "remote-playlist",
                State = "active", SyncMode = "bidirectional"
            };
            Db.ServicePlaylistMappings.Add(mapping);
            return mapping;
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class FakePlatform(ApplicationDbContext db, ConnectedServiceAccount account)
        : IPlatformService, IPlaylistSyncProvider
    {
        public string PlatformId => account.Service;
        public string Name { get; set; } = "Remote songs";
        public IReadOnlyList<PlatformPlaylistDto> Playlists { get; set; } = [];
        public Task<IReadOnlyList<PlaylistRemoteCatalogItem>> ListPlaylistsAsync(
            PlatformAccountContext context, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<PlaylistRemoteCatalogItem>>(
                Playlists.Select(item => new PlaylistRemoteCatalogItem(item.Id, item.Title)).ToList());
        public PlaylistRemoteSnapshot? SnapshotOverride { get; set; }
        public string? ResolvedId { get; set; }
        public int? FailReadNumber { get; set; }
        public int? ReadFailureNumber { get; set; }
        public Exception? ReadFailureAtNumber { get; set; }
        public Exception? ReadFailure { get; set; }
        public Action? OnRead { get; set; }
        private int _readCount;
        public int Reads => _readCount;
        public int Deletes { get; private set; }
        public Task<PlaylistRemoteSnapshot> ReadAsync(PlatformAccountContext context, string playlistId, CancellationToken ct)
        {
            OnRead?.Invoke();
            var readNumber = ++_readCount;
            if (ReadFailure is { } failure) throw failure;
            if (readNumber == FailReadNumber) throw new InvalidOperationException("Provider read failed.");
            if (readNumber == ReadFailureNumber && ReadFailureAtNumber is { } numberedFailure)
                throw numberedFailure;
            return Task.FromResult(SnapshotOverride is null
                ? new PlaylistRemoteSnapshot(playlistId, Name, [])
                : SnapshotOverride with { Id = playlistId, Name = Name });
        }
        public Task<string?> ResolveAsync(PlatformAccountContext context, PlaylistEntry entry, CancellationToken ct)
            => Task.FromResult(ResolvedId);
        public Task RenameAsync(PlatformAccountContext context, string playlistId, string name, CancellationToken ct)
        {
            Name = name;
            return Task.CompletedTask;
        }
        public Task DeleteAsync(PlatformAccountContext context, string playlistId, CancellationToken ct)
        {
            Deletes++;
            return Task.CompletedTask;
        }
        public Task<ConnectedServiceAccount?> GetConnectedAccountAsync(int userId)
            => Task.FromResult<ConnectedServiceAccount?>(account);
        public string GetAuthorizationUrl(string redirectUri, string state) => throw new NotSupportedException();
        public Task<ConnectedServiceAccount> ExchangeCodeAndSaveAsync(int userId, string code, string redirectUri)
            => throw new NotSupportedException();
        public async Task DisconnectAsync(int userId)
        {
            account.ConnectionState = "disconnected";
            foreach (var mapping in await db.ServicePlaylistMappings.Where(item => item.ConnectedServiceAccountId == account.Id).ToListAsync())
                if (mapping.State == "active") mapping.State = "paused";
            await db.SaveChangesAsync();
        }
        public Task<IReadOnlyList<PlatformPlaylistDto>> GetPlaylistsAsync(int userId)
            => Task.FromResult(Playlists);
        public Task<IReadOnlyList<PlatformSongDto>> GetPlaylistSongsAsync(int userId, string playlistId)
            => throw new NotSupportedException();
        public Task<Guid> SyncPlaylistAsync(PlatformAccountContext context, string playlistId, CancellationToken ct)
            => throw new NotSupportedException();
        public bool TryValidatePlaylistId(string playlistId, out string? error)
        {
            error = string.IsNullOrWhiteSpace(playlistId) ? "Playlist ID required." : null;
            return error is null;
        }
    }

    private sealed class FakeWriter(string platformId) : IPlaylistWriter
    {
        public string PlatformId => platformId;
        public int Creates { get; private set; }
        public int Writes { get; private set; }
        public IReadOnlyList<string> LastWrittenIds { get; private set; } = [];
        public Action<IReadOnlyList<string>>? OnReconcile { get; set; }
        public Task ValidateCreationAsync(PlatformAccountContext account, CancellationToken ct) => Task.CompletedTask;
        public Task<string> CreatePlaylistAsync(PlatformAccountContext account, string name, CancellationToken ct)
        {
            Creates++;
            return Task.FromResult("created-playlist");
        }
        public Task ValidateDestinationAsync(PlatformAccountContext account, string playlistId, CancellationToken ct)
            => Task.CompletedTask;
        public Task ReconcileAsync(PlatformAccountContext account, string playlistId, IReadOnlyList<string> trackIds,
            CancellationToken ct)
        {
            Writes++;
            LastWrittenIds = trackIds.ToArray();
            OnReconcile?.Invoke(trackIds);
            return Task.CompletedTask;
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; private set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
        public void Advance(TimeSpan duration) => Now = Now.Add(duration);
    }
}
