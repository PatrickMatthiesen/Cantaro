using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaObservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MediaObservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    SiteIdentifier = table.Column<string>(type: "text", nullable: false),
                    ObservedUrl = table.Column<string>(type: "text", nullable: false),
                    SiteMediaId = table.Column<string>(type: "text", nullable: true),
                    ObservedTitle = table.Column<string>(type: "text", nullable: false),
                    ProgressHint = table.Column<string>(type: "text", nullable: true),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExtensionVersion = table.Column<string>(type: "text", nullable: true),
                    RawPayload = table.Column<string>(type: "text", nullable: true),
                    MatchStatus = table.Column<string>(type: "text", nullable: false),
                    MediaTitleId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcceptedCandidateId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolutionNotes = table.Column<string>(type: "text", nullable: true),
                    MatchAttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastMatchAttemptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastMatchError = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaObservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaObservations_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MediaObservations_MediaTitles_MediaTitleId",
                        column: x => x.MediaTitleId,
                        principalTable: "MediaTitles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MediaObservationCandidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaObservationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateSource = table.Column<string>(type: "text", nullable: false),
                    MediaTitleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderMediaId = table.Column<string>(type: "text", nullable: true),
                    Provider = table.Column<string>(type: "text", nullable: true),
                    Title = table.Column<string>(type: "text", nullable: false),
                    MediaKind = table.Column<string>(type: "text", nullable: false),
                    Score = table.Column<decimal>(type: "numeric", nullable: false),
                    Explanation = table.Column<string>(type: "text", nullable: true),
                    IsAccepted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaObservationCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaObservationCandidates_MediaObservations_MediaObservati~",
                        column: x => x.MediaObservationId,
                        principalTable: "MediaObservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MediaObservationCandidates_MediaTitles_MediaTitleId",
                        column: x => x.MediaTitleId,
                        principalTable: "MediaTitles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MediaObservations_UserId_CreatedAt",
                table: "MediaObservations",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaObservations_UserId_MatchStatus",
                table: "MediaObservations",
                columns: new[] { "UserId", "MatchStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaObservations_UserId_SiteIdentifier_SiteMediaId",
                table: "MediaObservations",
                columns: new[] { "UserId", "SiteIdentifier", "SiteMediaId" },
                unique: true,
                filter: "\"SiteMediaId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MediaObservationCandidates_MediaObservationId_Score",
                table: "MediaObservationCandidates",
                columns: new[] { "MediaObservationId", "Score" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaObservationCandidates_MediaTitleId",
                table: "MediaObservationCandidates",
                column: "MediaTitleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MediaObservationCandidates");

            migrationBuilder.DropTable(
                name: "MediaObservations");
        }
    }
}
