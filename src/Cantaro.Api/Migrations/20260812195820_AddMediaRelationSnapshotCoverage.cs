using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaRelationSnapshotCoverage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RelationsSnapshotId",
                table: "MediaProviderLinks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsTrusted",
                table: "MediaEpisodeProviderIdentities",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RelationsSnapshotId",
                table: "MediaProviderLinks");

            migrationBuilder.DropColumn(
                name: "IsTrusted",
                table: "MediaEpisodeProviderIdentities");
        }
    }
}
