using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class MinimizeMediaObservationStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SeriesTitle",
                table: "MediaObservations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EpisodeNumber",
                table: "MediaObservations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EpisodeTitle",
                table: "MediaObservations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCatalogObservation",
                table: "MediaObservations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "NextEpisodeNumber",
                table: "MediaObservations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NextEpisodeProviderId",
                table: "MediaObservations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NextEpisodeReleaseTrack",
                table: "MediaObservations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NextEpisodeTitle",
                table: "MediaObservations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NextEpisodeUrl",
                table: "MediaObservations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderSeasonId",
                table: "MediaObservations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProviderSequenceNumber",
                table: "MediaObservations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderSeriesId",
                table: "MediaObservations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReleaseTrack",
                table: "MediaObservations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SeasonNumber",
                table: "MediaObservations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SeasonTitle",
                table: "MediaObservations",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MediaObservationEpisode",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaObservationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderEpisodeId = table.Column<string>(type: "text", nullable: false),
                    ProviderUrl = table.Column<string>(type: "text", nullable: false),
                    EpisodeNumber = table.Column<int>(type: "integer", nullable: false),
                    EpisodeTitle = table.Column<string>(type: "text", nullable: true),
                    ReleaseTrack = table.Column<string>(type: "text", nullable: true),
                    AvailableSubtitleLanguageCodes = table.Column<List<string>>(type: "text[]", nullable: false),
                    AvailableAudioLanguageCodes = table.Column<List<string>>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaObservationEpisode", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaObservationEpisode_MediaObservations_MediaObservationId",
                        column: x => x.MediaObservationId,
                        principalTable: "MediaObservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MediaObservationEpisode_MediaObservationId",
                table: "MediaObservationEpisode",
                column: "MediaObservationId");

            BackfillEvidence(migrationBuilder);
            migrationBuilder.DropColumn(name: "RawPayload", table: "MediaObservations");
            migrationBuilder.CreateIndex(name: "IX_MediaObservations_UpdatedAt", table: "MediaObservations", column: "UpdatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Retired request bodies cannot be reconstructed on rollback.
            migrationBuilder.AddColumn<string>(name: "RawPayload", table: "MediaObservations", type: "text", nullable: true);
            migrationBuilder.DropIndex(name: "IX_MediaObservations_UpdatedAt", table: "MediaObservations");
            migrationBuilder.DropTable(
                name: "MediaObservationEpisode");

            migrationBuilder.DropColumn(
                name: "EpisodeNumber",
                table: "MediaObservations");

            migrationBuilder.DropColumn(
                name: "EpisodeTitle",
                table: "MediaObservations");

            migrationBuilder.DropColumn(
                name: "IsCatalogObservation",
                table: "MediaObservations");

            migrationBuilder.DropColumn(
                name: "NextEpisodeNumber",
                table: "MediaObservations");

            migrationBuilder.DropColumn(
                name: "NextEpisodeProviderId",
                table: "MediaObservations");

            migrationBuilder.DropColumn(
                name: "NextEpisodeReleaseTrack",
                table: "MediaObservations");

            migrationBuilder.DropColumn(
                name: "NextEpisodeTitle",
                table: "MediaObservations");

            migrationBuilder.DropColumn(
                name: "NextEpisodeUrl",
                table: "MediaObservations");

            migrationBuilder.DropColumn(
                name: "ProviderSeasonId",
                table: "MediaObservations");

            migrationBuilder.DropColumn(
                name: "ProviderSequenceNumber",
                table: "MediaObservations");

            migrationBuilder.DropColumn(
                name: "ProviderSeriesId",
                table: "MediaObservations");

            migrationBuilder.DropColumn(
                name: "ReleaseTrack",
                table: "MediaObservations");

            migrationBuilder.DropColumn(
                name: "SeasonNumber",
                table: "MediaObservations");

            migrationBuilder.DropColumn(
                name: "SeasonTitle",
                table: "MediaObservations");

            migrationBuilder.DropColumn(
                name: "SeriesTitle",
                table: "MediaObservations");
        }
    }
}
