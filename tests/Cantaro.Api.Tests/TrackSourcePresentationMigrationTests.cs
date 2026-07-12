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

public sealed class TrackSourcePresentationMigrationTests
{
    private const string PreviousMigration = "20260712202653_AddTrackVersionTraitEvidence";

    [Fact]
    public void Migration_OnlyAddsNullablePresentationColumnsAndChecks()
    {
        var migration = new AddTrackSourcePresentationMetadata();
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(AddTrackSourcePresentationMetadata)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);

        Assert.All(
            builder.Operations.OfType<AddColumnOperation>(),
            operation =>
            {
                Assert.Equal("TrackSourceIds", operation.Table);
                Assert.True(operation.IsNullable);
                Assert.Null(operation.DefaultValue);
                Assert.Null(operation.DefaultValueSql);
            });
        Assert.Equal(16, builder.Operations.OfType<AddColumnOperation>().Count());
        Assert.Equal(4, builder.Operations.OfType<AddCheckConstraintOperation>().Count());
        Assert.DoesNotContain(builder.Operations, operation =>
            operation is CreateIndexOperation
                or DropIndexOperation
                or SqlOperation);
    }

    [Fact]
    public async Task PostgreSqlUpgrade_PreservesSourceIdentityIndexAndExistingRows()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable("CANTARO_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            return;
        }

        var databaseName = $"cantaro_presentation_migration_{Guid.NewGuid():N}";
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
            var trackId = Guid.NewGuid();
            var sourceId = Guid.NewGuid();
            var now = new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Tracks" ("Id", "SongId", "CanonicalMetadata", "MbidRecording", "Isrc", "CreatedAt", "UpdatedAt")
                VALUES ({trackId}, NULL, NULL, NULL, NULL, {now}, {now});
                INSERT INTO "TrackSourceIds" ("Id", "TrackId", "SourceType", "ExternalId", "Confidence", "OriginMetadata", "LastVerifiedAt")
                VALUES ({sourceId}, {trackId}, 'youtube', 'video-1', NULL, NULL, NULL);
                """);
            var originalIndex = await ReadIdentityIndexAsync(databaseBuilder.ConnectionString);

            await migrator.MigrateAsync();
            var source = await db.TrackSourceIds.AsNoTracking().SingleAsync();
            Assert.Null(source.PresentationKind);
            Assert.Null(source.UploaderAuthority);
            Assert.Equal(originalIndex, await ReadIdentityIndexAsync(databaseBuilder.ConnectionString));

            var service = new TrackSourcePresentationClassificationService(
                db,
                new TrackSourcePresentationVocabulary(),
                TimeProvider.System);
            var classified = await service.ApplyAsync(
                sourceId,
                new TrackSourcePresentationPatch(
                    TrackSourceClassificationChange.Set(Proposal("music-video", "kind:1")),
                    TrackSourceClassificationChange.Set(Proposal("official", "authority:1"))));
            Assert.Equal(TrackSourcePresentationKinds.MusicVideo, classified.PresentationKind?.Value);
            Assert.Equal(TrackSourceUploaderAuthorities.Official, classified.UploaderAuthority?.Value);

            db.TrackSourceIds.Add(new TrackSourceId
            {
                Id = Guid.NewGuid(),
                TrackId = trackId,
                SourceType = "youtube",
                ExternalId = "invalid-bundle",
                PresentationKind = TrackSourcePresentationKinds.Audio
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();

            db.TrackSourceIds.Add(InvalidConfidence(trackId));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();

            db.TrackSourceIds.Add(InvalidPresentationConfidence(trackId));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();

            db.TrackSourceIds.Add(new TrackSourceId
            {
                Id = Guid.NewGuid(),
                TrackId = trackId,
                SourceType = "youtube",
                ExternalId = "video-1"
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();

            await migrator.MigrateAsync(PreviousMigration);
            Assert.Equal(originalIndex, await ReadIdentityIndexAsync(databaseBuilder.ConnectionString));
            await using var verify = new NpgsqlConnection(databaseBuilder.ConnectionString);
            await verify.OpenAsync();
            await using var command = new NpgsqlCommand(
                """
                SELECT count(*), min("SourceType"), min("ExternalId")
                FROM "TrackSourceIds";
                """,
                verify);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1L, reader.GetInt64(0));
            Assert.Equal("youtube", reader.GetString(1));
            Assert.Equal("video-1", reader.GetString(2));
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static TrackSourceClassificationProposal Proposal(string value, string identity) => new(
        value,
        0.9m,
        "provider-metadata",
        identity,
        "manual-review",
        "1");

    private static TrackSourceId InvalidConfidence(Guid trackId) => new()
    {
        Id = Guid.NewGuid(),
        TrackId = trackId,
        SourceType = "youtube",
        ExternalId = "invalid-confidence",
        UploaderAuthority = TrackSourceUploaderAuthorities.User,
        UploaderAuthorityConfidence = 1.0001m,
        UploaderAuthorityEvidenceSource = "test",
        UploaderAuthorityEvidenceIdentity = "invalid",
        UploaderAuthorityEvidenceMethod = "test",
        UploaderAuthorityClassifiedAt = DateTimeOffset.UtcNow,
        UploaderAuthorityRevision = Guid.NewGuid()
    };

    private static TrackSourceId InvalidPresentationConfidence(Guid trackId) => new()
    {
        Id = Guid.NewGuid(),
        TrackId = trackId,
        SourceType = "youtube",
        ExternalId = "invalid-presentation-confidence",
        PresentationKind = TrackSourcePresentationKinds.Audio,
        PresentationKindConfidence = -0.0001m,
        PresentationKindEvidenceSource = "test",
        PresentationKindEvidenceIdentity = "invalid",
        PresentationKindEvidenceMethod = "test",
        PresentationKindClassifiedAt = DateTimeOffset.UtcNow,
        PresentationKindRevision = Guid.NewGuid()
    };

    private static async Task<string> ReadIdentityIndexAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT indexdef
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND indexname = 'IX_TrackSourceIds_SourceType_ExternalId';
            """,
            connection);
        return Assert.IsType<string>(await command.ExecuteScalarAsync());
    }
}
