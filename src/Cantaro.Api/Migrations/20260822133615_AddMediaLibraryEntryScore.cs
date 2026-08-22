using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaLibraryEntryScore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Score",
                table: "MediaLibraryEntries",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_MediaLibraryEntries_Score",
                table: "MediaLibraryEntries",
                sql: "\"Score\" IS NULL OR (CAST(\"Score\" AS NUMERIC) >= 1 AND CAST(\"Score\" AS NUMERIC) <= 100)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_MediaLibraryEntries_Score",
                table: "MediaLibraryEntries");

            migrationBuilder.DropColumn(
                name: "Score",
                table: "MediaLibraryEntries");
        }
    }
}
