INSERT INTO "AspNetUsers" ("Id", "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
VALUES (153, false, false, false, false, 0);

INSERT INTO "MediaTitles" ("Id", "CanonicalTitle", "MediaKind", "PrimaryProgressDimension", "ReleaseStatusDimension", "SupportsEpisodeProgress", "SupportsChapterProgress", "SupportsVolumeProgress", "IsCompletionOnly", "Synonyms")
VALUES ('00000000-0000-0000-0000-000000000153', 'Example', 'anime', 'episodes', 'episodes', true, false, false, false, '{}');
INSERT INTO "MediaEpisodes" ("Id", "MediaTitleId", "EpisodeNumber", "Title")
VALUES ('00000000-0000-0000-0000-000000000154', '00000000-0000-0000-0000-000000000153', 7, 'Seventh');
INSERT INTO "MediaEpisodeProviderContents" ("Id", "MediaEpisodeId", "Provider", "ProviderSeriesId", "ProviderSeasonId", "ProviderEpisodeNumber")
VALUES ('00000000-0000-0000-0000-000000000155', '00000000-0000-0000-0000-000000000154', 'crunchyroll', 'SERIES1', 'SEASON1', 7);
INSERT INTO "MediaEpisodeProviderIdentities" ("Id", "MediaEpisodeProviderContentId", "Provider", "ProviderEpisodeId", "ProviderUrlPath", "FirstSeenAt", "LastSeenAt", "IsTrusted", "HasConflict", "ReleaseTrack")
VALUES ('00000000-0000-0000-0000-000000000156', '00000000-0000-0000-0000-000000000155', 'crunchyroll', 'EP7', '/watch/EP7', now(), now(), true, false, 'sub:en');

INSERT INTO "MediaObservations" ("Id", "UserId", "SiteIdentifier", "SiteMediaId", "ObservedUrl", "ObservedTitle", "ProgressHint", "ObservedAt", "RawPayload", "MatchStatus", "MediaTitleId", "MatchAttemptCount", "EpisodeOffset", "ResolvedProgress", "ResolutionNotes", "LastMatchError")
VALUES
('00000000-0000-0000-0001-000000000001', 153, 'crunchyroll', 'EP7', 'https://user:SECRET@www.crunchyroll.com/watch/EP7?token=SECRET#private', 'Example', 'Episode 7', now(),
'{"siteIdentifier":"crunchyroll","siteMediaId":"EP7","observedTitle":"Example","seriesTitle":"Example","episodeTitle":"Seventh","episodeNumber":7,"seasonTitle":"Season 1","seasonNumber":1,"providerSeriesId":"SERIES1","providerSeasonId":"SEASON1","providerSequenceNumber":7,"releaseTrack":"sub:en","nextEpisodeProviderId":"EP8","nextEpisodeUrl":"https://www.crunchyroll.com/watch/EP8?token=SECRET#private","nextEpisodeTitle":"Eighth","nextEpisodeNumber":8,"nextEpisodeReleaseTrack":"dub:en","positionSeconds":999,"durationSeconds":1000,"watchProgressPercent":99,"observedEpisodes":[{"providerEpisodeId":"EP9","providerUrl":"https://www.crunchyroll.com/watch/EP9?token=SECRET#private","episodeNumber":9,"episodeTitle":"Ninth","releaseTrack":"sub:en","availableSubtitleLanguageCodes":["en","en"],"availableAudioLanguageCodes":["ja"]}]}',
'matched', '00000000-0000-0000-0000-000000000153', 2, 0, 7, 'Preserved resolution', 'HTTP failed https://host/?token=SECRET'),
('00000000-0000-0000-0001-000000000002', 153, 'crunchyroll', 'catalog:season1', 'https://www.crunchyroll.com/series/SERIES1?token=SECRET#private', 'Example Season 1', null, now(),
'{"provider":"crunchyroll","seriesTitle":"Example","seriesUrl":"https://www.crunchyroll.com/series/SERIES1?token=SECRET#private","providerSeriesId":"SERIES1","providerSeasonId":"SEASON1","seasonTitle":"Season 1","seasonNumber":1,"episodes":[{"providerEpisodeId":"EP7","providerUrl":"https://www.crunchyroll.com/watch/EP7?token=SECRET#private","episodeNumber":7,"episodeTitle":"Seventh","releaseTrack":"sub:en","availableSubtitleLanguageCodes":["en"],"availableAudioLanguageCodes":["ja"]}]}',
'pending', null, 0, null, null, null, null),
('00000000-0000-0000-0001-000000000003', 153, 'crunchyroll', null, 'https://www.crunchyroll.com/watch/EP10?token=SECRET#private', 'Malformed legacy request', null, now(), '{broken', 'no_match', null, 1, null, null, null, null),
('00000000-0000-0000-0001-000000000004', 153, 'crunchyroll', null, 'https://www.crunchyroll.com/watch/EP11#private', 'Wrong JSON types', null, now(), '{"episodeNumber":999999999999999999999,"seasonNumber":"invalid","episodes":null}', 'no_match', null, 1, null, null, null, null);
