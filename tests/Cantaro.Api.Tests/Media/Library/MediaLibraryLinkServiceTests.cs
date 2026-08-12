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
    [Fact]
    public async Task LinkProviderAsync_RequiresViewerStateForTitle()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var (_, title) = await SeedAsync(db, 401, addToLibrary: false);

        var result = await MakeService(db).LinkProviderAsync(
            401, title.Id, "anilist", "99999", true, CancellationToken.None);

        Assert.Equal(MediaLinkResultKind.EntryNotFound, result.Kind);
        Assert.Empty(db.MediaProviderLinks);
    }

    [Fact]
    public async Task LinkProviderAsync_CreatesCanonicalProviderIdentity()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var (user, title) = await SeedAsync(db, 402);

        var result = await MakeService(db).LinkProviderAsync(
            user.Id, title.Id, " AniList ", "154587", true, CancellationToken.None);

        Assert.Equal(MediaLinkResultKind.Success, result.Kind);
        var link = await db.MediaProviderLinks.SingleAsync();
        Assert.Equal(title.Id, link.MediaTitleId);
        Assert.Equal("anilist", link.Provider);
        Assert.Equal("154587", link.ExternalId);
        Assert.Equal(MediaMappingSources.UserConfirmed, link.LinkSource);
    }

    [Fact]
    public async Task LinkProviderAsync_RejectsIdentityOwnedByAnotherTitle()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var (user, title) = await SeedAsync(db, 403);
        var other = MakeTitle("Other title");
        db.AddRange(other, MakeLink(other, "anilist", "42"));
        await db.SaveChangesAsync();

        var result = await MakeService(db).LinkProviderAsync(
            user.Id, title.Id, "anilist", "42", true, CancellationToken.None);

        Assert.Equal(MediaLinkResultKind.ConflictingTitle, result.Kind);
        Assert.Equal(other.Id, result.ConflictingMediaTitleId);
    }

    [Fact]
    public async Task LinkProviderAsync_RequiresConfirmationBeforeReplacingIdentity()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var (user, title) = await SeedAsync(db, 404);
        db.Add(MakeLink(title, "anilist", "old"));
        await db.SaveChangesAsync();

        var result = await MakeService(db).LinkProviderAsync(
            user.Id, title.Id, "anilist", "new", false, CancellationToken.None);

        Assert.Equal(MediaLinkResultKind.ReplacementConfirmationRequired, result.Kind);
        Assert.Equal("old", result.CurrentProviderMediaId);
    }

    [Fact]
    public async Task LinkProviderAsync_DoesNotReplaceIdentityUsedByProviderBinding()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var (user, title) = await SeedAsync(db, 405);
        var entry = await db.MediaLibraryEntries.SingleAsync();
        var link = MakeLink(title, "anilist", "old");
        db.Add(link);
        db.Add(new MediaLibraryProviderBinding
        {
            Id = Guid.NewGuid(),
            MediaLibraryEntryId = entry.Id,
            MediaProviderLinkId = link.Id,
            ProviderAccountId = "viewer-405",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await MakeService(db).LinkProviderAsync(
            user.Id, title.Id, "anilist", "new", true, CancellationToken.None);

        Assert.Equal(MediaLinkResultKind.LinkInUse, result.Kind);
        Assert.Equal("old", (await db.MediaProviderLinks.SingleAsync()).ExternalId);
    }

    [Fact]
    public async Task UnlinkProviderAsync_RemovesUnusedCanonicalIdentity()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var (user, title) = await SeedAsync(db, 406);
        db.Add(MakeLink(title, "anilist", "154587"));
        await db.SaveChangesAsync();

        var result = await MakeService(db).UnlinkProviderAsync(
            user.Id, title.Id, "anilist", CancellationToken.None);

        Assert.Equal(MediaLinkResultKind.Success, result.Kind);
        Assert.Empty(db.MediaProviderLinks);
    }

    private static MediaLibraryLinkService MakeService(ApplicationDbContext db) =>
        new(db, NullLogger<MediaLibraryLinkService>.Instance);

    private static async Task<(ApplicationDbContext Db, SqliteConnection Connection)> CreateDbAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        return (db, connection);
    }

    private static async Task<(User User, MediaTitle Title)> SeedAsync(
        ApplicationDbContext db,
        int userId,
        bool addToLibrary = true)
    {
        var user = TestUserFactory.Create(userId, $"viewer-{userId}@example.test");
        var title = MakeTitle($"Title {userId}");
        db.AddRange(user, title);
        if (addToLibrary)
        {
            db.Add(new MediaLibraryEntry
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                MediaTitleId = title.Id,
                Status = MediaLibraryStatuses.Planned,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        await db.SaveChangesAsync();
        return (user, title);
    }

    private static MediaTitle MakeTitle(string name) => new()
    {
        Id = Guid.NewGuid(),
        CanonicalTitle = name,
        SortTitle = name,
        MediaKind = MediaKinds.Anime,
        SupportsEpisodeProgress = true,
        PrimaryProgressDimension = MediaProgressDimensions.Episode,
        ReleaseStatusDimension = MediaProgressDimensions.Episode,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static MediaProviderLink MakeLink(MediaTitle title, string provider, string externalId) => new()
    {
        Id = Guid.NewGuid(),
        MediaTitleId = title.Id,
        Provider = provider,
        ExternalId = externalId,
        LinkSource = MediaMappingSources.Imported,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };
}
