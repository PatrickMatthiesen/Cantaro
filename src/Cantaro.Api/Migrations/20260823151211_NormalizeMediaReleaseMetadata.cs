using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeMediaReleaseMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RawMetadata",
                table: "MediaProviderLinks");

            migrationBuilder.DropColumn(
                name: "RawMetadata",
                table: "MediaLibraryProviderBindings");

            migrationBuilder.AddColumn<int>(
                name: "TotalKnownCount",
                table: "MediaTitles",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TotalKnownCount",
                table: "MediaTitles");

            migrationBuilder.AddColumn<string>(
                name: "RawMetadata",
                table: "MediaProviderLinks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RawMetadata",
                table: "MediaLibraryProviderBindings",
                type: "text",
                nullable: true);
        }
    }
}
