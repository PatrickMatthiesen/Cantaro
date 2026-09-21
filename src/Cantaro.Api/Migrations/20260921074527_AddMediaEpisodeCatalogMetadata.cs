using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaEpisodeCatalogMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EpisodeCatalogLastVerifiedAt",
                table: "MediaProviderLinks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpecialEpisodeCatalogSnapshot",
                table: "MediaProviderLinks",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EpisodeCatalogLastVerifiedAt",
                table: "MediaProviderLinks");

            migrationBuilder.DropColumn(
                name: "SpecialEpisodeCatalogSnapshot",
                table: "MediaProviderLinks");
        }
    }
}
