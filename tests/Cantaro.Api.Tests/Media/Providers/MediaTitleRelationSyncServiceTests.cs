using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class MediaTitleRelationSyncServiceTests
{
    [Fact]
    public async Task SyncAsync_UpsertsRelatedTitlesAndPrunesEachRefreshedSource()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var service = new MediaTitleRelationSyncService(
            dbContext,
            NullLogger<MediaTitleRelationSyncService>.Instance);
        var provider = new StubRelationGraphProvider(CreateSnapshot(isComplete: true, includeEdge: true));

        var first = await service.SyncAsync(42, provider, "200", CancellationToken.None);

        Assert.Equal(2, first.CreatedTitles);
        Assert.Equal(1, first.CreatedRelations);
        Assert.NotNull(first.RootMediaTitleId);
        Assert.Equal(2, await dbContext.MediaTitles.CountAsync());
        Assert.Equal(2, await dbContext.MediaProviderLinks.CountAsync());
        Assert.Empty(await dbContext.MediaLibraryEntries.ToListAsync());
        var relatedTitle = await dbContext.MediaProviderLinks
            .Where(link => link.ExternalId == "100")
            .Select(link => link.MediaTitle!)
            .SingleAsync();
        Assert.Equal(MediaFormats.Tv, relatedTitle.Format);
        Assert.Equal(12, relatedTitle.EpisodeCount);
        Assert.Equal(MediaRelationTypes.Prequel, (await dbContext.MediaTitleRelations.SingleAsync()).RelationType);

        var repeated = await service.SyncAsync(42, provider, "200", CancellationToken.None);

        Assert.Equal(0, repeated.CreatedTitles);
        Assert.Equal(0, repeated.CreatedRelations);
        Assert.Single(await dbContext.MediaTitleRelations.ToListAsync());

        provider.Snapshot = CreateSnapshot(isComplete: false, includeEdge: false);
        var incomplete = await service.SyncAsync(42, provider, "200", CancellationToken.None);

        Assert.False(incomplete.IsComplete);
        Assert.Equal(1, incomplete.RemovedRelations);
        Assert.Empty(await dbContext.MediaTitleRelations.ToListAsync());

        provider.Snapshot = CreateSnapshot(isComplete: true, includeEdge: false);
        var complete = await service.SyncAsync(42, provider, "200", CancellationToken.None);

        Assert.True(complete.IsComplete);
        Assert.Equal(0, complete.RemovedRelations);
        Assert.Empty(await dbContext.MediaTitleRelations.ToListAsync());
        Assert.All(
            await dbContext.MediaProviderLinks.ToListAsync(),
            link => Assert.NotNull(link.RelationsLastVerifiedAt));
        Assert.Single((await dbContext.MediaProviderLinks.ToListAsync())
            .Select(link => link.RelationsSnapshotId)
            .Distinct());
    }

    [Fact]
    public async Task SyncAsync_RecordsFreshnessForCompleteGraphWithoutRelations()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var service = new MediaTitleRelationSyncService(
            dbContext,
            NullLogger<MediaTitleRelationSyncService>.Instance);
        var provider = new StubRelationGraphProvider(
            CreateSnapshot(isComplete: true, includeEdge: false));

        var result = await service.SyncAsync(42, provider, "200", CancellationToken.None);

        Assert.True(result.IsComplete);
        Assert.Empty(await dbContext.MediaTitleRelations.ToListAsync());
        Assert.All(
            await dbContext.MediaProviderLinks.ToListAsync(),
            link => Assert.NotNull(link.RelationsLastVerifiedAt));
    }

    private static MediaProviderRelationGraphSnapshot CreateSnapshot(bool isComplete, bool includeEdge)
    {
        return new MediaProviderRelationGraphSnapshot
        {
            ProviderId = "anilist",
            RootProviderMediaId = "200",
            IsComplete = isComplete,
            RefreshedProviderMediaIds = ["200", "100"],
            Nodes =
            [
                new MediaProviderRelationGraphNode
                {
                    ProviderMediaId = "200",
                    Title = "Example Season 2",
                    MediaKind = MediaKinds.Anime,
                    Format = MediaFormats.Tv,
                    EpisodeCount = 12,
                    ExternalUrl = "https://anilist.co/anime/200"
                },
                new MediaProviderRelationGraphNode
                {
                    ProviderMediaId = "100",
                    Title = "Example",
                    MediaKind = MediaKinds.Anime,
                    Format = MediaFormats.Tv,
                    EpisodeCount = 12,
                    ExternalUrl = "https://anilist.co/anime/100"
                }
            ],
            Edges = includeEdge
                ?
                [
                    new MediaProviderRelationGraphEdge
                    {
                        MediaProviderMediaId = "200",
                        RelatedProviderMediaId = "100",
                        RelationType = MediaRelationTypes.Prequel,
                        SourceRelationId = "1"
                    }
                ]
                : []
        };
    }

    private sealed class StubRelationGraphProvider(MediaProviderRelationGraphSnapshot snapshot)
        : IMediaRelationGraphProvider
    {
        public string ProviderId => "anilist";

        public MediaProviderRelationGraphSnapshot Snapshot { get; set; } = snapshot;

        public Task<MediaProviderRelationGraphSnapshot> GetRelationGraphAsync(
            int userId,
            string providerMediaId,
            CancellationToken cancellationToken)
            => Task.FromResult(Snapshot);
    }
}
