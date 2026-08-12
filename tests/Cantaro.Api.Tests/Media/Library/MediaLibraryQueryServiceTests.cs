using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cantaro.Api.Tests;

public class MediaLibraryQueryServiceTests
{
    [Fact]
    public async Task GetMediaTitleAsync_ReturnsGlobalTitleWithoutViewerState()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var title = MakeTitle("One Piece");
        var link = MakeLink(title, "anilist", "21");
        db.AddRange(title, link);
        await db.SaveChangesAsync();

        var service = new MediaLibraryQueryService(db);
        var result = await service.GetMediaTitleAsync(title.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(title.Id, result.Id);
        Assert.Equal("One Piece", result.CanonicalTitle);
        Assert.Equal("21", Assert.Single(result.ProviderLinks).ExternalId);
        Assert.Empty(db.MediaLibraryEntries);
    }

    [Fact]
    public async Task GetViewerStateAsync_IsOptionalAndScopedToUser()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var firstUser = TestUserFactory.Create(301, "first@example.test");
        var secondUser = TestUserFactory.Create(302, "second@example.test");
        var title = MakeTitle("Frieren");
        var entry = MakeEntry(firstUser.Id, title, progress: 7);
        db.AddRange(firstUser, secondUser, title, entry);
        await db.SaveChangesAsync();

        var service = new MediaLibraryQueryService(db);

        var ownerState = await service.GetViewerStateAsync(firstUser.Id, title.Id, CancellationToken.None);
        var otherState = await service.GetViewerStateAsync(secondUser.Id, title.Id, CancellationToken.None);

        Assert.NotNull(ownerState);
        Assert.Equal(7, ownerState.ProgressEpisodes);
        Assert.Null(otherState);
    }

    [Fact]
    public async Task GetViewerStateAsync_ReturnsProviderBindingsSeparatelyFromTitleLinks()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var now = DateTimeOffset.UtcNow;
        var user = TestUserFactory.Create(303, "viewer@example.test");
        var account = new ConnectedServiceAccount
        {
            Id = 703,
            UserId = user.Id,
            Service = "anilist",
            ExternalAccountId = "viewer-303",
            CreatedAt = now.UtcDateTime,
            UpdatedAt = now.UtcDateTime
        };
        var title = MakeTitle("Wistoria");
        var link = MakeLink(title, "anilist", "178022");
        var entry = MakeEntry(user.Id, title, progress: 6);
        var binding = new MediaLibraryProviderBinding
        {
            Id = Guid.NewGuid(),
            MediaLibraryEntryId = entry.Id,
            MediaProviderLinkId = link.Id,
            ConnectedServiceAccountId = account.Id,
            ProviderAccountId = account.ExternalAccountId,
            ProviderLibraryEntryId = "list-1",
            LastSyncedAt = now,
            LastRemoteUpdateAt = now.AddMinutes(-1),
            CreatedAt = now,
            UpdatedAt = now,
            ProviderListMemberships = [new() { Name = "Watching" }]
        };
        db.AddRange(user, account, title, link, entry, binding);
        await db.SaveChangesAsync();

        var result = await new MediaLibraryQueryService(db)
            .GetViewerStateAsync(user.Id, title.Id, CancellationToken.None);

        var provider = Assert.Single(Assert.IsType<MediaViewerStateDto>(result).ProviderBindings);
        Assert.Equal("anilist", provider.Provider);
        Assert.Equal("178022", provider.ProviderMediaId);
        Assert.True(provider.IsConnected);
        Assert.Equal(["Watching"], provider.ProviderListNames);
    }

    [Fact]
    public async Task GetLibraryAsync_UsesCanonicalTitleIdAndViewerProgress()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var user = TestUserFactory.Create(304, "library@example.test");
        var alpha = MakeTitle("Alpha");
        var beta = MakeTitle("Beta");
        db.AddRange(user, alpha, beta, MakeEntry(user.Id, alpha, 2), MakeEntry(user.Id, beta, 5));
        await db.SaveChangesAsync();

        var page = await new MediaLibraryQueryService(db).GetLibraryAsync(user.Id, new()
        {
            SortBy = "title",
            SortDir = "asc",
            PageSize = 1
        }, CancellationToken.None);

        Assert.Equal(2, page.TotalCount);
        var item = Assert.Single(page.Items);
        Assert.Equal(alpha.Id, item.MediaTitleId);
        Assert.Equal(2, item.ProgressEpisodes);
        Assert.Equal("Alpha", item.CanonicalTitle);
    }

    [Fact]
    public async Task GetMediaTitleAsync_UsesTypedReleaseMetadata()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var title = MakeTitle("Upcoming");
        title.Format = "TV";
        title.PosterUrl = "https://example.test/poster.jpg";
        title.ReleasedCount = 0;
        title.EpisodeCount = null;
        title.NextReleaseAt = DateTimeOffset.UtcNow.AddDays(2);
        title.NextReleaseLabel = "Episode 1";
        db.Add(title);
        await db.SaveChangesAsync();

        var result = await new MediaLibraryQueryService(db)
            .GetMediaTitleAsync(title.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("TV", result.Format);
        Assert.Equal(0, result.ReleasedCount);
        Assert.Null(result.EpisodeCount);
        Assert.Equal("Episode 1", result.NextReleaseLabel);
    }

    private static async Task<(ApplicationDbContext Db, SqliteConnection Connection)> CreateDbAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return (db, connection);
    }

    private static MediaTitle MakeTitle(string name)
    {
        var now = DateTimeOffset.UtcNow;
        return new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = name,
            SortTitle = name,
            MediaKind = MediaKinds.Anime,
            SupportsEpisodeProgress = true,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static MediaProviderLink MakeLink(MediaTitle title, string provider, string externalId)
    {
        var now = DateTimeOffset.UtcNow;
        return new MediaProviderLink
        {
            Id = Guid.NewGuid(),
            MediaTitleId = title.Id,
            Provider = provider,
            ExternalId = externalId,
            LinkSource = MediaMappingSources.Imported,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static MediaLibraryEntry MakeEntry(int userId, MediaTitle title, int progress)
    {
        var now = DateTimeOffset.UtcNow;
        return new MediaLibraryEntry
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            MediaTitleId = title.Id,
            Status = MediaLibraryStatuses.Current,
            ProgressEpisodes = progress,
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
