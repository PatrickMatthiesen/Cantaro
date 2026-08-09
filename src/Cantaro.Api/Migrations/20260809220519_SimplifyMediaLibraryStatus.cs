using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyMediaLibraryStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RawListName",
                table: "MediaLibraryEntries");

            migrationBuilder.DropColumn(
                name: "RawStatus",
                table: "MediaLibraryEntries");

            migrationBuilder.RenameColumn(
                name: "NormalizedStatus",
                table: "MediaLibraryEntries",
                newName: "Status");

            migrationBuilder.RenameIndex(
                name: "IX_MediaLibraryEntries_UserId_NormalizedStatus",
                table: "MediaLibraryEntries",
                newName: "IX_MediaLibraryEntries_UserId_Status");

            migrationBuilder.CreateTable(
                name: "MediaProviderListMemberships",
                columns: table => new
                {
                    MediaLibraryEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaProviderListMemberships", x => new { x.MediaLibraryEntryId, x.Name });
                    table.ForeignKey(
                        name: "FK_MediaProviderListMemberships_MediaLibraryEntries_MediaLibra~",
                        column: x => x.MediaLibraryEntryId,
                        principalTable: "MediaLibraryEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MediaProviderListMemberships_Name_MediaLibraryEntryId",
                table: "MediaProviderListMemberships",
                columns: new[] { "Name", "MediaLibraryEntryId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MediaProviderListMemberships");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "MediaLibraryEntries",
                newName: "NormalizedStatus");

            migrationBuilder.RenameIndex(
                name: "IX_MediaLibraryEntries_UserId_Status",
                table: "MediaLibraryEntries",
                newName: "IX_MediaLibraryEntries_UserId_NormalizedStatus");

            migrationBuilder.AddColumn<string>(
                name: "RawListName",
                table: "MediaLibraryEntries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RawStatus",
                table: "MediaLibraryEntries",
                type: "text",
                nullable: true);
        }
    }
}
