using System.Reflection;
using Cantaro.Api.Data;
using Cantaro.Api.Migrations;
using Cantaro.Api.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class ArtistDomainModelTests
{
    [Fact]
    public async Task ArtistCredits_PreserveOrderRolesAndCreditedNames()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var track = NewTrack();
        var primary = NewArtist("Alpha", "0d5f4b04-3be8-427a-8c24-23b1454b2f31");
        var featured = NewArtist("Beta", "82e3e1de-9243-4e58-b2d2-796c64f76c82");
        var remixer = NewArtist("Gamma");
        var producer = NewArtist("Delta");
        track.ArtistCredits =
        [
            Credit(track, remixer, TrackArtistRole.Remixer, 3, "Gamma Remix"),
            Credit(track, primary, TrackArtistRole.Primary, 0, "Alpha"),
            Credit(track, featured, TrackArtistRole.Featured, 1, "feat. Beta"),
            Credit(track, producer, TrackArtistRole.Producer, 2, "Delta")
        ];

        db.Tracks.Add(track);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var credits = await db.TrackArtistCredits
            .Include(credit => credit.Artist)
            .Where(credit => credit.TrackId == track.Id)
            .OrderBy(credit => credit.Position)
            .ToListAsync();

        Assert.Collection(
            credits,
            credit =>
            {
                Assert.Equal(TrackArtistRole.Primary, credit.Role);
                Assert.Equal("Alpha", credit.CreditedName);
                Assert.Equal(primary.MusicBrainzArtistId, credit.Artist?.MusicBrainzArtistId);
            },
            credit =>
            {
                Assert.Equal(TrackArtistRole.Featured, credit.Role);
                Assert.Equal("feat. Beta", credit.CreditedName);
            },
            credit => Assert.Equal(TrackArtistRole.Producer, credit.Role),
            credit => Assert.Equal(TrackArtistRole.Remixer, credit.Role));
    }

    [Fact]
    public async Task Artists_WithSameNameRemainSeparateWithoutStableIdentity()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Artists.AddRange(NewArtist("The Twins"), NewArtist("The Twins"));
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.Artists.CountAsync(artist => artist.Name == "The Twins"));
    }

    [Fact]
    public async Task Artists_CannotDuplicateStableMusicBrainzIdentity()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        const string mbid = "0d5f4b04-3be8-427a-8c24-23b1454b2f31";
        db.Artists.Add(NewArtist("First name", mbid));
        await db.SaveChangesAsync();
        db.Artists.Add(NewArtist("Later alias", mbid.ToUpperInvariant()));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public void AddArtistCreditsMigration_ConservativelyRetainsLegacyArtistText()
    {
        var migration = new AddArtistCredits();
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(AddArtistCredits)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);

        var operation = Assert.Single(builder.Operations.OfType<SqlOperation>());
        Assert.Contains("metadata ->> 'Artist'", operation.Sql, StringComparison.Ordinal);
        Assert.Contains("source_track.\"Id\"", operation.Sql, StringComparison.Ordinal);
        Assert.Contains("'Primary'", operation.Sql, StringComparison.Ordinal);
        Assert.Contains("EXCEPTION WHEN others", operation.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("PARTITION BY", operation.Sql, StringComparison.OrdinalIgnoreCase);
    }

    private static Track NewTrack()
    {
        var now = DateTimeOffset.UtcNow;
        return new Track { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now };
    }

    private static Artist NewArtist(string name, string? mbid = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new Artist
        {
            Id = Guid.NewGuid(), Name = name, MusicBrainzArtistId = mbid,
            CreatedAt = now, UpdatedAt = now
        };
    }

    private static TrackArtistCredit Credit(
        Track track,
        Artist artist,
        TrackArtistRole role,
        int position,
        string creditedName) => new()
    {
        Id = Guid.NewGuid(), TrackId = track.Id, Track = track,
        ArtistId = artist.Id, Artist = artist, Role = role,
        Position = position, CreditedName = creditedName
    };
}
