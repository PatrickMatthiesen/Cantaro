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
}
