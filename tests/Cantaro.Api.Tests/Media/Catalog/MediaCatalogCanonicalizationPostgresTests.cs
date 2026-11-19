using Cantaro.Api.Data;
using Cantaro.Api.Controllers;
using Cantaro.Api.Models;
using System.Data.Common;
using System.Diagnostics;
using System.Security.Claims;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Cantaro.Api.Tests.Media.Catalog;

[Collection("Media catalog PostgreSQL")]
public sealed class MediaCatalogCanonicalizationPostgresTests(ITestOutputHelper output)
{
    [PostgresFact]
    public async Task ProviderEpisodeMigrationRetainsTrustedPlaybackWithSeasonPrecedence()
    {
        await using var schema = await SchemaContext.CreateAsync(createTables: false);
        var db = schema.Db;
        await db.GetService<IMigrator>().MigrateAsync("20261114093133_RetainCandidateVersionAnchor");
        var first = CreateSeries("Legacy destination");
        var second = CreateSeries("Automatic season destination");
        var movie = new MediaTitle
        {
            Id = Guid.NewGuid(), CanonicalTitle = "Legacy movie", MediaKind = MediaKinds.Movie,
            IsCompletionOnly = true, PrimaryProgressDimension = MediaProgressDimensions.CompletionOnly,
            ReleaseStatusDimension = MediaProgressDimensions.CompletionOnly
        };
        db.MediaTitles.AddRange(first, second, movie);
        await db.SaveChangesAsync();
        var firstEpisode = Guid.NewGuid();
        var secondEpisode = Guid.NewGuid();
        var movieEpisode = Guid.NewGuid();
        var uncovered = Guid.NewGuid();
        var divergent = Guid.NewGuid();
        var siblingCovered = Guid.NewGuid();
        var localizedSibling = Guid.NewGuid();
        var standalone = Guid.NewGuid();
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "MediaEpisodes" ("Id", "MediaTitleId", "EpisodeNumber",
                "AvailableSubtitleLanguageCodes", "AvailableAudioLanguageCodes")
            VALUES ({firstEpisode}, {first.Id}, 2, ARRAY[]::text[], ARRAY[]::text[]),
                   ({secondEpisode}, {second.Id}, 3, ARRAY[]::text[], ARRAY[]::text[]),
                   ({movieEpisode}, {movie.Id}, 1, ARRAY[]::text[], ARRAY[]::text[]);
            INSERT INTO "MediaEpisodeProviderContents" ("Id", "MediaEpisodeId", "Provider", "ProviderContentKey",
                "ProviderSeriesId", "ProviderSeasonId", "ProviderSeasonNumber", "ProviderEpisodeNumber")
            VALUES ({uncovered}, {firstEpisode}, 'crunchyroll', 'UNCOVERED', 'SERIES', 'UNCOVERED', 2, 2),
                   ({divergent}, {firstEpisode}, 'crunchyroll', 'DIVERGENT', 'SERIES', 'COVERED', 1, 1),
                   ({siblingCovered}, {secondEpisode}, 'crunchyroll', 'SHAREDKEY', 'SERIES', 'COVERED', 1, 3),
                   ({localizedSibling}, {firstEpisode}, 'crunchyroll', 'SHAREDKEY', null, null, null, null),
                   ({standalone}, {movieEpisode}, 'crunchyroll', 'MOVIE', 'SERIES', 'COVERED', 1, 2);
            INSERT INTO "MediaEpisodeProviderIdentities" ("Id", "MediaEpisodeProviderContentId", "Provider",
                "ProviderEpisodeId", "ProviderUrlPath", "SeenCount", "FirstSeenAt", "LastSeenAt", "IsTrusted", "HasConflict")
            VALUES ({Guid.NewGuid()}, {uncovered}, 'crunchyroll', 'UNCOVERED', '/watch/UNCOVERED', 1, {now}, {now}, true, false),
                   ({Guid.NewGuid()}, {divergent}, 'crunchyroll', 'DIVERGENT', '/watch/DIVERGENT', 1, {now}, {now}, true, false),
                   ({Guid.NewGuid()}, {siblingCovered}, 'crunchyroll', 'SIBLINGBASE', '/watch/SIBLINGBASE', 1, {now}, {now}, true, false),
                   ({Guid.NewGuid()}, {localizedSibling}, 'crunchyroll', 'LOCALIZED', '/watch/LOCALIZED', 1, {now}, {now}, true, false),
                   ({Guid.NewGuid()}, {standalone}, 'crunchyroll', 'MOVIE', '/watch/MOVIE', 1, {now}, {now}, true, false);
            INSERT INTO "MediaProviderSeasonMappings" ("Id", "Provider", "ProviderSeriesId",
                "ProviderSeasonId", "ProviderSeasonNumber", "MediaTitleId", "EpisodeOffset",
                "MappingSource", "Confidence", "HasConflict", "FirstSeenAt", "LastVerifiedAt")
            VALUES ({Guid.NewGuid()}, 'crunchyroll', 'SERIES', 'COVERED', 1, {second.Id}, 0,
                'provider_episode_identity', 1, false, {now}, {now});
            """);

        await db.Database.MigrateAsync();
        var direct = await db.MediaProviderEpisodeMappings.ToDictionaryAsync(row => row.ProviderEpisodeId);
        Assert.Equal("automatic", direct["UNCOVERED"].MappingSource);
        Assert.Equal("manual", direct["DIVERGENT"].MappingSource);
        Assert.DoesNotContain("SIBLINGBASE", direct.Keys);
        Assert.Equal("manual", direct["LOCALIZED"].MappingSource);
        Assert.Equal("manual", direct["MOVIE"].MappingSource);

        var mappings = new MediaProviderSeasonMappingService(db, NullLogger<MediaProviderSeasonMappingService>.Instance);
        var playback = new MediaEpisodeIdentityService(db, mappings, NullLogger<MediaEpisodeIdentityService>.Instance);
        var firstCatalog = Assert.IsType<MediaEpisodeCatalogDto>(await playback.GetEpisodeCatalogAsync(first.Id, default));
        var episodeTwo = Assert.Single(firstCatalog.Episodes, item => item.EpisodeNumber == 2);
        Assert.Contains(episodeTwo.Destinations, item => item.Url == "https://www.crunchyroll.com/watch/UNCOVERED");
        Assert.Contains(episodeTwo.Destinations, item => item.Url == "https://www.crunchyroll.com/watch/DIVERGENT");
        Assert.Contains(episodeTwo.Destinations, item => item.Url == "https://www.crunchyroll.com/watch/LOCALIZED");
        var secondCatalogBefore = Assert.IsType<MediaEpisodeCatalogDto>(await playback.GetEpisodeCatalogAsync(second.Id, default));
        Assert.DoesNotContain(secondCatalogBefore.Episodes.SelectMany(item => item.Destinations),
            item => item.Url == "https://www.crunchyroll.com/watch/LOCALIZED");
        var movieCatalog = Assert.IsType<MediaEpisodeCatalogDto>(await playback.GetEpisodeCatalogAsync(movie.Id, default));
        Assert.Equal("https://www.crunchyroll.com/watch/MOVIE", Assert.Single(movieCatalog.StandaloneDestinations).Url);

        Assert.NotNull(await mappings.EstablishAsync("crunchyroll", "SERIES", "UNCOVERED", 2, second.Id, -1,
            "manual", 1m, default, episodeStart: 2, episodeEnd: 2));
        var corrected = Assert.IsType<MediaEpisodeCatalogDto>(await playback.GetEpisodeCatalogAsync(second.Id, default));
        Assert.Equal("https://www.crunchyroll.com/watch/UNCOVERED",
            Assert.Single(Assert.Single(corrected.Episodes, item => item.EpisodeNumber == 1).Destinations).Url);
        Assert.DoesNotContain(corrected.Episodes.SelectMany(item => item.Destinations),
            item => item.Url is "https://www.crunchyroll.com/watch/DIVERGENT" or "https://www.crunchyroll.com/watch/LOCALIZED" or "https://www.crunchyroll.com/watch/MOVIE");

        static MediaTitle CreateSeries(string name) => new()
        {
            Id = Guid.NewGuid(), CanonicalTitle = name, MediaKind = MediaKinds.Anime, EpisodeCount = 12,
            SupportsEpisodeProgress = true, PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode
        };
    }

    [PostgresFact]
    public async Task ProviderEpisodeMigrationBackfillsRawFactsAndPreservesExplicitManualMappings()
    {
        await using var schema = await SchemaContext.CreateAsync(createTables: false);
        var db = schema.Db;
        await db.GetService<IMigrator>().MigrateAsync("20261114093133_RetainCandidateVersionAnchor");
        var titleId = Guid.NewGuid();
        var mangaTitleId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        var contentId = Guid.NewGuid();
        var secondEpisodeId = Guid.NewGuid();
        var secondContentId = Guid.NewGuid();
        var unscopedContentId = Guid.NewGuid();
        var manualSeasonMappingId = Guid.NewGuid();
        var latestAutomaticSeasonMappingId = Guid.NewGuid();
        var legacyWatchId = Guid.NewGuid();
        var newestDuplicateWatchId = Guid.NewGuid();
        var retriedDuplicateWatchId = Guid.NewGuid();
        var futureDuplicateWatchId = Guid.NewGuid();
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var earlier = now.AddDays(-1);
        var future = now.AddYears(73);
        var viewer = TestUserFactory.Create(713, "migrated-episode-viewer@example.test");
        db.Users.Add(viewer);
        db.MediaTitles.Add(new MediaTitle
        {
            Id = titleId, CanonicalTitle = "Legacy title", MediaKind = MediaKinds.Anime,
            SupportsEpisodeProgress = true, PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode, CreatedAt = now, UpdatedAt = now
        });
        db.MediaTitles.Add(new MediaTitle
        {
            Id = mangaTitleId, CanonicalTitle = "Legacy manga", MediaKind = MediaKinds.Manga,
            SupportsChapterProgress = true, PrimaryProgressDimension = MediaProgressDimensions.Chapter,
            ReleaseStatusDimension = MediaProgressDimensions.Chapter, CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "MediaObservations" ("Id", "UserId", "SiteIdentifier", "SiteMediaId", "ObservedTitle",
                "ObservedUrl", "ObservedAt", "IsCatalogObservation", "MatchStatus", "MatchAttemptCount", "UpdatedAt")
                VALUES ({legacyWatchId}, {viewer.Id}, 'crunchyroll', 'abc123', 'Pending watch',
                        'https://www.crunchyroll.com/watch/abc123', {future}, false, 'pending', 0, {earlier}),
                       ({newestDuplicateWatchId}, {viewer.Id}, 'crunchyroll', ' dup123 ', 'Newer watch',
                        'https://www.crunchyroll.com/watch/dup123', {now}, false, 'pending', 0, {now}),
                       ({retriedDuplicateWatchId}, {viewer.Id}, 'crunchyroll', 'DUP123', 'Older retried watch',
                        'https://www.crunchyroll.com/watch/DUP123', {earlier}, false, 'pending', 1, {now.AddMinutes(3)}),
                       ({futureDuplicateWatchId}, {viewer.Id}, 'crunchyroll', 'dup123', 'Watch with a future client clock',
                        'https://www.crunchyroll.com/watch/dup123', {future}, false, 'pending', 0, {earlier}),
                       ({Guid.NewGuid()}, {viewer.Id}, 'crunchyroll', 'catalog:series:SEASON1', 'Catalog observation',
                        'https://www.crunchyroll.com/series/GSERIES1', {earlier}, true, 'pending', 0, {now});
            INSERT INTO "MediaEpisodes" ("Id", "MediaTitleId", "EpisodeNumber", "Title",
                "AvailableSubtitleLanguageCodes", "AvailableAudioLanguageCodes")
                VALUES ({episodeId}, {titleId}, 1, 'SIMKL title', ARRAY['en','fr'], ARRAY['de']),
                       ({secondEpisodeId}, {titleId}, 2, 'Crunchyroll title', ARRAY['en'], ARRAY['de']);
            INSERT INTO "MediaEpisodeProviderContents" ("Id", "MediaEpisodeId", "Provider",
                "ProviderContentKey", "ProviderSeriesId", "ProviderSeasonId", "ProviderSeasonNumber", "ProviderEpisodeNumber")
                VALUES ({contentId}, {episodeId}, 'crunchyroll', ' ge00340376 ', 'gseries1', 'season1', 1, 1),
                       ({Guid.NewGuid()}, {episodeId}, 'simkl', 'simkl-content', 'simkl-series', null, 1, 1),
                       ({secondContentId}, {secondEpisodeId}, 'crunchyroll', 'SECOND', 'GSERIES1', 'SEASON3', 3, 2),
                       ({unscopedContentId}, {secondEpisodeId}, 'crunchyroll', 'UNSCOPED', null, null, null, 2);
            INSERT INTO "MediaEpisodeProviderIdentities" ("Id", "MediaEpisodeProviderContentId", "Provider",
                "ProviderEpisodeId", "ProviderUrlPath", "AudioLocale", "ReleaseTrack", "SeenCount", "FirstSeenAt", "LastSeenAt",
                "IsTrusted", "HasConflict")
                VALUES ({Guid.NewGuid()}, {contentId}, 'crunchyroll', 'GE00340376ENUS', '/watch/GE00340376ENUS', 'en-US', null, 7,
                        {now}, {now}, true, false),
                       ({Guid.NewGuid()}, {contentId}, 'crunchyroll', 'GE00340376JAJP', '/watch/GE00340376JAJP', 'ja-JP', 'sub:fr', 2,
                        {earlier}, {future}, true, false),
                       ({Guid.NewGuid()}, {secondContentId}, 'crunchyroll', 'SECOND', '/watch/SECOND', null, 'dub:es', 1,
                        {future}, {future}, true, false),
                       ({Guid.NewGuid()}, {unscopedContentId}, 'crunchyroll', 'UNSCOPED', '/watch/UNSCOPED', null, 'sub:en', 1,
                        {earlier}, {now}, false, false),
                       ({Guid.NewGuid()}, {unscopedContentId}, 'crunchyroll', 'CONFIRMED', '/watch/CONFIRMED', null, 'dub:en', 1,
                        {earlier}, {future}, true, false),
                       ({Guid.NewGuid()}, {contentId}, 'crunchyroll', 'AMBIGUOUS', '/watch/AMBIGUOUS', null, null, 1,
                        {now}, {now}, true, false),
                       ({Guid.NewGuid()}, {secondContentId}, 'crunchyroll', 'ambiguous', '/watch/AMBIGUOUS', null, null, 1,
                        {now}, {now}, true, false),
                       ({Guid.NewGuid()}, {secondContentId}, 'crunchyroll', 'DISPUTED', '/watch/DISPUTED', null, null, 1,
                        {now}, {now}, false, true),
                       ({Guid.NewGuid()}, {contentId}, 'crunchyroll', 'UNSAFE', 'https://example.com/watch/UNSAFE', null, null, 1,
                        {now}, {now}, false, false);
            INSERT INTO "MediaProviderSeasonMappings" ("Id", "Provider", "ProviderSeriesId",
                "ProviderSeasonId", "ProviderSeasonNumber", "MediaTitleId", "EpisodeOffset",
                "MappingSource", "Confidence", "HasConflict", "FirstSeenAt", "LastVerifiedAt")
                VALUES ({Guid.NewGuid()}, 'crunchyroll', 'GSERIES1', 'SEASON2', 2, {titleId}, 0,
                        'provider_episode_identity', 1, true, {now}, {now}),
                       ({manualSeasonMappingId}, 'crunchyroll', 'GSERIES1', 'SEASON1', 1, {titleId}, 0,
                        'manual', 1, false, {earlier}, {earlier}),
                       ({Guid.NewGuid()}, ' Crunchyroll ', 'GSERIES1', ' season1 ', 1, {mangaTitleId}, 0,
                        'manual', 1, false, {now}, {now}),
                       ({Guid.NewGuid()}, ' Crunchyroll ', ' gseries1 ', ' season1 ', 1, {titleId}, 1,
                        'provider_episode_identity', 1, false, {now}, {now}),
                       ({Guid.NewGuid()}, 'crunchyroll', ' gseries1 ', ' season3 ', 3, {titleId}, 0,
                        'provider_episode_identity', 1, false, {earlier}, {earlier}),
                       ({latestAutomaticSeasonMappingId}, 'crunchyroll', 'GSERIES1', 'SEASON3', 3, {titleId}, 0,
                        'provider_episode_identity', 1, false, {now}, {now}),
                       ({Guid.NewGuid()}, 'crunchyroll', 'GSERIES1', 'SEASON4', 4, {titleId}, 0,
                        'manual', 1, false, {earlier}, {earlier}),
                       ({Guid.NewGuid()}, 'crunchyroll', 'GSERIES1', ' season4 ', 4, {titleId}, 1,
                        'manual', 1, false, {now}, {now}),
                       ({Guid.NewGuid()}, 'crunchyroll', 'GSERIES1', 'SEASON5', 5, {titleId}, 0,
                        'provider_episode_identity', 1, false, {earlier}, {earlier}),
                       ({Guid.NewGuid()}, 'crunchyroll', 'GSERIES1', ' season5 ', 5, {titleId}, 1,
                        'provider_episode_identity', 1, false, {now}, {now});
            """);

