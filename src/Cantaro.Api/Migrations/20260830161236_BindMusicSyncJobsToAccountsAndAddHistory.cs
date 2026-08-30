using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class BindMusicSyncJobsToAccountsAndAddHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing sync jobs and provider mappings predate explicit account binding.
            // They cannot be assigned safely when a user has more than one provider account,
            // so reset this disposable sync state and rebuild it from the providers.
            migrationBuilder.Sql("DELETE FROM \"MusicSyncJobs\";");
            migrationBuilder.Sql("DELETE FROM \"ServicePlaylistMappings\";");

            migrationBuilder.DropIndex(
                name: "IX_ServicePlaylistMappings_ConnectedServiceAccountId",
                table: "ServicePlaylistMappings");

            migrationBuilder.DropIndex(
                name: "IX_ServicePlaylistMappings_PlaylistId_Service",
                table: "ServicePlaylistMappings");

            migrationBuilder.AlterColumn<int>(
                name: "ConnectedServiceAccountId",
                table: "ServicePlaylistMappings",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConnectedServiceAccountId",
                table: "MusicSyncJobs",
                type: "integer",
                nullable: true);

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
                name: "IX_MusicSyncJobs_ConnectedServiceAccountId",
                table: "MusicSyncJobs",
                column: "ConnectedServiceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MusicSyncJobs_UserId_CreatedAt_Id",
                table: "MusicSyncJobs",
                columns: new[] { "UserId", "CreatedAt", "Id" });

            migrationBuilder.AddForeignKey(
                name: "FK_MusicSyncJobs_ConnectedServiceAccounts_ConnectedServiceAcco~",
                table: "MusicSyncJobs",
                column: "ConnectedServiceAccountId",
                principalTable: "ConnectedServiceAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MusicSyncJobs_ConnectedServiceAccounts_ConnectedServiceAcco~",
                table: "MusicSyncJobs");

            migrationBuilder.DropIndex(
                name: "IX_ServicePlaylistMappings_ConnectedServiceAccountId_ServicePl~",
                table: "ServicePlaylistMappings");

            migrationBuilder.DropIndex(
                name: "IX_ServicePlaylistMappings_PlaylistId_ConnectedServiceAccountI~",
                table: "ServicePlaylistMappings");

            migrationBuilder.DropIndex(
                name: "IX_MusicSyncJobs_ConnectedServiceAccountId",
                table: "MusicSyncJobs");

            migrationBuilder.DropIndex(
                name: "IX_MusicSyncJobs_UserId_CreatedAt_Id",
                table: "MusicSyncJobs");

            migrationBuilder.DropColumn(
                name: "ConnectedServiceAccountId",
                table: "MusicSyncJobs");

            migrationBuilder.AlterColumn<int>(
                name: "ConnectedServiceAccountId",
                table: "ServicePlaylistMappings",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.CreateIndex(
                name: "IX_ServicePlaylistMappings_ConnectedServiceAccountId",
                table: "ServicePlaylistMappings",
                column: "ConnectedServiceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ServicePlaylistMappings_PlaylistId_Service",
                table: "ServicePlaylistMappings",
                columns: new[] { "PlaylistId", "Service" },
                unique: true);
        }
    }
}
