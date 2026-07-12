using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Cantaro.Api.Data;
using Cantaro.Api.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class SongMigrationTests
{
    private const string PreviousMigration = "20260711185411_AddArtistCredits";

    [Fact]
    public void Backfill_IsSetBasedDeterministicNullOnlyAndIdempotent()
    {
        var sql = GetBackfillSql();

        Assert.Contains("INSERT INTO \"Songs\"", sql, StringComparison.Ordinal);
        Assert.Contains("cantaro:legacy-song:", sql, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT (\"Id\") DO NOTHING", sql, StringComparison.Ordinal);
        Assert.Equal(2, Count(sql, "WHERE track.\"SongId\" IS NULL"));
        Assert.DoesNotContain("CanonicalMetadata", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("PARTITION BY", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostgreSqlUpgrade_BackfillsWithoutGroupingAndCanBeRepeated()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable("CANTARO_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            return;
        }

        var databaseName = $"cantaro_song_migration_{Guid.NewGuid():N}";
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

            var firstTrackId = Guid.Parse("00000000-0000-0000-0000-000000000001");
            var secondTrackId = Guid.Parse("00000000-0000-0000-0000-000000000002");
            var firstCreated = new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero);
            var secondCreated = new DateTimeOffset(2025, 2, 3, 4, 5, 6, TimeSpan.Zero);
            await InsertLegacyTrackAsync(db, firstTrackId, firstCreated);
            await InsertLegacyTrackAsync(db, secondTrackId, secondCreated);

            await migrator.MigrateAsync();
            db.ChangeTracker.Clear();

            var tracks = await db.Tracks.OrderBy(x => x.Id).ToListAsync();
            var songs = await db.Songs.OrderBy(x => x.CreatedAt).ToListAsync();
            Assert.Equal(2, songs.Count);
            Assert.Equal(2, tracks.Select(x => x.SongId).Distinct().Count());
            Assert.Equal(DeterministicSongId(firstTrackId), tracks[0].SongId);
            Assert.Equal(DeterministicSongId(secondTrackId), tracks[1].SongId);
            Assert.Equal(firstCreated, songs.Single(x => x.Id == tracks[0].SongId).CreatedAt);
            Assert.Equal(secondCreated, songs.Single(x => x.Id == tracks[1].SongId).CreatedAt);

            var secondOriginalSongId = tracks[1].SongId!.Value;
            tracks[1].SongId = tracks[0].SongId;
            await db.SaveChangesAsync();
            Assert.Equal(2, await db.Tracks.CountAsync(x => x.SongId == tracks[0].SongId));

            tracks[1].SongId = null;
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync(GetBackfillSql());
            await db.Database.ExecuteSqlRawAsync(GetBackfillSql());
            db.ChangeTracker.Clear();

            Assert.Equal(secondOriginalSongId, (await db.Tracks.SingleAsync(x => x.Id == secondTrackId)).SongId);
            Assert.Equal(2, await db.Songs.CountAsync());

            db.Songs.Remove(await db.Songs.SingleAsync(x => x.Id == tracks[0].SongId));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();

            await migrator.MigrateAsync(PreviousMigration);
            await using var shape = new NpgsqlCommand(
                """
                SELECT
                    to_regclass('public."Songs"') IS NULL,
                    NOT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'Tracks'
                          AND column_name = 'SongId');
                """,
                new NpgsqlConnection(databaseBuilder.ConnectionString));
            await shape.Connection!.OpenAsync();
            await using var reader = await shape.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.True(reader.GetBoolean(0));
            Assert.True(reader.GetBoolean(1));
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task InsertLegacyTrackAsync(
        ApplicationDbContext db,
        Guid trackId,
        DateTimeOffset timestamp)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Tracks"
                ("Id", "CanonicalMetadata", "MbidRecording", "Isrc", "CreatedAt", "UpdatedAt")
            VALUES ({trackId}, NULL, NULL, NULL, {timestamp}, {timestamp});
            """);
    }

    private static string GetBackfillSql()
    {
        var migration = new AddSongIdentityFoundation();
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(AddSongIdentityFoundation)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);
        return Assert.Single(builder.Operations.OfType<SqlOperation>()).Sql;
    }

    private static Guid DeterministicSongId(Guid trackId)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"cantaro:legacy-song:{trackId}"));
        return Guid.Parse(Convert.ToHexString(bytes));
    }

    private static int Count(string value, string substring)
    {
        var count = 0;
        for (var index = 0; (index = value.IndexOf(substring, index, StringComparison.Ordinal)) >= 0;)
        {
            count++;
            index += substring.Length;
        }

        return count;
    }
}
