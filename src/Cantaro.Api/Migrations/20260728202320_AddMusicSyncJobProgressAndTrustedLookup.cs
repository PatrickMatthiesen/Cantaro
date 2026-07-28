using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMusicSyncJobProgressAndTrustedLookup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CurrentSongName",
                table: "MusicSyncJobs",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackObservations_SourceType_NormalizedTitle_DurationSecond~",
                table: "TrackObservations",
                columns: new[] { "SourceType", "NormalizedTitle", "DurationSeconds", "TrackId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TrackObservations_SourceType_NormalizedTitle_DurationSecond~",
                table: "TrackObservations");

            migrationBuilder.DropColumn(
                name: "CurrentSongName",
                table: "MusicSyncJobs");
        }
    }
}
