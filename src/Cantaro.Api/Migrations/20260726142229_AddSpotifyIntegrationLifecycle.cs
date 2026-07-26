using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSpotifyIntegrationLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConnectedServiceAccountId",
                table: "ServicePlaylistMappings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImportedFromService",
                table: "Playlists",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConnectionState",
                table: "ConnectedServiceAccounts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "connected");

            migrationBuilder.AddColumn<string>(
                name: "EncryptedAccessToken",
                table: "ConnectedServiceAccounts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReconnectReason",
                table: "ConnectedServiceAccounts",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReconnectRequiredAt",
                table: "ConnectedServiceAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RefreshTokenExpiresAt",
                table: "ConnectedServiceAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServicePlaylistMappings_ConnectedServiceAccountId",
                table: "ServicePlaylistMappings",
                column: "ConnectedServiceAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_ServicePlaylistMappings_ConnectedServiceAccounts_ConnectedS~",
                table: "ServicePlaylistMappings",
                column: "ConnectedServiceAccountId",
                principalTable: "ConnectedServiceAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ServicePlaylistMappings_ConnectedServiceAccounts_ConnectedS~",
                table: "ServicePlaylistMappings");

            migrationBuilder.DropIndex(
                name: "IX_ServicePlaylistMappings_ConnectedServiceAccountId",
                table: "ServicePlaylistMappings");

            migrationBuilder.DropColumn(
                name: "ConnectedServiceAccountId",
                table: "ServicePlaylistMappings");

            migrationBuilder.DropColumn(
                name: "ImportedFromService",
                table: "Playlists");

            migrationBuilder.DropColumn(
                name: "ConnectionState",
                table: "ConnectedServiceAccounts");

            migrationBuilder.DropColumn(
                name: "EncryptedAccessToken",
                table: "ConnectedServiceAccounts");

            migrationBuilder.DropColumn(
                name: "ReconnectReason",
                table: "ConnectedServiceAccounts");

            migrationBuilder.DropColumn(
                name: "ReconnectRequiredAt",
                table: "ConnectedServiceAccounts");

            migrationBuilder.DropColumn(
                name: "RefreshTokenExpiresAt",
                table: "ConnectedServiceAccounts");
        }
    }
}
