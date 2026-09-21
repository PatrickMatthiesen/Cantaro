using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantaro.Api.Tests;

public class MediaLibraryImportServiceTests
{
    [Fact]
    public async Task ImportAsync_CreatesCanonicalTitleViewerStateAndProviderBinding()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var (user, account) = await SeedAccountAsync(db, 201);
        var now = DateTimeOffset.UtcNow;

        var result = await MakeService(db).ImportAsync(
            user.Id,
            account,
            MakeImport(now, progress: 12),
            CancellationToken.None);

        Assert.Equal(1, result.CreatedTitles);
        Assert.Equal(1, result.CreatedEntries);
        var title = await db.MediaTitles.SingleAsync();
        var link = await db.MediaProviderLinks.SingleAsync();
        var entry = await db.MediaLibraryEntries.SingleAsync();
        var binding = await db.MediaLibraryProviderBindings
            .Include(item => item.ProviderListMemberships)
            .SingleAsync();
        Assert.Equal("Frieren", title.CanonicalTitle);
        Assert.Equal(["Frieren: Beyond Journey's End", "Sousou no Frieren"], title.Synonyms);
        Assert.Equal("TV", title.Format);
        Assert.Equal("https://example.test/poster.jpg", title.PosterUrl);
        Assert.Equal(title.Id, link.MediaTitleId);
        Assert.Equal(title.Id, entry.MediaTitleId);
        Assert.Equal(12, entry.ProgressEpisodes);
        Assert.Equal(87.5m, entry.Score);
        Assert.Equal(entry.Id, binding.MediaLibraryEntryId);
        Assert.Equal(link.Id, binding.MediaProviderLinkId);
        Assert.Equal(account.Id, binding.ConnectedServiceAccountId);
        Assert.Equal(["Favorites"], binding.ProviderListMemberships.Select(item => item.Name));
    }

    [Fact]
    public async Task ImportAsync_ReusesGlobalTitleForDifferentUsers()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var first = await SeedAccountAsync(db, 202);
        var second = await SeedAccountAsync(db, 203);
        var service = MakeService(db);

        await service.ImportAsync(first.User.Id, first.Account, MakeImport(DateTimeOffset.UtcNow, 4), CancellationToken.None);
        await service.ImportAsync(second.User.Id, second.Account, MakeImport(DateTimeOffset.UtcNow.AddMinutes(1), 9), CancellationToken.None);

        Assert.Equal(1, await db.MediaTitles.CountAsync());
        Assert.Equal(1, await db.MediaProviderLinks.CountAsync());
        Assert.Equal(2, await db.MediaLibraryEntries.CountAsync());
        Assert.Equal(2, await db.MediaLibraryProviderBindings.CountAsync());
        Assert.Equal([4, 9], await db.MediaLibraryEntries.OrderBy(item => item.UserId)
            .Select(item => item.ProgressEpisodes!.Value).ToListAsync());
    }

    [Fact]
    public async Task ImportAsync_DoesNotOverwriteViewerProgressWithOlderRemoteState()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var (user, account) = await SeedAccountAsync(db, 204);
        var service = MakeService(db);
        var now = DateTimeOffset.UtcNow;
        await service.ImportAsync(user.Id, account, MakeImport(now, 10, now), CancellationToken.None);

        var entry = await db.MediaLibraryEntries.SingleAsync();
        entry.ProgressEpisodes = 11;
        entry.LastLocalEditAt = now.AddMinutes(2);
        await db.SaveChangesAsync();

        await service.ImportAsync(
            user.Id,
            account,
            MakeImport(now.AddMinutes(3), 5, now.AddMinutes(-1)),
            CancellationToken.None);

        Assert.Equal(11, (await db.MediaLibraryEntries.SingleAsync()).ProgressEpisodes);
        var binding = await db.MediaLibraryProviderBindings.SingleAsync();
        Assert.Equal(now.AddMinutes(-1), binding.LastRemoteUpdateAt);
    }

    [Fact]
    public async Task ImportAsync_ClampsImpossibleRemoteEpisodeProgressToKnownTotal()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var (user, account) = await SeedAccountAsync(db, 205);
        var now = DateTimeOffset.UtcNow;

        await MakeService(db).ImportAsync(
            user.Id,
            account,
            MakeImport(now, progress: 35),
            CancellationToken.None);

        var entry = await db.MediaLibraryEntries.SingleAsync();
        Assert.Equal(28, entry.ProgressEpisodes);
        Assert.Equal(28, (await db.MediaTitles.SingleAsync()).TotalKnownCount);
    }

    [Fact]
    public async Task ImportAsync_NonContiguousRemoteProgress_DoesNotFanOutAggregateProgress()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var (user, sourceAccount) = await SeedAccountAsync(db, 206);
        var service = MakeService(db);
        var initialImportAt = DateTimeOffset.UtcNow;
        await service.ImportAsync(
            user.Id,
            sourceAccount,
            MakeImport(initialImportAt, progress: 10, remoteUpdatedAt: initialImportAt),
            CancellationToken.None);

        var entry = await db.MediaLibraryEntries.SingleAsync();
        var title = await db.MediaTitles.SingleAsync();
        var targetAccount = new ConnectedServiceAccount
        {
            Id = 1206,
            UserId = user.Id,
            Service = "myanimelist",
            ExternalAccountId = "viewer-206-target",
            CreatedAt = initialImportAt.UtcDateTime,
            UpdatedAt = initialImportAt.UtcDateTime
        };
        var targetLink = new MediaProviderLink
        {
            Id = Guid.NewGuid(),
            MediaTitleId = title.Id,
            Provider = "myanimelist",
            ExternalId = "anime:52991",
            LinkSource = MediaMappingSources.Imported,
            CreatedAt = initialImportAt,
            UpdatedAt = initialImportAt
        };
        var targetBinding = new MediaLibraryProviderBinding
        {
            Id = Guid.NewGuid(),
            MediaLibraryEntryId = entry.Id,
            MediaProviderLinkId = targetLink.Id,
            ConnectedServiceAccountId = targetAccount.Id,
            ProviderAccountId = targetAccount.ExternalAccountId,
            LastRemoteUpdateAt = initialImportAt,
            CreatedAt = initialImportAt,
            UpdatedAt = initialImportAt
        };
        db.AddRange(targetAccount, targetLink, targetBinding);
        await db.SaveChangesAsync();

        var newerImportAt = initialImportAt.AddMinutes(2);
        await service.ImportAsync(
            user.Id,
            sourceAccount,
            MakeImport(
                newerImportAt,
                progress: 15,
                remoteUpdatedAt: newerImportAt,
                hasNonContiguousProgress: true),
            CancellationToken.None);

        Assert.Equal(15, (await db.MediaLibraryEntries.SingleAsync()).ProgressEpisodes);
        Assert.Empty(await db.MediaProviderOperations.ToListAsync());
    }

    [Fact]
    public async Task ImportAsync_FirstSimklImport_PreservesCompletedAniListHistoryDespiteNewerActivityTime()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var (user, anilistAccount) = await SeedAccountAsync(db, 207);
        var service = MakeService(db);
        var now = DateTimeOffset.UtcNow;
        var anilistImport = MakeImport(now.AddDays(-30), progress: 28);
        anilistImport.Items[0].Status = MediaLibraryStatuses.Completed;
        await service.ImportAsync(user.Id, anilistAccount, anilistImport, CancellationToken.None);

        var simklAccount = new ConnectedServiceAccount
        {
            Id = 1207,
            UserId = user.Id,
            Service = "simkl",
            ExternalAccountId = "viewer-207-simkl",
            CreatedAt = now.UtcDateTime,
            UpdatedAt = now.UtcDateTime
        };
        db.ConnectedServiceAccounts.Add(simklAccount);
        await db.SaveChangesAsync();

        // This timestamp can reflect recent account activity, while this title's
        // actual viewing history is old. First import must not trust it as a
        // reason to replace the completed canonical entry.
        var simklImport = MakeImport(now, progress: 5, remoteUpdatedAt: now);
        simklImport.ProviderId = "simkl";
        var simklItem = simklImport.Items[0];
        simklItem.ProviderMediaId = "show-42";
        simklItem.Status = MediaLibraryStatuses.Current;
        simklItem.Score = 20m;
        simklItem.CrossReferences = [new MediaProviderCrossReference
        {
            ProviderId = "anilist",
            ProviderMediaId = "154587"
        }];

        await service.ImportAsync(user.Id, simklAccount, simklImport, CancellationToken.None);

        var entry = await db.MediaLibraryEntries.SingleAsync();
        Assert.Equal(MediaLibraryStatuses.Completed, entry.Status);
        Assert.Equal(28, entry.ProgressEpisodes);
        Assert.Equal(87.5m, entry.Score);
        Assert.Equal(MediaMutationSources.ProviderImport, entry.LastMutationSource);
        Assert.Null(entry.LastLocalEditAt);
        Assert.Equal(2, await db.MediaLibraryProviderBindings.CountAsync());
        Assert.Empty(await db.MediaProviderOperations.ToListAsync());

        simklImport.ImportedAt = now.AddMinutes(10);
        simklItem.LastRemoteUpdateAt = now.AddMinutes(10);
        simklItem.Status = MediaLibraryStatuses.Completed;
        simklItem.ProgressEpisodes = 28;
        simklItem.Score = 95m;
        await service.ImportAsync(user.Id, simklAccount, simklImport, CancellationToken.None);

        Assert.Equal(95m, (await db.MediaLibraryEntries.SingleAsync()).Score);
        var fanOut = await db.MediaProviderOperations.SingleAsync();
        Assert.Equal(MediaProviderOperationTypes.ImportFanOutScore, fanOut.OperationType);
    }

    [Fact]
    public async Task ImportAsync_PreservesNormalizedStatusWhileApplyingNewerScore()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var (user, account) = await SeedAccountAsync(db, 208);
        var service = MakeService(db);
        var now = DateTimeOffset.UtcNow;
        await service.ImportAsync(user.Id, account, MakeImport(now, 12, now, score: 80m), CancellationToken.None);

        var entry = await db.MediaLibraryEntries.SingleAsync();
        var sourceBinding = await db.MediaLibraryProviderBindings.SingleAsync();
        entry.Status = MediaLibraryStatuses.Completed;
        sourceBinding.LastRequestedStatus = MediaLibraryStatuses.Completed;
        sourceBinding.LastAppliedStatus = MediaLibraryStatuses.Current;

        var targetAccount = new ConnectedServiceAccount
        {
            Id = 1208,
            UserId = user.Id,
            Service = "myanimelist",
            ExternalAccountId = "viewer-208-target",
            CreatedAt = now.UtcDateTime,
            UpdatedAt = now.UtcDateTime
        };
        var targetLink = new MediaProviderLink
        {
            Id = Guid.NewGuid(),
            MediaTitleId = entry.MediaTitleId,
            Provider = "myanimelist",
            ExternalId = "anime:52991",
            LinkSource = MediaMappingSources.Imported,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.AddRange(targetAccount, targetLink, new MediaLibraryProviderBinding
        {
            Id = Guid.NewGuid(),
            MediaLibraryEntryId = entry.Id,
            MediaProviderLinkId = targetLink.Id,
            ConnectedServiceAccountId = targetAccount.Id,
            ProviderAccountId = targetAccount.ExternalAccountId,
            LastRemoteUpdateAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var newer = now.AddMinutes(2);
        var import = MakeImport(newer, 12, newer, score: 90m);
        import.Items[0].Status = MediaLibraryStatuses.Current;
        await service.ImportAsync(user.Id, account, import, CancellationToken.None);

        var persistedEntry = await db.MediaLibraryEntries.SingleAsync();
        Assert.Equal(MediaLibraryStatuses.Completed, persistedEntry.Status);
        Assert.Equal(90m, persistedEntry.Score);
        var operations = await db.MediaProviderOperations.ToListAsync();
        Assert.Equal(MediaProviderOperationTypes.ImportFanOutScore, Assert.Single(operations).OperationType);
    }

    [Fact]
    public async Task ImportAsync_GenuineRemoteStatusChangeClearsNormalizationAndImportsStatus()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var (user, account) = await SeedAccountAsync(db, 209);
        var service = MakeService(db);
        var now = DateTimeOffset.UtcNow;
        await service.ImportAsync(user.Id, account, MakeImport(now, 12, now), CancellationToken.None);

        var entry = await db.MediaLibraryEntries.SingleAsync();
        var binding = await db.MediaLibraryProviderBindings.SingleAsync();
        entry.Status = MediaLibraryStatuses.Completed;
        binding.LastRequestedStatus = MediaLibraryStatuses.Completed;
        binding.LastAppliedStatus = MediaLibraryStatuses.Current;
        await db.SaveChangesAsync();

        var newer = now.AddMinutes(2);
        var import = MakeImport(newer, 12, newer);
        import.Items[0].Status = MediaLibraryStatuses.Paused;
        await service.ImportAsync(user.Id, account, import, CancellationToken.None);

        var persistedEntry = await db.MediaLibraryEntries.SingleAsync();
        var persistedBinding = await db.MediaLibraryProviderBindings.SingleAsync();
        Assert.Equal(MediaLibraryStatuses.Paused, persistedEntry.Status);
        Assert.Null(persistedBinding.LastRequestedStatus);
        Assert.Null(persistedBinding.LastAppliedStatus);
    }

    private static MediaLibraryImportService MakeService(ApplicationDbContext db) =>
        new(db, new MediaLibraryEventHub(), NullLogger<MediaLibraryImportService>.Instance);

    private static MediaProviderLibraryImportResult MakeImport(
        DateTimeOffset importedAt,
        int progress,
        DateTimeOffset? remoteUpdatedAt = null,
        decimal? score = 87.5m,
        bool hasNonContiguousProgress = false) => new()
    {
        ProviderId = "anilist",
        ImportedAt = importedAt,
        Items =
        [
            new MediaProviderLibraryItem
            {
                ProviderMediaId = "154587",
                ProviderLibraryEntryId = "list-154587",
                Title = "Frieren",
                NativeTitle = "Sousou no Frieren",
                Synonyms = ["Frieren: Beyond Journey's End", "Sousou no Frieren"],
                MediaKind = MediaKinds.Anime,
                Format = "TV",
                PosterUrl = "https://example.test/poster.jpg",
                EpisodeCount = 28,
                ReleasedCount = 28,
                TotalKnownCount = 28,
                Status = MediaLibraryStatuses.Current,
                Score = score,
                ProviderListNames = ["Favorites"],
                ProgressEpisodes = progress,
                HasNonContiguousProgress = hasNonContiguousProgress,
                PrimaryProgressDimension = MediaProgressDimensions.Episode,
                ReleaseStatusDimension = MediaProgressDimensions.Episode,
                LastRemoteUpdateAt = remoteUpdatedAt ?? importedAt
            }
        ]
    };

    private static async Task<(User User, ConnectedServiceAccount Account)> SeedAccountAsync(
        ApplicationDbContext db,
        int userId)
    {
        var user = TestUserFactory.Create(userId, $"import-{userId}@example.test");
        var account = new ConnectedServiceAccount
        {
            Id = 800 + userId,
            UserId = user.Id,
            Service = "anilist",
            ExternalAccountId = $"viewer-{userId}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.AddRange(user, account);
        await db.SaveChangesAsync();
        return (user, account);
    }

    private static async Task<(ApplicationDbContext Db, SqliteConnection Connection)> CreateDbAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        return (db, connection);
    }
}
