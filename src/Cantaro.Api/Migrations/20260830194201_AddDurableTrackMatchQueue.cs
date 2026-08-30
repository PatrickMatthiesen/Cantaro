using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableTrackMatchQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrackMatchQueueItems",
                columns: table => new
                {
                    TrackObservationId = table.Column<Guid>(type: "uuid", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackMatchQueueItems", x => x.TrackObservationId);
                    table.ForeignKey(
                        name: "FK_TrackMatchQueueItems_TrackObservations_TrackObservationId",
                        column: x => x.TrackObservationId,
                        principalTable: "TrackObservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrackMatchQueueItems_NextAttemptAt",
                table: "TrackMatchQueueItems",
                column: "NextAttemptAt");

            migrationBuilder.Sql("""
                INSERT INTO "TrackMatchQueueItems" ("TrackObservationId", "NextAttemptAt")
                SELECT "Id", CURRENT_TIMESTAMP
                FROM "TrackObservations"
                WHERE "TrackId" IS NULL
                  AND "MatchStatus" = 'pending'
                  AND "MatchAttemptCount" < 5
                ON CONFLICT ("TrackObservationId") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrackMatchQueueItems");
        }
    }
}
