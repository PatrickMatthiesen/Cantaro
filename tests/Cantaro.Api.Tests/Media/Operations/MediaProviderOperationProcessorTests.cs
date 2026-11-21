using System.Data.Common;
using System.Net;
using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantaro.Api.Tests;

public class MediaProviderOperationProcessorTests
{
    [Theory]
    [InlineData("complete")]
    [InlineData("retry")]
    [InlineData("fail")]
    [InlineData("cancel")]
    [InlineData("before-dispatch")]
    public async Task ReclaimedSqliteLease_FencesOriginalWorker(string staleOutcome)
    {
        await using var db = await OpenLeaseTestSqliteAsync();
        await AssertReclaimedLeaseAsync(db, staleOutcome);
    }

    [PostgresTheory]
    [InlineData("complete")]
    [InlineData("retry")]
    [InlineData("fail")]
    [InlineData("cancel")]
    [InlineData("before-dispatch")]
    public async Task ReclaimedPostgresLease_FencesOriginalWorker(string staleOutcome)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await AssertReclaimedLeaseAsync(schema.Db, staleOutcome);
    }

    private static async Task AssertReclaimedLeaseAsync(ApplicationDbContext db, string staleOutcome)
    {
        var (entry, binding) = await SeedMediaEntryAsync(db, 414, "lease@example.test");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var staleCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var originalEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reclaimedEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOriginal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReclaimed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var beforeDispatch = staleOutcome == "before-dispatch";
        var originalPaused = false;
        var remoteProgress = 9;
        var baseline = await db.MediaLibraryProviderBindings.AsNoTracking().SingleAsync(timeout.Token);
        var sharedProvider = new FakeMediaProvider("anilist")
        {
            ProgressRemoteUpdateAt = beforeDispatch ? baseline.LastRemoteUpdateAt : null,
            BeforeProgressUpdate = async (request, token) =>
            {
                if (!beforeDispatch && !originalPaused)
                {
                    originalPaused = true;
                    originalEntered.SetResult();
                    await releaseOriginal.Task.WaitAsync(token);
                    if (staleOutcome == "retry")
                    {
                        throw new InvalidOperationException("Old provider attempt failed.");
                    }
                    if (staleOutcome == "fail")
                    {
                        throw new PlatformApiException("platform_sync_conflict", "Old provider attempt failed.", 409);
                    }
                }
                else if (request.ProgressEpisodes == 10)
                {
                    reclaimedEntered.SetResult();
                    await releaseReclaimed.Task.WaitAsync(token);
                }

                remoteProgress = request.ProgressEpisodes!.Value;
            }
        };

        var options = (DbContextOptions<ApplicationDbContext>)db.GetService<IDbContextOptions>();
        var originalOptions = new DbContextOptionsBuilder<ApplicationDbContext>(options);
        if (beforeDispatch)
        {
            // Stop before the remote version query, after the original worker loaded its claim.
            originalOptions.AddInterceptors(new BeforeReaderInterceptor(
                sql => sql.Contains("FROM \"MediaLibraryProviderBindings\"", StringComparison.Ordinal)
                    && sql[..sql.IndexOf("FROM", StringComparison.Ordinal)].Contains("LastRemoteUpdateAt", StringComparison.Ordinal),
                async token =>
                {
                    originalEntered.SetResult();
                    await releaseOriginal.Task.WaitAsync(token);
                }));
        }
        var restartedClaimed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var restartedOptions = new DbContextOptionsBuilder<ApplicationDbContext>(options)
            .AddInterceptors(new BeforeReaderInterceptor(IsClaimedOperationLoad, _ =>
            {
                restartedClaimed.TrySetResult();
                return Task.CompletedTask;
            }));
        await using var originalDb = new ApplicationDbContext(originalOptions.Options);
        await using var restartedDb = new ApplicationDbContext(restartedOptions.Options);
        var original = CreateProcessor(originalDb, sharedProvider);
        var restarted = CreateProcessor(restartedDb, sharedProvider);
        var operation = await CreateProcessor(db, sharedProvider).EnqueueAutoProgressAsync(entry.UserId, binding,
            new AutoProgressUpdatePayload
            {
                ProviderMediaId = "140960", ProgressEpisodes = 10,
                LastKnownRemoteUpdateAt = binding.LastRemoteUpdateAt
            }, timeout.Token);
        await CreateProcessor(db, sharedProvider).EnqueueAutoProgressAsync(entry.UserId, binding,
            new AutoProgressUpdatePayload { ProviderMediaId = "140960", ProgressEpisodes = 11 }, timeout.Token);
        var firstRun = original.ProcessOperationAsync(operation.Id, staleCancellation.Token);
        Task<int>? restartedRun = null;
        try
        {
            await originalEntered.Task.WaitAsync(timeout.Token);
            Assert.Equal(0, await restarted.ProcessDueOperationsAsync(timeout.Token));
            var expired = DateTimeOffset.UtcNow - MediaProviderOperationProcessor.ProcessingLeaseTimeout - TimeSpan.FromMinutes(1);
            await db.MediaProviderOperations.Where(row => row.Id == operation.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.UpdatedAt, expired), timeout.Token);

            restartedRun = restarted.ProcessDueOperationsAsync(timeout.Token);
            await restartedClaimed.Task.WaitAsync(timeout.Token);
            var reclaimed = await db.MediaProviderOperations.AsNoTracking().SingleAsync(row => row.Id == operation.Id, timeout.Token);
            Assert.Equal(2, reclaimed.AttemptCount);

            if (beforeDispatch)
            {
                await reclaimedEntered.Task.WaitAsync(timeout.Token);
                releaseReclaimed.SetResult();
                Assert.Equal(2, await restartedRun);
                Assert.Equal(11, remoteProgress);
                releaseOriginal.SetResult();
                Assert.Equal(MediaProviderOperationExecutionOutcome.Queued, (await firstRun).Outcome);
            }
            else
            {
                Assert.False(reclaimedEntered.Task.IsCompleted);
                if (staleOutcome == "cancel")
                {
                    await staleCancellation.CancelAsync();
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstRun);
                }
                else
                {
                    releaseOriginal.SetResult();
                    Assert.Equal(MediaProviderOperationExecutionOutcome.Queued, (await firstRun).Outcome);
                }
                await reclaimedEntered.Task.WaitAsync(timeout.Token);
                await AssertClaimAndBindingUnchangedAsync(reclaimed);
            }
        }
        finally
        {
            releaseOriginal.TrySetResult();
            releaseReclaimed.TrySetResult();
            if (restartedRun is not null)
            {
                Assert.Equal(2, await restartedRun);
            }
            if (staleOutcome != "cancel")
            {
                await firstRun;
            }
        }

        Assert.Equal(beforeDispatch ? 2 : 3, sharedProvider.ProgressUpdateCallCount);
        Assert.Equal(11, remoteProgress);
        Assert.Equal(11, await db.MediaLibraryEntries.Select(row => row.ProgressEpisodes).SingleAsync(timeout.Token));
        Assert.Empty(await db.MediaProviderOperations.ToListAsync(timeout.Token));

        async Task AssertClaimAndBindingUnchangedAsync(MediaProviderOperation reclaimed)
        {
            var stillOwned = await db.MediaProviderOperations.AsNoTracking().SingleAsync(row => row.Id == operation.Id, timeout.Token);
            Assert.Equal(MediaProviderOperationStatuses.Processing, stillOwned.Status);
            Assert.Equal(reclaimed.AttemptCount, stillOwned.AttemptCount);
            Assert.Equal(reclaimed.UpdatedAt, stillOwned.UpdatedAt);
            Assert.Equal(reclaimed.PayloadJson, stillOwned.PayloadJson);
            Assert.Null(stillOwned.LastError);
            var unchanged = await db.MediaLibraryProviderBindings.AsNoTracking().SingleAsync(timeout.Token);
            Assert.Equal(baseline.LastSyncedAt, unchanged.LastSyncedAt);
            Assert.Equal(baseline.LastRemoteUpdateAt, unchanged.LastRemoteUpdateAt);
            Assert.Equal(baseline.UpdatedAt, unchanged.UpdatedAt);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancelledSqliteLease_ReleasesBinding(bool duringLoad)
    {
        await using var db = await OpenLeaseTestSqliteAsync();
        await AssertCancelledLeaseAsync(db, duringLoad);
    }

    [PostgresTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancelledPostgresLease_ReleasesBinding(bool duringLoad)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await AssertCancelledLeaseAsync(schema.Db, duringLoad);
    }

    private static async Task AssertCancelledLeaseAsync(ApplicationDbContext db, bool duringLoad)
    {
        var (entry, binding) = await SeedMediaEntryAsync(db, 415, "cancel-lease@example.test");
        using var cancelled = new CancellationTokenSource();
        var healthy = new FakeMediaProvider("anilist");
        var queue = CreateProcessor(db, healthy);
        var operation = await queue.EnqueueProgressUpdateAsync(entry.UserId, binding,
            new MediaProgressUpdateRequest { ProviderMediaId = "140960", ProgressEpisodes = 11 }, default);
        await queue.EnqueueProgressUpdateAsync(entry.UserId, binding,
            new MediaProgressUpdateRequest { ProviderMediaId = "140960", ProgressEpisodes = 12 }, default);

        var cancellingProvider = new FakeMediaProvider("anilist")
        {
            BeforeProgressUpdate = async (_, token) =>
            {
                await cancelled.CancelAsync();
                token.ThrowIfCancellationRequested();
            }
        };
        var options = new DbContextOptionsBuilder<ApplicationDbContext>((DbContextOptions<ApplicationDbContext>)db.GetService<IDbContextOptions>());
        if (duringLoad)
        {
            options.AddInterceptors(new CancelOperationLoadInterceptor(cancelled));
        }
        await using var workerDb = new ApplicationDbContext(options.Options);
        var worker = CreateProcessor(workerDb, cancellingProvider);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => worker.ProcessOperationAsync(operation.Id, cancelled.Token));
        Assert.Equal(duringLoad ? 0 : 1, cancellingProvider.ProgressUpdateCallCount);
        var released = await db.MediaProviderOperations.AsNoTracking().SingleAsync(row => row.Id == operation.Id);
        Assert.Equal(MediaProviderOperationStatuses.Retrying, released.Status);
        Assert.Equal(0, released.AttemptCount);
        Assert.Null(released.NextAttemptAt);

        Assert.Equal(2, await queue.ProcessDueOperationsAsync(default));
        Assert.Equal(2, healthy.ProgressUpdateCallCount);
        Assert.Empty(await db.MediaProviderOperations.ToListAsync());
    }

    private static MediaProviderOperationProcessor CreateProcessor(ApplicationDbContext db, IMediaProvider provider) =>
        new(db, new MediaProviderRegistry([provider]), NullLogger<MediaProviderOperationProcessor>.Instance);

    private static async Task<ApplicationDbContext> OpenLeaseTestSqliteAsync()
    {
        var path = $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(path).Options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private static bool IsClaimedOperationLoad(string sql) =>
        sql.Contains("SELECT", StringComparison.Ordinal)
        && sql.Contains("JOIN \"MediaLibraryEntries\"", StringComparison.Ordinal)
        && sql.Contains("FROM \"MediaProviderOperations\"", StringComparison.Ordinal);

    private sealed class BeforeReaderInterceptor(Func<string, bool> matches, Func<CancellationToken, Task> beforeRead)
        : DbCommandInterceptor
    {
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (matches(command.CommandText))
            {
                await beforeRead(cancellationToken);
            }
            return result;
        }
    }

    private sealed class CancelOperationLoadInterceptor(CancellationTokenSource cancelled) : DbCommandInterceptor
    {
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (IsClaimedOperationLoad(command.CommandText))
            {
                await cancelled.CancelAsync();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return result;
        }
    }

    [PostgresFact]
    public async Task BlockedOperations_RemainQueuedWithoutStarvingHealthyAccountAndResume()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        var db = schema.Db;
        var (blockedEntry, blockedBinding) = await SeedMediaEntryAsync(db, 901, "blocked@example.test");
        var (healthyEntry, healthyBinding) = await SeedMediaEntryAsync(db, 902, "healthy@example.test", "140961");
        var account = await db.ConnectedServiceAccounts.SingleAsync(item => item.UserId == 901);
        account.ConnectionState = "reconnect_required";
        await db.SaveChangesAsync();
        var fake = new FakeMediaProvider("anilist");
        var processor = new MediaProviderOperationProcessor(db, new MediaProviderRegistry([fake]),
            NullLogger<MediaProviderOperationProcessor>.Instance);
        for (var index = 0; index < 12; index++)
            await processor.EnqueueProgressUpdateAsync(blockedEntry.UserId, blockedBinding,
                new MediaProgressUpdateRequest { ProviderMediaId = "140960", ProgressEpisodes = 17 }, default);
        await processor.EnqueueProgressUpdateAsync(healthyEntry.UserId, healthyBinding,
            new MediaProgressUpdateRequest { ProviderMediaId = "140960", ProgressEpisodes = 17 }, default);
        Assert.Equal(1, await processor.ProcessDueOperationsAsync(default));
        Assert.Equal(1, fake.ProgressUpdateCallCount);
        Assert.Equal(0, await processor.ProcessDueOperationsAsync(default));
        var pending = await db.MediaProviderOperations.AsNoTracking().ToListAsync();
        Assert.NotEmpty(pending);
        Assert.All(pending, item => Assert.Equal(0, item.AttemptCount));
        account.ConnectionState = "connected";
        account.TokenVersion++;
        await db.SaveChangesAsync();
        Assert.True(await processor.ProcessDueOperationsAsync(default) > 0);
        Assert.True(fake.ProgressUpdateCallCount > 1);
    }

    [Fact]
    public async Task ProcessOperationAsync_LeavesLocallySavedProgressUnchangedOnSuccessfulProviderWrite()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var (entry, binding) = await SeedMediaEntryAsync(dbContext, 401, "success@example.com");
        var registry = new MediaProviderRegistry([new FakeMediaProvider("anilist")]);
        var logger = new RecordingLogger<MediaProviderOperationProcessor>();
        var processor = new MediaProviderOperationProcessor(dbContext, registry, logger);

        entry.ProgressEpisodes = 17;
        entry.LastMutationSource = MediaMutationSources.UserProgressUpdate;
        entry.LastLocalEditAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync();

        var operation = await processor.EnqueueProgressUpdateAsync(
            entry.UserId,
            binding,
            new MediaProgressUpdateRequest
            {
                ProviderMediaId = "140960",
                ProgressEpisodes = 17,
                LastKnownRemoteUpdateAt = binding.LastRemoteUpdateAt
            },
            CancellationToken.None);

        var processed = await processor.ProcessOperationAsync(operation.Id, CancellationToken.None);
        var persistedEntry = await dbContext.MediaLibraryEntries.SingleAsync();

        Assert.Equal(MediaProviderOperationExecutionOutcome.Succeeded, processed.Outcome);
        Assert.Equal(17, persistedEntry.ProgressEpisodes);
        Assert.Equal(MediaMutationSources.UserProgressUpdate, persistedEntry.LastMutationSource);
        Assert.NotNull(persistedEntry.LastLocalEditAt);
        Assert.Equal(0, await dbContext.MediaProviderOperations.CountAsync());
        Assert.Contains(
            logger.Messages,
            message => message.Level == LogLevel.Information
                && message.Text.Contains("Attempting provider progress update", StringComparison.Ordinal)
                && message.Text.Contains("episodes=17", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProcessOperationAsync_LeavesLocallySavedScoreUnchangedOnSuccessfulProviderWrite()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var (entry, binding) = await SeedMediaEntryAsync(dbContext, 413, "score-success@example.com");
        var processor = new MediaProviderOperationProcessor(
            dbContext,
            new MediaProviderRegistry([new FakeMediaProvider("anilist")]),
            NullLogger<MediaProviderOperationProcessor>.Instance);

        entry.Score = 82.5m;
        entry.LastMutationSource = MediaMutationSources.UserScoreUpdate;
        entry.LastLocalEditAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync();

        var operation = await processor.EnqueueScoreUpdateAsync(
            entry.UserId,
            binding,
            new MediaScoreUpdateRequest
            {
                ProviderMediaId = "140960",
                Score = 82.5m,
                LastKnownRemoteUpdateAt = binding.LastRemoteUpdateAt
            },
            CancellationToken.None);

        var processed = await processor.ProcessOperationAsync(operation.Id, CancellationToken.None);

        Assert.Equal(MediaProviderOperationExecutionOutcome.Succeeded, processed.Outcome);
        Assert.Equal(82.5m, (await dbContext.MediaLibraryEntries.SingleAsync()).Score);
        Assert.Equal(MediaMutationSources.UserScoreUpdate, (await dbContext.MediaLibraryEntries.SingleAsync()).LastMutationSource);
        Assert.Equal(0, await dbContext.MediaProviderOperations.CountAsync());
    }

    [Fact]
    public async Task ProcessOperationAsync_StatusWrite_RecordsAppliedStatusWithoutChangingLocalStatus()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var (entry, binding) = await SeedMediaEntryAsync(dbContext, 415, "status-normalization@example.com");
        var provider = new FakeMediaProvider("anilist", appliedStatus: MediaLibraryStatuses.Completed);
        var processor = new MediaProviderOperationProcessor(
            dbContext,
            new MediaProviderRegistry([provider]),
            NullLogger<MediaProviderOperationProcessor>.Instance);

        var operation = await processor.EnqueueStatusUpdateAsync(
            entry.UserId,
            binding,
            new MediaStatusUpdateRequest
            {
                ProviderMediaId = "140960",
                Status = MediaLibraryStatuses.Current,
                LastKnownRemoteUpdateAt = binding.LastRemoteUpdateAt
            },
            CancellationToken.None);

        var processed = await processor.ProcessOperationAsync(operation.Id, CancellationToken.None);

        Assert.Equal(MediaProviderOperationExecutionOutcome.Succeeded, processed.Outcome);
        Assert.Equal(MediaLibraryStatuses.Current, (await dbContext.MediaLibraryEntries.SingleAsync()).Status);
        var persistedBinding = await dbContext.MediaLibraryProviderBindings.SingleAsync();
        Assert.Equal(MediaLibraryStatuses.Current, persistedBinding.LastRequestedStatus);
        Assert.Equal(MediaLibraryStatuses.Completed, persistedBinding.LastAppliedStatus);
    }

    [Fact]
    public async Task ProcessOperationAsync_SkipsOlderStatusWriteAfterNewerLocalEdit()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var (entry, binding) = await SeedMediaEntryAsync(dbContext, 416, "stale-status@example.com");
        var provider = new FakeMediaProvider("anilist");
        var processor = new MediaProviderOperationProcessor(dbContext,
            new MediaProviderRegistry([provider]), NullLogger<MediaProviderOperationProcessor>.Instance);
        var operation = await processor.EnqueueStatusUpdateAsync(entry.UserId, binding,
            new MediaStatusUpdateRequest
            {
                ProviderMediaId = "140960",
                Status = MediaLibraryStatuses.Completed,
                LastKnownRemoteUpdateAt = binding.LastRemoteUpdateAt
            }, CancellationToken.None);

        entry.Status = MediaLibraryStatuses.Paused;
        entry.LastLocalEditAt = operation.CreatedAt.AddSeconds(1);
        await dbContext.SaveChangesAsync();

        var result = await processor.ProcessOperationAsync(operation.Id, CancellationToken.None);

        Assert.Equal(MediaProviderOperationExecutionOutcome.Succeeded, result.Outcome);
        Assert.Equal(0, provider.StatusUpdateCallCount);
        Assert.Equal(MediaLibraryStatuses.Paused, (await dbContext.MediaLibraryEntries.SingleAsync()).Status);
        Assert.Empty(await dbContext.MediaProviderOperations.ToListAsync());
    }

    [Fact]
    public async Task ProcessDueOperationsAsync_ReloadsLocalStatusBetweenSqliteOperations()
    {
        await using var db = await OpenLeaseTestSqliteAsync();
        await AssertBatchReloadsLocalStatusAsync(db);
    }

    [PostgresFact]
    public async Task ProcessDueOperationsAsync_ReloadsLocalStatusBetweenPostgresOperations()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await AssertBatchReloadsLocalStatusAsync(schema.Db);
    }

    private static async Task AssertBatchReloadsLocalStatusAsync(ApplicationDbContext db)
    {
        var (entry, binding) = await SeedMediaEntryAsync(db, 418, "batch-status@example.com");
        var options = (DbContextOptions<ApplicationDbContext>)db.GetService<IDbContextOptions>();
        await using var workerDb = new ApplicationDbContext(options);
        var updatedAt = DateTimeOffset.UtcNow;
        var provider = new FakeMediaProvider("anilist", appliedStatus: MediaLibraryStatuses.Current)
        {
            BeforeProgressUpdate = async (_, cancellationToken) =>
            {
                await using var editor = new ApplicationDbContext(options);
                var edited = await editor.MediaLibraryEntries.SingleAsync(cancellationToken);
                edited.Status = MediaLibraryStatuses.Current;
                edited.ProgressEpisodes = 3;
                edited.LastLocalEditAt = updatedAt.AddSeconds(2);
                await editor.SaveChangesAsync(cancellationToken);
            }
        };
        var queue = CreateProcessor(db, provider);
        var progress = await queue.EnqueueProgressUpdateAsync(entry.UserId, binding,
            new() { ProviderMediaId = "140960", ProgressEpisodes = 10 }, default);
        var status = await queue.EnqueueStatusUpdateAsync(entry.UserId, binding,
            new() { ProviderMediaId = "140960", Status = MediaLibraryStatuses.Current }, default);
        progress.CreatedAt = updatedAt.AddSeconds(-2);
        status.CreatedAt = updatedAt.AddSeconds(-1);
        entry.Status = MediaLibraryStatuses.Completed;
        entry.LastLocalEditAt = updatedAt;
        await db.SaveChangesAsync();

        Assert.Equal(2, await CreateProcessor(workerDb, provider).ProcessDueOperationsAsync(default));

        Assert.Equal(1, provider.ProgressUpdateCallCount);
        Assert.Equal(1, provider.StatusUpdateCallCount);
        Assert.Equal(MediaLibraryStatuses.Current, await db.MediaLibraryProviderBindings
            .Select(row => row.LastRequestedStatus).SingleAsync());
        Assert.Equal(3, await db.MediaLibraryEntries.Select(row => row.ProgressEpisodes).SingleAsync());
        Assert.Empty(db.MediaProviderOperations);
    }

    [Theory]
    [InlineData("confirmed")]
    [InlineData("unconfirmed")]
    [InlineData("newer-remote")]
    [InlineData("account-changed")]
    public async Task ProcessOperationAsync_SyncLibraryState_RequiresConfirmedFreshBatch(string scenario)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var (entry, binding) = await SeedMediaEntryAsync(dbContext, 414, "whole-state@example.com");
        var originalLocalEditAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var originalEntryUpdatedAt = originalLocalEditAt;
        entry.Score = 82.5m;
        entry.LastLocalEditAt = originalLocalEditAt;
        entry.LastMutationSource = MediaMutationSources.ProviderImport;
        entry.UpdatedAt = originalEntryUpdatedAt;
        await dbContext.SaveChangesAsync();

        var connectedAccount = await dbContext.ConnectedServiceAccounts.AsNoTracking().SingleAsync();
        if (scenario == "account-changed")
        {
            connectedAccount.ExternalAccountId = "different";
        }
        var provider = new FakeMediaProvider("anilist", appliedStatus: MediaLibraryStatuses.Completed)
        {
            ConnectedAccount = connectedAccount
        };
        var processor = new MediaProviderOperationProcessor(
            dbContext,
            new MediaProviderRegistry([provider]),
            NullLogger<MediaProviderOperationProcessor>.Instance);
        var request = new MediaLibraryStateSyncRequest
        {
            ProviderMediaId = "140960",
            Status = entry.Status,
            Score = entry.Score,
            ProgressEpisodes = entry.ProgressEpisodes,
            LastKnownRemoteUpdateAt = binding.LastRemoteUpdateAt
        };

        var operation = await processor.EnqueueLibraryStateSyncAsync(
            entry.UserId,
            binding,
            request,
            CancellationToken.None);
        if (scenario != "unconfirmed")
        {
            operation.BatchId = Guid.NewGuid();
        }
        if (scenario == "newer-remote")
        {
            binding.LastRemoteUpdateAt = originalLocalEditAt.AddSeconds(1);
        }
        await dbContext.SaveChangesAsync();

        var processed = await processor.ProcessOperationAsync(operation.Id, CancellationToken.None);

        if (scenario is "newer-remote" or "account-changed")
        {
            Assert.Equal(0, provider.LibraryStateSyncCallCount);
            Assert.Equal(MediaProviderOperationStatuses.Failed, (await dbContext.MediaProviderOperations.SingleAsync()).Status);
            return;
        }

        Assert.Equal(MediaProviderOperationExecutionOutcome.Succeeded, processed.Outcome);
        Assert.Equal(scenario == "confirmed" ? 1 : 0, provider.LibraryStateSyncCallCount);
        Assert.Equal(0, await dbContext.MediaProviderOperations.CountAsync());
        if (scenario == "unconfirmed")
        {
            return;
        }

        var persistedEntry = await dbContext.MediaLibraryEntries.SingleAsync();
        var persistedBinding = await dbContext.MediaLibraryProviderBindings.SingleAsync();
        Assert.Equal(request.ProviderMediaId, provider.LastLibraryStateSyncRequest!.ProviderMediaId);
        Assert.Equal(originalLocalEditAt, persistedEntry.LastLocalEditAt);
        Assert.Equal(originalEntryUpdatedAt, persistedEntry.UpdatedAt);
        Assert.Equal(MediaMutationSources.ProviderImport, persistedEntry.LastMutationSource);
        Assert.Equal(82.5m, persistedEntry.Score);
        Assert.Equal(MediaLibraryStatuses.Current, persistedEntry.Status);
        Assert.Equal(MediaLibraryStatuses.Current, persistedBinding.LastRequestedStatus);
        Assert.Equal(MediaLibraryStatuses.Completed, persistedBinding.LastAppliedStatus);
        Assert.NotNull(persistedBinding.LastSyncedAt);
        Assert.NotNull(persistedBinding.LastRemoteUpdateAt);
        Assert.NotNull(request.LastKnownRemoteUpdateAt);
        Assert.True(persistedBinding.LastRemoteUpdateAt.Value > request.LastKnownRemoteUpdateAt.Value);
    }

    [Fact]
    public async Task SyncLibraryState_RechecksLocalEditAfterAccountPreflightOnSqlite()
    {
        await using var db = await OpenLeaseTestSqliteAsync();
        await AssertSyncSkipsLocalEditDuringAccountPreflightAsync(db);
    }

    [PostgresFact]
    public async Task SyncLibraryState_RechecksLocalEditAfterAccountPreflightOnPostgres()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await AssertSyncSkipsLocalEditDuringAccountPreflightAsync(schema.Db);
    }

    private static async Task AssertSyncSkipsLocalEditDuringAccountPreflightAsync(ApplicationDbContext db)
    {
        var (entry, binding) = await SeedMediaEntryAsync(db, 414, "sync-preflight@example.test");
        entry.LastLocalEditAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
        var account = await db.ConnectedServiceAccounts.AsNoTracking().SingleAsync();
        var enteredPreflight = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePreflight = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeMediaProvider("anilist")
        {
            ConnectedAccount = account,
            BeforeGetConnectedAccount = async token =>
            {
                enteredPreflight.SetResult();
                await releasePreflight.Task.WaitAsync(token);
            }
        };
        var operation = await CreateProcessor(db, provider).EnqueueLibraryStateSyncAsync(entry.UserId, binding, new()
        {
            ProviderMediaId = "140960",
            Status = entry.Status,
            ProgressEpisodes = entry.ProgressEpisodes,
            LastKnownRemoteUpdateAt = binding.LastRemoteUpdateAt
        }, default);
        operation.BatchId = Guid.NewGuid();
        await db.SaveChangesAsync();

        var options = (DbContextOptions<ApplicationDbContext>)db.GetService<IDbContextOptions>();
        await using var workerDb = new ApplicationDbContext(options);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var processing = CreateProcessor(workerDb, provider).ProcessOperationAsync(operation.Id, timeout.Token);
        MediaProviderOperationExecutionResult result;
        try
        {
            await enteredPreflight.Task.WaitAsync(timeout.Token);
            entry.ProgressEpisodes = 11;
            entry.LastLocalEditAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(timeout.Token);
        }
        finally
        {
            releasePreflight.TrySetResult();
            result = await processing;
        }

        Assert.Equal(MediaProviderOperationExecutionOutcome.Succeeded, result.Outcome);
        Assert.Equal(0, provider.LibraryStateSyncCallCount);
        Assert.Empty(await db.MediaProviderOperations.AsNoTracking().ToListAsync());
        Assert.Equal(11, await db.MediaLibraryEntries.Select(row => row.ProgressEpisodes).SingleAsync());
    }

    [Fact]
    public async Task ProcessOperationAsync_ImportFanOut_PreservesProviderImportProvenance()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var (entry, binding) = await SeedMediaEntryAsync(db, 416, "fanout@example.com");
        entry.Status = MediaLibraryStatuses.Completed;
        entry.LastMutationSource = MediaMutationSources.ProviderImport;
        await db.SaveChangesAsync();
        var operation = CreateImportFanOutStatus(binding, MediaLibraryStatuses.Completed);
        db.MediaProviderOperations.Add(operation);
        await db.SaveChangesAsync();

        var provider = new FakeMediaProvider("anilist", appliedStatus: MediaLibraryStatuses.Current);
        var processor = new MediaProviderOperationProcessor(db,
            new MediaProviderRegistry([provider]), NullLogger<MediaProviderOperationProcessor>.Instance);
        var result = await processor.ProcessOperationAsync(operation.Id, CancellationToken.None);

        Assert.Equal(MediaProviderOperationExecutionOutcome.Succeeded, result.Outcome);
        Assert.Equal(1, provider.StatusUpdateCallCount);
        Assert.Equal(MediaLibraryStatuses.Completed, entry.Status);
        Assert.Equal(MediaMutationSources.ProviderImport, entry.LastMutationSource);
        Assert.Null(entry.LastLocalEditAt);
        Assert.Equal(MediaLibraryStatuses.Completed, binding.LastRequestedStatus);
        Assert.Equal(MediaLibraryStatuses.Current, binding.LastAppliedStatus);
    }

    [Fact]
    public async Task ProcessOperationAsync_StaleImportFanOut_DropsWriteAfterCanonicalStateChanges()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var (entry, binding) = await SeedMediaEntryAsync(db, 417, "stale-fanout@example.com");
        var operation = CreateImportFanOutStatus(binding, MediaLibraryStatuses.Current);
        db.MediaProviderOperations.Add(operation);
        await db.SaveChangesAsync();
        entry.Status = MediaLibraryStatuses.Completed;
        entry.LastMutationSource = MediaMutationSources.ProviderImport;
        await db.SaveChangesAsync();

        var provider = new FakeMediaProvider("anilist");
        var processor = new MediaProviderOperationProcessor(db,
            new MediaProviderRegistry([provider]), NullLogger<MediaProviderOperationProcessor>.Instance);
        var result = await processor.ProcessOperationAsync(operation.Id, CancellationToken.None);

        Assert.Equal(MediaProviderOperationExecutionOutcome.Succeeded, result.Outcome);
        Assert.Equal(0, provider.StatusUpdateCallCount);
        Assert.Equal(MediaLibraryStatuses.Completed, entry.Status);
        Assert.Empty(await db.MediaProviderOperations.ToListAsync());
    }

    [Fact]
    public async Task ProcessOperationAsync_SetsRetryStateWhenProviderWriteFails()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var (entry, binding) = await SeedMediaEntryAsync(dbContext, 402, "retry@example.com");
        var registry = new MediaProviderRegistry([new FakeMediaProvider("anilist", shouldThrow: true)]);
        var processor = new MediaProviderOperationProcessor(dbContext, registry, NullLogger<MediaProviderOperationProcessor>.Instance);

        var operation = await processor.EnqueueStatusUpdateAsync(
            entry.UserId,
            binding,
            new MediaStatusUpdateRequest
            {
                ProviderMediaId = "140960",
                Status = MediaLibraryStatuses.Completed,
                LastKnownRemoteUpdateAt = binding.LastRemoteUpdateAt
            },
            CancellationToken.None);

        var processed = await processor.ProcessOperationAsync(operation.Id, CancellationToken.None);

        Assert.Equal(MediaProviderOperationExecutionOutcome.Queued, processed.Outcome);
        Assert.Contains("simulated", processed.LastError, StringComparison.OrdinalIgnoreCase);

        var queuedOperation = await dbContext.MediaProviderOperations.SingleAsync();
        Assert.Equal(MediaProviderOperationStatuses.Retrying, queuedOperation.Status);
        Assert.Equal(1, queuedOperation.AttemptCount);
        Assert.NotNull(queuedOperation.NextAttemptAt);
        Assert.Contains("simulated", queuedOperation.LastError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessOperationAsync_DoesNotScheduleBeforeAniListRetryAfter()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var (entry, binding) = await SeedMediaEntryAsync(dbContext, 412, "rate-limit@example.com");
        var failure = new AniListRequestException(
            HttpStatusCode.TooManyRequests,
            TimeSpan.FromMinutes(3));
        var registry = new MediaProviderRegistry([new FakeMediaProvider("anilist", failure: failure)]);
        var processor = new MediaProviderOperationProcessor(
            dbContext,
            registry,
            NullLogger<MediaProviderOperationProcessor>.Instance);
        var operation = await processor.EnqueueStatusUpdateAsync(
            entry.UserId,
            binding,
            new MediaStatusUpdateRequest
            {
                ProviderMediaId = "140960",
                Status = MediaLibraryStatuses.Completed,
                LastKnownRemoteUpdateAt = binding.LastRemoteUpdateAt
            },
            CancellationToken.None);

        await processor.ProcessOperationAsync(operation.Id, CancellationToken.None);

        var queuedOperation = await dbContext.MediaProviderOperations.SingleAsync();
        Assert.Equal(
            TimeSpan.FromMinutes(3),
            queuedOperation.NextAttemptAt!.Value - queuedOperation.UpdatedAt);
    }

    [Fact]
    public async Task ProcessOperationAsync_ReturnsQueuedWhenAnotherCallerClaimsTheOperation()
    {
        var connectionString = $"Data Source=file:media-ops-{Guid.NewGuid():N}?mode=memory&cache=shared";
        await using var keeperConnection = new SqliteConnection(connectionString);
        await keeperConnection.OpenAsync();

        var setupOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(keeperConnection)
            .Options;

        Guid operationId;
        await using (var setupContext = new ApplicationDbContext(setupOptions))
        {
            await setupContext.Database.EnsureCreatedAsync();

            var (entry, binding) = await SeedMediaEntryAsync(setupContext, 403, "claim@example.com");
            var enqueueProcessor = new MediaProviderOperationProcessor(
                setupContext,
                new MediaProviderRegistry([new FakeMediaProvider("anilist")]),
                NullLogger<MediaProviderOperationProcessor>.Instance);

            var operation = await enqueueProcessor.EnqueueProgressUpdateAsync(
                entry.UserId,
                binding,
                new MediaProgressUpdateRequest
                {
                    ProviderMediaId = "140960",
                    ProgressEpisodes = 12,
                    LastKnownRemoteUpdateAt = binding.LastRemoteUpdateAt
                },
                CancellationToken.None);

            operationId = operation.Id;
        }

        var interceptor = new ExternalOperationClaimInterceptor(async () =>
        {
            await using var claimConnection = new SqliteConnection(connectionString);
            await claimConnection.OpenAsync();

            var claimOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(claimConnection)
                .Options;

            await using var claimContext = new ApplicationDbContext(claimOptions);
            var claimedAt = DateTimeOffset.UtcNow;
            await claimContext.MediaProviderOperations
                .Where(item => item.Id == operationId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(item => item.Status, MediaProviderOperationStatuses.Processing)
                        .SetProperty(item => item.LastAttemptAt, claimedAt)
                        .SetProperty(item => item.AttemptCount, item => item.AttemptCount + 1)
                        .SetProperty(item => item.UpdatedAt, claimedAt));
        });

        await using var processorConnection = new SqliteConnection(connectionString);
        await processorConnection.OpenAsync();

        var processorOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(processorConnection)
            .AddInterceptors(interceptor)
            .Options;

        await using var processorContext = new ApplicationDbContext(processorOptions);
        var provider = new FakeMediaProvider("anilist");
        var processor = new MediaProviderOperationProcessor(
            processorContext,
            new MediaProviderRegistry([provider]),
            NullLogger<MediaProviderOperationProcessor>.Instance);

        var result = await processor.ProcessOperationAsync(operationId, CancellationToken.None);

        Assert.Equal(MediaProviderOperationExecutionOutcome.Queued, result.Outcome);
        Assert.Equal(0, provider.ProgressUpdateCallCount);
        Assert.Equal(0, provider.StatusUpdateCallCount);

        await using var verificationConnection = new SqliteConnection(connectionString);
        await verificationConnection.OpenAsync();

        var verificationOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(verificationConnection)
            .Options;

        await using var verificationContext = new ApplicationDbContext(verificationOptions);
        var queuedOperation = await verificationContext.MediaProviderOperations.SingleAsync();
        Assert.Equal(MediaProviderOperationStatuses.Processing, queuedOperation.Status);
        Assert.Equal(1, queuedOperation.AttemptCount);
        Assert.NotNull(queuedOperation.LastAttemptAt);
    }

    [Fact]
    public async Task ProcessOperationAsync_AutoProgressUpdate_UpdatesEntryAndSetsObservationMutationSource()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var (entry, binding) = await SeedMediaEntryAsync(dbContext, 411, "auto-progress@example.com");
        entry.ProgressChapters = 20;
        entry.ProgressVolumes = 2;
        var provider = new FakeMediaProvider("anilist");
        var registry = new MediaProviderRegistry([provider]);
        var processor = new MediaProviderOperationProcessor(dbContext, registry, NullLogger<MediaProviderOperationProcessor>.Instance);

        var payload = new AutoProgressUpdatePayload
        {
            ProviderMediaId = "140960",
            ProgressEpisodes = 15,
            ProgressChapters = 18,
            ProgressVolumes = 4,
            LastKnownRemoteUpdateAt = binding.LastRemoteUpdateAt,
        };

        var operation = await processor.EnqueueAutoProgressAsync(entry.UserId, binding, payload, CancellationToken.None);

        var result = await processor.ProcessOperationAsync(operation.Id, CancellationToken.None);
        var persistedEntry = await dbContext.MediaLibraryEntries.SingleAsync();

        Assert.Equal(MediaProviderOperationExecutionOutcome.Succeeded, result.Outcome);
        Assert.Equal(15, persistedEntry.ProgressEpisodes);
        Assert.Equal(20, persistedEntry.ProgressChapters);
        Assert.Equal(4, persistedEntry.ProgressVolumes);
        Assert.Equal(MediaMutationSources.ObservationAutoProgress, persistedEntry.LastMutationSource);
        Assert.NotNull(persistedEntry.LastLocalEditAt);
        Assert.Equal(1, provider.ProgressUpdateCallCount);
        Assert.Equal(0, await dbContext.MediaProviderOperations.CountAsync());
    }

    [Fact]
    public async Task ProcessOperationAsync_AutoProgressUpdate_SkipsWrite_WhenRemoteStateIsNewer()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var (entry, binding) = await SeedMediaEntryAsync(dbContext, 412, "sync-guard@example.com");

        // Payload carries an old LastKnownRemoteUpdateAt
        var oldKnownAt = binding.LastRemoteUpdateAt!.Value.AddMinutes(-5);

        // Simulate that the DB entry has since been updated to a newer timestamp
        // (i.e., a provider sync ran after we enqueued the operation).
        var newerRemoteAt = binding.LastRemoteUpdateAt.Value.AddMinutes(2);
        await dbContext.MediaLibraryProviderBindings
            .Where(e => e.Id == binding.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.LastRemoteUpdateAt, newerRemoteAt));

        var provider = new FakeMediaProvider("anilist");
        var registry = new MediaProviderRegistry([provider]);
        var processor = new MediaProviderOperationProcessor(dbContext, registry, NullLogger<MediaProviderOperationProcessor>.Instance);

        var payload = new AutoProgressUpdatePayload
        {
            ProviderMediaId = "140960",
            ProgressEpisodes = 18,
            LastKnownRemoteUpdateAt = oldKnownAt  // stale snapshot
        };

        var operation = await processor.EnqueueAutoProgressAsync(entry.UserId, binding, payload, CancellationToken.None);

        var result = await processor.ProcessOperationAsync(operation.Id, CancellationToken.None);

        // Operation should still succeed (skipped cleanly, not retried/failed)
        Assert.Equal(MediaProviderOperationExecutionOutcome.Succeeded, result.Outcome);
        // Provider write was NOT invoked
        Assert.Equal(0, provider.ProgressUpdateCallCount);
        // Operation was removed from queue after clean skip
        Assert.Equal(0, await dbContext.MediaProviderOperations.CountAsync());
    }

    [Fact]
    public async Task ProcessOperationAsync_CommittedProgressSkipsFirstExternalTimestamp()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        await dbContext.Database.EnsureCreatedAsync();
        var (entry, binding) = await SeedMediaEntryAsync(dbContext, 415, "first-remote-timestamp@example.com");
        binding.LastRemoteUpdateAt = null;
        entry.ProgressEpisodes = 15;
        await dbContext.SaveChangesAsync();
        var provider = new FakeMediaProvider("anilist");
        var processor = new MediaProviderOperationProcessor(dbContext, new MediaProviderRegistry([provider]),
            NullLogger<MediaProviderOperationProcessor>.Instance);
        var operation = await processor.EnqueueAutoProgressAsync(entry.UserId, binding, new AutoProgressUpdatePayload
        {
            ProviderMediaId = "140960", ProgressEpisodes = 15, LocalProgressCommitted = true
        }, default);

        var externalTimestamp = DateTimeOffset.UtcNow.AddMinutes(-1);
        await dbContext.MediaLibraryProviderBindings.Where(row => row.Id == binding.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.LastRemoteUpdateAt, externalTimestamp));
        var result = await processor.ProcessOperationAsync(operation.Id, default);

        Assert.Equal(MediaProviderOperationExecutionOutcome.Succeeded, result.Outcome);
        Assert.Equal(0, provider.ProgressUpdateCallCount);
        Assert.Empty(dbContext.MediaProviderOperations);
    }

    [Fact]
    public async Task ProcessOperationAsync_DoesNotSendSupersededObservationProgress()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        await dbContext.Database.EnsureCreatedAsync();
        var (entry, binding) = await SeedMediaEntryAsync(dbContext, 413, "superseded-progress@example.com");
        var provider = new FakeMediaProvider("anilist");
        var processor = new MediaProviderOperationProcessor(dbContext, new MediaProviderRegistry([provider]),
            NullLogger<MediaProviderOperationProcessor>.Instance);
        entry.ProgressEpisodes = 15;
        var operation = await processor.EnqueueAutoProgressAsync(entry.UserId, binding, new AutoProgressUpdatePayload
        {
            ProviderMediaId = "140960", ProgressEpisodes = 15, LocalProgressCommitted = true
        }, default);

        entry.ProgressEpisodes = 16;
        await dbContext.SaveChangesAsync();
        var result = await processor.ProcessOperationAsync(operation.Id, default);

        Assert.Equal(MediaProviderOperationExecutionOutcome.Succeeded, result.Outcome);
        Assert.Equal(0, provider.ProgressUpdateCallCount);
        Assert.Equal(16, await dbContext.MediaLibraryEntries.Select(row => row.ProgressEpisodes).SingleAsync());
        Assert.Empty(dbContext.MediaProviderOperations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DispatchSkipsNewerRemoteVersionOnSqlite(bool startsWithoutRemoteTimestamp)
    {
        await using var db = await OpenLeaseTestSqliteAsync();
        await AssertDispatchSkipsNewerRemoteVersionAsync(db, startsWithoutRemoteTimestamp);
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DispatchSkipsNewerRemoteVersionOnPostgres(bool startsWithoutRemoteTimestamp)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await AssertDispatchSkipsNewerRemoteVersionAsync(schema.Db, startsWithoutRemoteTimestamp);
    }

    private static async Task AssertDispatchSkipsNewerRemoteVersionAsync(
        ApplicationDbContext db, bool startsWithoutRemoteTimestamp)
    {
        var (entry, binding) = await SeedMediaEntryAsync(db, 416, "dispatch-remote-version@example.com");
        var newerVersion = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        binding.LastRemoteUpdateAt = startsWithoutRemoteTimestamp ? null : newerVersion.AddMinutes(-1);
        entry.ProgressEpisodes = 15;
        await db.SaveChangesAsync();

        var provider = new FakeMediaProvider("anilist");
        var operation = await CreateProcessor(db, provider).EnqueueAutoProgressAsync(entry.UserId, binding,
            new AutoProgressUpdatePayload
            {
                ProviderMediaId = "140960", ProgressEpisodes = 15, LocalProgressCommitted = true,
                LastKnownRemoteUpdateAt = binding.LastRemoteUpdateAt
            }, default);

        var preflightSeen = false;
        var interceptor = new BeforeReaderInterceptor(sql =>
        {
            if (!sql.StartsWith("SELECT", StringComparison.Ordinal)
                || !sql.Contains("FROM \"MediaLibraryProviderBindings\"", StringComparison.Ordinal))
            {
                return false;
            }

            // The preflight projects only the remote version. The next binding query reloads the full row.
            if (!sql.Contains("\"LastSyncedAt\"", StringComparison.Ordinal))
            {
                preflightSeen = true;
                return false;
            }

            return preflightSeen;
        }, async token =>
        {
            Assert.Equal(1, await db.MediaLibraryProviderBindings.Where(row => row.Id == binding.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.LastRemoteUpdateAt, newerVersion), token));
        });
        var options = (DbContextOptions<ApplicationDbContext>)db.GetService<IDbContextOptions>();
        await using var worker = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>(options)
            .AddInterceptors(interceptor).Options);

        var result = await CreateProcessor(worker, provider).ProcessOperationAsync(operation.Id, default);

        Assert.Equal(MediaProviderOperationExecutionOutcome.Succeeded, result.Outcome);
        Assert.Equal(0, provider.ProgressUpdateCallCount);
        Assert.Empty(await db.MediaProviderOperations.ToListAsync());
        Assert.Equal(15, await db.MediaLibraryEntries.Select(row => row.ProgressEpisodes).SingleAsync());
        Assert.Equal(newerVersion, await db.MediaLibraryProviderBindings.Select(row => row.LastRemoteUpdateAt).SingleAsync());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task InFlightObservationAcknowledgementPreservesNewerLocalProgress(
        bool externalRemoteUpdate, bool startsWithoutRemoteTimestamp)
    {
        var connectionString = $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connectionString).Options;
        await using var observationsDb = new ApplicationDbContext(options);
        await observationsDb.Database.EnsureCreatedAsync();
        await AssertOrderedProgressAsync(observationsDb, options, externalRemoteUpdate, startsWithoutRemoteTimestamp);
    }

    [Fact]
    public async Task LegacyAutoProgressAcknowledgementPreservesNewerSqliteObservation()
    {
        await using var db = await OpenLeaseTestSqliteAsync();
        var options = (DbContextOptions<ApplicationDbContext>)db.GetService<IDbContextOptions>();
        await AssertOrderedProgressAsync(db, options, externalRemoteUpdate: false, startsWithoutRemoteTimestamp: false, legacy: true);
    }

    [PostgresFact]
    public async Task LegacyAutoProgressAcknowledgementPreservesNewerPostgresObservation()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        var options = (DbContextOptions<ApplicationDbContext>)schema.Db.GetService<IDbContextOptions>();
        await AssertOrderedProgressAsync(schema.Db, options, externalRemoteUpdate: false, startsWithoutRemoteTimestamp: false, legacy: true);
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentPostgresProcessorsDispatchOneOperationPerBinding(bool startsWithoutRemoteTimestamp)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(schema.Db.Database.GetConnectionString(), provider => provider.EnableRetryOnFailure(1)).Options;
        await AssertOrderedProgressAsync(schema.Db, options, externalRemoteUpdate: false, startsWithoutRemoteTimestamp);
    }

    [Theory]
    [InlineData(MediaProviderOperationTypes.UpdateScore, false)]
    [InlineData(MediaProviderOperationTypes.UpdateStatus, false)]
    [InlineData(MediaProviderOperationTypes.UpdateScore, true)]
    public async Task InFlightMutationAcknowledgementPreservesQueuedSqliteProgress(string mutation, bool externalRemoteUpdate)
    {
        await using var db = await OpenLeaseTestSqliteAsync();
        var options = (DbContextOptions<ApplicationDbContext>)db.GetService<IDbContextOptions>();
        await AssertOrderedProgressAsync(db, options, externalRemoteUpdate, startsWithoutRemoteTimestamp: false,
            firstMutation: mutation);
    }

    [PostgresTheory]
    [InlineData(MediaProviderOperationTypes.UpdateScore, false)]
    [InlineData(MediaProviderOperationTypes.UpdateStatus, false)]
    [InlineData(MediaProviderOperationTypes.UpdateScore, true)]
    public async Task InFlightMutationAcknowledgementPreservesQueuedPostgresProgress(string mutation, bool externalRemoteUpdate)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        var options = (DbContextOptions<ApplicationDbContext>)schema.Db.GetService<IDbContextOptions>();
        await AssertOrderedProgressAsync(schema.Db, options, externalRemoteUpdate, startsWithoutRemoteTimestamp: false,
            firstMutation: mutation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LaterScoreAcknowledgementPreservesRetryingSqliteProgress(bool externalRemoteUpdate)
    {
        await using var db = await OpenLeaseTestSqliteAsync();
        await AssertRetryingProgressAfterScoreAsync(db, externalRemoteUpdate);
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LaterScoreAcknowledgementPreservesRetryingPostgresProgress(bool externalRemoteUpdate)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await AssertRetryingProgressAfterScoreAsync(schema.Db, externalRemoteUpdate);
    }

    [Fact]
    public async Task LaterScoreAcknowledgementPreservesExpiredSqliteProgress()
    {
        await using var db = await OpenLeaseTestSqliteAsync();
        await AssertExpiredProgressAfterScoreAsync(db);
    }

    [PostgresFact]
    public async Task LaterScoreAcknowledgementPreservesExpiredPostgresProgress()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await AssertExpiredProgressAfterScoreAsync(schema.Db);
    }

    private static async Task AssertExpiredProgressAfterScoreAsync(ApplicationDbContext db)
    {
        var (entry, binding) = await SeedMediaEntryAsync(db, 414, "expired-progress@example.test");
        entry.ProgressEpisodes = 11;
        entry.Score = 8;
        binding.LastRemoteUpdateAt = DateTimeOffset.UtcNow.AddHours(-1);
        await db.SaveChangesAsync();

        var remoteProgress = 10;
        var provider = new FakeMediaProvider("anilist")
        {
            BeforeProgressUpdate = (request, _) =>
            {
                remoteProgress = request.ProgressEpisodes!.Value;
                return Task.CompletedTask;
            }
        };
        var enqueueProcessor = CreateProcessor(db, provider);
        var progress = await enqueueProcessor.EnqueueAutoProgressAsync(entry.UserId, binding,
            new AutoProgressUpdatePayload
            {
                ProviderMediaId = "140960", ProgressEpisodes = 11, LocalProgressCommitted = true,
                LastKnownRemoteUpdateAt = binding.LastRemoteUpdateAt
            }, default);
        var expired = DateTimeOffset.UtcNow - MediaProviderOperationProcessor.ProcessingLeaseTimeout - TimeSpan.FromSeconds(1);
        progress.CreatedAt = expired;
        progress.UpdatedAt = expired;
        progress.LastAttemptAt = expired;
        progress.Status = MediaProviderOperationStatuses.Processing;
        progress.AttemptCount = 1;
        await db.SaveChangesAsync();
        var score = await enqueueProcessor.EnqueueScoreUpdateAsync(entry.UserId, binding,
            new MediaScoreUpdateRequest { ProviderMediaId = "140960", Score = 8 }, default);

        var options = (DbContextOptions<ApplicationDbContext>)db.GetService<IDbContextOptions>();
        await using var processorDb = new ApplicationDbContext(options);
        var processor = CreateProcessor(processorDb, provider);
        Assert.Equal(MediaProviderOperationExecutionOutcome.Succeeded,
            (await processor.ProcessOperationAsync(score.Id, default)).Outcome);
        Assert.Equal(10, remoteProgress);
        Assert.Equal(MediaProviderOperationStatuses.Processing,
            await db.MediaProviderOperations.Where(row => row.Id == progress.Id).Select(row => row.Status).SingleAsync());

        Assert.Equal(1, await processor.ProcessDueOperationsAsync(default));
        Assert.Equal(1, provider.ProgressUpdateCallCount);
        Assert.Equal(11, remoteProgress);
        Assert.Empty(await db.MediaProviderOperations.ToListAsync());
    }

    private static async Task AssertRetryingProgressAfterScoreAsync(ApplicationDbContext db, bool externalRemoteUpdate)
    {
        var (entry, binding) = await SeedMediaEntryAsync(db, 414, "retrying-progress@example.test");
        entry.ProgressEpisodes = 10;
        var initialVersion = DateTimeOffset.UtcNow.AddHours(-2);
        binding.LastRemoteUpdateAt = initialVersion.AddTicks(-(initialVersion.Ticks % 10));
        await db.SaveChangesAsync();

        var remoteProgress = 10;
        var failFirstProgressUpdate = true;
        var provider = new FakeMediaProvider("anilist")
        {
            BeforeProgressUpdate = (request, _) =>
            {
                if (failFirstProgressUpdate)
                {
                    failFirstProgressUpdate = false;
                    throw new TimeoutException("Simulated transient progress failure.");
                }

                remoteProgress = request.ProgressEpisodes!.Value;
                return Task.CompletedTask;
            }
        };
        var registry = new MediaProviderRegistry([provider]);
        var enqueueProcessor = CreateProcessor(db, provider);
        var mappings = new MediaProviderSeasonMappingService(db, NullLogger<MediaProviderSeasonMappingService>.Instance);
        var matching = new MediaObservationMatchingService(db, mappings,
            new MediaRelationGraphRefreshQueue(), NullLogger<MediaObservationMatchingService>.Instance);
        var progress = new MediaObservationProgressService(db, enqueueProcessor,
            new MediaLibraryEventHub(), NullLogger<MediaObservationProgressService>.Instance, mappings, matching);
        Assert.Equal(1, await progress.TryEnqueueAutoProgressAsync(new MediaObservation
        {
            UserId = entry.UserId, SiteIdentifier = "crunchyroll",
            ObservedTitle = "Spy x Family", ObservedUrl = "https://www.crunchyroll.com/watch/SPY11",
            SiteMediaId = "SPY11", MediaTitleId = entry.MediaTitleId,
            MatchStatus = MediaObservationStatuses.Matched, ResolvedProgress = 11
        }, default));
        Assert.Equal(11, entry.ProgressEpisodes);

        var options = (DbContextOptions<ApplicationDbContext>)db.GetService<IDbContextOptions>();
        await using var processorDb = new ApplicationDbContext(options);
        var processor = new MediaProviderOperationProcessor(processorDb, registry,
            NullLogger<MediaProviderOperationProcessor>.Instance);
        var progressId = await db.MediaProviderOperations.Select(row => row.Id).SingleAsync();
        Assert.Equal(MediaProviderOperationExecutionOutcome.Queued,
            (await processor.ProcessOperationAsync(progressId, default)).Outcome);
        var retry = await db.MediaProviderOperations.AsNoTracking().SingleAsync(row => row.Id == progressId);
        Assert.Equal(MediaProviderOperationStatuses.Retrying, retry.Status);
        Assert.Equal(10, remoteProgress);

        if (externalRemoteUpdate)
        {
            binding.LastRemoteUpdateAt = binding.LastRemoteUpdateAt!.Value.AddHours(1);
            await db.SaveChangesAsync();
        }

        entry.Score = 8;
        var score = await enqueueProcessor.EnqueueScoreUpdateAsync(entry.UserId, binding,
            new MediaScoreUpdateRequest { ProviderMediaId = "140960", Score = 8 }, default);
        Assert.True(score.CreatedAt > retry.CreatedAt);
        Assert.Equal(MediaProviderOperationExecutionOutcome.Succeeded,
            (await processor.ProcessOperationAsync(score.Id, default)).Outcome);

        await db.MediaProviderOperations.Where(row => row.Id == progressId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.NextAttemptAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        Assert.Equal(1, await processor.ProcessDueOperationsAsync(default));
        Assert.Equal(externalRemoteUpdate ? 1 : 2, provider.ProgressUpdateCallCount);
        Assert.Equal(externalRemoteUpdate ? 10 : 11, remoteProgress);
        Assert.Empty(await db.MediaProviderOperations.ToListAsync());
    }

    private static async Task AssertOrderedProgressAsync(ApplicationDbContext observationsDb,
        DbContextOptions<ApplicationDbContext> options, bool externalRemoteUpdate, bool startsWithoutRemoteTimestamp,
        bool legacy = false, string? firstMutation = null)
    {
        await using var processorDb = new ApplicationDbContext(options);
        await using var secondProcessorDb = new ApplicationDbContext(options);
        var (entry, binding) = await SeedMediaEntryAsync(observationsDb, 414, "in-flight-progress@example.test");
        var initialProgress = firstMutation is null ? 9 : 10;
        entry.ProgressEpisodes = initialProgress;
        binding.LastRemoteUpdateAt = startsWithoutRemoteTimestamp ? null : DateTimeOffset.UtcNow.AddHours(-2);
        await observationsDb.SaveChangesAsync();

        var enteredProvider = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var acknowledge = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sentProgress = new List<int?>();
        var sentVersions = new List<DateTimeOffset?>();
        int? remoteProgress = initialProgress;
        async Task SuspendWriteAsync(CancellationToken cancellationToken)
        {
            enteredProvider.SetResult();
            await acknowledge.Task.WaitAsync(cancellationToken);
        }

        var provider = new FakeMediaProvider("anilist")
        {
            BeforeStatusOrScoreUpdate = SuspendWriteAsync,
            BeforeProgressUpdate = async (request, cancellationToken) =>
            {
                sentProgress.Add(request.ProgressEpisodes);
                sentVersions.Add(request.LastKnownRemoteUpdateAt);
                if (request.ProgressEpisodes == 10)
                {
                    await SuspendWriteAsync(cancellationToken);
                }
                remoteProgress = request.ProgressEpisodes;
            }
        };
        var registry = new MediaProviderRegistry([provider]);
        var mappings = new MediaProviderSeasonMappingService(observationsDb, NullLogger<MediaProviderSeasonMappingService>.Instance);
        var matching = new MediaObservationMatchingService(observationsDb, mappings,
            new MediaRelationGraphRefreshQueue(), NullLogger<MediaObservationMatchingService>.Instance);
        var progress = new MediaObservationProgressService(observationsDb,
            new MediaProviderOperationProcessor(observationsDb, registry, NullLogger<MediaProviderOperationProcessor>.Instance),
            new MediaLibraryEventHub(), NullLogger<MediaObservationProgressService>.Instance, mappings, matching);
        var processor = new MediaProviderOperationProcessor(processorDb, registry,
            NullLogger<MediaProviderOperationProcessor>.Instance);
        var secondProcessor = new MediaProviderOperationProcessor(secondProcessorDb, registry,
            NullLogger<MediaProviderOperationProcessor>.Instance);

        Task<int> CommitEpisodeAsync(int number) => progress.TryEnqueueAutoProgressAsync(new MediaObservation
        {
            UserId = entry.UserId, SiteIdentifier = "crunchyroll",
            ObservedTitle = "Spy x Family", ObservedUrl = $"https://www.crunchyroll.com/watch/SPY{number}",
            SiteMediaId = $"SPY{number}", MediaTitleId = entry.MediaTitleId,
            MatchStatus = MediaObservationStatuses.Matched, ResolvedProgress = number
        }, default);

        var enqueueProcessor = CreateProcessor(observationsDb, provider);
        if (firstMutation == MediaProviderOperationTypes.UpdateScore)
        {
            await enqueueProcessor.EnqueueScoreUpdateAsync(entry.UserId, binding,
                new MediaScoreUpdateRequest { ProviderMediaId = "140960", Score = 8 }, default);
        }
        else if (firstMutation == MediaProviderOperationTypes.UpdateStatus)
        {
            await enqueueProcessor.EnqueueStatusUpdateAsync(entry.UserId, binding,
                new MediaStatusUpdateRequest { ProviderMediaId = "140960", Status = MediaLibraryStatuses.Current }, default);
        }
        else if (legacy)
        {
            await enqueueProcessor.EnqueueAutoProgressAsync(entry.UserId, binding,
                new AutoProgressUpdatePayload
                {
                    ProviderMediaId = "140960", ProgressEpisodes = 10, LocalProgressCommitted = false,
                    LastKnownRemoteUpdateAt = binding.LastRemoteUpdateAt
                }, default);
        }
        else
        {
            Assert.Equal(1, await CommitEpisodeAsync(10));
        }
        var firstId = await observationsDb.MediaProviderOperations.Select(row => row.Id).SingleAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var firstWrite = processor.ProcessOperationAsync(firstId, timeout.Token);
        Guid secondId;
        DateTimeOffset? latestLocalEdit;
        var expectedProgress = new List<int?>();
        if (firstMutation is null)
        {
            expectedProgress.Add(10);
        }
        try
        {
            await enteredProvider.Task.WaitAsync(timeout.Token);
            Assert.Equal(1, await CommitEpisodeAsync(11));
            secondId = await observationsDb.MediaProviderOperations.Where(row => row.Id != firstId)
                .Select(row => row.Id).SingleAsync();
            latestLocalEdit = await observationsDb.MediaLibraryEntries.Where(row => row.Id == entry.Id)
                .Select(row => row.LastLocalEditAt).SingleAsync(timeout.Token);
            Assert.Equal(MediaProviderOperationExecutionOutcome.Queued,
                (await secondProcessor.ProcessOperationAsync(secondId, timeout.Token)).Outcome);
            if (observationsDb.Database.IsNpgsql())
            {
                Assert.Equal(0, await secondProcessor.ProcessDueOperationsAsync(timeout.Token));
            }
            var queued = await secondProcessorDb.MediaProviderOperations.AsNoTracking().SingleAsync(row => row.Id == secondId);
            Assert.Equal(MediaProviderOperationStatuses.Pending, queued.Status);
            Assert.Equal(0, queued.AttemptCount);
            Assert.Equal(expectedProgress, sentProgress);
            Assert.Equal(initialProgress, remoteProgress);
            if (externalRemoteUpdate)
            {
                binding.LastRemoteUpdateAt = DateTimeOffset.UtcNow.AddHours(-1);
                await observationsDb.SaveChangesAsync();
            }
        }
        finally
        {
            acknowledge.TrySetResult();
            Assert.Equal(MediaProviderOperationExecutionOutcome.Succeeded, (await firstWrite).Outcome);
        }

        await observationsDb.Entry(entry).ReloadAsync();
        Assert.Equal(11, entry.ProgressEpisodes);
        Assert.Equal(latestLocalEdit, entry.LastLocalEditAt);
        if (observationsDb.Database.IsNpgsql())
        {
            Assert.Equal(1, await secondProcessor.ProcessDueOperationsAsync(timeout.Token));
        }
        else
        {
            Assert.Equal(MediaProviderOperationExecutionOutcome.Succeeded,
                (await secondProcessor.ProcessOperationAsync(secondId, timeout.Token)).Outcome);
        }
        if (!externalRemoteUpdate)
        {
            expectedProgress.Add(11);
        }
        Assert.Equal(expectedProgress, sentProgress);
        Assert.Equal(externalRemoteUpdate ? 10 : 11, remoteProgress);
        if (startsWithoutRemoteTimestamp && !externalRemoteUpdate)
        {
            Assert.Null(sentVersions[0]);
            Assert.NotNull(sentVersions[1]);
        }
        Assert.Empty(await observationsDb.MediaProviderOperations.ToListAsync());
    }

    private static MediaProviderOperation CreateImportFanOutStatus(MediaLibraryProviderBinding binding, string status)
    {
        var now = DateTimeOffset.UtcNow;
        return new MediaProviderOperation
        {
            Id = Guid.NewGuid(),
            MediaLibraryProviderBindingId = binding.Id,
            OperationType = MediaProviderOperationTypes.ImportFanOutStatus,
            PayloadJson = JsonSerializer.Serialize(new MediaStatusUpdateRequest
            {
                ProviderMediaId = "140960",
                Status = status,
                LastKnownRemoteUpdateAt = binding.LastRemoteUpdateAt
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            Status = MediaProviderOperationStatuses.Pending,
            NextAttemptAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static async Task<(MediaLibraryEntry Entry, MediaLibraryProviderBinding Binding)> SeedMediaEntryAsync(ApplicationDbContext dbContext, int userId, string email, string externalId = "140960")
    {
        var now = DateTimeOffset.UtcNow;
        var user = TestUserFactory.Create(userId, email);
        var account = new ConnectedServiceAccount
        {
            Id = 1000 + userId,
            UserId = userId,
            Service = "anilist",
            ExternalAccountId = $"viewer-{userId}",
            DisplayName = "Queue Tester",
            CreatedAt = now.UtcDateTime,
            UpdatedAt = now.UtcDateTime
        };
        var title = new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = "Spy x Family",
            MediaKind = MediaKinds.Anime,
            SupportsEpisodeProgress = true,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            CreatedAt = now,
            UpdatedAt = now
        };
        var entry = new MediaLibraryEntry
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            MediaTitleId = title.Id,
            Status = MediaLibraryStatuses.Current,
            ProgressEpisodes = 10,
            CreatedAt = now,
            UpdatedAt = now
        };
        var link = new MediaProviderLink
        {
            Id = Guid.NewGuid(), MediaTitleId = title.Id, Provider = "anilist", ExternalId = externalId,
            LinkSource = MediaMappingSources.Imported, CreatedAt = now, UpdatedAt = now
        };
        var binding = new MediaLibraryProviderBinding
        {
            Id = Guid.NewGuid(), MediaLibraryEntryId = entry.Id, MediaProviderLinkId = link.Id,
            ConnectedServiceAccountId = account.Id, ProviderAccountId = account.ExternalAccountId,
            LastRemoteUpdateAt = now.AddMinutes(-2), CreatedAt = now, UpdatedAt = now
        };

        dbContext.Users.Add(user);
        dbContext.ConnectedServiceAccounts.Add(account);
        dbContext.MediaTitles.Add(title);
        dbContext.MediaProviderLinks.Add(link);
        dbContext.MediaLibraryEntries.Add(entry);
        dbContext.MediaLibraryProviderBindings.Add(binding);
        await dbContext.SaveChangesAsync();

        return (entry, binding);
    }

    private sealed class FakeMediaProvider : IMediaProvider
    {
        private readonly string _providerId;
        private readonly bool _shouldThrow;
        private readonly Exception? _failure;
        private readonly string? _appliedStatus;

        public FakeMediaProvider(
            string providerId,
            bool shouldThrow = false,
            Exception? failure = null,
            string? appliedStatus = null)
        {
            _providerId = providerId;
            _shouldThrow = shouldThrow;
            _failure = failure;
            _appliedStatus = appliedStatus;
        }

        public string ProviderId => _providerId;

        public int ProgressUpdateCallCount { get; private set; }
        public DateTimeOffset? ProgressRemoteUpdateAt { get; init; }
        public Func<MediaProgressUpdateRequest, CancellationToken, Task>? BeforeProgressUpdate { get; init; }
        public Func<CancellationToken, Task>? BeforeStatusOrScoreUpdate { get; init; }

        public int StatusUpdateCallCount { get; private set; }

        public int LibraryStateSyncCallCount { get; private set; }

        public MediaLibraryStateSyncRequest? LastLibraryStateSyncRequest { get; private set; }
        public ConnectedServiceAccount? ConnectedAccount { get; init; }
        public Func<CancellationToken, Task>? BeforeGetConnectedAccount { get; init; }

        public async Task<ConnectedServiceAccount?> GetConnectedAccountAsync(int userId, CancellationToken cancellationToken = default)
        {
            if (BeforeGetConnectedAccount is not null)
            {
                await BeforeGetConnectedAccount(cancellationToken);
            }
            return ConnectedAccount;
        }

        public string GetAuthorizationUrl(string redirectUri, string state, string codeChallenge)
        {
            throw new NotSupportedException();
        }

        public Task<ConnectedServiceAccount> ExchangeCodeAndSaveAsync(int userId, string authorizationCode, string redirectUri, string codeVerifier, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task DisconnectAsync(int userId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<MediaProviderLibraryImportResult> ImportLibraryAsync(int userId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<MediaProviderSearchResult>> SearchAsync(int userId, MediaCatalogSearchRequest request, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<MediaProviderTitleDetails?> GetTitleDetailsAsync(int userId, string providerMediaId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public async Task<MediaProviderMutationResult> UpdateProgressAsync(int userId, MediaProgressUpdateRequest request, CancellationToken cancellationToken)
        {
            ProgressUpdateCallCount += 1;

            if (_failure is not null) throw _failure;
            if (_shouldThrow)
            {
                throw new InvalidOperationException("Simulated provider write failure.");
            }

            if (BeforeProgressUpdate is not null)
            {
                await BeforeProgressUpdate(request, cancellationToken);
            }

            return new MediaProviderMutationResult
            {
                ProviderId = _providerId,
                ProviderMediaId = request.ProviderMediaId,
                AppliedAt = DateTimeOffset.UtcNow,
                AppliedProgressEpisodes = request.ProgressEpisodes,
                LastRemoteUpdateAt = ProgressRemoteUpdateAt ?? DateTimeOffset.UtcNow
            };
        }

        public async Task<MediaProviderMutationResult> UpdateStatusAsync(int userId, MediaStatusUpdateRequest request, CancellationToken cancellationToken)
        {
            StatusUpdateCallCount += 1;

            if (_failure is not null) throw _failure;
            if (_shouldThrow)
            {
                throw new InvalidOperationException("Simulated provider write failure.");
            }

            if (BeforeStatusOrScoreUpdate is not null)
            {
                await BeforeStatusOrScoreUpdate(cancellationToken);
            }

            return new MediaProviderMutationResult
            {
                ProviderId = _providerId,
                ProviderMediaId = request.ProviderMediaId,
                AppliedStatus = _appliedStatus,
                AppliedAt = DateTimeOffset.UtcNow,
                LastRemoteUpdateAt = DateTimeOffset.UtcNow
            };
        }

        public async Task<MediaProviderMutationResult> UpdateScoreAsync(int userId, MediaScoreUpdateRequest request, CancellationToken cancellationToken)
        {
            if (_failure is not null) throw _failure;
            if (_shouldThrow)
            {
                throw new InvalidOperationException("Simulated provider write failure.");
            }

            if (BeforeStatusOrScoreUpdate is not null)
            {
                await BeforeStatusOrScoreUpdate(cancellationToken);
            }

            return new MediaProviderMutationResult
            {
                ProviderId = "anilist",
                ProviderMediaId = request.ProviderMediaId,
                AppliedAt = DateTimeOffset.UtcNow,
                LastRemoteUpdateAt = DateTimeOffset.UtcNow
            };
        }

        public Task<MediaProviderMutationResult> SyncLibraryStateAsync(
            int userId,
            MediaLibraryStateSyncRequest request,
            CancellationToken cancellationToken)
        {
            LibraryStateSyncCallCount += 1;
            LastLibraryStateSyncRequest = request;

            if (_failure is not null) throw _failure;
            if (_shouldThrow)
            {
                throw new InvalidOperationException("Simulated provider write failure.");
            }

            return Task.FromResult(new MediaProviderMutationResult
            {
                ProviderId = _providerId,
                ProviderMediaId = request.ProviderMediaId,
                AppliedStatus = _appliedStatus,
                AppliedAt = DateTimeOffset.UtcNow,
                LastRemoteUpdateAt = DateTimeOffset.UtcNow
            });
        }

        public Task<MediaReleaseMetadata?> GetReleaseMetadataAsync(int userId, string providerMediaId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Text)> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add((logLevel, formatter(state, exception)));
        }
    }

    private sealed class ExternalOperationClaimInterceptor(Func<Task> claimAsync) : DbCommandInterceptor
    {
        private readonly Func<Task> _claimAsync = claimAsync;
        private int _claimed;

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (ShouldClaim(command.CommandText))
            {
                await _claimAsync();
            }

            return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        private bool ShouldClaim(string commandText)
        {
            if (Interlocked.CompareExchange(ref _claimed, 1, 0) != 0)
            {
                return false;
            }

            var isOperationUpdate = commandText.Contains("UPDATE", StringComparison.OrdinalIgnoreCase)
                && commandText.Contains("MediaProviderOperations", StringComparison.OrdinalIgnoreCase);

            if (isOperationUpdate)
            {
                return true;
            }

            Interlocked.Exchange(ref _claimed, 0);
            return false;
        }
    }
}
