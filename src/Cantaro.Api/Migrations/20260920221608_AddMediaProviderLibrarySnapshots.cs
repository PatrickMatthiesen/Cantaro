using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaProviderLibrarySnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastAppliedStatus",
                table: "MediaLibraryProviderBindings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastRequestedStatus",
                table: "MediaLibraryProviderBindings",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MediaProviderLibrarySnapshots",
                columns: table => new
                {
                    ConnectedServiceAccountId = table.Column<int>(type: "integer", nullable: false),
                    Cursor = table.Column<string>(type: "text", nullable: true),
                    ItemsJson = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaProviderLibrarySnapshots", x => x.ConnectedServiceAccountId);
                    table.ForeignKey(
                        name: "FK_MediaProviderLibrarySnapshots_ConnectedServiceAccounts_Conn~",
                        column: x => x.ConnectedServiceAccountId,
                        principalTable: "ConnectedServiceAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MediaProviderLibrarySnapshots");

            migrationBuilder.DropColumn(
                name: "LastAppliedStatus",
                table: "MediaLibraryProviderBindings");

            migrationBuilder.DropColumn(
                name: "LastRequestedStatus",
                table: "MediaLibraryProviderBindings");
        }
    }
}
