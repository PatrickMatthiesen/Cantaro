using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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
            MbidRecording = "recording-1",
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
        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(), TrackId = track.Id, SourceType = " YouTube ", ExternalId = " video-1 ",
            Title = "Imported song", MatchStatus = TrackMatchingStatuses.Matched, CreatedAt = now, UpdatedAt = now
        };
        db.TrackObservations.Add(observation);
        db.Tracks.Add(track);
        db.Playlists.AddRange(firstPlaylist, secondPlaylist, otherUserPlaylist);
        await db.SaveChangesAsync();

        db.PlaylistEntries.AddRange(
            MakeEntry(firstPlaylist.Id, track.Id, observation.Id, 0, "youtube", now),
            MakeEntry(secondPlaylist.Id, track.Id, observation.Id, 3, "youtube", now),
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
        Assert.Equal(["isrc", "musicbrainz", "youtube"], song.SourcePlatforms);
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
    public async Task LocalEdits_AllowDuplicatesAndRemoveOnlySelectedOccurrence()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var now = DateTimeOffset.UtcNow;
        var owner = TestUserFactory.Create(709, "duplicate-owner@example.com");
        var playlist = MakePlaylist(owner.Id, "Duplicates", now);
        playlist.AllowDuplicateTracks = true;
        playlist.SyncEnabled = true;
        var track = new Track { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now };
        db.AddRange(owner, playlist, track);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var service = new MusicLibraryQueryService(db);

        Assert.True(await service.AddCanonicalSongToPlaylistAsync(track.Id, playlist.Id, owner.Id, default));
        Assert.True(await service.AddCanonicalSongToPlaylistAsync(track.Id, playlist.Id, owner.Id, default));
        var entries = await db.PlaylistEntries.Where(item => item.PlaylistId == playlist.Id)
            .OrderBy(item => item.Position).ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.Equal([0, 1], entries.Select(item => item.Position));
        Assert.True(await service.RemoveCanonicalSongFromPlaylistAsync(
            track.Id, playlist.Id, owner.Id, default, entries[0].Id));

        var remaining = Assert.Single(await db.PlaylistEntries.Where(item => item.PlaylistId == playlist.Id).ToListAsync());
        Assert.Equal(entries[1].Id, remaining.Id);
        Assert.Equal(0, remaining.Position);
        var updated = await db.Playlists.SingleAsync(item => item.Id == playlist.Id);
        Assert.Equal(3, updated.SyncRevision);
        Assert.NotNull(updated.NextSyncAt);
        Assert.InRange(updated.NextSyncAt.Value, now.AddHours(23), DateTimeOffset.UtcNow.AddHours(25));
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

    [Fact]
    public async Task GetCanonicalSongAsync_PreservesUserScopedObservationMetadataFallbacks()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var now = DateTimeOffset.UtcNow;
        var firstUser = TestUserFactory.Create(722, "first-observation@example.com");
        var secondUser = TestUserFactory.Create(723, "second-observation@example.com");
        var firstPlaylist = MakePlaylist(firstUser.Id, "First library", now);
        var secondPlaylist = MakePlaylist(secondUser.Id, "Second library", now);
        var track = new Track
        {
            Id = Guid.NewGuid(),
            CanonicalMetadata = JsonSerializer.Serialize(new TrackCanonicalMetadata { Title = "Canonical song" }),
            CreatedAt = now,
            UpdatedAt = now
        };
        TrackObservation MakeObservation(string externalId, string artwork, int duration) => new()
        {
            Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = externalId,
            Title = "Observed song", MatchStatus = TrackMatchingStatuses.Matched, TrackId = track.Id,
            RawMetadata = JsonSerializer.Serialize(new TrackObservationMetadata
            {
                ThumbnailUrl = artwork, DurationSeconds = duration
            }),
            CreatedAt = now, UpdatedAt = now
        };
        var firstObservation = MakeObservation("first-video", "https://img.example/first.jpg", 189);
        var secondObservation = MakeObservation("second-video", "https://img.example/second.jpg", 205);
        db.AddRange(firstUser, secondUser, firstPlaylist, secondPlaylist, track, firstObservation, secondObservation);
        await db.SaveChangesAsync();
        db.PlaylistEntries.AddRange(
            MakeEntry(firstPlaylist.Id, track.Id, firstObservation.Id, 0, "youtube", now),
            MakeEntry(secondPlaylist.Id, track.Id, secondObservation.Id, 0, "youtube", now));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var service = new MusicLibraryQueryService(db);
        var firstLibrarySong = Assert.Single((await service.GetLibraryAsync(firstUser.Id, default)).Songs);
        var firstDetail = Assert.IsType<MusicLibrarySongDto>(await service.GetCanonicalSongAsync(track.Id, firstUser.Id, default));
        var secondDetail = Assert.IsType<MusicLibrarySongDto>(await service.GetCanonicalSongAsync(track.Id, secondUser.Id, default));

        Assert.Equal(189, firstLibrarySong.DurationSeconds);
        Assert.Equal(firstLibrarySong.DurationSeconds, firstDetail.DurationSeconds);
        Assert.Equal(firstLibrarySong.ThumbnailUrl, firstDetail.ThumbnailUrl);
        Assert.Equal("https://img.example/first.jpg", firstDetail.ThumbnailUrl);
        Assert.Equal(205, secondDetail.DurationSeconds);
        Assert.Equal("https://img.example/second.jpg", secondDetail.ThumbnailUrl);
    }

    [Fact]
    public async Task GetCanonicalSongAsync_PreservesAllOwnedObservationIdentitiesAndPlatformLinks()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var now = DateTimeOffset.UtcNow;
        var owner = TestUserFactory.Create(724, "identity-owner@example.com");
        var other = TestUserFactory.Create(725, "identity-other@example.com");
        var firstPlaylist = MakePlaylist(owner.Id, "First", now);
        var secondPlaylist = MakePlaylist(owner.Id, "Second", now);
        var privatePlaylist = MakePlaylist(other.Id, "Private", now);
        var track = new Track
        {
            Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now,
            Isrc = "USABC1234567", MbidRecording = "recording-id",
            CanonicalMetadata = JsonSerializer.Serialize(new TrackCanonicalMetadata { Title = "Song" })
        };
        track.SourceIds.Add(new TrackSourceId
        {
            Id = Guid.NewGuid(), TrackId = track.Id, SourceType = "youtube", ExternalId = "video-one"
        });
        TrackObservation Observation(string source, string externalId) => new()
        {
            Id = Guid.NewGuid(), SourceType = source, ExternalId = externalId, Title = "Observed song",
            TrackId = track.Id, MatchStatus = TrackMatchingStatuses.Matched, CreatedAt = now, UpdatedAt = now
        };
        var duplicate = Observation(" YouTube ", " video-one ");
        var alternate = Observation("youtube", "video-two");
        var spotify = Observation("spotify", "spotify-id");
        var privateObservation = Observation("youtube", "private-video");
        track.SourceIds.Add(new TrackSourceId
        {
            Id = Guid.NewGuid(), TrackId = track.Id, SourceType = "youtube", ExternalId = privateObservation.ExternalId
        });
        track.SourceIds.Add(new TrackSourceId
        {
            Id = Guid.NewGuid(), TrackId = track.Id, SourceType = "unclassified", ExternalId = "unowned-global-id"
        });
        db.AddRange(owner, other, firstPlaylist, secondPlaylist, privatePlaylist, track,
            duplicate, alternate, spotify, privateObservation);
        await db.SaveChangesAsync();
        db.PlaylistEntries.AddRange(
            MakeEntry(firstPlaylist.Id, track.Id, duplicate.Id, 0, "youtube", now),
            MakeEntry(secondPlaylist.Id, track.Id, alternate.Id, 0, "youtube", now),
            MakeEntry(secondPlaylist.Id, track.Id, alternate.Id, 1, "youtube", now),
            MakeEntry(secondPlaylist.Id, track.Id, spotify.Id, 2, "spotify", now),
            MakeEntry(privatePlaylist.Id, track.Id, privateObservation.Id, 0, "youtube", now));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var service = new MusicLibraryQueryService(db);
        var librarySong = Assert.Single((await service.GetLibraryAsync(owner.Id, default)).Songs);
        var detail = Assert.IsType<MusicLibrarySongDto>(await service.GetCanonicalSongAsync(track.Id, owner.Id, default));

        Assert.Equal(librarySong.SourceIdentities.Select(item => (item.Source, item.ExternalId)),
            detail.SourceIdentities.Select(item => (item.Source, item.ExternalId)));
        Assert.Equal(5, librarySong.SourceIdentities.Count);
        Assert.Equal(["isrc", "musicbrainz", "spotify", "youtube"], librarySong.SourcePlatforms);
        Assert.Equal(librarySong.SourcePlatforms, detail.SourcePlatforms);
        Assert.Equal(5, detail.SourceIdentities.Count);
        Assert.Equal(new[] { "video-one", "video-two" },
            detail.SourceIdentities.Where(item => item.Source == "youtube").Select(item => item.ExternalId));
        Assert.Equal(new[] { "https://www.youtube.com/watch?v=video-one", "https://www.youtube.com/watch?v=video-two" },
            detail.PlatformLinks.Where(item => item.Platform == "youtube").Select(item => item.Url));
        Assert.Equal(new[] { "video-one", "video-two" },
            detail.PlatformLinks.Where(item => item.Platform == "youtube-music").Select(item => item.ExternalId));
        Assert.Contains(detail.PlatformLinks, item => item.Platform == "spotify"
            && item.Source == "spotify" && item.ExternalId == "spotify-id"
            && item.Url == "https://open.spotify.com/track/spotify-id");
        Assert.DoesNotContain(detail.SourceIdentities, item => item.ExternalId == "private-video");
        Assert.DoesNotContain(detail.PlatformLinks, item => item.ExternalId == "private-video");
        Assert.Equal(4, detail.Playlists.Count);

        var privateDetail = Assert.IsType<MusicLibrarySongDto>(await service.GetCanonicalSongAsync(track.Id, other.Id, default));
        Assert.Contains(privateDetail.SourceIdentities, item => item.ExternalId == "private-video");
        Assert.DoesNotContain(privateDetail.SourceIdentities, item => item.ExternalId is "video-one" or "video-two" or "spotify-id" or "unowned-global-id");
        var privateLibrarySong = Assert.Single((await service.GetLibraryAsync(other.Id, default)).Songs);
        Assert.Equal(privateDetail.SourceIdentities.Select(item => (item.Source, item.ExternalId)),
            privateLibrarySong.SourceIdentities.Select(item => (item.Source, item.ExternalId)));

        // Removing all owned occurrences of one variant revokes it even while global mappings and membership remain.
        var entriesToRemove = await db.PlaylistEntries.Where(entry => entry.PlaylistId == secondPlaylist.Id
            && entry.TrackObservationId == alternate.Id).ToListAsync();
        foreach (var entry in entriesToRemove)
            Assert.True(await service.RemoveCanonicalSongFromPlaylistAsync(track.Id, secondPlaylist.Id, owner.Id, default, entry.Id));
        Assert.True(await service.RemoveCanonicalSongFromPlaylistAsync(track.Id, firstPlaylist.Id, owner.Id, default));
        db.ChangeTracker.Clear();
        var afterRemoval = Assert.IsType<MusicLibrarySongDto>(await service.GetCanonicalSongAsync(track.Id, owner.Id, default));
        var libraryAfterRemoval = Assert.Single((await service.GetLibraryAsync(owner.Id, default)).Songs);
        var expected = new[] { ("isrc", track.Isrc!), ("musicbrainz", track.MbidRecording!), ("spotify", spotify.ExternalId) };
        Assert.Equal(expected, afterRemoval.SourceIdentities.Select(item => (item.Source, item.ExternalId)));
        Assert.Equal(expected, libraryAfterRemoval.SourceIdentities.Select(item => (item.Source, item.ExternalId)));
        Assert.Equal(["isrc", "musicbrainz", "spotify"], afterRemoval.SourcePlatforms);
        Assert.Equal(afterRemoval.SourcePlatforms, libraryAfterRemoval.SourcePlatforms);
        Assert.DoesNotContain(afterRemoval.PlatformLinks, item => item.Platform is "youtube" or "youtube-music");
        Assert.DoesNotContain(libraryAfterRemoval.PlatformLinks, item => item.Platform is "youtube" or "youtube-music");
        Assert.Equal(3, await db.TrackSourceIds.CountAsync());
        Assert.True(await db.TrackObservations.AnyAsync(item => item.Id == alternate.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetCanonicalSongAsync_FillsEachMissingFieldFromOrderedOwnedObservations(bool hasCanonicalMetadata)
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var now = DateTimeOffset.UtcNow;
        var owner = TestUserFactory.Create(726, "fallback-owner@example.com");
        var other = TestUserFactory.Create(727, "fallback-other@example.com");
        var canonicalOnlyPlaylist = MakePlaylist(owner.Id, "A canonical only", now);
        var observedPlaylist = MakePlaylist(owner.Id, "B observations", now);
        var privatePlaylist = MakePlaylist(other.Id, "A private", now);
        var track = new Track
        {
            Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now,
            CanonicalMetadata = JsonSerializer.Serialize(new TrackCanonicalMetadata
            {
                Title = "Canonical song", DurationSeconds = hasCanonicalMetadata ? 250 : null,
                ThumbnailUrl = hasCanonicalMetadata ? "https://img.example/canonical.jpg" : null
            })
        };
        TrackObservation Observation(string externalId) => new()
        {
            Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = externalId, Title = "Observed song",
            TrackId = track.Id, MatchStatus = TrackMatchingStatuses.Matched, CreatedAt = now, UpdatedAt = now
        };
        var empty = Observation("empty");
        var duration = Observation("duration");
        duration.RawMetadata = JsonSerializer.Serialize(new TrackObservationMetadata { DurationSeconds = 189 });
        var artwork = Observation("artwork");
        artwork.ThumbnailUrl = "https://img.example/owned.jpg";
        var later = Observation("later");
        later.DurationSeconds = 300;
        later.ThumbnailUrl = "https://img.example/later.jpg";
        // Canonical precedence requires artwork provenance on an attached owned observation.
        later.RawMetadata = JsonSerializer.Serialize(new TrackObservationMetadata
        {
            ThumbnailUrl = hasCanonicalMetadata ? "https://img.example/canonical.jpg" : null
        });
        var privateObservation = Observation("private");
        privateObservation.DurationSeconds = 400;
        privateObservation.ThumbnailUrl = "https://img.example/private.jpg";
        db.AddRange(owner, other, canonicalOnlyPlaylist, observedPlaylist, privatePlaylist,
            track, empty, duration, artwork, later, privateObservation);
        await db.SaveChangesAsync();
        // Insert in reverse display order to check deterministic fallback selection.
        db.PlaylistEntries.AddRange(
            MakeEntry(observedPlaylist.Id, track.Id, later.Id, 4, "youtube", now),
            MakeEntry(observedPlaylist.Id, track.Id, artwork.Id, 3, "youtube", now),
            MakeEntry(observedPlaylist.Id, track.Id, duration.Id, 2, "youtube", now),
            MakeEntry(observedPlaylist.Id, track.Id, empty.Id, 1, "youtube", now),
            MakeEntry(canonicalOnlyPlaylist.Id, track.Id, null, 0, "cantaro", now),
            MakeEntry(privatePlaylist.Id, track.Id, privateObservation.Id, 0, "youtube", now));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var service = new MusicLibraryQueryService(db);
        var detail = Assert.IsType<MusicLibrarySongDto>(await service.GetCanonicalSongAsync(track.Id, owner.Id, default));
        var librarySong = Assert.Single((await service.GetLibraryAsync(owner.Id, default)).Songs);
        Assert.Equal(detail.ThumbnailUrl, librarySong.ThumbnailUrl);
        Assert.Equal(detail.DurationSeconds, librarySong.DurationSeconds);

        Assert.Equal(hasCanonicalMetadata ? 250 : 189, detail.DurationSeconds);
        Assert.Equal(hasCanonicalMetadata ? "https://img.example/canonical.jpg" : "https://img.example/owned.jpg", detail.ThumbnailUrl);
        var stored = await db.Tracks.AsNoTracking().SingleAsync(item => item.Id == track.Id);
        Assert.Equal(track.CanonicalMetadata, stored.CanonicalMetadata);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task GetCanonicalSongAsync_RelatedRecordingsFillEachFieldFromOrderedOwnedObservations(
        bool hasCanonicalDuration, bool hasCanonicalArtwork)
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var now = DateTimeOffset.UtcNow;
        var owner = TestUserFactory.Create(728, "related-fallback-owner@example.com");
        var other = TestUserFactory.Create(729, "related-fallback-other@example.com");
        var firstPlaylist = MakePlaylist(owner.Id, "A canonical only", now);
        var observedPlaylist = MakePlaylist(owner.Id, "B observations", now);
        observedPlaylist.Id = Guid.Parse("00000000-0000-0000-0000-000000000100");
        var sameNamePlaylist = MakePlaylist(owner.Id, observedPlaylist.Name, now);
        sameNamePlaylist.Id = Guid.Parse("00000000-0000-0000-0000-000000000200");
        var laterSameNamePlaylist = MakePlaylist(owner.Id, observedPlaylist.Name, now);
        laterSameNamePlaylist.Id = Guid.Parse("00000000-0000-0000-0000-000000000300");
        var lastPlaylist = MakePlaylist(owner.Id, "C observations", now);
        lastPlaylist.Id = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var privatePlaylist = MakePlaylist(other.Id, "A private", now);
        var original = new Track { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now };
        var related = new Track
        {
            Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now,
            CanonicalMetadata = JsonSerializer.Serialize(new TrackCanonicalMetadata
            {
                Title = "Related song", DurationSeconds = hasCanonicalDuration ? 250 : null,
                ThumbnailUrl = hasCanonicalArtwork ? "https://img.example/canonical.jpg" : " "
            })
        };
        TrackObservation Observation(string externalId) => new()
        {
            Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = externalId, Title = "Observed song",
            TrackId = related.Id, MatchStatus = TrackMatchingStatuses.Matched, CreatedAt = now, UpdatedAt = now
        };
        var empty = Observation("empty");
        var duration = Observation("duration");
        duration.RawMetadata = JsonSerializer.Serialize(new TrackObservationMetadata { DurationSeconds = 189 });
        var artwork = Observation("artwork");
        artwork.ThumbnailUrl = "https://img.example/owned.jpg";
        var later = Observation("later");
        later.DurationSeconds = 300;
        later.RawMetadata = JsonSerializer.Serialize(new TrackObservationMetadata
        {
            ThumbnailUrl = hasCanonicalArtwork ? "https://img.example/canonical.jpg" : "https://img.example/later.jpg"
        });
        var privateObservation = Observation("private");
        privateObservation.DurationSeconds = 400;
        privateObservation.ThumbnailUrl = "https://img.example/private.jpg";
        db.AddRange(owner, other, firstPlaylist, observedPlaylist, sameNamePlaylist, laterSameNamePlaylist, lastPlaylist,
            privatePlaylist, original, related, empty, duration, artwork, later, privateObservation);
        db.TrackRelations.Add(new TrackRelation
        {
            FromTrackId = related.Id, ToTrackId = original.Id, RelationType = TrackRelationType.RemixOf
        });
        await db.SaveChangesAsync();
        // Competing values test position, playlist name and playlist ID ordering.
        db.PlaylistEntries.AddRange(
            MakeEntry(laterSameNamePlaylist.Id, related.Id, later.Id, 2, "youtube", now),
            MakeEntry(lastPlaylist.Id, related.Id, later.Id, 2, "youtube", now),
            MakeEntry(firstPlaylist.Id, related.Id, later.Id, 3, "youtube", now),
            MakeEntry(sameNamePlaylist.Id, related.Id, artwork.Id, 2, "youtube", now),
            MakeEntry(observedPlaylist.Id, related.Id, duration.Id, 2, "youtube", now),
            MakeEntry(observedPlaylist.Id, related.Id, empty.Id, 1, "youtube", now),
            MakeEntry(firstPlaylist.Id, related.Id, null, 0, "cantaro", now),
            MakeEntry(privatePlaylist.Id, related.Id, privateObservation.Id, 0, "youtube", now),
            MakeEntry(firstPlaylist.Id, original.Id, null, 1, "cantaro", now));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var service = new MusicLibraryQueryService(db);
        var originalDetail = Assert.IsType<MusicLibrarySongDto>(await service.GetCanonicalSongAsync(original.Id, owner.Id, default));
        var card = Assert.Single(originalDetail.RelatedRecordings);
        Assert.True(card.InLibrary);
        Assert.Equal(hasCanonicalDuration ? 250 : 189, card.DurationSeconds);
        Assert.Equal(hasCanonicalArtwork ? "https://img.example/canonical.jpg" : "https://img.example/owned.jpg", card.ThumbnailUrl);
        var relatedDetail = Assert.IsType<MusicLibrarySongDto>(await service.GetCanonicalSongAsync(related.Id, owner.Id, default));
        Assert.Equal(relatedDetail.DurationSeconds, card.DurationSeconds);
        Assert.Equal(relatedDetail.ThumbnailUrl, card.ThumbnailUrl);
        var librarySong = Assert.Single((await service.GetLibraryAsync(owner.Id, default)).Songs, song => song.Id == relatedDetail.Id);
        Assert.Equal(relatedDetail.ThumbnailUrl, librarySong.ThumbnailUrl);
        Assert.Equal(relatedDetail.DurationSeconds, librarySong.DurationSeconds);
        var originalCard = Assert.Single(relatedDetail.RelatedRecordings);
        Assert.True(originalCard.InLibrary);
        Assert.Null(originalCard.DurationSeconds);
        Assert.Null(originalCard.ThumbnailUrl);
        Assert.Equal(related.CanonicalMetadata, (await db.Tracks.AsNoTracking().SingleAsync(track => track.Id == related.Id)).CanonicalMetadata);
    }

    [Theory]
    [InlineData(null, null, null, "plain", false, true)]
    [InlineData("", "", "", "plain", true, false)]
    [InlineData("{}", " ", " \t ", "formatted", false, true)]
    [InlineData("{\"Title\":\" \",\"Artist\":\"\"}", "", null, "formatted", true, false)]
    [InlineData(null, "", "", "blank-formatted", false, true)]
    [InlineData("{}", null, " \t ", "blank-formatted", true, false)]
    public async Task ReadSongs_MissingTitleArtistProjectionsUseOnlyOrderedOwnedDisplayMetadata(
        string? canonicalMetadata, string? searchValue, string? emptyValue, string format,
        bool sameComposition, bool titleFirst)
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var now = DateTimeOffset.UtcNow;
        var firstUser = TestUserFactory.Create(740, "display-first@example.com");
        var secondUser = TestUserFactory.Create(741, "display-second@example.com");
        var firstPlaylist = MakePlaylist(firstUser.Id, "A canonical only", now);
        var observedPlaylist = MakePlaylist(firstUser.Id, "B observations", now);
        var secondPlaylist = MakePlaylist(secondUser.Id, "Private observations", now);
        var anchor = new Track { Id = Guid.NewGuid(), SearchTitle = "Anchor", CreatedAt = now, UpdatedAt = now };
        var related = new Track
        {
            Id = Guid.NewGuid(), CanonicalMetadata = canonicalMetadata, SearchTitle = searchValue,
            SearchArtist = searchValue, CreatedAt = now, UpdatedAt = now
        };
        TrackObservation Observation(string name) => new()
        {
            Id = Guid.NewGuid(), TrackId = related.Id, SourceType = "youtube", ExternalId = name,
            Title = format != "plain" ? "Unformatted upload" : $"{name} song",
            Artist = format != "plain" ? null : $"{name} artist",
            RawMetadata = format != "plain" ? JsonSerializer.Serialize(new TrackObservationMetadata
            {
                OriginalTitle = $"{name} artist - {name} song", SearchArtist = $"{name} artist"
            }) : null,
            MatchStatus = TrackMatchingStatuses.Matched, CreatedAt = now, UpdatedAt = now
        };
        var emptyObservation = Observation("Empty");
        // Title is required in SQLite; missing nullable artist/raw metadata use null.
        emptyObservation.Title = emptyValue ?? "";
        emptyObservation.Artist = emptyValue;
        emptyObservation.RawMetadata = null;
        emptyObservation.MatchStatus = TrackMatchingStatuses.Pending;
        var titleObservation = Observation("Title");
        titleObservation.Title = format == "formatted" ? "Unformatted upload" : "  First song  ";
        titleObservation.Artist = null;
        titleObservation.RawMetadata = format == "plain" ? null : JsonSerializer.Serialize(new TrackObservationMetadata
        {
            OriginalTitle = format == "blank-formatted" ? "(OST)" : "  First song - OST  "
        });
        var artistObservation = Observation("Artist");
        artistObservation.Title = " ";
        artistObservation.Artist = format == "plain" ? "  First artist  " : null;
        artistObservation.RawMetadata = format == "plain" ? null : JsonSerializer.Serialize(new TrackObservationMetadata
        {
            Artist = "Other raw artist", SearchArtist = "  First artist  "
        });
        var laterObservation = Observation("Later");
        var privateObservation = Observation("Private");
        db.AddRange(firstUser, secondUser, firstPlaylist, observedPlaylist, secondPlaylist, anchor, related,
            emptyObservation, titleObservation, artistObservation, laterObservation, privateObservation);
        if (sameComposition)
        {
            var composition = new Song { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now };
            db.Songs.Add(composition);
            db.SongTracks.AddRange(
                new SongTrack { SongId = composition.Id, TrackId = anchor.Id },
                new SongTrack { SongId = composition.Id, TrackId = related.Id });
        }
        else
        {
            db.TrackRelations.Add(new TrackRelation
            {
                FromTrackId = related.Id, ToTrackId = anchor.Id, RelationType = TrackRelationType.RemixOf
            });
        }
        await db.SaveChangesAsync();
        // A canonical-only entry and an empty observation precede independent title/artist
        // evidence. Insert conflicting evidence first to catch dependence on insertion order.
        db.PlaylistEntries.AddRange(
            MakeEntry(observedPlaylist.Id, related.Id, laterObservation.Id, 4, "youtube", now),
            MakeEntry(observedPlaylist.Id, related.Id, artistObservation.Id, titleFirst ? 3 : 2, "youtube", now),
            MakeEntry(observedPlaylist.Id, related.Id, titleObservation.Id, titleFirst ? 2 : 3, "youtube", now),
            MakeEntry(observedPlaylist.Id, related.Id, emptyObservation.Id, 1, "youtube", now),
            MakeEntry(firstPlaylist.Id, related.Id, null, 0, "cantaro", now),
            MakeEntry(firstPlaylist.Id, anchor.Id, null, 3, "cantaro", now),
            MakeEntry(secondPlaylist.Id, related.Id, privateObservation.Id, 0, "youtube", now),
            MakeEntry(secondPlaylist.Id, anchor.Id, null, 1, "cantaro", now));
        await db.SaveChangesAsync();
        // Seed legacy projection gaps without SaveChanges rebuilding search fields from JSON.
        await db.Tracks.Where(track => track.Id == related.Id).ExecuteUpdateAsync(setters => setters
            .SetProperty(track => track.SearchTitle, searchValue)
            .SetProperty(track => track.SearchArtist, searchValue));
        db.ChangeTracker.Clear();

        var service = new MusicLibraryQueryService(db);
        foreach (var (userId, name) in new[] { (firstUser.Id, "First"), (secondUser.Id, "Private") })
        {
            var anchorDetail = Assert.IsType<MusicLibrarySongDto>(await service.GetCanonicalSongAsync(anchor.Id, userId, default));
            var card = Assert.Single(anchorDetail.RelatedRecordings);
            var detail = Assert.IsType<MusicLibrarySongDto>(await service.GetCanonicalSongAsync(related.Id, userId, default));
            var librarySong = Assert.Single((await service.GetLibraryAsync(userId, default)).Songs, song => song.Id == detail.Id);
            Assert.Equal($"{name} song", card.Title);
            Assert.Equal($"{name} artist", card.Artist);
            Assert.Equal(card.Title, detail.Title);
            Assert.Equal(card.Artist, detail.Artist);
            Assert.Equal(detail.Title, librarySong.Title);
            Assert.Equal(detail.Artist, librarySong.Artist);
            Assert.True(card.InLibrary);
            Assert.Equal(userId == firstUser.Id ? TrackMatchingStatuses.Pending : null, librarySong.MatchStatus);
            Assert.Equal([sameComposition ? "Another recording of this composition" : "Remix of this recording"], card.RelationshipLabels);
        }
        var stored = await db.Tracks.AsNoTracking().SingleAsync(track => track.Id == related.Id);
        Assert.Equal(canonicalMetadata, stored.CanonicalMetadata);
        Assert.Equal(searchValue, stored.SearchTitle);
        Assert.Equal(searchValue, stored.SearchArtist);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    public async Task ReadSongs_TitleArtistFallbackPreservesCanonicalCreditAndSearchPrecedence(
        bool hasCanonicalValues, bool hasArtistCredit, bool hasSearchValues)
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var now = DateTimeOffset.UtcNow;
        var user = TestUserFactory.Create(742, "display-precedence@example.com");
        var playlist = MakePlaylist(user.Id, "Saved", now);
        var anchor = new Track { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now };
        var related = new Track
        {
            Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now,
            CanonicalMetadata = JsonSerializer.Serialize(new TrackCanonicalMetadata
            {
                Title = hasCanonicalValues ? "Canonical song" : " ",
                Artist = hasCanonicalValues ? "Canonical artist" : ""
            }),
            SearchTitle = hasSearchValues ? "Search song" : null,
            SearchArtist = hasSearchValues ? "Search artist" : null
        };
        if (hasArtistCredit)
        {
            var artist = new Artist { Id = Guid.NewGuid(), Name = "Credit artist", CreatedAt = now, UpdatedAt = now };
            related.ArtistCredits.Add(new TrackArtistCredit
            {
                Id = Guid.NewGuid(), TrackId = related.Id, ArtistId = artist.Id, Artist = artist,
                Role = TrackArtistRole.Primary, Position = 0, CreditedName = "Credit artist"
            });
        }
        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(), TrackId = related.Id, SourceType = "youtube", ExternalId = "owned-precedence",
            Title = "Observed song", Artist = "Observed artist", MatchStatus = TrackMatchingStatuses.Matched,
            CreatedAt = now, UpdatedAt = now
        };
        var emptyObservation = new TrackObservation
        {
            Id = Guid.NewGuid(), TrackId = related.Id, SourceType = "youtube", ExternalId = "empty-precedence",
            Title = " ", Artist = "", MatchStatus = TrackMatchingStatuses.Matched, CreatedAt = now, UpdatedAt = now
        };
        var laterObservation = new TrackObservation
        {
            Id = Guid.NewGuid(), TrackId = related.Id, SourceType = "youtube", ExternalId = "later-precedence",
            Title = "Conflicting later song", Artist = "Conflicting later artist",
            MatchStatus = TrackMatchingStatuses.Matched, CreatedAt = now, UpdatedAt = now
        };
        db.AddRange(user, playlist, anchor, related, observation, emptyObservation, laterObservation);
        db.TrackRelations.Add(new TrackRelation
        {
            FromTrackId = related.Id, ToTrackId = anchor.Id, RelationType = TrackRelationType.RemixOf
        });
        await db.SaveChangesAsync();
        db.PlaylistEntries.AddRange(
            MakeEntry(playlist.Id, anchor.Id, null, 0, "cantaro", now),
            MakeEntry(playlist.Id, related.Id, laterObservation.Id, 3, "youtube", now),
            MakeEntry(playlist.Id, related.Id, observation.Id, 2, "youtube", now),
            MakeEntry(playlist.Id, related.Id, emptyObservation.Id, 1, "youtube", now));
        await db.SaveChangesAsync();
        // Model independently persisted search projections, bypassing write-time rebuilding.
        await db.Tracks.Where(track => track.Id == related.Id).ExecuteUpdateAsync(setters => setters
            .SetProperty(track => track.SearchTitle, hasSearchValues ? "Search song" : null)
            .SetProperty(track => track.SearchArtist, hasSearchValues ? "Search artist" : null));
        db.ChangeTracker.Clear();

        var service = new MusicLibraryQueryService(db);
        var card = Assert.Single((await service.GetCanonicalSongAsync(anchor.Id, user.Id, default))!.RelatedRecordings);
        var detail = Assert.IsType<MusicLibrarySongDto>(await service.GetCanonicalSongAsync(related.Id, user.Id, default));
        var librarySong = Assert.Single((await service.GetLibraryAsync(user.Id, default)).Songs, song => song.Id == detail.Id);
        Assert.Equal(hasCanonicalValues ? "Canonical song" : "Search song", card.Title);
        Assert.Equal(hasCanonicalValues ? "Canonical artist" : hasArtistCredit ? "Credit artist" : "Search artist", card.Artist);
        Assert.Equal(card.Title, detail.Title);
        Assert.Equal(card.Artist, detail.Artist);
        Assert.Equal(hasArtistCredit ? "Credit artist" : null, detail.ArtistCredits.SingleOrDefault()?.CreditedName);
        // Library display already prefers owned observations over search projections.
        Assert.Equal(hasCanonicalValues ? "Canonical song" : "Observed song", librarySong.Title);
        Assert.Equal(hasCanonicalValues ? "Canonical artist" : hasArtistCredit ? "Credit artist" : "Observed artist", librarySong.Artist);
    }

    [Fact]
    public async Task GetCanonicalSongAsync_NonownedRelatedRecordingsNeverUseOtherUsersObservations()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var now = DateTimeOffset.UtcNow;
        var owner = TestUserFactory.Create(730, "related-privacy-owner@example.com");
        var other = TestUserFactory.Create(731, "related-privacy-other@example.com");
        var playlist = MakePlaylist(owner.Id, "Saved", now);
        var privatePlaylist = MakePlaylist(other.Id, "Private", now);
        var original = new Track { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now };
        var related = new Track { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now };
        var canonicalRelated = new Track
        {
            Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now,
            CanonicalMetadata = JsonSerializer.Serialize(new TrackCanonicalMetadata
            {
                Title = "Canonical related", DurationSeconds = 250, ThumbnailUrl = "https://img.example/canonical.jpg"
            })
        };
        var searchRelated = new Track
        {
            Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now,
            CanonicalMetadata = "{\"Title\":\"\",\"Artist\":\" \"}",
            SearchTitle = "Search related", SearchArtist = "Search artist"
        };
        var privateObservation = new TrackObservation
        {
            Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = "private-related", Title = "Private related", Artist = "Private artist",
            TrackId = related.Id, MatchStatus = TrackMatchingStatuses.Matched, CreatedAt = now, UpdatedAt = now,
            RawMetadata = JsonSerializer.Serialize(new TrackObservationMetadata
            {
                DurationSeconds = 400, ThumbnailUrl = "https://img.example/private.jpg"
            })
        };
        var emptyPrivateObservation = new TrackObservation
        {
            Id = Guid.NewGuid(), TrackId = related.Id, SourceType = "youtube", ExternalId = "empty-private-related",
            Title = " ", Artist = null, MatchStatus = TrackMatchingStatuses.Matched, CreatedAt = now, UpdatedAt = now
        };
        var composition = new Song { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now };
        db.AddRange(owner, other, playlist, privatePlaylist, original, related, canonicalRelated, searchRelated,
            emptyPrivateObservation, privateObservation, composition);
        db.SongTracks.AddRange(
            new SongTrack { SongId = composition.Id, TrackId = original.Id },
            new SongTrack { SongId = composition.Id, TrackId = canonicalRelated.Id },
            new SongTrack { SongId = composition.Id, TrackId = searchRelated.Id });
        db.TrackRelations.Add(new TrackRelation
        {
            FromTrackId = related.Id, ToTrackId = original.Id, RelationType = TrackRelationType.RemixOf
        });
        await db.SaveChangesAsync();
        db.PlaylistEntries.AddRange(
            MakeEntry(playlist.Id, original.Id, null, 0, "cantaro", now),
            MakeEntry(privatePlaylist.Id, related.Id, emptyPrivateObservation.Id, 0, "youtube", now),
            MakeEntry(privatePlaylist.Id, related.Id, privateObservation.Id, 1, "youtube", now));
        await db.SaveChangesAsync();
        await db.Tracks.Where(track => track.Id == searchRelated.Id).ExecuteUpdateAsync(setters => setters
            .SetProperty(track => track.SearchTitle, "Search related")
            .SetProperty(track => track.SearchArtist, "Search artist"));
        db.ChangeTracker.Clear();

        var service = new MusicLibraryQueryService(db);
        var detail = Assert.IsType<MusicLibrarySongDto>(await service.GetCanonicalSongAsync(original.Id, owner.Id, default));
        Assert.Equal(["Canonical related", "Search related", "Unknown song"], detail.RelatedRecordings.Select(card => card.Title));
        var privateCard = Assert.Single(detail.RelatedRecordings, card => card.Id == $"track:{related.Id}");
        Assert.False(privateCard.InLibrary);
        Assert.Equal("Unknown song", privateCard.Title);
        Assert.Null(privateCard.Artist);
        Assert.Null(privateCard.DurationSeconds);
        Assert.Null(privateCard.ThumbnailUrl);
        var canonicalCard = Assert.Single(detail.RelatedRecordings, card => card.Id == $"track:{canonicalRelated.Id}");
        Assert.False(canonicalCard.InLibrary);
        Assert.Equal(250, canonicalCard.DurationSeconds);
        Assert.Null(canonicalCard.ThumbnailUrl);
        var searchCard = Assert.Single(detail.RelatedRecordings, card => card.Id == $"track:{searchRelated.Id}");
        Assert.False(searchCard.InLibrary);
        Assert.Equal("Search artist", searchCard.Artist);
        var searchDetail = Assert.IsType<MusicLibrarySongDto>(await service.GetCanonicalSongAsync(searchRelated.Id, owner.Id, default));
        Assert.Equal(searchCard.Title, searchDetail.Title);
        Assert.Equal(searchCard.Artist, searchDetail.Artist);
        var relatedDetail = Assert.IsType<MusicLibrarySongDto>(await service.GetCanonicalSongAsync(related.Id, owner.Id, default));
        Assert.Empty(relatedDetail.Playlists);
        Assert.Equal(privateCard.Title, relatedDetail.Title);
        Assert.Null(relatedDetail.Artist);
        Assert.Equal(privateCard.DurationSeconds, relatedDetail.DurationSeconds);
        Assert.Equal(privateCard.ThumbnailUrl, relatedDetail.ThumbnailUrl);
        var otherDetail = Assert.IsType<MusicLibrarySongDto>(await service.GetCanonicalSongAsync(related.Id, other.Id, default));
        Assert.Equal("Private related", otherDetail.Title);
        Assert.Equal("Private artist", otherDetail.Artist);
        Assert.Equal(400, otherDetail.DurationSeconds);
        Assert.Equal("https://img.example/private.jpg", otherDetail.ThumbnailUrl);
    }

    [Theory]
    [InlineData("incoming", false)]
    [InlineData("incoming", true)]
    [InlineData("outgoing", false)]
    [InlineData("outgoing", true)]
    [InlineData("composition", false)]
    [InlineData("composition", true)]
    public async Task GetCanonicalSongAsync_RelatedMappingsStayHiddenAcrossCanonicalOnlyAddAndRemove(
        string accessKind, bool hasPublicIdentifiers)
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var now = DateTimeOffset.UtcNow;
        var requester = TestUserFactory.Create(732, "relation-requester@example.com");
        var owner = TestUserFactory.Create(733, "relation-owner@example.com");
        var saved = MakePlaylist(requester.Id, "Saved", now);
        var privatePlaylist = MakePlaylist(owner.Id, "Private", now);
        Track MakeTrack(string title) => new()
        {
            Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now,
            CanonicalMetadata = JsonSerializer.Serialize(new TrackCanonicalMetadata { Title = title, Artist = "Artist" })
        };
        var original = MakeTrack("Original");
        var related = MakeTrack("Related canonical title");
        related.Isrc = hasPublicIdentifiers ? "USABC1234567" : null;
        related.MbidRecording = hasPublicIdentifiers ? "public-recording" : null;
        var secondHop = MakeTrack("Second hop");
        var unrelated = MakeTrack("Unrelated");
        // Matching persists observation identities globally, without a visibility classification.
        var observations = new[] { "youtube", "spotify", "musicbrainz", "isrc", "unclassified" }
            .Select(source => new TrackObservation
            {
                Id = Guid.NewGuid(), TrackId = related.Id, SourceType = source, ExternalId = $"private-{source}",
                Title = "Private observed title", MatchStatus = TrackMatchingStatuses.Matched,
                CreatedAt = now, UpdatedAt = now
            }).ToList();
        related.SourceIds = observations.Select(observation => new TrackSourceId
        {
            Id = Guid.NewGuid(), TrackId = related.Id,
            SourceType = observation.SourceType, ExternalId = observation.ExternalId
        }).ToList();
        var alternate = new TrackObservation
        {
            Id = Guid.NewGuid(), TrackId = related.Id, SourceType = "youtube", ExternalId = "private-youtube-variant",
            Title = "Private variant", MatchStatus = TrackMatchingStatuses.Matched, CreatedAt = now, UpdatedAt = now
        };
        db.AddRange(requester, owner, saved, privatePlaylist, original, related, secondHop, unrelated, alternate);
        db.AddRange(observations);
        if (accessKind == "composition")
        {
            var composition = new Song { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now };
            db.Add(composition);
            db.SongTracks.AddRange(
                new SongTrack { SongId = composition.Id, TrackId = original.Id },
                new SongTrack { SongId = composition.Id, TrackId = related.Id });
        }
        else
        {
            db.TrackRelations.Add(new TrackRelation
            {
                FromTrackId = accessKind == "incoming" ? related.Id : original.Id,
                ToTrackId = accessKind == "incoming" ? original.Id : related.Id,
                RelationType = TrackRelationType.RemixOf
            });
        }
        db.TrackRelations.Add(new TrackRelation
        {
            FromTrackId = secondHop.Id, ToTrackId = related.Id, RelationType = TrackRelationType.RemixOf
        });
        db.PlaylistEntries.Add(MakeEntry(saved.Id, original.Id, null, 0, "cantaro", now));
        db.PlaylistEntries.AddRange(observations.Append(alternate).Select((observation, index) =>
            MakeEntry(privatePlaylist.Id, related.Id, observation.Id, index, observation.SourceType, now)));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var service = new MusicLibraryQueryService(db);
        var originalDetail = Assert.IsType<MusicLibrarySongDto>(await service.GetCanonicalSongAsync(original.Id, requester.Id, default));
        var card = Assert.Single(originalDetail.RelatedRecordings);
        Assert.Equal($"track:{related.Id}", card.Id);
        Assert.False(card.InLibrary);
        var detail = Assert.IsType<MusicLibrarySongDto>(await service.GetCanonicalSongAsync(related.Id, requester.Id, default));
        Assert.Equal(card.Id, detail.Id);
        Assert.Equal("Related canonical title", detail.Title);
        Assert.Equal(card.Title, detail.Title);
        Assert.Equal("Artist", detail.Artist);
        Assert.Empty(detail.Playlists);
        Assert.Equal(hasPublicIdentifiers ? new[] { ("isrc", related.Isrc!), ("musicbrainz", related.MbidRecording!) } : [],
            detail.SourceIdentities.Select(identity => (identity.Source, identity.ExternalId)));
        Assert.Equal(hasPublicIdentifiers ? new[] { "isrc", "musicbrainz" } : [], detail.SourcePlatforms);
        if (hasPublicIdentifiers)
        {
            var link = Assert.Single(detail.PlatformLinks);
            Assert.Equal("musicbrainz", link.Platform);
            Assert.Equal("public-recording", link.ExternalId);
            Assert.Equal("https://musicbrainz.org/recording/public-recording", link.Url);
        }
        else Assert.Empty(detail.PlatformLinks);
        Assert.Equal($"track:{original.Id}", Assert.Single(detail.RelatedRecordings).Id);
        Assert.Null(await service.GetCanonicalSongAsync(secondHop.Id, requester.Id, default));
        Assert.Null(await service.GetCanonicalSongAsync(unrelated.Id, requester.Id, default));

        // The card's disclosed canonical ID can be added, but the new entry provides no observation provenance.
        Assert.True(await service.AddCanonicalSongToPlaylistAsync(related.Id, saved.Id, requester.Id, default));
        db.ChangeTracker.Clear();
        var addedEntry = await db.PlaylistEntries.AsNoTracking()
            .SingleAsync(entry => entry.PlaylistId == saved.Id && entry.TrackId == related.Id);
        Assert.Null(addedEntry.TrackObservationId);
        var afterAdd = Assert.IsType<MusicLibrarySongDto>(await service.GetCanonicalSongAsync(related.Id, requester.Id, default));
        Assert.Single(afterAdd.Playlists);
        AssertSameIdentitiesAndLinks(detail, afterAdd);
        var libraryAfterAdd = Assert.Single((await service.GetLibraryAsync(requester.Id, default)).Songs,
            song => song.Id == detail.Id);
        AssertSameIdentitiesAndLinks(detail, libraryAfterAdd);
        Assert.True(Assert.Single((await service.GetCanonicalSongAsync(original.Id, requester.Id, default))!.RelatedRecordings).InLibrary);

        Assert.True(await service.RemoveCanonicalSongFromPlaylistAsync(related.Id, saved.Id, requester.Id, default));
        db.ChangeTracker.Clear();
        var afterRemove = Assert.IsType<MusicLibrarySongDto>(await service.GetCanonicalSongAsync(related.Id, requester.Id, default));
        Assert.Empty(afterRemove.Playlists);
        AssertSameIdentitiesAndLinks(detail, afterRemove);
        Assert.DoesNotContain((await service.GetLibraryAsync(requester.Id, default)).Songs, song => song.Id == detail.Id);
        Assert.Null(await service.GetCanonicalSongAsync(secondHop.Id, requester.Id, default));
        Assert.Equal(observations.Count, await db.TrackSourceIds.CountAsync());

        var ownedDetail = Assert.IsType<MusicLibrarySongDto>(await service.GetCanonicalSongAsync(related.Id, owner.Id, default));
        var ownedLibrarySong = Assert.Single((await service.GetLibraryAsync(owner.Id, default)).Songs);
        Assert.Equal(ownedLibrarySong.SourceIdentities.Select(identity => (identity.Source, identity.ExternalId)),
            ownedDetail.SourceIdentities.Select(identity => (identity.Source, identity.ExternalId)));
        foreach (var observation in observations.Append(alternate))
            Assert.Contains(ownedDetail.SourceIdentities, identity => identity.Source == observation.SourceType
                && identity.ExternalId == observation.ExternalId);
        Assert.Equal(new[] { "private-youtube", "private-youtube-variant" },
            ownedDetail.PlatformLinks.Where(link => link.Platform == "youtube").Select(link => link.ExternalId));
        Assert.Equal(new[] { "private-youtube", "private-youtube-variant" },
            ownedDetail.PlatformLinks.Where(link => link.Platform == "youtube-music").Select(link => link.ExternalId));
    }

    [Fact]
    public async Task GetCanonicalSongAsync_UsesStoredRecordingRelationshipsAndKeepsRelatedPagesReachable()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var now = DateTimeOffset.UtcNow;
        var owner = TestUserFactory.Create(720, "versions-owner@example.com");
        var other = TestUserFactory.Create(721, "versions-other@example.com");
        var playlist = MakePlaylist(owner.Id, "Saved", now);
        Track MakeTrack(string title, TrackVersionFlags flags = TrackVersionFlags.None) => new()
        {
            Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now, VersionFlags = flags,
            CanonicalMetadata = JsonSerializer.Serialize(new TrackCanonicalMetadata { Title = title, Artist = "Shared artist" })
        };
        var original = MakeTrack("Original");
        original.MbidRecording = "musicbrainz-original";
        original.Isrc = "USABC1234567";
        original.SourceIds.Add(new TrackSourceId
        {
            Id = Guid.NewGuid(), TrackId = original.Id, SourceType = "spotify", ExternalId = "spotify-original"
        });
        var acoustic = MakeTrack("Acoustic take", TrackVersionFlags.Acoustic);
        var remix = MakeTrack("Remix", TrackVersionFlags.Remix);
        var unrelated = MakeTrack("Another song by the artist");
        var composition = new Song { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now };
        db.AddRange(owner, other, playlist, original, acoustic, remix, unrelated, composition);
        db.SongTracks.AddRange(
            new SongTrack { SongId = composition.Id, TrackId = original.Id },
            new SongTrack { SongId = composition.Id, TrackId = acoustic.Id });
        db.TrackRelations.Add(new TrackRelation
        {
            FromTrackId = remix.Id, ToTrackId = original.Id, RelationType = TrackRelationType.RemixOf
        });
        await db.SaveChangesAsync();
        db.PlaylistEntries.Add(MakeEntry(playlist.Id, original.Id, null, 0, "spotify", now));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var service = new MusicLibraryQueryService(db);
        var detail = Assert.IsType<MusicLibrarySongDto>(await service.GetCanonicalSongAsync(original.Id, owner.Id, default));
        Assert.Equal(2, detail.RelatedRecordings.Count);
        var acousticVersion = Assert.Single(detail.RelatedRecordings, item => item.Id == $"track:{acoustic.Id}");
        Assert.Equal(["Another recording of this composition"], acousticVersion.RelationshipLabels);
        Assert.Equal(["Acoustic"], acousticVersion.VersionLabels);
        Assert.False(acousticVersion.InLibrary);
        var remixVersion = Assert.Single(detail.RelatedRecordings, item => item.Id == $"track:{remix.Id}");
        Assert.Equal(["Remix of this recording"], remixVersion.RelationshipLabels);
        Assert.Equal(["Remix"], remixVersion.VersionLabels);
        Assert.DoesNotContain(detail.RelatedRecordings, item => item.Id == $"track:{unrelated.Id}");
        Assert.Contains(detail.SourceIdentities, identity => identity.Source == "musicbrainz" && identity.ExternalId == "musicbrainz-original");
        Assert.Contains(detail.SourceIdentities, identity => identity.Source == "isrc" && identity.ExternalId == "USABC1234567");
        Assert.Equal(["isrc", "musicbrainz"], detail.SourcePlatforms);
        Assert.Equal(2, detail.SourceIdentities.Count);
        var publicLink = Assert.Single(detail.PlatformLinks);
        Assert.Equal("musicbrainz", publicLink.Platform);
        Assert.Equal("musicbrainz-original", publicLink.ExternalId);
        Assert.DoesNotContain(detail.SourceIdentities, identity => identity.ExternalId == "spotify-original");

        var remixDetail = Assert.IsType<MusicLibrarySongDto>(await service.GetCanonicalSongAsync(remix.Id, owner.Id, default));
        Assert.Empty(remixDetail.Playlists);
        Assert.Equal(["Source of this remix"], Assert.Single(remixDetail.RelatedRecordings).RelationshipLabels);
        Assert.Null(await service.GetCanonicalSongAsync(unrelated.Id, owner.Id, default));
        Assert.Null(await service.GetCanonicalSongAsync(original.Id, other.Id, default));
        Assert.Null(await service.GetCanonicalSongAsync(remix.Id, other.Id, default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadSongs_CanonicalMembershipAndSourceServiceDoNotAuthorizeMappings(bool readLibrary)
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var now = DateTimeOffset.UtcNow;
        var user = TestUserFactory.Create(734, "canonical-only@example.com");
        var playlist = MakePlaylist(user.Id, "Canonical only", now);
        var track = new Track { Id = Guid.NewGuid(), SearchTitle = "Song", CreatedAt = now, UpdatedAt = now };
        var unattachedObservation = new TrackObservation
        {
            Id = Guid.NewGuid(), TrackId = track.Id, SourceType = "youtube", ExternalId = "unattached-video",
            Title = "Unattached", MatchStatus = TrackMatchingStatuses.Matched, CreatedAt = now, UpdatedAt = now
        };
        track.SourceIds.Add(new TrackSourceId
        {
            Id = Guid.NewGuid(), TrackId = track.Id, SourceType = "youtube", ExternalId = unattachedObservation.ExternalId
        });
        db.AddRange(user, playlist, track, unattachedObservation);
        db.PlaylistEntries.AddRange(
            MakeEntry(playlist.Id, track.Id, null, 0, "youtube", now),
            MakeEntry(playlist.Id, track.Id, null, 1, "spotify", now),
            MakeEntry(playlist.Id, track.Id, null, 2, "cantaro", now));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var service = new MusicLibraryQueryService(db);

        var song = readLibrary ? Assert.Single((await service.GetLibraryAsync(user.Id, default)).Songs)
            : Assert.IsType<MusicLibrarySongDto>(await service.GetCanonicalSongAsync(track.Id, user.Id, default));

        Assert.Equal(3, song.Playlists.Count);
        Assert.Empty(song.SourceIdentities);
        Assert.Empty(song.PlatformLinks);
        Assert.Empty(song.SourcePlatforms);
    }

    [Theory]
    [InlineData("incoming", "before")]
    [InlineData("outgoing", "before")]
    [InlineData("composition", "before")]
    [InlineData("incoming", "canonical")]
    [InlineData("outgoing", "canonical")]
    [InlineData("composition", "canonical")]
    [InlineData("incoming", "owned")]
    [InlineData("outgoing", "owned")]
    [InlineData("composition", "owned")]
    [InlineData("incoming", "removed")]
    [InlineData("outgoing", "removed")]
    [InlineData("composition", "removed")]
    public async Task ReadSongs_ArtworkRequiresOwnedObservationProvenance(string accessKind, string phase)
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var now = DateTimeOffset.UtcNow;
        var requester = TestUserFactory.Create(737, "artwork-requester@example.com");
        var originalOwner = TestUserFactory.Create(738, "artwork-owner@example.com");
        var playlist = MakePlaylist(requester.Id, "Saved", now);
        var privatePlaylist = MakePlaylist(originalOwner.Id, "Private", now);
        var importedPlaylist = MakePlaylist(requester.Id, "Imported", now);
        var anchor = new Track { Id = Guid.NewGuid(), SearchTitle = "Anchor", CreatedAt = now, UpdatedAt = now };
        const string privateArtwork = "https://i.ytimg.com/vi/privateVid1/mqdefault.jpg";
        const string ownedArtwork = "https://i.ytimg.com/vi/ownedVideo1/mqdefault.jpg";
        var privateObservation = new TrackObservation
        {
            Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = "privateVid1",
            Title = "Song", Artist = "Artist", ThumbnailUrl = privateArtwork, DurationSeconds = 200,
            MatchStatus = TrackMatchingStatuses.Pending, CreatedAt = now, UpdatedAt = now
        };
        db.AddRange(requester, originalOwner, playlist, importedPlaylist, privatePlaylist, anchor, privateObservation);
        db.PlaylistEntries.Add(MakeEntry(privatePlaylist.Id, null, privateObservation.Id, 0, "youtube", now));
        db.PlaylistEntries.Add(MakeEntry(playlist.Id, anchor.Id, null, 0, "cantaro", now));
        await db.SaveChangesAsync();
        // Exercise production matching and reconciliation, which copy private artwork into shared canonical JSON.
        var matching = new TrackMatchingService(db, [], NullLogger<TrackMatchingService>.Instance);
        var matched = await matching.CreateTrackFromMatchAsync(privateObservation.Id, new TrackMatchSearchCandidate
        {
            CandidateSource = "spotify", ExternalId = "catalog-song", Title = "Song", Artist = "Artist",
            ArtistCredits = ["Artist"], Isrc = "USABC2400001", DurationSeconds = 210
        }, default);
        var trackId = Assert.IsType<Guid>(matched.TrackId);
        var stored = await db.Tracks.AsNoTracking().SingleAsync(track => track.Id == trackId);
        Assert.Equal(privateArtwork, JsonSerializer.Deserialize<TrackCanonicalMetadata>(stored.CanonicalMetadata!)!.ThumbnailUrl);
        if (accessKind == "composition")
        {
            var composition = new Song { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now };
            db.Songs.Add(composition);
            db.SongTracks.AddRange(new SongTrack { SongId = composition.Id, TrackId = anchor.Id },
                new SongTrack { SongId = composition.Id, TrackId = trackId });
        }
        else db.TrackRelations.Add(new TrackRelation
        {
            FromTrackId = accessKind == "incoming" ? trackId : anchor.Id,
            ToTrackId = accessKind == "incoming" ? anchor.Id : trackId,
            RelationType = TrackRelationType.RemixOf
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var service = new MusicLibraryQueryService(db);
        if (phase != "before")
            Assert.True(await service.AddCanonicalSongToPlaylistAsync(trackId, playlist.Id, requester.Id, default));
        if (phase is "owned" or "removed")
        {
            // A different imported observation resolves to the same canonical track, with its own artwork.
            var ownObservation = new TrackObservation
            {
                Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = "ownedVideo1", Title = "Song", Artist = "Artist",
                RawMetadata = JsonSerializer.Serialize(new TrackObservationMetadata { ThumbnailUrl = ownedArtwork }),
                MatchStatus = TrackMatchingStatuses.Pending, CreatedAt = now, UpdatedAt = now
            };
            db.TrackObservations.Add(ownObservation);
            var ownEntry = MakeEntry(importedPlaylist.Id, null, ownObservation.Id, 2, "youtube", now);
            db.PlaylistEntries.Add(ownEntry);
            await db.SaveChangesAsync();
            var ownMatch = await matching.CreateTrackFromMatchAsync(ownObservation.Id, new TrackMatchSearchCandidate
            {
                CandidateSource = "spotify", ExternalId = "catalog-song", Title = "Song", Artist = "Artist",
                ArtistCredits = ["Artist"], Isrc = "USABC2400001", DurationSeconds = 210
            }, default);
            Assert.Equal(trackId, ownMatch.TrackId);
            if (phase == "removed")
                Assert.True(await service.RemoveCanonicalSongFromPlaylistAsync(trackId, importedPlaylist.Id, requester.Id, default, ownEntry.Id));
        }
        db.ChangeTracker.Clear();
        var expectedArtwork = phase == "owned" ? ownedArtwork : null;
        var detail = Assert.IsType<MusicLibrarySongDto>(await service.GetCanonicalSongAsync(trackId, requester.Id, default));
        Assert.Equal(expectedArtwork, detail.ThumbnailUrl);
        Assert.Equal("Song", detail.Title);
        Assert.Equal(210, detail.DurationSeconds);
        Assert.Contains(detail.SourceIdentities, identity => identity.Source == "isrc" && identity.ExternalId == "USABC2400001");
        Assert.DoesNotContain("privateVid1", JsonSerializer.Serialize(detail));
        var anchorDetail = Assert.IsType<MusicLibrarySongDto>(await service.GetCanonicalSongAsync(anchor.Id, requester.Id, default));
        var card = Assert.Single(anchorDetail.RelatedRecordings);
        Assert.Equal(expectedArtwork, card.ThumbnailUrl);
        Assert.Equal(phase != "before", card.InLibrary);
        Assert.DoesNotContain("privateVid1", JsonSerializer.Serialize(anchorDetail));
        var library = await service.GetLibraryAsync(requester.Id, default);
        Assert.DoesNotContain("privateVid1", JsonSerializer.Serialize(library));
        if (phase != "before")
        {
            var librarySong = Assert.Single(library.Songs, song => song.Id == detail.Id);
            Assert.Equal(expectedArtwork, librarySong.ThumbnailUrl);
            AssertSameIdentitiesAndLinks(detail, librarySong);
        }
        if (phase == "removed")
        {
            // Removing the remaining canonical-only membership still allows related detail access,
            // but neither the detached observation nor the persisted canonical URL authorizes artwork.
            Assert.True(await service.RemoveCanonicalSongFromPlaylistAsync(trackId, playlist.Id, requester.Id, default));
            db.ChangeTracker.Clear();
            var afterCanonicalRemoval = Assert.IsType<MusicLibrarySongDto>(await service.GetCanonicalSongAsync(trackId, requester.Id, default));
            Assert.Empty(afterCanonicalRemoval.Playlists);
            Assert.Null(afterCanonicalRemoval.ThumbnailUrl);
            Assert.DoesNotContain("privateVid1", JsonSerializer.Serialize(afterCanonicalRemoval));
            var afterRemovalAnchor = Assert.IsType<MusicLibrarySongDto>(await service.GetCanonicalSongAsync(anchor.Id, requester.Id, default));
            var afterRemovalCard = Assert.Single(afterRemovalAnchor.RelatedRecordings);
            Assert.False(afterRemovalCard.InLibrary);
            Assert.Null(afterRemovalCard.ThumbnailUrl);
            Assert.DoesNotContain((await service.GetLibraryAsync(requester.Id, default)).Songs, song => song.Id == detail.Id);
            Assert.Equal(2, await db.TrackObservations.CountAsync());
        }
        var ownerDetail = Assert.IsType<MusicLibrarySongDto>(await service.GetCanonicalSongAsync(trackId, originalOwner.Id, default));
        Assert.Equal(privateArtwork, ownerDetail.ThumbnailUrl);
        Assert.Equal(privateArtwork, Assert.Single((await service.GetLibraryAsync(originalOwner.Id, default)).Songs).ThumbnailUrl);
        Assert.Equal(stored.CanonicalMetadata, (await db.Tracks.AsNoTracking().SingleAsync(track => track.Id == trackId)).CanonicalMetadata);
    }

    private static void AssertSameIdentitiesAndLinks(MusicLibrarySongDto expected, MusicLibrarySongDto actual)
    {
        Assert.Equal(expected.SourceIdentities.Select(identity => (identity.Source, identity.ExternalId)),
            actual.SourceIdentities.Select(identity => (identity.Source, identity.ExternalId)));
        Assert.Equal(expected.SourcePlatforms, actual.SourcePlatforms);
        Assert.Equal(expected.PlatformLinks.Select(link => (link.Platform, link.Source, link.ExternalId, link.Url)),
            actual.PlatformLinks.Select(link => (link.Platform, link.Source, link.ExternalId, link.Url)));
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
