using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCompletedObservationMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Queued provider updates are self-contained; observation provenance
            // was used only for logging and is unnecessary for execution/retry.
            migrationBuilder.Sql("""
                UPDATE "MediaProviderOperations"
                SET "PayloadJson" = ("PayloadJson"::jsonb - ARRAY[
                    'triggeredByObservationId', 'observedSiteIdentifier',
                    'observedProgressHint', 'observationMatchScore', 'triggeredAt',
                    'TriggeredByObservationId', 'ObservedSiteIdentifier',
                    'ObservedProgressHint', 'ObservationMatchScore', 'TriggeredAt'
                ])::text
                WHERE "OperationType" = 'auto_progress_update'
                  AND CASE WHEN pg_input_is_valid("PayloadJson", 'jsonb')
                      THEN jsonb_typeof("PayloadJson"::jsonb) = 'object'
                      ELSE false END;

                DELETE FROM "MediaObservations" WHERE "MatchStatus" = 'rejected';
                """);

            migrationBuilder.DropColumn(
                name: "ExtensionVersion",
                table: "MediaObservations");

            migrationBuilder.DropColumn(
                name: "ResolutionHistoryPayload",
                table: "MediaObservations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExtensionVersion",
                table: "MediaObservations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolutionHistoryPayload",
                table: "MediaObservations",
                type: "text",
                nullable: true);
        }
    }
}
