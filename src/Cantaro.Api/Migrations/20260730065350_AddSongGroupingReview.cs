using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSongGroupingReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SongGroupingSuggestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateTrackId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSongId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetSongId = table.Column<Guid>(type: "uuid", nullable: false),
                    AnchorTrackId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    EvidenceJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewedByUserId = table.Column<int>(type: "integer", nullable: true),
                    AppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SongGroupingSuggestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SongGroupingSuggestions_AspNetUsers_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SongGroupingSuggestions_Songs_SourceSongId",
                        column: x => x.SourceSongId,
                        principalTable: "Songs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SongGroupingSuggestions_Songs_TargetSongId",
                        column: x => x.TargetSongId,
                        principalTable: "Songs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SongGroupingSuggestions_Tracks_AnchorTrackId",
                        column: x => x.AnchorTrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SongGroupingSuggestions_Tracks_CandidateTrackId",
                        column: x => x.CandidateTrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SongGroupingSuggestions_AnchorTrackId",
                table: "SongGroupingSuggestions",
                column: "AnchorTrackId");

            migrationBuilder.CreateIndex(
                name: "IX_SongGroupingSuggestions_CandidateTrackId_TargetSongId",
                table: "SongGroupingSuggestions",
                columns: new[] { "CandidateTrackId", "TargetSongId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SongGroupingSuggestions_ReviewedByUserId",
                table: "SongGroupingSuggestions",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SongGroupingSuggestions_SourceSongId",
                table: "SongGroupingSuggestions",
                column: "SourceSongId");

            migrationBuilder.CreateIndex(
                name: "IX_SongGroupingSuggestions_Status_CreatedAt",
                table: "SongGroupingSuggestions",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SongGroupingSuggestions_TargetSongId",
                table: "SongGroupingSuggestions",
                column: "TargetSongId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SongGroupingSuggestions");
        }
    }
}
