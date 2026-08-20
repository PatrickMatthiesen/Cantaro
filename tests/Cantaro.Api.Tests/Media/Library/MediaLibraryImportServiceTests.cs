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
        Assert.Equal("{\"progress\":5}", binding.RawMetadata);
    }

    private static MediaLibraryImportService MakeService(ApplicationDbContext db) =>
        new(db, NullLogger<MediaLibraryImportService>.Instance);

    private static MediaProviderLibraryImportResult MakeImport(
        DateTimeOffset importedAt,
        int progress,
        DateTimeOffset? remoteUpdatedAt = null) => new()
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
                Status = MediaLibraryStatuses.Current,
                ProviderListNames = ["Favorites"],
                ProgressEpisodes = progress,
                PrimaryProgressDimension = MediaProgressDimensions.Episode,
                ReleaseStatusDimension = MediaProgressDimensions.Episode,
                LastRemoteUpdateAt = remoteUpdatedAt ?? importedAt,
                RawMetadata = $"{{\"progress\":{progress}}}"
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
