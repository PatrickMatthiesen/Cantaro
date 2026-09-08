DO $test$
DECLARE watch_row record; catalog_row record; bad_row record; evidence record;
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'MediaObservations' AND column_name = 'RawPayload') THEN
        RAISE EXCEPTION 'RawPayload column was not retired';
    END IF;
    SELECT * INTO STRICT watch_row FROM "MediaObservations" WHERE "Id" = '00000000-0000-0000-0001-000000000001';
    IF watch_row."EpisodeNumber" IS DISTINCT FROM 7 OR watch_row."SeasonNumber" IS DISTINCT FROM 1
       OR watch_row."EpisodeTitle" IS DISTINCT FROM 'Seventh' OR watch_row."SeasonTitle" IS DISTINCT FROM 'Season 1'
       OR watch_row."ProviderSeriesId" IS DISTINCT FROM 'SERIES1' OR watch_row."ProviderSeasonId" IS DISTINCT FROM 'SEASON1'
       OR watch_row."ProviderSequenceNumber" IS DISTINCT FROM 7 OR watch_row."ReleaseTrack" IS DISTINCT FROM 'sub:en'
       OR watch_row."NextEpisodeProviderId" IS DISTINCT FROM 'EP8' OR watch_row."NextEpisodeTitle" IS DISTINCT FROM 'Eighth'
       OR watch_row."NextEpisodeNumber" IS DISTINCT FROM 8 OR watch_row."NextEpisodeReleaseTrack" IS DISTINCT FROM 'dub:en'
       OR watch_row."IsCatalogObservation" THEN
        RAISE EXCEPTION 'Historical watch evidence changed';
    END IF;
    IF watch_row."ObservedUrl" IS DISTINCT FROM 'https://www.crunchyroll.com/watch/EP7'
       OR watch_row."NextEpisodeUrl" IS DISTINCT FROM 'https://www.crunchyroll.com/watch/EP8'
       OR watch_row."LastMatchError" LIKE '%SECRET%' THEN RAISE EXCEPTION 'Sensitive URLs retained'; END IF;
    IF watch_row."MatchStatus" IS DISTINCT FROM 'matched'
       OR watch_row."MediaTitleId" IS DISTINCT FROM '00000000-0000-0000-0000-000000000153'::uuid
       OR watch_row."EpisodeOffset" IS DISTINCT FROM 0 OR watch_row."ResolvedProgress" IS DISTINCT FROM 7
       OR watch_row."MatchAttemptCount" IS DISTINCT FROM 2 OR watch_row."ResolutionNotes" IS DISTINCT FROM 'Preserved resolution' THEN
        RAISE EXCEPTION 'Historical matching or resolution state changed';
    END IF;
    SELECT * INTO STRICT catalog_row FROM "MediaObservations" WHERE "Id" = '00000000-0000-0000-0001-000000000002';
    IF NOT catalog_row."IsCatalogObservation" OR catalog_row."SeriesTitle" IS DISTINCT FROM 'Example'
       OR catalog_row."ProviderSeasonId" IS DISTINCT FROM 'SEASON1' OR catalog_row."SeasonNumber" IS DISTINCT FROM 1 THEN
        RAISE EXCEPTION 'Catalog discriminator or season evidence lost';
    END IF;
    IF (SELECT count(*) FROM "MediaObservationEpisode") <> 2 THEN RAISE EXCEPTION 'Episode backfill count incorrect'; END IF;
    SELECT * INTO STRICT evidence FROM "MediaObservationEpisode" WHERE "MediaObservationId" = catalog_row."Id";
    IF evidence."ProviderEpisodeId" IS DISTINCT FROM 'EP7' OR evidence."EpisodeNumber" IS DISTINCT FROM 7
       OR evidence."ProviderUrl" IS DISTINCT FROM 'https://www.crunchyroll.com/watch/EP7'
       OR evidence."AvailableSubtitleLanguageCodes" IS DISTINCT FROM ARRAY['en']
       OR evidence."AvailableAudioLanguageCodes" IS DISTINCT FROM ARRAY['ja'] THEN
        RAISE EXCEPTION 'Catalog episode identity or languages lost';
    END IF;
    SELECT * INTO STRICT evidence FROM "MediaObservationEpisode" WHERE "MediaObservationId" = watch_row."Id";
    IF evidence."ProviderEpisodeId" IS DISTINCT FROM 'EP9' OR evidence."EpisodeTitle" IS DISTINCT FROM 'Ninth'
       OR evidence."ReleaseTrack" IS DISTINCT FROM 'sub:en' OR evidence."EpisodeNumber" IS DISTINCT FROM 9
       OR evidence."ProviderUrl" IS DISTINCT FROM 'https://www.crunchyroll.com/watch/EP9'
       OR evidence."AvailableSubtitleLanguageCodes" IS DISTINCT FROM ARRAY['en'] THEN
        RAISE EXCEPTION 'Series-page episode evidence lost or not minimized';
    END IF;
    SELECT * INTO STRICT bad_row FROM "MediaObservations" WHERE "Id" = '00000000-0000-0000-0001-000000000003';
    IF bad_row."SeriesTitle" IS NOT NULL OR bad_row."EpisodeNumber" IS NOT NULL THEN RAISE EXCEPTION 'Malformed JSON became evidence'; END IF;
    SELECT * INTO STRICT bad_row FROM "MediaObservations" WHERE "Id" = '00000000-0000-0000-0001-000000000004';
    IF bad_row."EpisodeNumber" IS NOT NULL OR bad_row."SeasonNumber" IS NOT NULL THEN RAISE EXCEPTION 'Invalid numeric evidence retained'; END IF;
    IF EXISTS (SELECT 1 FROM "MediaObservations" o WHERE row_to_json(o)::text LIKE '%SECRET%')
       OR EXISTS (SELECT 1 FROM "MediaObservationEpisode" e WHERE row_to_json(e)::text LIKE '%SECRET%') THEN
        RAISE EXCEPTION 'Retired sensitive data remains in structured storage';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM "MediaEpisodeProviderIdentities" WHERE "Id" = '00000000-0000-0000-0000-000000000156'
        AND "IsTrusted" AND NOT "HasConflict" AND "ReleaseTrack" = 'sub:en' AND "SeenCount" = 1) THEN
        RAISE EXCEPTION 'Canonical trusted identity changed during migration';
    END IF;
END $test$;

-- Account deletion removes both observation shapes and children, while shared
-- canonical episode identities remain independent of their original observer.
DELETE FROM "AspNetUsers" WHERE "Id" = 153;
DO $test$
BEGIN
    IF EXISTS (SELECT 1 FROM "MediaObservations") OR EXISTS (SELECT 1 FROM "MediaObservationEpisode") THEN
        RAISE EXCEPTION 'Account deletion failed to cascade to observation evidence';
    END IF;
    IF (SELECT count(*) FROM "MediaTitles") <> 1 OR (SELECT count(*) FROM "MediaEpisodeProviderIdentities") <> 1 THEN
        RAISE EXCEPTION 'Account deletion erased shared canonical data';
    END IF;
END $test$;
