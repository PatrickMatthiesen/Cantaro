using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddKitsuWatchStateIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Episode",
                table: "AioStreamsWatchEvents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MetaId",
                table: "AioStreamsWatchEvents",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Season",
                table: "AioStreamsWatchEvents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "AioStreamsWatchEvents",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "AioStreamsWatchEvents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VideoId",
                table: "AioStreamsWatchEvents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AioStreamsWatchEvents_UserId_EventId",
                table: "AioStreamsWatchEvents",
                columns: new[] { "UserId", "EventId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AioStreamsWatchEvents_UserId_EventId",
                table: "AioStreamsWatchEvents");

            migrationBuilder.DropColumn(
                name: "Episode",
                table: "AioStreamsWatchEvents");

            migrationBuilder.DropColumn(
                name: "MetaId",
                table: "AioStreamsWatchEvents");

            migrationBuilder.DropColumn(
                name: "Season",
                table: "AioStreamsWatchEvents");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "AioStreamsWatchEvents");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "AioStreamsWatchEvents");

            migrationBuilder.DropColumn(
                name: "VideoId",
                table: "AioStreamsWatchEvents");
        }
    }
}
