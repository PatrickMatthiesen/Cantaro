using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantaro.Api.Tests;

public class MediaLibraryQueryServiceTests
{
    private static async Task<(ApplicationDbContext, SqliteConnection)> CreateDbAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return (db, connection);
    }

    [Fact]
    public async Task GetLibraryAsync_ReturnsPaginatedItemsForUser()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;

        var now = DateTimeOffset.UtcNow;
        var user = TestUserFactory.Create(301, "query@example.com");
        db.Users.Add(user);

        var titles = new[]
        {
            MakeTitle("Frieren", MediaKinds.Anime, now),
            MakeTitle("Berserk", MediaKinds.Manga, now),
            MakeTitle("Spy x Family", MediaKinds.Anime, now)
        };
        db.MediaTitles.AddRange(titles);
        await db.SaveChangesAsync();

        db.MediaLibraryEntries.AddRange(
            MakeEntry(user.Id, titles[0], MediaLibraryStatuses.Current, now),
            MakeEntry(user.Id, titles[1], MediaLibraryStatuses.Planned, now),
            MakeEntry(user.Id, titles[2], MediaLibraryStatuses.Completed, now));
        await db.SaveChangesAsync();

        var service = new MediaLibraryQueryService(db);

        var page = await service.GetLibraryAsync(user.Id, new MediaLibraryQueryOptions
        {
            Page = 1,
            PageSize = 2,
            SortBy = "title",
            SortDir = "asc"
        }, CancellationToken.None);

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(2, page.TotalPages);
        // Sorted alphabetically ascending: Berserk, Frieren, Spy x Family → page 1: Berserk, Frieren
        Assert.Equal("Berserk", page.Items[0].CanonicalTitle);
        Assert.Equal("Frieren", page.Items[1].CanonicalTitle);
    }

    [Fact]
    public async Task GetLibraryAsync_FiltersCorrectly()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;

        var now = DateTimeOffset.UtcNow;
        var user = TestUserFactory.Create(302, "filter@example.com");
        db.Users.Add(user);

        var anime = MakeTitle("My Hero Academia", MediaKinds.Anime, now);
        var manga = MakeTitle("Chainsaw Man", MediaKinds.Manga, now);
        db.MediaTitles.AddRange(anime, manga);
        await db.SaveChangesAsync();

        db.MediaLibraryEntries.AddRange(
            MakeEntry(user.Id, anime, MediaLibraryStatuses.Current, now),
            MakeEntry(user.Id, manga, MediaLibraryStatuses.Planned, now));
        await db.SaveChangesAsync();

        var service = new MediaLibraryQueryService(db);

        var currentOnly = await service.GetLibraryAsync(user.Id, new MediaLibraryQueryOptions
        {
            Status = MediaLibraryStatuses.Current
        }, CancellationToken.None);

        Assert.Equal(1, currentOnly.TotalCount);
        Assert.Equal("My Hero Academia", currentOnly.Items[0].CanonicalTitle);

        var animeOnly = await service.GetLibraryAsync(user.Id, new MediaLibraryQueryOptions
        {
            MediaKind = MediaKinds.Anime
        }, CancellationToken.None);

        Assert.Equal(1, animeOnly.TotalCount);
        Assert.Equal(MediaKinds.Anime, animeOnly.Items[0].MediaKind);
    }

    [Fact]
    public async Task GetLibraryAsync_IsolatesAcrossUsers()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;

        var now = DateTimeOffset.UtcNow;
        var userA = TestUserFactory.Create(303, "userA@example.com");
        var userB = TestUserFactory.Create(304, "userB@example.com");
        db.Users.AddRange(userA, userB);

        var title = MakeTitle("Vinland Saga", MediaKinds.Anime, now);
        db.MediaTitles.Add(title);
        await db.SaveChangesAsync();

        db.MediaLibraryEntries.Add(MakeEntry(userA.Id, title, MediaLibraryStatuses.Completed, now));
        await db.SaveChangesAsync();

        var service = new MediaLibraryQueryService(db);

        var pageA = await service.GetLibraryAsync(userA.Id, new MediaLibraryQueryOptions(), CancellationToken.None);
        var pageB = await service.GetLibraryAsync(userB.Id, new MediaLibraryQueryOptions(), CancellationToken.None);

        Assert.Equal(1, pageA.TotalCount);
        Assert.Equal(0, pageB.TotalCount);
    }

    [Fact]
    public async Task GetLibraryEntryDetailAsync_ReturnsEntryWithProviderLinks()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;

        var now = DateTimeOffset.UtcNow;
        var user = TestUserFactory.Create(305, "detail@example.com");
        db.Users.Add(user);

        var title = MakeTitle("Attack on Titan", MediaKinds.Anime, now);
        db.MediaTitles.Add(title);
        await db.SaveChangesAsync();

        var link = new MediaProviderLink
        {
            Id = Guid.NewGuid(),
            MediaTitleId = title.Id,
            Provider = "anilist",
            ExternalId = "16498",
            ExternalUrl = "https://anilist.co/anime/16498",
            LinkSource = MediaMappingSources.Imported,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.MediaProviderLinks.Add(link);

        var entry = MakeEntry(user.Id, title, MediaLibraryStatuses.Completed, now);
        entry.ProgressEpisodes = 87;
        db.MediaLibraryEntries.Add(entry);
        await db.SaveChangesAsync();

        var service = new MediaLibraryQueryService(db);
        var detail = await service.GetLibraryEntryDetailAsync(user.Id, entry.Id, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal("Attack on Titan", detail.Title.CanonicalTitle);
        Assert.Equal(87, detail.ProgressEpisodes);
        Assert.Single(detail.ProviderLinks);
        Assert.Equal("anilist", detail.ProviderLinks[0].Provider);
        Assert.Equal("16498", detail.ProviderLinks[0].ExternalId);
    }

    [Fact]
    public async Task GetLibraryEntryDetailAsync_ReturnsNullForWrongUser()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;

        var now = DateTimeOffset.UtcNow;
        var userA = TestUserFactory.Create(306, "ownerA@example.com");
        var userB = TestUserFactory.Create(307, "ownerB@example.com");
        db.Users.AddRange(userA, userB);

        var title = MakeTitle("Death Note", MediaKinds.Anime, now);
        db.MediaTitles.Add(title);
        await db.SaveChangesAsync();

        var entry = MakeEntry(userA.Id, title, MediaLibraryStatuses.Completed, now);
        db.MediaLibraryEntries.Add(entry);
        await db.SaveChangesAsync();

        var service = new MediaLibraryQueryService(db);
        var detail = await service.GetLibraryEntryDetailAsync(userB.Id, entry.Id, CancellationToken.None);

        Assert.Null(detail);
    }

    private static MediaTitle MakeTitle(string name, string kind, DateTimeOffset now)
    {
        return new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = name,
            MediaKind = kind,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static MediaLibraryEntry MakeEntry(int userId, MediaTitle title, string status, DateTimeOffset now)
    {
        return new MediaLibraryEntry
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            MediaTitleId = title.Id,
            MediaTitle = title,
            Provider = "anilist",
            ProviderAccountId = $"account-{userId}",
            ProviderMediaId = Guid.NewGuid().ToString("N")[..6],
            NormalizedStatus = status,
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
