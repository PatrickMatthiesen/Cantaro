INSERT INTO "AspNetUsers" ("Id", "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
VALUES (156, false, false, false, false, 0);
INSERT INTO "MediaObservations" ("Id", "UserId", "SiteIdentifier", "ObservedUrl", "ObservedTitle", "ObservedAt", "MatchStatus", "MatchAttemptCount", "ExtensionVersion", "ResolutionHistoryPayload", "CreatedAt", "UpdatedAt")
VALUES
('00000000-0000-0000-0156-000000000001',156,'crunchyroll','https://www.crunchyroll.com/watch/PENDING','Pending',now(),'no_match',0,'old-version','{"history":"obsolete"}',now()-interval '90 days',now()-interval '90 days'),
('00000000-0000-0000-0156-000000000002',156,'crunchyroll','https://www.crunchyroll.com/watch/REJECTED','Rejected',now(),'rejected',0,null,null,now(),now());
INSERT INTO "MediaObservationEpisode" ("Id", "MediaObservationId", "ProviderEpisodeId", "ProviderUrl", "EpisodeNumber", "AvailableAudioLanguageCodes", "AvailableSubtitleLanguageCodes")
VALUES ('00000000-0000-0000-0156-000000000003','00000000-0000-0000-0156-000000000002','REJECTED','https://www.crunchyroll.com/watch/REJECTED',1,'{}','{}');
INSERT INTO "MediaProviderLinks" ("Id", "MediaTitleId", "Provider", "ExternalId", "LinkSource")
VALUES ('00000000-0000-0000-0156-000000000004','00000000-0000-0000-0000-000000000153','anilist','156','automatic');
INSERT INTO "MediaLibraryEntries" ("Id", "UserId", "MediaTitleId", "Status", "ProgressEpisodes")
VALUES ('00000000-0000-0000-0156-000000000005',156,'00000000-0000-0000-0000-000000000153','watching',7);
INSERT INTO "MediaLibraryProviderBindings" ("Id", "MediaLibraryEntryId", "MediaProviderLinkId", "ProviderAccountId")
VALUES ('00000000-0000-0000-0156-000000000006','00000000-0000-0000-0156-000000000005','00000000-0000-0000-0156-000000000004','156');
INSERT INTO "MediaProviderOperations" ("Id", "MediaLibraryProviderBindingId", "OperationType", "PayloadJson", "Status", "AttemptCount")
VALUES
('00000000-0000-0000-0156-000000000007','00000000-0000-0000-0156-000000000006','auto_progress_update','{"providerMediaId":"156","progressEpisodes":7,"lastKnownRemoteUpdateAt":"2026-09-01T12:00:00Z","triggeredByObservationId":"private-id","observedSiteIdentifier":"crunchyroll","observedProgressHint":"7","observationMatchScore":0.95,"triggeredAt":"2026-09-01T12:00:00Z"}','pending',0),
('00000000-0000-0000-0156-000000000008','00000000-0000-0000-0156-000000000006','auto_progress_update','{broken','failed',1);
