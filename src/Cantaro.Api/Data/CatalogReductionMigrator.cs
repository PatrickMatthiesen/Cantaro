using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Cantaro.Api.Data;

/// <summary>One-time schema and saved-choice conversion, used only by the migration process.</summary>
public static class CatalogReductionMigrator
{
    public const string PreviousMigration = "20261105215254_SeparateProviderCatalogAndObservationOwnership";
    public const string ReductionMigration = "20261113223104_ReduceProviderCatalogStorage";

    public static async Task ApplyAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToArray();
        if (!pending.Contains(ReductionMigration, StringComparer.Ordinal))
        {
            await db.Database.MigrateAsync(ct);
            return;
        }

        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration, ct);
        // EF owns its normal migration transactions. For this one conversion,
        // execute its generated SQL and the C# fingerprint conversion together.
        // Do not nest MigrateAsync inside an application transaction.
        var sql = migrator.GenerateScript(PreviousMigration, ReductionMigration,
            MigrationsSqlGenerationOptions.NoTransactions);
        await using var connection = new NpgsqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await ExecuteAsync(connection, transaction, "LOCK TABLE \"__EFMigrationsHistory\" IN ACCESS EXCLUSIVE MODE", ct);
        await using (var applied = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = @id)", connection, transaction))
        {
            applied.Parameters.AddWithValue("id", ReductionMigration);
            if ((bool)(await applied.ExecuteScalarAsync(ct))!)
            {
                await transaction.CommitAsync(ct);
                await db.Database.MigrateAsync(ct);
                return;
            }
        }
        await ExecuteAsync(connection, transaction, """
            LOCK TABLE "Playlists", "PlaylistEntries", "ServicePlaylistMappings",
                "ConnectedServiceAccounts", "TrackObservations", "TrackResolutionCandidates", "Tracks",
                "TrackSourceIds", "ProviderCatalogItems", "Artists", "ArtistProviderLinks",
                "TrackArtistCredits", "ArtistReconciliationConflicts", "PlaylistExportAttempts"
            IN ACCESS EXCLUSIVE MODE;
            CREATE TEMP TABLE cantaro_proof_conversion (ready boolean) ON COMMIT DROP;
            """, ct);
        var choices = await CaptureChoicesAsync(connection, transaction, ct);
        await ExecuteAsync(connection, transaction, sql, ct);

        // A non-retrying context participates in the already owned transaction.
        // Queries use the new model only after its schema exists.
        await using var current = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connection).Options);
        await current.Database.UseTransactionAsync(transaction, ct);
        foreach (var choice in choices)
        {
            foreach (var batch in choice.ValidEntryIds.Chunk(256))
            {
                var entries = await current.PlaylistEntries.AsNoTracking()
                    .Include(e => e.TrackObservation).ThenInclude(o => o!.ProviderCatalogItem)
                    .Include(e => e.Track).ThenInclude(t => t!.ProviderItems)
                    .Where(e => batch.Contains(e.Id) && e.PlaylistId == choice.PlaylistId
                        && e.OwnerUserId == choice.UserId).ToListAsync(ct);
                if (entries.Count != batch.Length)
                    throw new InvalidOperationException($"Catalog conversion changed membership for playlist link {choice.LinkId}.");
                foreach (var entry in entries)
                {
                    var old = choice.Results[entry.Id];
                    choice.Results[entry.Id] = old with
                    {
                        ProofVersion = PlaylistMatchingProgress.CurrentProofVersion,
                        Fingerprint = PlaylistMatchingProgress.Fingerprint(entry, !old.ManualConfirmation)
                    };
                }
            }
            await using var update = new NpgsqlCommand("""
                UPDATE "ServicePlaylistMappings" SET "MatchingProgressJson" = @json WHERE "Id" = @id
                """, connection, transaction);
            update.Parameters.AddWithValue("json", PlaylistMatchingProgress.Serialize(choice.Results)!);
            update.Parameters.AddWithValue("id", choice.LinkId);
            if (await update.ExecuteNonQueryAsync(ct) != 1)
                throw new InvalidOperationException($"Catalog conversion lost playlist link {choice.LinkId}.");
        }
        await transaction.CommitAsync(ct);
        // Future migrations, if present, retain EF's normal execution path.
        await db.Database.MigrateAsync(ct);
    }

    private sealed record Choice(Guid LinkId, Guid PlaylistId, int UserId,
        PlaylistMatchingScope Scope, Dictionary<Guid, PlaylistMatchingResult> Results,
        List<Guid> ValidEntryIds)
    {
        public bool HasManualHistory { get; set; }
    }

    private static async Task<List<Choice>> CaptureChoicesAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, CancellationToken ct)
    {
        var choices = new List<Choice>();
        await using (var command = new NpgsqlCommand("""
            SELECT "Id", "PlaylistId", "UserId", "ConnectedServiceAccountId", "ExternalAccountId",
                "Service", "ServicePlaylistId", "MatchingProgressJson"
            FROM "ServicePlaylistMappings" WHERE "MatchingProgressJson" IS NOT NULL
            """, connection, transaction))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var results = PlaylistMatchingProgress.Read(reader.GetString(7));
                if (!results.Values.Any(p => p.ProofVersion == 3 && p.Scope is not null)) continue;
                var id = reader.GetGuid(0);
                var playlist = reader.GetGuid(1);
                var user = reader.GetInt32(2);
                choices.Add(new Choice(id, playlist, user,
                    new PlaylistMatchingScope(user, playlist, id, reader.GetInt32(3), reader.GetString(4),
                        reader.GetString(5), reader.GetString(6), Guid.Empty), results, []));
            }
        }
        var now = DateTimeOffset.UtcNow;
        foreach (var choice in choices)
        {
            await using var command = new NpgsqlCommand("""
                SELECT jsonb_build_object('Entry', to_jsonb(e), 'Observation', to_jsonb(o), 'Track', to_jsonb(t),
                    'Sources', COALESCE((SELECT jsonb_agg(jsonb_build_object('Mapping', to_jsonb(s), 'Catalog', to_jsonb(c)))
                        FROM "TrackSourceIds" s LEFT JOIN "ProviderCatalogItems" c ON c."Id" = s."ProviderCatalogItemId"
                        WHERE s."TrackId" = e."TrackId"), '[]'::jsonb))::text
                FROM "PlaylistEntries" e LEFT JOIN "TrackObservations" o ON o."Id" = e."TrackObservationId"
                LEFT JOIN "Tracks" t ON t."Id" = e."TrackId" WHERE e."PlaylistId" = @playlist
                """, connection, transaction);
            command.Parameters.AddWithValue("playlist", choice.PlaylistId);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                using var document = JsonDocument.Parse(reader.GetString(0));
                var row = document.RootElement;
                var id = row.GetProperty("Entry").GetProperty("Id").GetGuid();
                if (!choice.Results.TryGetValue(id, out var proof) || proof.ProofVersion != 3
                    || proof.Scope != (choice.Scope with { EntryId = id })) continue;
                if (proof.Fingerprint == LegacyFingerprint(row, !proof.ManualConfirmation, now))
                    choice.ValidEntryIds.Add(id);
                else if (proof.ManualConfirmation)
                {
                    // Keep the user's membership decision in the current
                    // representation. This sentinel cannot equal a SHA256
                    // fingerprint and grants no permission for another export.
                    choice.Results[id] = proof with
                    {
                        ProofVersion = PlaylistMatchingProgress.CurrentProofVersion,
                        Fingerprint = "invalidated:v3:" + proof.Fingerprint
                    };
                    choice.HasManualHistory = true;
                }
            }
        }
        // Foreign scopes, malformed records and stale automatic choices remain
        // version 3. Current manual history protects membership, never export.
        return choices.Where(c => c.ValidEntryIds.Count > 0 || c.HasManualHistory).ToList();
    }

    internal static string LegacyFingerprint(JsonElement row, bool includeSources, DateTimeOffset now)
    {
        var entry = row.GetProperty("Entry");
        var observation = row.GetProperty("Observation");
        var track = row.GetProperty("Track");
        var sources = row.GetProperty("Sources").EnumerateArray()
            .OrderBy(s => s.GetProperty("Mapping").GetProperty("SourceType").GetString())
            .ThenBy(s => s.GetProperty("Mapping").GetProperty("ExternalId").GetString())
            .Select(s =>
            {
                var mapping = s.GetProperty("Mapping");
                return new Dictionary<string, object?>
                {
                    ["SourceType"] = Value(mapping, "SourceType"), ["ExternalId"] = Value(mapping, "ExternalId"),
                    ["AssociationState"] = Value(mapping, "AssociationState"), ["Revision"] = Value(mapping, "Revision"),
                    ["ProviderCatalogItemId"] = Value(mapping, "ProviderCatalogItemId"),
                    ["CatalogRevision"] = Value(mapping, "CatalogRevision"),
                    ["Current"] = LegacyVerified(mapping, s.GetProperty("Catalog"), now)
                };
            }).ToArray();
        var evidence = new Dictionary<string, object?>
        {
            ["TrackId"] = Value(entry, "TrackId"), ["TrackObservationId"] = Value(entry, "TrackObservationId"),
            ["Title"] = Value(observation, "Title"), ["Artist"] = Value(observation, "Artist"),
            ["DurationSeconds"] = Value(observation, "DurationSeconds"), ["RawMetadata"] = Value(observation, "RawMetadata"),
            ["OwnerUserId"] = Value(observation, "OwnerUserId"), ["ObservationTrackId"] = Value(observation, "TrackId"),
            ["CanonicalMetadata"] = Value(track, "CanonicalMetadata"), ["SearchTitle"] = Value(track, "SearchTitle"),
            ["SearchArtist"] = Value(track, "SearchArtist"), ["Isrc"] = Value(track, "Isrc"),
            ["MbidRecording"] = Value(track, "MbidRecording"), ["VersionFlags"] = Value(track, "VersionFlags"),
            ["PreferredCatalogItemId"] = includeSources ? Value(track, "PreferredCatalogItemId") : null,
            ["Sources"] = includeSources && track.ValueKind == JsonValueKind.Object ? sources : null
        };
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(evidence)));
    }

    private static object? Value(JsonElement element, string key) => element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null ? value : null;

    private static bool LegacyVerified(JsonElement source, JsonElement catalog, DateTimeOffset now)
    {
        if (catalog.ValueKind != JsonValueKind.Object) return false;
        var type = source.GetProperty("SourceType").GetString()!;
        static bool Same(JsonElement left, string key, JsonElement right, string other) =>
            left.TryGetProperty(key, out var a) && right.TryGetProperty(other, out var b)
            && a.ValueKind != JsonValueKind.Null && a.ToString() == b.ToString();
        return source.GetProperty("AssociationState").GetInt32() == (int)RecordingAssociationState.Verified
            && Same(source, "ProviderCatalogItemId", catalog, "Id")
            && Same(source, "SourceType", catalog, "SourceType") && Same(source, "ExternalId", catalog, "ExternalId")
            && Same(source, "CatalogRevision", catalog, "Revision")
            && catalog.GetProperty("VisibilityState").GetInt32() == (int)CatalogVisibility.Public
            && DateTimeOffset.TryParse(catalog.GetProperty("VerifiedAt").ToString(), CultureInfo.InvariantCulture, out var verified)
            && DateTimeOffset.TryParse(catalog.GetProperty("RevalidateAfter").ToString(), CultureInfo.InvariantCulture, out var expires)
            && verified <= now && expires > now
            && CatalogVerificationMethods.Matches(type, catalog.GetProperty("VerificationMethod").GetString());
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 0 };
        await command.ExecuteNonQueryAsync(ct);
    }
}
