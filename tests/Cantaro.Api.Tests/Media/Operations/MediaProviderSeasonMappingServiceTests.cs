using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class MediaProviderSeasonMappingServiceTests
{
    [Fact]
    public async Task ManualMapping_SupersedesAutomaticAndCannotBeChangedByAutomaticWrites()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var first = CreateTitle("First");
        var second = CreateTitle("Second");
        db.MediaTitles.AddRange(first, second);
        await db.SaveChangesAsync();
        var service = new MediaProviderSeasonMappingService(
            db,
            NullLogger<MediaProviderSeasonMappingService>.Instance);

        await service.EstablishAsync(
            "Crunchyroll", "series", "season", 2, first.Id, -12,
            MediaProviderSeasonMappingSources.Automatic, 1m, CancellationToken.None);
        await db.SaveChangesAsync();
        var established = await service.FindAsync(
            "crunchyroll", "SERIES", "SEASON", 999, CancellationToken.None);

        Assert.NotNull(established);
        Assert.Equal(first.Id, established.MediaTitleId);
        Assert.Equal(-12, established.EpisodeOffset);

        await service.EstablishAsync(
            "crunchyroll", "series", "season", 2, second.Id, 0,
            MediaProviderSeasonMappingSources.Manual, 1m, CancellationToken.None);
        await db.SaveChangesAsync();

        await service.EstablishAsync("crunchyroll", "series", "season", 2, first.Id, -12,
            MediaProviderSeasonMappingSources.Automatic, 0.99m, CancellationToken.None);

        var corrected = await service.FindAsync(
            "crunchyroll", "series", "season", 2, CancellationToken.None);
        Assert.NotNull(corrected);
        Assert.Equal(second.Id, corrected.MediaTitleId);
        Assert.Equal(0, corrected.EpisodeOffset);
        Assert.Equal(MediaProviderSeasonMappingSources.Manual, corrected.MappingSource);
    }

    [Fact]
    public async Task EstablishAsync_RejectsUnknownMappingSource()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var title = CreateTitle("Title");
        db.MediaTitles.Add(title);
        await db.SaveChangesAsync();
        var service = new MediaProviderSeasonMappingService(
            db,
            NullLogger<MediaProviderSeasonMappingService>.Instance);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.EstablishAsync(
            "crunchyroll", "series", "season", 1, title.Id, 0,
            "catalog_observation", 0.95m, CancellationToken.None));
        Assert.Empty(db.MediaProviderSeasonMappings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncompatibleTitleMetadataInvalidatesMappingInBothDirections(bool direct)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        const string provider = MediaObservationSiteIdentifiers.Crunchyroll;
        const string episodeId = "EPISODE1";
        const int userId = 901;
        var title = CreateTitle("Changing title");
        db.Users.Add(TestUserFactory.Create(userId, "season-target@example.com"));
        db.MediaTitles.Add(title);
        db.MediaLibraryEntries.Add(new MediaLibraryEntry
        {
            UserId = userId, MediaTitleId = title.Id, Status = MediaLibraryStatuses.Current
        });
        db.MediaProviderEpisodes.Add(new MediaProviderEpisode
        {
            Provider = provider, ProviderEpisodeId = episodeId, ProviderUrlPath = "/watch/EPISODE1",
            ProviderSeriesId = "SERIES", ProviderSeasonNumber = 1, ProviderEpisodeNumber = 1, SeenCount = 1
        });
        await db.SaveChangesAsync();
        var service = new MediaProviderSeasonMappingService(db, NullLogger<MediaProviderSeasonMappingService>.Instance);
        if (direct)
        {
            await service.EstablishDirectAsync(provider, episodeId, title.Id, 1,
                MediaProviderSeasonMappingSources.Manual, default);
        }
        else
        {
            await service.EstablishAsync(provider, "SERIES", null, 1, title.Id, 0,
                MediaProviderSeasonMappingSources.Manual, 1m, default);
        }
        var observation = new MediaObservation
        {
            SiteIdentifier = provider, SiteMediaId = episodeId, ObservedTitle = "Changing title",
            ObservedUrl = "https://www.crunchyroll.com/watch/EPISODE1", MatchStatus = MediaObservationStatuses.NoMatch
        };
        Assert.Equal(title.Id, (await service.ResolveAsync(observation, default))?.Title.Id);
        Assert.Single(await MediaProviderSeasonMappingService.GetMappedEpisodesAsync(db, [title.Id], default));
        var identity = new MediaEpisodeIdentityService(db, service, NullLogger<MediaEpisodeIdentityService>.Instance);
        Assert.Single((await identity.GetEpisodeCatalogAsync(title.Id, default))!.SeriesDestinations);

        title.PrimaryProgressDimension = MediaProgressDimensions.Chapter;
        title.MediaKind = MediaKinds.Manga;
        await db.SaveChangesAsync();

        Assert.Null(await service.ResolveAsync(observation, default));
        Assert.Empty(await MediaProviderSeasonMappingService.GetMappedEpisodesAsync(db, [title.Id], default));
        var catalog = await identity.GetEpisodeCatalogAsync(title.Id, default);
        Assert.NotNull(catalog);
        Assert.Empty(catalog.SeriesDestinations);
        Assert.Empty(catalog.Episodes);
        var destination = await identity.ResolveContinueWatchingAsync(userId, title.Id, default);
        Assert.NotNull(destination);
        Assert.Equal("unavailable", destination.Outcome);
        Assert.Null(destination.Url);
    }

    private static MediaTitle CreateTitle(string title)
    {
        var now = DateTimeOffset.UtcNow;
        return new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = title,
            MediaKind = MediaKinds.Anime,
            SupportsEpisodeProgress = true,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
