using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAniListProviderOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MediaProviderOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    MediaLibraryEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectedServiceAccountId = table.Column<int>(type: "integer", nullable: true),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    OperationType = table.Column<string>(type: "text", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaProviderOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaProviderOperations_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MediaProviderOperations_ConnectedServiceAccounts_ConnectedS~",
                        column: x => x.ConnectedServiceAccountId,
                        principalTable: "ConnectedServiceAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MediaProviderOperations_MediaLibraryEntries_MediaLibraryEnt~",
                        column: x => x.MediaLibraryEntryId,
                        principalTable: "MediaLibraryEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MediaProviderOperations_ConnectedServiceAccountId",
                table: "MediaProviderOperations",
                column: "ConnectedServiceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaProviderOperations_MediaLibraryEntryId",
                table: "MediaProviderOperations",
                column: "MediaLibraryEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaProviderOperations_Status_NextAttemptAt",
                table: "MediaProviderOperations",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaProviderOperations_UserId_Provider_CreatedAt",
                table: "MediaProviderOperations",
                columns: new[] { "UserId", "Provider", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MediaProviderOperations");
        }
    }
}
