using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class SongDomainModelTests
{
    [Fact]
    public async Task SongTrack_SupportsMultipleVersionsAndMultiSongTracks()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateContextAsync(connection);

        var now = DateTimeOffset.UtcNow;
        var firstSong = NewSong(now);
        var secondSong = NewSong(now);
        var studio = NewTrack(now);
        var acoustic = NewTrack(now, TrackVersionFlags.Acoustic);
        var mashup = NewTrack(now, TrackVersionFlags.Mashup);
        db.AddRange(firstSong, secondSong, studio, acoustic, mashup);
        db.SongTracks.AddRange(
            Membership(firstSong, studio),
            Membership(firstSong, acoustic),
            Membership(firstSong, mashup),
            Membership(secondSong, mashup));

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        Assert.Equal(3, await db.SongTracks.CountAsync(x => x.SongId == firstSong.Id));
        Assert.Equal(2, await db.SongTracks.CountAsync(x => x.TrackId == mashup.Id));
        Assert.Equal(
            TrackVersionFlags.Acoustic,
            (await db.Tracks.SingleAsync(x => x.Id == acoustic.Id)).VersionFlags);
    }

    [Fact]
    public void SongTrack_HasNarrowPrimaryAndReverseCoveringIndexes()
    {
        using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite("Data Source=:memory:")
                .Options);
        var entity = db.Model.FindEntityType(typeof(SongTrack))!;

        Assert.Equal(
            [nameof(SongTrack.SongId), nameof(SongTrack.TrackId)],
            entity.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Contains(
            entity.GetIndexes(),
            index => index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(SongTrack.TrackId), nameof(SongTrack.SongId)]));
        Assert.Equal(
            [nameof(SongTrack.SongId), nameof(SongTrack.TrackId)],
            entity.GetProperties().Select(property => property.Name).Order());
    }

    [Fact]
    public async Task TrackVersionFlags_PersistCombinedValuesAsOneScalar()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateContextAsync(connection);
        var now = DateTimeOffset.UtcNow;
        var flags = TrackVersionFlags.Live | TrackVersionFlags.Acoustic | TrackVersionFlags.Remastered;
        var track = NewTrack(now, flags);
        db.Tracks.Add(track);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var stored = await db.Tracks.SingleAsync();
        Assert.Equal(flags, stored.VersionFlags);
        Assert.True(stored.VersionFlags.HasFlag(TrackVersionFlags.Remastered));
        Assert.Equal(typeof(long), Enum.GetUnderlyingType(typeof(TrackVersionFlags)));
    }

    [Fact]
    public async Task TrackRelation_RejectsSelfAndDuplicateEdges()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateContextAsync(connection);
        var now = DateTimeOffset.UtcNow;
        var source = NewTrack(now);
        var remix = NewTrack(now, TrackVersionFlags.Remix);
        db.Tracks.AddRange(source, remix);
        db.TrackRelations.Add(new TrackRelation
        {
            FromTrackId = remix.Id,
            ToTrackId = source.Id,
            RelationType = TrackRelationType.RemixOf
        });
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        db.TrackRelations.Add(new TrackRelation
        {
            FromTrackId = remix.Id,
            ToTrackId = source.Id,
            RelationType = TrackRelationType.RemixOf
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        db.ChangeTracker.Clear();
        db.TrackRelations.Add(new TrackRelation
        {
            FromTrackId = source.Id,
            ToTrackId = source.Id,
            RelationType = TrackRelationType.DerivedFrom
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task SongAndTrackCredits_RepresentDifferentResponsibilities()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateContextAsync(connection);
        var now = DateTimeOffset.UtcNow;
        var song = NewSong(now);
        var track = NewTrack(now);
        var writer = NewArtist("Writer", now);
        var performer = NewArtist("Performer", now);
        db.AddRange(song, track, writer, performer, Membership(song, track));
        db.SongCredits.Add(new SongCredit
        {
            Id = Guid.NewGuid(),
            SongId = song.Id,
            ArtistId = writer.Id,
            Role = SongCreditRole.Writer,
            Position = 0,
            CreditedName = writer.Name
        });
        db.TrackArtistCredits.Add(new TrackArtistCredit
        {
            Id = Guid.NewGuid(),
            TrackId = track.Id,
            ArtistId = performer.Id,
            Role = TrackArtistRole.Primary,
            Position = 0,
            CreditedName = performer.Name
        });

        await db.SaveChangesAsync();

        Assert.Equal(SongCreditRole.Writer, (await db.SongCredits.SingleAsync()).Role);
        Assert.Equal(TrackArtistRole.Primary, (await db.TrackArtistCredits.SingleAsync()).Role);
    }

    private static async Task<ApplicationDbContext> CreateContextAsync(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private static Song NewSong(DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        CreatedAt = now,
        UpdatedAt = now
    };

    private static Track NewTrack(DateTimeOffset now, TrackVersionFlags flags = TrackVersionFlags.None) => new()
    {
        Id = Guid.NewGuid(),
        VersionFlags = flags,
        CreatedAt = now,
        UpdatedAt = now
    };

    private static SongTrack Membership(Song song, Track track) => new()
    {
        SongId = song.Id,
        TrackId = track.Id
    };

    private static Artist NewArtist(string name, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        CreatedAt = now,
        UpdatedAt = now
    };
}
