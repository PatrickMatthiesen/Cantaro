using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAioStreamsWatchState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AioStreamsWatchEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MediaLibraryEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaEpisodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    At = table.Column<long>(type: "bigint", nullable: false),
                    PositionMs = table.Column<long>(type: "bigint", nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true),
                    Played = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AioStreamsWatchEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AioStreamsWatchEvents_MediaEpisodes_MediaEpisodeId",
                        column: x => x.MediaEpisodeId,
                        principalTable: "MediaEpisodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AioStreamsWatchEvents_MediaLibraryEntries_MediaLibraryEntry~",
                        column: x => x.MediaLibraryEntryId,
                        principalTable: "MediaLibraryEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AioStreamsWatchEvents_MediaEpisodeId",
                table: "AioStreamsWatchEvents",
                column: "MediaEpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_AioStreamsWatchEvents_MediaLibraryEntryId_MediaEpisodeId_At~",
                table: "AioStreamsWatchEvents",
                columns: new[] { "MediaLibraryEntryId", "MediaEpisodeId", "At", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AioStreamsWatchEvents_MediaLibraryEntryId_MediaEpisodeId_Ev~",
                table: "AioStreamsWatchEvents",
                columns: new[] { "MediaLibraryEntryId", "MediaEpisodeId", "EventId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AioStreamsWatchEvents");
        }
    }
}
