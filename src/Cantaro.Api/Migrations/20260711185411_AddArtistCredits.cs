using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddArtistCredits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Artists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    SortName = table.Column<string>(type: "text", nullable: true),
                    MusicBrainzArtistId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Artists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrackArtistCredits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TrackId = table.Column<Guid>(type: "uuid", nullable: false),
                    ArtistId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    CreditedName = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackArtistCredits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackArtistCredits_Artists_ArtistId",
                        column: x => x.ArtistId,
                        principalTable: "Artists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrackArtistCredits_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Existing canonical artist text is intentionally migrated as one
            // track-local artist credit. Artist strings are not reliable
            // identities and may contain collaborations, aliases, or provider
            // formatting, so this migration must not merge equal names or guess
            // where a collaboration should be split. The original JSON remains
            // untouched for backwards-compatible display.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    source_track record;
                    metadata jsonb;
                    artist_name text;
                    artist_id uuid;
                BEGIN
                    FOR source_track IN
                        SELECT "Id", "CanonicalMetadata", "CreatedAt", "UpdatedAt"
                        FROM "Tracks"
                        WHERE "CanonicalMetadata" IS NOT NULL
                    LOOP
                        BEGIN
                            metadata := source_track."CanonicalMetadata"::jsonb;
                        EXCEPTION WHEN others THEN
                            CONTINUE;
                        END;

                        artist_name := NULLIF(btrim(COALESCE(metadata ->> 'Artist', metadata ->> 'artist')), '');
                        IF artist_name IS NULL THEN
                            CONTINUE;
                        END IF;

                        artist_id := md5('cantaro:legacy-artist:' || source_track."Id"::text)::uuid;

                        INSERT INTO "Artists" ("Id", "Name", "SortName", "MusicBrainzArtistId", "CreatedAt", "UpdatedAt")
                        VALUES (artist_id, artist_name, NULL, NULL, source_track."CreatedAt", source_track."UpdatedAt");

                        INSERT INTO "TrackArtistCredits" ("Id", "TrackId", "ArtistId", "Role", "Position", "CreditedName")
                        VALUES (
                            md5('cantaro:legacy-credit:' || source_track."Id"::text)::uuid,
                            source_track."Id",
                            artist_id,
                            'Primary',
                            0,
                            artist_name);
                    END LOOP;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Artists_MusicBrainzArtistId",
                table: "Artists",
                column: "MusicBrainzArtistId",
                unique: true,
                filter: "\"MusicBrainzArtistId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TrackArtistCredits_ArtistId",
                table: "TrackArtistCredits",
                column: "ArtistId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackArtistCredits_TrackId_ArtistId_Role",
                table: "TrackArtistCredits",
                columns: new[] { "TrackId", "ArtistId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackArtistCredits_TrackId_Position",
                table: "TrackArtistCredits",
                columns: new[] { "TrackId", "Position" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrackArtistCredits");

            migrationBuilder.DropTable(
                name: "Artists");
        }
    }
}
