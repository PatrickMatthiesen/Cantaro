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
        Assert.DoesNotContain(privateDetail.SourceIdentities, item => item.ExternalId is "video-two" or "spotify-id");
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

        var detail = Assert.IsType<MusicLibrarySongDto>(
            await new MusicLibraryQueryService(db).GetCanonicalSongAsync(track.Id, owner.Id, default));

        Assert.Equal(hasCanonicalMetadata ? 250 : 189, detail.DurationSeconds);
        Assert.Equal(hasCanonicalMetadata ? "https://img.example/canonical.jpg" : "https://img.example/owned.jpg", detail.ThumbnailUrl);
        var stored = await db.Tracks.AsNoTracking().SingleAsync(item => item.Id == track.Id);
        Assert.Equal(track.CanonicalMetadata, stored.CanonicalMetadata);
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
        var spotifyLink = Assert.Single(detail.PlatformLinks, link => link.Platform == "spotify");
        Assert.Equal("spotify", spotifyLink.Source);
        Assert.Equal("spotify-original", spotifyLink.ExternalId);

        var remixDetail = Assert.IsType<MusicLibrarySongDto>(await service.GetCanonicalSongAsync(remix.Id, owner.Id, default));
        Assert.Empty(remixDetail.Playlists);
        Assert.Equal(["Source of this remix"], Assert.Single(remixDetail.RelatedRecordings).RelationshipLabels);
        Assert.Null(await service.GetCanonicalSongAsync(unrelated.Id, owner.Id, default));
        Assert.Null(await service.GetCanonicalSongAsync(original.Id, other.Id, default));
        Assert.Null(await service.GetCanonicalSongAsync(remix.Id, other.Id, default));
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
