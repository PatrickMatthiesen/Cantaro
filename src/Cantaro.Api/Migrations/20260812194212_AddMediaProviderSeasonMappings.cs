using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaProviderSeasonMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MediaProviderSeasonMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderSeriesId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ProviderSeasonId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ProviderSeasonNumber = table.Column<int>(type: "integer", nullable: true),
                    MediaTitleId = table.Column<Guid>(type: "uuid", nullable: false),
                    EpisodeOffset = table.Column<int>(type: "integer", nullable: false),
                    MappingSource = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    HasConflict = table.Column<bool>(type: "boolean", nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastVerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaProviderSeasonMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaProviderSeasonMappings_MediaTitles_MediaTitleId",
                        column: x => x.MediaTitleId,
                        principalTable: "MediaTitles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MediaProviderSeasonMappings_MediaTitleId",
                table: "MediaProviderSeasonMappings",
                column: "MediaTitleId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaProviderSeasonMappings_Provider_ProviderSeriesId_Prov~1",
                table: "MediaProviderSeasonMappings",
                columns: new[] { "Provider", "ProviderSeriesId", "ProviderSeasonNumber" },
                unique: true,
                filter: "\"ProviderSeasonId\" IS NULL AND \"ProviderSeasonNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MediaProviderSeasonMappings_Provider_ProviderSeriesId_Provi~",
                table: "MediaProviderSeasonMappings",
                columns: new[] { "Provider", "ProviderSeriesId", "ProviderSeasonId" },
                unique: true,
                filter: "\"ProviderSeasonId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MediaProviderSeasonMappings");
        }
    }
}
