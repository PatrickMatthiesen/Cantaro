using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSpotifyTokenRefreshLease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "TokenRefreshLeaseExpiresAt",
                table: "ConnectedServiceAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TokenRefreshLeaseId",
                table: "ConnectedServiceAccounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TokenVersion",
                table: "ConnectedServiceAccounts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "IX_ConnectedServiceAccounts_TokenRefreshLeaseExpiresAt",
                table: "ConnectedServiceAccounts",
                column: "TokenRefreshLeaseExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ConnectedServiceAccounts_TokenRefreshLeaseExpiresAt",
                table: "ConnectedServiceAccounts");

            migrationBuilder.DropColumn(
                name: "TokenRefreshLeaseExpiresAt",
                table: "ConnectedServiceAccounts");

            migrationBuilder.DropColumn(
                name: "TokenRefreshLeaseId",
                table: "ConnectedServiceAccounts");

            migrationBuilder.DropColumn(
                name: "TokenVersion",
                table: "ConnectedServiceAccounts");
        }
    }
}
