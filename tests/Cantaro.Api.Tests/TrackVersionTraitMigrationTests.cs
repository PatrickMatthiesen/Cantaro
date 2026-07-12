using System.Reflection;
using Cantaro.Api.Data;
using Cantaro.Api.Migrations;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class TrackVersionTraitMigrationTests
{
    private const string PreviousMigration = "20260712200312_AddSongIdentityFoundation";

    [Fact]
    public void Migration_IsAdditiveAndTouchesOnlyTraitEvidenceSchema()
    {
        var migration = new AddTrackVersionTraitEvidence();
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(AddTrackVersionTraitEvidence)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);

        var table = Assert.Single(builder.Operations.OfType<CreateTableOperation>());
        Assert.Equal("TrackVersionTraits", table.Name);
        Assert.All(
            builder.Operations.OfType<CreateIndexOperation>(),
            index => Assert.Equal("TrackVersionTraits", index.Table));
        Assert.DoesNotContain(builder.Operations, operation =>
            operation is AddColumnOperation
                or AlterColumnOperation
                or DropColumnOperation
                or SqlOperation);
    }

    [Fact]
    public async Task PostgreSqlUpgrade_AddsAuditableTraitEvidenceWithoutChangingSongOrProviderModels()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable("CANTARO_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            return;
        }

        var databaseName = $"cantaro_trait_migration_{Guid.NewGuid():N}";
        var adminBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = "postgres" };
        var databaseBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = databaseName };
        await using var admin = new NpgsqlConnection(adminBuilder.ConnectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin))
        {
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(databaseBuilder.ConnectionString)
                .Options;
            await using var db = new ApplicationDbContext(options);
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);

            var now = new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);
            var song = new Song { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now };
            var track = new Track
            {
                Id = Guid.NewGuid(),
                SongId = song.Id,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.AddRange(song, track);
            await db.SaveChangesAsync();

            await migrator.MigrateAsync();
            var service = new TrackVersionTraitEvidenceService(
                db,
                new TrackVersionTraitVocabulary(),
                TimeProvider.System);
            var first = await service.AssertAsync(Command(track.Id, TrackVersionTraitKeys.Live, "observation:1"));
            var replay = await service.AssertAsync(Command(track.Id, TrackVersionTraitKeys.Live, "observation:1"));
            await service.AssertAsync(Command(track.Id, TrackVersionTraitKeys.Acoustic, "observation:1"));

            Assert.Equal(first.Id, replay.Id);
            Assert.Equal(2, await db.TrackVersionTraits.CountAsync());
            Assert.Equal(song.Id, (await db.Tracks.AsNoTracking().SingleAsync()).SongId);
            Assert.Empty(await db.TrackSourceIds.AsNoTracking().ToListAsync());

            var disposableTrack = new Track
            {
                Id = Guid.NewGuid(),
                SongId = song.Id,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.Tracks.Add(disposableTrack);
            await db.SaveChangesAsync();
            await service.AssertAsync(Command(disposableTrack.Id, TrackVersionTraitKeys.Live, "replace-me"));
            await service.AssertAsync(
                Command(disposableTrack.Id, TrackVersionTraitKeys.Live, "replace-me") with
                {
                    Confidence = 0.7m,
                    MethodVersion = "2"
                });
            db.Tracks.Remove(disposableTrack);
            await db.SaveChangesAsync();
            Assert.Empty(await db.TrackVersionTraits.Where(x => x.TrackId == disposableTrack.Id).ToListAsync());

            db.TrackVersionTraits.Add(new TrackVersionTrait
            {
                Id = Guid.NewGuid(),
                TrackId = track.Id,
                TraitKey = TrackVersionTraitKeys.Demo,
                Confidence = 1.0001m,
                EvidenceSource = "test",
                EvidenceIdentity = "invalid-confidence",
                EvidenceMethod = "test",
                AssertedByType = "system",
                AssertedById = "tests",
                CreatedAt = now
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();

            db.TrackVersionTraits.Add(InvalidRevocation(track.Id, now, completeAudit: true));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();

            db.TrackVersionTraits.Add(InvalidRevocation(track.Id, now, completeAudit: false));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();

            await using (var connection = new NpgsqlConnection(databaseBuilder.ConnectionString))
            {
                await connection.OpenAsync();
                await using var index = new NpgsqlCommand(
                    """
                    SELECT indexdef
                    FROM pg_indexes
                    WHERE schemaname = 'public'
                      AND indexname = 'UX_TrackVersionTraits_ActiveEvidence';
                    """,
                    connection);
                var definition = Assert.IsType<string>(await index.ExecuteScalarAsync());
                Assert.Contains("UNIQUE INDEX", definition, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("RevokedAt", definition, StringComparison.Ordinal);
                Assert.Contains("IS NULL", definition, StringComparison.OrdinalIgnoreCase);
            }

            await migrator.MigrateAsync(PreviousMigration);
            await using var shapeConnection = new NpgsqlConnection(databaseBuilder.ConnectionString);
            await shapeConnection.OpenAsync();
            await using var shape = new NpgsqlCommand(
                """
                SELECT
                    to_regclass('public."TrackVersionTraits"') IS NULL,
                    (SELECT count(*) FROM "Songs"),
                    (SELECT count(*) FROM "Tracks");
                """,
                shapeConnection);
            await using var reader = await shape.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.True(reader.GetBoolean(0));
            Assert.Equal(1L, reader.GetInt64(1));
            Assert.Equal(1L, reader.GetInt64(2));
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static AssertTrackVersionTraitCommand Command(
        Guid trackId,
        string traitKey,
        string evidenceIdentity) => new(
        trackId,
        traitKey,
        0.9m,
        "provider-observation",
        evidenceIdentity,
        "manual-review",
        "1",
        "user",
        "test-user");

    private static TrackVersionTrait InvalidRevocation(
        Guid trackId,
        DateTimeOffset createdAt,
        bool completeAudit) => new()
        {
            Id = Guid.NewGuid(),
            TrackId = trackId,
            TraitKey = TrackVersionTraitKeys.Demo,
            Confidence = 0.5m,
            EvidenceSource = "test",
            EvidenceIdentity = completeAudit ? "chronology" : "incomplete-audit",
            EvidenceMethod = "test",
            AssertedByType = "system",
            AssertedById = "tests",
            CreatedAt = createdAt,
            RevokedAt = completeAudit ? createdAt.AddMinutes(-1) : createdAt.AddMinutes(1),
            RevokedByType = completeAudit ? "user" : null,
            RevokedById = completeAudit ? "user:1" : null,
            RevocationReason = completeAudit ? "Rejected." : null
        };
}
