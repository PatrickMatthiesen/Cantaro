namespace Cantaro.Api.Migrations;

// These statements run once, inside the reduction migration transaction.
internal static class CatalogReductionData
{
    internal const string Stage = """
        DO $$ BEGIN
          IF to_regclass('pg_temp.cantaro_proof_conversion') IS NULL
             AND EXISTS (SELECT 1 FROM "ServicePlaylistMappings"
               WHERE "MatchingProgressJson" IS NOT NULL AND "MatchingProgressJson" <> '{}') THEN
            RAISE EXCEPTION 'Run Cantaro.MigrationService for the catalog reduction. Saved matching choices require transactional fingerprint conversion.';
          END IF;
        END $$;
        CREATE FUNCTION pg_temp.cr_json(value text) RETURNS jsonb LANGUAGE plpgsql AS $$
        BEGIN RETURN value::jsonb; EXCEPTION WHEN invalid_text_representation THEN RETURN NULL; END $$;
        CREATE FUNCTION pg_temp.cr_uuid(value text) RETURNS uuid LANGUAGE plpgsql AS $$
        BEGIN RETURN value::uuid; EXCEPTION WHEN invalid_text_representation THEN RETURN NULL; END $$;
        CREATE FUNCTION pg_temp.cr_credit_ids(value jsonb) RETURNS jsonb LANGUAGE sql AS $$
          SELECT COALESCE(jsonb_agg(jsonb_strip_nulls(jsonb_build_object(
            'Name', c->'Name', 'CanonicalName', c->'CanonicalName', 'SortName', c->'SortName',
            'Provider', c->'Provider', 'ExternalId', c->'ExternalId', 'MusicBrainzArtistId', c->'MusicBrainzArtistId',
            'JoinPhrase', c->'JoinPhrase', 'Role', c->'Role')) ORDER BY n), '[]'::jsonb)
          FROM jsonb_array_elements(CASE WHEN jsonb_typeof(value) = 'array' THEN value ELSE '[]'::jsonb END)
            WITH ORDINALITY entries(c,n)
        $$;
        DO $$ DECLARE bad uuid; BEGIN
          SELECT e."Id" INTO bad FROM "PlaylistEntries" e
            JOIN "Playlists" p ON p."Id" = e."PlaylistId"
            JOIN "TrackObservations" o ON o."Id" = e."TrackObservationId"
            WHERE p."UserId" <> o."OwnerUserId" LIMIT 1;
          IF bad IS NOT NULL THEN RAISE EXCEPTION 'Cross-owner playlist entry % prevents catalog reduction', bad; END IF;
          SELECT o."Id" INTO bad FROM "TrackObservations" o
            JOIN "ProviderCatalogItems" c ON c."Id" = o."ProviderCatalogItemId"
            WHERE o."SourceType" <> c."SourceType" OR o."ExternalId" <> c."ExternalId" LIMIT 1;
          IF bad IS NOT NULL THEN RAISE EXCEPTION 'Mismatched observation provider key % prevents catalog reduction', bad; END IF;
          SELECT s."Id" INTO bad FROM "TrackSourceIds" s
            JOIN "ProviderCatalogItems" c ON c."Id" = s."ProviderCatalogItemId"
            WHERE s."SourceType" <> c."SourceType" OR s."ExternalId" <> c."ExternalId" LIMIT 1;
          IF bad IS NOT NULL THEN RAISE EXCEPTION 'Mismatched recording provider key % prevents catalog reduction', bad; END IF;
          SELECT m."Id" INTO bad FROM "ServicePlaylistMappings" m
            JOIN "Playlists" p ON p."Id" = m."PlaylistId"
            JOIN "ConnectedServiceAccounts" a ON a."Id" = m."ConnectedServiceAccountId"
            WHERE m."UserId" <> p."UserId" OR m."UserId" <> a."UserId" OR m."Service" <> a."Service" LIMIT 1;
          IF bad IS NOT NULL THEN RAISE EXCEPTION 'Mismatched account/playlist binding % prevents catalog reduction', bad; END IF;
          SELECT o."Id" INTO bad FROM "TrackObservations" o
            LEFT JOIN "TrackResolutionCandidates" c ON c."Id" = o."AcceptedCandidateId"
            WHERE o."AcceptedCandidateId" IS NOT NULL AND (c."Id" IS NULL OR c."TrackObservationId" <> o."Id") LIMIT 1;
          IF bad IS NOT NULL THEN RAISE EXCEPTION 'Foreign or missing accepted candidate on observation % prevents catalog reduction', bad; END IF;
          SELECT o."Id" INTO bad FROM "TrackObservations" o WHERE NULLIF(o."RawMetadata", '') IS NOT NULL
            AND (pg_temp.cr_json(o."RawMetadata") IS NULL OR jsonb_typeof(pg_temp.cr_json(o."RawMetadata")) <> 'object') LIMIT 1;
          IF bad IS NOT NULL THEN RAISE EXCEPTION 'Malformed observation input % prevents lossless catalog reduction', bad; END IF;
          SELECT c."Id" INTO bad FROM "TrackResolutionCandidates" c WHERE NULLIF(c."RawMetadata", '') IS NOT NULL
            AND (pg_temp.cr_json(c."RawMetadata") IS NULL OR jsonb_typeof(pg_temp.cr_json(c."RawMetadata")) <> 'object') LIMIT 1;
          IF bad IS NOT NULL THEN RAISE EXCEPTION 'Malformed candidate input % prevents lossless catalog reduction', bad; END IF;
          SELECT a."Id" INTO bad FROM "Artists" a
            JOIN "ArtistProviderLinks" l ON l."ArtistId" = a."Id" AND l."Provider" = 'musicbrainz'
            WHERE NULLIF(trim(a."MusicBrainzArtistId"), '') IS NOT NULL
              AND lower(trim(l."ExternalId")) <> lower(trim(a."MusicBrainzArtistId")) LIMIT 1;
          IF bad IS NOT NULL THEN RAISE EXCEPTION 'Conflicting MusicBrainz identity on artist % prevents catalog reduction', bad; END IF;
          SELECT a."Id" INTO bad FROM "Artists" a
            JOIN "ArtistProviderLinks" l ON l."Provider" = 'musicbrainz'
              AND lower(trim(l."ExternalId")) = lower(trim(a."MusicBrainzArtistId"))
            WHERE a."Id" <> l."ArtistId" LIMIT 1;
          IF bad IS NOT NULL THEN RAISE EXCEPTION 'MusicBrainz identity assigned to another artist % prevents catalog reduction', bad; END IF;
          SELECT min(l."Id"::text)::uuid INTO bad FROM "ArtistProviderLinks" l WHERE l."Provider" = 'musicbrainz'
            GROUP BY lower(trim(l."ExternalId")) HAVING count(*) > 1 LIMIT 1;
          IF bad IS NOT NULL THEN RAISE EXCEPTION 'Duplicate normalized MusicBrainz link % prevents catalog reduction', bad; END IF;
        END $$;
        CREATE TEMP TABLE cr_entries ON COMMIT DROP AS
          SELECT "Id", "PlaylistId", "TrackId", "TrackObservationId", "Position", "AddedAt", "SourceService" FROM "PlaylistEntries";
        CREATE TEMP TABLE cr_links ON COMMIT DROP AS
          SELECT "Id", "PlaylistId", "UserId", "ConnectedServiceAccountId", "Service", "ExternalAccountId", "ServicePlaylistId",
            "BaselineJson", "PendingWriteJson", "MatchingProgressJson" FROM "ServicePlaylistMappings";
        CREATE TEMP TABLE cr_exports ON COMMIT DROP AS SELECT * FROM "PlaylistExportAttempts";
        CREATE TEMP TABLE cr_sources ON COMMIT DROP AS SELECT * FROM "TrackSourceIds";
        CREATE TEMP TABLE cr_observations ON COMMIT DROP AS SELECT "Id", pg_temp.cr_json("RawMetadata") AS input FROM "TrackObservations";
        CREATE TEMP TABLE cr_candidates ON COMMIT DROP AS SELECT "Id", pg_temp.cr_json("RawMetadata") AS input FROM "TrackResolutionCandidates";
        CREATE TEMP TABLE cr_artists ON COMMIT DROP AS SELECT "Id", "MusicBrainzArtistId" FROM "Artists";
        CREATE TEMP TABLE cr_conflicts ON COMMIT DROP AS SELECT "Id", pg_temp.cr_json("Evidence") AS evidence FROM "ArtistReconciliationConflicts";
        """;

