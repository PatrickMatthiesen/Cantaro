using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class MediaProviderInitialSyncServiceTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ApplyAsync_TargetNewerOverlap_PreservesCantaroAndQueuesOneCompositeTargetOperation()
    {
        await using var fixture = await InitialSyncFixture.CreateAsync();
        var entry = await fixture.SeedEntryAsync(includeTargetLink: true);
        fixture.Provider.Snapshot = Snapshot(RemoteItem(
            status: MediaLibraryStatuses.Completed,
            score: 90m,
            progressEpisodes: 28,
            remoteUpdatedAt: fixture.Now.AddDays(1)));

        var preview = await fixture.Service.PreviewAsync(
            fixture.User.Id,
            fixture.Provider.ProviderId,
            CancellationToken.None);

        Assert.Equal("ready", preview.Status);
        Assert.Equal(1, preview.WillUpdate);
        Assert.Equal(MediaLibraryStatuses.Current, entry.Status);
        Assert.Equal(80m, entry.Score);
        Assert.Equal(4, entry.ProgressEpisodes);

        var result = await fixture.Service.ApplyAsync(
            fixture.User.Id,
            fixture.Provider.ProviderId,
            Assert.IsType<string>(preview.Fingerprint),
            CancellationToken.None);

        Assert.Equal("queued", result.Status);
        Assert.Equal(1, result.QueuedOperations);
        var operation = await fixture.Db.MediaProviderOperations
            .Include(candidate => candidate.MediaLibraryProviderBinding)
                .ThenInclude(binding => binding!.MediaProviderLink)
            .SingleAsync();
        Assert.Equal(MediaProviderOperationTypes.SyncLibraryState, operation.OperationType);
        Assert.Equal(fixture.Provider.ProviderId, operation.MediaLibraryProviderBinding!.MediaProviderLink!.Provider);
        var request = JsonSerializer.Deserialize<MediaLibraryStateSyncRequest>(
            operation.PayloadJson,
            SerializerOptions);
        Assert.NotNull(request);
        Assert.Equal(MediaLibraryStatuses.Current, request.Status);
        Assert.Equal(80m, request.Score);
        Assert.Equal(4, request.ProgressEpisodes);

        await fixture.Db.Entry(entry).ReloadAsync();
        Assert.Equal(MediaLibraryStatuses.Current, entry.Status);
        Assert.Equal(80m, entry.Score);
        Assert.Equal(4, entry.ProgressEpisodes);
    }

    [Fact]
    public async Task ApplyAsync_KnownTargetLinkAbsentRemotely_CreatesBindingAndCompositeOperation()
    {
        await using var fixture = await InitialSyncFixture.CreateAsync();
        var entry = await fixture.SeedEntryAsync(includeTargetLink: true);
        fixture.Provider.Snapshot = Snapshot();

        var preview = await fixture.Service.PreviewAsync(
            fixture.User.Id,
            fixture.Provider.ProviderId,
            CancellationToken.None);

        Assert.Equal("ready", preview.Status);
        Assert.Equal(1, preview.WillAdd);

        var result = await fixture.Service.ApplyAsync(
            fixture.User.Id,
            fixture.Provider.ProviderId,
            Assert.IsType<string>(preview.Fingerprint),
            CancellationToken.None);

        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.QueuedOperations);
        var binding = await fixture.Db.MediaLibraryProviderBindings
            .Include(candidate => candidate.MediaProviderLink)
            .SingleAsync();
        Assert.Equal(entry.Id, binding.MediaLibraryEntryId);
        Assert.Equal(fixture.Provider.ProviderId, binding.MediaProviderLink!.Provider);
        Assert.Equal(InitialSyncFixture.TargetMediaId, binding.MediaProviderLink.ExternalId);
        var operation = await fixture.Db.MediaProviderOperations.SingleAsync();
        Assert.Equal(MediaProviderOperationTypes.SyncLibraryState, operation.OperationType);
    }

    [Fact]
    public async Task ApplyAsync_RemoteOnlyTarget_RemainsUnpersisted()
    {
        await using var fixture = await InitialSyncFixture.CreateAsync();
        await fixture.SeedEntryAsync(includeTargetLink: false);
        fixture.Provider.Snapshot = Snapshot(RemoteItem());

        var preview = await fixture.Service.PreviewAsync(
            fixture.User.Id,
            fixture.Provider.ProviderId,
            CancellationToken.None);

        Assert.Equal("ready", preview.Status);
        Assert.Equal(1, preview.ProviderOnly);

        var result = await fixture.Service.ApplyAsync(
            fixture.User.Id,
            fixture.Provider.ProviderId,
            Assert.IsType<string>(preview.Fingerprint),
            CancellationToken.None);

        Assert.Equal(1, result.ProviderOnly);
        Assert.Equal(0, result.QueuedOperations);
        Assert.Single(await fixture.Db.MediaTitles.ToListAsync());
        Assert.Single(await fixture.Db.MediaLibraryEntries.ToListAsync());
        Assert.DoesNotContain(
            await fixture.Db.MediaProviderLinks.ToListAsync(),
            link => link.Provider == fixture.Provider.ProviderId);
        Assert.Empty(await fixture.Db.MediaLibraryProviderBindings.ToListAsync());
        Assert.Empty(await fixture.Db.MediaProviderOperations.ToListAsync());
    }

    [Fact]
    public async Task ApplyAsync_UnresolvedCantaroTitle_IsReportedWithoutBindingOrOperation()
    {
        await using var fixture = await InitialSyncFixture.CreateAsync();
        var entry = await fixture.SeedEntryAsync(includeTargetLink: false);
        fixture.Provider.Snapshot = Snapshot();

        var preview = await fixture.Service.PreviewAsync(
            fixture.User.Id,
            fixture.Provider.ProviderId,
            CancellationToken.None);

        Assert.Equal("ready", preview.Status);
        Assert.Equal(1, preview.NeedsMatching);
        var unresolved = Assert.Single(preview.UnresolvedTitles);
        Assert.Equal(entry.MediaTitleId, unresolved.MediaTitleId);

        var result = await fixture.Service.ApplyAsync(
            fixture.User.Id,
            fixture.Provider.ProviderId,
            Assert.IsType<string>(preview.Fingerprint),
            CancellationToken.None);

        Assert.Equal(1, result.NeedsMatching);
        Assert.Equal(0, result.QueuedOperations);
        Assert.Empty(await fixture.Db.MediaLibraryProviderBindings.ToListAsync());
        Assert.Empty(await fixture.Db.MediaProviderOperations.ToListAsync());
        Assert.DoesNotContain(
            await fixture.Db.MediaProviderLinks.ToListAsync(),
            link => link.Provider == fixture.Provider.ProviderId);
    }

    [Fact]
    public async Task ApplyAsync_WhenCanonicalStateChangedAfterPreview_RejectsFingerprint()
    {
        await using var fixture = await InitialSyncFixture.CreateAsync();
        var entry = await fixture.SeedEntryAsync(includeTargetLink: true);
        fixture.Provider.Snapshot = Snapshot(RemoteItem(
            status: entry.Status,
            score: entry.Score,
            progressEpisodes: entry.ProgressEpisodes));
        var preview = await fixture.Service.PreviewAsync(
            fixture.User.Id,
            fixture.Provider.ProviderId,
            CancellationToken.None);

        entry.ProgressEpisodes = 5;
        entry.UpdatedAt = fixture.Now.AddMinutes(1);
        await fixture.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<MediaProviderInitialSyncChangedException>(() =>
            fixture.Service.ApplyAsync(
                fixture.User.Id,
                fixture.Provider.ProviderId,
                Assert.IsType<string>(preview.Fingerprint),
                CancellationToken.None));

        Assert.Empty(await fixture.Db.MediaLibraryProviderBindings.ToListAsync());
        Assert.Empty(await fixture.Db.MediaProviderOperations.ToListAsync());
    }

    [Fact]
    public async Task PreviewAsync_WithActiveOperation_ReturnsSettlingWithoutReadingTarget()
    {
        await using var fixture = await InitialSyncFixture.CreateAsync();
        var entry = await fixture.SeedEntryAsync(includeTargetLink: true);
        var link = await fixture.Db.MediaProviderLinks.SingleAsync(
            candidate => candidate.Provider == fixture.Provider.ProviderId);
        var binding = new MediaLibraryProviderBinding
        {
            Id = Guid.NewGuid(),
            MediaLibraryEntryId = entry.Id,
            MediaProviderLinkId = link.Id,
            ConnectedServiceAccountId = fixture.Account.Id,
            ProviderAccountId = fixture.Account.ExternalAccountId,
            CreatedAt = fixture.Now,
            UpdatedAt = fixture.Now
        };
        fixture.Db.MediaLibraryProviderBindings.Add(binding);
        fixture.Db.MediaProviderOperations.Add(new MediaProviderOperation
        {
            Id = Guid.NewGuid(),
            MediaLibraryProviderBindingId = binding.Id,
            OperationType = MediaProviderOperationTypes.SyncLibraryState,
            PayloadJson = JsonSerializer.Serialize(new MediaLibraryStateSyncRequest
            {
                ProviderMediaId = InitialSyncFixture.TargetMediaId,
                Status = entry.Status,
                Score = entry.Score,
                ProgressEpisodes = entry.ProgressEpisodes
            }, SerializerOptions),
            Status = MediaProviderOperationStatuses.Pending,
            AttemptCount = 0,
            NextAttemptAt = fixture.Now,
            CreatedAt = fixture.Now,
            UpdatedAt = fixture.Now
        });
        await fixture.Db.SaveChangesAsync();

        var preview = await fixture.Service.PreviewAsync(
            fixture.User.Id,
            fixture.Provider.ProviderId,
            CancellationToken.None);

        Assert.Equal("settling", preview.Status);
        Assert.Equal(1, preview.PendingOperations);
        Assert.Equal(0, fixture.Provider.ImportCallCount);
        Assert.Null(preview.Fingerprint);
    }

    [Fact]
    public async Task PreviewAsync_RefreshesExistingProviderBeforeTargetAndPersistsNewerState()
    {
        await using var fixture = await InitialSyncFixture.CreateAsync(includeExistingProvider: true);
        var entry = await fixture.SeedEntryAsync(
            includeTargetLink: true,
            includeExistingLink: true);
        var newerRemoteAt = fixture.Now.AddHours(1);
        fixture.ExistingProvider!.Snapshot = SnapshotFor(
            InitialSyncFixture.ExistingProviderId,
            RemoteItem(
                providerMediaId: InitialSyncFixture.ExistingMediaId,
                status: MediaLibraryStatuses.Completed,
                score: 90m,
                progressEpisodes: 28,
                remoteUpdatedAt: newerRemoteAt));
        fixture.Provider.Snapshot = Snapshot(RemoteItem(
            status: MediaLibraryStatuses.Completed,
            score: 90m,
            progressEpisodes: 28,
            remoteUpdatedAt: newerRemoteAt));

        var preview = await fixture.Service.PreviewAsync(
            fixture.User.Id,
            fixture.Provider.ProviderId,
            CancellationToken.None);

        Assert.Equal("ready", preview.Status);
        Assert.Equal([InitialSyncFixture.ExistingProviderId], preview.RefreshedProviderIds);
        Assert.Equal(
            [InitialSyncFixture.ExistingProviderId, InitialSyncFixture.TargetProviderId],
            fixture.ImportCallOrder);
        Assert.Equal(1, preview.AlreadyAligned);
        await fixture.Db.Entry(entry).ReloadAsync();
        Assert.Equal(MediaLibraryStatuses.Completed, entry.Status);
        Assert.Equal(90m, entry.Score);
        Assert.Equal(28, entry.ProgressEpisodes);
        Assert.Empty(await fixture.Db.MediaProviderOperations.ToListAsync());
    }

    [Fact]
    public async Task PreviewAsync_WhenExistingProviderNeedsReconciliation_ReturnsSettlingWithoutReadingTarget()
    {
        await using var fixture = await InitialSyncFixture.CreateAsync(includeExistingProvider: true);
        var entry = await fixture.SeedEntryAsync(
            includeTargetLink: true,
            includeExistingLink: true);
        await fixture.SeedExistingBindingAsync(entry);
        fixture.ExistingProvider!.Snapshot = SnapshotFor(
            InitialSyncFixture.ExistingProviderId,
            RemoteItem(
                providerMediaId: InitialSyncFixture.ExistingMediaId,
                status: MediaLibraryStatuses.Completed,
                score: 60m,
                progressEpisodes: 2,
                remoteUpdatedAt: fixture.Now.AddHours(-1)));
        fixture.Provider.Snapshot = Snapshot(RemoteItem());

        var preview = await fixture.Service.PreviewAsync(
            fixture.User.Id,
            fixture.Provider.ProviderId,
            CancellationToken.None);

        Assert.Equal("settling", preview.Status);
        Assert.Equal(1, preview.PendingOperations);
        Assert.Equal([InitialSyncFixture.ExistingProviderId], fixture.ImportCallOrder);
        Assert.Equal(0, fixture.Provider.ImportCallCount);
        var operation = await fixture.Db.MediaProviderOperations
            .Include(candidate => candidate.MediaLibraryProviderBinding)
                .ThenInclude(binding => binding!.MediaProviderLink)
            .SingleAsync();
        Assert.Equal(MediaProviderOperationTypes.SyncLibraryState, operation.OperationType);
        Assert.Equal(
            InitialSyncFixture.ExistingProviderId,
            operation.MediaLibraryProviderBinding!.MediaProviderLink!.Provider);
        await fixture.Db.Entry(entry).ReloadAsync();
        Assert.Equal(MediaLibraryStatuses.Current, entry.Status);
        Assert.Equal(80m, entry.Score);
        Assert.Equal(4, entry.ProgressEpisodes);
    }

    [Fact]
    public async Task PreviewAsync_MalProjectedScoreMatchesRemoteTenPointScale()
    {
        await using var fixture = await InitialSyncFixture.CreateAsync();
        var entry = await fixture.SeedEntryAsync(includeTargetLink: true);
        entry.Score = 85m;
        await fixture.Db.SaveChangesAsync();
        fixture.Provider.Snapshot = Snapshot(RemoteItem(score: 90m));

        var preview = await fixture.Service.PreviewAsync(
            fixture.User.Id,
            fixture.Provider.ProviderId,
            CancellationToken.None);

        Assert.Equal("ready", preview.Status);
        Assert.Equal(0, preview.WillUpdate);
        Assert.Equal(1, preview.AlreadyAligned);
        Assert.Empty(await fixture.Db.MediaProviderOperations.ToListAsync());
    }

    [Fact]
    public async Task PreviewAsync_EmptyCantaroLibrary_RequiresInboundImport()
    {
        await using var fixture = await InitialSyncFixture.CreateAsync();
        fixture.Provider.Snapshot = Snapshot(RemoteItem());

        var preview = await fixture.Service.PreviewAsync(
            fixture.User.Id,
            fixture.Provider.ProviderId,
            CancellationToken.None);

        Assert.Equal("import-required", preview.Status);
        Assert.Equal(1, preview.ProviderOnly);
        Assert.Null(preview.Fingerprint);
        Assert.Empty(await fixture.Db.MediaTitles.ToListAsync());
    }

    [Fact]
    public async Task ApplyAsync_HistoricalFailure_DoesNotBlockFreshAttempt()
    {
        await using var fixture = await InitialSyncFixture.CreateAsync();
        var entry = await fixture.SeedEntryAsync(includeTargetLink: true);
        var binding = await fixture.SeedTargetBindingAsync(entry);
        fixture.Db.MediaProviderOperations.Add(new MediaProviderOperation
        {
            Id = Guid.NewGuid(),
            MediaLibraryProviderBindingId = binding.Id,
            OperationType = MediaProviderOperationTypes.SyncLibraryState,
            PayloadJson = "{}",
            Status = MediaProviderOperationStatuses.Failed,
            AttemptCount = 4,
            LastError = "Earlier failure",
            CreatedAt = fixture.Now.AddHours(-1),
            UpdatedAt = fixture.Now.AddHours(-1)
        });
        await fixture.Db.SaveChangesAsync();
        fixture.Provider.Snapshot = Snapshot(RemoteItem(progressEpisodes: 1));

        var preview = await fixture.Service.PreviewAsync(
            fixture.User.Id,
            fixture.Provider.ProviderId,
            CancellationToken.None);
        var result = await fixture.Service.ApplyAsync(
            fixture.User.Id,
            fixture.Provider.ProviderId,
            Assert.IsType<string>(preview.Fingerprint),
            CancellationToken.None);

        Assert.Equal("ready", preview.Status);
        Assert.Equal(1, result.QueuedOperations);
        Assert.Equal(2, await fixture.Db.MediaProviderOperations.CountAsync());
        Assert.Single(await fixture.Db.MediaProviderOperations
            .Where(operation => operation.Status == MediaProviderOperationStatuses.Pending)
            .ToListAsync());
    }

    [Fact]
    public async Task GetProgressAsync_TracksOnlyTheAppliedBatch()
    {
        await using var fixture = await InitialSyncFixture.CreateAsync();
        await fixture.SeedEntryAsync(includeTargetLink: true);
        fixture.Provider.Snapshot = Snapshot(RemoteItem(progressEpisodes: 1));
        var preview = await fixture.Service.PreviewAsync(
            fixture.User.Id,
            fixture.Provider.ProviderId,
            CancellationToken.None);
        var result = await fixture.Service.ApplyAsync(
            fixture.User.Id,
            fixture.Provider.ProviderId,
            Assert.IsType<string>(preview.Fingerprint),
            CancellationToken.None);
        var batchId = Assert.IsType<Guid>(result.BatchId);

        var running = await fixture.Service.GetProgressAsync(
            fixture.User.Id,
            fixture.Provider.ProviderId,
            batchId,
            CancellationToken.None);
        Assert.Equal("running", running.Status);

        var operation = await fixture.Db.MediaProviderOperations.SingleAsync();
        operation.Status = MediaProviderOperationStatuses.Failed;
        await fixture.Db.SaveChangesAsync();
        var failed = await fixture.Service.GetProgressAsync(
            fixture.User.Id,
            fixture.Provider.ProviderId,
            batchId,
            CancellationToken.None);
        Assert.Equal("failed", failed.Status);
        Assert.Equal(1, failed.FailedOperations);
    }

    [Fact]
    public async Task ApplyAsync_ConcurrentRequests_QueuesOnlyOneBatch()
    {
        await using var fixture = await InitialSyncFixture.CreateAsync();
        await fixture.SeedEntryAsync(includeTargetLink: true);
        fixture.Provider.Snapshot = Snapshot(RemoteItem(progressEpisodes: 1));
        var preview = await fixture.Service.PreviewAsync(
            fixture.User.Id,
            fixture.Provider.ProviderId,
            CancellationToken.None);
        var fingerprint = Assert.IsType<string>(preview.Fingerprint);

        async Task<string> ApplyOnceAsync()
        {
            try
            {
                await fixture.Service.ApplyAsync(
                    fixture.User.Id,
                    fixture.Provider.ProviderId,
                    fingerprint,
                    CancellationToken.None);
                return "queued";
            }
            catch (MediaProviderInitialSyncNotReadyException)
            {
                return "not-ready";
            }
        }

        var outcomes = await Task.WhenAll(ApplyOnceAsync(), ApplyOnceAsync());

        Assert.Equal(1, outcomes.Count(outcome => outcome == "queued"));
        Assert.Equal(1, outcomes.Count(outcome => outcome == "not-ready"));
        Assert.Single(await fixture.Db.MediaProviderOperations.ToListAsync());
    }

    private static MediaProviderLibraryImportResult Snapshot(params MediaProviderLibraryItem[] items)
        => SnapshotFor(InitialSyncFixture.TargetProviderId, items);

    private static MediaProviderLibraryImportResult SnapshotFor(
        string providerId,
        params MediaProviderLibraryItem[] items)
        => new()
        {
            ProviderId = providerId,
            ImportedAt = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero),
            Items = items
        };

    private static MediaProviderLibraryItem RemoteItem(
        string providerMediaId = InitialSyncFixture.TargetMediaId,
        string status = MediaLibraryStatuses.Current,
        decimal? score = 80m,
        int? progressEpisodes = 4,
        DateTimeOffset? remoteUpdatedAt = null)
        => new()
        {
            ProviderMediaId = providerMediaId,
            ProviderLibraryEntryId = "target-list-entry",
            Title = "Frieren: Beyond Journey's End",
            MediaKind = MediaKinds.Anime,
            EpisodeCount = 28,
            Status = status,
            Score = score,
            ProgressEpisodes = progressEpisodes,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            LastRemoteUpdateAt = remoteUpdatedAt
                ?? new DateTimeOffset(2026, 8, 24, 11, 0, 0, TimeSpan.Zero)
        };

    private sealed class InitialSyncFixture : IAsyncDisposable
    {
        public const string TargetProviderId = "myanimelist";
        public const string TargetMediaId = "anime:52991";
        public const string ExistingProviderId = "anilist";
        public const string ExistingMediaId = "154587";

        private readonly SqliteConnection _connection;

        private InitialSyncFixture(
            SqliteConnection connection,
            ApplicationDbContext db,
            User user,
            ConnectedServiceAccount account,
            FakeMediaProvider provider,
            ConnectedServiceAccount? existingAccount,
            FakeMediaProvider? existingProvider,
            MediaProviderInitialSyncService service,
            List<string> importCallOrder,
            DateTimeOffset now)
        {
            _connection = connection;
            Db = db;
            User = user;
            Account = account;
            Provider = provider;
            ExistingAccount = existingAccount;
            ExistingProvider = existingProvider;
            Service = service;
            ImportCallOrder = importCallOrder;
            Now = now;
        }

        public ApplicationDbContext Db { get; }
        public User User { get; }
        public ConnectedServiceAccount Account { get; }
        public FakeMediaProvider Provider { get; }
        public ConnectedServiceAccount? ExistingAccount { get; }
        public FakeMediaProvider? ExistingProvider { get; }
        public MediaProviderInitialSyncService Service { get; }
        public List<string> ImportCallOrder { get; }
        public DateTimeOffset Now { get; }

        public static async Task<InitialSyncFixture> CreateAsync(bool includeExistingProvider = false)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options);
            await db.Database.EnsureCreatedAsync();
            var now = new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero);
            var user = TestUserFactory.Create(941, "initial-sync@example.test");
            var account = new ConnectedServiceAccount
            {
                Id = 1941,
                UserId = user.Id,
                Service = TargetProviderId,
                ExternalAccountId = "target-viewer",
                CreatedAt = now.UtcDateTime,
                UpdatedAt = now.UtcDateTime
            };
            ConnectedServiceAccount? existingAccount = null;
            if (includeExistingProvider)
            {
                existingAccount = new ConnectedServiceAccount
                {
                    Id = 1942,
                    UserId = user.Id,
                    Service = ExistingProviderId,
                    ExternalAccountId = "existing-viewer",
                    CreatedAt = now.UtcDateTime,
                    UpdatedAt = now.UtcDateTime
                };
            }
            db.AddRange(user, account);
            if (existingAccount is not null)
            {
                db.ConnectedServiceAccounts.Add(existingAccount);
            }
            await db.SaveChangesAsync();

            var importCallOrder = new List<string>();
            var provider = new FakeMediaProvider(account, TargetProviderId, importCallOrder)
            {
                Snapshot = Snapshot()
            };
            FakeMediaProvider? existingProvider = null;
            if (existingAccount is not null)
            {
                existingProvider = new FakeMediaProvider(
                    existingAccount,
                    ExistingProviderId,
                    importCallOrder)
                {
                    Snapshot = SnapshotFor(ExistingProviderId)
                };
            }
            var providers = existingProvider is null
                ? [provider]
                : new FakeMediaProvider[] { provider, existingProvider };
            var registry = new MediaProviderRegistry(providers);
            var importService = new MediaLibraryImportService(
                db,
                new MediaLibraryEventHub(),
                NullLogger<MediaLibraryImportService>.Instance);
            var service = new MediaProviderInitialSyncService(
                db,
                registry,
                importService,
                new MediaProviderInitialSyncGate(),
                NullLogger<MediaProviderInitialSyncService>.Instance);
            return new InitialSyncFixture(
                connection,
                db,
                user,
                account,
                provider,
                existingAccount,
                existingProvider,
                service,
                importCallOrder,
                now);
        }

        public async Task<MediaLibraryEntry> SeedEntryAsync(
            bool includeTargetLink,
            bool includeExistingLink = false)
        {
            var title = new MediaTitle
            {
                Id = Guid.NewGuid(),
                CanonicalTitle = "Frieren: Beyond Journey's End",
                MediaKind = MediaKinds.Anime,
                EpisodeCount = 28,
                SupportsEpisodeProgress = true,
                PrimaryProgressDimension = MediaProgressDimensions.Episode,
                ReleaseStatusDimension = MediaProgressDimensions.Episode,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            var entry = new MediaLibraryEntry
            {
                Id = Guid.NewGuid(),
                UserId = User.Id,
                MediaTitleId = title.Id,
                Status = MediaLibraryStatuses.Current,
                Score = 80m,
                ProgressEpisodes = 4,
                LastLocalEditAt = Now,
                LastMutationSource = MediaMutationSources.UserProgressUpdate,
                CreatedAt = Now,
                UpdatedAt = Now,
                MediaTitle = title
            };
            if (includeTargetLink)
            {
                AddProviderLink(title, TargetProviderId, TargetMediaId);
            }
            if (!includeTargetLink || includeExistingLink)
            {
                AddProviderLink(title, ExistingProviderId, ExistingMediaId);
            }
            title.LibraryEntries.Add(entry);
            Db.AddRange(title, entry);
            await Db.SaveChangesAsync();
            return entry;
        }

        public async Task<MediaLibraryProviderBinding> SeedExistingBindingAsync(MediaLibraryEntry entry)
        {
            var account = ExistingAccount
                ?? throw new InvalidOperationException("The fixture has no existing provider account.");
            var link = await Db.MediaProviderLinks.SingleAsync(candidate =>
                candidate.MediaTitleId == entry.MediaTitleId
                && candidate.Provider == ExistingProviderId);
            var binding = new MediaLibraryProviderBinding
            {
                Id = Guid.NewGuid(),
                MediaLibraryEntryId = entry.Id,
                MediaProviderLinkId = link.Id,
                ConnectedServiceAccountId = account.Id,
                ProviderAccountId = account.ExternalAccountId,
                LastRemoteUpdateAt = Now.AddHours(-2),
                CreatedAt = Now,
                UpdatedAt = Now
            };
            Db.MediaLibraryProviderBindings.Add(binding);
            await Db.SaveChangesAsync();
            return binding;
        }

        public async Task<MediaLibraryProviderBinding> SeedTargetBindingAsync(MediaLibraryEntry entry)
        {
            var link = await Db.MediaProviderLinks.SingleAsync(candidate =>
                candidate.MediaTitleId == entry.MediaTitleId
                && candidate.Provider == TargetProviderId);
            var binding = new MediaLibraryProviderBinding
            {
                Id = Guid.NewGuid(),
                MediaLibraryEntryId = entry.Id,
                MediaProviderLinkId = link.Id,
                ConnectedServiceAccountId = Account.Id,
                ProviderAccountId = Account.ExternalAccountId,
                LastRemoteUpdateAt = Now.AddHours(-2),
                CreatedAt = Now,
                UpdatedAt = Now
            };
            Db.MediaLibraryProviderBindings.Add(binding);
            await Db.SaveChangesAsync();
            return binding;
        }

        private void AddProviderLink(MediaTitle title, string providerId, string providerMediaId)
        {
            title.ProviderLinks.Add(new MediaProviderLink
            {
                Id = Guid.NewGuid(),
                MediaTitleId = title.Id,
                Provider = providerId,
                ExternalId = providerMediaId,
                LinkSource = MediaMappingSources.Imported,
                CreatedAt = Now,
                UpdatedAt = Now,
                MediaTitle = title
            });
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class FakeMediaProvider(
        ConnectedServiceAccount account,
        string providerId,
        List<string> importCallOrder) : IMediaProvider
    {
        private readonly ConnectedServiceAccount _account = account;
        private readonly string _providerId = providerId;
        private readonly List<string> _importCallOrder = importCallOrder;

        public string ProviderId => _providerId;
        public required MediaProviderLibraryImportResult Snapshot { get; set; }
        public int ImportCallCount { get; private set; }

        public Task<ConnectedServiceAccount?> GetConnectedAccountAsync(
            int userId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<ConnectedServiceAccount?>(userId == _account.UserId ? _account : null);

        public string GetAuthorizationUrl(string redirectUri, string state, string codeChallenge)
            => throw new NotSupportedException();

        public Task<ConnectedServiceAccount> ExchangeCodeAndSaveAsync(
            int userId,
            string authorizationCode,
            string redirectUri,
            string codeVerifier,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task DisconnectAsync(int userId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<MediaProviderLibraryImportResult> ImportLibraryAsync(
            int userId,
            CancellationToken cancellationToken)
        {
            ImportCallCount++;
            _importCallOrder.Add(ProviderId);
            return Task.FromResult(Snapshot);
        }

        public Task<IReadOnlyList<MediaProviderSearchResult>> SearchAsync(
            int userId,
            MediaCatalogSearchRequest request,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<MediaProviderTitleDetails?> GetTitleDetailsAsync(
            int userId,
            string providerMediaId,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<MediaProviderMutationResult> UpdateProgressAsync(
            int userId,
            MediaProgressUpdateRequest request,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<MediaProviderMutationResult> UpdateStatusAsync(
            int userId,
            MediaStatusUpdateRequest request,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<MediaProviderMutationResult> UpdateScoreAsync(
            int userId,
            MediaScoreUpdateRequest request,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<MediaReleaseMetadata?> GetReleaseMetadataAsync(
            int userId,
            string providerMediaId,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
