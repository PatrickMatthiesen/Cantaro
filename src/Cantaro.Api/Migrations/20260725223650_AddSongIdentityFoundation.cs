using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSongIdentityFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Cantaro is still in beta, so replace the old canonical music
            // graph instead of guessing at composition membership. Imported
            // observations and playlist positions remain available to rebuild
            // exact Tracks through the matcher.
            migrationBuilder.Sql(
                """
                UPDATE "PlaylistEntries"
                SET "TrackId" = NULL;

                UPDATE "TrackObservations"
                SET
                    "TrackId" = NULL,
                    "AcceptedCandidateId" = NULL,
                    "MatchStatus" = 'pending',
                    "LastMatchError" = NULL,
                    "ResolutionNotes" = 'Canonical music model reset; matching required.',
                    "UpdatedAt" = CURRENT_TIMESTAMP;

                DELETE FROM "TrackResolutionCandidates";
                DELETE FROM "TrackSourceIds";
                DELETE FROM "TrackArtistCredits";
                DELETE FROM "Tracks";
                DELETE FROM "Artists";

                -- Remove schema left by the abandoned, unmerged #114-#118
                -- migration stack. Some beta databases ran those migrations
                -- locally before the PRs were closed.
                DROP TABLE IF EXISTS "SongGroupingSuggestionReviews" CASCADE;
                DROP TABLE IF EXISTS "SongGroupingEvidence" CASCADE;
                DROP TABLE IF EXISTS "SongGroupingSuggestions" CASCADE;
                DROP TABLE IF EXISTS "TrackVersionTraits" CASCADE;

                ALTER TABLE "Tracks" DROP COLUMN IF EXISTS "SongId" CASCADE;
                DROP TABLE IF EXISTS "Songs" CASCADE;

                ALTER TABLE "TrackSourceIds"
                    DROP COLUMN IF EXISTS "PresentationKind",
                    DROP COLUMN IF EXISTS "PresentationKindClassifiedAt",
                    DROP COLUMN IF EXISTS "PresentationKindConfidence",
                    DROP COLUMN IF EXISTS "PresentationKindEvidenceIdentity",
                    DROP COLUMN IF EXISTS "PresentationKindEvidenceMethod",
                    DROP COLUMN IF EXISTS "PresentationKindEvidenceSource",
                    DROP COLUMN IF EXISTS "PresentationKindMethodVersion",
                    DROP COLUMN IF EXISTS "PresentationKindRevision",
                    DROP COLUMN IF EXISTS "UploaderAuthority",
                    DROP COLUMN IF EXISTS "UploaderAuthorityClassifiedAt",
                    DROP COLUMN IF EXISTS "UploaderAuthorityConfidence",
                    DROP COLUMN IF EXISTS "UploaderAuthorityEvidenceIdentity",
                    DROP COLUMN IF EXISTS "UploaderAuthorityEvidenceMethod",
                    DROP COLUMN IF EXISTS "UploaderAuthorityEvidenceSource",
                    DROP COLUMN IF EXISTS "UploaderAuthorityMethodVersion",
                    DROP COLUMN IF EXISTS "UploaderAuthorityRevision";

                DELETE FROM "__EFMigrationsHistory"
                WHERE "MigrationId" IN (
                    '20260712200312_AddSongIdentityFoundation',
                    '20260712202653_AddTrackVersionTraitEvidence',
                    '20260712210804_AddTrackSourcePresentationMetadata',
                    '20260712214152_AddSongGroupingReview'
                );
                """);

            migrationBuilder.AddColumn<bool>(
                name: "IsOfficial",
                table: "TrackSourceIds",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "PresentationConfidence",
                table: "TrackSourceIds",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "PresentationKind",
                table: "TrackSourceIds",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<string>(
                name: "VersionEvidence",
                table: "Tracks",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "VersionFlags",
                table: "Tracks",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.Sql(
                """
                ALTER TABLE "TrackArtistCredits"
                ALTER COLUMN "Role" TYPE smallint
                USING 0::smallint;
                """);

            migrationBuilder.CreateTable(
                name: "Songs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Songs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrackRelations",
                columns: table => new
                {
                    FromTrackId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToTrackId = table.Column<Guid>(type: "uuid", nullable: false),
                    RelationType = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackRelations", x => new { x.FromTrackId, x.ToTrackId, x.RelationType });
                    table.CheckConstraint("CK_TrackRelations_NoSelfRelation", "\"FromTrackId\" <> \"ToTrackId\"");
                    table.ForeignKey(
                        name: "FK_TrackRelations_Tracks_FromTrackId",
                        column: x => x.FromTrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrackRelations_Tracks_ToTrackId",
                        column: x => x.ToTrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SongCredits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SongId = table.Column<Guid>(type: "uuid", nullable: false),
                    ArtistId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<short>(type: "smallint", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    CreditedName = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SongCredits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SongCredits_Artists_ArtistId",
                        column: x => x.ArtistId,
                        principalTable: "Artists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SongCredits_Songs_SongId",
                        column: x => x.SongId,
                        principalTable: "Songs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SongTracks",
                columns: table => new
                {
                    SongId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrackId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SongTracks", x => new { x.SongId, x.TrackId });
                    table.ForeignKey(
                        name: "FK_SongTracks_Songs_SongId",
                        column: x => x.SongId,
                        principalTable: "Songs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SongTracks_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_TrackSourceIds_PresentationConfidence",
                table: "TrackSourceIds",
                sql: "\"PresentationConfidence\" IS NULL OR (\"PresentationConfidence\" >= 0 AND \"PresentationConfidence\" <= 100)");

            migrationBuilder.CreateIndex(
                name: "IX_SongCredits_ArtistId",
                table: "SongCredits",
                column: "ArtistId");

            migrationBuilder.CreateIndex(
                name: "IX_SongCredits_SongId_ArtistId_Role",
                table: "SongCredits",
                columns: new[] { "SongId", "ArtistId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SongCredits_SongId_Position",
                table: "SongCredits",
                columns: new[] { "SongId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SongTracks_TrackId_SongId",
                table: "SongTracks",
                columns: new[] { "TrackId", "SongId" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackRelations_ToTrackId_RelationType_FromTrackId",
                table: "TrackRelations",
                columns: new[] { "ToTrackId", "RelationType", "FromTrackId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SongCredits");

            migrationBuilder.DropTable(
                name: "SongTracks");

            migrationBuilder.DropTable(
                name: "TrackRelations");

            migrationBuilder.DropTable(
                name: "Songs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TrackSourceIds_PresentationConfidence",
                table: "TrackSourceIds");

            migrationBuilder.DropColumn(
                name: "IsOfficial",
                table: "TrackSourceIds");

            migrationBuilder.DropColumn(
                name: "PresentationConfidence",
                table: "TrackSourceIds");

            migrationBuilder.DropColumn(
                name: "PresentationKind",
                table: "TrackSourceIds");

            migrationBuilder.DropColumn(
                name: "VersionEvidence",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "VersionFlags",
                table: "Tracks");

            migrationBuilder.Sql(
                """
                ALTER TABLE "TrackArtistCredits"
                ALTER COLUMN "Role" TYPE character varying(32)
                USING CASE "Role"
                    WHEN 0 THEN 'Primary'
                    WHEN 1 THEN 'Featured'
                    WHEN 2 THEN 'Remixer'
                    WHEN 3 THEN 'Producer'
                    ELSE 'Primary'
                END;
                """);
        }
    }
}