    internal const string Convert = """
        INSERT INTO "ProviderCatalogItems" ("Id", "SourceType", "ExternalId", "FactsJson", "Revision", "VisibilityState",
          "AssociationState", "AssociationRevision", "PresentationKind")
          SELECT s."Id", s."SourceType", s."ExternalId", '{"Title":""}', 0, 0, 0, 0, s."PresentationKind"
          FROM cr_sources s WHERE NOT EXISTS (SELECT 1 FROM "ProviderCatalogItems" c
            WHERE c."SourceType" = s."SourceType" AND c."ExternalId" = s."ExternalId");
        UPDATE "ProviderCatalogItems" c SET
          "TrackId" = s."TrackId", "AssociationMethod" = s."AssociationMethod",
          "AssociationVerifiedAt" = s."AssociationVerifiedAt", "AssociationFactsRevision" = s."CatalogRevision",
          "AssociationRevision" = s."Revision", "PresentationKind" = s."PresentationKind",
          "PresentationConfidence" = s."PresentationConfidence", "IsOfficial" = s."IsOfficial",
          "AssociationEvidenceJson" = CASE WHEN jsonb_typeof(pg_temp.cr_json(s."IndependentEvidenceJson")) = 'object'
            THEN jsonb_strip_nulls(jsonb_build_object(
              'Title', pg_temp.cr_json(s."IndependentEvidenceJson")->'Title',
              'Artist', pg_temp.cr_json(s."IndependentEvidenceJson")->'Artist',
              'DurationSeconds', pg_temp.cr_json(s."IndependentEvidenceJson")->'DurationSeconds',
              'Isrc', pg_temp.cr_json(s."IndependentEvidenceJson")->'Isrc',
              'MusicBrainzRecordingId', pg_temp.cr_json(s."IndependentEvidenceJson")->'MusicBrainzRecordingId',
              'VersionFlags', pg_temp.cr_json(s."IndependentEvidenceJson")->'VersionFlags',
              'ArtistCredits', pg_temp.cr_credit_ids(pg_temp.cr_json(s."IndependentEvidenceJson")->'ArtistCredits')))::text END,
          "AssociationState" = CASE WHEN s."AssociationState" = 1 AND (
              s."AssociationMethod" IS NULL OR s."AssociationMethod" NOT IN ('provider_origin', 'independent_recording')
              OR s."AssociationVerifiedAt" IS NULL OR s."CatalogRevision" IS NULL OR s."CatalogRevision" <= 0
              OR jsonb_typeof(pg_temp.cr_json(s."IndependentEvidenceJson")) IS DISTINCT FROM 'object')
            THEN 0 ELSE s."AssociationState" END
          FROM cr_sources s WHERE c."SourceType" = s."SourceType" AND c."ExternalId" = s."ExternalId";

        UPDATE "PlaylistEntries" e SET "OwnerUserId" = p."UserId" FROM "Playlists" p WHERE e."PlaylistId" = p."Id";
        UPDATE "TrackObservations" o SET "InputKind" = CASE WHEN NULLIF(trim(o."Title"), '') IS NULL THEN 0 ELSE 2 END,
          "PrivateInputJson" = CASE WHEN NULLIF(trim(o."Title"), '') IS NOT NULL THEN
            jsonb_strip_nulls(jsonb_build_object(
              'OriginalTitle', CASE WHEN s.input->>'OriginalTitle' IS DISTINCT FROM o."Title" THEN s.input->'OriginalTitle' END,
              'OriginalArtist', CASE WHEN s.input->>'OriginalArtist' IS DISTINCT FROM o."Artist" THEN s.input->'OriginalArtist' END,
              'ChannelTitle', CASE WHEN s.input->>'ChannelTitle' IS DISTINCT FROM o."Artist" THEN s.input->'ChannelTitle' END,
              'Description', s.input->'Description', 'Isrc', s.input->'Isrc', 'MbidRecording', s.input->'MbidRecording',
              'Album', s.input->'Album', 'ArtistIdentities', pg_temp.cr_credit_ids(s.input->'ArtistIdentities')))::text END
          FROM cr_observations s WHERE s."Id" = o."Id";
        -- A matching key alone is not provenance. Only inputs whose needed
        -- values already exist in an independently acquired record lose their
        -- private copy. Otherwise keep a bounded owner-only input.
        UPDATE "TrackObservations" o SET "InputKind" = 1, "Title" = NULL, "Artist" = NULL,
          "ThumbnailUrl" = NULL, "DurationSeconds" = NULL, "PrivateInputJson" = NULL
          FROM "ProviderCatalogItems" c, cr_observations s
          WHERE s."Id" = o."Id" AND c."Id" = o."ProviderCatalogItemId" AND o."InputKind" = 2
            AND c."VisibilityState" = 1 AND c."VerifiedAt" IS NOT NULL AND c."RevalidateAfter" IS NOT NULL
            AND ((c."SourceType" = 'spotify' AND c."VerificationMethod" = 'spotify_application_catalog')
              OR (c."SourceType" = 'youtube' AND c."VerificationMethod" = 'youtube_public_video_response')
              OR (c."SourceType" = 'musicbrainz' AND c."VerificationMethod" = 'musicbrainz_recording_response'))
            AND c."FactsJson"::jsonb->>'Title' = o."Title"
            AND c."FactsJson"::jsonb->>'Artist' IS NOT DISTINCT FROM o."Artist"
            AND c."FactsJson"::jsonb->>'ThumbnailUrl' IS NOT DISTINCT FROM o."ThumbnailUrl"
            AND c."FactsJson"::jsonb->>'DurationSeconds' IS NOT DISTINCT FROM o."DurationSeconds"::text
            AND (NULLIF(s.input->>'Description', '') IS NULL OR s.input->>'Description' = c."FactsJson"::jsonb->>'Description')
            AND (NULLIF(s.input->>'OriginalTitle', '') IS NULL OR s.input->>'OriginalTitle' IN (o."Title", c."FactsJson"::jsonb->>'OriginalTitle'))
            AND (NULLIF(s.input->>'OriginalArtist', '') IS NULL OR s.input->>'OriginalArtist' IN (o."Artist", c."FactsJson"::jsonb->>'ChannelTitle'))
            AND (NULLIF(s.input->>'ChannelTitle', '') IS NULL OR s.input->>'ChannelTitle' IN (o."Artist", c."FactsJson"::jsonb->>'ChannelTitle'))
            AND (NULLIF(s.input->>'Isrc', '') IS NULL OR s.input->>'Isrc' = c."FactsJson"::jsonb->>'Isrc')
            AND (NULLIF(s.input->>'MbidRecording', '') IS NULL OR s.input->>'MbidRecording' = c."FactsJson"::jsonb->>'MusicBrainzRecordingId')
            AND (NULLIF(s.input->>'Album', '') IS NULL OR c."FactsJson"::jsonb->'Albums' ? (s.input->>'Album'))
            AND (pg_temp.cr_credit_ids(s.input->'ArtistIdentities') = '[]'::jsonb
              OR pg_temp.cr_credit_ids(s.input->'ArtistIdentities') = pg_temp.cr_credit_ids(c."FactsJson"::jsonb->'ArtistCredits'));
        UPDATE "TrackObservations" SET "Title" = NULL, "Artist" = NULL, "ThumbnailUrl" = NULL,
          "DurationSeconds" = NULL, "PrivateInputJson" = NULL WHERE "InputKind" = 0;

        UPDATE "TrackResolutionCandidates" candidate SET
          "ProviderCatalogItemId" = c."Id", "ProviderCatalogRevision" = c."Revision",
          "Title" = NULL, "Artist" = NULL, "MbidRecording" = NULL, "Isrc" = NULL, "DurationSeconds" = NULL
          FROM cr_candidates s, "ProviderCatalogItems" c
          WHERE candidate."Id" = s."Id" AND c."Id" = pg_temp.cr_uuid(s.input->>'ProviderCatalogItemId')
            AND c."SourceType" = candidate."CandidateSource" AND c."ExternalId" = candidate."ExternalId"
            AND s.input->>'ProviderCatalogRevision' = c."Revision"::text
            AND c."FactsJson"::jsonb->>'Title' = candidate."Title"
            AND c."FactsJson"::jsonb->>'Artist' IS NOT DISTINCT FROM candidate."Artist"
            AND c."FactsJson"::jsonb->>'DurationSeconds' IS NOT DISTINCT FROM candidate."DurationSeconds"::text
            AND c."FactsJson"::jsonb->>'Isrc' IS NOT DISTINCT FROM candidate."Isrc"
            AND c."FactsJson"::jsonb->>'MusicBrainzRecordingId' IS NOT DISTINCT FROM candidate."MbidRecording";
        UPDATE "TrackResolutionCandidates" c SET "EvaluationJson" =
          jsonb_strip_nulls(jsonb_build_object('Matching', s.input->'Matching',
            'ArtistIdentities', CASE WHEN c."ProviderCatalogItemId" IS NULL THEN pg_temp.cr_credit_ids(s.input->'ArtistIdentities') END,
            'ArtistMusicBrainzId', CASE WHEN c."ProviderCatalogItemId" IS NULL THEN s.input->'ArtistMusicBrainzId' END,
            'ArtistSortName', CASE WHEN c."ProviderCatalogItemId" IS NULL THEN s.input->'ArtistSortName' END))::text
          FROM cr_candidates s WHERE s."Id" = c."Id";

        UPDATE "ArtistProviderLinks" SET "ExternalId" = lower(trim("ExternalId")) WHERE "Provider" = 'musicbrainz';
        INSERT INTO "ArtistProviderLinks" ("Id", "ArtistId", "Provider", "ExternalId", "UpdatedAt")
          SELECT gen_random_uuid(), a."Id", 'musicbrainz', lower(trim(a."MusicBrainzArtistId")), CURRENT_TIMESTAMP
          FROM cr_artists a WHERE NULLIF(trim(a."MusicBrainzArtistId"), '') IS NOT NULL
            AND NOT EXISTS (SELECT 1 FROM "ArtistProviderLinks" l WHERE l."ArtistId" = a."Id" AND l."Provider" = 'musicbrainz');
        UPDATE "TrackArtistCredits" credit SET "ProviderCatalogRevision" = c."Revision",
          "SourceCreditPosition" = (match.position - 1)::integer
          FROM "ProviderCatalogItems" c, LATERAL jsonb_array_elements(c."FactsJson"::jsonb->'ArtistCredits') WITH ORDINALITY match(value,position)
          WHERE credit."ProviderCatalogItemId" = c."Id" AND credit."CreditedName" = match.value->>'Name'
            AND credit."Role" = COALESCE((match.value->>'Role')::smallint, 0)
            AND credit."Position" = match.position - 1;
        UPDATE "ArtistReconciliationConflicts" c SET "IdentityConflictJson" = jsonb_strip_nulls(jsonb_build_object(
          'Provider', s.evidence->'incoming'->'Provider', 'ExternalId', s.evidence->'incoming'->'ExternalId',
          'MusicBrainzArtistId', s.evidence->'incoming'->'MusicBrainzArtistId',
          'ExistingArtistIds', (SELECT COALESCE(jsonb_agg(e->'ArtistId'), '[]'::jsonb)
            FROM jsonb_array_elements(CASE WHEN jsonb_typeof(s.evidence->'existing') = 'array' THEN s.evidence->'existing' ELSE '[]'::jsonb END) e)))::text
          FROM cr_conflicts s WHERE s."Id" = c."Id";
        """;

