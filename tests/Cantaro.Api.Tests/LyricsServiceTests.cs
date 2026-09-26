using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Cantaro.Api.Services.Lyrics;
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
