using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class SeparateProviderEpisodeCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MediaObservationEpisodeOffsets");

            migrationBuilder.DropIndex(
                name: "IX_MediaProviderSeasonMappings_Provider_ProviderSeriesId_Prov~1",
                table: "MediaProviderSeasonMappings");

            migrationBuilder.DropIndex(
                name: "IX_MediaProviderSeasonMappings_Provider_ProviderSeriesId_Provi~",
                table: "MediaProviderSeasonMappings");

            migrationBuilder.Sql("""DELETE FROM "MediaProviderSeasonMappings" WHERE "HasConflict" = TRUE;""");

            migrationBuilder.DropColumn(
                name: "HasConflict",
                table: "MediaProviderSeasonMappings");

            migrationBuilder.AddColumn<string>(
                name: "NormalizedSeasonTitle",
                table: "MediaProviderSeasonMappings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastObservedAt",
                table: "MediaProviderSeasonMappings",
                type: "timestamp(6) with time zone",
                precision: 6,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProviderEpisodeEnd",
                table: "MediaProviderSeasonMappings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProviderEpisodeStart",
                table: "MediaProviderSeasonMappings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderSeasonKey",
                table: "MediaProviderSeasonMappings",
                type: "character varying(264)",
                maxLength: 264,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "MediaProviderEpisodeMappings",
                columns: table => new
                {
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderEpisodeId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    MediaTitleId = table.Column<Guid>(type: "uuid", nullable: false),
                    CanonicalEpisodeNumber = table.Column<int>(type: "integer", nullable: true),
                    MappingSource = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastObservedAt = table.Column<DateTimeOffset>(type: "timestamp(6) with time zone", precision: 6, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaProviderEpisodeMappings", x => new { x.Provider, x.ProviderEpisodeId });
                    table.CheckConstraint("CK_MediaProviderEpisodeMappings_Number", "\"CanonicalEpisodeNumber\" > 0");
                    table.CheckConstraint("CK_MediaProviderEpisodeMappings_Source", "\"MappingSource\" IN ('automatic', 'manual')");
                    table.ForeignKey(
                        name: "FK_MediaProviderEpisodeMappings_MediaTitles_MediaTitleId",
                        column: x => x.MediaTitleId,
                        principalTable: "MediaTitles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaProviderEpisodes",
                columns: table => new
                {
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderEpisodeId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ProviderSeriesId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ProviderSeasonId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ProviderSeasonNumber = table.Column<int>(type: "integer", nullable: true),
                    NormalizedSeasonTitle = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ProviderEpisodeNumber = table.Column<int>(type: "integer", nullable: true),
                    ProviderSequenceNumber = table.Column<int>(type: "integer", nullable: true),
                    ProviderUrlPath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    ContentKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    AudioLocale = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: true),
                    ReleaseTrack = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    EpisodeTitle = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    AvailableSubtitleLanguageCodes = table.Column<string[]>(type: "text[]", nullable: false),
                    AvailableAudioLanguageCodes = table.Column<string[]>(type: "text[]", nullable: false),
                    SeenCount = table.Column<int>(type: "integer", nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaProviderEpisodes", x => new { x.Provider, x.ProviderEpisodeId });
                    table.CheckConstraint("CK_MediaProviderEpisodes_SeenCount", "\"SeenCount\" > 0");
                });

            migrationBuilder.Sql("""
                UPDATE "MediaObservations"
                SET "ObservedAt" = LEAST("UpdatedAt", NOW())
                WHERE "ObservedAt" > LEAST("UpdatedAt", NOW());

                WITH ranked_watch_observations AS (
                    SELECT "Id", ROW_NUMBER() OVER (
                        PARTITION BY "UserId", LOWER(BTRIM("SiteIdentifier")), UPPER(BTRIM("SiteMediaId"))
                        ORDER BY "ObservedAt" DESC, "UpdatedAt" DESC, "Id" DESC
                    ) AS row_rank
                    FROM "MediaObservations"
                    WHERE LOWER("SiteIdentifier") = 'crunchyroll'
                        AND "SiteMediaId" IS NOT NULL AND "SiteMediaId" NOT LIKE 'catalog:%'
                )
                DELETE FROM "MediaObservations" observation
                USING ranked_watch_observations ranked
                WHERE observation."Id" = ranked."Id" AND ranked.row_rank > 1;

                UPDATE "MediaObservations"
                SET "SiteMediaId" = UPPER(BTRIM("SiteMediaId"))
                WHERE LOWER("SiteIdentifier") = 'crunchyroll'
                    AND "SiteMediaId" IS NOT NULL AND "SiteMediaId" NOT LIKE 'catalog:%'
                    AND "SiteMediaId" <> UPPER(BTRIM("SiteMediaId"));

                UPDATE "MediaProviderSeasonMappings"
                SET "Provider" = LOWER(BTRIM("Provider")),
                    "ProviderSeriesId" = UPPER(BTRIM("ProviderSeriesId")),
                    "ProviderSeasonId" = NULLIF(UPPER(BTRIM("ProviderSeasonId")), ''),
                    "ProviderSeasonKey" = COALESCE('id:' || NULLIF(UPPER(BTRIM("ProviderSeasonId")), ''),
                        'number:' || COALESCE(CAST("ProviderSeasonNumber" AS text), 'unknown:' || CAST("Id" AS text))),
                    "MappingSource" = CASE WHEN "MappingSource" = 'manual' THEN 'manual' ELSE 'automatic' END;

                DELETE FROM "MediaProviderSeasonMappings" mapping
                USING "MediaTitles" title
                WHERE mapping."Provider" = 'crunchyroll'
                    AND mapping."MediaTitleId" = title."Id"
                    AND (NOT title."SupportsEpisodeProgress"
                        OR title."IsCompletionOnly"
                        OR LOWER(title."PrimaryProgressDimension") <> 'episode'
                        OR LOWER(title."MediaKind") = 'manga'
                        OR LOWER(COALESCE(title."Format", '')) IN ('manga', 'novel', 'one_shot'));

                WITH collisions AS (
                    SELECT "Provider", "ProviderSeriesId", "ProviderSeasonKey",
                        COUNT(DISTINCT ("MediaTitleId", "EpisodeOffset"))
                            FILTER (WHERE "MappingSource" = 'manual') AS manual_targets,
                        COUNT(DISTINCT ("MediaTitleId", "EpisodeOffset")) AS all_targets
                    FROM "MediaProviderSeasonMappings"
                    GROUP BY "Provider", "ProviderSeriesId", "ProviderSeasonKey"
                    HAVING COUNT(*) > 1
                ), ranked AS (
                    SELECT mapping."Id", COALESCE(NULLIF(collisions.manual_targets, 0), collisions.all_targets) AS target_count,
                        ROW_NUMBER() OVER (
                            PARTITION BY mapping."Provider", mapping."ProviderSeriesId", mapping."ProviderSeasonKey"
                            ORDER BY (mapping."MappingSource" = 'manual') DESC, mapping."LastVerifiedAt" DESC, mapping."Id"
                        ) AS row_rank
                    FROM "MediaProviderSeasonMappings" mapping
                    JOIN collisions USING ("Provider", "ProviderSeriesId", "ProviderSeasonKey")
                )
                DELETE FROM "MediaProviderSeasonMappings" mapping
                USING ranked
                WHERE mapping."Id" = ranked."Id" AND (ranked.target_count > 1 OR ranked.row_rank > 1);

                UPDATE "MediaProviderSeasonMappings"
                SET "LastObservedAt" = LEAST("LastVerifiedAt", NOW())
                WHERE "MappingSource" = 'automatic';

                -- Case variants of a legacy ID can represent different assignments; exclude them before copying raw coordinates.
                WITH ambiguous_ids AS (
                    SELECT UPPER(BTRIM(identity."ProviderEpisodeId")) AS episode_id
                    FROM "MediaEpisodeProviderIdentities" identity
                    JOIN "MediaEpisodeProviderContents" content ON content."Id" = identity."MediaEpisodeProviderContentId"
                    JOIN "MediaEpisodes" episode ON episode."Id" = content."MediaEpisodeId"
                    WHERE LOWER(BTRIM(identity."Provider")) = 'crunchyroll'
                    GROUP BY UPPER(BTRIM(identity."ProviderEpisodeId"))
                    HAVING BOOL_OR(identity."HasConflict")
                        OR COUNT(DISTINCT (episode."MediaTitleId", episode."EpisodeNumber")) > 1
                )
                INSERT INTO "MediaProviderEpisodes"
                    ("Provider", "ProviderEpisodeId", "ProviderSeriesId", "ProviderSeasonId",
                     "ProviderSeasonNumber", "ProviderEpisodeNumber", "ProviderSequenceNumber",
                     "ProviderUrlPath", "ContentKey", "AudioLocale", "ReleaseTrack", "EpisodeTitle",
                     "AvailableSubtitleLanguageCodes", "AvailableAudioLanguageCodes",
                     "SeenCount", "FirstSeenAt", "LastSeenAt")
                SELECT identity."Provider", identity."ProviderEpisodeId", NULLIF(UPPER(BTRIM(content."ProviderSeriesId")), ''),
                    NULLIF(UPPER(BTRIM(content."ProviderSeasonId")), ''), content."ProviderSeasonNumber", content."ProviderEpisodeNumber",
                    content."ProviderSequenceNumber", identity."ProviderUrlPath", NULLIF(UPPER(BTRIM(content."ProviderContentKey")), ''),
                    identity."AudioLocale", identity."ReleaseTrack",
                    CASE WHEN NOT EXISTS (
                        SELECT 1 FROM "MediaEpisodeProviderContents" metadata
                        WHERE metadata."MediaEpisodeId" = episode."Id" AND metadata."Provider" <> 'crunchyroll'
                    ) THEN episode."Title" END,
                    CASE WHEN identity."ReleaseTrack" LIKE 'sub:%'
                        THEN ARRAY[split_part(identity."ReleaseTrack", ':', 2)] ELSE ARRAY[]::text[] END,
                    CASE WHEN identity."ReleaseTrack" LIKE 'dub:%'
                        THEN ARRAY[split_part(identity."ReleaseTrack", ':', 2)]
                        WHEN identity."ReleaseTrack" LIKE 'sub:%' OR identity."AudioLocale" IS NULL
                        THEN ARRAY[]::text[]
                        ELSE ARRAY[lower(split_part(identity."AudioLocale", '-', 1))] END,
                    identity."SeenCount", LEAST(identity."FirstSeenAt", NOW()), LEAST(identity."LastSeenAt", NOW())
                FROM "MediaEpisodeProviderIdentities" identity
                JOIN "MediaEpisodeProviderContents" content ON content."Id" = identity."MediaEpisodeProviderContentId"
                JOIN "MediaEpisodes" episode ON episode."Id" = content."MediaEpisodeId"
                WHERE identity."Provider" = 'crunchyroll'
                    AND identity."HasConflict" = FALSE
                    AND identity."ProviderEpisodeId" ~ '^[A-Z0-9]+$'
                    AND identity."ProviderUrlPath" = '/watch/' || identity."ProviderEpisodeId"
                    AND NOT EXISTS (SELECT 1 FROM ambiguous_ids WHERE episode_id = identity."ProviderEpisodeId");

                INSERT INTO "MediaProviderEpisodeMappings"
                    ("Provider", "ProviderEpisodeId", "MediaTitleId", "CanonicalEpisodeNumber",
                     "MappingSource", "CreatedAt", "UpdatedAt", "LastObservedAt")
                SELECT raw."Provider", raw."ProviderEpisodeId", title."Id",
                    CASE WHEN NOT title."IsCompletionOnly" THEN episode."EpisodeNumber" END,
                    CASE WHEN season."Id" IS NOT NULL THEN 'manual' ELSE 'automatic' END,
                    raw."FirstSeenAt", raw."LastSeenAt",
                    CASE WHEN season."Id" IS NULL THEN raw."LastSeenAt" END
                FROM "MediaProviderEpisodes" raw
                JOIN "MediaEpisodeProviderIdentities" identity USING ("Provider", "ProviderEpisodeId")
                JOIN "MediaEpisodeProviderContents" content ON content."Id" = identity."MediaEpisodeProviderContentId"
                JOIN "MediaEpisodes" episode ON episode."Id" = content."MediaEpisodeId"
                JOIN "MediaTitles" title ON title."Id" = episode."MediaTitleId"
                -- Complete missing coordinates from unambiguous ContentKey siblings to match runtime playback resolution.
                LEFT JOIN LATERAL (
                    SELECT
                        COALESCE(raw."ProviderSeriesId", sibling.series_id) AS series_id,
                        COALESCE(raw."ProviderSeasonId", sibling.season_id) AS season_id,
                        COALESCE(raw."ProviderSeasonNumber", sibling.season_number) AS season_number,
                        COALESCE(raw."ProviderEpisodeNumber", sibling.episode_number) AS episode_number
                    FROM (
                        SELECT
                            grouped.series_id, grouped.season_id, grouped.season_number, grouped.episode_number,
                            COUNT(*) OVER () AS group_count
                        FROM (
                            SELECT
                                s."ProviderSeriesId" AS series_id,
                                s."ProviderSeasonId" AS season_id,
                                s."ProviderSeasonNumber" AS season_number,
                                CASE WHEN COUNT(DISTINCT s."ProviderEpisodeNumber")
                                        FILTER (WHERE s."ProviderEpisodeNumber" IS NOT NULL) = 1
                                    THEN MIN(s."ProviderEpisodeNumber") END AS episode_number
                            FROM "MediaProviderEpisodes" s
                            WHERE raw."ContentKey" IS NOT NULL
                                AND (raw."ProviderSeriesId" IS NULL OR raw."ProviderSeasonId" IS NULL
                                    OR raw."ProviderEpisodeNumber" IS NULL)
                                AND s."Provider" = raw."Provider"
                                AND s."ContentKey" = raw."ContentKey"
                                AND s."ProviderEpisodeId" <> raw."ProviderEpisodeId"
                                AND (s."ProviderSeasonId" IS NOT NULL
                                    OR (s."ProviderSeriesId" IS NOT NULL AND s."ProviderSeasonNumber" > 0))
                                AND (raw."ProviderSeriesId" IS NULL OR s."ProviderSeriesId" = raw."ProviderSeriesId")
                                AND (raw."ProviderSeasonId" IS NULL OR s."ProviderSeasonId" IS NULL
                                    OR s."ProviderSeasonId" = raw."ProviderSeasonId")
                                AND (raw."ProviderSeasonNumber" IS NULL OR s."ProviderSeasonNumber" IS NULL
                                    OR s."ProviderSeasonNumber" = raw."ProviderSeasonNumber")
                                AND (raw."ProviderEpisodeNumber" IS NULL OR s."ProviderEpisodeNumber" IS NULL
                                    OR s."ProviderEpisodeNumber" = raw."ProviderEpisodeNumber")
                            GROUP BY s."ProviderSeriesId", s."ProviderSeasonId", s."ProviderSeasonNumber"
                        ) grouped
                    ) sibling
                    WHERE sibling.group_count = 1
                ) completed ON TRUE
                CROSS JOIN LATERAL (
                    SELECT
                        COALESCE(completed.series_id, raw."ProviderSeriesId") AS series_id,
                        COALESCE(completed.season_id, raw."ProviderSeasonId") AS season_id,
                        COALESCE(completed.season_number, raw."ProviderSeasonNumber") AS season_number,
                        COALESCE(completed.episode_number, raw."ProviderEpisodeNumber") AS episode_number
                ) eff
                -- Resolve the winning season before comparing its assignment with the legacy identity.
                LEFT JOIN LATERAL (
                    SELECT candidate."Id", candidate."MediaTitleId", candidate."MappingSource", candidate.canonical_number
                    FROM (
                        SELECT mapping."Id", mapping."MediaTitleId", mapping."MappingSource",
                            eff.episode_number::bigint + mapping."EpisodeOffset" AS canonical_number,
                            priority.rank, COUNT(*) OVER (PARTITION BY priority.rank) AS matches
                        FROM "MediaProviderSeasonMappings" mapping
                        JOIN "MediaTitles" target ON target."Id" = mapping."MediaTitleId"
                        CROSS JOIN LATERAL (
                            SELECT
                                mapping."ProviderSeasonKey" = COALESCE('id:' || eff.season_id,
                                    'number:' || CASE WHEN eff.season_number > 0 THEN eff.season_number::text END) AS exact,
                                eff.season_number > 0 AND mapping."ProviderSeasonNumber" = eff.season_number
                                    AND (eff.season_id IS NULL OR mapping."ProviderSeasonId" IS NULL
                                        OR mapping."ProviderSeasonId" = eff.season_id) AS ordinal
                        ) coordinates
                        CROSS JOIN LATERAL (VALUES
                            (1, coordinates.exact AND mapping."MappingSource" = 'manual'),
                            (2, coordinates.ordinal AND mapping."MappingSource" = 'manual'),
                            (3, coordinates.exact),
                            (4, coordinates.ordinal),
                            (5, eff.season_id IS NULL AND eff.season_number IS NULL
                                AND NOT EXISTS (
                                    SELECT 1 FROM "MediaProviderSeasonMappings" other
                                    WHERE other."Provider" = raw."Provider"
                                        AND other."ProviderSeriesId" = eff.series_id
                                        AND other."ProviderSeasonKey" <> mapping."ProviderSeasonKey"
                                )
                                AND NOT EXISTS (
                                    SELECT 1 FROM "MediaProviderEpisodes" known
                                    WHERE known."Provider" = raw."Provider" AND known."ProviderSeriesId" = eff.series_id
                                    HAVING COUNT(DISTINCT known."ProviderSeasonId") > 1
                                        OR COUNT(DISTINCT known."ProviderSeasonNumber")
                                            FILTER (WHERE known."ProviderSeasonNumber" > 0) > 1
                                ))
                        ) priority(rank, eligible)
                        WHERE mapping."Provider" = raw."Provider"
                            AND mapping."ProviderSeriesId" = eff.series_id
                            AND priority.eligible
                            AND NOT target."IsCompletionOnly"
                            AND eff.episode_number
                                BETWEEN COALESCE(mapping."ProviderEpisodeStart", 1)
                                    AND COALESCE(mapping."ProviderEpisodeEnd", 2147483647)
                            AND eff.episode_number::bigint + mapping."EpisodeOffset"
                                BETWEEN 1 AND COALESCE(NULLIF(GREATEST(target."EpisodeCount", 0), 0), 2147483647)
                    ) candidate
                    WHERE candidate.matches = 1
                    ORDER BY candidate.rank
                    LIMIT 1
                ) season ON TRUE
                WHERE identity."IsTrusted" = TRUE
                    AND LOWER(title."MediaKind") IN ('anime', 'series', 'movie')
                    AND LOWER(COALESCE(title."Format", '')) NOT IN ('manga', 'novel', 'one_shot')
                    AND LOWER(title."PrimaryProgressDimension") NOT IN ('chapter', 'volume')
                    AND (title."IsCompletionOnly" OR (
                        title."SupportsEpisodeProgress"
                        AND LOWER(title."PrimaryProgressDimension") = 'episode'
                        AND episode."EpisodeNumber" > 0
                        AND (COALESCE(title."EpisodeCount", 0) <= 0 OR episode."EpisodeNumber" <= title."EpisodeCount")))
                    AND (season."Id" IS NULL OR (season."MappingSource" = 'automatic'
                        AND (title."IsCompletionOnly" OR season."MediaTitleId" <> title."Id"
                            OR season.canonical_number <> episode."EpisodeNumber")));

                -- AnimeSchedule also wrote English availability to canonical episodes.
                -- Keep that evidence because its source cannot be recovered from the old arrays.
                UPDATE "MediaEpisodes" episode
                SET "Title" = CASE WHEN EXISTS (
                        SELECT 1 FROM "MediaEpisodeProviderContents" metadata
                        WHERE metadata."MediaEpisodeId" = episode."Id" AND metadata."Provider" <> 'crunchyroll'
                    ) THEN episode."Title" END,
                    "AvailableSubtitleLanguageCodes" = ARRAY(SELECT code FROM unnest(episode."AvailableSubtitleLanguageCodes") code WHERE lower(code) = 'en'),
                    "AvailableAudioLanguageCodes" = ARRAY(SELECT code FROM unnest(episode."AvailableAudioLanguageCodes") code WHERE lower(code) = 'en')
                WHERE EXISTS (SELECT 1 FROM "MediaEpisodeProviderContents" content
                    WHERE content."MediaEpisodeId" = episode."Id" AND content."Provider" = 'crunchyroll');

                DELETE FROM "MediaEpisodeProviderContents" WHERE "Provider" = 'crunchyroll';
                """);

            migrationBuilder.DropTable(
                name: "MediaEpisodeProviderIdentities");

            migrationBuilder.CreateIndex(
                name: "IX_MediaProviderSeasonMappings_Provider_ProviderSeriesId_Prov~1",
                table: "MediaProviderSeasonMappings",
                columns: new[] { "Provider", "ProviderSeriesId", "ProviderSeasonNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaProviderSeasonMappings_Provider_ProviderSeriesId_Prov~2",
                table: "MediaProviderSeasonMappings",
                columns: new[] { "Provider", "ProviderSeriesId", "ProviderSeasonKey", "ProviderEpisodeEnd" },
                unique: true,
                filter: "\"ProviderEpisodeStart\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MediaProviderSeasonMappings_Provider_ProviderSeriesId_Prov~3",
                table: "MediaProviderSeasonMappings",
                columns: new[] { "Provider", "ProviderSeriesId", "ProviderSeasonKey", "ProviderEpisodeStart" },
                unique: true,
                filter: "\"ProviderEpisodeEnd\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MediaProviderSeasonMappings_Provider_ProviderSeriesId_Prov~4",
                table: "MediaProviderSeasonMappings",
                columns: new[] { "Provider", "ProviderSeriesId", "ProviderSeasonKey", "ProviderEpisodeStart", "ProviderEpisodeEnd" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaProviderSeasonMappings_Provider_ProviderSeriesId_Provi~",
                table: "MediaProviderSeasonMappings",
                columns: new[] { "Provider", "ProviderSeriesId", "ProviderSeasonKey" },
                unique: true,
                filter: "\"ProviderEpisodeStart\" IS NULL AND \"ProviderEpisodeEnd\" IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MediaProviderSeasonMappings_Range",
                table: "MediaProviderSeasonMappings",
                sql: "COALESCE(\"ProviderEpisodeStart\", 1) >= 1 AND COALESCE(\"ProviderEpisodeEnd\", 2147483647) >= COALESCE(\"ProviderEpisodeStart\", 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MediaProviderSeasonMappings_Source",
                table: "MediaProviderSeasonMappings",
                sql: "\"MappingSource\" IN ('automatic', 'manual')");

            migrationBuilder.CreateIndex(
                name: "IX_MediaProviderEpisodeMappings_MediaTitleId",
                table: "MediaProviderEpisodeMappings",
                column: "MediaTitleId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaProviderEpisodes_Provider_ContentKey",
                table: "MediaProviderEpisodes",
                columns: new[] { "Provider", "ContentKey" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaProviderEpisodes_Provider_ProviderSeriesId",
                table: "MediaProviderEpisodes",
                columns: new[] { "Provider", "ProviderSeriesId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Restore the pre-migration database backup to recover legacy episode assignments and private offsets.");
        }
    }
}