    internal const string AssertPreserved = """
        DO $$ BEGIN
          IF EXISTS ((SELECT * FROM cr_entries EXCEPT SELECT "Id", "PlaylistId", "TrackId", "TrackObservationId", "Position", "AddedAt", "SourceService" FROM "PlaylistEntries")
            UNION ALL (SELECT "Id", "PlaylistId", "TrackId", "TrackObservationId", "Position", "AddedAt", "SourceService" FROM "PlaylistEntries" EXCEPT SELECT * FROM cr_entries))
          THEN RAISE EXCEPTION 'Catalog reduction changed playlist membership'; END IF;
          IF EXISTS ((SELECT * FROM cr_exports EXCEPT SELECT * FROM "PlaylistExportAttempts")
            UNION ALL (SELECT * FROM "PlaylistExportAttempts" EXCEPT SELECT * FROM cr_exports))
          THEN RAISE EXCEPTION 'Catalog reduction changed export history'; END IF;
          IF EXISTS ((SELECT * FROM cr_links EXCEPT SELECT "Id", "PlaylistId", "UserId", "ConnectedServiceAccountId", "Service", "ExternalAccountId", "ServicePlaylistId", "BaselineJson", "PendingWriteJson", "MatchingProgressJson" FROM "ServicePlaylistMappings")
            UNION ALL (SELECT "Id", "PlaylistId", "UserId", "ConnectedServiceAccountId", "Service", "ExternalAccountId", "ServicePlaylistId", "BaselineJson", "PendingWriteJson", "MatchingProgressJson" FROM "ServicePlaylistMappings" EXCEPT SELECT * FROM cr_links))
          THEN RAISE EXCEPTION 'Catalog reduction changed account bindings or sync receipts'; END IF;
        END $$;
        """;
}
