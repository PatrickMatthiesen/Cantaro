using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaEpisodeDestinations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MediaEpisodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaTitleId = table.Column<Guid>(type: "uuid", nullable: false),
                    EpisodeNumber = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaEpisodes", x => x.Id);
                    table.CheckConstraint("CK_MediaEpisodes_EpisodeNumber", "\"EpisodeNumber\" > 0");
                    table.ForeignKey(
                        name: "FK_MediaEpisodes_MediaTitles_MediaTitleId",
                        column: x => x.MediaTitleId,
                        principalTable: "MediaTitles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaEpisodeProviderIdentities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaEpisodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderSeriesId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ProviderSeasonId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ProviderEpisodeId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ProviderSeasonNumber = table.Column<int>(type: "integer", nullable: true),
                    ProviderEpisodeNumber = table.Column<int>(type: "integer", nullable: true),
                    ProviderSequenceNumber = table.Column<int>(type: "integer", nullable: true),
                    ProviderUrlPath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    SeenCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    HasConflict = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaEpisodeProviderIdentities", x => x.Id);
                    table.CheckConstraint("CK_MediaEpisodeProviderIdentities_SeenCount", "\"SeenCount\" > 0");
                    table.ForeignKey(
                        name: "FK_MediaEpisodeProviderIdentities_MediaEpisodes_MediaEpisodeId",
                        column: x => x.MediaEpisodeId,
                        principalTable: "MediaEpisodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MediaEpisodeProviderIdentities_MediaEpisodeId_Provider_HasC~",
                table: "MediaEpisodeProviderIdentities",
                columns: new[] { "MediaEpisodeId", "Provider", "HasConflict" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaEpisodeProviderIdentities_Provider_ProviderEpisodeId",
                table: "MediaEpisodeProviderIdentities",
                columns: new[] { "Provider", "ProviderEpisodeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaEpisodes_MediaTitleId_EpisodeNumber",
                table: "MediaEpisodes",
                columns: new[] { "MediaTitleId", "EpisodeNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MediaEpisodeProviderIdentities");

            migrationBuilder.DropTable(
                name: "MediaEpisodes");
        }
    }
}
