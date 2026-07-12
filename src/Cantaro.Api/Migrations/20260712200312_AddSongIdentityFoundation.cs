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
            migrationBuilder.AddColumn<Guid>(
                name: "SongId",
                table: "Tracks",
                type: "uuid",
                nullable: true);

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

            // Existing Tracks are deliberately kept separate. The namespaced,
            // deterministic Song ID makes this set-based backfill safe to run
            // again without grouping Tracks or creating duplicate Songs.
            migrationBuilder.Sql("""
                INSERT INTO "Songs" ("Id", "CreatedAt", "UpdatedAt")
                SELECT
                    md5('cantaro:legacy-song:' || track."Id"::text)::uuid,
                    track."CreatedAt",
                    track."UpdatedAt"
                FROM "Tracks" AS track
                WHERE track."SongId" IS NULL
                ON CONFLICT ("Id") DO NOTHING;

                UPDATE "Tracks" AS track
                SET "SongId" = md5('cantaro:legacy-song:' || track."Id"::text)::uuid
                WHERE track."SongId" IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Tracks_SongId",
                table: "Tracks",
                column: "SongId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tracks_Songs_SongId",
                table: "Tracks",
                column: "SongId",
                principalTable: "Songs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tracks_Songs_SongId",
                table: "Tracks");

            migrationBuilder.DropTable(
                name: "Songs");

            migrationBuilder.DropIndex(
                name: "IX_Tracks_SongId",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "SongId",
                table: "Tracks");
        }
    }
}
