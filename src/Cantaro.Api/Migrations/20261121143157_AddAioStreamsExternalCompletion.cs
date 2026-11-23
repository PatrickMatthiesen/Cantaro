using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

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
                previous_progress integer := GREATEST(OLD."ProgressEpisodes", 0);
                episode_progress_increased boolean := COALESCE(NEW."ProgressEpisodes", 0) > previous_progress;
                movie_completed boolean;
                completion_at bigint := floor(extract(epoch FROM NEW."UpdatedAt"))::bigint;
            BEGIN
                movie_completed := NEW."Status" = 'completed' AND OLD."Status" IS DISTINCT FROM 'completed'
                    AND EXISTS (
                        SELECT 1 FROM "MediaTitles"
                        WHERE "Id" = NEW."MediaTitleId"
                            AND ("MediaKind" = 'movie' OR ("MediaKind" = 'anime' AND "Format" = 'movie')));
                IF NOT (episode_progress_increased OR movie_completed) THEN
                    RETURN NULL;
                END IF;

                IF episode_progress_increased THEN
                    INSERT INTO "AioStreamsExternalCompletions"
                        ("MediaLibraryEntryId", "FirstEpisode", "LastEpisode", "At")
                    VALUES (NEW."Id", previous_progress + 1, NEW."ProgressEpisodes", completion_at);
                END IF;

                IF movie_completed THEN
                    INSERT INTO "AioStreamsExternalCompletions" ("MediaLibraryEntryId", "At")
                    VALUES (NEW."Id", completion_at);
                END IF;

                RETURN NULL;
            END;
            $function$;

            CREATE TRIGGER "MarkAioStreamsExternalCompletion"
                AFTER INSERT OR UPDATE OF "Status", "ProgressEpisodes" ON "MediaLibraryEntries"
                FOR EACH ROW
                WHEN (NEW."LastMutationSource" IS DISTINCT FROM 'aiostreams_watch_state')
                EXECUTE FUNCTION "MarkAioStreamsExternalCompletion"();
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AioStreamsExternalCompletions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MediaLibraryEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstEpisode = table.Column<int>(type: "integer", nullable: true),
                    LastEpisode = table.Column<int>(type: "integer", nullable: true),
                    At = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AioStreamsExternalCompletions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AioStreamsExternalCompletions_MediaLibraryEntries_MediaLibr~",
                        column: x => x.MediaLibraryEntryId,
                        principalTable: "MediaLibraryEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AioStreamsExternalCompletions_MediaLibraryEntryId",
                table: "AioStreamsExternalCompletions",
                column: "MediaLibraryEntryId");

            migrationBuilder.Sql(InstallTriggerSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER "MarkAioStreamsExternalCompletion" ON "MediaLibraryEntries";
                DROP FUNCTION "MarkAioStreamsExternalCompletion"();
                """);

            migrationBuilder.DropTable(name: "AioStreamsExternalCompletions");
        }
    }
}
