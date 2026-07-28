using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class GeneralizeTrackIdentityLookupIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TrackObservations_SourceType_NormalizedTitle_DurationSecond~",
                table: "TrackObservations");

            migrationBuilder.CreateIndex(
                name: "IX_TrackObservations_IdentityLookup",
                table: "TrackObservations",
                columns: new[] { "NormalizedTitle", "DurationSeconds", "TrackId", "SourceType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TrackObservations_IdentityLookup",
                table: "TrackObservations");

            migrationBuilder.CreateIndex(
                name: "IX_TrackObservations_SourceType_NormalizedTitle_DurationSecond~",
                table: "TrackObservations",
                columns: new[] { "SourceType", "NormalizedTitle", "DurationSeconds", "TrackId" });
        }
    }
}
