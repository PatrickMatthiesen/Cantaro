using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class PersistSpotifyApiGate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SpotifyApiGateStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NotBefore = table.Column<long>(type: "bigint", nullable: false),
                    NotBeforeIsQuotaExceeded = table.Column<bool>(type: "boolean", nullable: false),
                    NextRequestAt = table.Column<long>(type: "bigint", nullable: false),
                    NormalLastRateLimitAt = table.Column<long>(type: "bigint", nullable: true),
                    QuotaLastRateLimitAt = table.Column<long>(type: "bigint", nullable: true),
                    NormalMissingRetryAfterCount = table.Column<int>(type: "integer", nullable: false),
                    QuotaMissingRetryAfterCount = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpotifyApiGateStates", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "SpotifyApiGateStates",
                columns: new[] { "Id", "NextRequestAt", "NormalLastRateLimitAt", "NormalMissingRetryAfterCount", "NotBefore", "NotBeforeIsQuotaExceeded", "QuotaLastRateLimitAt", "QuotaMissingRetryAfterCount", "UpdatedAt", "Version" },
                values: new object[] { 1, 0L, null, 0, 0L, false, null, 0, 0L, 0L });

            // Preserve an existing Spotify retry deadline when moving from the
            // playlist-link-only cooldown to this app-wide durable gate.
            migrationBuilder.Sql("""
                WITH spotify_cooldown AS (
                    SELECT "NextAttemptAt", "LastSyncStatus"
                    FROM "ServicePlaylistMappings"
                    WHERE "Service" = 'spotify'
                      AND "LastSyncStatus" IN ('rate_limited', 'quota_limited')
                      AND "NextAttemptAt" > CURRENT_TIMESTAMP
                    ORDER BY "NextAttemptAt" DESC, "Id"
                    LIMIT 1
                )
                UPDATE "SpotifyApiGateStates"
                SET "NotBefore" = COALESCE(
                        (SELECT CEIL(EXTRACT(EPOCH FROM "NextAttemptAt") * 10000000)::bigint
                            + 621355968000000000 FROM spotify_cooldown),
                        0),
                    "NotBeforeIsQuotaExceeded" = COALESCE(
                        (SELECT "LastSyncStatus" = 'quota_limited' FROM spotify_cooldown),
                        FALSE)
                WHERE "Id" = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SpotifyApiGateStates");
        }
    }
}
