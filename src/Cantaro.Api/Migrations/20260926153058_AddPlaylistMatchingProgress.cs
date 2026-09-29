using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaylistMatchingProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MatchingProcessedCount",
                table: "ServicePlaylistMappings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "MatchingProgressJson",
                table: "ServicePlaylistMappings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MatchingTotalCount",
                table: "ServicePlaylistMappings",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MatchingProcessedCount",
                table: "ServicePlaylistMappings");

            migrationBuilder.DropColumn(
                name: "MatchingProgressJson",
                table: "ServicePlaylistMappings");

            migrationBuilder.DropColumn(
                name: "MatchingTotalCount",
                table: "ServicePlaylistMappings");
        }
    }
}
