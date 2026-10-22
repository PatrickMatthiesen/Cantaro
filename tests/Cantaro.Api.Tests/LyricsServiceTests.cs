using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Cantaro.Api.Services.Lyrics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class LyricsServiceTests
{
    [Fact]
    public async Task GetLyricsAsync_MapsCanonicalMetadataForTrackInUsersLibrary()
    {
        await using var db = CreateDb();
        var trackId = Guid.NewGuid();
        var playlist = new Playlist
        {
            Id = Guid.NewGuid(),
            UserId = 7,
            Name = "Favorites"
        };
        var track = new Track
        {
            Id = trackId,
            MbidRecording = "recording-id",
            Isrc = "ISRC123",
            CanonicalMetadata = JsonSerializer.Serialize(new TrackCanonicalMetadata
            {
                Title = "Test Song",
                Artist = "Test Artist",
                Albums = ["Test Album"],
                DurationSeconds = 200
            })
        };
        db.AddRange(playlist, track, new PlaylistEntry
        {
            Id = Guid.NewGuid(),
            PlaylistId = playlist.Id,
            TrackId = trackId,
            Position = 0
        });
        await db.SaveChangesAsync();
        var provider = new CapturingProvider();

        var result = await new LyricsService(db, provider).GetLyricsAsync(7, trackId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(provider.Lookup);
        Assert.Equal("Test Song", provider.Lookup.Title);
        Assert.Equal("Test Artist", provider.Lookup.Artist);
        Assert.Equal("Test Album", provider.Lookup.Album);
        Assert.Equal(200, provider.Lookup.DurationSeconds);
        Assert.Equal("recording-id", provider.Lookup.MbidRecording);
        Assert.Equal("ISRC123", provider.Lookup.Isrc);
    }

    [Fact]
    public async Task GetLyricsAsync_RejectsCanonicalTrackOutsideUsersLibrary()
    {
        await using var db = CreateDb();
        var trackId = Guid.NewGuid();
        var playlist = new Playlist { Id = Guid.NewGuid(), UserId = 8, Name = "Private" };
        var track = new Track
        {
            Id = trackId,
            CanonicalMetadata = JsonSerializer.Serialize(new TrackCanonicalMetadata
            {
                Title = "Test Song",
                Artist = "Test Artist"
            })
        };
        db.AddRange(playlist, track, new PlaylistEntry
        {
            Id = Guid.NewGuid(),
            PlaylistId = playlist.Id,
            TrackId = trackId,
            Position = 0
        });
        await db.SaveChangesAsync();
        var provider = new CapturingProvider();

        var result = await new LyricsService(db, provider).GetLyricsAsync(7, trackId, CancellationToken.None);

        Assert.Null(result);
        Assert.Null(provider.Lookup);
    }

    [Fact]
    public async Task GetLyricsAsync_AllowsOnlyImmediateRelatedCanonicalRecordings()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var owner = TestUserFactory.Create(7, "lyrics-related-owner@example.com");
        var other = TestUserFactory.Create(8, "lyrics-related-other@example.com");
        var outsider = TestUserFactory.Create(9, "lyrics-related-outsider@example.com");
        var playlist = new Playlist { Id = Guid.NewGuid(), UserId = owner.Id, Name = "Saved" };
        var privatePlaylist = new Playlist { Id = Guid.NewGuid(), UserId = other.Id, Name = "Private" };
        Track Track(string title) => new()
        {
            Id = Guid.NewGuid(),
            CanonicalMetadata = JsonSerializer.Serialize(new TrackCanonicalMetadata { Title = title, Artist = "Artist" })
        };
        var original = Track("Original");
        var sibling = Track("Composition sibling");
        var remix = Track("Remix");
        var source = Track("Sample source");
        var secondHop = Track("Remix of remix");
        var unrelated = Track("Unrelated");
        var composition = new Song { Id = Guid.NewGuid() };
        var privateObservation = new TrackObservation
        {
            Id = Guid.NewGuid(), TrackId = remix.Id, SourceType = "youtube", ExternalId = "private-video",
            Title = "Private observation", DurationSeconds = 999, ThumbnailUrl = "https://img.example/private.jpg",
            MatchStatus = TrackMatchingStatuses.Matched
        };
        db.AddRange(owner, other, outsider, playlist, privatePlaylist, original, sibling, remix, source,
            secondHop, unrelated, composition, privateObservation);
        db.SongTracks.AddRange(
            new SongTrack { SongId = composition.Id, TrackId = original.Id },
            new SongTrack { SongId = composition.Id, TrackId = sibling.Id });
        db.TrackRelations.AddRange(
            new TrackRelation { FromTrackId = remix.Id, ToTrackId = original.Id, RelationType = TrackRelationType.RemixOf },
            new TrackRelation { FromTrackId = original.Id, ToTrackId = source.Id, RelationType = TrackRelationType.Samples },
            new TrackRelation { FromTrackId = secondHop.Id, ToTrackId = remix.Id, RelationType = TrackRelationType.RemixOf });
        db.PlaylistEntries.AddRange(
            new PlaylistEntry { Id = Guid.NewGuid(), PlaylistId = playlist.Id, TrackId = original.Id, Position = 0 },
            new PlaylistEntry
            {
                Id = Guid.NewGuid(), PlaylistId = privatePlaylist.Id, TrackId = remix.Id,
                TrackObservationId = privateObservation.Id, Position = 0
            },
            new PlaylistEntry { Id = Guid.NewGuid(), PlaylistId = privatePlaylist.Id, TrackId = unrelated.Id, Position = 1 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var details = new MusicLibraryQueryService(db);

        foreach (var related in new[] { sibling, remix, source })
        {
            var detail = Assert.IsType<MusicLibrarySongDto>(await details.GetCanonicalSongAsync(related.Id, owner.Id, default));
            Assert.Empty(detail.Playlists);
            Assert.Empty(detail.SourceIdentities);
            Assert.Empty(detail.PlatformLinks);
            Assert.Null(detail.DurationSeconds);
            Assert.Null(detail.ThumbnailUrl);
            var provider = new CapturingProvider();

            var lyrics = await new LyricsService(db, provider).GetLyricsAsync(owner.Id, detail.Id, default);

            Assert.Equal(LyricsStates.Available, lyrics?.State);
            Assert.Equal(related.Id, provider.Lookup?.TrackId);
            Assert.Equal(detail.Title, provider.Lookup?.Title);
            Assert.Null(provider.Lookup?.DurationSeconds);

            var outsiderProvider = new CapturingProvider();
            Assert.Null(await details.GetCanonicalSongAsync(related.Id, outsider.Id, default));
            Assert.Null(await new LyricsService(db, outsiderProvider).GetLyricsAsync(outsider.Id, detail.Id, default));
            Assert.Null(outsiderProvider.Lookup);
        }

        foreach (var denied in new[] { secondHop, unrelated })
        {
            var provider = new CapturingProvider();
            Assert.Null(await details.GetCanonicalSongAsync(denied.Id, owner.Id, default));
            Assert.Null(await new LyricsService(db, provider).GetLyricsAsync(owner.Id, $"track:{denied.Id}", default));
            Assert.Null(provider.Lookup);
        }
        var observationProvider = new CapturingProvider();
        Assert.Null(await new LyricsService(db, observationProvider).GetLyricsAsync(
            owner.Id, $"observation:{privateObservation.Id}", default));
        Assert.Null(observationProvider.Lookup);
        Assert.Equal(3, await db.PlaylistEntries.CountAsync());
    }

    [Fact]
    public async Task GetLyricsAsync_UsesCanonicalMetadataForLinkedObservation()
    {
        await using var db = CreateDb();
        var track = new Track
        {
            Id = Guid.NewGuid(),
            CanonicalMetadata = JsonSerializer.Serialize(new TrackCanonicalMetadata
            {
                Title = "Canonical Song",
                Artist = "Canonical Artist"
            })
        };
        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(),
            SourceType = "youtube",
            ExternalId = "video-id",
            Title = "Raw video title",
            MatchStatus = TrackMatchingStatuses.Matched,
            TrackId = track.Id
        };
        var playlist = new Playlist { Id = Guid.NewGuid(), UserId = 7, Name = "Favorites" };
        db.AddRange(track, observation, playlist, new PlaylistEntry
        {
            Id = Guid.NewGuid(),
            PlaylistId = playlist.Id,
            TrackObservationId = observation.Id,
            Position = 0
        });
        await db.SaveChangesAsync();
        var provider = new CapturingProvider();

        var result = await new LyricsService(db, provider).GetLyricsAsync(
            7, $"observation:{observation.Id}", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(track.Id, provider.Lookup?.TrackId);
        Assert.Equal("Canonical Song", provider.Lookup?.Title);
        Assert.Equal("Canonical Artist", provider.Lookup?.Artist);
    }

    [Fact]
    public async Task GetLyricsAsync_UsesParsedMetadataForUnlinkedObservation()
    {
        await using var db = CreateDb();
        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(),
            SourceType = "youtube",
            ExternalId = "video-id",
            Title = "Test Artist - Test Song (Official Video)",
            Artist = "Test Artist",
            DurationSeconds = 200,
            MatchStatus = TrackMatchingStatuses.NoMatch
        };
        var playlist = new Playlist { Id = Guid.NewGuid(), UserId = 7, Name = "Favorites" };
        db.AddRange(observation, playlist, new PlaylistEntry
        {
            Id = Guid.NewGuid(),
            PlaylistId = playlist.Id,
            TrackObservationId = observation.Id,
            Position = 0
        });
        await db.SaveChangesAsync();
        var provider = new CapturingProvider();

        var result = await new LyricsService(db, provider).GetLyricsAsync(
            7, $"observation:{observation.Id}", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(observation.Id, provider.Lookup?.TrackId);
        Assert.Equal("Test Song", provider.Lookup?.Title);
        Assert.Equal("Test Artist", provider.Lookup?.Artist);
        Assert.Equal(200, provider.Lookup?.DurationSeconds);
    }

    [Fact]
    public async Task GetLyricsAsync_RejectsObservationOutsideUsersLibrary()
    {
        await using var db = CreateDb();
        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(),
            SourceType = "spotify",
            ExternalId = "source-id",
            Title = "Private Song",
            Artist = "Private Artist",
            MatchStatus = TrackMatchingStatuses.NoMatch
        };
        var playlist = new Playlist { Id = Guid.NewGuid(), UserId = 8, Name = "Private" };
        db.AddRange(observation, playlist, new PlaylistEntry
        {
            Id = Guid.NewGuid(),
            PlaylistId = playlist.Id,
            TrackObservationId = observation.Id,
            Position = 0
        });
        await db.SaveChangesAsync();
        var provider = new CapturingProvider();

        var result = await new LyricsService(db, provider).GetLyricsAsync(
            7, $"observation:{observation.Id}", CancellationToken.None);

        Assert.Null(result);
        Assert.Null(provider.Lookup);
    }

    [Fact]
    public async Task GetLyricsAsync_ReturnsUnavailableForObservationWithoutArtist()
    {
        await using var db = CreateDb();
        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(),
            SourceType = "spotify",
            ExternalId = "source-id",
            Title = "Song Without Artist",
            MatchStatus = TrackMatchingStatuses.NoMatch
        };
        var playlist = new Playlist { Id = Guid.NewGuid(), UserId = 7, Name = "Favorites" };
        db.AddRange(observation, playlist, new PlaylistEntry
        {
            Id = Guid.NewGuid(),
            PlaylistId = playlist.Id,
            TrackObservationId = observation.Id,
            Position = 0
        });
        await db.SaveChangesAsync();
        var provider = new CapturingProvider();

        var result = await new LyricsService(db, provider).GetLyricsAsync(
            7, $"observation:{observation.Id}", CancellationToken.None);

        Assert.Equal(LyricsStates.Unavailable, result?.State);
        Assert.Null(provider.Lookup);
    }

    [Theory]
    [InlineData("observation:not-a-guid")]
    [InlineData("entry:7772ae09-8ad1-497e-90aa-1357aadd31ca")]
    public async Task GetLyricsAsync_RejectsInvalidSongIdentifiers(string songId)
    {
        await using var db = CreateDb();
        var provider = new CapturingProvider();

        var result = await new LyricsService(db, provider).GetLyricsAsync(7, songId, CancellationToken.None);

        Assert.Null(result);
        Assert.Null(provider.Lookup);
    }

    [Fact]
    public async Task GetLyricsAsync_ReturnsUnavailableWhenCanonicalMetadataIsIncomplete()
    {
        await using var db = CreateDb();
        var trackId = Guid.NewGuid();
        var playlist = new Playlist { Id = Guid.NewGuid(), UserId = 7, Name = "Favorites" };
        var track = new Track
        {
            Id = trackId,
            CanonicalMetadata = JsonSerializer.Serialize(new TrackCanonicalMetadata { Title = "Test Song" })
        };
        db.AddRange(playlist, track, new PlaylistEntry
        {
            Id = Guid.NewGuid(),
            PlaylistId = playlist.Id,
            TrackId = trackId,
            Position = 0
        });
        await db.SaveChangesAsync();
        var provider = new CapturingProvider();

        var result = await new LyricsService(db, provider).GetLyricsAsync(7, trackId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(LyricsStates.Unavailable, result.State);
        Assert.Null(provider.Lookup);
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed class CapturingProvider : ILyricsProvider
    {
        public LyricsLookup? Lookup { get; private set; }

        public Task<LyricsResult> GetLyricsAsync(LyricsLookup lookup, CancellationToken cancellationToken)
        {
            Lookup = lookup;
            return Task.FromResult(new LyricsResult
            {
                State = LyricsStates.Available,
                MatchStatus = LyricsMatchStatuses.Exact,
                Provider = "test",
                Attribution = "Test"
            });
        }
    }
}
