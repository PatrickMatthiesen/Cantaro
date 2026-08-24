using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class TrackMediaProviderInitialSyncBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BatchId",
                table: "MediaProviderOperations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaProviderOperations_BatchId",
                table: "MediaProviderOperations",
                column: "BatchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MediaProviderOperations_BatchId",
                table: "MediaProviderOperations");

            migrationBuilder.DropColumn(
                name: "BatchId",
                table: "MediaProviderOperations");
        }
    }
}
