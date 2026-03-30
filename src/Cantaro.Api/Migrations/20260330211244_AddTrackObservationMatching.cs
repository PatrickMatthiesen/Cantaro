using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackObservationMatching : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "TrackId",
                table: "PlaylistEntries",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "TrackObservationId",
                table: "PlaylistEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TrackObservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceType = table.Column<string>(type: "text", nullable: false),
                    ExternalId = table.Column<string>(type: "text", nullable: false),
                    RawMetadata = table.Column<string>(type: "text", nullable: true),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Artist = table.Column<string>(type: "text", nullable: true),
                    ThumbnailUrl = table.Column<string>(type: "text", nullable: true),
                    NormalizedTitle = table.Column<string>(type: "text", nullable: true),
                    NormalizedArtist = table.Column<string>(type: "text", nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    MatchStatus = table.Column<string>(type: "text", nullable: false),
                    TrackId = table.Column<Guid>(type: "uuid", nullable: true),
                    MatchAttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastMatchAttemptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastMatchError = table.Column<string>(type: "text", nullable: true),
                    ResolutionNotes = table.Column<string>(type: "text", nullable: true),
                    AcceptedCandidateId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackObservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackObservations_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TrackResolutionCandidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TrackObservationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateSource = table.Column<string>(type: "text", nullable: false),
                    ExternalId = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Artist = table.Column<string>(type: "text", nullable: true),
                    MbidRecording = table.Column<string>(type: "text", nullable: true),
                    Isrc = table.Column<string>(type: "text", nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    Score = table.Column<decimal>(type: "numeric", nullable: false),
                    Explanation = table.Column<string>(type: "text", nullable: true),
                    RawMetadata = table.Column<string>(type: "text", nullable: true),
                    IsAccepted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackResolutionCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackResolutionCandidates_TrackObservations_TrackObservatio~",
                        column: x => x.TrackObservationId,
                        principalTable: "TrackObservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistEntries_TrackObservationId",
                table: "PlaylistEntries",
                column: "TrackObservationId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackObservations_MatchStatus",
                table: "TrackObservations",
                column: "MatchStatus");

            migrationBuilder.CreateIndex(
                name: "IX_TrackObservations_SourceType_ExternalId",
                table: "TrackObservations",
                columns: new[] { "SourceType", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackObservations_TrackId",
                table: "TrackObservations",
                column: "TrackId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackResolutionCandidates_TrackObservationId_Score",
                table: "TrackResolutionCandidates",
                columns: new[] { "TrackObservationId", "Score" });

            migrationBuilder.AddForeignKey(
                name: "FK_PlaylistEntries_TrackObservations_TrackObservationId",
                table: "PlaylistEntries",
                column: "TrackObservationId",
                principalTable: "TrackObservations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PlaylistEntries_TrackObservations_TrackObservationId",
                table: "PlaylistEntries");

            migrationBuilder.DropTable(
                name: "TrackResolutionCandidates");

            migrationBuilder.DropTable(
                name: "TrackObservations");

            migrationBuilder.DropIndex(
                name: "IX_PlaylistEntries_TrackObservationId",
                table: "PlaylistEntries");

            migrationBuilder.DropColumn(
                name: "TrackObservationId",
                table: "PlaylistEntries");

            migrationBuilder.AlterColumn<Guid>(
                name: "TrackId",
                table: "PlaylistEntries",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
