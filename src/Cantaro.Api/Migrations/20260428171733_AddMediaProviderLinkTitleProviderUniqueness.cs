using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaProviderLinkTitleProviderUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MediaProviderLinks_MediaTitleId",
                table: "MediaProviderLinks");

            migrationBuilder.CreateIndex(
                name: "IX_MediaProviderLinks_MediaTitleId_Provider",
                table: "MediaProviderLinks",
                columns: new[] { "MediaTitleId", "Provider" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MediaProviderLinks_MediaTitleId_Provider",
                table: "MediaProviderLinks");

            migrationBuilder.CreateIndex(
                name: "IX_MediaProviderLinks_MediaTitleId",
                table: "MediaProviderLinks",
                column: "MediaTitleId");
        }
    }
}
