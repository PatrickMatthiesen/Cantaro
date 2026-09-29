using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCanonicalPlaylistSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ServicePlaylistMappings_ConnectedServiceAccounts_ConnectedS~",
                table: "ServicePlaylistMappings");

            migrationBuilder.DropIndex(
                name: "IX_ServicePlaylistMappings_ConnectedServiceAccountId_ServicePl~",
                table: "ServicePlaylistMappings");

            migrationBuilder.DropIndex(
                name: "IX_ServicePlaylistMappings_PlaylistId_ConnectedServiceAccountI~",
                table: "ServicePlaylistMappings");

            migrationBuilder.DropIndex(
                name: "IX_PlaylistEntries_PlaylistId_TrackId",
                table: "PlaylistEntries");

            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                table: "ServicePlaylistMappings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "BaselineJson",
                table: "ServicePlaylistMappings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BaselineName",
                table: "ServicePlaylistMappings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CanonicalRevision",
                table: "ServicePlaylistMappings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "DesiredName",
                table: "ServicePlaylistMappings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalAccountId",
                table: "ServicePlaylistMappings",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "InitialMode",
                table: "ServicePlaylistMappings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "ServicePlaylistMappings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAttemptAt",
                table: "ServicePlaylistMappings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PendingName",
                table: "ServicePlaylistMappings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PendingWriteJson",
                table: "ServicePlaylistMappings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectedName",
                table: "ServicePlaylistMappings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "ServicePlaylistMappings",
                type: "text",
                nullable: false,
                defaultValue: "active");

            migrationBuilder.AddColumn<int>(
                name: "UnresolvedCount",
                table: "ServicePlaylistMappings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "ServicePlaylistMappings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "AllowDuplicateTracks",
                table: "Playlists",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextSyncAt",
                table: "Playlists",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SyncEnabled",
                table: "Playlists",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SyncLeaseExpiresAt",
                table: "Playlists",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncLeaseId",
                table: "Playlists",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SyncRevision",
                table: "Playlists",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Bind existing copies to their real owner before adding identity constraints.
            // An unknown baseline adopts the remote contents without inferring removals.
            migrationBuilder.Sql("""
                UPDATE "ServicePlaylistMappings" AS mapping
                SET "UserId" = playlist."UserId",
                    "ExternalAccountId" = account."ExternalAccountId",
                    "State" = CASE WHEN mapping."ServicePlaylistId" LIKE 'pending:%'
                                   THEN 'creation_uncertain'
                                   WHEN account."ConnectionState" = 'disconnected' THEN 'paused'
                                   ELSE 'active' END,
                    "SyncMode" = CASE WHEN mapping."SyncMode" = 'import_only'
                                      THEN 'bidirectional' ELSE mapping."SyncMode" END
                FROM "Playlists" AS playlist, "ConnectedServiceAccounts" AS account
                WHERE mapping."PlaylistId" = playlist."Id"
                  AND mapping."ConnectedServiceAccountId" = account."Id";

                UPDATE "Playlists" AS playlist
                SET "SyncEnabled" = TRUE, "NextSyncAt" = CURRENT_TIMESTAMP + INTERVAL '1 day'
                WHERE EXISTS (SELECT 1 FROM "ServicePlaylistMappings" AS mapping
                              WHERE mapping."PlaylistId" = playlist."Id");
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ServicePlaylistMappings_ConnectedServiceAccountId",
                table: "ServicePlaylistMappings",
                column: "ConnectedServiceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ServicePlaylistMappings_NextAttemptAt",
                table: "ServicePlaylistMappings",
                column: "NextAttemptAt");

            migrationBuilder.CreateIndex(
                name: "IX_ServicePlaylistMappings_PlaylistId_Service_ExternalAccountId",
                table: "ServicePlaylistMappings",
                columns: new[] { "PlaylistId", "Service", "ExternalAccountId" },
                unique: true,
                filter: "\"State\" <> 'unlinked'");

            migrationBuilder.CreateIndex(
                name: "IX_ServicePlaylistMappings_UserId_Service_ExternalAccountId_Se~",
                table: "ServicePlaylistMappings",
                columns: new[] { "UserId", "Service", "ExternalAccountId", "ServicePlaylistId" },
                unique: true,
                filter: "\"State\" <> 'unlinked'");

            migrationBuilder.CreateIndex(
                name: "IX_Playlists_NextSyncAt",
                table: "Playlists",
                column: "NextSyncAt");

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistEntries_PlaylistId_TrackId",
                table: "PlaylistEntries",
                columns: new[] { "PlaylistId", "TrackId" },
                filter: "\"TrackId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_ServicePlaylistMappings_ConnectedServiceAccounts_ConnectedS~",
                table: "ServicePlaylistMappings",
                column: "ConnectedServiceAccountId",
                principalTable: "ConnectedServiceAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
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

            migrationBuilder.DropIndex(
                name: "IX_ServicePlaylistMappings_NextAttemptAt",
                table: "ServicePlaylistMappings");

            migrationBuilder.DropIndex(
                name: "IX_ServicePlaylistMappings_PlaylistId_Service_ExternalAccountId",
                table: "ServicePlaylistMappings");

            migrationBuilder.DropIndex(
                name: "IX_ServicePlaylistMappings_UserId_Service_ExternalAccountId_Se~",
                table: "ServicePlaylistMappings");

            migrationBuilder.DropIndex(
                name: "IX_Playlists_NextSyncAt",
                table: "Playlists");

            migrationBuilder.DropIndex(
                name: "IX_PlaylistEntries_PlaylistId_TrackId",
                table: "PlaylistEntries");

            migrationBuilder.DropColumn(
                name: "AttemptCount",
                table: "ServicePlaylistMappings");

            migrationBuilder.DropColumn(
                name: "BaselineJson",
                table: "ServicePlaylistMappings");

            migrationBuilder.DropColumn(
                name: "BaselineName",
                table: "ServicePlaylistMappings");

            migrationBuilder.DropColumn(
                name: "CanonicalRevision",
                table: "ServicePlaylistMappings");

            migrationBuilder.DropColumn(
                name: "DesiredName",
                table: "ServicePlaylistMappings");

            migrationBuilder.DropColumn(
                name: "ExternalAccountId",
                table: "ServicePlaylistMappings");

            migrationBuilder.DropColumn(
                name: "InitialMode",
                table: "ServicePlaylistMappings");

            migrationBuilder.DropColumn(
                name: "LastError",
                table: "ServicePlaylistMappings");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "ServicePlaylistMappings");

            migrationBuilder.DropColumn(
                name: "PendingName",
                table: "ServicePlaylistMappings");

            migrationBuilder.DropColumn(
                name: "PendingWriteJson",
                table: "ServicePlaylistMappings");

            migrationBuilder.DropColumn(
                name: "RejectedName",
                table: "ServicePlaylistMappings");

            migrationBuilder.DropColumn(
                name: "State",
                table: "ServicePlaylistMappings");

            migrationBuilder.DropColumn(
                name: "UnresolvedCount",
                table: "ServicePlaylistMappings");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "ServicePlaylistMappings");

            migrationBuilder.DropColumn(
                name: "AllowDuplicateTracks",
                table: "Playlists");

            migrationBuilder.DropColumn(
                name: "NextSyncAt",
                table: "Playlists");

            migrationBuilder.DropColumn(
                name: "SyncEnabled",
                table: "Playlists");

            migrationBuilder.DropColumn(
                name: "SyncLeaseExpiresAt",
                table: "Playlists");

            migrationBuilder.DropColumn(
                name: "SyncLeaseId",
                table: "Playlists");

            migrationBuilder.DropColumn(
                name: "SyncRevision",
                table: "Playlists");

            migrationBuilder.CreateIndex(
                name: "IX_ServicePlaylistMappings_ConnectedServiceAccountId_ServicePl~",
                table: "ServicePlaylistMappings",
                columns: new[] { "ConnectedServiceAccountId", "ServicePlaylistId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServicePlaylistMappings_PlaylistId_ConnectedServiceAccountI~",
                table: "ServicePlaylistMappings",
                columns: new[] { "PlaylistId", "ConnectedServiceAccountId", "Service" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistEntries_PlaylistId_TrackId",
                table: "PlaylistEntries",
                columns: new[] { "PlaylistId", "TrackId" },
                unique: true,
                filter: "\"TrackId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_ServicePlaylistMappings_ConnectedServiceAccounts_ConnectedS~",
                table: "ServicePlaylistMappings",
                column: "ConnectedServiceAccountId",
                principalTable: "ConnectedServiceAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
