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
    public async Task ManualMapping_SupersedesConflictingExactIdentityMapping()
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
            MediaProviderSeasonMappingSources.ProviderEpisodeIdentity, 1m, CancellationToken.None);
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

        var corrected = await service.FindAsync(
            "crunchyroll", "series", "season", 2, CancellationToken.None);
        Assert.NotNull(corrected);
        Assert.False(corrected.HasConflict);
        Assert.Equal(second.Id, corrected.MediaTitleId);
        Assert.Equal(0, corrected.EpisodeOffset);
        Assert.Equal(MediaProviderSeasonMappingSources.Manual, corrected.MappingSource);
    }

    [Fact]
    public async Task EstablishAsync_RejectsUntrustedAutomaticSource()
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

    private static MediaTitle CreateTitle(string title)
    {
        var now = DateTimeOffset.UtcNow;
        return new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = title,
            MediaKind = MediaKinds.Anime,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
