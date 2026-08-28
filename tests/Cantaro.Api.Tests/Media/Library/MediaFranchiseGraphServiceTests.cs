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
        seasonTwo.BackgroundUrl = "https://example.test/season-two-banner.jpg";
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
        Assert.Equal(
            seasonTwo.BackgroundUrl,
            graph.Nodes.Single(node => node.MediaTitleId == seasonTwo.Id).BackgroundUrl);
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
    public async Task GetAsync_MarksContinuityIncompleteWhenARelatedSourceWasNotRefreshed()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var now = DateTimeOffset.UtcNow;
        var first = CreateTitle("First", "TV", 12, 2024, now);
        var second = CreateTitle("Second", "TV", 12, 2025, now);
        var firstLink = CreateLink(first, "coverage-1", now);
        var secondLink = CreateLink(second, "coverage-2", now);
        secondLink.RelationsLastVerifiedAt = null;
        db.AddRange(first, second, firstLink, secondLink);
        db.MediaTitleRelations.Add(CreateRelation(first, second, MediaRelationTypes.Sequel, now));
        await db.SaveChangesAsync();

        var graph = await new MediaFranchiseGraphService(db)
            .GetAsync(null, first.Id, CancellationToken.None);

        Assert.NotNull(graph);
        Assert.False(graph.Continuity.IsComplete);
        Assert.Equal([first.Id, second.Id], graph.Continuity.OrderedMediaTitleIds);
    }

    [Fact]
    public async Task GetAsync_MarksContinuityIncompleteWhenSourcesComeFromDifferentSnapshots()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var now = DateTimeOffset.UtcNow;
        var first = CreateTitle("First", "TV", 12, 2024, now);
        var second = CreateTitle("Second", "TV", 12, 2025, now);
        var firstLink = CreateLink(first, "snapshot-1", now);
        var secondLink = CreateLink(second, "snapshot-2", now);
        secondLink.RelationsSnapshotId = Guid.NewGuid();
        db.AddRange(first, second, firstLink, secondLink);
        db.MediaTitleRelations.Add(CreateRelation(first, second, MediaRelationTypes.Sequel, now));
        await db.SaveChangesAsync();

        var graph = await new MediaFranchiseGraphService(db)
            .GetAsync(null, first.Id, CancellationToken.None);

        Assert.NotNull(graph);
        Assert.False(graph.Continuity.IsComplete);
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

    [Fact]
    public async Task GetAsync_DoesNotMergeFranchisesThroughASharedBranchNode()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var now = DateTimeOffset.UtcNow;
        var slime = CreateTitle("Slime", "TV", 24, 2018, now);
        var slimeSeasonTwo = CreateTitle("Slime season two", "TV", 24, 2021, now);
        var attackOnTitan = CreateTitle("Attack on Titan", "TV", 25, 2013, now);
        var sharedCharacterStory = CreateTitle("Shared character short", "ONA", 1, 2020, now);
        db.MediaTitles.AddRange(slime, slimeSeasonTwo, attackOnTitan, sharedCharacterStory);
        db.MediaProviderLinks.AddRange(
            CreateLink(slime, "shared-1", now),
            CreateLink(slimeSeasonTwo, "shared-2", now),
            CreateLink(attackOnTitan, "shared-3", now),
            CreateLink(sharedCharacterStory, "shared-4", now));
        db.MediaTitleRelations.AddRange(
            CreateRelation(slime, slimeSeasonTwo, MediaRelationTypes.Sequel, now),
            CreateRelation(slime, sharedCharacterStory, MediaRelationTypes.Character, now),
            CreateRelation(attackOnTitan, sharedCharacterStory, MediaRelationTypes.Character, now));
        await db.SaveChangesAsync();

        var graph = await new MediaFranchiseGraphService(db)
            .GetAsync(null, slime.Id, CancellationToken.None);

        Assert.NotNull(graph);
        Assert.Contains(graph.Nodes, node => node.MediaTitleId == sharedCharacterStory.Id);
        Assert.DoesNotContain(graph.Nodes, node => node.MediaTitleId == attackOnTitan.Id);
        Assert.DoesNotContain(graph.Relations, relation =>
            relation.SourceMediaTitleId == attackOnTitan.Id
            || relation.TargetMediaTitleId == attackOnTitan.Id);
    }

    [Fact]
    public async Task GetAsync_UsesSnapshotMatchedParentForABranchOnlyRoot()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var now = DateTimeOffset.UtcNow;
        var seasonOne = CreateTitle("Season one", "TV", 24, 2018, now);
        var seasonTwo = CreateTitle("Season two", "TV", 12, 2021, now);
        var characterShort = CreateTitle("Character short", "ONA", 1, 2020, now);
        var unrelated = CreateTitle("Unrelated series", "TV", 12, 2020, now);
        var seasonOneLink = CreateLink(seasonOne, "branch-parent", now);
        var seasonTwoLink = CreateLink(seasonTwo, "branch-sequel", now);
        var characterShortLink = CreateLink(characterShort, "branch-short", now);
        var unrelatedLink = CreateLink(unrelated, "branch-unrelated", now);
        unrelatedLink.RelationsSnapshotId = Guid.NewGuid();
        db.AddRange(
            seasonOne,
            seasonTwo,
            characterShort,
            unrelated,
            seasonOneLink,
            seasonTwoLink,
            characterShortLink,
            unrelatedLink);
        db.MediaTitleRelations.AddRange(
            CreateRelation(seasonOne, seasonTwo, MediaRelationTypes.Sequel, now),
            CreateRelation(seasonOne, characterShort, MediaRelationTypes.Character, now),
            CreateRelation(unrelated, characterShort, MediaRelationTypes.Character, now));
        await db.SaveChangesAsync();

        var graph = await new MediaFranchiseGraphService(db)
            .GetAsync(null, characterShort.Id, CancellationToken.None);

        Assert.NotNull(graph);
        Assert.Contains(graph.Nodes, node => node.MediaTitleId == seasonOne.Id);
        Assert.Contains(graph.Nodes, node => node.MediaTitleId == seasonTwo.Id);
        Assert.Contains(graph.Nodes, node => node.MediaTitleId == characterShort.Id);
        Assert.DoesNotContain(graph.Nodes, node => node.MediaTitleId == unrelated.Id);
    }

    [Fact]
    public async Task GetAsync_ConnectsTvSeasonsAcrossAnOvaWithoutPuttingTheOvaInTheEpisodeLane()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var now = DateTimeOffset.UtcNow;
        var seasonOne = CreateTitle("Season one", "TV", 24, 2018, now);
        var bridgeOva = CreateTitle("Bridge OVA", "OVA", 3, 2023, now);
        var seasonTwo = CreateTitle("Season two", "TV", 24, 2021, now);
        db.MediaTitles.AddRange(seasonOne, bridgeOva, seasonTwo);
        db.MediaProviderLinks.AddRange(
            CreateLink(seasonOne, "bridge-1", now),
            CreateLink(bridgeOva, "bridge-ova", now),
            CreateLink(seasonTwo, "bridge-2", now));
        db.MediaTitleRelations.AddRange(
            CreateRelation(seasonOne, bridgeOva, MediaRelationTypes.Sequel, now),
            CreateRelation(bridgeOva, seasonTwo, MediaRelationTypes.Sequel, now));
        await db.SaveChangesAsync();

        var graph = await new MediaFranchiseGraphService(db)
            .GetAsync(null, seasonTwo.Id, CancellationToken.None);

        Assert.NotNull(graph);
        Assert.Equal(
            [seasonOne.Id, seasonTwo.Id],
            graph.Continuity.OrderedMediaTitleIds);
        Assert.Equal(0, graph.Continuity.EpisodeOffsetByMediaTitleId[seasonOne.Id]);
        Assert.Equal(24, graph.Continuity.EpisodeOffsetByMediaTitleId[seasonTwo.Id]);
        Assert.DoesNotContain(bridgeOva.Id, graph.Continuity.EpisodeOffsetByMediaTitleId.Keys);
        Assert.All(graph.Relations, relation => Assert.False(relation.IsEpisodeContinuity));
    }

    [Fact]
    public async Task GetAsync_ReturnsTheSameStructuralFranchiseFromAnimeMangaAndMovieRoots()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var now = DateTimeOffset.UtcNow;
        var manga = CreateTitle("Source manga", "MANGA", 0, 2014, now);
        manga.MediaKind = MediaKinds.Manga;
        var season = CreateTitle("Season", "TV", 24, 2018, now);
        var movie = CreateTitle("Movie", "MOVIE", 1, 2020, now);
        db.MediaTitles.AddRange(manga, season, movie);
        db.MediaProviderLinks.AddRange(
            CreateLink(manga, "stable-manga", now),
            CreateLink(season, "stable-season", now),
            CreateLink(movie, "stable-movie", now));
        db.MediaTitleRelations.AddRange(
            CreateRelation(season, manga, MediaRelationTypes.Source, now),
            CreateRelation(season, movie, MediaRelationTypes.Sequel, now));
        await db.SaveChangesAsync();

        var service = new MediaFranchiseGraphService(db);
        var fromAnime = await service.GetAsync(null, season.Id, CancellationToken.None);
        var fromManga = await service.GetAsync(null, manga.Id, CancellationToken.None);
        var fromMovie = await service.GetAsync(null, movie.Id, CancellationToken.None);

        Assert.NotNull(fromAnime);
        Assert.NotNull(fromManga);
        Assert.NotNull(fromMovie);
        var expectedIds = fromAnime.Nodes.Select(node => node.MediaTitleId).Order().ToArray();
        Assert.Equal(expectedIds, fromManga.Nodes.Select(node => node.MediaTitleId).Order().ToArray());
        Assert.Equal(expectedIds, fromMovie.Nodes.Select(node => node.MediaTitleId).Order().ToArray());
        Assert.Equal(
            fromAnime.Continuity.OrderedMediaTitleIds,
            fromManga.Continuity.OrderedMediaTitleIds);
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
            RelationsLastVerifiedAt = now,
            RelationsSnapshotId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
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
