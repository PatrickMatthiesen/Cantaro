using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cantaro.Api.Tests;

public class MediaDomainModelTests
{
    [Fact]
    public async Task MediaDomainEntities_PersistCanonicalTitleLinksAndSyncMetadata()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var timestamp = DateTimeOffset.UtcNow;
        var user = TestUserFactory.Create(101, "media-foundation@example.com");
        var account = new ConnectedServiceAccount
        {
            Id = 701,
            UserId = user.Id,
            Service = "anilist",
            ExternalAccountId = "anilist-user-101",
            DisplayName = "Media Foundation Tester",
            CreatedAt = timestamp.UtcDateTime,
            UpdatedAt = timestamp.UtcDateTime
        };

        var title = new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = "Frieren: Beyond Journey's End",
            MediaKind = MediaKinds.Anime,
            SupportsEpisodeProgress = true,
            SupportsChapterProgress = false,
            SupportsVolumeProgress = false,
            IsCompletionOnly = false,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            EpisodeCount = 28,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };

        var providerLink = new MediaProviderLink
        {
            Id = Guid.NewGuid(),
            MediaTitleId = title.Id,
            Provider = "anilist",
            ExternalId = "154587",
            ExternalUrl = "https://anilist.co/anime/154587",
            LinkSource = MediaMappingSources.Imported,
            LinkedByUserId = user.Id,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };

        var libraryEntry = new MediaLibraryEntry
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            MediaTitleId = title.Id,
            ConnectedServiceAccountId = account.Id,
            Provider = "anilist",
            ProviderAccountId = account.ExternalAccountId,
            ProviderMediaId = providerLink.ExternalId,
            ProviderLibraryEntryId = "list-entry-500",
            NormalizedStatus = MediaLibraryStatuses.Current,
            RawStatus = "CURRENT",
            ProgressEpisodes = 12,
            LastSyncedAt = timestamp,
            LastRemoteUpdateAt = timestamp.AddMinutes(-5),
            LastLocalEditAt = timestamp.AddMinutes(-1),
            LastMutationSource = MediaMappingSources.UserConfirmed,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };

        dbContext.Users.Add(user);
        dbContext.ConnectedServiceAccounts.Add(account);
        dbContext.MediaTitles.Add(title);
        dbContext.MediaProviderLinks.Add(providerLink);
        dbContext.MediaLibraryEntries.Add(libraryEntry);

        await dbContext.SaveChangesAsync();

        var persistedEntry = await dbContext.MediaLibraryEntries.SingleAsync();
        var persistedProviderLink = await dbContext.MediaProviderLinks.SingleAsync();

        Assert.Equal(MediaLibraryStatuses.Current, persistedEntry.NormalizedStatus);
        Assert.Equal(MediaProgressDimensions.Episode, title.PrimaryProgressDimension);
        Assert.Equal(account.Id, persistedEntry.ConnectedServiceAccountId);
        Assert.Equal(timestamp.AddMinutes(-5), persistedEntry.LastRemoteUpdateAt);
        Assert.Equal(providerLink.ExternalId, persistedProviderLink.ExternalId);
        Assert.Equal(MediaMappingSources.Imported, persistedProviderLink.LinkSource);
    }

    [Fact]
    public async Task MediaLibraryEntries_AllowNullGranularProgressForCompletionOnlyTitles()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var timestamp = DateTimeOffset.UtcNow;
        var user = TestUserFactory.Create(102, "completion-only@example.com");
        var title = new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = "Your Name.",
            MediaKind = MediaKinds.Movie,
            SupportsEpisodeProgress = false,
            SupportsChapterProgress = false,
            SupportsVolumeProgress = false,
            IsCompletionOnly = true,
            PrimaryProgressDimension = MediaProgressDimensions.CompletionOnly,
            ReleaseStatusDimension = MediaProgressDimensions.CompletionOnly,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };

        var entry = new MediaLibraryEntry
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            MediaTitleId = title.Id,
            Provider = "anilist",
            ProviderAccountId = "anilist-user-102",
            ProviderMediaId = "21519",
            NormalizedStatus = MediaLibraryStatuses.Completed,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };

        dbContext.Users.Add(user);
        dbContext.MediaTitles.Add(title);
        dbContext.MediaLibraryEntries.Add(entry);

        await dbContext.SaveChangesAsync();

        var persistedEntry = await dbContext.MediaLibraryEntries.SingleAsync();

        Assert.Null(persistedEntry.ProgressEpisodes);
        Assert.Null(persistedEntry.ProgressChapters);
        Assert.Null(persistedEntry.ProgressVolumes);
    }

    [Fact]
    public async Task MediaProviderLinks_EnforceProviderExternalIdentityUniqueness()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var timestamp = DateTimeOffset.UtcNow;
        var firstTitle = new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = "Delicious in Dungeon",
            MediaKind = MediaKinds.Anime,
            SupportsEpisodeProgress = true,
            SupportsChapterProgress = false,
            SupportsVolumeProgress = false,
            IsCompletionOnly = false,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };

        var secondTitle = new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = "Dungeon Meshi",
            MediaKind = MediaKinds.Manga,
            SupportsEpisodeProgress = false,
            SupportsChapterProgress = true,
            SupportsVolumeProgress = true,
            IsCompletionOnly = false,
            PrimaryProgressDimension = MediaProgressDimensions.Chapter,
            ReleaseStatusDimension = MediaProgressDimensions.Chapter,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };

        dbContext.MediaTitles.AddRange(firstTitle, secondTitle);
        await dbContext.SaveChangesAsync();

        dbContext.MediaProviderLinks.Add(new MediaProviderLink
        {
            Id = Guid.NewGuid(),
            MediaTitleId = firstTitle.Id,
            Provider = "anilist",
            ExternalId = "153518",
            LinkSource = MediaMappingSources.Imported,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        });

        await dbContext.SaveChangesAsync();

        dbContext.MediaProviderLinks.Add(new MediaProviderLink
        {
            Id = Guid.NewGuid(),
            MediaTitleId = secondTitle.Id,
            Provider = "anilist",
            ExternalId = "153518",
            LinkSource = MediaMappingSources.UserConfirmed,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

}
