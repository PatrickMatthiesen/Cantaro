using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class SongDomainModelTests
{
    [Fact]
    public async Task Song_CanOwnMultipleTrackVersions_WhileTransitionAllowsUnassignedTracks()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateContextAsync(connection);

        var now = DateTimeOffset.UtcNow;
        var song = new Song { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now };
        song.Tracks.Add(NewTrack(now, song));
        song.Tracks.Add(NewTrack(now, song));
        db.Songs.Add(song);
        db.Tracks.Add(NewTrack(now));

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var storedSong = await db.Songs.Include(x => x.Tracks).SingleAsync();
        Assert.Equal(2, storedSong.Tracks.Count);
        Assert.Equal(3, await db.Tracks.CountAsync());
        Assert.Single(await db.Tracks.Where(x => x.SongId == null).ToListAsync());
    }

    [Fact]
    public async Task Song_CannotBeDeletedWhileTrackVersionsReferenceIt()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateContextAsync(connection);

        var now = DateTimeOffset.UtcNow;
        var song = new Song { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now };
        db.Songs.Add(song);
        db.Tracks.Add(NewTrack(now, song));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        db.Songs.Remove(await db.Songs.SingleAsync());

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
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

    private static Track NewTrack(DateTimeOffset now, Song? song = null) => new()
    {
        Id = Guid.NewGuid(),
        SongId = song?.Id,
        Song = song,
        CreatedAt = now,
        UpdatedAt = now
    };
}
