using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackSearchProjections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SearchArtist",
                table: "Tracks",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SearchTitle",
                table: "Tracks",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            if (ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(
                    """
                    DO $$
                    DECLARE
                        track_row record;
                        metadata jsonb;
                    BEGIN
                        FOR track_row IN
                            SELECT "Id", "CanonicalMetadata"
                            FROM "Tracks"
                            WHERE "CanonicalMetadata" IS NOT NULL
                        LOOP
                            BEGIN
                                metadata := track_row."CanonicalMetadata"::jsonb;

                                UPDATE "Tracks"
                                SET
                                    "SearchTitle" = NULLIF(
                                        LEFT(BTRIM(COALESCE(metadata ->> 'Title', metadata ->> 'title')), 512),
                                        ''),
                                    "SearchArtist" = NULLIF(
                                        LEFT(BTRIM(COALESCE(metadata ->> 'Artist', metadata ->> 'artist')), 1024),
                                        '')
                                WHERE "Id" = track_row."Id";
                            EXCEPTION
                                WHEN OTHERS THEN
                                    -- Preserve malformed legacy metadata and leave its
                                    -- search projections empty rather than failing deploy.
                                    NULL;
                            END;
                        END LOOP;
                    END $$;
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SearchArtist",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "SearchTitle",
                table: "Tracks");
        }
    }
}
