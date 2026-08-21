using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaProviderAvailabilitySnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AvailabilityLastVerifiedAt",
                table: "MediaProviderLinks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AvailabilitySnapshot",
                table: "MediaProviderLinks",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AvailabilityLastVerifiedAt",
                table: "MediaProviderLinks");

            migrationBuilder.DropColumn(
                name: "AvailabilitySnapshot",
                table: "MediaProviderLinks");
        }
    }
}
