using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class CantaroSearchServiceTests
{
    [Fact]
    public async Task SearchAsync_ReturnsOwnedRankedGroupsWithCanonicalRoutes()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;

        var now = DateTimeOffset.UtcNow;
        var owner = TestUserFactory.Create(801, "search-owner@example.com");
        var other = TestUserFactory.Create(802, "search-other@example.com");
        db.Users.AddRange(owner, other);

        var exactTrack = MakeTrack("Echo", "First Artist", "https://img.example/echo.jpg", now);
        var prefixTrack = MakeTrack("Echoes", "Second Artist", null, now);
        var containsTrack = MakeTrack("Midnight Echo", "Third Artist", null, now);
        var artistTrack = MakeTrack("A Different Song", "Echo", null, now);
        var otherTrack = MakeTrack("Echo Private", "Other Artist", null, now);
        var unreachableTrack = MakeTrack("Echo Orphan", "Nobody", null, now);
        db.Tracks.AddRange(exactTrack, prefixTrack, containsTrack, artistTrack, otherTrack, unreachableTrack);

        var exactPlaylist = MakePlaylist(owner.Id, "Echo", "Exact playlist", now);
        var ownedPlaylist = MakePlaylist(owner.Id, "Echo collection", "An owned collection", now);
        var otherPlaylist = MakePlaylist(other.Id, "Echo private", "Must stay private", now);
        db.Playlists.AddRange(exactPlaylist, ownedPlaylist, otherPlaylist);
        await db.SaveChangesAsync();

        db.PlaylistEntries.AddRange(
            MakeEntry(exactPlaylist.Id, exactTrack.Id, 0, now),
            MakeEntry(exactPlaylist.Id, prefixTrack.Id, 1, now),
            MakeEntry(exactPlaylist.Id, containsTrack.Id, 2, now),
            MakeEntry(exactPlaylist.Id, artistTrack.Id, 3, now),
            MakeEntry(otherPlaylist.Id, otherTrack.Id, 0, now));

        var exactMedia = MakeMediaTitle("Echo", "anime", 2024, now);
        var prefixMedia = MakeMediaTitle("Echo Point", "movie", 2023, now);
        var otherMedia = MakeMediaTitle("Echo Private", "series", 2022, now);
        db.MediaTitles.AddRange(exactMedia, prefixMedia, otherMedia);
        await db.SaveChangesAsync();

        var exactMediaEntry = MakeMediaEntry(owner.Id, exactMedia.Id, "watching", now);
        var prefixMediaEntry = MakeMediaEntry(owner.Id, prefixMedia.Id, "planned", now);
        var otherMediaEntry = MakeMediaEntry(other.Id, otherMedia.Id, "watching", now);
        db.MediaLibraryEntries.AddRange(exactMediaEntry, prefixMediaEntry, otherMediaEntry);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await CreateService(db).SearchAsync(owner.Id, "  ECHO  ", 3, default);

        Assert.Equal("ECHO", result.Query);
        Assert.Equal(SearchGroupStatuses.Ok, result.Groups.Songs.Status);
        Assert.True(result.Groups.Songs.HasMore);
        Assert.Equal(
            [exactTrack.Id, prefixTrack.Id, containsTrack.Id],
            result.Groups.Songs.Items.Select(item => Guid.Parse(item.Id["track:".Length..])));
        var song = result.Groups.Songs.Items[0];
        Assert.Equal("song", song.EntityType);
        Assert.Equal("First Artist", song.Subtitle);
        Assert.Equal("https://img.example/echo.jpg", song.ArtworkUrl);
        Assert.Equal($"/music/songs/track%3A{exactTrack.Id}", song.CanonicalRoute);
        Assert.DoesNotContain(result.Groups.Songs.Items, item => item.Id == $"track:{otherTrack.Id}");
        Assert.DoesNotContain(result.Groups.Songs.Items, item => item.Id == $"track:{unreachableTrack.Id}");

        Assert.Equal(SearchGroupStatuses.Unavailable, result.Groups.Artists.Status);
        Assert.Empty(result.Groups.Artists.Items);
        Assert.False(result.Groups.Artists.HasMore);

        Assert.Equal([exactPlaylist.Id, ownedPlaylist.Id],
            result.Groups.Playlists.Items.Select(item => Guid.Parse(item.Id)));
        Assert.All(result.Groups.Playlists.Items,
            item => Assert.StartsWith("/music/playlists/", item.CanonicalRoute));
        Assert.DoesNotContain(result.Groups.Playlists.Items, item => item.Id == otherPlaylist.Id.ToString());

        Assert.Equal([exactMediaEntry.Id, prefixMediaEntry.Id],
            result.Groups.Media.Items.Select(item => Guid.Parse(item.Id)));
        Assert.Equal("https://img.example/poster.jpg", result.Groups.Media.Items[0].ArtworkUrl);
        Assert.Equal($"/media/library/{exactMediaEntry.Id}", result.Groups.Media.Items[0].CanonicalRoute);
        Assert.DoesNotContain(result.Groups.Media.Items, item => item.Id == otherMediaEntry.Id.ToString());
    }

    [Fact]
    public async Task SearchAsync_ClampsLimitAndUsesDeterministicTies()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var now = DateTimeOffset.UtcNow;
        var owner = TestUserFactory.Create(803, "search-limit@example.com");
        var playlist = MakePlaylist(owner.Id, "Library", null, now);
        db.AddRange(owner, playlist);

        var tracks = Enumerable.Range(0, 22)
            .Select(index => MakeTrack($"Same match {index:00}", "Artist", null, now))
            .ToList();
        db.Tracks.AddRange(tracks);
        await db.SaveChangesAsync();
        db.PlaylistEntries.AddRange(tracks.Select((track, index) => MakeEntry(playlist.Id, track.Id, index, now)));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await CreateService(db).SearchAsync(owner.Id, "same", 1000, default);

        Assert.Equal(CantaroSearchService.MaximumLimitPerGroup, result.Groups.Songs.Items.Count);
        Assert.True(result.Groups.Songs.HasMore);
        Assert.Equal(
            Enumerable.Range(0, CantaroSearchService.MaximumLimitPerGroup)
                .Select(index => $"Same match {index:00}"),
            result.Groups.Songs.Items.Select(item => item.Title));
    }

    [Fact]
    public async Task SearchAsync_PropagatesCancellation()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateService(db).SearchAsync(1, "anything", 6, cancellation.Token));
    }

    [Fact]
    public async Task SearchAsync_ToleratesLegacyNullAlbumCollection()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var now = DateTimeOffset.UtcNow;
        var owner = TestUserFactory.Create(804, "search-legacy@example.com");
        var playlist = MakePlaylist(owner.Id, "Library", null, now);
        var track = new Track
        {
            Id = Guid.NewGuid(),
            CanonicalMetadata = """{"Title":"Legacy song","Artist":"Legacy artist","Albums":null}""",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.AddRange(owner, playlist, track);
        await db.SaveChangesAsync();
        db.PlaylistEntries.Add(MakeEntry(playlist.Id, track.Id, 0, now));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await CreateService(db).SearchAsync(owner.Id, "legacy", 6, default);

        var item = Assert.Single(result.Groups.Songs.Items);
        Assert.Equal("Legacy song", item.Title);
        Assert.Null(item.Detail);
    }

    [Fact]
    public async Task SaveChanges_DerivesSearchProjectionsAndToleratesMalformedMetadata()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var now = DateTimeOffset.UtcNow;
        var track = MakeTrack("  Searchable title  ", "  Display artist  ", null, now);
        var malformed = new Track
        {
            Id = Guid.NewGuid(),
            CanonicalMetadata = "{broken",
            CreatedAt = now,
            UpdatedAt = now
        };
        var nonObject = new Track
        {
            Id = Guid.NewGuid(),
            CanonicalMetadata = "[]",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Tracks.AddRange(track, malformed, nonObject);

        await db.SaveChangesAsync();

        Assert.Equal("Searchable title", track.SearchTitle);
        Assert.Equal("Display artist", track.SearchArtist);
        Assert.Null(malformed.SearchTitle);
        Assert.Null(malformed.SearchArtist);
        Assert.Null(nonObject.SearchTitle);
        Assert.Null(nonObject.SearchArtist);
    }

    [Fact]
    public void SearchController_RequiresAuthorization()
    {
        var attribute = Assert.Single(
            typeof(Controllers.SearchController)
                .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true));
        Assert.IsType<AuthorizeAttribute>(attribute);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SearchController_RejectsBlankQuery(string? query)
    {
        var controller = new Controllers.SearchController(null!, null!);

        var response = await controller.Search(query, 6);

        Assert.IsType<BadRequestObjectResult>(response.Result);
    }

    [Fact]
    public async Task SearchController_RejectsOverlongQuery()
    {
        var controller = new Controllers.SearchController(null!, null!);

        var response = await controller.Search(
            new string('x', CantaroSearchService.MaximumQueryLength + 1),
            6);

        Assert.IsType<BadRequestObjectResult>(response.Result);
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

    private static CantaroSearchService CreateService(ApplicationDbContext db) =>
        new(db, NullLogger<CantaroSearchService>.Instance);

    private static Track MakeTrack(
        string title,
        string artist,
        string? artworkUrl,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            CanonicalMetadata = JsonSerializer.Serialize(new TrackCanonicalMetadata
            {
                Title = title,
                Artist = artist,
                Albums = ["Search album"],
                ThumbnailUrl = artworkUrl
            }),
            CreatedAt = now,
            UpdatedAt = now
        };

    private static Playlist MakePlaylist(
        int userId,
        string name,
        string? description,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = name,
            Description = description,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static PlaylistEntry MakeEntry(
        Guid playlistId,
        Guid trackId,
        int position,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            PlaylistId = playlistId,
            TrackId = trackId,
            Position = position,
            AddedAt = now
        };

    private static MediaTitle MakeMediaTitle(
        string title,
        string kind,
        int startYear,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = title,
            MediaKind = kind,
            StartYear = startYear,
            PrimaryProgressDimension = "episodes",
            ReleaseStatusDimension = "episodes",
            CanonicalMetadata =
                """{"coverImage":{"medium":"https://img.example/medium.jpg","extraLarge":"https://img.example/poster.jpg"}}""",
            CreatedAt = now,
            UpdatedAt = now
        };

    private static MediaLibraryEntry MakeMediaEntry(
        int userId,
        Guid mediaTitleId,
        string status,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            MediaTitleId = mediaTitleId,
            Provider = "anilist",
            ProviderAccountId = $"account-{userId}",
            ProviderMediaId = Guid.NewGuid().ToString(),
            NormalizedStatus = status,
            CreatedAt = now,
            UpdatedAt = now
        };
}
