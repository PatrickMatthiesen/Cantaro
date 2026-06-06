using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveMediaObservationAutoProgressOptIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoProgressFromObservations",
                table: "MediaLibraryEntries");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoProgressFromObservations",
                table: "MediaLibraryEntries",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
