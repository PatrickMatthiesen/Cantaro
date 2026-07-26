using BenchmarkDotNet.Attributes;
using Npgsql;

namespace Cantaro.Benchmarks;

[MemoryDiagnoser]
public class MusicRelationBenchmarks
{
    private const string ConnectionStringEnvironmentVariable =
        "CANTARO_BENCHMARK_POSTGRES";
    private const string RequiredDatabaseName = "cantaro_benchmark";
    private const int TrackCount = 100_000;

    private string connectionString = null!;
    private string unpooledConnectionString = null!;
    private NpgsqlDataSource dataSource = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        var configuredConnectionString =
            Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable)
            ?? throw new InvalidOperationException(
                $"{ConnectionStringEnvironmentVariable} must point to the disposable " +
                $"{RequiredDatabaseName} database.");

        var builder = new NpgsqlConnectionStringBuilder(configuredConnectionString)
        {
            GssEncryptionMode = GssEncryptionMode.Disable
        };

        if (!string.Equals(
                builder.Database,
                RequiredDatabaseName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Refusing destructive benchmark setup for database " +
                $"'{builder.Database}'. Expected '{RequiredDatabaseName}'.");
        }

        connectionString = builder.ConnectionString;
        builder.Pooling = false;
        unpooledConnectionString = builder.ConnectionString;
        dataSource = NpgsqlDataSource.Create(connectionString);

        await using var connection = await dataSource.OpenConnectionAsync();
        await using var initializedCommand = connection.CreateCommand();
        initializedCommand.CommandText =
            """
            SELECT to_regclass('"BenchmarkMetadata"') IS NOT NULL
            """;

        if (await initializedCommand.ExecuteScalarAsync() is true)
        {
            initializedCommand.CommandText =
                """
                SELECT EXISTS (
                    SELECT 1
                    FROM "BenchmarkMetadata"
                    WHERE "Key" = 'SchemaVersion' AND "Value" = '1'
                )
                """;

            if (await initializedCommand.ExecuteScalarAsync() is true)
            {
                return;
            }
        }

        await using var command = connection.CreateCommand();
        command.CommandText = CreateBenchmarkSchema;
        command.Parameters.AddWithValue("trackCount", TrackCount);
        await command.ExecuteNonQueryAsync();
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await dataSource.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
    }

    [Benchmark]
    public async Task OpenFreshPhysicalConnection()
    {
        await using var connection = new NpgsqlConnection(unpooledConnectionString);
        await connection.OpenAsync();
    }

    [Benchmark]
    public async Task<Guid> ProviderIdentityLookup()
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT "TrackId"
            FROM "TrackSourceIds"
            WHERE "SourceType" = 'youtube' AND "ExternalId" = 'video-50000'
            """);

        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    [Benchmark]
    public Task<int> SongToTracks() =>
        CountRowsAsync(
            """
            SELECT "TrackId"
            FROM "SongTracks"
            WHERE "SongId" = md5('song:5000')::uuid
            """);

    [Benchmark]
    public Task<int> TrackToSongs() =>
        CountRowsAsync(
            """
            SELECT "SongId"
            FROM "SongTracks"
            WHERE "TrackId" = md5('track:1')::uuid
            """);

    [Benchmark]
    public Task<int> FirstPlaylistPage() =>
        CountRowsAsync(
            """
            SELECT entry."Position", track."Id", credit."CreditedName", source."ExternalId"
            FROM "PlaylistEntries" AS entry
            JOIN "Tracks" AS track ON track."Id" = entry."TrackId"
            LEFT JOIN "TrackArtistCredits" AS credit ON credit."TrackId" = track."Id"
            LEFT JOIN "TrackSourceIds" AS source ON source."TrackId" = track."Id"
            WHERE entry."PlaylistId" = md5('playlist:1')::uuid
            ORDER BY entry."Position"
            LIMIT 50
            """);

    private async Task<int> CountRowsAsync(string sql)
    {
        await using var command = dataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync();
        var count = 0;

        while (await reader.ReadAsync())
        {
            count++;
        }

        return count;
    }

    private const string CreateBenchmarkSchema =
        """
        DROP TABLE IF EXISTS "BenchmarkMetadata", "PlaylistEntries",
            "TrackArtistCredits", "TrackSourceIds", "SongTracks", "Tracks", "Songs";

        CREATE TABLE "BenchmarkMetadata" (
            "Key" text PRIMARY KEY,
            "Value" text NOT NULL
        );
        INSERT INTO "BenchmarkMetadata" ("Key", "Value")
        VALUES ('SchemaVersion', '1');

        CREATE TABLE "Songs" ("Id" uuid PRIMARY KEY);
        CREATE TABLE "Tracks" (
            "Id" uuid PRIMARY KEY,
            "VersionFlags" bigint NOT NULL DEFAULT 0
        );
        CREATE TABLE "SongTracks" (
            "SongId" uuid NOT NULL,
            "TrackId" uuid NOT NULL,
            PRIMARY KEY ("SongId", "TrackId")
        );
        CREATE INDEX "IX_SongTracks_TrackId_SongId"
            ON "SongTracks" ("TrackId", "SongId");

        CREATE TABLE "TrackSourceIds" (
            "Id" uuid PRIMARY KEY,
            "TrackId" uuid NOT NULL,
            "SourceType" text NOT NULL,
            "ExternalId" text NOT NULL
        );
        CREATE UNIQUE INDEX "IX_TrackSourceIds_SourceType_ExternalId"
            ON "TrackSourceIds" ("SourceType", "ExternalId");
        CREATE INDEX "IX_TrackSourceIds_TrackId"
            ON "TrackSourceIds" ("TrackId");

        CREATE TABLE "TrackArtistCredits" (
            "Id" uuid PRIMARY KEY,
            "TrackId" uuid NOT NULL,
            "Position" integer NOT NULL,
            "CreditedName" text NOT NULL
        );
        CREATE UNIQUE INDEX "IX_TrackArtistCredits_TrackId_Position"
            ON "TrackArtistCredits" ("TrackId", "Position");

        CREATE TABLE "PlaylistEntries" (
            "Id" uuid PRIMARY KEY,
            "PlaylistId" uuid NOT NULL,
            "TrackId" uuid NOT NULL,
            "Position" integer NOT NULL
        );
        CREATE UNIQUE INDEX "IX_PlaylistEntries_PlaylistId_Position"
            ON "PlaylistEntries" ("PlaylistId", "Position");

        INSERT INTO "Songs" ("Id")
        SELECT md5('song:' || value)::uuid
        FROM generate_series(1, @trackCount / 10) AS value;

        INSERT INTO "Tracks" ("Id", "VersionFlags")
        SELECT
            md5('track:' || value)::uuid,
            CASE WHEN value % 10 = 0 THEN 3 ELSE 0 END
        FROM generate_series(1, @trackCount) AS value;

        INSERT INTO "SongTracks" ("SongId", "TrackId")
        SELECT
            md5('song:' || (((value - 1) / 10) + 1))::uuid,
            md5('track:' || value)::uuid
        FROM generate_series(1, @trackCount) AS value;

        INSERT INTO "SongTracks" ("SongId", "TrackId")
        VALUES (md5('song:2')::uuid, md5('track:1')::uuid);

        INSERT INTO "TrackSourceIds" ("Id", "TrackId", "SourceType", "ExternalId")
        SELECT
            md5('source:' || value)::uuid,
            md5('track:' || value)::uuid,
            'youtube',
            'video-' || value
        FROM generate_series(1, @trackCount) AS value;

        INSERT INTO "TrackArtistCredits" ("Id", "TrackId", "Position", "CreditedName")
        SELECT
            md5('credit:' || value)::uuid,
            md5('track:' || value)::uuid,
            0,
            'Artist ' || value
        FROM generate_series(1, @trackCount) AS value;

        INSERT INTO "PlaylistEntries" ("Id", "PlaylistId", "TrackId", "Position")
        SELECT
            md5('entry:' || value)::uuid,
            md5('playlist:1')::uuid,
            md5('track:' || value)::uuid,
            value - 1
        FROM generate_series(1, LEAST(@trackCount, 10000)) AS value;

        ANALYZE "Songs";
        ANALYZE "Tracks";
        ANALYZE "SongTracks";
        ANALYZE "TrackSourceIds";
        ANALYZE "TrackArtistCredits";
        ANALYZE "PlaylistEntries";
        """;
}
