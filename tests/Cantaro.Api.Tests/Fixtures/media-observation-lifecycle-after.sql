DO $test$
DECLARE payload jsonb;
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'MediaObservations' AND column_name IN ('ExtensionVersion','ResolutionHistoryPayload')) THEN
        RAISE EXCEPTION 'Unused observation metadata columns remain';
    END IF;
    IF (SELECT count(*) FROM "MediaObservations") <> 1 OR NOT EXISTS (SELECT 1 FROM "MediaObservations" WHERE "MatchStatus" = 'no_match') THEN
        RAISE EXCEPTION 'Old unresolved task lost or rejected task retained';
    END IF;
    IF EXISTS (SELECT 1 FROM "MediaObservationEpisode") THEN RAISE EXCEPTION 'Rejected child evidence retained'; END IF;
    SELECT "PayloadJson"::jsonb INTO STRICT payload FROM "MediaProviderOperations" WHERE "Id" = '00000000-0000-0000-0156-000000000007';
    IF payload IS DISTINCT FROM '{"providerMediaId":"156","progressEpisodes":7,"lastKnownRemoteUpdateAt":"2026-09-01T12:00:00Z"}'::jsonb THEN
        RAISE EXCEPTION 'Provider target/progress/concurrency lost or unnecessary provenance retained';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM "MediaProviderOperations" WHERE "PayloadJson" = '{broken') THEN RAISE EXCEPTION 'Malformed unrelated data changed'; END IF;
    IF NOT EXISTS (SELECT 1 FROM "MediaLibraryEntries" WHERE "ProgressEpisodes" = 7) OR (SELECT count(*) FROM "MediaEpisodeProviderIdentities") <> 1 THEN
        RAISE EXCEPTION 'Personal progress or shared canonical identity lost';
    END IF;
END $test$;
