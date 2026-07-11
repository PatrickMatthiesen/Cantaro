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
    public async Task GetLyricsAsync_AllowsCanonicalTrackOutsideUsersLibrary()
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

        Assert.NotNull(result);
        Assert.Equal(trackId, provider.Lookup?.TrackId);
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
