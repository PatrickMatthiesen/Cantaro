using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations;

public partial class MinimizeMediaObservationStorage
{
    private static void BackfillEvidence(MigrationBuilder migrationBuilder)
    {
        // PostgreSQL executes this with the schema changes in one transaction.
        // The temporary helpers cannot leak into the application schema.
        migrationBuilder.Sql("""
            CREATE FUNCTION pg_temp.observation_json(value text) RETURNS jsonb
            LANGUAGE plpgsql AS $fn$
            DECLARE parsed jsonb;
            BEGIN
                parsed := value::jsonb;
                RETURN CASE WHEN jsonb_typeof(parsed) = 'object' THEN parsed END;
            EXCEPTION WHEN invalid_text_representation OR numeric_value_out_of_range THEN
                RETURN NULL;
            END $fn$;

            CREATE FUNCTION pg_temp.observation_text(value jsonb) RETURNS text
            LANGUAGE sql IMMUTABLE AS $fn$
                SELECT CASE WHEN jsonb_typeof(value) = 'string' THEN NULLIF(btrim(value #>> '{}'), '') END
            $fn$;

            CREATE FUNCTION pg_temp.observation_integer(value jsonb) RETURNS integer
            LANGUAGE plpgsql AS $fn$
            BEGIN
                IF jsonb_typeof(value) <> 'number' THEN RETURN NULL; END IF;
                RETURN (value #>> '{}')::integer;
            EXCEPTION WHEN invalid_text_representation OR numeric_value_out_of_range THEN
                RETURN NULL;
            END $fn$;

            CREATE FUNCTION pg_temp.observation_url(value text) RETURNS text
            LANGUAGE sql IMMUTABLE AS $fn$
                SELECT regexp_replace(split_part(split_part(btrim(value), '?', 1), '#', 1),
                    '^([a-zA-Z][a-zA-Z0-9+.-]*://)[^/]*@', '\1')
            $fn$;

            CREATE FUNCTION pg_temp.observation_languages(value jsonb) RETURNS text[]
            LANGUAGE sql IMMUTABLE AS $fn$
                SELECT COALESCE(array_agg(DISTINCT lower(btrim(item #>> '{}')) ORDER BY lower(btrim(item #>> '{}'))), ARRAY[]::text[])
                FROM jsonb_array_elements(CASE WHEN jsonb_typeof(value) = 'array' THEN value ELSE '[]'::jsonb END) AS item
                WHERE jsonb_typeof(item) = 'string' AND (item #>> '{}') ~ '^[a-zA-Z]{2,3}(-[a-zA-Z0-9]{2,8})*$'
            $fn$;

            CREATE TEMP TABLE observation_backfill ON COMMIT DROP AS
                SELECT "Id", pg_temp.observation_json("RawPayload") AS body FROM "MediaObservations";

            UPDATE "MediaObservations" o SET
                    "SeriesTitle" = pg_temp.observation_text(p.body -> 'seriesTitle'),
                    "SeasonTitle" = pg_temp.observation_text(p.body -> 'seasonTitle'),
                    "EpisodeTitle" = pg_temp.observation_text(p.body -> 'episodeTitle'),
                    "ProviderSeriesId" = pg_temp.observation_text(p.body -> 'providerSeriesId'),
                    "ProviderSeasonId" = pg_temp.observation_text(p.body -> 'providerSeasonId'),
                    "ReleaseTrack" = pg_temp.observation_text(p.body -> 'releaseTrack'),
                    "NextEpisodeProviderId" = pg_temp.observation_text(p.body -> 'nextEpisodeProviderId'),
                    "NextEpisodeTitle" = pg_temp.observation_text(p.body -> 'nextEpisodeTitle'),
                    "NextEpisodeReleaseTrack" = pg_temp.observation_text(p.body -> 'nextEpisodeReleaseTrack'),
                    "EpisodeNumber" = pg_temp.observation_integer(p.body -> 'episodeNumber'),
                    "SeasonNumber" = pg_temp.observation_integer(p.body -> 'seasonNumber'),
                    "ProviderSequenceNumber" = pg_temp.observation_integer(p.body -> 'providerSequenceNumber'),
                    "NextEpisodeNumber" = pg_temp.observation_integer(p.body -> 'nextEpisodeNumber'),
                    "NextEpisodeUrl" = pg_temp.observation_url(pg_temp.observation_text(p.body -> 'nextEpisodeUrl')),
                    "IsCatalogObservation" = COALESCE(p.body ? 'episodes', false)
                FROM observation_backfill p WHERE o."Id" = p."Id";

            INSERT INTO "MediaObservationEpisode"
                ("Id", "MediaObservationId", "ProviderEpisodeId", "ProviderUrl", "EpisodeNumber",
                 "EpisodeTitle", "ReleaseTrack", "AvailableSubtitleLanguageCodes", "AvailableAudioLanguageCodes")
                SELECT gen_random_uuid(), p."Id",
                    COALESCE(pg_temp.observation_text(e.item -> 'providerEpisodeId'), ''),
                    COALESCE(pg_temp.observation_url(pg_temp.observation_text(e.item -> 'providerUrl')), ''),
                    pg_temp.observation_integer(e.item -> 'episodeNumber'),
                    pg_temp.observation_text(e.item -> 'episodeTitle'),
                    pg_temp.observation_text(e.item -> 'releaseTrack'),
                    pg_temp.observation_languages(e.item -> 'availableSubtitleLanguageCodes'),
                    pg_temp.observation_languages(e.item -> 'availableAudioLanguageCodes')
                FROM observation_backfill p
                CROSS JOIN LATERAL jsonb_array_elements(
                    CASE WHEN jsonb_typeof(p.body -> 'episodes') = 'array' THEN p.body -> 'episodes'
                         WHEN jsonb_typeof(p.body -> 'observedEpisodes') = 'array' THEN p.body -> 'observedEpisodes'
                         ELSE '[]'::jsonb END) WITH ORDINALITY AS e(item, ordinal)
                WHERE e.ordinal <= 2000 AND pg_temp.observation_integer(e.item -> 'episodeNumber') > 0;

            UPDATE "MediaObservations"
                SET "ObservedUrl" = pg_temp.observation_url("ObservedUrl"),
                    "LastMatchError" = CASE WHEN "LastMatchError" IS NOT NULL THEN 'Historical matching failure; retry for current status.' END;
            UPDATE "MediaEpisodeProviderIdentities"
                SET "ProviderUrlPath" = pg_temp.observation_url("ProviderUrlPath");

            DROP TABLE observation_backfill;
            DROP FUNCTION pg_temp.observation_json(text);
            DROP FUNCTION pg_temp.observation_integer(jsonb);
            DROP FUNCTION pg_temp.observation_text(jsonb);
            DROP FUNCTION pg_temp.observation_url(text);
            DROP FUNCTION pg_temp.observation_languages(jsonb);
            """);
    }
}
