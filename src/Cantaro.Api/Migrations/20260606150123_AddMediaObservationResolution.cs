using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaObservationResolution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EpisodeOffset",
                table: "MediaObservations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderChoicesPayload",
                table: "MediaObservations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolutionHistoryPayload",
                table: "MediaObservations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ResolvedLibraryEntryId",
                table: "MediaObservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResolvedProgress",
                table: "MediaObservations",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MediaObservationEpisodeOffsets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    SiteIdentifier = table.Column<string>(type: "text", nullable: false),
                    MediaTitleId = table.Column<Guid>(type: "uuid", nullable: false),
                    EpisodeOffset = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaObservationEpisodeOffsets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaObservationEpisodeOffsets_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MediaObservationEpisodeOffsets_MediaTitles_MediaTitleId",
                        column: x => x.MediaTitleId,
                        principalTable: "MediaTitles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MediaObservationEpisodeOffsets_MediaTitleId",
                table: "MediaObservationEpisodeOffsets",
                column: "MediaTitleId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaObservationEpisodeOffsets_UserId_SiteIdentifier_MediaT~",
                table: "MediaObservationEpisodeOffsets",
                columns: new[] { "UserId", "SiteIdentifier", "MediaTitleId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MediaObservationEpisodeOffsets");

            migrationBuilder.DropColumn(
                name: "EpisodeOffset",
                table: "MediaObservations");

            migrationBuilder.DropColumn(
                name: "ProviderChoicesPayload",
                table: "MediaObservations");

            migrationBuilder.DropColumn(
                name: "ResolutionHistoryPayload",
                table: "MediaObservations");

            migrationBuilder.DropColumn(
                name: "ResolvedLibraryEntryId",
                table: "MediaObservations");

            migrationBuilder.DropColumn(
                name: "ResolvedProgress",
                table: "MediaObservations");
        }
    }
}
