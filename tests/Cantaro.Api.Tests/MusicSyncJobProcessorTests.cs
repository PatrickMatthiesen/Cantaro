using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class MusicSyncJobProcessorTests
{
    [PostgresTheory]
    [InlineData("original", "replacement", false)]
    [InlineData(null, "replacement", false)]
    [InlineData("original", "original", true)]
    public async Task InboundJob_UsesOriginalAccountOrStopsWithoutConsumingPlaylist(
        string? selectedExternalId, string currentExternalId, bool succeeds)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        var db = schema.Db;
        db.Users.Add(TestUserFactory.Create(704, "account-pinning@example.test"));
        var account = new ConnectedServiceAccount { UserId = 704, Service = "youtube", ExternalAccountId = currentExternalId };
        db.ConnectedServiceAccounts.Add(account);
        await db.SaveChangesAsync();
        var job = new MusicSyncJob
        {
            Id = Guid.NewGuid(), UserId = 704, ConnectedServiceAccountId = account.Id, Service = "youtube",
            Status = MusicSyncJobStatuses.Queued, PlaylistCount = 1, SongCount = 1,
            PlaylistsJson = JsonSerializer.Serialize(new[] { new MusicSyncJobPlaylist("selected", "Selected", 1,
                ExternalAccountId: selectedExternalId) }), CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        db.MusicSyncJobs.Add(job);
        await db.SaveChangesAsync();
        var platform = new StubPlatformService("youtube");
        var processor = new MusicSyncJobProcessor(db, new PlatformRegistry([platform]), new MusicSyncThrottleService(),
            NullLogger<MusicSyncJobProcessor>.Instance, new OutboundPlaylistSyncService(db, []));
        await processor.ProcessNextAsync(default);
        var saved = await db.MusicSyncJobs.AsNoTracking().SingleAsync();
        Assert.Equal(succeeds ? 1 : 0, saved.ProcessedPlaylistCount);
        Assert.Equal(succeeds ? MusicSyncJobStatuses.Completed : MusicSyncJobStatuses.Failed, saved.Status);
        Assert.Equal(job.PlaylistsJson, saved.PlaylistsJson);
        Assert.Equal(succeeds ? new[] { "original" } : Array.Empty<string?>(), platform.ExpectedAccountIds);
        Assert.Equal(succeeds ? 1 : 0, platform.SyncedPlaylistIds.Count);
    }

    [PostgresFact]
    public async Task BlockedAccount_DoesNotStarveHealthyJobAndResumesWithProgressPreserved()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        var db = schema.Db;
        db.Users.Add(TestUserFactory.Create(702, "queue@example.test"));
        var account = new ConnectedServiceAccount { UserId = 702, Service = "test", ExternalAccountId = "same",
            ConnectionState = "reconnect_required", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.ConnectedServiceAccounts.Add(account);
        await db.SaveChangesAsync();
        MusicSyncJob Job(int? accountId, string id) => new()
        {
            Id = Guid.NewGuid(), UserId = 702, ConnectedServiceAccountId = accountId, Service = "test",
            Status = MusicSyncJobStatuses.Queued, PlaylistsJson = JsonSerializer.Serialize(new[] { new MusicSyncJobPlaylist(id, id, 1) }),
            PlaylistCount = 1, SongCount = 1, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        var blocked = Job(account.Id, "blocked");
        db.Users.Add(TestUserFactory.Create(703, "healthy@example.test"));
        var healthy = Job(null, "healthy");
        healthy.UserId = 703;
        // The stub requires a binding but does not read it. A second connected account provides one.
        var second = new ConnectedServiceAccount { UserId = 703, Service = "other", ExternalAccountId = "healthy",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.ConnectedServiceAccounts.Add(second);
        await db.SaveChangesAsync();
        healthy.ConnectedServiceAccountId = second.Id;
        db.MusicSyncJobs.AddRange(blocked, healthy);
        await db.SaveChangesAsync();
        var platform = new StubPlatformService();
        var processor = new MusicSyncJobProcessor(db, new PlatformRegistry([platform]), new MusicSyncThrottleService(),
            NullLogger<MusicSyncJobProcessor>.Instance, new OutboundPlaylistSyncService(db, []));
        Assert.True(await processor.ProcessNextAsync(default));
        Assert.Equal(new[] { "healthy" }, platform.SyncedPlaylistIds);
        Assert.False(await processor.ProcessNextAsync(default));
        var pending = await db.MusicSyncJobs.AsNoTracking().SingleAsync(item => item.Id == blocked.Id);
        Assert.Equal(MusicSyncJobStatuses.Queued, pending.Status);
        Assert.Equal(0, pending.ProcessedPlaylistCount);
        Assert.Equal(blocked.PlaylistsJson, pending.PlaylistsJson);
        await db.ConnectedServiceAccounts.Where(item => item.Id == account.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.ConnectionState, "connected"));
        Assert.True(await processor.ProcessNextAsync(default));
        Assert.Equal(new[] { "healthy", "blocked" }, platform.SyncedPlaylistIds);
    }

    [Fact]
    public async Task ProcessNextAsync_PersistsProgressAndCompletion()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new ApplicationDbContext(options);
        var playlists = new[]
        {
            new MusicSyncJobPlaylist("one", "First", 3),
            new MusicSyncJobPlaylist("two", "Second", 5)
        };
        var job = new MusicSyncJob
        {
            Id = Guid.NewGuid(),
            UserId = 42,
            ConnectedServiceAccountId = 99,
            Service = "test",
            PlaylistsJson = JsonSerializer.Serialize(playlists),
            Status = MusicSyncJobStatuses.Queued,
            PlaylistCount = playlists.Length,
            SongCount = 8,
            EstimatedNewSongCount = 8,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.MusicSyncJobs.Add(job);
        await dbContext.SaveChangesAsync();

        var platform = new StubPlatformService();
        var throttle = new MusicSyncThrottleService();
        var processor = new MusicSyncJobProcessor(
            dbContext,
            new PlatformRegistry([platform]),
            throttle,
            NullLogger<MusicSyncJobProcessor>.Instance, new OutboundPlaylistSyncService(dbContext, []));

        Assert.True(await processor.ProcessNextAsync(CancellationToken.None));

        var persisted = await dbContext.MusicSyncJobs.SingleAsync();
        Assert.Equal(MusicSyncJobStatuses.Completed, persisted.Status);
        Assert.Equal(2, persisted.ProcessedPlaylistCount);
        Assert.Equal(8, persisted.ProcessedSongCount);
        Assert.Equal(2, persisted.SuccessCount);
        Assert.Equal(0, persisted.FailureCount);
        Assert.NotNull(persisted.CompletedAt);
        Assert.Equal(["one", "two"], platform.SyncedPlaylistIds);
        Assert.Equal(8, throttle.GetStatus(42, "test", DateTimeOffset.UtcNow).Usage);
    }

    [Fact]
    public async Task ProcessNextAsync_CompletesWhileImportedTrackStillAwaitsMatching()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new ApplicationDbContext(options);
        dbContext.MusicSyncJobs.Add(new MusicSyncJob
        {
            Id = Guid.NewGuid(),
            UserId = 42,
            ConnectedServiceAccountId = 99,
            Service = "test",
            PlaylistsJson = JsonSerializer.Serialize(
                new[] { new MusicSyncJobPlaylist("one", "First", 1) }),
            Status = MusicSyncJobStatuses.Queued,
            PlaylistCount = 1,
            SongCount = 1,
            EstimatedNewSongCount = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var platform = new PendingMatchPlatformService(dbContext);
        var processor = new MusicSyncJobProcessor(
            dbContext,
            new PlatformRegistry([platform]),
            new MusicSyncThrottleService(),
            NullLogger<MusicSyncJobProcessor>.Instance, new OutboundPlaylistSyncService(dbContext, []));

        Assert.True(await processor.ProcessNextAsync(CancellationToken.None));

        var completedJob = await dbContext.MusicSyncJobs.AsNoTracking().SingleAsync();
        var observation = await dbContext.TrackObservations.AsNoTracking().SingleAsync();
        var queueItem = await dbContext.TrackMatchQueueItems.AsNoTracking().SingleAsync();
        Assert.Equal(MusicSyncJobStatuses.Completed, completedJob.Status);
        Assert.Equal(TrackMatchingStatuses.Pending, observation.MatchStatus);
        Assert.Equal(observation.Id, queueItem.TrackObservationId);
    }

    [Fact]
    public async Task ProcessNextAsync_PersistsSongProgressWhilePlaylistIsStillRunning()
    {
        var databaseName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        await using var dbContext = new ApplicationDbContext(options);
        dbContext.MusicSyncJobs.Add(new MusicSyncJob
        {
            Id = Guid.NewGuid(),
            UserId = 42,
            ConnectedServiceAccountId = 99,
            Service = "test",
            PlaylistsJson = JsonSerializer.Serialize(
                new[] { new MusicSyncJobPlaylist("one", "First", 3) }),
            Status = MusicSyncJobStatuses.Queued,
            PlaylistCount = 1,
            SongCount = 3,
            EstimatedNewSongCount = 3,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var platform = new BlockingProgressPlatformService();
        var processor = new MusicSyncJobProcessor(
            dbContext,
            new PlatformRegistry([platform]),
            new MusicSyncThrottleService(),
            NullLogger<MusicSyncJobProcessor>.Instance, new OutboundPlaylistSyncService(dbContext, []));

        var processing = processor.ProcessNextAsync(CancellationToken.None);
        await platform.ProgressPersisted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await using (var observer = new ApplicationDbContext(options))
        {
            var running = await observer.MusicSyncJobs.AsNoTracking().SingleAsync();
            Assert.Equal(MusicSyncJobStatuses.Running, running.Status);
            Assert.Equal(0, running.ProcessedPlaylistCount);
            Assert.Equal(2, running.ProcessedSongCount);
            Assert.Equal("First", running.CurrentPlaylistName);
            Assert.Equal("Currently matching", running.CurrentSongName);
        }

        platform.AllowCompletion.TrySetResult();
        Assert.True(await processing);
        var completed = await dbContext.MusicSyncJobs.SingleAsync();
        Assert.Equal(3, completed.ProcessedSongCount);
        Assert.Null(completed.CurrentPlaylistName);
        Assert.Null(completed.CurrentSongName);
    }

    [Fact]
    public void ThrottleService_ExpiresOldUsageAndReportsRemainingCapacity()
    {
        var throttle = new MusicSyncThrottleService();
        var now = DateTimeOffset.UtcNow;

        throttle.AddUsage(7, "youtube", 125, now);

        var current = throttle.GetStatus(7, "youtube", now);
        Assert.Equal(125, current.Usage);
        Assert.Equal(MusicSyncThrottleService.SongSyncLimitPerWindow - 125, current.Remaining);
        Assert.Equal(0, throttle.GetStatus(7, "youtube", now.AddMinutes(11)).Usage);
    }

    [Fact]
    public async Task ProcessNextAsync_DiscardsTrackedPlatformChangesAfterFailure()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new ApplicationDbContext(options);
        var playlists = new[]
        {
            new MusicSyncJobPlaylist("fails", "Fails", 1),
            new MusicSyncJobPlaylist("succeeds", "Succeeds", 1)
        };
        dbContext.MusicSyncJobs.Add(new MusicSyncJob
        {
            Id = Guid.NewGuid(),
            UserId = 42,
            ConnectedServiceAccountId = 99,
            Service = "test",
            PlaylistsJson = JsonSerializer.Serialize(playlists),
            Status = MusicSyncJobStatuses.Queued,
            PlaylistCount = playlists.Length,
            SongCount = 2,
            EstimatedNewSongCount = 2,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var platform = new RollbackPoisonPlatformService(dbContext);
        var processor = new MusicSyncJobProcessor(
            dbContext,
            new PlatformRegistry([platform]),
            new MusicSyncThrottleService(),
            NullLogger<MusicSyncJobProcessor>.Instance, new OutboundPlaylistSyncService(dbContext, []));

        Assert.True(await processor.ProcessNextAsync(CancellationToken.None));

        var job = await dbContext.MusicSyncJobs.SingleAsync();
        Assert.Equal(MusicSyncJobStatuses.Failed, job.Status);
        Assert.Equal(1, job.FailureCount);
        Assert.Equal(1, job.SuccessCount);
        Assert.Empty(await dbContext.Playlists.ToListAsync());
    }

    [Fact]
    public async Task ActiveJobIndex_RejectsConcurrentJobsForSameUserAndService()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Users.Add(TestUserFactory.Create(42, "sync-jobs@example.com"));
        var now = DateTimeOffset.UtcNow;
        dbContext.MusicSyncJobs.AddRange(
            CreateQueuedJob(42, "youtube", now),
            CreateQueuedJob(42, "youtube", now.AddMilliseconds(1)));

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    private static MusicSyncJob CreateQueuedJob(int userId, string service, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Service = service,
        PlaylistsJson = "[]",
        Status = MusicSyncJobStatuses.Queued,
        CreatedAt = now,
        UpdatedAt = now
    };

    internal sealed class StubPlatformService(string service = "test") : IPlatformService
    {
        public string PlatformId => service;
        public List<string> SyncedPlaylistIds { get; } = [];
        public List<string?> ExpectedAccountIds { get; } = [];

        public Task<Guid> SyncPlaylistAsync(PlatformAccountContext account, string playlistId, CancellationToken cancellationToken)
        {
            SyncedPlaylistIds.Add(playlistId);
            ExpectedAccountIds.Add(account.ExpectedExternalAccountId);
            return Task.FromResult(Guid.NewGuid());
        }

        public Task<ConnectedServiceAccount?> GetConnectedAccountAsync(int userId) => throw new NotSupportedException();
        public string GetAuthorizationUrl(string redirectUri, string state) => throw new NotSupportedException();
        public Task<ConnectedServiceAccount> ExchangeCodeAndSaveAsync(int userId, string authorizationCode, string redirectUri) => throw new NotSupportedException();
        public Task DisconnectAsync(int userId) => throw new NotSupportedException();
        public Task<IReadOnlyList<PlatformPlaylistDto>> GetPlaylistsAsync(int userId) => throw new NotSupportedException();
        public Task<IReadOnlyList<PlatformSongDto>> GetPlaylistSongsAsync(int userId, string playlistId) => throw new NotSupportedException();
        public bool TryValidatePlaylistId(string playlistId, out string? error)
        {
            error = null;
            return true;
        }
    }

    private sealed class RollbackPoisonPlatformService(ApplicationDbContext dbContext) : IPlatformService
    {
        public string PlatformId => "test";

        public Task<Guid> SyncPlaylistAsync(PlatformAccountContext account, string playlistId, CancellationToken cancellationToken)
        {
            if (playlistId == "fails")
            {
                dbContext.Playlists.Add(new Playlist
                {
                    Id = Guid.NewGuid(),
                    UserId = account.UserId,
                    Name = "Must not be persisted",
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
                throw new InvalidOperationException("Simulated rolled-back platform work");
            }

            return Task.FromResult(Guid.NewGuid());
        }

        public Task<ConnectedServiceAccount?> GetConnectedAccountAsync(int userId) => throw new NotSupportedException();
        public string GetAuthorizationUrl(string redirectUri, string state) => throw new NotSupportedException();
        public Task<ConnectedServiceAccount> ExchangeCodeAndSaveAsync(int userId, string authorizationCode, string redirectUri) => throw new NotSupportedException();
        public Task DisconnectAsync(int userId) => throw new NotSupportedException();
        public Task<IReadOnlyList<PlatformPlaylistDto>> GetPlaylistsAsync(int userId) => throw new NotSupportedException();
        public Task<IReadOnlyList<PlatformSongDto>> GetPlaylistSongsAsync(int userId, string playlistId) => throw new NotSupportedException();
        public bool TryValidatePlaylistId(string playlistId, out string? error)
        {
            error = null;
            return true;
        }
    }

    private sealed class PendingMatchPlatformService(ApplicationDbContext dbContext) : IPlatformService
    {
        public string PlatformId => "test";

        public async Task<Guid> SyncPlaylistAsync(
            PlatformAccountContext account,
            string playlistId,
            CancellationToken cancellationToken)
        {
            var observationId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            dbContext.TrackObservations.Add(new TrackObservation { OwnerUserId = 1,  InputKind = ObservationInputKind.Private,
                Id = observationId,
                SourceType = "youtube",
                ExternalId = "pending-video",
                Title = "Pending song",
                MatchStatus = TrackMatchingStatuses.Pending,
                CreatedAt = now,
                UpdatedAt = now
             });
            dbContext.TrackMatchQueueItems.Add(new TrackMatchQueueItem
            {
                TrackObservationId = observationId,
                NextAttemptAt = now.UtcDateTime
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            return Guid.NewGuid();
        }

        public Task<ConnectedServiceAccount?> GetConnectedAccountAsync(int userId) => throw new NotSupportedException();
        public string GetAuthorizationUrl(string redirectUri, string state) => throw new NotSupportedException();
        public Task<ConnectedServiceAccount> ExchangeCodeAndSaveAsync(int userId, string authorizationCode, string redirectUri) => throw new NotSupportedException();
        public Task DisconnectAsync(int userId) => throw new NotSupportedException();
        public Task<IReadOnlyList<PlatformPlaylistDto>> GetPlaylistsAsync(int userId) => throw new NotSupportedException();
        public Task<IReadOnlyList<PlatformSongDto>> GetPlaylistSongsAsync(int userId, string playlistId) => throw new NotSupportedException();
        public bool TryValidatePlaylistId(string playlistId, out string? error)
        {
            error = null;
            return true;
        }
    }

    private sealed class BlockingProgressPlatformService : IPlatformService
    {
        public string PlatformId => "test";
        public TaskCompletionSource ProgressPersisted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowCompletion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Guid> SyncPlaylistAsync(
            PlatformAccountContext account,
            string playlistId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public async Task<Guid> SyncPlaylistAsync(
            PlatformAccountContext account,
            string playlistId,
            Func<PlatformSyncProgress, CancellationToken, Task> reportProgressAsync,
            CancellationToken cancellationToken)
        {
            await reportProgressAsync(
                new PlatformSyncProgress(2, "Currently matching"),
                cancellationToken);
            ProgressPersisted.TrySetResult();
            await AllowCompletion.Task.WaitAsync(cancellationToken);
            return Guid.NewGuid();
        }

        public Task<ConnectedServiceAccount?> GetConnectedAccountAsync(int userId) => throw new NotSupportedException();
        public string GetAuthorizationUrl(string redirectUri, string state) => throw new NotSupportedException();
        public Task<ConnectedServiceAccount> ExchangeCodeAndSaveAsync(int userId, string authorizationCode, string redirectUri) => throw new NotSupportedException();
        public Task DisconnectAsync(int userId) => throw new NotSupportedException();
        public Task<IReadOnlyList<PlatformPlaylistDto>> GetPlaylistsAsync(int userId) => throw new NotSupportedException();
        public Task<IReadOnlyList<PlatformSongDto>> GetPlaylistSongsAsync(int userId, string playlistId) => throw new NotSupportedException();
        public bool TryValidatePlaylistId(string playlistId, out string? error)
        {
            error = null;
            return true;
        }
    }
}
