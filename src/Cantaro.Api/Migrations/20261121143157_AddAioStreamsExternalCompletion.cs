using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAioStreamsExternalCompletion : Migration
    {
        internal const string InstallTriggerSql = """
            CREATE FUNCTION "MarkAioStreamsExternalCompletion"() RETURNS trigger
            LANGUAGE plpgsql AS $function$
            DECLARE
                completion_at bigint := floor(extract(epoch FROM NEW."UpdatedAt"))::bigint;
            BEGIN
                IF NEW."ProgressEpisodes" > COALESCE(OLD."ProgressEpisodes", 0) THEN
                    UPDATE "AioStreamsWatchEvents" AS receipt
                    SET "ExternalCompletionAt" = GREATEST(receipt."ExternalCompletionAt", completion_at)
                    FROM "MediaEpisodes" AS episode
                    WHERE receipt."MediaLibraryEntryId" = NEW."Id"
                        AND receipt."MediaEpisodeId" = episode."Id"
                        AND episode."MediaTitleId" = NEW."MediaTitleId"
                        AND episode."EpisodeNumber" > COALESCE(OLD."ProgressEpisodes", 0)
                        AND episode."EpisodeNumber" <= NEW."ProgressEpisodes";
                END IF;

                IF NEW."Status" = 'completed' AND OLD."Status" IS DISTINCT FROM 'completed' THEN
                    UPDATE "AioStreamsWatchEvents" AS receipt
                    SET "ExternalCompletionAt" = GREATEST(receipt."ExternalCompletionAt", completion_at)
                    WHERE receipt."MediaLibraryEntryId" = NEW."Id"
                        AND receipt."MediaEpisodeId" IS NULL
                        AND receipt."Type" = 'movie';
                END IF;

                RETURN NULL;
            END;
            $function$;

            CREATE TRIGGER "MarkAioStreamsExternalCompletion"
                AFTER UPDATE OF "Status", "ProgressEpisodes" ON "MediaLibraryEntries"
                FOR EACH ROW
                WHEN (NEW."LastMutationSource" IS DISTINCT FROM 'aiostreams_watch_state'
                    AND (NEW."ProgressEpisodes" > COALESCE(OLD."ProgressEpisodes", 0)
                        OR (NEW."Status" = 'completed' AND OLD."Status" IS DISTINCT FROM 'completed')))
                EXECUTE FUNCTION "MarkAioStreamsExternalCompletion"();
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ExternalCompletionAt",
                table: "AioStreamsWatchEvents",
                type: "bigint",
                nullable: true);

            migrationBuilder.Sql(InstallTriggerSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER "MarkAioStreamsExternalCompletion" ON "MediaLibraryEntries";
                DROP FUNCTION "MarkAioStreamsExternalCompletion"();
                """);

            migrationBuilder.DropColumn(
                name: "ExternalCompletionAt",
                table: "AioStreamsWatchEvents");
        }
    }
}
