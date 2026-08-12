using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cantaro.Api.Tests;

public class MediaDomainModelTests
{
    [Fact]
    public async Task MediaDomainEntities_SeparateCanonicalTitleViewerStateAndProviderBinding()
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
            Status = MediaLibraryStatuses.Current,
            ProgressEpisodes = 12,
            LastLocalEditAt = timestamp.AddMinutes(-1),
            LastMutationSource = MediaMappingSources.UserConfirmed,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
        var binding = new MediaLibraryProviderBinding
        {
            Id = Guid.NewGuid(),
            MediaLibraryEntryId = libraryEntry.Id,
            MediaProviderLinkId = providerLink.Id,
            ConnectedServiceAccountId = account.Id,
            ProviderAccountId = account.ExternalAccountId,
            ProviderLibraryEntryId = "list-entry-500",
            LastSyncedAt = timestamp,
            LastRemoteUpdateAt = timestamp.AddMinutes(-5),
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };

        dbContext.Users.Add(user);
        dbContext.ConnectedServiceAccounts.Add(account);
        dbContext.MediaTitles.Add(title);
        dbContext.MediaProviderLinks.Add(providerLink);
        dbContext.MediaLibraryEntries.Add(libraryEntry);
        dbContext.MediaLibraryProviderBindings.Add(binding);

        await dbContext.SaveChangesAsync();

        var persistedTitle = await dbContext.MediaTitles.SingleAsync();
        var persistedEntry = await dbContext.MediaLibraryEntries.SingleAsync();
        var persistedProviderLink = await dbContext.MediaProviderLinks.SingleAsync();
        var persistedBinding = await dbContext.MediaLibraryProviderBindings.SingleAsync();

        Assert.Equal(MediaLibraryStatuses.Current, persistedEntry.Status);
        Assert.Equal(MediaProgressDimensions.Episode, persistedTitle.PrimaryProgressDimension);
        Assert.Equal(account.Id, persistedBinding.ConnectedServiceAccountId);
        Assert.Equal(timestamp.AddMinutes(-5), persistedBinding.LastRemoteUpdateAt);
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
            Status = MediaLibraryStatuses.Completed,
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

    [Fact]
    public async Task MediaProviderLinks_EnforceTitleProviderUniqueness()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var timestamp = DateTimeOffset.UtcNow;
        var title = new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = "Sousou no Frieren",
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

        dbContext.MediaTitles.Add(title);
        await dbContext.SaveChangesAsync();

        dbContext.MediaProviderLinks.Add(new MediaProviderLink
        {
            Id = Guid.NewGuid(),
            MediaTitleId = title.Id,
            Provider = "anilist",
            ExternalId = "154587",
            LinkSource = MediaMappingSources.Imported,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        });

        await dbContext.SaveChangesAsync();

        dbContext.MediaProviderLinks.Add(new MediaProviderLink
        {
            Id = Guid.NewGuid(),
            MediaTitleId = title.Id,
            Provider = "anilist",
            ExternalId = "200001",
            LinkSource = MediaMappingSources.UserConfirmed,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task MediaLibraryEntries_EnforceOneViewerStatePerUserAndTitle()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        await using var _ = db;
        await db.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.UtcNow;
        var user = TestUserFactory.Create(103, "one-state@example.test");
        var title = new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = "One canonical title",
            MediaKind = MediaKinds.Anime,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.AddRange(user, title);
        await db.SaveChangesAsync();
        db.MediaLibraryEntries.AddRange(
            new MediaLibraryEntry
            {
                Id = Guid.NewGuid(), UserId = user.Id, MediaTitleId = title.Id,
                Status = MediaLibraryStatuses.Planned, CreatedAt = now, UpdatedAt = now
            },
            new MediaLibraryEntry
            {
                Id = Guid.NewGuid(), UserId = user.Id, MediaTitleId = title.Id,
                Status = MediaLibraryStatuses.Current, CreatedAt = now, UpdatedAt = now
            });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

}
