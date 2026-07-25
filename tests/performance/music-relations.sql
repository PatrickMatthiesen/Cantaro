\set ON_ERROR_STOP on
\timing on

BEGIN;

CREATE TEMP TABLE "Songs" (
    "Id" uuid PRIMARY KEY
) ON COMMIT DROP;

CREATE TEMP TABLE "Tracks" (
    "Id" uuid PRIMARY KEY,
    "VersionFlags" bigint NOT NULL DEFAULT 0
) ON COMMIT DROP;

CREATE TEMP TABLE "SongTracks" (
    "SongId" uuid NOT NULL,
    "TrackId" uuid NOT NULL,
    PRIMARY KEY ("SongId", "TrackId")
) ON COMMIT DROP;
CREATE INDEX ON "SongTracks" ("TrackId", "SongId");

CREATE TEMP TABLE "TrackSourceIds" (
    "Id" uuid PRIMARY KEY,
    "TrackId" uuid NOT NULL,
    "SourceType" text NOT NULL,
    "ExternalId" text NOT NULL,
    "PresentationKind" smallint NOT NULL DEFAULT 0,
    UNIQUE ("SourceType", "ExternalId")
) ON COMMIT DROP;
CREATE INDEX ON "TrackSourceIds" ("TrackId");

CREATE TEMP TABLE "TrackArtistCredits" (
    "Id" uuid PRIMARY KEY,
    "TrackId" uuid NOT NULL,
    "Position" integer NOT NULL,
    "CreditedName" text NOT NULL
) ON COMMIT DROP;
CREATE INDEX ON "TrackArtistCredits" ("TrackId", "Position");

CREATE TEMP TABLE "PlaylistEntries" (
    "Id" uuid PRIMARY KEY,
    "PlaylistId" uuid NOT NULL,
    "TrackId" uuid NOT NULL,
    "Position" integer NOT NULL
) ON COMMIT DROP;
CREATE INDEX ON "PlaylistEntries" ("PlaylistId", "Position");

INSERT INTO "Songs" ("Id")
SELECT md5('song:' || value)::uuid
FROM generate_series(1, 10000) AS value;

INSERT INTO "Tracks" ("Id", "VersionFlags")
SELECT
    md5('track:' || value)::uuid,
    CASE WHEN value % 10 = 0 THEN 3 ELSE 0 END
FROM generate_series(1, 100000) AS value;

INSERT INTO "SongTracks" ("SongId", "TrackId")
SELECT
    md5('song:' || (((value - 1) / 10) + 1))::uuid,
    md5('track:' || value)::uuid
FROM generate_series(1, 100000) AS value;

-- A representative mashup that realizes two compositions.
INSERT INTO "SongTracks" ("SongId", "TrackId")
VALUES (md5('song:2')::uuid, md5('track:1')::uuid);

INSERT INTO "TrackSourceIds" ("Id", "TrackId", "SourceType", "ExternalId")
SELECT
    md5('source:' || value)::uuid,
    md5('track:' || value)::uuid,
    'youtube',
    'video-' || value
FROM generate_series(1, 100000) AS value;

INSERT INTO "TrackArtistCredits" ("Id", "TrackId", "Position", "CreditedName")
SELECT
    md5('credit:' || value)::uuid,
    md5('track:' || value)::uuid,
    0,
    'Artist ' || value
FROM generate_series(1, 100000) AS value;

INSERT INTO "PlaylistEntries" ("Id", "PlaylistId", "TrackId", "Position")
SELECT
    md5('entry:' || value)::uuid,
    md5('playlist:1')::uuid,
    md5('track:' || value)::uuid,
    value - 1
FROM generate_series(1, 10000) AS value;

ANALYZE "Songs";
ANALYZE "Tracks";
ANALYZE "SongTracks";
ANALYZE "TrackSourceIds";
ANALYZE "TrackArtistCredits";
ANALYZE "PlaylistEntries";

EXPLAIN (ANALYZE, BUFFERS)
SELECT "TrackId"
FROM "TrackSourceIds"
WHERE "SourceType" = 'youtube' AND "ExternalId" = 'video-50000';

EXPLAIN (ANALYZE, BUFFERS)
SELECT t."Id", t."VersionFlags", c."Position", c."CreditedName", s."ExternalId"
FROM "Tracks" AS t
LEFT JOIN "TrackArtistCredits" AS c ON c."TrackId" = t."Id"
LEFT JOIN "TrackSourceIds" AS s ON s."TrackId" = t."Id"
WHERE t."Id" = md5('track:50000')::uuid;

EXPLAIN (ANALYZE, BUFFERS)
SELECT "TrackId"
FROM "SongTracks"
WHERE "SongId" = md5('song:5000')::uuid;

EXPLAIN (ANALYZE, BUFFERS)
SELECT "SongId"
FROM "SongTracks"
WHERE "TrackId" = md5('track:1')::uuid;

EXPLAIN (ANALYZE, BUFFERS)
SELECT e."Position", t."Id", c."CreditedName", s."ExternalId"
FROM "PlaylistEntries" AS e
JOIN "Tracks" AS t ON t."Id" = e."TrackId"
LEFT JOIN "TrackArtistCredits" AS c ON c."TrackId" = t."Id"
LEFT JOIN "TrackSourceIds" AS s ON s."TrackId" = t."Id"
WHERE e."PlaylistId" = md5('playlist:1')::uuid
ORDER BY e."Position"
LIMIT 50;

CREATE TEMP TABLE benchmark_samples (
    scenario text NOT NULL,
    elapsed_ms double precision NOT NULL
) ON COMMIT DROP;

DO $$
DECLARE
    started_at timestamptz;
    iteration integer;
BEGIN
    FOR iteration IN 1..500 LOOP
        started_at := clock_timestamp();
        PERFORM "TrackId" FROM "TrackSourceIds"
        WHERE "SourceType" = 'youtube' AND "ExternalId" = 'video-50000';
        INSERT INTO benchmark_samples VALUES
            ('provider_lookup', 1000 * EXTRACT(epoch FROM clock_timestamp() - started_at));

        started_at := clock_timestamp();
        PERFORM "TrackId" FROM "SongTracks"
        WHERE "SongId" = md5('song:5000')::uuid;
        INSERT INTO benchmark_samples VALUES
            ('song_versions', 1000 * EXTRACT(epoch FROM clock_timestamp() - started_at));

        started_at := clock_timestamp();
        PERFORM "SongId" FROM "SongTracks"
        WHERE "TrackId" = md5('track:1')::uuid;
        INSERT INTO benchmark_samples VALUES
            ('track_songs', 1000 * EXTRACT(epoch FROM clock_timestamp() - started_at));
    END LOOP;
END $$;

SELECT
    scenario,
    round(percentile_cont(0.5) WITHIN GROUP (ORDER BY elapsed_ms)::numeric, 4) AS median_ms,
    round(percentile_cont(0.95) WITHIN GROUP (ORDER BY elapsed_ms)::numeric, 4) AS p95_ms
FROM benchmark_samples
GROUP BY scenario
ORDER BY scenario;

ROLLBACK;