        await db.Database.MigrateAsync();
        var episodes = await db.MediaProviderEpisodes.ToDictionaryAsync(row => row.ProviderEpisodeId);
        Assert.Equal(5, episodes.Count);
        Assert.Contains("UNSCOPED", episodes.Keys);
        Assert.DoesNotContain("AMBIGUOUS", episodes.Keys);
        var raw = episodes["GE00340376ENUS"];
        Assert.Equal("GE00340376", raw.ContentKey);
        Assert.Equal(7, raw.SeenCount);
        Assert.Equal("GSERIES1", raw.ProviderSeriesId);
        Assert.Equal("SEASON1", raw.ProviderSeasonId);
        Assert.Equal(1, raw.ProviderEpisodeNumber);
        Assert.Null(raw.EpisodeTitle);
        Assert.Empty(raw.AvailableSubtitleLanguageCodes);
        Assert.Equal(["en"], raw.AvailableAudioLanguageCodes);
        Assert.Equal(now, raw.FirstSeenAt);
        Assert.Equal(now, raw.LastSeenAt);
        Assert.Null(episodes["GE00340376JAJP"].EpisodeTitle);
        Assert.Equal(["fr"], episodes["GE00340376JAJP"].AvailableSubtitleLanguageCodes);
        Assert.Empty(episodes["GE00340376JAJP"].AvailableAudioLanguageCodes);
        Assert.Equal(earlier, episodes["GE00340376JAJP"].FirstSeenAt);
        Assert.InRange(episodes["GE00340376JAJP"].LastSeenAt, now, DateTimeOffset.UtcNow);
        Assert.Equal("Crunchyroll title", episodes["SECOND"].EpisodeTitle);
        Assert.Equal(["es"], episodes["SECOND"].AvailableAudioLanguageCodes);
        Assert.InRange(episodes["SECOND"].FirstSeenAt, now, DateTimeOffset.UtcNow);
        Assert.Equal(episodes["SECOND"].FirstSeenAt, episodes["SECOND"].LastSeenAt);
        Assert.DoesNotContain("DISPUTED", episodes.Keys);
        Assert.Equal("simkl-content", (await db.MediaEpisodeProviderContents.SingleAsync()).ProviderContentKey);
        Assert.Equal("SIMKL title", (await db.MediaEpisodes.FindAsync(episodeId))!.Title);
        var directMappings = await db.MediaProviderEpisodeMappings.ToDictionaryAsync(row => row.ProviderEpisodeId);
        var confirmed = Assert.Single(directMappings).Value;
        Assert.Equal("CONFIRMED", confirmed.ProviderEpisodeId);
        Assert.Equal(titleId, confirmed.MediaTitleId);
        Assert.Equal(2, confirmed.CanonicalEpisodeNumber);
        Assert.Equal(MediaProviderSeasonMappingSources.Automatic, confirmed.MappingSource);
        Assert.Equal(episodes["CONFIRMED"].LastSeenAt, confirmed.LastObservedAt);
        var mappings = await db.MediaProviderSeasonMappings.OrderBy(row => row.ProviderSeasonKey).ToListAsync();
        Assert.Collection(mappings,
            row =>
            {
                Assert.Equal(manualSeasonMappingId, row.Id);
                Assert.Equal("id:SEASON1", row.ProviderSeasonKey);
                Assert.Equal("manual", row.MappingSource);
                Assert.Null(row.LastObservedAt);
            },
            row =>
            {
                Assert.Equal(latestAutomaticSeasonMappingId, row.Id);
                Assert.Equal("GSERIES1", row.ProviderSeriesId);
                Assert.Equal("SEASON3", row.ProviderSeasonId);
                Assert.Equal("id:SEASON3", row.ProviderSeasonKey);
                Assert.Equal("automatic", row.MappingSource);
                Assert.Equal(now, row.LastVerifiedAt);
                Assert.Equal(row.LastVerifiedAt, row.LastObservedAt);
            });
        var service = new MediaProviderSeasonMappingService(db, NullLogger<MediaProviderSeasonMappingService>.Instance);
        Assert.Null(await service.FindAsync("crunchyroll", "GSERIES1", "SEASON2", 2, default));
        Assert.Null(await service.FindAsync("crunchyroll", "GSERIES1", "SEASON4", 4, default));
        Assert.Null(await service.FindAsync("crunchyroll", "GSERIES1", "SEASON5", 5, default));
        var published = await MediaProviderSeasonMappingService.GetMappedEpisodesAsync(db, [titleId], default);
        Assert.DoesNotContain(published, row => row.Episode.ProviderUrlPath is "/watch/DISPUTED" or "/watch/AMBIGUOUS" or "/watch/UNSCOPED");

