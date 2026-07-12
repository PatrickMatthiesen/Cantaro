using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class TrackVersionTraitDomainTests
{
    [Fact]
    public async Task Track_AllowsSeveralActiveTraitsFromAuditableEvidence()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateContextAsync(connection);
        var track = NewTrack();
        db.Tracks.Add(track);
        db.TrackVersionTraits.AddRange(
            NewTrait(track, TrackVersionTraitKeys.Live, "observation:1"),
            NewTrait(track, TrackVersionTraitKeys.Acoustic, "observation:1"));

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var stored = await db.Tracks.Include(x => x.VersionTraits).SingleAsync();
        Assert.Equal(2, stored.VersionTraits.Count);
    }

    [Fact]
    public async Task Track_RejectsDuplicateActiveAssertionButAllowsReassertionAfterRevocation()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateContextAsync(connection);
        var track = NewTrack();
        var assertion = NewTrait(track, TrackVersionTraitKeys.Live, "observation:1");
        db.AddRange(track, assertion);
        await db.SaveChangesAsync();

        db.TrackVersionTraits.Add(NewTrait(track, TrackVersionTraitKeys.Live, "observation:1"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        assertion = await db.TrackVersionTraits.SingleAsync();
        assertion.RevokedAt = assertion.CreatedAt.AddMinutes(1);
        assertion.RevokedByType = "system";
        assertion.RevokedById = "test";
        assertion.RevocationReason = "superseded";
        db.TrackVersionTraits.Add(NewTrait(track, TrackVersionTraitKeys.Live, "observation:1"));
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.TrackVersionTraits.CountAsync());
        Assert.Single(await db.TrackVersionTraits.Where(x => x.RevokedAt == null).ToListAsync());
    }

    [Fact]
    public async Task Track_AllowsSameTraitFromDifferentEvidenceIdentities()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateContextAsync(connection);
        var track = NewTrack();
        db.AddRange(
            track,
            NewTrait(track, TrackVersionTraitKeys.Live, "observation:1"),
            NewTrait(track, TrackVersionTraitKeys.Live, "observation:2"));

        await db.SaveChangesAsync();

        Assert.Equal(2, await db.TrackVersionTraits.CountAsync());
    }

    [Fact]
    public async Task Track_AcceptsConfidenceBoundaries()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateContextAsync(connection);
        var track = NewTrack();
        db.AddRange(
            track,
            NewTrait(track, TrackVersionTraitKeys.Live, "lower", 0m),
            NewTrait(track, TrackVersionTraitKeys.Acoustic, "upper", 1m));

        await db.SaveChangesAsync();

        Assert.Equal(2, await db.TrackVersionTraits.CountAsync());
    }

    [Fact]
    public async Task Track_RequiresCompleteAndChronologicalRevocationAudit()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateContextAsync(connection);
        var track = NewTrack();
        var assertion = NewTrait(track, TrackVersionTraitKeys.Live, "test");
        assertion.RevokedAt = assertion.CreatedAt.AddMinutes(-1);
        assertion.RevokedByType = "user";
        assertion.RevokedById = "user:1";
        assertion.RevocationReason = "Rejected.";
        db.AddRange(track, assertion);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        track = NewTrack();
        assertion = NewTrait(track, TrackVersionTraitKeys.Live, "test");
        assertion.RevokedAt = assertion.CreatedAt.AddMinutes(1);
        db.AddRange(track, assertion);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Theory]
    [InlineData(-0.0001)]
    [InlineData(1.0001)]
    public async Task Track_RejectsInvalidConfidence(double confidence)
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateContextAsync(connection);
        var track = NewTrack();
        db.AddRange(track, NewTrait(track, TrackVersionTraitKeys.Live, "test", (decimal)confidence));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
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

    private static Track NewTrack()
    {
        var now = DateTimeOffset.UtcNow;
        return new Track { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now };
    }

    private static TrackVersionTrait NewTrait(
        Track track,
        string traitKey,
        string evidenceIdentity,
        decimal confidence = 0.9m) => new()
        {
            Id = Guid.NewGuid(),
            TrackId = track.Id,
            TraitKey = traitKey,
            Confidence = confidence,
            EvidenceSource = "provider-observation",
            EvidenceIdentity = evidenceIdentity,
            EvidenceMethod = "title-parser",
            MethodVersion = "1",
            AssertedByType = "system",
            AssertedById = "test",
            CreatedAt = DateTimeOffset.UtcNow
        };
}
