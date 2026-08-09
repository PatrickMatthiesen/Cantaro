using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaEpisodeLanguageAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PreferredMediaReleaseTrack",
                table: "UserSettings",
                type: "text",
                nullable: false,
                defaultValue: "sub:en");

            migrationBuilder.AddColumn<string[]>(
                name: "AvailableAudioLanguageCodes",
                table: "MediaEpisodes",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string[]>(
                name: "AvailableSubtitleLanguageCodes",
                table: "MediaEpisodes",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreferredMediaReleaseTrack",
                table: "UserSettings");

            migrationBuilder.DropColumn(
                name: "AvailableAudioLanguageCodes",
                table: "MediaEpisodes");

            migrationBuilder.DropColumn(
                name: "AvailableSubtitleLanguageCodes",
                table: "MediaEpisodes");
        }
    }
}
