using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class RetainPlaylistExportAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlaylistExportAttempts",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Service = table.Column<string>(type: "text", nullable: false),
                    ExternalAccountId = table.Column<string>(type: "text", nullable: false),
                    ServicePlaylistId = table.Column<string>(type: "text", nullable: false),
                    ExternalTrackId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaylistExportAttempts", x => new { x.UserId, x.Service, x.ExternalAccountId, x.ServicePlaylistId, x.ExternalTrackId });
                    table.ForeignKey(
                        name: "FK_PlaylistExportAttempts_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Preserve recoverable scoped baseline/pending receipts before a
            // later link deletion or baseline replacement can forget them. An
            // old pending export plan needs no recording proof for suppression.
            // Bare baselines and already-forgotten history cannot prove an export.
            // No recording identity or permission is recovered by this backfill.
            migrationBuilder.Sql("""
                CREATE FUNCTION pg_temp.cantaro_export_json(value text) RETURNS jsonb
                LANGUAGE plpgsql AS $function$
                BEGIN
                    RETURN value::jsonb;
                EXCEPTION WHEN invalid_text_representation THEN
                    RETURN NULL;
                END;
                $function$;

                WITH receipts AS (
                    SELECT m.*, item.value AS item, document.pending
                    FROM "ServicePlaylistMappings" m
                    JOIN "Playlists" p ON p."Id" = m."PlaylistId" AND p."UserId" = m."UserId"
                    CROSS JOIN LATERAL (VALUES
                        (pg_temp.cantaro_export_json(m."BaselineJson"), false),
                        (pg_temp.cantaro_export_json(m."PendingWriteJson"), true)) document(value, pending)
                    CROSS JOIN LATERAL jsonb_array_elements(CASE
                        WHEN jsonb_typeof(document.value->'Items') = 'array' THEN document.value->'Items'
                        ELSE '[]'::jsonb END) item(value)
                    WHERE m."Service" IN ('spotify', 'youtube') AND m."ExternalAccountId" <> ''
                        AND m."ServicePlaylistId" <> '' AND m."ServicePlaylistId" NOT LIKE 'pending:%'
                ), scoped AS (
                    SELECT r.* FROM receipts r
                    WHERE r.item->>'Service' = r."Service" AND COALESCE(r.item->>'ExternalId', '') <> ''
                    AND (
                        r.pending OR
                        (r.item->'DestinationBinding'->>'UserId' = r."UserId"::text
                            AND r.item->'DestinationBinding'->>'PlaylistId' = r."PlaylistId"::text
                            AND r.item->'DestinationBinding'->>'MappingId' = r."Id"::text
                            AND r.item->'DestinationBinding'->>'ConnectedServiceAccountId' = r."ConnectedServiceAccountId"::text
                            AND r.item->'DestinationBinding'->>'ExternalAccountId' = r."ExternalAccountId"
                            AND r.item->'DestinationBinding'->>'Service' = r."Service"
                            AND r.item->'DestinationBinding'->>'ServicePlaylistId' = r."ServicePlaylistId"
                            AND r.item->'DestinationBinding'->>'EntryId' ~ '^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$')
                        OR (COALESCE(r.item->'DestinationBinding', 'null'::jsonb) = 'null'::jsonb AND EXISTS (
                            SELECT 1 FROM jsonb_each(CASE
                                WHEN jsonb_typeof(pg_temp.cantaro_export_json(r."MatchingProgressJson")) = 'object'
                                THEN pg_temp.cantaro_export_json(r."MatchingProgressJson") ELSE '{}'::jsonb END) proof
                            WHERE proof.value->>'ProofVersion' = '2'
                                AND proof.value->>'ExternalId' = r.item->>'ExternalId'
                                AND proof.value->'Scope'->>'EntryId' = proof.key
                                AND proof.value->'Scope'->>'UserId' = r."UserId"::text
                                AND proof.value->'Scope'->>'PlaylistId' = r."PlaylistId"::text
                                AND proof.value->'Scope'->>'MappingId' = r."Id"::text
                                AND proof.value->'Scope'->>'ConnectedServiceAccountId' = r."ConnectedServiceAccountId"::text
                                AND proof.value->'Scope'->>'ExternalAccountId' = r."ExternalAccountId"
                                AND proof.value->'Scope'->>'Service' = r."Service"
                                AND proof.value->'Scope'->>'ServicePlaylistId' = r."ServicePlaylistId"
                        ))
                    )
                )
                INSERT INTO "PlaylistExportAttempts"
                    ("UserId", "Service", "ExternalAccountId", "ServicePlaylistId", "ExternalTrackId")
                SELECT DISTINCT "UserId", "Service", "ExternalAccountId", "ServicePlaylistId", item->>'ExternalId'
                FROM scoped
                ON CONFLICT ("UserId", "Service", "ExternalAccountId", "ServicePlaylistId", "ExternalTrackId") DO NOTHING;

                DROP FUNCTION pg_temp.cantaro_export_json(text);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlaylistExportAttempts");
        }
    }
}
