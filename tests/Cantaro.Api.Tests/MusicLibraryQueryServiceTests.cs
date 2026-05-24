using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cantaro.Api.Tests;

public class MusicLibraryQueryServiceTests
{
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

    [Fact]
    public async Task GetLibraryAsync_IsolatesUserAndDedupesResolvedTracks()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;

        var now = DateTimeOffset.UtcNow;
        var userA = TestUserFactory.Create(701, "music-a@example.com");
        var userB = TestUserFactory.Create(702, "music-b@example.com");
        db.Users.AddRange(userA, userB);
        var track = new Track
        {
            Id = Guid.NewGuid(),
            CanonicalMetadata = JsonSerializer.Serialize(new TrackCanonicalMetadata
            {
                Title = "Canonical Song",
                Artist = "Canonical Artist",
                ThumbnailUrl = "https://img.example/song.jpg",
                DurationSeconds = 214
            }),
            CreatedAt = now,
            UpdatedAt = now
        };
        track.SourceIds.Add(new TrackSourceId
        {
            Id = Guid.NewGuid(),
            TrackId = track.Id,
            SourceType = "musicbrainz",
            ExternalId = "recording-1"
        });

        var firstPlaylist = MakePlaylist(userA.Id, "Road songs", now);
        var secondPlaylist = MakePlaylist(userA.Id, "Favorites", now);
        var otherUserPlaylist = MakePlaylist(userB.Id, "Other library", now);

        firstPlaylist.ServiceMappings.Add(MakeMapping(firstPlaylist.Id, "youtube", "yt-road", now));
        secondPlaylist.ServiceMappings.Add(MakeMapping(secondPlaylist.Id, "youtube", "yt-faves", now));
        db.Tracks.Add(track);
        db.Playlists.AddRange(firstPlaylist, secondPlaylist, otherUserPlaylist);
        await db.SaveChangesAsync();

        db.PlaylistEntries.AddRange(
            MakeEntry(firstPlaylist.Id, track.Id, null, 0, "youtube", now),
            MakeEntry(secondPlaylist.Id, track.Id, null, 3, "youtube", now),
            MakeEntry(otherUserPlaylist.Id, track.Id, null, 0, "youtube", now));
        await db.SaveChangesAsync();

        var result = await new MusicLibraryQueryService(db).GetLibraryAsync(userA.Id, CancellationToken.None);

        Assert.Equal(1, result.Summary.SongCount);
        Assert.Equal(2, result.Summary.PlaylistCount);

        var song = Assert.Single(result.Songs);
        Assert.Equal($"track:{track.Id}", song.Id);
        Assert.Equal("Canonical Song", song.Title);
        Assert.Equal("Canonical Artist", song.Artist);
        Assert.Null(song.MatchStatus);
        Assert.Equal(["musicbrainz", "youtube"], song.SourcePlatforms);
        Assert.Equal(2, song.Playlists.Count);
    }

    [Fact]
    public async Task GetLibraryAsync_FallsBackToObservationMetadataForUnresolvedSongs()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;

        var now = DateTimeOffset.UtcNow;
        var user = TestUserFactory.Create(703, "music-fallback@example.com");
        db.Users.Add(user);

        var playlist = MakePlaylist(user.Id, "Needs review", now);
        db.Playlists.Add(playlist);

        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(),
            SourceType = "youtube",
            ExternalId = "video-1",
            Title = "Channel - Raw Video Title",
            Artist = "Channel",
            RawMetadata = JsonSerializer.Serialize(new TrackObservationMetadata
            {
                OriginalTitle = "Channel - Better Title",
                ThumbnailUrl = "https://img.example/video.jpg",
                DurationSeconds = 189
            }),
            MatchStatus = TrackMatchingStatuses.Ambiguous,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.TrackObservations.Add(observation);
        await db.SaveChangesAsync();

        db.PlaylistEntries.Add(MakeEntry(playlist.Id, null, observation.Id, 0, null, now));
        await db.SaveChangesAsync();

        var result = await new MusicLibraryQueryService(db).GetLibraryAsync(user.Id, CancellationToken.None);

        var song = Assert.Single(result.Songs);
        Assert.Equal($"observation:{observation.Id}", song.Id);
        Assert.Equal("Better Title", song.Title);
        Assert.Equal("Channel", song.Artist);
        Assert.Equal("https://img.example/video.jpg", song.ThumbnailUrl);
        Assert.Equal(189, song.DurationSeconds);
        Assert.Equal(TrackMatchingStatuses.Ambiguous, song.MatchStatus);
        Assert.Equal(["youtube"], song.SourcePlatforms);
    }

    private static Playlist MakePlaylist(int userId, string name, DateTimeOffset now)
    {
        return new Playlist
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = name,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static ServicePlaylistMapping MakeMapping(Guid playlistId, string service, string servicePlaylistId, DateTimeOffset now)
    {
        return new ServicePlaylistMapping
        {
            Id = Guid.NewGuid(),
            PlaylistId = playlistId,
            Service = service,
            ServicePlaylistId = servicePlaylistId,
            SyncMode = "import_only",
            LastSyncedAt = now,
            LastSyncStatus = "success"
        };
    }

    private static PlaylistEntry MakeEntry(
        Guid playlistId,
        Guid? trackId,
        Guid? observationId,
        int position,
        string? sourceService,
        DateTimeOffset now)
    {
        return new PlaylistEntry
        {
            Id = Guid.NewGuid(),
            PlaylistId = playlistId,
            TrackId = trackId,
            TrackObservationId = observationId,
            Position = position,
            SourceService = sourceService,
            AddedAt = now
        };
    }
}
