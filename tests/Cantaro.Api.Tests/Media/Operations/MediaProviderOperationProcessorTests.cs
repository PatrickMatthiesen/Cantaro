using System.Data.Common;
using System.Net;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantaro.Api.Tests;

public class MediaProviderOperationProcessorTests
{
    [Fact]
    public async Task ProcessOperationAsync_UpdatesEntryOnSuccessfulProgressWrite()
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
    public async Task ProcessOperationAsync_UpdatesEntryOnSuccessfulScoreWrite()
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
    public async Task ProcessOperationAsync_SyncLibraryState_UpdatesOnlyProviderBinding()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var (entry, binding) = await SeedMediaEntryAsync(dbContext, 414, "whole-state@example.com");
        var originalLocalEditAt = DateTimeOffset.UtcNow.AddHours(-2);
        var originalEntryUpdatedAt = DateTimeOffset.UtcNow.AddHours(-1);
        entry.Score = 82.5m;
        entry.LastLocalEditAt = originalLocalEditAt;
        entry.LastMutationSource = MediaMutationSources.ProviderImport;
        entry.UpdatedAt = originalEntryUpdatedAt;
        await dbContext.SaveChangesAsync();

        var provider = new FakeMediaProvider("anilist");
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
        var processed = await processor.ProcessOperationAsync(operation.Id, CancellationToken.None);

        var persistedEntry = await dbContext.MediaLibraryEntries.SingleAsync();
        var persistedBinding = await dbContext.MediaLibraryProviderBindings.SingleAsync();
        Assert.Equal(MediaProviderOperationExecutionOutcome.Succeeded, processed.Outcome);
        Assert.Equal(1, provider.LibraryStateSyncCallCount);
        Assert.Equal(request.ProviderMediaId, provider.LastLibraryStateSyncRequest!.ProviderMediaId);
        Assert.Equal(originalLocalEditAt, persistedEntry.LastLocalEditAt);
        Assert.Equal(originalEntryUpdatedAt, persistedEntry.UpdatedAt);
        Assert.Equal(MediaMutationSources.ProviderImport, persistedEntry.LastMutationSource);
        Assert.Equal(82.5m, persistedEntry.Score);
        Assert.NotNull(persistedBinding.LastSyncedAt);
        Assert.NotNull(persistedBinding.LastRemoteUpdateAt);
        Assert.NotNull(request.LastKnownRemoteUpdateAt);
        Assert.True(persistedBinding.LastRemoteUpdateAt.Value > request.LastKnownRemoteUpdateAt.Value);
        Assert.Equal(0, await dbContext.MediaProviderOperations.CountAsync());
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
        var provider = new FakeMediaProvider("anilist");
        var registry = new MediaProviderRegistry([provider]);
        var processor = new MediaProviderOperationProcessor(dbContext, registry, NullLogger<MediaProviderOperationProcessor>.Instance);

        var payload = new AutoProgressUpdatePayload
        {
            ProviderMediaId = "140960",
            ProgressEpisodes = 15,
            LastKnownRemoteUpdateAt = binding.LastRemoteUpdateAt,
            TriggeredByObservationId = Guid.NewGuid().ToString(),
            ObservedSiteIdentifier = MediaObservationSiteIdentifiers.Crunchyroll,
            ObservedProgressHint = "15",
            ObservationMatchScore = 0.97m,
            TriggeredAt = DateTimeOffset.UtcNow
        };

        var operation = await processor.EnqueueAutoProgressAsync(entry.UserId, binding, payload, CancellationToken.None);

        var result = await processor.ProcessOperationAsync(operation.Id, CancellationToken.None);
        var persistedEntry = await dbContext.MediaLibraryEntries.SingleAsync();

        Assert.Equal(MediaProviderOperationExecutionOutcome.Succeeded, result.Outcome);
        Assert.Equal(15, persistedEntry.ProgressEpisodes);
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
            LastKnownRemoteUpdateAt = oldKnownAt,  // stale snapshot
            TriggeredAt = DateTimeOffset.UtcNow
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

    private static async Task<(MediaLibraryEntry Entry, MediaLibraryProviderBinding Binding)> SeedMediaEntryAsync(ApplicationDbContext dbContext, int userId, string email)
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
            Id = Guid.NewGuid(), MediaTitleId = title.Id, Provider = "anilist", ExternalId = "140960",
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

        public FakeMediaProvider(string providerId, bool shouldThrow = false, Exception? failure = null)
        {
            _providerId = providerId;
            _shouldThrow = shouldThrow;
            _failure = failure;
        }

        public string ProviderId => _providerId;

        public int ProgressUpdateCallCount { get; private set; }

        public int StatusUpdateCallCount { get; private set; }

        public int LibraryStateSyncCallCount { get; private set; }

        public MediaLibraryStateSyncRequest? LastLibraryStateSyncRequest { get; private set; }

        public Task<ConnectedServiceAccount?> GetConnectedAccountAsync(int userId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
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

        public Task<MediaProviderMutationResult> UpdateProgressAsync(int userId, MediaProgressUpdateRequest request, CancellationToken cancellationToken)
        {
            ProgressUpdateCallCount += 1;

            if (_failure is not null) throw _failure;
            if (_shouldThrow)
            {
                throw new InvalidOperationException("Simulated provider write failure.");
            }

            return Task.FromResult(new MediaProviderMutationResult
            {
                ProviderId = _providerId,
                ProviderMediaId = request.ProviderMediaId,
                AppliedAt = DateTimeOffset.UtcNow,
                LastRemoteUpdateAt = DateTimeOffset.UtcNow
            });
        }

        public Task<MediaProviderMutationResult> UpdateStatusAsync(int userId, MediaStatusUpdateRequest request, CancellationToken cancellationToken)
        {
            StatusUpdateCallCount += 1;

            if (_failure is not null) throw _failure;
            if (_shouldThrow)
            {
                throw new InvalidOperationException("Simulated provider write failure.");
            }

            return Task.FromResult(new MediaProviderMutationResult
            {
                ProviderId = _providerId,
                ProviderMediaId = request.ProviderMediaId,
                AppliedAt = DateTimeOffset.UtcNow,
                LastRemoteUpdateAt = DateTimeOffset.UtcNow
            });
        }

        public Task<MediaProviderMutationResult> UpdateScoreAsync(int userId, MediaScoreUpdateRequest request, CancellationToken cancellationToken)
        {
            if (_failure is not null) throw _failure;
            if (_shouldThrow)
            {
                throw new InvalidOperationException("Simulated provider write failure.");
            }

            return Task.FromResult(new MediaProviderMutationResult
            {
                ProviderId = "anilist",
                ProviderMediaId = request.ProviderMediaId,
                AppliedAt = DateTimeOffset.UtcNow,
                LastRemoteUpdateAt = DateTimeOffset.UtcNow
            });
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
