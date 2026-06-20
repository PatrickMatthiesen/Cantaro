using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserSettings",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Theme = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    NotifyOnSyncSuccess = table.Column<bool>(type: "boolean", nullable: false),
                    NotifyOnSyncFailure = table.Column<bool>(type: "boolean", nullable: false),
                    NotifyOnMediaReview = table.Column<bool>(type: "boolean", nullable: false),
                    KeepPlaylistOrder = table.Column<bool>(type: "boolean", nullable: false),
                    KeepPlaylistMetadata = table.Column<bool>(type: "boolean", nullable: false),
                    HideUnavailableTracks = table.Column<bool>(type: "boolean", nullable: false),
                    ScheduledSync = table.Column<bool>(type: "boolean", nullable: false),
                    AvatarObjectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    AvatarETag = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    AvatarVersion = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSettings", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserSettings_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserSettings");
        }
    }
}
