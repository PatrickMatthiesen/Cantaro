using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaEpisodeProviderReleaseTrack : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReleaseTrack",
                table: "MediaEpisodeProviderIdentities",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "MediaEpisodeProviderIdentities"
                SET "ReleaseTrack" = CASE
                    WHEN "ProviderEpisodeId" LIKE '%ENUS' THEN 'dub:en'
                    WHEN "ProviderEpisodeId" LIKE '%JAJP' THEN 'sub:en'
                END
                WHERE "Provider" = 'crunchyroll'
                  AND ("ProviderEpisodeId" LIKE '%ENUS' OR "ProviderEpisodeId" LIKE '%JAJP');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReleaseTrack",
                table: "MediaEpisodeProviderIdentities");
        }
    }
}
