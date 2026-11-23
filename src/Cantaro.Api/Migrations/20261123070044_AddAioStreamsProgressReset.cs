using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    public partial class AddAioStreamsProgressReset : Migration
    {
        internal const string InstallTriggerSql = """
            CREATE FUNCTION "MarkAioStreamsProgressReset"() RETURNS trigger
            LANGUAGE plpgsql AS $function$
            BEGIN
                IF NEW."LastMutationSource" = 'user_status_update'
                    AND NOT EXISTS (
                        SELECT 1 FROM "MediaTitles" WHERE "Id" = NEW."MediaTitleId"
                            AND ("MediaKind" = 'movie' OR ("MediaKind" = 'anime' AND "Format" = 'movie')))
                THEN
                    RETURN NEW;
                END IF;

                NEW."AioStreamsProgressResetAt" := GREATEST(OLD."AioStreamsProgressResetAt",
                    floor(extract(epoch FROM NEW."UpdatedAt"))::bigint);
                RETURN NEW;
            END;
            $function$;

            CREATE TRIGGER "MarkAioStreamsProgressReset"
                BEFORE UPDATE OF "ProgressEpisodes", "Status" ON "MediaLibraryEntries"
                FOR EACH ROW
                WHEN ((NEW."LastMutationSource" = 'user_progress_update'
                    AND GREATEST(NEW."ProgressEpisodes", 0) < GREATEST(OLD."ProgressEpisodes", 0))
                    OR (NEW."LastMutationSource" = 'user_status_update' AND OLD."Status" = 'completed'
                        AND NEW."Status" IS DISTINCT FROM 'completed'))
                EXECUTE FUNCTION "MarkAioStreamsProgressReset"();
            """;

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AioStreamsProgressResetAt",
                table: "MediaLibraryEntries",
                type: "bigint",
                nullable: true);

            migrationBuilder.Sql(InstallTriggerSql);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER "MarkAioStreamsProgressReset" ON "MediaLibraryEntries";
                DROP FUNCTION "MarkAioStreamsProgressReset"();
                """);

            migrationBuilder.DropColumn(
                name: "AioStreamsProgressResetAt",
                table: "MediaLibraryEntries");
        }
    }
}
