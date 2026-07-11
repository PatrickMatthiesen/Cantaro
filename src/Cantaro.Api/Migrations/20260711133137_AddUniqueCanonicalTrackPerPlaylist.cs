using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueCanonicalTrackPerPlaylist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "PlaylistEntries" duplicate
                USING "PlaylistEntries" keeper
                WHERE duplicate."PlaylistId" = keeper."PlaylistId"
                  AND duplicate."TrackId" = keeper."TrackId"
                  AND duplicate."TrackId" IS NOT NULL
                  AND (duplicate."Position" > keeper."Position"
                    OR (duplicate."Position" = keeper."Position" AND duplicate."Id" > keeper."Id"));
                """);
            migrationBuilder.CreateIndex(
                name: "IX_PlaylistEntries_PlaylistId_TrackId",
                table: "PlaylistEntries",
                columns: new[] { "PlaylistId", "TrackId" },
                unique: true,
                filter: "\"TrackId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlaylistEntries_PlaylistId_TrackId",
                table: "PlaylistEntries");
        }
    }
}
