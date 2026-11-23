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
                NEW."AioStreamsProgressResetAt" := GREATEST(OLD."AioStreamsProgressResetAt",
                    floor(extract(epoch FROM NEW."UpdatedAt"))::bigint);
                RETURN NEW;
            END;
            $function$;

            CREATE TRIGGER "MarkAioStreamsProgressReset"
                BEFORE UPDATE OF "ProgressEpisodes" ON "MediaLibraryEntries"
                FOR EACH ROW
                WHEN (NEW."LastMutationSource" = 'user_progress_update'
                    AND GREATEST(NEW."ProgressEpisodes", 0) < GREATEST(OLD."ProgressEpisodes", 0))
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
