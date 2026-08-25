using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeMediaEpisodeProviderVariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MediaEpisodeProviderIdentities_MediaEpisodes_MediaEpisodeId",
                table: "MediaEpisodeProviderIdentities");

            migrationBuilder.DropIndex(
                name: "IX_MediaEpisodeProviderIdentities_MediaEpisodeId_Provider_HasC~",
                table: "MediaEpisodeProviderIdentities");

            migrationBuilder.AddColumn<string>(
                name: "AudioLocale",
                table: "MediaEpisodeProviderIdentities",
                type: "character varying(35)",
                maxLength: 35,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MediaEpisodeProviderContentId",
                table: "MediaEpisodeProviderIdentities",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MediaEpisodeProviderContents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaEpisodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderContentKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ProviderSeriesId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ProviderSeasonId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ProviderSeasonNumber = table.Column<int>(type: "integer", nullable: true),
                    ProviderEpisodeNumber = table.Column<int>(type: "integer", nullable: true),
                    ProviderSequenceNumber = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaEpisodeProviderContents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaEpisodeProviderContents_MediaEpisodes_MediaEpisodeId",
                        column: x => x.MediaEpisodeId,
                        principalTable: "MediaEpisodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO "MediaEpisodeProviderContents" (
                    "Id", "MediaEpisodeId", "Provider", "ProviderContentKey",
                    "ProviderSeriesId", "ProviderSeasonId", "ProviderSeasonNumber",
                    "ProviderEpisodeNumber", "ProviderSequenceNumber")
                SELECT
                    (array_agg("Id" ORDER BY "Id"))[1],
                    "MediaEpisodeId",
                    "Provider",
                    CASE
                        WHEN "Provider" = 'crunchyroll'
                            AND "ProviderEpisodeId" ~ '^[A-Z0-9]{10}[A-Z]{4}$'
                        THEN left("ProviderEpisodeId", 10)
                        ELSE NULL
                    END,
                    max("ProviderSeriesId"),
                    max("ProviderSeasonId"),
                    max("ProviderSeasonNumber"),
                    max("ProviderEpisodeNumber"),
                    max("ProviderSequenceNumber")
                FROM "MediaEpisodeProviderIdentities"
                GROUP BY
                    "MediaEpisodeId",
                    "Provider",
                    CASE
                        WHEN "Provider" = 'crunchyroll'
                            AND "ProviderEpisodeId" ~ '^[A-Z0-9]{10}[A-Z]{4}$'
                        THEN left("ProviderEpisodeId", 10)
                        ELSE NULL
                    END;

                UPDATE "MediaEpisodeProviderIdentities" AS variant
                SET
                    "MediaEpisodeProviderContentId" = content."Id",
                    "AudioLocale" = CASE
                        WHEN variant."Provider" = 'crunchyroll'
                            AND variant."ProviderEpisodeId" ~ '^[A-Z0-9]{10}[A-Z]{4}$'
                        THEN lower(substr(variant."ProviderEpisodeId", 11, 2))
                            || '-' || upper(substr(variant."ProviderEpisodeId", 13, 2))
                        ELSE NULL
                    END
                FROM "MediaEpisodeProviderContents" AS content
                WHERE content."MediaEpisodeId" = variant."MediaEpisodeId"
                    AND content."Provider" = variant."Provider"
                    AND content."ProviderContentKey" IS NOT DISTINCT FROM CASE
                        WHEN variant."Provider" = 'crunchyroll'
                            AND variant."ProviderEpisodeId" ~ '^[A-Z0-9]{10}[A-Z]{4}$'
                        THEN left(variant."ProviderEpisodeId", 10)
                        ELSE NULL
                    END;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "MediaEpisodeProviderContentId",
                table: "MediaEpisodeProviderIdentities",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.DropColumn(name: "MediaEpisodeId", table: "MediaEpisodeProviderIdentities");
            migrationBuilder.DropColumn(name: "ProviderEpisodeNumber", table: "MediaEpisodeProviderIdentities");
            migrationBuilder.DropColumn(name: "ProviderSeasonId", table: "MediaEpisodeProviderIdentities");
            migrationBuilder.DropColumn(name: "ProviderSeasonNumber", table: "MediaEpisodeProviderIdentities");
            migrationBuilder.DropColumn(name: "ProviderSequenceNumber", table: "MediaEpisodeProviderIdentities");
            migrationBuilder.DropColumn(name: "ProviderSeriesId", table: "MediaEpisodeProviderIdentities");

            migrationBuilder.CreateIndex(
                name: "IX_MediaEpisodeProviderIdentities_MediaEpisodeProviderContentI~",
                table: "MediaEpisodeProviderIdentities",
                columns: new[] { "MediaEpisodeProviderContentId", "HasConflict" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaEpisodeProviderContents_MediaEpisodeId_Provider_Provid~",
                table: "MediaEpisodeProviderContents",
                columns: new[] { "MediaEpisodeId", "Provider", "ProviderContentKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaEpisodeProviderContents_Provider_ProviderContentKey",
                table: "MediaEpisodeProviderContents",
                columns: new[] { "Provider", "ProviderContentKey" });

            migrationBuilder.AddForeignKey(
                name: "FK_MediaEpisodeProviderIdentities_MediaEpisodeProviderContents~",
                table: "MediaEpisodeProviderIdentities",
                column: "MediaEpisodeProviderContentId",
                principalTable: "MediaEpisodeProviderContents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MediaEpisodeProviderIdentities_MediaEpisodeProviderContents~",
                table: "MediaEpisodeProviderIdentities");

            migrationBuilder.DropIndex(
                name: "IX_MediaEpisodeProviderIdentities_MediaEpisodeProviderContentI~",
                table: "MediaEpisodeProviderIdentities");

            migrationBuilder.AddColumn<int>(
                name: "ProviderEpisodeNumber",
                table: "MediaEpisodeProviderIdentities",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderSeasonId",
                table: "MediaEpisodeProviderIdentities",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProviderSeasonNumber",
                table: "MediaEpisodeProviderIdentities",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProviderSequenceNumber",
                table: "MediaEpisodeProviderIdentities",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderSeriesId",
                table: "MediaEpisodeProviderIdentities",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MediaEpisodeId",
                table: "MediaEpisodeProviderIdentities",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "MediaEpisodeProviderIdentities" AS variant
                SET
                    "MediaEpisodeId" = content."MediaEpisodeId",
                    "ProviderSeriesId" = content."ProviderSeriesId",
                    "ProviderSeasonId" = content."ProviderSeasonId",
                    "ProviderSeasonNumber" = content."ProviderSeasonNumber",
                    "ProviderEpisodeNumber" = content."ProviderEpisodeNumber",
                    "ProviderSequenceNumber" = content."ProviderSequenceNumber"
                FROM "MediaEpisodeProviderContents" AS content
                WHERE content."Id" = variant."MediaEpisodeProviderContentId";
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "MediaEpisodeId",
                table: "MediaEpisodeProviderIdentities",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.DropColumn(name: "AudioLocale", table: "MediaEpisodeProviderIdentities");
            migrationBuilder.DropColumn(name: "MediaEpisodeProviderContentId", table: "MediaEpisodeProviderIdentities");
            migrationBuilder.DropTable(name: "MediaEpisodeProviderContents");

            migrationBuilder.CreateIndex(
                name: "IX_MediaEpisodeProviderIdentities_MediaEpisodeId_Provider_HasC~",
                table: "MediaEpisodeProviderIdentities",
                columns: new[] { "MediaEpisodeId", "Provider", "HasConflict" });

            migrationBuilder.AddForeignKey(
                name: "FK_MediaEpisodeProviderIdentities_MediaEpisodes_MediaEpisodeId",
                table: "MediaEpisodeProviderIdentities",
                column: "MediaEpisodeId",
                principalTable: "MediaEpisodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
