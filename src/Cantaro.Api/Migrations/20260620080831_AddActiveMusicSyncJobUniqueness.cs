using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddActiveMusicSyncJobUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_MusicSyncJobs_UserId_Service",
                table: "MusicSyncJobs",
                columns: new[] { "UserId", "Service" },
                unique: true,
                filter: "\"Status\" IN ('queued', 'running')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MusicSyncJobs_UserId_Service",
                table: "MusicSyncJobs");
        }
    }
}