        db.MediaLibraryEntries.Add(new MediaLibraryEntry
        {
            Id = Guid.NewGuid(), UserId = viewer.Id, MediaTitleId = titleId,
            Status = MediaLibraryStatuses.Current, ProgressEpisodes = 0
        });
        await db.SaveChangesAsync();
        var playback = new MediaEpisodeIdentityService(db, service, NullLogger<MediaEpisodeIdentityService>.Instance);
        const string migratedUrl = "https://www.crunchyroll.com/watch/GE00340376JAJP";
        var catalog = await playback.GetEpisodeCatalogAsync(titleId, default);
        var firstEpisode = Assert.Single(Assert.IsType<MediaEpisodeCatalogDto>(catalog).Episodes, row => row.EpisodeNumber == 1);
        Assert.Contains(firstEpisode.Destinations, destination => destination.Url == migratedUrl);
        Assert.DoesNotContain(firstEpisode.Destinations, destination => destination.Url == "https://www.crunchyroll.com/watch/AMBIGUOUS");
        var secondEpisode = Assert.Single(catalog!.Episodes, row => row.EpisodeNumber == 2);
        Assert.DoesNotContain(secondEpisode.Destinations, destination => destination.Url == "https://www.crunchyroll.com/watch/UNSCOPED");
        Assert.Contains(secondEpisode.Destinations, destination => destination.Url == "https://www.crunchyroll.com/watch/CONFIRMED");
        var next = Assert.IsType<MediaContinueWatchingDto>(await playback.ResolveContinueWatchingAsync(viewer.Id, titleId, default));
        Assert.Equal("direct", next.Outcome);
        Assert.Equal(1, next.EpisodeNumber);
        Assert.Equal(migratedUrl, next.Url);

