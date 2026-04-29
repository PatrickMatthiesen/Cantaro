using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantaro.Api.Tests;

public class MediaLibraryLinkServiceTests
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
    public async Task LinkProviderAsync_CreatesNewProviderLink()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;

        var (user, title, entry) = await SeedAsync(db, 401);

        var service = MakeService(db);
        var result = await service.LinkProviderAsync(
            user.Id, entry.Id, "anilist", "99999", forceRelink: false, CancellationToken.None);

        Assert.Equal(MediaLinkResultKind.Success, result.Kind);

        var link = await db.MediaProviderLinks.SingleAsync();
        Assert.Equal("anilist", link.Provider);
        Assert.Equal("99999", link.ExternalId);
        Assert.Equal(title.Id, link.MediaTitleId);
        Assert.Equal(MediaMappingSources.UserConfirmed, link.LinkSource);
        Assert.Equal(user.Id, link.LinkedByUserId);
    }

    [Fact]
    public async Task LinkProviderAsync_IsIdempotentWhenLinkAlreadyCorrect()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;

        var now = DateTimeOffset.UtcNow;
        var (user, title, entry) = await SeedAsync(db, 402);

        var existingLink = new MediaProviderLink
        {
            Id = Guid.NewGuid(),
            MediaTitleId = title.Id,
            Provider = "anilist",
            ExternalId = "77777",
            LinkSource = MediaMappingSources.Imported,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.MediaProviderLinks.Add(existingLink);
        await db.SaveChangesAsync();

        var service = MakeService(db);
        var result = await service.LinkProviderAsync(
            user.Id, entry.Id, "anilist", "77777", forceRelink: false, CancellationToken.None);

        Assert.Equal(MediaLinkResultKind.AlreadyLinked, result.Kind);
        // Verification timestamp refreshed
        Assert.NotNull(existingLink.LastVerifiedAt);
    }

    [Fact]
    public async Task LinkProviderAsync_ReturnsConflictWhenExternalIdBelongsToDifferentTitle()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;

        var now = DateTimeOffset.UtcNow;
        var user = TestUserFactory.Create(403, "conflict@example.com");
        db.Users.Add(user);

        var titleA = MakeTitle("Title A", now);
        var titleB = MakeTitle("Title B", now);
        db.MediaTitles.AddRange(titleA, titleB);
        await db.SaveChangesAsync();

        // Entry pointing to Title B, but the external ID "55555" is already linked to Title A.
        var entryB = MakeEntry(user.Id, titleB, now);
        db.MediaLibraryEntries.Add(entryB);
        db.MediaProviderLinks.Add(new MediaProviderLink
        {
            Id = Guid.NewGuid(),
            MediaTitleId = titleA.Id,
            Provider = "anilist",
            ExternalId = "55555",
            LinkSource = MediaMappingSources.Imported,
            MediaTitle = titleA,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var service = MakeService(db);
        var result = await service.LinkProviderAsync(
            user.Id, entryB.Id, "anilist", "55555", forceRelink: false, CancellationToken.None);

        Assert.Equal(MediaLinkResultKind.ConflictingTitle, result.Kind);
        Assert.Equal(titleA.Id, result.ConflictingMediaTitleId);
        Assert.Equal("Title A", result.ConflictingCanonicalTitle);

        // No changes persisted
        var link = await db.MediaProviderLinks.SingleAsync();
        Assert.Equal(titleA.Id, link.MediaTitleId);
    }

    [Fact]
    public async Task LinkProviderAsync_ForceRelinkOverridesConflict()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;

        var now = DateTimeOffset.UtcNow;
        var user = TestUserFactory.Create(404, "force@example.com");
        db.Users.Add(user);

        var titleA = MakeTitle("Title A", now);
        var titleB = MakeTitle("Title B", now);
        db.MediaTitles.AddRange(titleA, titleB);
        await db.SaveChangesAsync();

        var entryB = MakeEntry(user.Id, titleB, now);
        db.MediaLibraryEntries.Add(entryB);
        db.MediaProviderLinks.Add(new MediaProviderLink
        {
            Id = Guid.NewGuid(),
            MediaTitleId = titleA.Id,
            Provider = "anilist",
            ExternalId = "55555",
            LinkSource = MediaMappingSources.Imported,
            MediaTitle = titleA,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var service = MakeService(db);
        var result = await service.LinkProviderAsync(
            user.Id, entryB.Id, "anilist", "55555", forceRelink: true, CancellationToken.None);

        Assert.Equal(MediaLinkResultKind.Success, result.Kind);

        // Link now points to titleB
        var link = await db.MediaProviderLinks.SingleAsync();
        Assert.Equal(titleB.Id, link.MediaTitleId);
        Assert.Equal(MediaMappingSources.UserConfirmed, link.LinkSource);
    }

    [Fact]
    public async Task LinkProviderAsync_ForceRelinkReplacesExistingProviderLinkForTitle()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;

        var now = DateTimeOffset.UtcNow;
        var user = TestUserFactory.Create(408, "replace@example.com");
        db.Users.Add(user);

        var titleA = MakeTitle("Title A", now);
        var titleB = MakeTitle("Title B", now);
        db.MediaTitles.AddRange(titleA, titleB);
        await db.SaveChangesAsync();

        var entryA = MakeEntry(user.Id, titleA, now);
        db.MediaLibraryEntries.Add(entryA);
        db.MediaProviderLinks.AddRange(
            new MediaProviderLink
            {
                Id = Guid.NewGuid(),
                MediaTitleId = titleA.Id,
                Provider = "anilist",
                ExternalId = "11111",
                LinkSource = MediaMappingSources.Imported,
                CreatedAt = now,
                UpdatedAt = now
            },
            new MediaProviderLink
            {
                Id = Guid.NewGuid(),
                MediaTitleId = titleB.Id,
                Provider = "anilist",
                ExternalId = "55555",
                LinkSource = MediaMappingSources.Imported,
                MediaTitle = titleB,
                CreatedAt = now,
                UpdatedAt = now
            });
        await db.SaveChangesAsync();

        var service = MakeService(db);
        var result = await service.LinkProviderAsync(
            user.Id, entryA.Id, "anilist", "55555", forceRelink: true, CancellationToken.None);

        Assert.Equal(MediaLinkResultKind.Success, result.Kind);

        var links = await db.MediaProviderLinks.OrderBy(l => l.ExternalId).ToListAsync();
        Assert.Single(links);
        Assert.Equal(titleA.Id, links[0].MediaTitleId);
        Assert.Equal("55555", links[0].ExternalId);
    }

    [Fact]
    public async Task LinkProviderAsync_ReturnsEntryNotFoundForWrongUser()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;

        var (user, _, entry) = await SeedAsync(db, 405);

        var service = MakeService(db);
        var result = await service.LinkProviderAsync(
            userId: 9999, entry.Id, "anilist", "12345", forceRelink: false, CancellationToken.None);

        Assert.Equal(MediaLinkResultKind.EntryNotFound, result.Kind);
    }

    [Fact]
    public async Task UnlinkProviderAsync_RemovesExistingLink()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;

        var now = DateTimeOffset.UtcNow;
        var (user, title, entry) = await SeedAsync(db, 406);

        db.MediaProviderLinks.Add(new MediaProviderLink
        {
            Id = Guid.NewGuid(),
            MediaTitleId = title.Id,
            Provider = "anilist",
            ExternalId = "33333",
            LinkSource = MediaMappingSources.Imported,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var service = MakeService(db);
        var result = await service.UnlinkProviderAsync(user.Id, entry.Id, "anilist", CancellationToken.None);

        Assert.Equal(MediaLinkResultKind.Success, result.Kind);
        Assert.Equal(0, await db.MediaProviderLinks.CountAsync());
    }

    [Fact]
    public async Task UnlinkProviderAsync_ReturnsProviderLinkNotFoundWhenNoLinkExists()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;

        var (user, _, entry) = await SeedAsync(db, 407);

        var service = MakeService(db);
        var result = await service.UnlinkProviderAsync(user.Id, entry.Id, "anilist", CancellationToken.None);

        Assert.Equal(MediaLinkResultKind.ProviderLinkNotFound, result.Kind);
    }

    private static MediaLibraryLinkService MakeService(ApplicationDbContext db)
        => new(db, NullLogger<MediaLibraryLinkService>.Instance);

    private static async Task<(User User, MediaTitle Title, MediaLibraryEntry Entry)> SeedAsync(
        ApplicationDbContext db,
        int userId)
    {
        var now = DateTimeOffset.UtcNow;
        var user = TestUserFactory.Create(userId, $"user{userId}@example.com");
        db.Users.Add(user);

        var title = MakeTitle($"Title for user {userId}", now);
        db.MediaTitles.Add(title);
        await db.SaveChangesAsync();

        var entry = MakeEntry(user.Id, title, now);
        db.MediaLibraryEntries.Add(entry);
        await db.SaveChangesAsync();

        return (user, title, entry);
    }

    private static MediaTitle MakeTitle(string name, DateTimeOffset now)
    {
        return new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = name,
            MediaKind = MediaKinds.Anime,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static MediaLibraryEntry MakeEntry(int userId, MediaTitle title, DateTimeOffset now)
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
            NormalizedStatus = MediaLibraryStatuses.Current,
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
