using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services.Spotify;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class SpotifyTrackResolverTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 28, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ResolveAsync_CreatesCanonicalIdentityAndStructuredArtistCredits()
    {
        await using var fixture = await ResolverFixture.CreateAsync();

        var track = await fixture.Resolver.ResolveAsync(
            Snapshot(
                id: "spotify-track",
                isrc: "us-sp0-26-00001",
                artistNames: ["First artist", "Featured artist"]),
            Now,
            CancellationToken.None);
        await fixture.DbContext.SaveChangesAsync();

        fixture.DbContext.ChangeTracker.Clear();
        var storedTrack = await fixture.DbContext.Tracks
            .Include(candidate => candidate.SongMemberships)
            .Include(candidate => candidate.SourceIds)
            .Include(candidate => candidate.ArtistCredits)
                .ThenInclude(credit => credit.Artist)
            .SingleAsync(candidate => candidate.Id == track.Id);

        Assert.Equal("USSP02600001", storedTrack.Isrc);
        Assert.Single(storedTrack.SongMemberships);
        var sourceId = Assert.Single(storedTrack.SourceIds);
        Assert.Equal(SpotifyService.ServiceName, sourceId.SourceType);
        Assert.Equal("spotify-track", sourceId.ExternalId);
        Assert.Equal(1m, sourceId.Confidence);
        Assert.True(sourceId.IsOfficial);
        Assert.Equal(
            ["First artist", "Featured artist"],
            storedTrack.ArtistCredits
                .OrderBy(credit => credit.Position)
                .Select(credit => credit.Artist!.Name)
                .ToArray());
    }

    [Fact]
    public async Task ResolveAsync_ReusesTheOnlyTrackWithTheSameIsrc()
    {
        await using var fixture = await ResolverFixture.CreateAsync();
        var existing = fixture.AddTrackWithSong("USSP02600001");
        await fixture.DbContext.SaveChangesAsync();

        var resolved = await fixture.Resolver.ResolveAsync(
            Snapshot(id: "spotify-track", isrc: "USSP02600001"),
            Now,
            CancellationToken.None);
        await fixture.DbContext.SaveChangesAsync();

        Assert.Equal(existing.Id, resolved.Id);
        Assert.Equal(1, await fixture.DbContext.Tracks.CountAsync());
        var sourceId = await fixture.DbContext.TrackSourceIds.SingleAsync();
        Assert.Equal(existing.Id, sourceId.TrackId);
    }

    [Fact]
    public async Task ResolveAsync_LinksDifferentSpotifyIdsWithTheSameUniqueIsrc()
    {
        await using var fixture = await ResolverFixture.CreateAsync();

        var first = await fixture.Resolver.ResolveAsync(
            Snapshot(id: "spotify-track-one", isrc: "USSP02600001"),
            Now,
            CancellationToken.None);
        var second = await fixture.Resolver.ResolveAsync(
            Snapshot(id: "spotify-track-two", isrc: "USSP02600001"),
            Now,
            CancellationToken.None);
        await fixture.DbContext.SaveChangesAsync();

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await fixture.DbContext.Tracks.CountAsync());
        Assert.Equal(2, await fixture.DbContext.TrackSourceIds.CountAsync());
        Assert.All(
            await fixture.DbContext.TrackSourceIds.ToListAsync(),
            sourceId => Assert.Equal(first.Id, sourceId.TrackId));
    }

    [Fact]
    public async Task ResolveAsync_DoesNotGuessWhenSeveralTracksShareTheIsrc()
    {
        await using var fixture = await ResolverFixture.CreateAsync();
        var first = fixture.AddTrackWithSong("USSP02600001");
        var second = fixture.AddTrackWithSong("USSP02600001");
        await fixture.DbContext.SaveChangesAsync();

        var resolved = await fixture.Resolver.ResolveAsync(
            Snapshot(id: "spotify-track", isrc: "USSP02600001"),
            Now,
            CancellationToken.None);
        await fixture.DbContext.SaveChangesAsync();

        Assert.NotEqual(first.Id, resolved.Id);
        Assert.NotEqual(second.Id, resolved.Id);
        Assert.Equal(3, await fixture.DbContext.Tracks.CountAsync());
        Assert.Equal(resolved.Id, await fixture.DbContext.TrackSourceIds
            .Where(sourceId => sourceId.SourceType == SpotifyService.ServiceName)
            .Select(sourceId => sourceId.TrackId)
            .SingleAsync());
    }

    private static SpotifyTrackSnapshot Snapshot(
        string id,
        string? isrc,
        IReadOnlyList<string>? artistNames = null)
    {
        artistNames ??= ["Artist"];
        return new SpotifyTrackSnapshot(
            id,
            "Track title",
            string.Join(", ", artistNames),
            artistNames,
            "Album",
            "https://i.scdn.co/image/temporary",
            $"https://open.spotify.com/track/{id}",
            "https://open.spotify.com/album/album",
            isrc,
            180,
            Now,
            0);
    }

    private sealed class ResolverFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private ResolverFixture(SqliteConnection connection, ApplicationDbContext dbContext)
        {
            _connection = connection;
            DbContext = dbContext;
            Resolver = new SpotifyTrackResolver(dbContext);
        }

        public ApplicationDbContext DbContext { get; }
        public SpotifyTrackResolver Resolver { get; }

        public static async Task<ResolverFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;
            var dbContext = new ApplicationDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();
            return new ResolverFixture(connection, dbContext);
        }

        public Track AddTrackWithSong(string isrc)
        {
            var song = new Song
            {
                Id = Guid.NewGuid(),
                CreatedAt = Now,
                UpdatedAt = Now
            };
            var track = new Track
            {
                Id = Guid.NewGuid(),
                Isrc = isrc,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            DbContext.AddRange(song, track, new SongTrack
            {
                SongId = song.Id,
                Song = song,
                TrackId = track.Id,
                Track = track
            });
            return track;
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
