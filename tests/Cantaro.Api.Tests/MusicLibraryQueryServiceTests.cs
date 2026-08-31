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
        var account = new ConnectedServiceAccount
        {
            UserId = userA.Id,
            Service = "youtube",
            ExternalAccountId = "youtube-music-a",
            CreatedAt = now.UtcDateTime,
            UpdatedAt = now.UtcDateTime
        };
        db.ConnectedServiceAccounts.Add(account);
        var track = new Track
        {
            Id = Guid.NewGuid(),
            Isrc = "US-ABC-12-34567",
            CanonicalMetadata = JsonSerializer.Serialize(new TrackCanonicalMetadata
            {
                Title = "Canonical Song",
                Artist = "Canonical Artist",
                Albums = ["Studio Album", "Anniversary Edition"],
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
        track.SourceIds.Add(new TrackSourceId
        {
            Id = Guid.NewGuid(), TrackId = track.Id, SourceType = " MusicBrainz ", ExternalId = " recording-1 "
        });
        track.SourceIds.Add(new TrackSourceId
        {
            Id = Guid.NewGuid(), TrackId = track.Id, SourceType = "youtube", ExternalId = "video-1"
        });
        var primaryArtist = new Artist
        {
            Id = Guid.NewGuid(), Name = "Canonical Artist", SortName = "Artist, Canonical",
            MusicBrainzArtistId = "0d5f4b04-3be8-427a-8c24-23b1454b2f31", CreatedAt = now, UpdatedAt = now
        };
        var featuredArtist = new Artist
        {
            Id = Guid.NewGuid(), Name = "Guest Artist", CreatedAt = now, UpdatedAt = now
        };
        track.ArtistCredits.Add(new TrackArtistCredit
        {
            Id = Guid.NewGuid(), TrackId = track.Id, ArtistId = featuredArtist.Id, Artist = featuredArtist,
            Role = TrackArtistRole.Featured, Position = 1, CreditedName = "feat. Guest Artist"
        });
        track.ArtistCredits.Add(new TrackArtistCredit
        {
            Id = Guid.NewGuid(), TrackId = track.Id, ArtistId = primaryArtist.Id, Artist = primaryArtist,
            Role = TrackArtistRole.Primary, Position = 0, CreditedName = "Canonical Artist"
        });

        var firstPlaylist = MakePlaylist(userA.Id, "Road songs", now);
        var secondPlaylist = MakePlaylist(userA.Id, "Favorites", now);
        var otherUserPlaylist = MakePlaylist(userB.Id, "Other library", now);

        firstPlaylist.ServiceMappings.Add(MakeMapping(firstPlaylist.Id, account, "youtube", "yt-road", now));
        secondPlaylist.ServiceMappings.Add(MakeMapping(secondPlaylist.Id, account, "youtube", "yt-faves", now));
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
        Assert.Collection(
            song.ArtistCredits,
            credit =>
            {
                Assert.Equal("primary", credit.Role);
                Assert.Equal("Canonical Artist", credit.CreditedName);
                Assert.Equal(primaryArtist.MusicBrainzArtistId, credit.MusicBrainzArtistId);
            },
            credit =>
            {
                Assert.Equal("featured", credit.Role);
                Assert.Equal("feat. Guest Artist", credit.CreditedName);
            });
        Assert.Equal(["Studio Album", "Anniversary Edition"], song.Albums);
        Assert.Null(song.MatchStatus);
        Assert.Equal(["musicbrainz", "youtube"], song.SourcePlatforms);
        Assert.Equal(3, song.SourceIdentities.Count);
        Assert.Contains(song.SourceIdentities, identity => identity.Source == "isrc" && identity.ExternalId == "US-ABC-12-34567");
        Assert.Single(song.SourceIdentities, identity => identity.Source == "musicbrainz" && identity.ExternalId == "recording-1");
        Assert.Equal(3, song.PlatformLinks.Count);
        Assert.Contains(song.PlatformLinks, link => link.Url == "https://musicbrainz.org/recording/recording-1");
        Assert.Contains(song.PlatformLinks, link => link.Url == "https://www.youtube.com/watch?v=video-1");
        Assert.Contains(song.PlatformLinks, link => link.Url == "https://music.youtube.com/watch?v=video-1");
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
        Assert.Empty(song.Albums);
        Assert.Equal(["youtube"], song.SourcePlatforms);
        var identity = Assert.Single(song.SourceIdentities);
        Assert.Equal("youtube", identity.Source);
        Assert.Equal("video-1", identity.ExternalId);
        Assert.Equal(2, song.PlatformLinks.Count);
    }

    [Fact]
    public async Task GetLibraryAsync_ExcludesEntriesMarkedAsNotMusic()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var now = DateTimeOffset.UtcNow;
        var user = TestUserFactory.Create(707, "not-music-library@example.com");
        var playlist = MakePlaylist(user.Id, "Coding", now);
        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(),
            SourceType = "youtube",
            ExternalId = "coding-talk",
            Title = "Deep dive on LINQ",
            MatchStatus = TrackMatchingStatuses.NotMusic,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.AddRange(user, playlist, observation);
        await db.SaveChangesAsync();
        db.PlaylistEntries.Add(MakeEntry(playlist.Id, null, observation.Id, 0, "youtube", now));
        await db.SaveChangesAsync();

        var result = await new MusicLibraryQueryService(db).GetLibraryAsync(user.Id, CancellationToken.None);

        Assert.Empty(result.Songs);
        Assert.Equal(0, Assert.Single(result.Playlists).EntryCount);
    }

    [Fact]
    public async Task AddCanonicalSongToPlaylistAsync_IsUserScopedAndIdempotent()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var now = DateTimeOffset.UtcNow;
        var owner = TestUserFactory.Create(704, "owner@example.com");
        var other = TestUserFactory.Create(705, "other@example.com");
        var playlist = MakePlaylist(owner.Id, "Owner playlist", now);
        var track = new Track { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now };
        db.AddRange(owner, other, playlist, track);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var service = new MusicLibraryQueryService(db);

        Assert.False(await service.AddCanonicalSongToPlaylistAsync(track.Id, playlist.Id, other.Id, default));
        Assert.True(await service.AddCanonicalSongToPlaylistAsync(track.Id, playlist.Id, owner.Id, default));
        Assert.True(await service.AddCanonicalSongToPlaylistAsync(track.Id, playlist.Id, owner.Id, default));

        Assert.Equal(1, await db.PlaylistEntries.CountAsync(x => x.PlaylistId == playlist.Id && x.TrackId == track.Id));
    }

    [Fact]
    public async Task RemoveCanonicalSongFromPlaylistAsync_ReindexesRemainingEntriesAndIsIdempotent()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var now = DateTimeOffset.UtcNow;
        var owner = TestUserFactory.Create(706, "remove@example.com");
        var playlist = MakePlaylist(owner.Id, "Playlist", now);
        var first = new Track { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now };
        var removed = new Track { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now };
        var last = new Track { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now };
        db.AddRange(owner, playlist, first, removed, last);
        await db.SaveChangesAsync();
        db.PlaylistEntries.AddRange(
            MakeEntry(playlist.Id, first.Id, null, 2, "cantaro", now),
            MakeEntry(playlist.Id, removed.Id, null, 5, "cantaro", now),
            MakeEntry(playlist.Id, last.Id, null, 9, "cantaro", now));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var service = new MusicLibraryQueryService(db);

        Assert.True(await service.RemoveCanonicalSongFromPlaylistAsync(removed.Id, playlist.Id, owner.Id, default));
        Assert.True(await service.RemoveCanonicalSongFromPlaylistAsync(removed.Id, playlist.Id, owner.Id, default));

        var entries = await db.PlaylistEntries.Where(x => x.PlaylistId == playlist.Id).OrderBy(x => x.Position).ToListAsync();
        Assert.Equal([0, 1], entries.Select(x => x.Position));
        Assert.DoesNotContain(entries, x => x.TrackId == removed.Id);
    }

    [Fact]
    public async Task GetCanonicalSongAsync_DoesNotExposeTrackOutsideUsersLibrary()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var now = DateTimeOffset.UtcNow;
        var owner = TestUserFactory.Create(707, "song-owner@example.com");
        var other = TestUserFactory.Create(708, "song-other@example.com");
        var ownerPlaylist = MakePlaylist(owner.Id, "Private", now);
        var track = new Track
        {
            Id = Guid.NewGuid(),
            CanonicalMetadata = JsonSerializer.Serialize(
                new TrackCanonicalMetadata { Title = "Private song" }),
            CreatedAt = now,
            UpdatedAt = now
        };
        db.AddRange(owner, other, ownerPlaylist, track);
        await db.SaveChangesAsync();
        db.PlaylistEntries.Add(MakeEntry(ownerPlaylist.Id, track.Id, null, 0, "cantaro", now));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var service = new MusicLibraryQueryService(db);

        Assert.Null(await service.GetCanonicalSongAsync(track.Id, other.Id, default));
        Assert.NotNull(await service.GetCanonicalSongAsync(track.Id, owner.Id, default));
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

    private static ServicePlaylistMapping MakeMapping(
        Guid playlistId,
        ConnectedServiceAccount account,
        string service,
        string servicePlaylistId,
        DateTimeOffset now)
    {
        return new ServicePlaylistMapping
        {
            Id = Guid.NewGuid(),
            PlaylistId = playlistId,
            ConnectedServiceAccount = account,
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
