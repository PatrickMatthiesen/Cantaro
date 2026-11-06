using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class SeparateProviderCatalogAndObservationOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TrackObservations_SourceType_ExternalId",
                table: "TrackObservations");

            migrationBuilder.AddColumn<string>(
                name: "AssociationMethod",
                table: "TrackSourceIds",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AssociationState",
                table: "TrackSourceIds",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AssociationVerifiedAt",
                table: "TrackSourceIds",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CatalogRevision",
                table: "TrackSourceIds",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IndependentEvidenceJson",
                table: "TrackSourceIds",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProviderCatalogItemId",
                table: "TrackSourceIds",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Revision",
                table: "TrackSourceIds",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "PreferredCatalogItemId",
                table: "Tracks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OwnerUserId",
                table: "TrackObservations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProviderCatalogItemId",
                table: "TrackObservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProviderCatalogItemId",
                table: "TrackArtistCredits",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RevalidateAfter",
                table: "ArtistProviderLinks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationMethod",
                table: "ArtistProviderLinks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VerifiedAt",
                table: "ArtistProviderLinks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProviderCatalogItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceType = table.Column<string>(type: "text", nullable: false),
                    ExternalId = table.Column<string>(type: "text", nullable: false),
                    FactsJson = table.Column<string>(type: "text", nullable: false),
                    NormalizedTitle = table.Column<string>(type: "text", nullable: true),
                    NormalizedArtist = table.Column<string>(type: "text", nullable: true),
                    NormalizedIsrc = table.Column<string>(type: "text", nullable: true),
                    NormalizedMusicBrainzRecordingId = table.Column<string>(type: "text", nullable: true),
                    VerificationMethod = table.Column<string>(type: "text", nullable: true),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevalidateAfter = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VisibilityState = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderCatalogItems", x => x.Id);
                });

            // One stopped-writer cutover. Retain owned source references and
            // membership, discard uncertain snapshots, and leave no repair state.
            migrationBuilder.Sql("""
                CREATE FUNCTION pg_temp.cantaro_catalog_json(value text) RETURNS jsonb
                LANGUAGE plpgsql AS $function$
                BEGIN
                    RETURN value::jsonb;
                EXCEPTION WHEN invalid_text_representation THEN
                    RETURN NULL;
                END;
                $function$;

                CREATE TEMP TABLE observation_scopes ON COMMIT DROP AS
                WITH owner_references AS (
                    SELECT o."Id" AS old_id, p."UserId" AS owner_id
                    FROM "TrackObservations" o
                    JOIN "PlaylistEntries" e ON e."TrackObservationId" = o."Id"
                    JOIN "Playlists" p ON p."Id" = e."PlaylistId"
                    UNION
                    SELECT o."Id", m."UserId"
                    FROM "ServicePlaylistMappings" m
                    JOIN "Playlists" p ON p."Id" = m."PlaylistId" AND p."UserId" = m."UserId"
                    CROSS JOIN LATERAL (VALUES
                        (pg_temp.cantaro_catalog_json(m."BaselineJson")),
                        (pg_temp.cantaro_catalog_json(m."PendingWriteJson"))) document(value)
                    CROSS JOIN LATERAL jsonb_array_elements(CASE
                        WHEN jsonb_typeof(document.value->'Items') = 'array' THEN document.value->'Items'
                        ELSE '[]'::jsonb END) item(value)
                    JOIN "TrackObservations" o ON item.value->>'ObservationId' = o."Id"::text
                )
                SELECT old_id, owner_id,
                    CASE WHEN owner_id = min(owner_id) OVER (PARTITION BY old_id)
                        THEN old_id ELSE md5(old_id::text || ':' || owner_id::text)::uuid END AS scoped_id
                FROM owner_references;

                UPDATE "TrackObservations" SET "Title" = '', "Artist" = NULL,
                    "ThumbnailUrl" = NULL, "DurationSeconds" = NULL, "RawMetadata" = NULL,
                    "NormalizedTitle" = NULL, "NormalizedArtist" = NULL,
                    "ResolutionNotes" = NULL, "AcceptedCandidateId" = NULL,
                    "MatchAttemptCount" = 0, "LastMatchAttemptedAt" = NULL, "LastMatchError" = NULL,
                    "MatchStatus" = CASE WHEN "TrackId" IS NULL THEN 'pending' ELSE 'matched' END;
                DELETE FROM "TrackResolutionCandidates";
                DELETE FROM "TrackMatchQueueItems";

                INSERT INTO "TrackObservations"
                SELECT (jsonb_populate_record(NULL::"TrackObservations", to_jsonb(o) ||
                    jsonb_build_object('Id', s.scoped_id, 'OwnerUserId', s.owner_id))).*
                FROM observation_scopes s JOIN "TrackObservations" o ON o."Id" = s.old_id
                WHERE s.scoped_id <> s.old_id;
                UPDATE "TrackObservations" o SET "OwnerUserId" = s.owner_id
                FROM observation_scopes s WHERE o."Id" = s.old_id AND s.scoped_id = s.old_id;
                -- Rewrite only observation reference fields. Recover a unique
                -- older scoped acknowledgment before discarding matching proofs.
                CREATE FUNCTION pg_temp.cantaro_catalog_receipt(document text, mapping jsonb) RETURNS text
                LANGUAGE plpgsql AS $function$
                DECLARE payload jsonb; items jsonb = '[]'::jsonb; item jsonb;
                    progress jsonb; scoped_id uuid; proof_count bigint; entry_id text;
                BEGIN
                    payload = pg_temp.cantaro_catalog_json(document);
                    IF jsonb_typeof(payload->'Items') IS DISTINCT FROM 'array' THEN RETURN document; END IF;
                    progress = pg_temp.cantaro_catalog_json(mapping->>'MatchingProgressJson');
                    FOR item IN SELECT value FROM jsonb_array_elements(payload->'Items') LOOP
                        IF COALESCE(item->'DestinationBinding', 'null'::jsonb) = 'null'::jsonb
                            AND item->>'TrackId' IS NOT NULL AND item->>'ExternalId' IS NOT NULL THEN
                            SELECT count(*), min(proof.key) INTO proof_count, entry_id
                            FROM jsonb_each(CASE WHEN jsonb_typeof(progress) = 'object' THEN progress ELSE '{}'::jsonb END) proof
                            WHERE proof.value->>'ProofVersion' IN ('2', '3')
                                AND proof.value->>'ExternalId' = item->>'ExternalId'
                                AND proof.key ~ '^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$'
                                AND proof.value->'Scope'->>'EntryId' = proof.key
                                AND proof.value->'Scope'->>'UserId' = mapping->>'UserId'
                                AND proof.value->'Scope'->>'PlaylistId' = mapping->>'PlaylistId'
                                AND proof.value->'Scope'->>'MappingId' = mapping->>'Id'
                                AND proof.value->'Scope'->>'ConnectedServiceAccountId' = mapping->>'ConnectedServiceAccountId'
                                AND proof.value->'Scope'->>'ExternalAccountId' = mapping->>'ExternalAccountId'
                                AND proof.value->'Scope'->>'Service' = mapping->>'Service'
                                AND proof.value->'Scope'->>'ServicePlaylistId' = mapping->>'ServicePlaylistId'
                                AND NOT EXISTS (SELECT 1 FROM "PlaylistEntries" e WHERE e."Id"::text = proof.key
                                    AND (e."TrackId"::text IS DISTINCT FROM item->>'TrackId'
                                        OR e."TrackObservationId"::text IS DISTINCT FROM item->>'ObservationId'));
                            IF proof_count = 1 THEN
                                item = jsonb_set(item, '{DestinationBinding}', (progress->entry_id->'Scope') ||
                                    jsonb_build_object('ObservationTrackId', CASE WHEN item->>'ObservationId' IS NULL
                                        THEN NULL ELSE item->>'TrackId' END));
                            END IF;
                        END IF;
                        SELECT s.scoped_id INTO scoped_id FROM observation_scopes s
                        WHERE s.old_id::text = item->>'ObservationId' AND s.owner_id::text = mapping->>'UserId';
                        IF scoped_id IS NOT NULL THEN item = jsonb_set(item, '{ObservationId}', to_jsonb(scoped_id)); END IF;
                        items = items || jsonb_build_array(item);
                    END LOOP;
                    RETURN jsonb_set(payload, '{Items}', items)::text;
                END;
                $function$;

                UPDATE "ServicePlaylistMappings" m SET
                    "BaselineJson" = pg_temp.cantaro_catalog_receipt(m."BaselineJson", to_jsonb(m)),
                    "PendingWriteJson" = pg_temp.cantaro_catalog_receipt(m."PendingWriteJson", to_jsonb(m)),
                    "MatchingProgressJson" = NULL;
                UPDATE "PlaylistEntries" e SET "TrackObservationId" = s.scoped_id
                FROM observation_scopes s, "Playlists" p
                WHERE e."PlaylistId" = p."Id" AND e."TrackObservationId" = s.old_id
                    AND p."UserId" = s.owner_id;
                DELETE FROM "TrackObservations" WHERE "OwnerUserId" IS NULL;
                -- Old global mappings have no acquisition or association proof.
                -- Their unique keys must not block independently acquired records.
                DELETE FROM "TrackSourceIds";
                UPDATE "Tracks" SET "CanonicalMetadata" = NULL, "SearchTitle" = NULL, "SearchArtist" = NULL;
                DROP FUNCTION pg_temp.cantaro_catalog_receipt(text, jsonb);
                DROP FUNCTION pg_temp.cantaro_catalog_json(text);
                """);

            migrationBuilder.AlterColumn<int>(name: "OwnerUserId", table: "TrackObservations", type: "integer",
                nullable: false, oldClrType: typeof(int), oldType: "integer", oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackSourceIds_ProviderCatalogItemId",
                table: "TrackSourceIds",
                column: "ProviderCatalogItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Tracks_PreferredCatalogItemId",
                table: "Tracks",
                column: "PreferredCatalogItemId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackObservations_OwnerUserId_SourceType_ExternalId",
                table: "TrackObservations",
                columns: new[] { "OwnerUserId", "SourceType", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackObservations_ProviderCatalogItemId",
                table: "TrackObservations",
                column: "ProviderCatalogItemId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackArtistCredits_ProviderCatalogItemId",
                table: "TrackArtistCredits",
                column: "ProviderCatalogItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderCatalogItems_SourceType_ExternalId",
                table: "ProviderCatalogItems",
                columns: new[] { "SourceType", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProviderCatalogItems_NormalizedTitle_NormalizedArtist",
                table: "ProviderCatalogItems",
                columns: new[] { "NormalizedTitle", "NormalizedArtist" });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderCatalogItems_NormalizedIsrc",
                table: "ProviderCatalogItems",
                column: "NormalizedIsrc");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderCatalogItems_NormalizedMusicBrainzRecordingId",
                table: "ProviderCatalogItems",
                column: "NormalizedMusicBrainzRecordingId");

            migrationBuilder.AddForeignKey(
                name: "FK_TrackArtistCredits_ProviderCatalogItems_ProviderCatalogItem~",
                table: "TrackArtistCredits",
                column: "ProviderCatalogItemId",
                principalTable: "ProviderCatalogItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TrackObservations_AspNetUsers_OwnerUserId",
                table: "TrackObservations",
                column: "OwnerUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TrackObservations_ProviderCatalogItems_ProviderCatalogItemId",
                table: "TrackObservations",
                column: "ProviderCatalogItemId",
                principalTable: "ProviderCatalogItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Tracks_ProviderCatalogItems_PreferredCatalogItemId",
                table: "Tracks",
                column: "PreferredCatalogItemId",
                principalTable: "ProviderCatalogItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TrackSourceIds_ProviderCatalogItems_ProviderCatalogItemId",
                table: "TrackSourceIds",
                column: "ProviderCatalogItemId",
                principalTable: "ProviderCatalogItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A downgrade must never combine account snapshots. Restore a backup
            // after scoped writes; an operator cannot resolve this by discarding rows.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "TrackObservations"
                        GROUP BY "SourceType", "ExternalId" HAVING count(*) > 1) THEN
                        RAISE EXCEPTION 'Unsafe downgrade: scoped observations cannot be merged. Restore the pre-migration backup.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_TrackArtistCredits_ProviderCatalogItems_ProviderCatalogItem~",
                table: "TrackArtistCredits");

            migrationBuilder.DropForeignKey(
                name: "FK_TrackObservations_AspNetUsers_OwnerUserId",
                table: "TrackObservations");

            migrationBuilder.DropForeignKey(
                name: "FK_TrackObservations_ProviderCatalogItems_ProviderCatalogItemId",
                table: "TrackObservations");

            migrationBuilder.DropForeignKey(
                name: "FK_Tracks_ProviderCatalogItems_PreferredCatalogItemId",
                table: "Tracks");

            migrationBuilder.DropForeignKey(
                name: "FK_TrackSourceIds_ProviderCatalogItems_ProviderCatalogItemId",
                table: "TrackSourceIds");

            migrationBuilder.DropTable(
                name: "ProviderCatalogItems");

            migrationBuilder.DropIndex(
                name: "IX_TrackSourceIds_ProviderCatalogItemId",
                table: "TrackSourceIds");

            migrationBuilder.DropIndex(
                name: "IX_Tracks_PreferredCatalogItemId",
                table: "Tracks");

            migrationBuilder.DropIndex(
                name: "IX_TrackObservations_OwnerUserId_SourceType_ExternalId",
                table: "TrackObservations");

            migrationBuilder.DropIndex(
                name: "IX_TrackObservations_ProviderCatalogItemId",
                table: "TrackObservations");

            migrationBuilder.DropIndex(
                name: "IX_TrackArtistCredits_ProviderCatalogItemId",
                table: "TrackArtistCredits");

            migrationBuilder.DropColumn(
                name: "AssociationMethod",
                table: "TrackSourceIds");

            migrationBuilder.DropColumn(
                name: "AssociationState",
                table: "TrackSourceIds");

            migrationBuilder.DropColumn(
                name: "AssociationVerifiedAt",
                table: "TrackSourceIds");

            migrationBuilder.DropColumn(
                name: "CatalogRevision",
                table: "TrackSourceIds");

            migrationBuilder.DropColumn(
                name: "IndependentEvidenceJson",
                table: "TrackSourceIds");

            migrationBuilder.DropColumn(
                name: "ProviderCatalogItemId",
                table: "TrackSourceIds");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "TrackSourceIds");

            migrationBuilder.DropColumn(
                name: "PreferredCatalogItemId",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "TrackObservations");

            migrationBuilder.DropColumn(
                name: "ProviderCatalogItemId",
                table: "TrackObservations");

            migrationBuilder.DropColumn(
                name: "ProviderCatalogItemId",
                table: "TrackArtistCredits");

            migrationBuilder.DropColumn(
                name: "RevalidateAfter",
                table: "ArtistProviderLinks");

            migrationBuilder.DropColumn(
                name: "VerificationMethod",
                table: "ArtistProviderLinks");

            migrationBuilder.DropColumn(
                name: "VerifiedAt",
                table: "ArtistProviderLinks");

            migrationBuilder.CreateIndex(
                name: "IX_TrackObservations_SourceType_ExternalId",
                table: "TrackObservations",
                columns: new[] { "SourceType", "ExternalId" },
                unique: true);
        }
    }
}
