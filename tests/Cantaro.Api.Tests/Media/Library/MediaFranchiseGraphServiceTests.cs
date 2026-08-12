using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class MediaFranchiseGraphServiceTests
{
    [Fact]
    public async Task GetAsync_ReturnsContinuityOffsetsAndKeepsSideStoriesOutOfEpisodeOrder()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var now = DateTimeOffset.UtcNow;
        var user = TestUserFactory.Create(941, "franchise@example.com");
        var seasonOne = CreateTitle("Season one", "TV", 12, 2024, now);
        var seasonTwo = CreateTitle("Season two", "TV", 12, 2025, now);
        var ova = CreateTitle("Side story", "OVA", 1, 2025, now);
        db.Users.Add(user);
        db.MediaTitles.AddRange(seasonOne, seasonTwo, ova);
        db.MediaProviderLinks.AddRange(
            CreateLink(seasonOne, "1", now),
            CreateLink(seasonTwo, "2", now),
            CreateLink(ova, "3", now));
        var entry = CreateEntry(user.Id, seasonTwo, now);
        db.MediaLibraryEntries.Add(entry);
        db.MediaTitleRelations.AddRange(
            CreateRelation(seasonTwo, seasonOne, MediaRelationTypes.Prequel, now),
            CreateRelation(seasonTwo, ova, MediaRelationTypes.SideStory, now));
        await db.SaveChangesAsync();

        var graph = await new MediaFranchiseGraphService(db)
            .GetAsync(user.Id, seasonTwo.Id, CancellationToken.None);

        Assert.NotNull(graph);
        Assert.Equal(seasonTwo.Id, graph.CurrentMediaTitleId);
        Assert.Equal([seasonOne.Id, seasonTwo.Id], graph.Continuity.OrderedMediaTitleIds);
        Assert.Equal(0, graph.Continuity.EpisodeOffsetByMediaTitleId[seasonOne.Id]);
        Assert.Equal(12, graph.Continuity.EpisodeOffsetByMediaTitleId[seasonTwo.Id]);
        Assert.True(graph.Continuity.IsComplete);
        Assert.Equal(3, graph.Nodes.Count);
        Assert.Contains(graph.Relations, relation => relation.RelationType == MediaRelationTypes.SideStory
            && !relation.IsEpisodeContinuity);
        Assert.True(graph.Nodes.Single(node => node.MediaTitleId == seasonTwo.Id).IsInLibrary);
    }

    [Fact]
    public async Task GetAsync_MarksBranchedContinuityIncompleteWithoutChoosingASequel()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var now = DateTimeOffset.UtcNow;
        var user = TestUserFactory.Create(942, "branch@example.com");
        var root = CreateTitle("Root", "TV", 12, 2024, now);
        var sequelA = CreateTitle("Sequel A", "TV", 12, 2025, now);
        var sequelB = CreateTitle("Sequel B", "TV", 12, 2025, now);
        db.Users.Add(user);
        db.MediaTitles.AddRange(root, sequelA, sequelB);
        db.MediaProviderLinks.AddRange(
            CreateLink(root, "10", now),
            CreateLink(sequelA, "11", now),
            CreateLink(sequelB, "12", now));
        var entry = CreateEntry(user.Id, root, now);
        db.MediaLibraryEntries.Add(entry);
        db.MediaTitleRelations.AddRange(
            CreateRelation(root, sequelA, MediaRelationTypes.Sequel, now),
            CreateRelation(root, sequelB, MediaRelationTypes.Sequel, now));
        await db.SaveChangesAsync();

        var graph = await new MediaFranchiseGraphService(db)
            .GetAsync(user.Id, root.Id, CancellationToken.None);

        Assert.NotNull(graph);
        Assert.False(graph.Continuity.IsComplete);
        Assert.Equal([root.Id], graph.Continuity.OrderedMediaTitleIds);
        Assert.All(graph.Relations, relation => Assert.True(relation.IsEpisodeContinuity));
    }

    [Fact]
    public async Task GetAsync_ReturnsGlobalGraphWithoutLeakingViewerState()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var now = DateTimeOffset.UtcNow;
        var owner = TestUserFactory.Create(943, "owner@example.com");
        var other = TestUserFactory.Create(944, "other@example.com");
        var title = CreateTitle("Private", "TV", 12, 2024, now);
        var entry = CreateEntry(owner.Id, title, now);
        db.AddRange(owner, other, title, entry);
        await db.SaveChangesAsync();

        var graph = await new MediaFranchiseGraphService(db)
            .GetAsync(other.Id, title.Id, CancellationToken.None);

        Assert.NotNull(graph);
        Assert.False(Assert.Single(graph.Nodes).IsInLibrary);

        var anonymousGraph = await new MediaFranchiseGraphService(db)
            .GetAsync(null, title.Id, CancellationToken.None);

        Assert.NotNull(anonymousGraph);
        var anonymousNode = Assert.Single(anonymousGraph.Nodes);
        Assert.False(anonymousNode.IsInLibrary);
        Assert.Null(anonymousNode.ViewerStatus);
        Assert.Null(anonymousNode.ProgressEpisodes);
    }

    [Fact]
    public async Task GetAsync_MarksContinuityIncompleteWhenTraversalIsBounded()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var now = DateTimeOffset.UtcNow;
        var titles = Enumerable.Range(1, 10)
            .Select(index => CreateTitle($"Season {index}", "TV", 12, 2020 + index, now))
            .ToList();
        db.MediaTitles.AddRange(titles);
        db.MediaProviderLinks.AddRange(titles.Select((title, index) => CreateLink(title, $"bounded-{index}", now)));
        db.MediaTitleRelations.AddRange(titles
            .Zip(titles.Skip(1))
            .Select(pair => CreateRelation(pair.First, pair.Second, MediaRelationTypes.Sequel, now)));
        await db.SaveChangesAsync();

        var graph = await new MediaFranchiseGraphService(db)
            .GetAsync(null, titles[0].Id, CancellationToken.None);

        Assert.NotNull(graph);
        Assert.False(graph.Continuity.IsComplete);
        Assert.True(graph.Continuity.OrderedMediaTitleIds.Count < titles.Count);
    }

    private static async Task<(ApplicationDbContext Db, SqliteConnection Connection)> CreateDbAsync()
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

    private static MediaTitle CreateTitle(string title, string format, int episodes, int year, DateTimeOffset now)
        => new()
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = title,
            SortTitle = title,
            MediaKind = MediaKinds.Anime,
            Format = format,
            EpisodeCount = episodes,
            StartYear = year,
            SupportsEpisodeProgress = true,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static MediaProviderLink CreateLink(MediaTitle title, string externalId, DateTimeOffset now)
        => new()
        {
            Id = Guid.NewGuid(),
            MediaTitleId = title.Id,
            MediaTitle = title,
            Provider = "anilist",
            ExternalId = externalId,
            ExternalUrl = $"https://anilist.co/anime/{externalId}",
            LinkSource = MediaMappingSources.Automatic,
            LastVerifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static MediaLibraryEntry CreateEntry(int userId, MediaTitle title, DateTimeOffset now)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            MediaTitleId = title.Id,
            MediaTitle = title,
            Status = MediaLibraryStatuses.Current,
            ProgressEpisodes = 2,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static MediaTitleRelation CreateRelation(
        MediaTitle source,
        MediaTitle target,
        string relationType,
        DateTimeOffset now)
        => new()
        {
            Id = Guid.NewGuid(),
            MediaTitleId = source.Id,
            RelatedMediaTitleId = target.Id,
            RelationType = relationType,
            SourceProvider = "anilist",
            FirstSeenAt = now,
            LastVerifiedAt = now
        };
}
