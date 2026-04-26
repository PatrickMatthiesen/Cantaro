using System.Data.Common;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

        var entry = await SeedMediaEntryAsync(dbContext, 401, "success@example.com");
        var registry = new MediaProviderRegistry([new FakeMediaProvider("anilist")]);
        var processor = new MediaProviderOperationProcessor(dbContext, registry, NullLogger<MediaProviderOperationProcessor>.Instance);

        var operation = await processor.EnqueueProgressUpdateAsync(
            entry.UserId,
            entry,
            new MediaProgressUpdateRequest
            {
                ProviderMediaId = entry.ProviderMediaId,
                ProgressEpisodes = 17,
                LastKnownRemoteUpdateAt = entry.LastRemoteUpdateAt
            },
            CancellationToken.None);

        var processed = await processor.ProcessOperationAsync(operation.Id, CancellationToken.None);
        var persistedEntry = await dbContext.MediaLibraryEntries.SingleAsync();

        Assert.Equal(MediaProviderOperationExecutionOutcome.Succeeded, processed.Outcome);
        Assert.Equal(17, persistedEntry.ProgressEpisodes);
        Assert.Equal(MediaMutationSources.UserProgressUpdate, persistedEntry.LastMutationSource);
        Assert.NotNull(persistedEntry.LastLocalEditAt);
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

        var entry = await SeedMediaEntryAsync(dbContext, 402, "retry@example.com");
        var registry = new MediaProviderRegistry([new FakeMediaProvider("anilist", shouldThrow: true)]);
        var processor = new MediaProviderOperationProcessor(dbContext, registry, NullLogger<MediaProviderOperationProcessor>.Instance);

        var operation = await processor.EnqueueStatusUpdateAsync(
            entry.UserId,
            entry,
            new MediaStatusUpdateRequest
            {
                ProviderMediaId = entry.ProviderMediaId,
                Status = MediaLibraryStatuses.Completed,
                LastKnownRemoteUpdateAt = entry.LastRemoteUpdateAt
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

            var entry = await SeedMediaEntryAsync(setupContext, 403, "claim@example.com");
            var enqueueProcessor = new MediaProviderOperationProcessor(
                setupContext,
                new MediaProviderRegistry([new FakeMediaProvider("anilist")]),
                NullLogger<MediaProviderOperationProcessor>.Instance);

            var operation = await enqueueProcessor.EnqueueProgressUpdateAsync(
                entry.UserId,
                entry,
                new MediaProgressUpdateRequest
                {
                    ProviderMediaId = entry.ProviderMediaId,
                    ProgressEpisodes = 12,
                    LastKnownRemoteUpdateAt = entry.LastRemoteUpdateAt
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

    private static async Task<MediaLibraryEntry> SeedMediaEntryAsync(ApplicationDbContext dbContext, int userId, string email)
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
            ConnectedServiceAccountId = account.Id,
            Provider = "anilist",
            ProviderAccountId = account.ExternalAccountId,
            ProviderMediaId = "140960",
            NormalizedStatus = MediaLibraryStatuses.Current,
            ProgressEpisodes = 10,
            LastRemoteUpdateAt = now.AddMinutes(-2),
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.Users.Add(user);
        dbContext.ConnectedServiceAccounts.Add(account);
        dbContext.MediaTitles.Add(title);
        dbContext.MediaLibraryEntries.Add(entry);
        await dbContext.SaveChangesAsync();

        return entry;
    }

    private sealed class FakeMediaProvider : IMediaProvider
    {
        private readonly string _providerId;
        private readonly bool _shouldThrow;

        public FakeMediaProvider(string providerId, bool shouldThrow = false)
        {
            _providerId = providerId;
            _shouldThrow = shouldThrow;
        }

        public string ProviderId => _providerId;

        public int ProgressUpdateCallCount { get; private set; }

        public int StatusUpdateCallCount { get; private set; }

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

            if (_shouldThrow)
            {
                throw new InvalidOperationException("Simulated provider write failure.");
            }

            return Task.FromResult(new MediaProviderMutationResult
            {
                ProviderId = _providerId,
                ProviderMediaId = request.ProviderMediaId,
                AppliedAt = DateTimeOffset.UtcNow,
                LastRemoteUpdateAt = DateTimeOffset.UtcNow,
                RawStatus = "CURRENT",
                RawMetadata = "{\"status\":\"CURRENT\"}"
            });
        }

        public Task<MediaProviderMutationResult> UpdateStatusAsync(int userId, MediaStatusUpdateRequest request, CancellationToken cancellationToken)
        {
            StatusUpdateCallCount += 1;

            if (_shouldThrow)
            {
                throw new InvalidOperationException("Simulated provider write failure.");
            }

            return Task.FromResult(new MediaProviderMutationResult
            {
                ProviderId = _providerId,
                ProviderMediaId = request.ProviderMediaId,
                AppliedAt = DateTimeOffset.UtcNow,
                LastRemoteUpdateAt = DateTimeOffset.UtcNow,
                RawStatus = request.Status.ToUpperInvariant(),
                RawMetadata = $"{{\"status\":\"{request.Status}\"}}"
            });
        }

        public Task<MediaReleaseMetadata?> GetReleaseMetadataAsync(int userId, string providerMediaId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
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
