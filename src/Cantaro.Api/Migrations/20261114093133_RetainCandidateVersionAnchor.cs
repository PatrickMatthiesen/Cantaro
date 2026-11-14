using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class RetainCandidateVersionAnchor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AnchorTrackId",
                table: "TrackResolutionCandidates",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackResolutionCandidates_AnchorTrackId",
                table: "TrackResolutionCandidates",
                column: "AnchorTrackId");

            migrationBuilder.AddForeignKey(
                name: "FK_TrackResolutionCandidates_Tracks_AnchorTrackId",
                table: "TrackResolutionCandidates",
                column: "AnchorTrackId",
                principalTable: "Tracks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TrackResolutionCandidates_Tracks_AnchorTrackId",
                table: "TrackResolutionCandidates");

            migrationBuilder.DropIndex(
                name: "IX_TrackResolutionCandidates_AnchorTrackId",
                table: "TrackResolutionCandidates");

            migrationBuilder.DropColumn(
                name: "AnchorTrackId",
                table: "TrackResolutionCandidates");
        }
    }
}
