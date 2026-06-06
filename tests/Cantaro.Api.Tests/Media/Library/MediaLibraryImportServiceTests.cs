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
    public async Task ImportAsync_CreatesCanonicalRecordsAndUpdatesExistingEntries()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var service = new MediaLibraryImportService(dbContext, NullLogger<MediaLibraryImportService>.Instance);
        var now = DateTimeOffset.UtcNow;
        var user = TestUserFactory.Create(201, "import@example.com");
        var account = new ConnectedServiceAccount
        {
            Id = 801,
            UserId = user.Id,
            Service = "anilist",
            ExternalAccountId = "viewer-201",
            DisplayName = "Importer",
            CreatedAt = now.UtcDateTime,
            UpdatedAt = now.UtcDateTime
        };

        dbContext.Users.Add(user);
        dbContext.ConnectedServiceAccounts.Add(account);
        await dbContext.SaveChangesAsync();

        var initialImport = new MediaProviderLibraryImportResult
        {
            ProviderId = "anilist",
            ImportedAt = now,
            Items =
            [
                new MediaProviderLibraryItem
                {
                    ProviderMediaId = "154587",
                    ProviderLibraryEntryId = "entry-1",
                    Title = "Frieren: Beyond Journey's End",
                    NativeTitle = "Sousou no Frieren",
                    OriginalTitle = "葬送のフリーレン",
                    MediaKind = MediaKinds.Anime,
                    Synopsis = "An elven mage reflects on a journey long after it ended.",
                    ExternalUrl = "https://anilist.co/anime/154587",
                    StartYear = 2023,
                    EpisodeCount = 28,
                    NormalizedStatus = MediaLibraryStatuses.Current,
                    RawStatus = "CURRENT",
                    RawListName = "Watching",
                    ProgressEpisodes = 12,
                    PrimaryProgressDimension = MediaProgressDimensions.Episode,
                    ReleaseStatusDimension = MediaProgressDimensions.Episode,
                    LastRemoteUpdateAt = now.AddMinutes(-5),
                    RawMetadata = "{\"source\":\"initial\"}"
                }
            ]
        };

        var firstResult = await service.ImportAsync(user.Id, account, initialImport, CancellationToken.None);

        Assert.Equal(1, firstResult.CreatedTitles);
        Assert.Equal(1, firstResult.CreatedEntries);
        Assert.Equal(0, firstResult.UpdatedEntries);

        var refreshedImport = new MediaProviderLibraryImportResult
        {
            ProviderId = "anilist",
            ImportedAt = now.AddMinutes(10),
            Items =
            [
                new MediaProviderLibraryItem
                {
                    ProviderMediaId = "154587",
                    ProviderLibraryEntryId = "entry-1",
                    Title = "Frieren: Beyond Journey's End",
                    NativeTitle = "Sousou no Frieren",
                    OriginalTitle = "葬送のフリーレン",
                    MediaKind = MediaKinds.Anime,
                    Synopsis = "Updated synopsis",
                    ExternalUrl = "https://anilist.co/anime/154587",
                    StartYear = 2023,
                    EpisodeCount = 28,
                    NormalizedStatus = MediaLibraryStatuses.Completed,
                    RawStatus = "COMPLETED",
                    RawListName = "Completed",
                    ProgressEpisodes = 28,
                    PrimaryProgressDimension = MediaProgressDimensions.Episode,
                    ReleaseStatusDimension = MediaProgressDimensions.Episode,
                    LastRemoteUpdateAt = now.AddMinutes(8),
                    RawMetadata = "{\"source\":\"refresh\"}"
                }
            ]
        };

        var secondResult = await service.ImportAsync(user.Id, account, refreshedImport, CancellationToken.None);

        var entry = await dbContext.MediaLibraryEntries.SingleAsync();
        var title = await dbContext.MediaTitles.SingleAsync();
        var link = await dbContext.MediaProviderLinks.SingleAsync();

        Assert.Equal(0, secondResult.CreatedTitles);
        Assert.Equal(0, secondResult.CreatedEntries);
        Assert.Equal(1, secondResult.UpdatedEntries);
        Assert.Equal(MediaLibraryStatuses.Completed, entry.NormalizedStatus);
        Assert.Equal(28, entry.ProgressEpisodes);
        Assert.Equal("Completed", entry.RawListName);
        Assert.Equal("Updated synopsis", title.Synopsis);
        Assert.Equal("https://anilist.co/anime/154587", link.ExternalUrl);
    }

    [Fact]
    public async Task ImportAsync_DoesNotRollBackRecentLocalProgress_WhenRemoteSnapshotIsNotNewer()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var service = new MediaLibraryImportService(dbContext, NullLogger<MediaLibraryImportService>.Instance);
        var now = DateTimeOffset.UtcNow;
        var remoteUpdatedAt = now.AddSeconds(-2);
        var user = TestUserFactory.Create(202, "local-progress@example.com");
        var account = new ConnectedServiceAccount
        {
            Id = 802,
            UserId = user.Id,
            Service = "anilist",
            ExternalAccountId = "viewer-202",
            DisplayName = "Local Progress",
            CreatedAt = now.UtcDateTime,
            UpdatedAt = now.UtcDateTime
        };
        var title = new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = "Frieren: Beyond Journey's End",
            SortTitle = "Frieren: Beyond Journey's End",
            MediaKind = MediaKinds.Anime,
            SupportsEpisodeProgress = true,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            CreatedAt = now,
            UpdatedAt = now
        };
        var link = new MediaProviderLink
        {
            Id = Guid.NewGuid(),
            MediaTitleId = title.Id,
            Provider = "anilist",
            ExternalId = "154587",
            LinkSource = MediaMappingSources.Imported,
            CreatedAt = now,
            UpdatedAt = now
        };
        var entry = new MediaLibraryEntry
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            MediaTitleId = title.Id,
            ConnectedServiceAccountId = account.Id,
            Provider = "anilist",
            ProviderAccountId = account.ExternalAccountId,
            ProviderMediaId = "154587",
            ProviderLibraryEntryId = "entry-1",
            NormalizedStatus = MediaLibraryStatuses.Current,
            RawStatus = "CURRENT",
            RawListName = "Watching",
            ProgressEpisodes = 6,
            LastSyncedAt = now.AddMinutes(-5),
            LastRemoteUpdateAt = remoteUpdatedAt,
            LastLocalEditAt = now,
            LastMutationSource = MediaMutationSources.UserProgressUpdate,
            RawMetadata = "{\"progress\":6}",
            CreatedAt = now.AddMinutes(-10),
            UpdatedAt = now
        };

        dbContext.Users.Add(user);
        dbContext.ConnectedServiceAccounts.Add(account);
        dbContext.MediaTitles.Add(title);
        dbContext.MediaProviderLinks.Add(link);
        dbContext.MediaLibraryEntries.Add(entry);
        await dbContext.SaveChangesAsync();

        var staleImport = new MediaProviderLibraryImportResult
        {
            ProviderId = "anilist",
            ImportedAt = now.AddMinutes(1),
            Items =
            [
                new MediaProviderLibraryItem
                {
                    ProviderMediaId = "154587",
                    ProviderLibraryEntryId = "entry-1",
                    Title = "Frieren: Beyond Journey's End",
                    MediaKind = MediaKinds.Anime,
                    NormalizedStatus = MediaLibraryStatuses.Current,
                    RawStatus = "CURRENT",
                    RawListName = "Watching",
                    ProgressEpisodes = 5,
                    PrimaryProgressDimension = MediaProgressDimensions.Episode,
                    ReleaseStatusDimension = MediaProgressDimensions.Episode,
                    LastRemoteUpdateAt = remoteUpdatedAt,
                    RawMetadata = "{\"progress\":5}"
                }
            ]
        };

        await service.ImportAsync(user.Id, account, staleImport, CancellationToken.None);

        var persistedEntry = await dbContext.MediaLibraryEntries.SingleAsync();
        Assert.Equal(6, persistedEntry.ProgressEpisodes);
        Assert.Equal(MediaMutationSources.UserProgressUpdate, persistedEntry.LastMutationSource);
        Assert.Equal("{\"progress\":6}", persistedEntry.RawMetadata);
        Assert.Equal(staleImport.ImportedAt, persistedEntry.LastSyncedAt);
    }
}