        var legacyWatch = await db.MediaObservations.AsNoTracking().SingleAsync(row => row.Id == legacyWatchId);
        Assert.Equal(earlier, legacyWatch.ObservedAt);
        var controller = CreateObservationsController(db, new MediaProviderRegistry([]), viewer.Id, viewer.Email!);
        var currentReportAt = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var submission = await controller.Submit(new SubmitMediaObservationRequest
        {
            SiteIdentifier = "crunchyroll", SiteMediaId = "ABC123", ObservedTitle = "Updated pending watch",
            ObservedUrl = "https://www.crunchyroll.com/watch/ABC123", EpisodeNumber = 1, ObservedAt = currentReportAt
        }, default);
        var response = Assert.IsType<SubmitMediaObservationResponse>(Assert.IsType<OkObjectResult>(submission.Result).Value);
        Assert.True(response.WasDeduplicated);
        Assert.Equal(legacyWatchId.ToString(), response.ObservationId);
        var observations = await db.MediaObservations.AsNoTracking().Where(row => row.UserId == viewer.Id)
            .ToDictionaryAsync(row => row.SiteMediaId!);
        Assert.Equal(3, observations.Count);
        Assert.Equal(legacyWatchId, observations["ABC123"].Id);
        Assert.Equal("Updated pending watch", observations["ABC123"].ObservedTitle);
        Assert.Equal(currentReportAt, observations["ABC123"].ObservedAt);
        Assert.Equal(newestDuplicateWatchId, observations["DUP123"].Id);
        Assert.Equal(now, observations["DUP123"].ObservedAt);
        Assert.DoesNotContain(observations.Values, row => row.Id == retriedDuplicateWatchId || row.Id == futureDuplicateWatchId);
        Assert.True(observations["catalog:series:SEASON1"].IsCatalogObservation);
        Assert.Equal(earlier, observations["catalog:series:SEASON1"].ObservedAt);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());

        var replacement = new MediaTitle
        {
            Id = Guid.NewGuid(), CanonicalTitle = "Replacement series", MediaKind = MediaKinds.Anime,
            EpisodeCount = 12, SupportsEpisodeProgress = true, PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode
        };
        db.MediaTitles.Add(replacement);
        await db.SaveChangesAsync();
        var catalogController = new MediaCatalogObservationsController(db, CreateUserManager(db),
            new MediaObservationMatchingService(db, service, new MediaRelationGraphRefreshQueue(),
                NullLogger<MediaObservationMatchingService>.Instance),
            playback, NullLogger<MediaCatalogObservationsController>.Instance)
        {
            ControllerContext = controller.ControllerContext
        };

        await SubmitCatalogAsync(earlier, titleId, now);
        var newerReportAt = now.AddTicks(TimeSpan.TicksPerMicrosecond);
        await SubmitCatalogAsync(newerReportAt, replacement.Id, newerReportAt);

        async Task SubmitCatalogAsync(DateTimeOffset observedAt, Guid expectedTitleId, DateTimeOffset expectedEvidenceAt)
        {
            var result = await catalogController.Submit(new SubmitMediaCatalogObservationRequest
            {
                Provider = "crunchyroll", ProviderSeriesId = "GSERIES1", ProviderSeasonId = "SEASON3", SeasonNumber = 3,
                SeriesUrl = "https://www.crunchyroll.com/series/GSERIES1", SeriesTitle = replacement.CanonicalTitle,
                ObservedAt = observedAt,
                Episodes = [new()
                {
                    ProviderEpisodeId = "SEASONTHREE", EpisodeNumber = 2,
                    ProviderUrl = "https://www.crunchyroll.com/watch/SEASONTHREE"
                }]
            }, default);
            var catalogResponse = Assert.IsType<SubmitMediaCatalogObservationResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
            Assert.Equal(expectedTitleId.ToString(), catalogResponse.MatchedMediaTitleId);
            var mapping = await db.MediaProviderSeasonMappings.AsNoTracking()
                .SingleAsync(row => row.Id == latestAutomaticSeasonMappingId);
            Assert.Equal(expectedTitleId, mapping.MediaTitleId);
            Assert.Equal(expectedEvidenceAt, mapping.LastObservedAt);
        }
    }

    [PostgresFact]
    public async Task GetMappedEpisodes_ResolvesSeasonOffsetAndSeriesLessVariantOnPostgres()
    {
        await using var schema = await SchemaContext.CreateAsync();
        var db = schema.Db;
        var title = new MediaTitle
        {
            Id = Guid.NewGuid(), CanonicalTitle = "Mapped title", MediaKind = MediaKinds.Anime,
            SupportsEpisodeProgress = true,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode
        };
        db.MediaTitles.Add(title);
        db.MediaProviderEpisodes.AddRange(
            new MediaProviderEpisode
            {
                Provider = "crunchyroll", ProviderEpisodeId = "GE00340376JAJP", ProviderUrlPath = "/watch/GE00340376JAJP",
                ProviderSeriesId = "SERIES", ProviderSeasonNumber = 2, ProviderEpisodeNumber = 13,
                ContentKey = "GE00340376", SeenCount = 1
            },
            new MediaProviderEpisode
            {
                Provider = "crunchyroll", ProviderEpisodeId = "GE00340376ENUS", ProviderUrlPath = "/watch/GE00340376ENUS",
                ContentKey = "GE00340376", SeenCount = 1
            });
        await db.SaveChangesAsync();
        var service = new MediaProviderSeasonMappingService(db, NullLogger<MediaProviderSeasonMappingService>.Instance);
        await service.EstablishAsync("crunchyroll", "SERIES", null, 2, title.Id, -12, "automatic", 1m, default);

        var mapped = await MediaProviderSeasonMappingService.GetMappedEpisodesAsync(db, [title.Id], default);
        Assert.Equal(["GE00340376ENUS", "GE00340376JAJP"], mapped.Select(row => row.Episode.ProviderEpisodeId).Order());
        Assert.All(mapped, row => Assert.Equal(1, row.EpisodeNumber));
    }

    [PostgresFact]
    public async Task ProgressRevalidatesProviderMappingAfterConcurrentEdit()
    {
        await using var schema = await SchemaContext.CreateAsync();
        await using var editor = new ApplicationDbContext(schema.Options);
        await MediaEpisodeIdentityRemappingPolicyTests.AssertProgressRevalidatesAsync(schema.Db, editor);
    }

    [PostgresFact]
    public async Task ImportAsync_RechecksRemoteProgressAfterConcurrentTrackerCompletion()
    {
        await using var schema = await SchemaContext.CreateAsync();
        var user = TestUserFactory.Create(47812, "tracker-import-race@example.test");
        var account = new ConnectedServiceAccount
        {
            UserId = user.Id, Service = "anilist", ExternalAccountId = "tracker-import-race",
            ConnectionState = "connected", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        schema.Db.AddRange(user, account);
        await schema.Db.SaveChangesAsync();

        var importedAt = DateTimeOffset.FromUnixTimeSeconds(1_795_000_000);
        var item = new MediaProviderLibraryItem
        {
            ProviderMediaId = "1", Title = "Tracker import fixture", MediaKind = MediaKinds.Anime,
            PrimaryProgressDimension = MediaProgressDimensions.Episode, ReleaseStatusDimension = MediaProgressDimensions.Episode,
            Status = MediaLibraryStatuses.Current, ProgressEpisodes = 0, LastRemoteUpdateAt = importedAt
        };
        var service = new MediaLibraryImportService(schema.Db, new MediaLibraryEventHub(),
            MakeService(schema.Options), NullLogger<MediaLibraryImportService>.Instance);
        await service.ImportAsync(user.Id, account, new MediaProviderLibraryImportResult
        {
            ProviderId = "anilist", ImportedAt = importedAt, Items = [item]
        }, default);

        var completedAt = importedAt.AddMinutes(2);
        var interleaved = new DetailUpdateBeforeSave(ct => schema.Db.MediaLibraryEntries.ExecuteUpdateAsync(setters => setters
            .SetProperty(entry => entry.ProgressEpisodes, 1)
            .SetProperty(entry => entry.LastMutationSource, MediaMutationSources.AioStreamsWatchState)
            .SetProperty(entry => entry.LastLocalEditAt, completedAt)
            .SetProperty(entry => entry.UpdatedAt, completedAt), ct));
        var racingOptions = new DbContextOptionsBuilder<ApplicationDbContext>(schema.Options)
            .AddInterceptors(interleaved).Options;
        await using var caller = new ApplicationDbContext(racingOptions);
        service = new MediaLibraryImportService(caller, new MediaLibraryEventHub(),
            MakeService(racingOptions), NullLogger<MediaLibraryImportService>.Instance);
        item.ProgressEpisodes = 3;
        item.LastRemoteUpdateAt = importedAt.AddMinutes(1);
        await service.ImportAsync(user.Id, account, new MediaProviderLibraryImportResult
        {
            ProviderId = "anilist", ImportedAt = importedAt.AddMinutes(3), Items = [item]
        }, default);

        var persisted = await schema.Db.MediaLibraryEntries.AsNoTracking().SingleAsync();
        Assert.Equal(1, persisted.ProgressEpisodes);
        Assert.Equal(completedAt, persisted.LastLocalEditAt);
        Assert.Equal(MediaMutationSources.AioStreamsWatchState, persisted.LastMutationSource);
        Assert.Equal(2, interleaved.SaveAttempts);
    }

    [PostgresFact]
    public async Task CanonicalizeAsync_RetriesSummaryFillWhenDetailsChangeAfterPreload()
    {
        await using var schema = await SchemaContext.CreateAsync();
        var candidate = Candidate("anilist", "211778", "The World's Strongest Witch", "10");
        var ids = await MakeService(schema.Options).CanonicalizeAsync([candidate], default);
        var titleId = Assert.Single(ids.Values);
        var interleaved = new DetailUpdateBeforeSave(async cancellationToken =>
        {
            var title = await schema.Db.MediaTitles.SingleAsync(item => item.Id == titleId, cancellationToken);
            title.Synopsis = "Full details loaded concurrently";
            title.Synonyms.Add("Detailed alias");
            await schema.Db.SaveChangesAsync(cancellationToken);
        });
        var racingOptions = new DbContextOptionsBuilder<ApplicationDbContext>(schema.Options)
            .AddInterceptors(interleaved).Options;
        var summary = candidate with { Result = candidate.Result with
        {
            Synopsis = "Less detailed summary",
            Synonyms = ["Search alias"]
        } };

        await MakeService(racingOptions).CanonicalizeAsync([summary], default);

        var persisted = await schema.Db.MediaTitles.AsNoTracking().SingleAsync(item => item.Id == titleId);
        Assert.Equal("Full details loaded concurrently", persisted.Synopsis);
        Assert.Contains("Detailed alias", persisted.Synonyms);
        Assert.Contains("Search alias", persisted.Synonyms);
        Assert.Equal(1, interleaved.DetailWrites);
        Assert.Equal(2, interleaved.SaveAttempts);
    }

    [PostgresFact]
    public async Task CanonicalizeAsync_RetriesSortTitleFillWhenOnlyDetailedNamesChangeAfterPreload()
    {
        await using var schema = await SchemaContext.CreateAsync();
        var candidate = Candidate("anilist", "sort-title-race", "Summary title", "10");
        var ids = await MakeService(schema.Options).CanonicalizeAsync([candidate], default);
        var titleId = Assert.Single(ids.Values);
        var title = await schema.Db.MediaTitles.SingleAsync(item => item.Id == titleId);
        title.SortTitle = null;
        await schema.Db.SaveChangesAsync();
        var originalSynonyms = title.Synonyms.ToArray();
        var interleaved = new DetailUpdateBeforeSave(async cancellationToken =>
        {
            // Hydration can correct just the name without changing any other metadata.
            title.CanonicalTitle = "Detailed canonical title";
            title.SortTitle = "Detailed canonical title";
            await schema.Db.SaveChangesAsync(cancellationToken);
        });
        var racingOptions = new DbContextOptionsBuilder<ApplicationDbContext>(schema.Options)
            .AddInterceptors(interleaved).Options;

        await MakeService(racingOptions).CanonicalizeAsync([candidate], default);

        var persisted = await schema.Db.MediaTitles.AsNoTracking().SingleAsync(item => item.Id == titleId);
        Assert.Equal("Detailed canonical title", persisted.CanonicalTitle);
        Assert.Equal(persisted.CanonicalTitle, persisted.SortTitle);
        Assert.Equal(originalSynonyms, persisted.Synonyms);
        Assert.Equal(1, interleaved.DetailWrites);
        Assert.Equal(2, interleaved.SaveAttempts);
    }

    [PostgresFact]
    public async Task ImportAsync_RetriesIdentityRaceAtomicallyWithoutSavingCallerContextChanges()
    {
        await using var schema = await SchemaContext.CreateAsync();
        var user = TestUserFactory.Create(47811, "catalog-import-race@example.test");
        var account = new ConnectedServiceAccount
        {
            UserId = user.Id,
            Service = "anilist",
            ExternalAccountId = "catalog-import-race-account",
            ConnectionState = "connected",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        schema.Db.AddRange(user, account);
        await schema.Db.SaveChangesAsync();

        var candidateResult = Candidate("anilist", "211778", "The World's Strongest Witch", "10").Result
            with { CrossReferences = [] };
        var candidate = new MediaCatalogSearchCandidate("anilist", candidateResult);
        var interleaved = new ImportIdentityClaimBeforeSave(() => MakeService(schema.Options)
            .CanonicalizeAsync([candidate], default));
        var importOptions = new DbContextOptionsBuilder<ApplicationDbContext>(schema.Options)
            .AddInterceptors(interleaved).Options;
        await using var callerContext = new ApplicationDbContext(importOptions);
        var pendingChange = new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = "Unrelated caller change",
            MediaKind = "anime",
            PrimaryProgressDimension = "episode",
            ReleaseStatusDimension = "episode",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        callerContext.MediaTitles.Add(pendingChange);
        var importService = new MediaLibraryImportService(callerContext, new MediaLibraryEventHub(),
            MakeService(importOptions), NullLogger<MediaLibraryImportService>.Instance);
        var importedAt = DateTimeOffset.UtcNow;
        var import = new MediaProviderLibraryImportResult
        {
            ProviderId = "anilist",
            ImportedAt = importedAt,
            Items =
            [
                new MediaProviderLibraryItem
                {
                    ProviderMediaId = "211778",
                    Title = "The World's Strongest Witch",
                    MediaKind = "anime",
                    Status = MediaLibraryStatuses.Planned,
                    ProgressEpisodes = 3,
                    EpisodeCount = 12,
                    PrimaryProgressDimension = "episode",
                    ReleaseStatusDimension = "episode",
                    CrossReferences = []
                }
            ]
        };

        var result = await importService.ImportAsync(user.Id, account, import, default);

        Assert.Equal(1, interleaved.ClaimCount);
        Assert.Equal(2, interleaved.SaveAttempts);
        Assert.Equal(0, result.CreatedTitles);
        Assert.Equal(1, result.CreatedEntries);
        Assert.Equal(1, await schema.Db.MediaTitles.CountAsync());
        Assert.Equal(1, await schema.Db.MediaProviderLinks.CountAsync());
        var entry = await schema.Db.MediaLibraryEntries.SingleAsync();
        Assert.Equal(user.Id, entry.UserId);
        Assert.Equal(3, entry.ProgressEpisodes);
        var binding = await schema.Db.MediaLibraryProviderBindings.Include(item => item.MediaProviderLink).SingleAsync();
        Assert.Equal(account.Id, binding.ConnectedServiceAccountId);
        Assert.Equal("211778", binding.MediaProviderLink!.ExternalId);
        Assert.Equal(entry.MediaTitleId, binding.MediaProviderLink.MediaTitleId);
        Assert.Equal(EntityState.Added, callerContext.Entry(pendingChange).State);
        Assert.False(await schema.Db.MediaTitles.AnyAsync(title => title.Id == pendingChange.Id));

        pendingChange.Synopsis = "Caller context still works";
        await callerContext.SaveChangesAsync();
        Assert.Equal("Caller context still works", await schema.Db.MediaTitles.AsNoTracking()
            .Where(title => title.Id == pendingChange.Id).Select(title => title.Synopsis).SingleAsync());
        Assert.Equal(1, await schema.Db.MediaLibraryEntries.CountAsync());
    }

    [PostgresFact]
    public async Task Resolve_AddToLibrary_RetriesProviderIdentityRaceWithoutRepeatingRemoteCalls()
    {
        await using var schema = await SchemaContext.CreateAsync();
        const int userId = 47812;
        const string providerId = "anilist";
        const string providerMediaId = "observation-race-1";
        var user = TestUserFactory.Create(userId, "observation-add-race@example.test");
        var account = new ConnectedServiceAccount
        {
            UserId = user.Id,
            Service = providerId,
            ExternalAccountId = "observation-race-account",
            ConnectionState = "connected",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var observation = new MediaObservation
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            SiteIdentifier = MediaObservationSiteIdentifiers.Crunchyroll,
            ObservedUrl = "https://www.crunchyroll.com/watch/RACE/episode-3",
            SiteMediaId = "RACE",
            ObservedTitle = "Observation race title episode 3",
            ProgressHint = "3",
            ObservedAt = DateTimeOffset.UtcNow,
            MatchStatus = MediaObservationStatuses.NoMatch,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        schema.Db.AddRange(user, account, observation);
        await schema.Db.SaveChangesAsync();

        var details = new MediaProviderTitleDetails
        {
            ProviderId = providerId,
            ProviderMediaId = providerMediaId,
            Title = "Observation race title",
            MediaKind = MediaKinds.Anime,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            EpisodeCount = 12,
            CrossReferences = []
        };
        var provider = new ObservationRaceMediaProvider(account, details);
        var interleaved = new ProviderIdentityClaimBeforeSave(() => MakeService(schema.Options)
            .CanonicalizeAsync(
            [new MediaCatalogSearchCandidate(providerId, new CachedMediaProviderSearchResult(
                providerMediaId, details.Title, details.MediaKind, details.PosterUrl, details.StartYear)
            {
                PrimaryProgressDimension = details.PrimaryProgressDimension,
                ReleaseStatusDimension = details.ReleaseStatusDimension,
                EpisodeCount = details.EpisodeCount
            })], default));
        var controllerOptions = new DbContextOptionsBuilder<ApplicationDbContext>(schema.Options)
            .AddInterceptors(interleaved).Options;
        await using var controllerDb = new ApplicationDbContext(controllerOptions);
        var controllerObservation = await controllerDb.MediaObservations.SingleAsync(item => item.Id == observation.Id);
        var controller = CreateObservationsController(controllerDb, new MediaProviderRegistry([provider]), userId,
            "observation-add-race@example.test");

        var result = await controller.Resolve(observation.Id, new ResolveMediaObservationRequest
        {
            ProviderId = providerId,
            ProviderMediaId = providerMediaId,
            AddToLibraryConfirmed = true
        }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<MediaObservationDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(MediaObservationStatuses.Matched, response.MatchStatus);
        Assert.Equal("3", response.ProgressHint);
        Assert.Equal(3, response.ResolvedProgress);
        Assert.Equal(1, interleaved.ClaimCount);
        Assert.Equal(1, interleaved.UniqueViolationCount);
        Assert.Equal(1, provider.DetailsCalls);
        Assert.Equal(1, provider.StatusCalls);
        Assert.Equal(1, await schema.Db.MediaTitles.CountAsync());
        Assert.Equal(1, await schema.Db.MediaProviderLinks.CountAsync());
        var entry = await schema.Db.MediaLibraryEntries.SingleAsync();
        Assert.Equal(user.Id, entry.UserId);
        Assert.Equal(3, entry.ProgressEpisodes);
        var binding = await schema.Db.MediaLibraryProviderBindings
            .Include(item => item.MediaProviderLink).SingleAsync();
        Assert.Equal(account.Id, binding.ConnectedServiceAccountId);
        Assert.Equal(providerMediaId, binding.MediaProviderLink!.ExternalId);
        Assert.Equal(entry.MediaTitleId, binding.MediaProviderLink.MediaTitleId);
        Assert.False(await schema.Db.MediaObservations.AsNoTracking()
            .AnyAsync(item => item.Id == observation.Id));
        Assert.Equal(EntityState.Detached, controllerDb.Entry(controllerObservation).State);
        Assert.DoesNotContain(controllerDb.ChangeTracker.Entries(), entry => entry.State == EntityState.Added
            && entry.Entity is MediaTitle or MediaProviderLink or MediaLibraryEntry or MediaLibraryProviderBinding);
    }

    [PostgresFact]
    public async Task GetTitleDetails_RetriesDiscoveryIdentityRaceAndPersistsHydrationAtomically()
    {
        await using var schema = await SchemaContext.CreateAsync();
        const int userId = 47813;
        const string providerId = "simkl";
        const string providerMediaId = "details-discovery-race";
        const string titleName = "Details discovered during identity race";
        schema.Db.Users.Add(TestUserFactory.Create(userId, "details-discovery-race@example.test"));
        await schema.Db.SaveChangesAsync();
        var details = new MediaProviderTitleDetails
        {
            ProviderId = providerId,
            ProviderMediaId = providerMediaId,
            ExternalUrl = "https://simkl.com/anime/12345",
            Title = titleName,
            MediaKind = MediaKinds.Anime,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            EpisodeCount = 2,
            AvailabilityLinks =
            [
                new MediaProviderAvailabilityLink
                {
                    ServiceId = "crunchyroll",
                    DisplayName = "Crunchyroll",
                    Url = "https://www.crunchyroll.com/series/example",
                    AvailabilityKind = "subscription"
                }
            ],
            EpisodeCatalog =
            [
                new MediaProviderWatchedEpisode { SeasonNumber = 1, EpisodeNumber = 1, ProviderEpisodeId = "episode-1", Title = "First" },
                new MediaProviderWatchedEpisode { SeasonNumber = 1, EpisodeNumber = 2, ProviderEpisodeId = "episode-2", Title = "Second" }
            ],
            SpecialEpisodeCatalog =
            [
                new MediaProviderWatchedEpisode { SeasonNumber = 0, EpisodeNumber = 1, ProviderEpisodeId = "special-1", Title = "Special" }
            ]
        };
        var account = new ConnectedServiceAccount
        {
            UserId = userId,
            Service = providerId,
            ExternalAccountId = "details-discovery-race-account",
            ConnectionState = "connected",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var provider = new ObservationRaceMediaProvider(account, details);
        var candidate = new MediaCatalogSearchCandidate(providerId, new CachedMediaProviderSearchResult(
            providerMediaId, titleName, MediaKinds.Anime, null, null)
        {
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            EpisodeCount = 2,
            CrossReferences = []
        });
        var interleaved = new ProviderIdentityClaimBeforeSave(() => MakeService(schema.Options)
            .CanonicalizeAsync([candidate], default));
        var controllerOptions = new DbContextOptionsBuilder<ApplicationDbContext>(schema.Options)
            .AddInterceptors(interleaved).Options;
        await using var controllerDb = new ApplicationDbContext(controllerOptions);
        var registry = new MediaProviderRegistry([provider]);
        var controller = CreateMediaProvidersController(controllerDb, registry, userId,
            "details-discovery-race@example.test");

        var result = await controller.GetTitleDetails(providerId, providerMediaId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<MediaProviderTitleDetailsDto>(ok.Value);
        var canonicalLink = await schema.Db.MediaProviderLinks.AsNoTracking().SingleAsync();
        Assert.Equal(canonicalLink.MediaTitleId, response.MediaTitleId);
        Assert.Equal(titleName, (await schema.Db.MediaTitles.AsNoTracking().SingleAsync()).CanonicalTitle);
        Assert.Equal(1, await schema.Db.MediaTitles.CountAsync());
        Assert.Equal(1, await schema.Db.MediaProviderLinks.CountAsync());
        Assert.Equal("https://simkl.com/anime/12345", canonicalLink.ExternalUrl);
        Assert.Equal("fresh", response.AvailabilityStatus);
        Assert.NotNull(response.AvailabilityLastVerifiedAt);
        Assert.Equal("https://www.crunchyroll.com/series/example",
            Assert.Single(MediaProviderAvailabilitySnapshotCodec.Deserialize(canonicalLink.AvailabilitySnapshot)).Url);
        Assert.NotNull(canonicalLink.SpecialEpisodeCatalogSnapshot);
        Assert.Contains("Special", canonicalLink.SpecialEpisodeCatalogSnapshot);
        Assert.NotNull(canonicalLink.EpisodeCatalogLastVerifiedAt);
        Assert.Equal(2, await schema.Db.MediaEpisodes.CountAsync());
        Assert.Equal(2, await schema.Db.MediaEpisodeProviderContents.CountAsync());
        Assert.Equal(["First", "Second"], await schema.Db.MediaEpisodes.AsNoTracking()
            .OrderBy(episode => episode.EpisodeNumber).Select(episode => episode.Title).ToListAsync());
        Assert.Equal(0, await schema.Db.MediaLibraryEntries.CountAsync());
        Assert.Equal(1, interleaved.ClaimCount);
        Assert.Equal(1, interleaved.UniqueViolationCount);
        Assert.Equal(1, provider.DetailsCalls);
        Assert.DoesNotContain(controllerDb.ChangeTracker.Entries(), entry => entry.State == EntityState.Added
            && entry.Entity is MediaTitle or MediaProviderLink or MediaEpisode or MediaEpisodeProviderContent);
    }

    [PostgresFact]
    public async Task CanonicalizeAsync_BatchesWritesReusesStoredMetadataAndSupportsSynonymSearch()
    {
        var counter = new CommandCounter();
        await using var schema = await SchemaContext.CreateAsync(counter);
        var candidates = Enumerable.Range(1, 20).Select(index =>
            new MediaCatalogSearchCandidate("anilist", new CachedMediaProviderSearchResult(
                index.ToString(), $"Title {index}", "anime", null, 2026)
            {
                Synonyms = [$"Alias {index}"],
                PrimaryProgressDimension = "episode",
                ReleaseStatusDimension = "episode"
            })).ToArray();
        counter.Commands = 0;
        var timer = Stopwatch.StartNew();
        await MakeService(schema.Options).CanonicalizeAsync(candidates, default);
        output.WriteLine($"20 new summaries: {counter.Commands} database commands, {timer.Elapsed.TotalMilliseconds:F1} ms.");
        Assert.InRange(counter.Commands, 1, 4);
        counter.Commands = 0;
        timer.Restart();
        await MakeService(schema.Options).CanonicalizeAsync(candidates, default);
        output.WriteLine($"20 existing summaries: {counter.Commands} database commands, {timer.Elapsed.TotalMilliseconds:F1} ms.");
        Assert.Equal(1, counter.Commands);
        var user = TestUserFactory.Create(47810, "catalog-search@example.test");
        schema.Db.Users.Add(user);
        schema.Db.ConnectedServiceAccounts.Add(new ConnectedServiceAccount
        {
            UserId = user.Id,
            Service = "anilist",
            ExternalAccountId = "catalog-test-account",
            ConnectionState = "connected",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await schema.Db.SaveChangesAsync();
        var provider = new CantaroSearchServiceTests.FakeMediaProvider("anilist", []);
        using var cache = new MediaProviderSearchCache();
        var search = new CantaroSearchService(schema.Db, new MediaProviderRegistry([provider]), cache,
            MakeService(schema.Options), NullLogger<CantaroSearchService>.Instance);
        var matched = await search.SearchAsync(user.Id, "alias 20", 6, false, default);
        Assert.Equal("Title 20", Assert.Single(matched.Groups.Media.Items).Title);
        Assert.Equal(0, provider.SearchCallCount);
    }

    [PostgresFact]
    public async Task CanonicalizeAsync_ConcurrentSharedReferenceClaimsRetryIntoOneCanonicalTitle()
    {
        await using var schema = await SchemaContext.CreateAsync();
        var first = Candidate("anilist", "ani-1", "First", "mal-10");
        var second = Candidate("simkl", "sim-20", "First", "mal-10");
        var options = schema.Options;

        var concurrent = await Task.WhenAll(
            MakeService(options).CanonicalizeAsync([first], default),
            MakeService(options).CanonicalizeAsync([second], default));

        Assert.Equal(1, await schema.Db.MediaTitles.CountAsync());
        Assert.Equal(3, await schema.Db.MediaProviderLinks.CountAsync());
        Assert.All(concurrent, result => Assert.Single(result));
        Assert.Single(concurrent.SelectMany(result => result.Values).Distinct());
        Assert.Contains(await schema.Db.MediaProviderLinks.ToListAsync(), link => link.Provider == "myanimelist" && link.ExternalId == "anime:10");

        var repeated = await MakeService(options).CanonicalizeAsync([first, second], default);
        Assert.Single(repeated.Values.Distinct());
        Assert.Equal(1, await schema.Db.MediaTitles.CountAsync());
        Assert.Equal(3, await schema.Db.MediaProviderLinks.CountAsync());
    }

    [PostgresFact]
    public async Task CanonicalizeAsync_ConcurrentAmbiguousClaimsDoNotCreateOrphanAliases()
    {
        await using var schema = await SchemaContext.CreateAsync();
        var options = schema.Options;
        var first = Candidate("anilist", "ani-1", "First", "mal-10");
        var second = Candidate("anilist", "ani-2", "Second", "mal-10");

        await Task.WhenAll(
            MakeService(options).CanonicalizeAsync([first], default),
            MakeService(options).CanonicalizeAsync([second], default));

        Assert.Equal(2, await schema.Db.MediaTitles.CountAsync());
        Assert.Equal(3, await schema.Db.MediaProviderLinks.CountAsync());
        // The first individual request may already have verified the MAL link.
        // Later contradictory evidence must not move that established identity.
        var originalAlias = await schema.Db.MediaProviderLinks.SingleAsync(link => link.Provider == "myanimelist");
        var repeated = await MakeService(options).CanonicalizeAsync([first, second], default);
        Assert.Equal(2, repeated.Values.Distinct().Count());
        Assert.Equal(2, await schema.Db.MediaTitles.CountAsync());
        Assert.Equal(3, await schema.Db.MediaProviderLinks.CountAsync());
        Assert.Equal(originalAlias.MediaTitleId, (await schema.Db.MediaProviderLinks.AsNoTracking()
            .SingleAsync(link => link.Provider == "myanimelist")).MediaTitleId);
    }

    [PostgresFact]
    public async Task LinkProviderAsync_RetriesDiscoveryIdentityRaceAndReturnsWinningTitleConflict()
    {
        await using var schema = await SchemaContext.CreateAsync();
        const int userId = 47814;
        const string providerId = "anilist";
        const string providerMediaId = "manual-link-discovery-conflict";
        var user = TestUserFactory.Create(userId, "manual-link-discovery-conflict@example.test");
        var target = new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = "User's chosen title",
            MediaKind = MediaKinds.Anime,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var libraryEntry = new MediaLibraryEntry
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            MediaTitleId = target.Id,
            Status = MediaLibraryStatuses.Current,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        schema.Db.AddRange(user, target, libraryEntry);
        await schema.Db.SaveChangesAsync();

        var interleaved = new ProviderIdentityClaimBeforeSave(() => MakeService(schema.Options)
            .CanonicalizeAsync(
            [new MediaCatalogSearchCandidate(providerId, new CachedMediaProviderSearchResult(
                providerMediaId, "Discovery's different title", MediaKinds.Anime, null, 2024)
            {
                PrimaryProgressDimension = MediaProgressDimensions.Episode,
                ReleaseStatusDimension = MediaProgressDimensions.Episode
            })], default));
        var callerOptions = new DbContextOptionsBuilder<ApplicationDbContext>(schema.Options)
            .AddInterceptors(interleaved).Options;
        await using var callerDb = new ApplicationDbContext(callerOptions);

        var result = await new MediaLibraryLinkService(
            callerDb, NullLogger<MediaLibraryLinkService>.Instance)
            .LinkProviderAsync(user.Id, target.Id, providerId, providerMediaId, false, default);

        Assert.Equal(MediaLinkResultKind.ConflictingTitle, result.Kind);
        var winner = await schema.Db.MediaProviderLinks.AsNoTracking()
            .Include(link => link.MediaTitle)
            .SingleAsync(link => link.Provider == providerId && link.ExternalId == providerMediaId);
        Assert.Equal(1, interleaved.ClaimCount);
        Assert.Equal(1, interleaved.UniqueViolationCount);
        Assert.NotEqual(target.Id, winner.MediaTitleId);
        Assert.Equal("Discovery's different title", winner.MediaTitle!.CanonicalTitle);
        Assert.Equal(winner.MediaTitleId, result.ConflictingMediaTitleId);
        Assert.Equal(winner.MediaTitle.CanonicalTitle, result.ConflictingCanonicalTitle);
        Assert.Equal("User's chosen title", (await schema.Db.MediaTitles.AsNoTracking()
            .SingleAsync(title => title.Id == target.Id)).CanonicalTitle);
        Assert.Equal(target.Id, (await schema.Db.MediaLibraryEntries.AsNoTracking()
            .SingleAsync(entry => entry.Id == libraryEntry.Id)).MediaTitleId);
        Assert.DoesNotContain(callerDb.ChangeTracker.Entries<MediaProviderLink>(),
            entry => entry.State == EntityState.Added);
        Assert.Equal(2, await schema.Db.MediaTitles.CountAsync());
    }

    [PostgresFact]
    public async Task LinkProviderAsync_RetriesDiscoveryIdentityRaceAsAlreadyLinkedToOwnedTitle()
    {
        await using var schema = await SchemaContext.CreateAsync();
        const int userId = 47815;
        const string providerId = "anilist";
        const string providerMediaId = "manual-link-discovery-same-title";
        var user = TestUserFactory.Create(userId, "manual-link-discovery-same-title@example.test");
        var target = new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = "Existing canonical title",
            MediaKind = MediaKinds.Anime,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var entry = new MediaLibraryEntry
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            MediaTitleId = target.Id,
            Status = MediaLibraryStatuses.Current,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var knownIdentity = new MediaProviderLink
        {
            Id = Guid.NewGuid(),
            MediaTitleId = target.Id,
            Provider = "myanimelist",
            ExternalId = "anime:10",
            LinkSource = MediaMappingSources.Imported,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        schema.Db.AddRange(user, target, entry, knownIdentity);
        await schema.Db.SaveChangesAsync();

        var interleaved = new ProviderIdentityClaimBeforeSave(() => MakeService(schema.Options)
            .CanonicalizeAsync(
            [Candidate(providerId, providerMediaId, "Provider title", "mal-10")], default));
        var callerOptions = new DbContextOptionsBuilder<ApplicationDbContext>(schema.Options)
            .AddInterceptors(interleaved).Options;
        await using var callerDb = new ApplicationDbContext(callerOptions);

        var result = await new MediaLibraryLinkService(
            callerDb, NullLogger<MediaLibraryLinkService>.Instance)
            .LinkProviderAsync(user.Id, target.Id, providerId, providerMediaId, false, default);

        Assert.Equal(MediaLinkResultKind.AlreadyLinked, result.Kind);
        Assert.Equal(1, interleaved.ClaimCount);
        Assert.Equal(1, interleaved.UniqueViolationCount);
        Assert.Equal(1, await schema.Db.MediaTitles.CountAsync());
        var links = await schema.Db.MediaProviderLinks.AsNoTracking().OrderBy(link => link.Provider).ToListAsync();
        Assert.Equal(2, links.Count);
        Assert.All(links, link => Assert.Equal(target.Id, link.MediaTitleId));
        Assert.Contains(links, link => link.Provider == "myanimelist" && link.ExternalId == "anime:10");
        Assert.Contains(links, link => link.Provider == providerId && link.ExternalId == providerMediaId);
        Assert.Equal(target.Id, (await schema.Db.MediaLibraryEntries.AsNoTracking()
            .SingleAsync(item => item.Id == entry.Id)).MediaTitleId);
        Assert.DoesNotContain(callerDb.ChangeTracker.Entries<MediaProviderLink>(),
            link => link.State == EntityState.Added);
    }

    private static MediaCatalogCanonicalizationService MakeService(DbContextOptions<ApplicationDbContext> options)
        => new(options, NullLogger<MediaCatalogCanonicalizationService>.Instance);

    private static MediaObservationsController CreateObservationsController(
        ApplicationDbContext db,
        IMediaProviderRegistry registry,
        int userId,
        string email)
    {
        var seasonMappingService = new MediaProviderSeasonMappingService(
            db, NullLogger<MediaProviderSeasonMappingService>.Instance);
        var matchingService = new MediaObservationMatchingService(
            db, seasonMappingService, new MediaRelationGraphRefreshQueue(),
            NullLogger<MediaObservationMatchingService>.Instance);
        var eventHub = new MediaLibraryEventHub();
        var operationProcessor = new MediaProviderOperationProcessor(
            db, registry, NullLogger<MediaProviderOperationProcessor>.Instance);
        var progressService = new MediaObservationProgressService(
            db, operationProcessor, eventHub, NullLogger<MediaObservationProgressService>.Instance,
            seasonMappingService, matchingService);
        var episodeIdentityService = new MediaEpisodeIdentityService(
            db, seasonMappingService, NullLogger<MediaEpisodeIdentityService>.Instance);
        var controller = new MediaObservationsController(
            db,
            CreateUserManager(db),
            matchingService,
            progressService,
            episodeIdentityService,
            seasonMappingService,
            registry,
            eventHub,
            NullLogger<MediaObservationsController>.Instance,
            new MediaCatalogCanonicalizationService(
                (DbContextOptions<ApplicationDbContext>)db.GetService<IDbContextOptions>(),
                NullLogger<MediaCatalogCanonicalizationService>.Instance),
            operationProcessor);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                    new Claim(ClaimTypes.Name, email),
                    new Claim(ClaimTypes.Email, email)
                ], "Test"))
            }
        };
        return controller;
    }

    private static MediaProvidersController CreateMediaProvidersController(
        ApplicationDbContext db,
        IMediaProviderRegistry registry,
        int userId,
        string email)
    {
        var seasonMappingService = new MediaProviderSeasonMappingService(
            db, NullLogger<MediaProviderSeasonMappingService>.Instance);
        var episodeIdentityService = new MediaEpisodeIdentityService(
            db, seasonMappingService, NullLogger<MediaEpisodeIdentityService>.Instance);
        var eventHub = new MediaLibraryEventHub();
        var controller = new MediaProvidersController(
            db,
            registry,
            new MediaLibraryImportQueue(),
            new MediaProviderOperationProcessor(db, registry,
                NullLogger<MediaProviderOperationProcessor>.Instance),
            eventHub,
            episodeIdentityService,
            new NoOpAioStreamsAnimeEnricher(),
            new MediaCatalogCanonicalizationService(
                (DbContextOptions<ApplicationDbContext>)db.GetService<IDbContextOptions>(),
                NullLogger<MediaCatalogCanonicalizationService>.Instance),
            CreateUserManager(db),
            NullLogger<MediaProvidersController>.Instance,
            new EphemeralDataProtectionProvider(),
            new StubFrontendUrlResolver());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                    new Claim(ClaimTypes.Name, email),
                    new Claim(ClaimTypes.Email, email)
                ], "Test"))
            }
        };
        return controller;
    }

    private static UserManager<User> CreateUserManager(ApplicationDbContext db)
    {
        var store = new UserStore<User, IdentityRole<int>, ApplicationDbContext, int>(db);
        return new UserManager<User>(store, Options.Create(new IdentityOptions()), new PasswordHasher<User>(),
            [], [], new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(),
            new ServiceCollection().BuildServiceProvider(), NullLogger<UserManager<User>>.Instance);
    }

    private sealed class NoOpAioStreamsAnimeEnricher : IAioStreamsAnimeEnricher
    {
        public Task EnrichAsync(MediaProviderTitleDetails details, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class StubFrontendUrlResolver : IFrontendUrlResolver
    {
        public string GetFrontendUrl() => "https://frontend.test";
        public string GetCurrentRequestBaseUrl() => "https://api.test";
        public string GetCallbackUrl(string relativePath) => $"https://api.test/{relativePath.TrimStart('/')}";
    }

    private static MediaCatalogSearchCandidate Candidate(string provider, string id, string title, string malId)
        => new(provider, new CachedMediaProviderSearchResult(provider == "simkl" ? $"anime:{id.Replace("sim-", "")}" : id, title, "anime", null, 2026)
        {
            PrimaryProgressDimension = "episode",
            ReleaseStatusDimension = "episode",
            CrossReferences = [new("myanimelist", $"anime:{malId.Replace("mal-", "")}", null)]
        });

    private sealed class SchemaContext : IAsyncDisposable
    {
        private readonly string _adminConnectionString;
        private readonly string _schema;
        private bool _disposed;

        private SchemaContext(string adminConnectionString, string schema, ApplicationDbContext db,
            DbContextOptions<ApplicationDbContext> options)
        { _adminConnectionString = adminConnectionString; _schema = schema; Db = db; Options = options; }

        public ApplicationDbContext Db { get; }
        public DbContextOptions<ApplicationDbContext> Options { get; }

        public static async Task<SchemaContext> CreateAsync(CommandCounter? counter = null, bool createTables = true)
        {
            var raw = Environment.GetEnvironmentVariable("CANTARO_TEST_POSTGRES");
            if (string.IsNullOrWhiteSpace(raw)) throw new InvalidOperationException("CANTARO_TEST_POSTGRES is required.");
            var admin = new NpgsqlConnectionStringBuilder(raw);
            if (admin.Host is not ("localhost" or "127.0.0.1" or "::1"))
                throw new InvalidOperationException("CANTARO_TEST_POSTGRES must point to a loopback PostgreSQL server.");
            if (string.IsNullOrWhiteSpace(admin.Database)
                || !admin.Database.Contains("test", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("CANTARO_TEST_POSTGRES must use a database whose name contains 'test'.");

            var schema = $"cantaro_catalog_test_{Guid.NewGuid():N}";
            var adminConnectionString = admin.ConnectionString;
            await using (var connection = new NpgsqlConnection(adminConnectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"CREATE SCHEMA \"{schema}\"";
                await command.ExecuteNonQueryAsync();
            }

            var scoped = new NpgsqlConnectionStringBuilder(adminConnectionString) { SearchPath = schema };
            var builder = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(scoped.ConnectionString, provider => provider.EnableRetryOnFailure(maxRetryCount: 1));
            if (counter is not null) builder.AddInterceptors(counter);
            var options = builder.Options;
            var db = new ApplicationDbContext(options);
            var context = new SchemaContext(adminConnectionString, schema, db, options);
            try
            {
                // Other suites use separate schemas in the same test database.
                // EnsureCreated checks for tables database-wide and may skip this schema.
                if (createTables)
                {
                    await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
                }
                return context;
            }
            catch
            {
                await context.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            await Db.DisposeAsync();
            await using var connection = new NpgsqlConnection(_adminConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP SCHEMA IF EXISTS \"{_schema}\" CASCADE";
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int Commands { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Commands++; return base.ReaderExecutingAsync(command, eventData, result, cancellationToken); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Commands++; return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken); }
    }

    private sealed class DetailUpdateBeforeSave(Func<CancellationToken, Task> writeDetails) : SaveChangesInterceptor
    {
        public int DetailWrites { get; private set; }
        public int SaveAttempts { get; private set; }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            SaveAttempts++;
            if (DetailWrites == 0)
            {
                DetailWrites++;
                await writeDetails(cancellationToken);
            }
            return result;
        }
    }

    private sealed class ImportIdentityClaimBeforeSave(Func<Task> claimIdentity) : SaveChangesInterceptor
    {
        private bool _claimed;
        public int ClaimCount { get; private set; }
        public int SaveAttempts { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            SaveAttempts++;
            if (!_claimed)
            {
                _claimed = true;
                ClaimCount++;
                await claimIdentity();
            }
            return result;
        }
    }

    private sealed class ProviderIdentityClaimBeforeSave(Func<Task> claimIdentity) : SaveChangesInterceptor
    {
        private bool _claimed;
        public int ClaimCount { get; private set; }
        public int UniqueViolationCount { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!_claimed && eventData.Context?.ChangeTracker.Entries<MediaProviderLink>()
                    .Any(entry => entry.State == EntityState.Added) == true)
            {
                _claimed = true;
                ClaimCount++;
                await claimIdentity();
            }
            return result;
        }

        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Exception is DbUpdateException
                {
                    InnerException: PostgresException
                    {
                        SqlState: PostgresErrorCodes.UniqueViolation,
                        ConstraintName: "IX_MediaProviderLinks_Provider_ExternalId" or "IX_MediaProviderLinks_MediaTitleId_Provider"
                    }
                })
            {
                UniqueViolationCount++;
            }
            return base.SaveChangesFailedAsync(eventData, cancellationToken);
        }
    }

    private sealed class ObservationRaceMediaProvider(
        ConnectedServiceAccount account,
        MediaProviderTitleDetails details) : IMediaProvider
    {
        public string ProviderId => account.Service;
        public int DetailsCalls { get; private set; }
        public int StatusCalls { get; private set; }
        public Task<ConnectedServiceAccount?> GetConnectedAccountAsync(int userId, CancellationToken cancellationToken = default)
            => Task.FromResult<ConnectedServiceAccount?>(account);
        public Task<MediaProviderTitleDetails?> GetTitleDetailsAsync(int userId, string providerMediaId, CancellationToken cancellationToken)
        {
            DetailsCalls++;
            return Task.FromResult<MediaProviderTitleDetails?>(details);
        }
        public Task<MediaProviderMutationResult> UpdateStatusAsync(int userId, MediaStatusUpdateRequest request, CancellationToken cancellationToken)
        {
            StatusCalls++;
            return Task.FromResult(new MediaProviderMutationResult
            {
                ProviderId = ProviderId,
                ProviderMediaId = request.ProviderMediaId,
                AppliedAt = DateTimeOffset.UtcNow,
                LastRemoteUpdateAt = DateTimeOffset.UtcNow
            });
        }
        public bool SupportsMediaKind(string mediaKind) => true;
        public string GetAuthorizationUrl(string redirectUri, string state, string codeChallenge) => throw new NotSupportedException();
        public Task<ConnectedServiceAccount> ExchangeCodeAndSaveAsync(int userId, string authorizationCode, string redirectUri, string codeVerifier, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DisconnectAsync(int userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MediaProviderLibraryImportResult> ImportLibraryAsync(int userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<MediaProviderSearchResult>> SearchAsync(int userId, MediaCatalogSearchRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MediaProviderMutationResult> UpdateProgressAsync(int userId, MediaProgressUpdateRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MediaProviderMutationResult> UpdateScoreAsync(int userId, MediaScoreUpdateRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MediaReleaseMetadata?> GetReleaseMetadataAsync(int userId, string providerMediaId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CANTARO_TEST_POSTGRES")))
                Skip = "Set CANTARO_TEST_POSTGRES to a loopback database whose name contains 'test'.";
        }
    }
}

// Existing PostgreSQL suites use database-wide EnsureCreated checks while
// creating temporary schemas. Keep this schema-based suite from overlapping them.
[CollectionDefinition("Media catalog PostgreSQL", DisableParallelization = true)]
public sealed class MediaCatalogPostgresCollection;
