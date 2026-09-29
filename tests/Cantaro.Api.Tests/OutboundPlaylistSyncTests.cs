using System.Text.Json;
using Cantaro.Api.Controllers;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class OutboundPlaylistSyncTests
{
    [Fact]
    public async Task Sync_UsesCanonicalDestinationIdsInPlaylistOrderAndPersistsMapping()
    {
        await using var db = CreateDb();
        var playlist = await SeedAsync(db);
        AddTrack(db, playlist, 1, "second", "youtube");
        AddTrack(db, playlist, 0, "first", "youtube");
        await db.SaveChangesAsync();
        var writer = new RecordingWriter();

        var id = await new OutboundPlaylistSyncService(db, [writer])
            .SyncAsync(new(42, 99), "youtube", playlist.Id, null, default);

        Assert.Equal(playlist.Id, id);
        Assert.Equal(["first", "second"], writer.Writes.Single());
        var mapping = await db.ServicePlaylistMappings.SingleAsync();
        Assert.Equal("from_cantaro", mapping.SyncMode);
        Assert.Equal("success", mapping.LastSyncStatus);
        Assert.Equal(99, mapping.ConnectedServiceAccountId);
        Assert.Equal("created-destination", mapping.ServicePlaylistId);
        Assert.Equal(1, writer.Creations);
        Assert.NotNull(mapping.LastSyncedAt);
    }

    [Fact]
    public async Task Sync_MissingDestinationMatchesBlocksAllWritesAndReportsTrack()
    {
        await using var db = CreateDb();
        var playlist = await SeedAsync(db);
        AddTrack(db, playlist, 0, "matched", "youtube");
        AddTrack(db, playlist, 1, "missing", "spotify");
        await db.SaveChangesAsync();
        var writer = new RecordingWriter();

        var error = await Assert.ThrowsAsync<PlatformApiException>(() => new OutboundPlaylistSyncService(db, [writer])
            .SyncAsync(new(42, 99), "youtube", playlist.Id, null, default));

        Assert.Equal("unmatched_tracks", error.Code);
        Assert.Contains("missing", error.Message);
        Assert.True(MusicSyncFailureClassifier.Classify(error).Retryable);
        Assert.Empty(writer.Writes);
        Assert.Equal(0, writer.Creations);
        Assert.Empty(await db.ServicePlaylistMappings.ToListAsync());
    }

    [Fact]
    public async Task Sync_UsesExactDestinationObservationWhileCanonicalMatchIsPending()
    {
        await using var db = CreateDb();
        var playlist = await SeedAsync(db);
        db.PlaylistEntries.Add(new PlaylistEntry
        {
            Id = Guid.NewGuid(), PlaylistId = playlist.Id,
            TrackObservation = new TrackObservation
            {
                Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = "observed",
                Title = "Pending song", MatchStatus = "pending"
            }
        });
        await db.SaveChangesAsync();
        var writer = new RecordingWriter();

        await new OutboundPlaylistSyncService(db, [writer]).SyncAsync(new(42, 99), "youtube", playlist.Id, null, default);

        Assert.Equal(["observed"], writer.Writes.Single());
    }

    [Theory]
    [InlineData(43, 99)]
    [InlineData(42, 100)]
    public async Task Sync_RejectsOtherUsersAndReplacementAccounts(int userId, int accountId)
    {
        await using var db = CreateDb();
        var playlist = await SeedAsync(db);
        var writer = new RecordingWriter();

        await Assert.ThrowsAsync<PlatformApiException>(() => new OutboundPlaylistSyncService(db, [writer])
            .SyncAsync(new(userId, accountId), "youtube", playlist.Id, null, default));

        Assert.Equal(0, writer.Validations);
        Assert.Equal(0, writer.Creations);
        Assert.Empty(writer.Writes);
    }

    [Fact]
    public async Task Sync_RejectsAnAccountReconnectedAsAnotherProviderUser()
    {
        await using var db = CreateDb();
        var playlist = await SeedAsync(db);
        var writer = new RecordingWriter();

        var error = await Assert.ThrowsAsync<PlatformApiException>(() => new OutboundPlaylistSyncService(db, [writer])
            .SyncAsync(new(42, 99, "previous-account"), "youtube", playlist.Id, null, default));

        Assert.Equal("account_changed", error.Code);
        Assert.Equal(0, writer.Validations);
        Assert.Empty(writer.Writes);
    }

    [Fact]
    public async Task Sync_UsesCurrentCanonicalIdentityInsteadOfStaleObservation()
    {
        await using var db = CreateDb();
        var playlist = await SeedAsync(db);
        AddTrack(db, playlist, 0, "current-track", "youtube");
        var entry = db.ChangeTracker.Entries<PlaylistEntry>().Single().Entity;
        entry.TrackObservation = new TrackObservation
        {
            Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = "stale-track",
            Title = "Stale song", MatchStatus = "matched", TrackId = Guid.NewGuid()
        };
        db.TrackObservations.Add(entry.TrackObservation);
        await db.SaveChangesAsync();
        var writer = new RecordingWriter();

        await new OutboundPlaylistSyncService(db, [writer]).SyncAsync(new(42, 99), "youtube", playlist.Id, null, default);

        Assert.Equal(["current-track"], writer.Writes.Single());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Sync_RejectsUnlinkedOrImportOnlyMappings(bool otherPlaylist)
    {
        await using var db = CreateDb();
        var playlist = await SeedAsync(db);
        db.ServicePlaylistMappings.Add(new ServicePlaylistMapping
        {
            Id = Guid.NewGuid(), PlaylistId = otherPlaylist ? Guid.NewGuid() : playlist.Id,
            ConnectedServiceAccountId = 99, Service = "youtube",
            ServicePlaylistId = otherPlaylist ? "destination" : "another-destination", SyncMode = "import_only"
        });
        await db.SaveChangesAsync();
        var writer = new RecordingWriter();

        var error = await Assert.ThrowsAsync<PlatformApiException>(() => new OutboundPlaylistSyncService(db, [writer])
            .SyncAsync(new(42, 99), "youtube", playlist.Id, "destination", default));

        Assert.Equal(otherPlaylist ? "unlinked_destination" : "import_only_source", error.Code);
        Assert.Empty(writer.Writes);
    }

    [Fact]
    public async Task Sync_RecordsProviderCooldownAndBlocksRetryUntilDeadline()
    {
        await using var db = CreateDb();
        var playlist = await SeedAsync(db);
        var writer = new RecordingWriter { Failure = new PlatformApiException("rate_limited", "Try later", 429, TimeSpan.FromMinutes(10)) };
        var service = new OutboundPlaylistSyncService(db, [writer]);

        await Assert.ThrowsAsync<PlatformApiException>(() => service.SyncAsync(new(42, 99), "youtube", playlist.Id, null, default));
        var mapping = await db.ServicePlaylistMappings.SingleAsync();
        Assert.Equal("rate_limited", mapping.LastSyncStatus);
        Assert.Null(mapping.LastError);
        Assert.True(mapping.NextAttemptAt > DateTimeOffset.UtcNow.AddMinutes(9));

        var validationsBeforeRetry = writer.Validations;
        var cooldown = await Assert.ThrowsAsync<PlatformApiException>(() => service.SyncAsync(new(42, 99), "youtube", playlist.Id, null, default));
        Assert.Equal("youtube_rate_limited", cooldown.Code);
        Assert.Equal(validationsBeforeRetry, writer.Validations);
        Assert.Single(writer.Writes);

        mapping.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();
        writer.Failure = null;
        await service.SyncAsync(new(42, 99), "youtube", playlist.Id, null, default);

        Assert.Equal("success", (await db.ServicePlaylistMappings.SingleAsync()).LastSyncStatus);
        Assert.Equal(1, writer.Creations);
        Assert.Null((await db.ServicePlaylistMappings.SingleAsync()).NextAttemptAt);
        Assert.Empty(writer.Writes.Last()); // An empty Cantaro playlist intentionally clears its destination.
    }

    [Fact]
    public async Task Sync_AmbiguousCreationPreservesReservationAndDoesNotCreateAgain()
    {
        await using var db = CreateDb();
        var playlist = await SeedAsync(db);
        var writer = new RecordingWriter
        {
            CreationFailure = new PlatformApiException(
                "youtube_playlist_creation_unknown", "The provider response was lost.", 409)
        };
        var service = new OutboundPlaylistSyncService(db, [writer]);

        var first = await Assert.ThrowsAsync<PlatformApiException>(() =>
            service.SyncAsync(new(42, 99), "youtube", playlist.Id, null, default));
        Assert.Equal("destination_creation_uncertain", first.Code);
        var reservation = await db.ServicePlaylistMappings.SingleAsync();
        Assert.StartsWith("pending:", reservation.ServicePlaylistId);
        Assert.Equal("creation_uncertain", reservation.LastSyncStatus);

        writer.CreationFailure = null;
        var retry = await Assert.ThrowsAsync<PlatformApiException>(() =>
            service.SyncAsync(new(42, 99), "youtube", playlist.Id, null, default));
        Assert.Equal("destination_creation_uncertain", retry.Code);
        Assert.Equal(1, writer.Creations);
        Assert.Empty(writer.Writes);
    }

    [Fact]
    public async Task Sync_ConfirmedCreationRejectionReleasesReservationForRetry()
    {
        await using var db = CreateDb();
        var playlist = await SeedAsync(db);
        var writer = new RecordingWriter
        {
            CreationFailure = new PlatformApiException(
                "youtube_playlist_creation_rejected", "Permission denied.", 403)
        };
        var service = new OutboundPlaylistSyncService(db, [writer]);

        var error = await Assert.ThrowsAsync<PlatformApiException>(() =>
            service.SyncAsync(new(42, 99), "youtube", playlist.Id, null, default));
        Assert.Equal("youtube_playlist_creation_rejected", error.Code);
        Assert.Empty(await db.ServicePlaylistMappings.ToListAsync());

        writer.CreationFailure = null;
        await service.SyncAsync(new(42, 99), "youtube", playlist.Id, null, default);
        Assert.Equal(2, writer.Creations);
        Assert.Equal("created-destination", (await db.ServicePlaylistMappings.SingleAsync()).ServicePlaylistId);
    }

    [Fact]
    public async Task Sync_SpotifyQuotaDuringCreateReleasesReservationWithoutMarkingCreationUncertain()
    {
        await using var db = CreateDb();
        var playlist = await SeedAsync(db, "spotify");
        var writer = new RecordingWriter
        {
            PlatformId = "spotify",
            CreationFailure = new PlatformApiException("spotify_quota_exceeded", "Quota reached", 429,
                TimeSpan.FromHours(3))
        };
        var service = new OutboundPlaylistSyncService(db, [writer]);

        var failure = await Assert.ThrowsAsync<PlatformApiException>(() => service.SyncAsync(
            new(42, 99), "spotify", playlist.Id, null, default));

        Assert.Equal("spotify_quota_exceeded", failure.Code);
        Assert.Equal(429, failure.StatusCode);
        Assert.Empty(await db.ServicePlaylistMappings.ToListAsync());
        Assert.Equal(1, writer.Creations);

        writer.CreationFailure = null;
        await service.SyncAsync(new(42, 99), "spotify", playlist.Id, null, default);
        Assert.Equal(2, writer.Creations);
        Assert.Equal("created-destination", (await db.ServicePlaylistMappings.SingleAsync()).ServicePlaylistId);
    }

    [Fact]
    public async Task Sync_ExistingOutboundMappingReusesItsDestination()
    {
        await using var db = CreateDb();
        var playlist = await SeedAsync(db);
        db.ServicePlaylistMappings.Add(new ServicePlaylistMapping
        {
            Id = Guid.NewGuid(), PlaylistId = playlist.Id, ConnectedServiceAccountId = 99,
            Service = "youtube", ServicePlaylistId = "linked", SyncMode = "from_cantaro"
        });
        await db.SaveChangesAsync();
        var writer = new RecordingWriter();

        await new OutboundPlaylistSyncService(db, [writer])
            .SyncAsync(new(42, 99), "youtube", playlist.Id, null, default);

        Assert.Equal(0, writer.Creations);
        Assert.Equal("linked", writer.DestinationValidations.Single());
        Assert.Equal("linked", writer.WriteDestinations.Single());
    }

    [Fact]
    public async Task Sync_ImportedSourceCannotBeOverwrittenEvenWithoutMapping()
    {
        await using var db = CreateDb();
        var playlist = await SeedAsync(db);
        playlist.ImportedFromService = "youtube";
        await db.SaveChangesAsync();
        var writer = new RecordingWriter();

        var error = await Assert.ThrowsAsync<PlatformApiException>(() =>
            new OutboundPlaylistSyncService(db, [writer])
                .SyncAsync(new(42, 99), "youtube", playlist.Id, null, default));

        Assert.Equal("import_only_source", error.Code);
        Assert.Equal(0, writer.Creations);
        Assert.Empty(writer.Writes);
    }

    [Fact]
    public async Task Processor_ReportsCreatedDestinationAfterSuccessfulExport()
    {
        await using var db = CreateDb();
        var playlist = await SeedAsync(db);
        var job = new MusicSyncJob
        {
            Id = Guid.NewGuid(), UserId = 42, ConnectedServiceAccountId = 99, Service = "youtube",
            Status = MusicSyncJobStatuses.Queued, PlaylistCount = 1, SongCount = 0,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            PlaylistsJson = JsonSerializer.Serialize(new[]
            {
                new MusicSyncJobPlaylist(string.Empty, playlist.Name, 0, playlist.Id, "original-account")
            })
        };
        db.MusicSyncJobs.Add(job);
        await db.SaveChangesAsync();
        var writer = new RecordingWriter();
        var processor = new MusicSyncJobProcessor(db, new PlatformRegistry([new NoImportPlatform()]),
            new MusicSyncThrottleService(), NullLogger<MusicSyncJobProcessor>.Instance,
            new OutboundPlaylistSyncService(db, [writer]));

        Assert.True(await processor.ProcessNextAsync(default));

        var response = SyncJobsController.ToResponse(await db.MusicSyncJobs.SingleAsync());
        Assert.Equal("completed", response.Status);
        Assert.Equal("created-destination", Assert.Single(response.Results).ServicePlaylistId);
        Assert.Equal(1, writer.Creations);
    }

    [Fact]
    public async Task Processor_ExportsWithoutImportingAndReturnsDirectionAndSourceForRetry()
    {
        await using var db = CreateDb();
        var playlist = await SeedAsync(db);
        AddTrack(db, playlist, 0, "no-youtube-match", "spotify");
        var job = new MusicSyncJob
        {
            Id = Guid.NewGuid(), UserId = 42, ConnectedServiceAccountId = 99, Service = "youtube",
            Status = MusicSyncJobStatuses.Queued, PlaylistCount = 1, SongCount = 1,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            PlaylistsJson = JsonSerializer.Serialize(new[] { new MusicSyncJobPlaylist("destination", playlist.Name, 1, playlist.Id) })
        };
        db.MusicSyncJobs.Add(job);
        await db.SaveChangesAsync();
        var writer = new RecordingWriter();
        var platform = new NoImportPlatform();
        var processor = new MusicSyncJobProcessor(db, new PlatformRegistry([platform]), new MusicSyncThrottleService(),
            NullLogger<MusicSyncJobProcessor>.Instance, new OutboundPlaylistSyncService(db, [writer]));

        Assert.True(await processor.ProcessNextAsync(default));
        var response = SyncJobsController.ToResponse(await db.MusicSyncJobs.SingleAsync());
        Assert.Equal("export", response.Direction);
        Assert.Equal(playlist.Id.ToString(), response.CantaroPlaylistId);
        Assert.Equal("failed", response.Status);
        var result = Assert.Single(response.Results);
        Assert.Equal("unlinked_destination", result.ErrorCode);
        Assert.False(result.Retryable);
        Assert.Equal(playlist.Id.ToString(), result.CantaroPlaylistId);
        Assert.Empty(writer.Writes);
    }

    [Fact]
    public async Task CoordinatorAdapter_RejectsUnlinkedLegacyDestinationWithoutWriting()
    {
        await using var db = CreateDb();
        var playlist = await SeedAsync(db);
        var writer = new RecordingWriter();
        var provider = new CoordinatorPlatform();
        var coordinator = new PlaylistSyncCoordinator(db, [provider], [writer], TimeProvider.System,
            NullLogger<PlaylistSyncCoordinator>.Instance);

        var error = await Assert.ThrowsAsync<PlatformApiException>(() =>
            new OutboundPlaylistSyncService(db, [writer], coordinator).SyncAsync(
                new(42, 99, "original-account"), "youtube", playlist.Id, "unrelated-playlist", default));

        Assert.Equal("unlinked_destination", error.Code);
        Assert.Empty(writer.Writes);
        Assert.Equal(0, writer.Creations);
    }

    [Fact]
    public async Task CoordinatorAdapter_ReportsPartialLinkedSyncAfterWritingMatchedTracks()
    {
        await using var db = CreateDb();
        var playlist = await SeedAsync(db);
        AddTrack(db, playlist, 0, "matched", "youtube");
        AddTrack(db, playlist, 1, "missing", "spotify");
        db.ServicePlaylistMappings.Add(new ServicePlaylistMapping
        {
            Id = Guid.NewGuid(), PlaylistId = playlist.Id, UserId = 42,
            ConnectedServiceAccountId = 99, ExternalAccountId = "original-account",
            Service = "youtube", ServicePlaylistId = "linked", SyncMode = "bidirectional",
            State = "active", BaselineJson = new PlaylistSyncBaseline([]).Serialize()
        });
        await db.SaveChangesAsync();
        var provider = new CoordinatorPlatform();
        provider.KnownTrackIds["matched"] = await db.TrackSourceIds
            .Where(source => source.SourceType == "youtube" && source.ExternalId == "matched")
            .Select(source => source.TrackId).SingleAsync();
        var writer = new RecordingWriter { OnReconcile = ids => provider.RemoteIds = ids.ToList() };
        var coordinator = new PlaylistSyncCoordinator(db, [provider], [writer], TimeProvider.System,
            NullLogger<PlaylistSyncCoordinator>.Instance);

        var error = await Assert.ThrowsAsync<PlatformApiException>(() =>
            new OutboundPlaylistSyncService(db, [writer], coordinator).SyncAsync(
                new(42, 99, "original-account"), "youtube", playlist.Id, "linked", default));

        Assert.Equal("unmatched_tracks", error.Code);
        Assert.Equal(["matched"], Assert.Single(writer.Writes));
        var mapping = await db.ServicePlaylistMappings.SingleAsync();
        Assert.Equal("partial", mapping.LastSyncStatus);
        Assert.Equal(1, mapping.UnresolvedCount);
    }

    [Fact]
    public async Task CoordinatorAdapter_ReportsFailedProviderWriteInsteadOfCompletedJob()
    {
        await using var db = CreateDb();
        var playlist = await SeedAsync(db);
        AddTrack(db, playlist, 0, "matched", "youtube");
        db.ServicePlaylistMappings.Add(new ServicePlaylistMapping
        {
            Id = Guid.NewGuid(), PlaylistId = playlist.Id, UserId = 42,
            ConnectedServiceAccountId = 99, ExternalAccountId = "original-account",
            Service = "youtube", ServicePlaylistId = "linked", SyncMode = "bidirectional",
            State = "active", BaselineJson = new PlaylistSyncBaseline([]).Serialize()
        });
        db.MusicSyncJobs.Add(new MusicSyncJob
        {
            Id = Guid.NewGuid(), UserId = 42, ConnectedServiceAccountId = 99, Service = "youtube",
            Status = MusicSyncJobStatuses.Queued, PlaylistCount = 1, SongCount = 1,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            PlaylistsJson = JsonSerializer.Serialize(new[]
            {
                new MusicSyncJobPlaylist("linked", playlist.Name, 1, playlist.Id, "original-account")
            })
        });
        await db.SaveChangesAsync();
        var provider = new CoordinatorPlatform();
        var writer = new RecordingWriter
        {
            Failure = new PlatformApiException("provider_rejected", "The provider rejected the change.", 409)
        };
        var coordinator = new PlaylistSyncCoordinator(db, [provider], [writer], TimeProvider.System,
            NullLogger<PlaylistSyncCoordinator>.Instance);
        var processor = new MusicSyncJobProcessor(db, new PlatformRegistry([new NoImportPlatform()]),
            new MusicSyncThrottleService(), NullLogger<MusicSyncJobProcessor>.Instance,
            new OutboundPlaylistSyncService(db, [writer], coordinator));

        Assert.True(await processor.ProcessNextAsync(default));

        var response = SyncJobsController.ToResponse(await db.MusicSyncJobs.SingleAsync());
        Assert.Equal("failed", response.Status);
        Assert.Equal("playlist_sync_failed", Assert.Single(response.Results).ErrorCode);
        Assert.Equal("error", (await db.ServicePlaylistMappings.SingleAsync()).LastSyncStatus);
        Assert.Single(writer.Writes);
    }

    [Fact]
    public async Task CoordinatorAdapter_ForwardsSpotifyQuotaCooldownInsteadOfGenericConflict()
    {
        await using var db = CreateDb();
        var playlist = await SeedAsync(db, "spotify");
        AddTrack(db, playlist, 0, "matched", "spotify");
        db.ServicePlaylistMappings.Add(new ServicePlaylistMapping
        {
            Id = Guid.NewGuid(), PlaylistId = playlist.Id, UserId = 42,
            ConnectedServiceAccountId = 99, ExternalAccountId = "original-account",
            Service = "spotify", ServicePlaylistId = "linked", SyncMode = "bidirectional",
            State = "active", BaselineJson = new PlaylistSyncBaseline([]).Serialize()
        });
        await db.SaveChangesAsync();
        var provider = new CoordinatorPlatform { Service = "spotify" };
        var writer = new RecordingWriter
        {
            PlatformId = "spotify",
            Failure = new PlatformApiException("spotify_quota_exceeded", "Spotify quota reached.", 429,
                TimeSpan.FromHours(3))
        };
        var coordinator = new PlaylistSyncCoordinator(db, [provider], [writer], TimeProvider.System,
            NullLogger<PlaylistSyncCoordinator>.Instance);

        var error = await Assert.ThrowsAsync<PlatformApiException>(() =>
            new OutboundPlaylistSyncService(db, [writer], coordinator).SyncAsync(
                new(42, 99, "original-account"), "spotify", playlist.Id, "linked", default));

        Assert.Equal("spotify_quota_exceeded", error.Code);
        Assert.Equal(429, error.StatusCode);
        Assert.InRange(error.RetryAfter!.Value, TimeSpan.FromHours(2), TimeSpan.FromHours(3));
        var mapping = await db.ServicePlaylistMappings.SingleAsync();
        Assert.Equal("quota_limited", mapping.LastSyncStatus);
        Assert.Null(mapping.LastError);
        Assert.InRange(mapping.NextAttemptAt!.Value - DateTimeOffset.UtcNow,
            TimeSpan.FromHours(2), TimeSpan.FromHours(3));
    }

    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<Playlist> SeedAsync(ApplicationDbContext db, string service = "youtube")
    {
        var playlist = new Playlist { Id = Guid.NewGuid(), UserId = 42, Name = "Local songs" };
        db.Playlists.Add(playlist);
        db.ConnectedServiceAccounts.Add(new ConnectedServiceAccount
        {
            Id = 99, UserId = 42, Service = service, ExternalAccountId = "original-account"
        });
        await db.SaveChangesAsync();
        return playlist;
    }

    private static void AddTrack(ApplicationDbContext db, Playlist playlist, int position, string id, string service)
    {
        var track = new Track { Id = Guid.NewGuid(), CanonicalMetadata = JsonSerializer.Serialize(new { Title = id }) };
        track.SourceIds.Add(new TrackSourceId
        {
            Id = Guid.NewGuid(), TrackId = track.Id, SourceType = service, ExternalId = id, Confidence = 1
        });
        db.PlaylistEntries.Add(new PlaylistEntry
        {
            Id = Guid.NewGuid(), PlaylistId = playlist.Id, Position = position, Track = track, TrackId = track.Id
        });
    }

    private sealed class RecordingWriter : IPlaylistWriter
    {
        public string PlatformId { get; init; } = "youtube";
        public int Validations { get; private set; }
        public int Creations { get; private set; }
        public Exception? Failure { get; set; }
        public Exception? CreationFailure { get; set; }
        public List<string[]> Writes { get; } = [];
        public List<string> WriteDestinations { get; } = [];
        public List<string> DestinationValidations { get; } = [];
        public Action<IReadOnlyList<string>>? OnReconcile { get; set; }
        public Task ValidateCreationAsync(PlatformAccountContext account, CancellationToken cancellationToken)
        {
            Validations++;
            return Task.CompletedTask;
        }
        public Task<string> CreatePlaylistAsync(PlatformAccountContext account, string name, CancellationToken cancellationToken)
        {
            Creations++;
            if (CreationFailure is not null) throw CreationFailure;
            return Task.FromResult("created-destination");
        }
        public Task ValidateDestinationAsync(PlatformAccountContext account, string playlistId, CancellationToken cancellationToken)
        {
            Validations++;
            DestinationValidations.Add(playlistId);
            return Task.CompletedTask;
        }
        public Task ReconcileAsync(PlatformAccountContext account, string playlistId, IReadOnlyList<string> trackIds, CancellationToken cancellationToken)
        {
            Writes.Add(trackIds.ToArray());
            WriteDestinations.Add(playlistId);
            if (Failure is not null) throw Failure;
            OnReconcile?.Invoke(trackIds);
            return Task.CompletedTask;
        }
    }

    private sealed class CoordinatorPlatform : IPlaylistSyncProvider
    {
        public string Service { get; init; } = "youtube";
        public string PlatformId => Service;
        public Task<IReadOnlyList<PlaylistRemoteCatalogItem>> ListPlaylistsAsync(
            PlatformAccountContext account, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<PlaylistRemoteCatalogItem>>([]);
        public List<string> RemoteIds { get; set; } = [];
        public Dictionary<string, Guid> KnownTrackIds { get; } = [];
        public Task<PlaylistRemoteSnapshot> ReadAsync(PlatformAccountContext account, string playlistId,
            CancellationToken cancellationToken)
            => Task.FromResult(new PlaylistRemoteSnapshot(playlistId, "Local songs",
                RemoteIds.Select((id, index) => new PlaylistRemoteTrack(id,
                    KnownTrackIds.TryGetValue(id, out var trackId) ? trackId : null, null, id, index)).ToList()));
        public Task<string?> ResolveAsync(PlatformAccountContext account, PlaylistEntry entry,
            CancellationToken cancellationToken)
            => Task.FromResult(entry.Track?.SourceIds.FirstOrDefault(source => source.SourceType == Service)?.ExternalId);
        public Task RenameAsync(PlatformAccountContext account, string playlistId, string name,
            CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteAsync(PlatformAccountContext account, string playlistId,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoImportPlatform : IPlatformService
    {
        public string PlatformId => "youtube";
        public Task<ConnectedServiceAccount?> GetConnectedAccountAsync(int userId) => throw new NotSupportedException();
        public string GetAuthorizationUrl(string redirectUri, string state) => throw new NotSupportedException();
        public Task<ConnectedServiceAccount> ExchangeCodeAndSaveAsync(int userId, string code, string redirectUri) => throw new NotSupportedException();
        public Task DisconnectAsync(int userId) => throw new NotSupportedException();
        public Task<IReadOnlyList<PlatformPlaylistDto>> GetPlaylistsAsync(int userId) => throw new NotSupportedException();
        public Task<IReadOnlyList<PlatformSongDto>> GetPlaylistSongsAsync(int userId, string playlistId) => throw new NotSupportedException();
        public Task<Guid> SyncPlaylistAsync(PlatformAccountContext account, string playlistId, CancellationToken cancellationToken)
            => throw new InvalidOperationException("An outbound job must never import.");
        public bool TryValidatePlaylistId(string playlistId, out string? error) { error = null; return true; }
    }
}
