using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class TrackIdentityResolverTests
{
    [Theory]
    [InlineData(160)]
    [InlineData(null)]
    public async Task ResolveExistingAsync_MetadataMatchDoesNotRequireSimilarDuration(int? incomingDuration)
    {
        await using var database = await CreateDatabaseAsync();
        var track = NewTrack();
        database.Db.Tracks.Add(track);
        database.Db.TrackObservations.Add(NewObservation(track.Id, "spotify", "spotify-track",
            "Everything Goes On", "Porter Robinson, League of Legends", 202));
        await database.Db.SaveChangesAsync();

        var resolver = new TrackIdentityResolver(database.Db);
        var match = await resolver.ResolveExistingAsync(new TrackIdentityQuery(
            "youtube", "youtube-video", "Everything Goes On",
            "Porter Robinson, League of Legends", incomingDuration), CancellationToken.None);

        Assert.NotNull(match);
        Assert.Equal(track.Id, match.Track.Id);
        Assert.Equal(TrackIdentityMatchKind.Metadata, match.Kind);
    }

    [Fact]
    public async Task ResolveExistingAsync_UsesDescriptionCreditsToReuseCanonicalTrack()
    {
        await using var database = await CreateDatabaseAsync();
        var track = NewTrack();
        database.Db.Tracks.Add(track);
        database.Db.TrackObservations.Add(NewObservation(track.Id, "spotify", "spotify-mortals",
            "Mortals Funk Remix", "LXNGVX, Warriyo", 146));
        await database.Db.SaveChangesAsync();

        var incomingMetadata = """{"OriginalTitle":"MORTALS FUNK REMIX // Victory Royale B**ch!","OriginalArtist":"MrMoMMusic","ChannelTitle":"MrMoMMusic","Description":"LXNGVX, Warriyo - Mortals Funk Remix"}""";
        var resolver = new TrackIdentityResolver(database.Db);
        var match = await resolver.ResolveExistingAsync(new TrackIdentityQuery(
            "youtube", "mortals-video", "MORTALS FUNK REMIX // Victory Royale B**ch!",
            "MrMoMMusic", 147, RawMetadata: incomingMetadata), CancellationToken.None);

        Assert.NotNull(match);
        Assert.Equal(track.Id, match.Track.Id);
        Assert.Equal(TrackIdentityMatchKind.Metadata, match.Kind);
    }

    [Fact]
    public async Task ResolveExistingAsync_IneligibleVersionDoesNotBlockUniqueTrackMatch()
    {
        await using var database = await CreateDatabaseAsync();
        var normal = NewTrack();
        var live = NewTrack();
        database.Db.Tracks.AddRange(normal, live);
        database.Db.TrackObservations.AddRange(
            NewObservation(normal.Id, "spotify", "normal", "Song", "Singer", 180),
            NewObservation(live.Id, "spotify", "live", "Song (Live)", "Singer", 180));
        await database.Db.SaveChangesAsync();

        var resolver = new TrackIdentityResolver(database.Db);
        var match = await resolver.ResolveExistingAsync(new TrackIdentityQuery(
            "youtube", "new-version", "Song", "Singer", 180), CancellationToken.None);

        Assert.NotNull(match);
        Assert.Equal(normal.Id, match.Track.Id);
        Assert.Equal(TrackIdentityMatchKind.Metadata, match.Kind);
    }

    [Fact]
    public async Task ResolveExistingAsync_MetadataMatchDoesNotChooseBetweenDistinctTracks()
    {
        await using var database = await CreateDatabaseAsync();
        var first = NewTrack();
        var second = NewTrack();
        database.Db.Tracks.AddRange(first, second);
        database.Db.TrackObservations.AddRange(
            NewObservation(first.Id, "spotify", "spotify-track-1", "Everything Goes On", "Porter Robinson", 202),
            NewObservation(second.Id, "youtube", "youtube-video-2", "Everything Goes On", "Porter Robinson", 160));
        await database.Db.SaveChangesAsync();

        var resolver = new TrackIdentityResolver(database.Db);
        var match = await resolver.ResolveExistingAsync(new TrackIdentityQuery(
            "youtube", "new-youtube-video", "Everything Goes On", "Porter Robinson", 180),
            CancellationToken.None);

        Assert.Null(match);
    }

    [Fact]
    public async Task ResolveExistingAsync_StaleExactMappingCanUseUniqueIsrcDespiteDurationDifference()
    {
        await using var database = await CreateDatabaseAsync();
        var stale = NewTrack(isrc: "USRC17607839");
        var intended = NewTrack(isrc: "USRC17607840");
        database.Db.Tracks.AddRange(stale, intended);
        database.Db.TrackSourceIds.Add(new TrackSourceId
        {
            Id = Guid.NewGuid(), TrackId = stale.Id,
            SourceType = "youtube", ExternalId = "youtube-video"
        });
        database.Db.TrackObservations.Add(NewObservation(
            intended.Id, "spotify", "spotify-track", "Everything Goes On", "Porter Robinson", 202));
        await database.Db.SaveChangesAsync();

        var resolver = new TrackIdentityResolver(database.Db);
        var match = await resolver.ResolveExistingAsync(new TrackIdentityQuery(
            "youtube", "youtube-video", "Everything Goes On", "Porter Robinson", 160,
            Isrc: "USRC17607840"), CancellationToken.None);

        Assert.NotNull(match);
        Assert.Equal(intended.Id, match.Track.Id);
        Assert.Equal(TrackIdentityMatchKind.Isrc, match.Kind);
    }

    private static async Task<DatabaseScope> CreateDatabaseAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return new DatabaseScope(db, connection);
    }

    private static Track NewTrack(string? isrc = null) => new()
    {
        Id = Guid.NewGuid(),
        Isrc = isrc,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static TrackObservation NewObservation(
        Guid trackId,
        string sourceType,
        string externalId,
        string title,
        string artist,
        int durationSeconds) => new()
    {
        Id = Guid.NewGuid(),
        TrackId = trackId,
        SourceType = sourceType,
        ExternalId = externalId,
        Title = title,
        Artist = artist,
        NormalizedTitle = TrackTextNormalizer.Normalize(title),
        NormalizedArtist = TrackTextNormalizer.Normalize(artist),
        DurationSeconds = durationSeconds,
        MatchStatus = TrackMatchingStatuses.Matched,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private sealed class DatabaseScope(ApplicationDbContext db, SqliteConnection connection) : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = db;

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
