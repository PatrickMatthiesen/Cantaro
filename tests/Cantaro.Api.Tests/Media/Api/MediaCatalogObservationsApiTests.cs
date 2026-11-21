using System.Security.Claims;
using Cantaro.Api.Controllers;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public class MediaCatalogObservationsApiTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Resolve_SeasonlessSingleEpisodePreservesLearnedSeasonOnSqlite(bool manual) =>
        MediaObservationsApiTests.WithSqliteObservationDatabaseAsync(
            db => AssertSeasonlessReviewPreservesLearnedSeasonAsync(db, manual));

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Resolve_SeasonlessSingleEpisodePreservesLearnedSeasonOnPostgres(bool manual)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await AssertSeasonlessReviewPreservesLearnedSeasonAsync(schema.Db, manual);
    }

    private static async Task AssertSeasonlessReviewPreservesLearnedSeasonAsync(ApplicationDbContext db, bool manual)
    {
        const int reviewerId = 712;
        const int contributorId = 713;
        const string seriesId = "LEARNEDSEASON";
        var selected = CreateRegressionTitle("Silver Harbor");
        var seasonTitle = CreateRegressionTitle("Winter Orchard");
        db.AddRange(selected, CreateRegressionTitle(selected.CanonicalTitle), seasonTitle,
            TestUserFactory.Create(reviewerId, "reviewer@example.test"),
            TestUserFactory.Create(contributorId, "contributor@example.test"));
        await db.SaveChangesAsync();

        var reviewer = CatalogFixture.CreateController(db, reviewerId);
        var pending = CreateRequest(selected.CanonicalTitle, seriesId, 1);
        pending.ProviderSeasonId = null;
        pending.SeasonNumber = null;
        pending.SeasonTitle = null;
        var pendingResponse = GetResponse(await reviewer.Submit(pending, default));
        Assert.Equal(MediaObservationStatuses.Ambiguous, pendingResponse.MatchStatus);
        Assert.Empty(await db.MediaProviderSeasonMappings.ToListAsync());

        var keyed = CreateRequest(seasonTitle.CanonicalTitle, seriesId, 2);
        keyed.ObservedAt = pending.ObservedAt!.Value.AddMinutes(1);
        var contributor = CatalogFixture.CreateController(db, contributorId);
        Assert.Equal(seasonTitle.Id.ToString(), GetResponse(await contributor.Submit(keyed, default)).MatchedMediaTitleId);

        if (manual)
        {
            var mappings = new MediaProviderSeasonMappingService(db, NullLogger<MediaProviderSeasonMappingService>.Instance);
            var controller = new MediaCrunchyrollMappingsController(db, mappings);
            Assert.IsType<OkObjectResult>(await controller.PutSeason(seasonTitle.Id,
                new() { ProviderSeriesId = seriesId, ProviderSeasonId = keyed.ProviderSeasonId, ProviderSeasonNumber = 1 },
                default));
        }
        var before = await db.MediaProviderSeasonMappings.AsNoTracking().SingleAsync();
        Assert.Equal(manual ? MediaProviderSeasonMappingSources.Manual : MediaProviderSeasonMappingSources.Automatic,
            before.MappingSource);

        var observationId = Guid.Parse(pendingResponse.ObservationId!);
        var candidate = await db.MediaObservationCandidates.SingleAsync(row =>
            row.MediaObservationId == observationId && row.MediaTitleId == selected.Id);
        var review = CatalogFixture.CreateReviewController(db, reviewer.ControllerContext);
        Assert.IsType<OkObjectResult>((await review.Resolve(observationId,
            new ResolveMediaObservationRequest { CandidateId = candidate.Id, EpisodeOffset = 0 }, default)).Result);

        var after = await db.MediaProviderSeasonMappings.AsNoTracking().SingleAsync();
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(seasonTitle.Id, after.MediaTitleId);
        Assert.Equal(before.MappingSource, after.MappingSource);
        Assert.Equal(before.LastVerifiedAt, after.LastVerifiedAt);
        var direct = await db.MediaProviderEpisodeMappings.AsNoTracking().SingleAsync();
        Assert.Equal("EPISODE1", direct.ProviderEpisodeId);
        Assert.Equal(selected.Id, direct.MediaTitleId);
        Assert.Equal(1, direct.CanonicalEpisodeNumber);
        Assert.Equal(MediaProviderSeasonMappingSources.Manual, direct.MappingSource);
    }

    [Fact]
    public Task Submit_DelayedAutomaticCatalogSkipsContradictedFirstMappingOnSqlite() =>
        MediaObservationsApiTests.WithSqliteObservationDatabaseAsync(AssertDelayedAutomaticDirectMappingAsync);

    [PostgresFact]
    public async Task Submit_DelayedAutomaticCatalogSkipsContradictedFirstMappingOnPostgres()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await AssertDelayedAutomaticDirectMappingAsync(schema.Db);
    }

    private static async Task AssertDelayedAutomaticDirectMappingAsync(ApplicationDbContext db)
    {
        const int delayedViewerId = 712;
        const int newerViewerId = 713;
        const string seriesId = "DELAYEDMAPPING";
        const string ambiguousTitle = "Silver Harbor";
        var automatic = CreateRegressionTitle("Winter Orchard");
        db.AddRange(automatic, CreateRegressionTitle(ambiguousTitle), CreateRegressionTitle(ambiguousTitle),
            TestUserFactory.Create(delayedViewerId, "delayed@example.test"),
            TestUserFactory.Create(newerViewerId, "newer@example.test"));
        await db.SaveChangesAsync();

        var older = CreateRequest(automatic.CanonicalTitle, seriesId, 2);
        older.ProviderSeasonId = null;
        older.SeasonNumber = null;
        older.SeasonTitle = null;
        older.Episodes[0].EpisodeNumber = 12;
        older.Episodes[1].EpisodeNumber = 13;
        var newer = CreateRequest(ambiguousTitle, seriesId, 1);
        newer.ProviderSeasonId = null;
        newer.SeasonNumber = null;
        newer.SeasonTitle = null;
        newer.ObservedAt = older.ObservedAt!.Value.AddMinutes(1);
        newer.Episodes[0].EpisodeNumber = 13;

        var newerViewer = CatalogFixture.CreateController(db, newerViewerId);
        Assert.Equal(MediaObservationStatuses.Ambiguous, GetResponse(await newerViewer.Submit(newer, default)).MatchStatus);
        Assert.Empty(await db.MediaProviderEpisodeMappings.ToListAsync());
        Assert.Equal(13, (await db.MediaProviderEpisodes.AsNoTracking().SingleAsync()).ProviderEpisodeNumber);

        var olderViewer = CatalogFixture.CreateController(db, delayedViewerId);
        var response = GetResponse(await olderViewer.Submit(older, default));
        Assert.Equal(MediaCatalogObservationStatuses.Accepted, response.Status);
        Assert.Equal(automatic.Id.ToString(), response.MatchedMediaTitleId);
        var direct = await db.MediaProviderEpisodeMappings.AsNoTracking().SingleAsync();
        Assert.Equal("EPISODE2", direct.ProviderEpisodeId);
        Assert.Equal(13, direct.CanonicalEpisodeNumber);

        var watch = CatalogFixture.CreateReviewController(db, newerViewer.ControllerContext);
        var watched = Assert.IsType<SubmitMediaObservationResponse>(Assert.IsType<OkObjectResult>((await watch.Submit(new()
        {
            SiteIdentifier = "crunchyroll", SiteMediaId = "EPISODE1", ObservedUrl = newer.Episodes[0].ProviderUrl,
            ObservedTitle = ambiguousTitle, SeriesTitle = ambiguousTitle, ProviderSeriesId = seriesId, EpisodeNumber = 13,
            ObservedAt = newer.ObservedAt
        }, default)).Result).Value);
        Assert.Equal(MediaObservationStatuses.Ambiguous, watched.MatchStatus);
        Assert.Null(watched.ResolvedProgress);
    }

    [Fact]
    public Task Submit_PartialSeasonCorrectionUpdatesWatchAndPlaybackOnSqlite() =>
        MediaObservationsApiTests.WithSqliteObservationDatabaseAsync(AssertPartialSeasonCorrectionAsync);

    [PostgresFact]
    public async Task Submit_PartialSeasonCorrectionUpdatesWatchAndPlaybackOnPostgres()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await AssertPartialSeasonCorrectionAsync(schema.Db);
    }

    private static async Task AssertPartialSeasonCorrectionAsync(ApplicationDbContext db)
    {
        const int firstViewerId = 712;
        const int secondViewerId = 713;
        const string seriesId = "PARTIALCORRECTION";
        const string episodeUrl = "https://www.crunchyroll.com/watch/EPISODE1";
        var first = CreateRegressionTitle("Silver Harbor");
        var second = CreateRegressionTitle("Winter Orchard");
        db.AddRange(first, second, TestUserFactory.Create(firstViewerId, "first@example.test"),
            TestUserFactory.Create(secondViewerId, "second@example.test"));
        db.MediaLibraryEntries.AddRange(new[] { first, second }.Select(title => new MediaLibraryEntry
        {
            Id = Guid.NewGuid(), UserId = secondViewerId, MediaTitleId = title.Id, Status = MediaLibraryStatuses.Current
        }));
        await db.SaveChangesAsync();

        var mappings = new MediaProviderSeasonMappingService(db, NullLogger<MediaProviderSeasonMappingService>.Instance);
        await mappings.EstablishAsync("crunchyroll", seriesId, "A", 1, first.Id, 0,
            MediaProviderSeasonMappingSources.Manual, 1m, default);
        await mappings.EstablishAsync("crunchyroll", seriesId, null, 2, second.Id, 0,
            MediaProviderSeasonMappingSources.Manual, 1m, default);
        var identity = new MediaEpisodeIdentityService(db, mappings, NullLogger<MediaEpisodeIdentityService>.Instance);

        var original = CreateRequest("Provider series", seriesId, 1);
        original.ProviderSeasonId = "A";
        var firstViewer = CatalogFixture.CreateController(db, firstViewerId);
        Assert.Equal(first.Id.ToString(), GetResponse(await firstViewer.Submit(original, default)).MatchedMediaTitleId);
        Assert.Equal(episodeUrl, (await identity.ResolveContinueWatchingAsync(secondViewerId, first.Id, default))?.Url);

        var correction = CreateRequest(original.SeriesTitle, seriesId, 1);
        correction.ObservedAt = original.ObservedAt!.Value.AddMinutes(1);
        correction.ProviderSeasonId = null;
        correction.SeasonNumber = 2;
        correction.SeasonTitle = null;
        var secondViewer = CatalogFixture.CreateController(db, secondViewerId);
        var corrected = GetResponse(await secondViewer.Submit(correction, default));
        var row = await db.MediaProviderEpisodes.AsNoTracking().SingleAsync();
        Assert.Null(row.ProviderSeasonId);
        Assert.Equal(2, row.ProviderSeasonNumber);
        Assert.Null(row.NormalizedSeasonTitle);
        Assert.Equal(second.Id.ToString(), corrected.MatchedMediaTitleId);

        var catalog = (await identity.GetEpisodeCatalogAsync(second.Id, default))!;
        Assert.Equal(episodeUrl, Assert.Single(Assert.Single(catalog.Episodes).Destinations).Url);
        Assert.Empty((await identity.GetEpisodeCatalogAsync(first.Id, default))!.Episodes);
        var playback = (await identity.ResolveContinueWatchingAsync(secondViewerId, second.Id, default))!;
        Assert.Equal("direct", playback.Outcome);
        Assert.Equal(episodeUrl, playback.Url);
        Assert.NotEqual(episodeUrl, (await identity.ResolveContinueWatchingAsync(secondViewerId, first.Id, default))?.Url);

        var watch = CatalogFixture.CreateReviewController(db, secondViewer.ControllerContext);
        var watched = Assert.IsType<SubmitMediaObservationResponse>(Assert.IsType<OkObjectResult>((await watch.Submit(new()
        {
            SiteIdentifier = "crunchyroll", SiteMediaId = "EPISODE1", ObservedUrl = episodeUrl,
            ObservedTitle = "Provider episode", EpisodeNumber = 1, ObservedAt = original.ObservedAt,
            ProviderSeriesId = seriesId, ProviderSeasonId = "A", SeasonNumber = 1, SeasonTitle = "Season 1"
        }, default)).Result).Value);
        Assert.Equal(second.Id.ToString(), watched.MatchedMediaTitleId);
        Assert.Equal(1, watched.ResolvedProgress);
        row = await db.MediaProviderEpisodes.AsNoTracking().SingleAsync();
        Assert.Null(row.ProviderSeasonId);
        Assert.Null(row.NormalizedSeasonTitle);
        Assert.Equal(2, row.ProviderSeasonNumber);

        await identity.RecordObservationAsync(new MediaObservation
        {
            SiteIdentifier = "crunchyroll", SiteMediaId = "EPISODE1", ObservedUrl = episodeUrl,
            ObservedTitle = "Provider episode", ProviderSeriesId = seriesId, MatchStatus = MediaObservationStatuses.Pending,
            ProviderSeasonId = "B", SeasonNumber = 2, SeasonTitle = "Renamed Season",
            ObservedAt = correction.ObservedAt!.Value.AddMinutes(1), EpisodeNumber = 1
        }, default);
        await identity.RecordObservationAsync(new MediaObservation
        {
            SiteIdentifier = "crunchyroll", SiteMediaId = "EPISODE1", ObservedUrl = episodeUrl,
            ObservedTitle = "Provider episode", ProviderSeriesId = seriesId, MatchStatus = MediaObservationStatuses.Pending, ProviderSeasonId = "C",
            SeasonNumber = 3, ObservedAt = correction.ObservedAt.Value.AddMinutes(2), EpisodeNumber = 1
        }, default);
        row = await db.MediaProviderEpisodes.AsNoTracking().SingleAsync();
        Assert.Equal("C", row.ProviderSeasonId);
        Assert.Equal(3, row.ProviderSeasonNumber);
        Assert.Null(row.NormalizedSeasonTitle);
    }

    private static MediaTitle CreateRegressionTitle(string title) => new()
    {
        Id = Guid.NewGuid(), CanonicalTitle = title, MediaKind = MediaKinds.Anime,
        EpisodeCount = 24, SupportsEpisodeProgress = true, PrimaryProgressDimension = MediaProgressDimensions.Episode,
        ReleaseStatusDimension = MediaProgressDimensions.Episode
    };

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public Task Submit_OrdersAutomaticMappingsByClampedEvidenceOnSqlite(bool direct, bool future) =>
        MediaObservationsApiTests.WithSqliteObservationDatabaseAsync(
            db => AssertAutomaticMappingEvidenceAsync(db, direct, future));

    [PostgresTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Submit_OrdersAutomaticMappingsByClampedEvidenceOnPostgres(bool direct, bool future)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await AssertAutomaticMappingEvidenceAsync(schema.Db, direct, future);
    }

    private static async Task AssertAutomaticMappingEvidenceAsync(ApplicationDbContext db, bool direct, bool future)
    {
        const int firstContributor = 712;
        const int secondContributor = 713;
        const string episodeUrl = "https://www.crunchyroll.com/watch/E1";
        var first = CreateTitle("Silver Harbor");
        var second = CreateTitle("Winter Orchard");
        var expectedTitle = future ? second : first;
        var otherTitle = future ? first : second;
        var firstNumber = future ? 2 : 3;
        db.AddRange(first, second, TestUserFactory.Create(firstContributor, "first@example.test"),
            TestUserFactory.Create(secondContributor, "second@example.test"));
        db.MediaLibraryEntries.AddRange(new[] { first, second }.Select(title => new MediaLibraryEntry
        {
            Id = Guid.NewGuid(), UserId = secondContributor, MediaTitleId = title.Id,
            Status = MediaLibraryStatuses.Current, ProgressEpisodes = 2
        }));
        await db.SaveChangesAsync();

        var newerAt = new DateTimeOffset(2026, 11, 20, 10, 2, 0, TimeSpan.Zero);
        var firstAt = future ? DateTimeOffset.MaxValue : newerAt;
        var beforeReceipt = DateTimeOffset.UtcNow;
        await SubmitAsync(firstContributor, first, firstAt, firstNumber, first);
        var recorded = await db.MediaProviderEpisodes.AsNoTracking().SingleAsync();
        Assert.Equal(firstNumber, recorded.ProviderEpisodeNumber);
        if (future)
        {
            Assert.InRange(recorded.LastSeenAt, beforeReceipt.AddTicks(-TimeSpan.TicksPerMicrosecond), DateTimeOffset.UtcNow);
        }
        else
        {
            Assert.Equal(newerAt, recorded.LastSeenAt);
        }
        await AssertMappingAsync(first, firstNumber, recorded.LastSeenAt);

        var secondAt = future
            ? MediaObservationLifecycleService.NormalizeObservedAt(null, DateTimeOffset.UtcNow)
            : newerAt.AddMinutes(-1);
        await SubmitAsync(secondContributor, second, secondAt, future ? 3 : 2, expectedTitle);
        recorded = await db.MediaProviderEpisodes.AsNoTracking().SingleAsync();
        Assert.Equal(3, recorded.ProviderEpisodeNumber);
        Assert.Equal(future ? secondAt : newerAt, recorded.LastSeenAt);
        await AssertMappingAsync(expectedTitle, 3, recorded.LastSeenAt);

        var mappings = new MediaProviderSeasonMappingService(db, NullLogger<MediaProviderSeasonMappingService>.Instance);
        var identity = new MediaEpisodeIdentityService(db, mappings, NullLogger<MediaEpisodeIdentityService>.Instance);
        var playback = Assert.IsType<MediaContinueWatchingDto>(
            await identity.ResolveContinueWatchingAsync(secondContributor, expectedTitle.Id, default));
        Assert.Equal("direct", playback.Outcome);
        Assert.Equal(3, playback.EpisodeNumber);
        Assert.Equal(episodeUrl, playback.Url);
        var otherPlayback = await identity.ResolveContinueWatchingAsync(secondContributor, otherTitle.Id, default);
        Assert.NotEqual(episodeUrl, otherPlayback?.Url);

        static MediaTitle CreateTitle(string title) => new()
        {
            Id = Guid.NewGuid(), CanonicalTitle = title, MediaKind = MediaKinds.Anime,
            EpisodeCount = 12, SupportsEpisodeProgress = true, PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode
        };

        async Task SubmitAsync(int userId, MediaTitle evidence, DateTimeOffset at, int number, MediaTitle expected)
        {
            var request = CreateRequest(evidence.CanonicalTitle, "TIMEDCATALOG", 1);
            request.ObservedAt = at;
            if (direct)
            {
                request.ProviderSeasonId = null;
                request.SeasonNumber = null;
                request.SeasonTitle = null;
            }
            request.Episodes = [new() { ProviderEpisodeId = "E1", ProviderUrl = episodeUrl, EpisodeNumber = number }];
            var response = GetResponse(await CatalogFixture.CreateController(db, userId).Submit(request, default));
            Assert.Equal(MediaCatalogObservationStatuses.Accepted, response.Status);
            Assert.Equal(expected.Id.ToString(), response.MatchedMediaTitleId);
            Assert.Null(response.ObservationId);
        }

        async Task AssertMappingAsync(MediaTitle title, int number, DateTimeOffset at)
        {
            if (direct)
            {
                var mapping = await db.MediaProviderEpisodeMappings.AsNoTracking().SingleAsync();
                Assert.Equal(MediaProviderSeasonMappingSources.Automatic, mapping.MappingSource);
                Assert.Equal(title.Id, mapping.MediaTitleId);
                Assert.Equal(number, mapping.CanonicalEpisodeNumber);
                Assert.Equal(at, mapping.LastObservedAt);
                Assert.Empty(await db.MediaProviderSeasonMappings.AsNoTracking().ToListAsync());
            }
            else
            {
                var mapping = await db.MediaProviderSeasonMappings.AsNoTracking().SingleAsync();
                Assert.Equal(MediaProviderSeasonMappingSources.Automatic, mapping.MappingSource);
                Assert.Equal(title.Id, mapping.MediaTitleId);
                Assert.Equal(0, mapping.EpisodeOffset);
                Assert.Equal(at, mapping.LastObservedAt);
                Assert.Empty(await db.MediaProviderEpisodeMappings.AsNoTracking().ToListAsync());
            }
        }
    }

    [Fact]
    public Task Submit_DelayedCatalogPreservesPlaybackCoordinatesOnSqlite() =>
        MediaObservationsApiTests.WithSqliteObservationDatabaseAsync(AssertDelayedCatalogPreservesPlaybackAsync);

    [PostgresFact]
    public async Task Submit_DelayedCatalogPreservesPlaybackCoordinatesOnPostgres()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await AssertDelayedCatalogPreservesPlaybackAsync(schema.Db);
    }

    private static async Task AssertDelayedCatalogPreservesPlaybackAsync(ApplicationDbContext db)
    {
        const int contributorId = 712;
        const int viewerId = 713;
        const string seriesId = "CATALOGCORRECTION";
        const string correctedUrl = "https://www.crunchyroll.com/watch/E1";
        var correctedAt = new DateTimeOffset(2026, 11, 20, 10, 2, 0, TimeSpan.Zero);
        var delayedAt = correctedAt.AddMinutes(-1).ToOffset(TimeSpan.FromHours(1));
        db.Users.AddRange(TestUserFactory.Create(contributorId, "catalog-contributor@example.test"),
            TestUserFactory.Create(viewerId, "catalog-viewer@example.test"));
        await db.SaveChangesAsync();
        var controller = CatalogFixture.CreateController(db, contributorId);
        var corrected = CreateRequest("Provider series", seriesId, 1);
        corrected.ObservedAt = correctedAt;
        corrected.Episodes =
        [
            new()
            {
                ProviderEpisodeId = "E1", ProviderUrl = correctedUrl, EpisodeNumber = 3, EpisodeTitle = "Corrected title",
                AvailableAudioLanguageCodes = ["en"], AvailableSubtitleLanguageCodes = ["de"]
            }
        ];
        var accepted = GetResponse(await controller.Submit(corrected, default));
        Assert.Equal(MediaCatalogObservationStatuses.PendingMatch, accepted.Status);

        var delayed = CreateRequest("Provider series", seriesId, 1);
        delayed.ObservedAt = delayedAt;
        delayed.SeasonTitle = "Outdated season title";
        delayed.Episodes =
        [
            new()
            {
                ProviderEpisodeId = "E1", ProviderUrl = correctedUrl, EpisodeNumber = 2, EpisodeTitle = "Outdated title",
                ReleaseTrack = "dub:en", AvailableAudioLanguageCodes = ["ja"], AvailableSubtitleLanguageCodes = ["en"]
            },
            new()
            {
                ProviderEpisodeId = "E2", ProviderUrl = "https://www.crunchyroll.com/watch/E2", EpisodeNumber = 4
            }
        ];
        var replay = GetResponse(await controller.Submit(delayed, default));
        Assert.Equal(MediaCatalogObservationStatuses.Deduplicated, replay.Status);
        Assert.Equal(2, replay.RecordedEpisodeCount);

        var raw = await db.MediaProviderEpisodes.AsNoTracking().ToDictionaryAsync(row => row.ProviderEpisodeId);
        var episode = raw["E1"];
        Assert.Equal(3, episode.ProviderEpisodeNumber);
        Assert.Equal("Corrected title", episode.EpisodeTitle);
        Assert.Equal("season 1", episode.NormalizedSeasonTitle);
        Assert.Equal("dub:en", episode.ReleaseTrack);
        Assert.Equal(2, episode.SeenCount);
        Assert.Equal(new[] { "en", "ja" }, episode.AvailableAudioLanguageCodes);
        Assert.Equal(new[] { "de", "en" }, episode.AvailableSubtitleLanguageCodes);
        Assert.Equal(delayedAt, episode.FirstSeenAt);
        Assert.Equal(correctedAt, episode.LastSeenAt);
        Assert.Equal(4, raw["E2"].ProviderEpisodeNumber);
        Assert.Equal(delayedAt, raw["E2"].LastSeenAt);

        var title = new MediaTitle
        {
            Id = Guid.NewGuid(), CanonicalTitle = "Shared title", MediaKind = MediaKinds.Anime,
            EpisodeCount = 12, SupportsEpisodeProgress = true, PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode
        };
        db.MediaLibraryEntries.Add(new MediaLibraryEntry
        {
            Id = Guid.NewGuid(), UserId = viewerId, MediaTitle = title,
            Status = MediaLibraryStatuses.Current, ProgressEpisodes = 2
        });
        await db.SaveChangesAsync();
        var mappings = new MediaProviderSeasonMappingService(db, NullLogger<MediaProviderSeasonMappingService>.Instance);
        await mappings.EstablishAsync("crunchyroll", seriesId, "SEASON1", 1, title.Id, 0,
            MediaProviderSeasonMappingSources.Manual, 1m, default);
        var identity = new MediaEpisodeIdentityService(db, mappings, NullLogger<MediaEpisodeIdentityService>.Instance);
        var playback = Assert.IsType<MediaContinueWatchingDto>(await identity.ResolveContinueWatchingAsync(viewerId, title.Id, default));
        Assert.Equal("direct", playback.Outcome);
        Assert.Equal(3, playback.EpisodeNumber);
        Assert.Equal(correctedUrl, playback.Url);
        var catalog = Assert.IsType<MediaEpisodeCatalogDto>(await identity.GetEpisodeCatalogAsync(title.Id, default));
        Assert.Equal(new[] { 3, 4 }, catalog.Episodes.Select(item => item.EpisodeNumber));
        Assert.Equal(correctedUrl, Assert.Single(catalog.Episodes[0].Destinations).Url);
    }

    [Theory]
    [InlineData(true, -1)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(false, -1)]
    public Task Submit_OrdersCatalogEvidenceByObservationTimeOnSqlite(bool insertCollision, int contenderMinutes) =>
        MediaObservationsApiTests.WithSqliteObservationDatabaseAsync(
            db => AssertCatalogEvidenceOrderAsync(db, insertCollision, contenderMinutes));

    [PostgresTheory]
    [InlineData(true, -1)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(false, -1)]
    public async Task Submit_OrdersCatalogEvidenceByObservationTimeOnPostgres(bool insertCollision, int contenderMinutes)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await AssertCatalogEvidenceOrderAsync(schema.Db, insertCollision, contenderMinutes);
    }

    private static async Task AssertCatalogEvidenceOrderAsync(ApplicationDbContext db, bool insertCollision, int contenderMinutes)
    {
        const int userId = 712;
        const string seriesId = "CATALOGORDER";
        db.Users.Add(TestUserFactory.Create(userId, "catalog-order@example.test"));
        await db.SaveChangesAsync();
        var timestamp = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
        var first = CreateRequest("First catalog", seriesId, 1);
        first.ObservedAt = timestamp;
        var second = CreateRequest("Second catalog", seriesId, 2);
        second.Episodes.RemoveAt(0);
        second.ObservedAt = timestamp.AddMinutes(contenderMinutes);
        MediaObservation? winner = null;

        async Task InsertWinnerAsync()
        {
            var response = GetResponse(await CatalogFixture.CreateController(db, userId).Submit(first, default));
            Assert.Equal(MediaCatalogObservationStatuses.PendingMatch, response.Status);
            Assert.Equal(1, response.RecordedEpisodeCount);
            winner = await db.MediaObservations.AsNoTracking().SingleAsync();
        }

        if (!insertCollision)
        {
            await InsertWinnerAsync();
        }
        var collision = new MediaObservationsApiTests.DuplicateObservationSaveInterceptor(
            InsertWinnerAsync, EntityState.Added, null);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>(
                (DbContextOptions<ApplicationDbContext>)db.GetService<IDbContextOptions>())
            .AddInterceptors(collision).Options;
        await using var contenderDb = new ApplicationDbContext(options);
        var duplicate = GetResponse(await CatalogFixture.CreateController(contenderDb, userId).Submit(second, default));

        Assert.Equal(insertCollision ? 1 : 0, collision.UniqueViolationCount);
        Assert.NotNull(winner);
        Assert.Equal(MediaCatalogObservationStatuses.Deduplicated, duplicate.Status);
        Assert.Equal(1, duplicate.RecordedEpisodeCount);
        Assert.Equal(winner.Id.ToString(), duplicate.ObservationId);
        var expected = contenderMinutes > 0 ? second : first;
        var saved = await db.MediaObservations.AsNoTracking().Include(row => row.Episodes).SingleAsync();
        Assert.Equal(expected.ObservedAt, saved.ObservedAt);
        Assert.Equal(expected.SeriesTitle, saved.SeriesTitle);
        Assert.Contains(expected.SeriesTitle, saved.ObservedTitle, StringComparison.Ordinal);
        Assert.Equal(Assert.Single(expected.Episodes).ProviderEpisodeId, Assert.Single(saved.Episodes).ProviderEpisodeId);
        if (contenderMinutes <= 0)
        {
            Assert.Equal(winner.UpdatedAt, saved.UpdatedAt);
            Assert.Equal(winner.MatchAttemptCount, saved.MatchAttemptCount);
        }
        var raw = await db.MediaProviderEpisodes.AsNoTracking().OrderBy(row => row.ProviderEpisodeId).ToListAsync();
        Assert.Equal(new[] { "EPISODE1", "EPISODE2" }, raw.Select(row => row.ProviderEpisodeId));
        Assert.All(raw, row => Assert.Equal(1, row.SeenCount));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public Task Submit_PreservesNewerCatalogEvidenceOnSqlite(bool duringMatch, bool contenderIsNewer) =>
        MediaObservationsApiTests.WithSqliteObservationDatabaseAsync(
            db => AssertNewerCatalogEvidenceAsync(db, duringMatch, contenderIsNewer));

    [PostgresTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Submit_PreservesNewerCatalogEvidenceOnPostgres(bool duringMatch, bool contenderIsNewer)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await AssertNewerCatalogEvidenceAsync(schema.Db, duringMatch, contenderIsNewer);
    }

    private static async Task AssertNewerCatalogEvidenceAsync(
        ApplicationDbContext db, bool duringMatch, bool contenderIsNewer)
    {
        const int userId = 712;
        const string seriesId = "CATALOGRACE";
        db.Users.Add(TestUserFactory.Create(userId, "catalog-race@example.test"));
        await db.SaveChangesAsync();
        var timestamp = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
        var first = CatalogFixture.CreateController(db, userId);
        var originalRequest = CreateRequest("Original catalog", seriesId, 1);
        originalRequest.ObservedAt = timestamp;
        var original = GetResponse(await first.Submit(originalRequest, default));
        db.ChangeTracker.Clear();
        MediaObservation? winner = null;

        var firstWriter = CreateRequest("First writer catalog", seriesId, 1);
        firstWriter.ObservedAt = timestamp.AddMinutes(contenderIsNewer ? 1 : 2);
        firstWriter.Episodes.Single().EpisodeTitle = "First writer episode evidence";
        async Task SaveFirstWriterAsync()
        {
            var response = GetResponse(await first.Submit(firstWriter, default));
            Assert.Equal(MediaCatalogObservationStatuses.Deduplicated, response.Status);
            Assert.Equal(1, response.RecordedEpisodeCount);
            winner = await db.MediaObservations.AsNoTracking().SingleAsync();
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>(
            (DbContextOptions<ApplicationDbContext>)db.GetService<IDbContextOptions>());
        var collision = new MediaObservationsApiTests.DuplicateObservationSaveInterceptor(
            SaveFirstWriterAsync, EntityState.Modified, null);
        if (duringMatch)
        {
            options.AddInterceptors(new MediaObservationsApiTests.BeforeObservationTransaction(
                SaveFirstWriterAsync, observation => observation.MatchAttemptCount > 1));
        }
        else
        {
            options.AddInterceptors(collision);
        }
        await using var staleDb = new ApplicationDbContext(options.Options);
        var contender = CatalogFixture.CreateController(staleDb, userId);
        var contenderRequest = CreateRequest("Contender catalog", seriesId, 2);
        contenderRequest.ObservedAt = timestamp.AddMinutes(contenderIsNewer ? 2 : 1);
        contenderRequest.Episodes.RemoveAt(0);
        contenderRequest.Episodes.Single().EpisodeTitle = "Contender episode evidence";
        var response = GetResponse(await contender.Submit(contenderRequest, default));

        Assert.Equal(duringMatch ? 0 : 1, collision.ConcurrencyViolationCount);
        Assert.Equal(MediaCatalogObservationStatuses.Deduplicated, response.Status);
        Assert.Equal(1, response.RecordedEpisodeCount);
        Assert.Equal(original.ObservationId, response.ObservationId);
        Assert.NotNull(winner);
        var saved = await db.MediaObservations.AsNoTracking().Include(row => row.Episodes).SingleAsync();
        if (!contenderIsNewer)
        {
            Assert.Equal(winner.UpdatedAt, saved.UpdatedAt);
        }
        Assert.Equal(winner.MatchStatus, saved.MatchStatus);
        Assert.Equal(winner.MatchStatus, response.MatchStatus);
        var expected = contenderIsNewer ? contenderRequest : firstWriter;
        Assert.Equal(expected.ObservedAt, saved.ObservedAt);
        Assert.Equal(expected.SeriesTitle, saved.SeriesTitle);
        Assert.Contains(expected.SeriesTitle, saved.ObservedTitle, StringComparison.Ordinal);
        var winningEpisode = Assert.Single(saved.Episodes);
        Assert.Equal(expected.Episodes.Single().ProviderEpisodeId, winningEpisode.ProviderEpisodeId);
        Assert.Equal(expected.Episodes.Single().EpisodeTitle, winningEpisode.EpisodeTitle);
        var rawEpisodes = await db.MediaProviderEpisodes.AsNoTracking().OrderBy(row => row.ProviderEpisodeId).ToListAsync();
        Assert.Collection(rawEpisodes,
            row =>
            {
                Assert.Equal("EPISODE1", row.ProviderEpisodeId);
                Assert.Equal(2, row.SeenCount);
            },
            row =>
            {
                Assert.Equal("EPISODE2", row.ProviderEpisodeId);
                Assert.Equal(1, row.SeenCount);
            });
        Assert.Empty(await db.MediaProviderEpisodeMappings.ToListAsync());
        Assert.Empty(await db.MediaProviderSeasonMappings.ToListAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Submit_RecoversFromDisappearingCatalogObservationOnSqlite(bool afterInsertCollision)
    {
        await using var keeper = new SqliteConnection($"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await keeper.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(keeper.ConnectionString).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await AssertDisappearingCatalogRecoveryAsync(db, afterInsertCollision);
    }

    [PostgresTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Submit_RecoversFromDisappearingCatalogObservationOnPostgres(bool afterInsertCollision)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await AssertDisappearingCatalogRecoveryAsync(schema.Db, afterInsertCollision);
    }

    private static async Task AssertDisappearingCatalogRecoveryAsync(ApplicationDbContext db, bool afterInsertCollision)
    {
        const int userId = 712;
        const string show = "Disappearing Catalog";
        const string seriesId = "DISAPPEARING";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        db.Users.Add(TestUserFactory.Create(userId, "catalog-race@example.test"));
        await db.SaveChangesAsync(timeout.Token);
        var winner = CatalogFixture.CreateController(db, userId);
        var title = new MediaTitle
        {
            Id = Guid.NewGuid(), CanonicalTitle = show, MediaKind = MediaKinds.Anime,
            EpisodeCount = 12, SupportsEpisodeProgress = true, PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode
        };

        async Task InsertWinnerAsync()
        {
            var response = GetResponse(await winner.Submit(CreateRequest(show, seriesId, 1), timeout.Token));
            Assert.Equal(MediaCatalogObservationStatuses.PendingMatch, response.Status);
            Assert.NotNull(response.ObservationId);
        }

        async Task CompleteWinnerAsync()
        {
            db.MediaTitles.Add(title);
            await db.SaveChangesAsync(timeout.Token);
            var response = GetResponse(await winner.Submit(CreateRequest(show, seriesId, 1), timeout.Token));
            Assert.Equal(MediaObservationStatuses.Matched, response.MatchStatus);
            Assert.Null(response.ObservationId);
            Assert.Empty(await db.MediaObservations.ToListAsync(timeout.Token));
        }

        if (!afterInsertCollision)
        {
            await InsertWinnerAsync();
        }
        var collision = new MediaObservationsApiTests.DuplicateObservationSaveInterceptor(
            afterInsertCollision ? InsertWinnerAsync : CompleteWinnerAsync,
            afterInsertCollision ? EntityState.Added : EntityState.Modified,
            afterInsertCollision ? CompleteWinnerAsync : null);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>(
                (DbContextOptions<ApplicationDbContext>)db.GetService<IDbContextOptions>())
            .AddInterceptors(collision).Options;
        await using var contenderDb = new ApplicationDbContext(options);
        var contender = CatalogFixture.CreateController(contenderDb, userId);
        var request = CreateRequest(show, seriesId, 2);
        request.Episodes[1].AvailableSubtitleLanguageCodes = ["en"];
        var result = await contender.Submit(request, timeout.Token);

        Assert.Equal(StatusCodes.Status200OK, Assert.IsType<OkObjectResult>(result.Result).StatusCode);
        var recovered = GetResponse(result);
        Assert.Equal(MediaCatalogObservationStatuses.Accepted, recovered.Status);
        Assert.Equal(2, recovered.RecordedEpisodeCount);
        Assert.Null(recovered.ObservationId);
        Assert.Equal(afterInsertCollision ? 1 : 0, collision.UniqueViolationCount);
        Assert.Equal(afterInsertCollision ? 0 : 1, collision.ConcurrencyViolationCount + collision.ForeignKeyViolationCount);
        var stored = await contenderDb.MediaProviderEpisodes.AsNoTracking()
            .SingleAsync(row => row.ProviderEpisodeId == "EPISODE2", timeout.Token);
        Assert.Contains("en", stored.AvailableSubtitleLanguageCodes);
        var seasons = new MediaProviderSeasonMappingService(contenderDb, NullLogger<MediaProviderSeasonMappingService>.Instance);
        var identity = new MediaEpisodeIdentityService(contenderDb, seasons, NullLogger<MediaEpisodeIdentityService>.Instance);
        var catalog = await identity.GetEpisodeCatalogAsync(title.Id, timeout.Token);
        Assert.NotNull(catalog);
        Assert.Contains(catalog.Episodes, episode => episode.EpisodeNumber == 2);
    }

    [Fact]
    public void Controller_RequiresAuthentication()
    {
        var attribute = Assert.Single(
            typeof(MediaCatalogObservationsController).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true));

        Assert.IsType<AuthorizeAttribute>(attribute);
    }

    [Fact]
    public async Task Submit_MatchedSeries_RecordsDestinationsWithoutAdvancingProgress()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        var title = fixture.AddTitle("Wistoria: Wand and Sword", episodeCount: 12);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Controller.Submit(
            CreateRequest("Wistoria: Wand and Sword", "GW4HM7WK9", episodeCount: 2),
            CancellationToken.None);

        var response = GetResponse(result);
        Assert.Equal(MediaCatalogObservationStatuses.Accepted, response.Status);
        Assert.Equal(MediaObservationStatuses.Matched, response.MatchStatus);
        Assert.Equal(title.Id.ToString(), response.MatchedMediaTitleId);
        Assert.Equal(2, response.RecordedEpisodeCount);
        Assert.Equal(0, response.RejectedEpisodeCount);

        Assert.Empty(fixture.Db.MediaObservations);
        Assert.Equal(0, await fixture.Db.MediaProviderOperations.CountAsync());

        var identities = await fixture.Db.MediaProviderEpisodes
            .OrderBy(item => item.ProviderEpisodeNumber)
            .ToListAsync();
        Assert.Equal(2, identities.Count);
        Assert.All(identities, item => Assert.Equal("GW4HM7WK9", item.ProviderSeriesId));
        Assert.All(identities, item => Assert.Equal("SEASON1", item.ProviderSeasonId));
    }

    [Fact]
    public async Task Submit_SeasonlessCatalogPublishesDirectMappingsBeforeCleanup()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        var title = fixture.AddTitle("Seasonless Show", 12);
        await fixture.Db.SaveChangesAsync();
        var request = CreateRequest(title.CanonicalTitle, "SEASONLESS", episodeCount: 2);
        request.ProviderSeasonId = null;
        request.SeasonNumber = null;
        request.SeasonTitle = null;

        var response = GetResponse(await fixture.Controller.Submit(request, default));

        Assert.Equal(title.Id.ToString(), response.MatchedMediaTitleId);
        Assert.Null(response.ObservationId);
        Assert.Empty(fixture.Db.MediaObservations);
        Assert.Empty(fixture.Db.MediaProviderSeasonMappings);
        var mappings = await fixture.Db.MediaProviderEpisodeMappings.OrderBy(row => row.CanonicalEpisodeNumber).ToListAsync();
        Assert.Equal([1, 2], mappings.Select(row => row.CanonicalEpisodeNumber));
        Assert.All(mappings, row =>
        {
            Assert.Equal(title.Id, row.MediaTitleId);
            Assert.Equal(MediaProviderSeasonMappingSources.Automatic, row.MappingSource);
        });
    }

    [Fact]
    public async Task Submit_RetiresCatalogWithDestinationsMappedToDifferentTitles()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        var title = fixture.AddTitle("Seasonless Show", 12);
        var other = fixture.AddTitle("Other Show", 12);
        var now = DateTimeOffset.UtcNow;
        fixture.Db.MediaProviderEpisodes.Add(new MediaProviderEpisode
        {
            Provider = "crunchyroll", ProviderEpisodeId = "EPISODE2", ProviderUrlPath = "/watch/EPISODE2",
            SeenCount = 1, FirstSeenAt = now, LastSeenAt = now
        });
        await fixture.Db.SaveChangesAsync();
        var mappings = new MediaProviderSeasonMappingService(fixture.Db, NullLogger<MediaProviderSeasonMappingService>.Instance);
        await mappings.EstablishDirectAsync("crunchyroll", "EPISODE2", other.Id, 2,
            MediaProviderSeasonMappingSources.Manual, default);
        var request = CreateRequest(title.CanonicalTitle, "SEASONLESS", episodeCount: 2);
        request.ProviderSeasonId = null;
        request.SeasonNumber = null;
        request.SeasonTitle = null;

        var response = GetResponse(await fixture.Controller.Submit(request, default));

        Assert.Equal(title.Id.ToString(), response.MatchedMediaTitleId);
        Assert.Equal(2, response.RecordedEpisodeCount);
        Assert.Null(response.ObservationId);
        Assert.Empty(fixture.Db.MediaObservations);
        Assert.Equal(other.Id, (await fixture.Db.MediaProviderEpisodeMappings.SingleAsync(
            row => row.ProviderEpisodeId == "EPISODE2")).MediaTitleId);
    }

    [Fact]
    public async Task Submit_PrivateLibraryTieBreakStaysAmbiguous()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        var privateTitle = fixture.AddTitle("Same title", episodeCount: 12);
        fixture.AddTitle("Same title", episodeCount: 12);
        fixture.AddLibraryEntry(privateTitle, "1");
        await fixture.Db.SaveChangesAsync();

        var response = GetResponse(await fixture.Controller.Submit(CreateRequest("Same title", "TIED", 1), default));
        Assert.Equal(MediaCatalogObservationStatuses.PendingMatch, response.Status);
        Assert.Equal(MediaObservationStatuses.Ambiguous, response.MatchStatus);
        Assert.NotNull(response.ObservationId);
        Assert.Equal(2, await fixture.Db.MediaObservationCandidates.CountAsync());
        Assert.Empty(fixture.Db.MediaProviderSeasonMappings);
        Assert.Empty(fixture.Db.MediaProviderEpisodeMappings);
        Assert.Single(await fixture.Db.MediaProviderEpisodes.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Resolve_ManualSeasonCorrectionReplacesDirectDestinationForBothViewers(bool resolveCatalog)
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        var original = fixture.AddTitle("Original Show", 12);
        var corrected = fixture.AddTitle("Corrected Show", 12);
        fixture.AddTitle("Corrected Show", 12);
        var viewer = TestUserFactory.Create(713, "corrected-season-viewer@example.test");
        fixture.Db.Users.Add(viewer);
        await fixture.Db.SaveChangesAsync();

        var mappings = new MediaProviderSeasonMappingService(fixture.Db, NullLogger<MediaProviderSeasonMappingService>.Instance);
        var playback = new MediaEpisodeIdentityService(fixture.Db, mappings, NullLogger<MediaEpisodeIdentityService>.Instance);
        var controller = fixture.CreateReviewController();
        var watch = new SubmitMediaObservationRequest
        {
            SiteIdentifier = "crunchyroll", SiteMediaId = "EPISODE2",
            ObservedUrl = "https://www.crunchyroll.com/watch/EPISODE2",
            ObservedTitle = original.CanonicalTitle, SeriesTitle = original.CanonicalTitle,
            EpisodeNumber = 2, WatchProgressPercent = 90
        };
        await mappings.EstablishDirectAsync("crunchyroll", "EPISODE2", original.Id, 2,
            MediaProviderSeasonMappingSources.Manual, default);
        var watched = Assert.IsType<SubmitMediaObservationResponse>(
            Assert.IsType<OkObjectResult>((await controller.Submit(watch, default)).Result).Value);
        Assert.Equal(MediaObservationStatuses.Matched, watched.MatchStatus);
        var observationId = Guid.Parse(watched.ObservationId!);

        foreach (var userId in new[] { fixture.UserId, viewer.Id })
        {
            foreach (var title in new[] { original, corrected })
            {
                fixture.Db.MediaLibraryEntries.Add(new MediaLibraryEntry
                {
                    Id = Guid.NewGuid(), UserId = userId, MediaTitleId = title.Id,
                    Status = MediaLibraryStatuses.Current, ProgressEpisodes = 1
                });
            }
        }
        await fixture.Db.SaveChangesAsync();
        Assert.Equal(watch.ObservedUrl, (await playback.ResolveContinueWatchingAsync(viewer.Id, original.Id, default))!.Url);

        var scanned = GetResponse(await fixture.Controller.Submit(CreateRequest(corrected.CanonicalTitle, "CORRECTED", 3), default));
        Assert.Equal(MediaObservationStatuses.Ambiguous, scanned.MatchStatus);
        if (resolveCatalog)
        {
            observationId = Guid.Parse(scanned.ObservationId!);
            await mappings.EstablishDirectAsync("crunchyroll", "EPISODE3", original.Id, 3,
                MediaProviderSeasonMappingSources.Manual, default);
        }
        else
        {
            fixture.Db.MediaObservationCandidates.Add(new MediaObservationCandidate
            {
                Id = Guid.NewGuid(), MediaObservationId = observationId, MediaTitleId = corrected.Id,
                CandidateSource = MediaObservationCandidateSources.CatalogTitleSearch,
                Title = corrected.CanonicalTitle, MediaKind = corrected.MediaKind, Score = 0.7m
            });
            await fixture.Db.SaveChangesAsync();
        }

        var candidateId = await fixture.Db.MediaObservationCandidates
            .Where(row => row.MediaObservationId == observationId && row.MediaTitleId == corrected.Id)
            .Select(row => row.Id).SingleAsync();
        Assert.IsType<OkObjectResult>((await controller.Resolve(observationId,
            new ResolveMediaObservationRequest { CandidateId = candidateId }, default)).Result);
        if (resolveCatalog)
        {
            Assert.IsType<OkObjectResult>((await controller.Submit(watch, default)).Result);
        }

        Assert.Empty(await fixture.Db.MediaProviderEpisodeMappings.ToListAsync());
        Assert.Equal(2, await fixture.Db.MediaLibraryEntries.Where(entry => entry.UserId == fixture.UserId
            && entry.MediaTitleId == corrected.Id).Select(entry => entry.ProgressEpisodes).SingleAsync());
        Assert.Equal(1, await fixture.Db.MediaLibraryEntries.Where(entry => entry.UserId == fixture.UserId
            && entry.MediaTitleId == original.Id).Select(entry => entry.ProgressEpisodes).SingleAsync());
        Assert.Equal("https://www.crunchyroll.com/watch/EPISODE3",
            (await playback.ResolveContinueWatchingAsync(fixture.UserId, corrected.Id, default))!.Url);
        Assert.Equal(watch.ObservedUrl, (await playback.ResolveContinueWatchingAsync(viewer.Id, corrected.Id, default))!.Url);
        foreach (var userId in new[] { fixture.UserId, viewer.Id })
        {
            Assert.Equal("unavailable", (await playback.ResolveContinueWatchingAsync(userId, original.Id, default))!.Outcome);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task Resolve_StaleCatalogPreservesNewerDirectMappingOnSqlite(bool hasSeasonKey) =>
        MediaObservationsApiTests.WithSqliteObservationDatabaseAsync(db =>
            AssertStaleCatalogPreservesNewerDirectMappingAsync(db, hasSeasonKey));

    [PostgresTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Resolve_StaleCatalogPreservesNewerDirectMappingOnPostgres(bool hasSeasonKey)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await AssertStaleCatalogPreservesNewerDirectMappingAsync(schema.Db, hasSeasonKey);
    }

    private static async Task AssertStaleCatalogPreservesNewerDirectMappingAsync(ApplicationDbContext db, bool hasSeasonKey)
    {
        const int userId = 712;
        const int secondViewerId = 713;
        const string seriesId = "BOUNDED";
        const string correctedTitle = "Corrected Season";
        var original = CreateTitle("Original Season");
        var corrected = CreateTitle(correctedTitle);
        db.AddRange(original, corrected, CreateTitle(correctedTitle), TestUserFactory.Create(userId, "bounds@example.test"),
            TestUserFactory.Create(secondViewerId, "newer-scan@example.test"));
        db.MediaLibraryEntries.AddRange(from viewer in new[] { userId, secondViewerId }
            from title in new[] { original, corrected }
            select new MediaLibraryEntry
            {
                Id = Guid.NewGuid(), UserId = viewer, MediaTitleId = title.Id, Status = MediaLibraryStatuses.Current,
                ProgressEpisodes = title == original ? 12 : 11
            });
        await db.SaveChangesAsync();

        var mappings = new MediaProviderSeasonMappingService(db, NullLogger<MediaProviderSeasonMappingService>.Instance);
        if (hasSeasonKey)
        {
            await mappings.EstablishAsync("crunchyroll", seriesId, "SEASON1", 1, original.Id, 0,
                MediaProviderSeasonMappingSources.Manual, 1m, default, episodeStart: 1, episodeEnd: 12);
            await mappings.EstablishDirectAsync("crunchyroll", "EPISODE13", original.Id, 13,
                MediaProviderSeasonMappingSources.Manual, default);
        }
        await mappings.EstablishDirectAsync("crunchyroll", "EPISODE12", original.Id, 12,
            MediaProviderSeasonMappingSources.Manual, default);

        var catalog = CatalogFixture.CreateController(db, userId);
        var oldScan = CreateScan(24);
        oldScan.Episodes.Single(row => row.ProviderEpisodeId == "EPISODE13").EpisodeNumber = 12;
        var scanned = GetResponse(await catalog.Submit(oldScan, default));
        Assert.Equal(MediaObservationStatuses.Ambiguous, scanned.MatchStatus);
        var observationId = Guid.Parse(scanned.ObservationId!);
        var candidate = await db.MediaObservationCandidates.SingleAsync(row =>
            row.MediaObservationId == observationId && row.MediaTitleId == corrected.Id);

        var newerScan = CreateScan(13);
        newerScan.ObservedAt = oldScan.ObservedAt!.Value.AddMinutes(1);
        newerScan.Episodes = [newerScan.Episodes.Single(row => row.ProviderEpisodeId == "EPISODE13")];
        var newerCatalog = CatalogFixture.CreateController(db, secondViewerId);
        GetResponse(await newerCatalog.Submit(newerScan, default));
        if (!hasSeasonKey)
        {
            var mappingController = new MediaCrunchyrollMappingsController(db, mappings);
            Assert.IsType<NoContentResult>(await mappingController.PutEpisode(original.Id, "EPISODE13",
                new() { CanonicalEpisodeNumber = 13 }, default));
        }
        Assert.Equal(13, await db.MediaProviderEpisodes.Where(row => row.ProviderEpisodeId == "EPISODE13")
            .Select(row => row.ProviderEpisodeNumber).SingleAsync());
        Assert.Equal(12, await db.Set<MediaObservationEpisode>().Where(row => row.MediaObservationId == observationId
                && row.ProviderEpisodeId == "EPISODE13").Select(row => row.EpisodeNumber).SingleAsync());

        var review = CatalogFixture.CreateReviewController(db, catalog.ControllerContext);
        Assert.IsType<OkObjectResult>((await review.Resolve(observationId,
            new ResolveMediaObservationRequest { CandidateId = candidate.Id, EpisodeOffset = 0 }, default)).Result);

        var seasons = await db.MediaProviderSeasonMappings.AsNoTracking().ToListAsync();
        var directs = await db.MediaProviderEpisodeMappings.AsNoTracking().ToListAsync();
        if (hasSeasonKey)
        {
            var season = Assert.Single(seasons);
            Assert.Equal(corrected.Id, season.MediaTitleId);
            Assert.Equal(1, season.ProviderEpisodeStart);
            Assert.Equal(12, season.ProviderEpisodeEnd);
            Assert.Single(directs);
        }
        else
        {
            Assert.Empty(seasons);
            Assert.Equal(23, directs.Count(row => row.MediaTitleId == corrected.Id));
            Assert.Contains(directs, row => row.ProviderEpisodeId == "EPISODE12" && row.MediaTitleId == corrected.Id
                && row.CanonicalEpisodeNumber == 12 && row.MappingSource == MediaProviderSeasonMappingSources.Manual);
        }
        var remaining = Assert.Single(directs, row => row.ProviderEpisodeId == "EPISODE13");
        Assert.Equal(original.Id, remaining.MediaTitleId);
        Assert.Equal(13, remaining.CanonicalEpisodeNumber);
        Assert.Equal(MediaProviderSeasonMappingSources.Manual, remaining.MappingSource);

        var playback = new MediaEpisodeIdentityService(db, mappings, NullLogger<MediaEpisodeIdentityService>.Instance);
        foreach (var viewer in new[] { userId, secondViewerId })
        {
            Assert.Equal("https://www.crunchyroll.com/watch/EPISODE13",
                (await playback.ResolveContinueWatchingAsync(viewer, original.Id, default))!.Url);
            Assert.Equal("https://www.crunchyroll.com/watch/EPISODE12",
                (await playback.ResolveContinueWatchingAsync(viewer, corrected.Id, default))!.Url);
        }

        var watch = await CatalogFixture.CreateReviewController(db, newerCatalog.ControllerContext).Submit(new()
        {
            SiteIdentifier = "crunchyroll", SiteMediaId = "EPISODE13",
            ObservedUrl = "https://www.crunchyroll.com/watch/EPISODE13", ObservedTitle = correctedTitle,
            ProviderSeriesId = seriesId, ProviderSeasonId = newerScan.ProviderSeasonId,
            SeasonNumber = newerScan.SeasonNumber, EpisodeNumber = 13
        }, default);
        var watched = Assert.IsType<SubmitMediaObservationResponse>(Assert.IsType<OkObjectResult>(watch.Result).Value);
        Assert.Equal(original.Id.ToString(), watched.MatchedMediaTitleId);
        Assert.Equal(13, watched.ResolvedProgress);

        SubmitMediaCatalogObservationRequest CreateScan(int episodes)
        {
            var scan = CreateRequest(correctedTitle, seriesId, episodes);
            if (!hasSeasonKey)
            {
                scan.ProviderSeasonId = null;
                scan.SeasonNumber = null;
            }
            return scan;
        }

        static MediaTitle CreateTitle(string title) => new()
        {
            Id = Guid.NewGuid(), CanonicalTitle = title, MediaKind = MediaKinds.Anime,
            EpisodeCount = 24, SupportsEpisodeProgress = true, PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode
        };
    }

    [Theory]
    [InlineData("series")]
    [InlineData("season")]
    [InlineData("season-number")]
    [InlineData("episode")]
    [InlineData("mapped-season")]
    [InlineData("mapped-sibling")]
    public async Task Submit_DirectWatchPreservesSuccessorWithConflictingCatalogEvidence(string conflict)
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        var title = fixture.AddTitle("Direct Watch Show", 12);
        var other = fixture.AddTitle("Other Show", 12);
        var number = conflict == "episode" ? 8 : 2;
        var hasSeparateSeason = conflict is "mapped-season" or "mapped-sibling";
        const string successorId = "SUCCESSOR2ENUS";
        fixture.Db.MediaProviderEpisodes.Add(new MediaProviderEpisode
        {
            Provider = "crunchyroll", ProviderEpisodeId = successorId, ProviderUrlPath = $"/watch/{successorId}",
            ContentKey = "SUCCESSOR2",
            ProviderSeriesId = conflict switch { "mapped-sibling" => null, "series" or "mapped-season" => "OTHERSERIES", _ => "SERIES" },
            ProviderSeasonId = conflict switch { "mapped-sibling" or "season-number" => null, "season" or "mapped-season" => "OTHERSEASON", _ => "SEASON" },
            ProviderSeasonNumber = conflict == "season-number" ? 2 : null,
            ProviderEpisodeNumber = number, SeenCount = 1,
            FirstSeenAt = DateTimeOffset.UtcNow, LastSeenAt = DateTimeOffset.UtcNow
        });
        if (conflict == "mapped-sibling")
        {
            fixture.Db.MediaProviderEpisodes.Add(new MediaProviderEpisode
            {
                Provider = "crunchyroll", ProviderEpisodeId = "SUCCESSOR2JAJP", ProviderUrlPath = "/watch/SUCCESSOR2JAJP",
                ContentKey = "SUCCESSOR2", ProviderSeriesId = "OTHERSERIES", ProviderSeasonId = "OTHERSEASON",
                ProviderEpisodeNumber = number, SeenCount = 1,
                FirstSeenAt = DateTimeOffset.UtcNow, LastSeenAt = DateTimeOffset.UtcNow
            });
        }
        if (hasSeparateSeason)
        {
            fixture.AddSeasonMapping("OTHERSERIES", "OTHERSEASON", null, other, 0);
        }
        await fixture.Db.SaveChangesAsync();

        var mappings = new MediaProviderSeasonMappingService(fixture.Db, NullLogger<MediaProviderSeasonMappingService>.Instance);
        await mappings.EstablishDirectAsync("crunchyroll", "DIRECT1", title.Id, 1,
            MediaProviderSeasonMappingSources.Manual, default);
        await mappings.EstablishDirectAsync("crunchyroll", successorId, other.Id, number,
            MediaProviderSeasonMappingSources.Automatic, default);
        var result = await fixture.CreateReviewController().Submit(new SubmitMediaObservationRequest
        {
            SiteIdentifier = "crunchyroll", SiteMediaId = "DIRECT1",
            ObservedUrl = "https://www.crunchyroll.com/watch/DIRECT1",
            ObservedTitle = title.CanonicalTitle, EpisodeNumber = 1,
            ProviderSeriesId = hasSeparateSeason ? null : "SERIES",
            ProviderSeasonId = hasSeparateSeason ? null : "SEASON",
            SeasonNumber = conflict == "season-number" ? 1 : null,
            NextEpisodeProviderId = successorId, NextEpisodeUrl = $"https://www.crunchyroll.com/watch/{successorId}",
            NextEpisodeNumber = 2, WatchProgressPercent = 90
        }, default);

        Assert.IsType<OkObjectResult>(result.Result);
        var successor = await fixture.Db.MediaProviderEpisodeMappings.AsNoTracking()
            .SingleAsync(row => row.ProviderEpisodeId == successorId);
        Assert.Equal(other.Id, successor.MediaTitleId);
        Assert.Equal(number, successor.CanonicalEpisodeNumber);
        Assert.Equal(MediaProviderSeasonMappingSources.Automatic, successor.MappingSource);
        if (conflict == "season-number")
        {
            var raw = await fixture.Db.MediaProviderEpisodes.AsNoTracking().SingleAsync(row => row.ProviderEpisodeId == successorId);
            Assert.Equal(2, raw.ProviderSeasonNumber);
            Assert.Null(raw.ProviderSeasonId);
            Assert.Equal(1, raw.SeenCount);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Submit_SingleEpisodeMoviePublishesStandaloneMapping(bool requiresReview)
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        var title = fixture.AddTitle("Feature Film", episodeCount: 1, format: "movie");
        title.IsCompletionOnly = true;
        title.SupportsEpisodeProgress = false;
        if (requiresReview)
        {
            fixture.AddTitle("Feature Film", episodeCount: 1, format: "movie").IsCompletionOnly = true;
        }
        var viewer = TestUserFactory.Create(713, "movie-viewer@example.test");
        fixture.Db.Users.Add(viewer);
        fixture.Db.MediaLibraryEntries.Add(new MediaLibraryEntry
        {
            Id = Guid.NewGuid(), UserId = viewer.Id, MediaTitleId = title.Id, Status = MediaLibraryStatuses.Current
        });
        await fixture.Db.SaveChangesAsync();

        var request = CreateRequest("Feature Film", "FEATURE", 1);
        var response = GetResponse(await fixture.Controller.Submit(request, default));
        if (requiresReview)
        {
            Assert.Equal(MediaObservationStatuses.Ambiguous, response.MatchStatus);
            var candidate = await fixture.Db.MediaObservationCandidates.SingleAsync(row => row.MediaTitleId == title.Id);
            var resolve = await fixture.CreateReviewController().Resolve(Guid.Parse(response.ObservationId!),
                new ResolveMediaObservationRequest { CandidateId = candidate.Id }, default);
            Assert.IsType<OkObjectResult>(resolve.Result);
            response = GetResponse(await fixture.Controller.Submit(request, default));
        }

        Assert.Equal(MediaCatalogObservationStatuses.Accepted, response.Status);
        Assert.Equal(title.Id.ToString(), response.MatchedMediaTitleId);
        var mapping = await fixture.Db.MediaProviderEpisodeMappings.SingleAsync();
        Assert.Equal("EPISODE1", mapping.ProviderEpisodeId);
        Assert.Equal(title.Id, mapping.MediaTitleId);
        Assert.Null(mapping.CanonicalEpisodeNumber);
        Assert.Equal(requiresReview ? "manual" : "automatic", mapping.MappingSource);
        Assert.Empty(fixture.Db.MediaProviderSeasonMappings);
        Assert.Empty(fixture.Db.MediaProviderOperations);
        var catalog = new MediaEpisodeIdentityService(fixture.Db,
            new MediaProviderSeasonMappingService(fixture.Db, NullLogger<MediaProviderSeasonMappingService>.Instance),
            NullLogger<MediaEpisodeIdentityService>.Instance);
        var playback = await catalog.ResolveContinueWatchingAsync(viewer.Id, title.Id, default);
        Assert.Equal("direct", playback!.Outcome);
        Assert.Equal("https://www.crunchyroll.com/watch/EPISODE1", playback.Url);
        Assert.Null(playback.EpisodeNumber);
    }

    [Fact]
    public async Task Resolve_NumberedMoviePublishesStandalonePlaybackForAnotherViewer()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        var movie = fixture.AddTitle("Feature Film", episodeCount: 1, format: "movie");
        movie.IsCompletionOnly = true;
        movie.SupportsEpisodeProgress = false;
        var viewer = TestUserFactory.Create(713, "movie-viewer@example.test");
        fixture.Db.Users.Add(viewer);
        foreach (var userId in new[] { fixture.UserId, viewer.Id })
        {
            fixture.Db.MediaLibraryEntries.Add(new MediaLibraryEntry
            {
                Id = Guid.NewGuid(), UserId = userId, MediaTitleId = movie.Id, Status = MediaLibraryStatuses.Current
            });
        }
        await fixture.Db.SaveChangesAsync();

        const string url = "https://www.crunchyroll.com/watch/FEATURE2";
        var controller = fixture.CreateReviewController();
        var submitted = await controller.Submit(new SubmitMediaObservationRequest
        {
            SiteIdentifier = "crunchyroll", SiteMediaId = "FEATURE2", ObservedUrl = url,
            ObservedTitle = movie.CanonicalTitle, SeriesTitle = movie.CanonicalTitle,
            EpisodeNumber = 2, ProgressHint = "2", WatchProgressPercent = 90
        }, default);
        var response = Assert.IsType<SubmitMediaObservationResponse>(Assert.IsType<OkObjectResult>(submitted.Result).Value);
        Assert.Equal(MediaObservationStatuses.NoMatch, response.MatchStatus);
        var observationId = Guid.Parse(response.ObservationId!);
        var candidate = new MediaObservationCandidate
        {
            Id = Guid.NewGuid(), MediaObservationId = observationId, MediaTitleId = movie.Id,
            CandidateSource = MediaObservationCandidateSources.CatalogTitleSearch,
            Title = movie.CanonicalTitle, MediaKind = movie.MediaKind, Score = 0.7m
        };
        fixture.Db.MediaObservationCandidates.Add(candidate);
        await fixture.Db.SaveChangesAsync();

        var resolved = await controller.Resolve(observationId,
            new ResolveMediaObservationRequest { CandidateId = candidate.Id, EpisodeOffset = 0 }, default);

        Assert.IsType<OkObjectResult>(resolved.Result);
        var mapping = await fixture.Db.MediaProviderEpisodeMappings.SingleAsync();
        Assert.Equal("FEATURE2", mapping.ProviderEpisodeId);
        Assert.Equal(movie.Id, mapping.MediaTitleId);
        Assert.Null(mapping.CanonicalEpisodeNumber);
        Assert.Equal(MediaProviderSeasonMappingSources.Manual, mapping.MappingSource);
        Assert.Equal(MediaLibraryStatuses.Completed, await fixture.Db.MediaLibraryEntries
            .Where(entry => entry.UserId == fixture.UserId).Select(entry => entry.Status).SingleAsync());

        var mappings = new MediaProviderSeasonMappingService(fixture.Db, NullLogger<MediaProviderSeasonMappingService>.Instance);
        var catalog = new MediaEpisodeIdentityService(fixture.Db, mappings, NullLogger<MediaEpisodeIdentityService>.Instance);
        var episodes = await catalog.GetEpisodeCatalogAsync(movie.Id, default);
        Assert.Empty(episodes!.Episodes);
        Assert.Equal(url, Assert.Single(episodes.StandaloneDestinations).Url);
        var playback = await catalog.ResolveContinueWatchingAsync(viewer.Id, movie.Id, default);
        Assert.Equal("direct", playback!.Outcome);
        Assert.Equal(url, playback.Url);
        Assert.Null(playback.EpisodeNumber);
    }

    [Fact]
    public async Task Submit_SingleEpisodeMovieUsesAutomaticDirectMappingWhenAutomaticSeasonIsOutOfRange()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        var movie = fixture.AddTitle("Feature Film", episodeCount: 1, format: "movie");
        movie.IsCompletionOnly = true;
        movie.SupportsEpisodeProgress = false;
        fixture.Db.MediaLibraryEntries.Add(new MediaLibraryEntry
        {
            Id = Guid.NewGuid(), UserId = fixture.UserId, MediaTitleId = movie.Id,
            Status = MediaLibraryStatuses.Current
        });
        await fixture.Db.SaveChangesAsync();

        var request = CreateRequest("Feature Film", "FEATURE", episodeCount: 1);
        var initial = GetResponse(await fixture.Controller.Submit(request, default));
        Assert.Equal(movie.Id.ToString(), initial.MatchedMediaTitleId);
        Assert.Equal(MediaProviderSeasonMappingSources.Automatic,
            (await fixture.Db.MediaProviderEpisodeMappings.SingleAsync()).MappingSource);

        fixture.AddTitle("Feature Film", episodeCount: 1, format: "movie").IsCompletionOnly = true;
        var season = fixture.AddTitle("Unrelated Season", episodeCount: 12);
        await fixture.Db.SaveChangesAsync();
        var mappings = new MediaProviderSeasonMappingService(fixture.Db, NullLogger<MediaProviderSeasonMappingService>.Instance);
        Assert.NotNull(await mappings.EstablishAsync("crunchyroll", request.ProviderSeriesId,
            request.ProviderSeasonId, request.SeasonNumber, season.Id, -1,
            MediaProviderSeasonMappingSources.Automatic, 1m, default, episodeStart: 1, episodeEnd: 12));

        var response = GetResponse(await fixture.Controller.Submit(request, default));
        Assert.Equal(MediaCatalogObservationStatuses.Accepted, response.Status);
        Assert.Equal(MediaObservationStatuses.Matched, response.MatchStatus);
        Assert.Equal(movie.Id.ToString(), response.MatchedMediaTitleId);

        var catalog = new MediaEpisodeIdentityService(fixture.Db, mappings, NullLogger<MediaEpisodeIdentityService>.Instance);
        var playback = await catalog.ResolveContinueWatchingAsync(fixture.UserId, movie.Id, default);
        Assert.Equal("direct", playback!.Outcome);
        Assert.Equal("https://www.crunchyroll.com/watch/EPISODE1", playback.Url);
        Assert.Null(playback.EpisodeNumber);

        var watch = await fixture.CreateReviewController().Submit(new SubmitMediaObservationRequest
        {
            SiteIdentifier = "crunchyroll", SiteMediaId = "EPISODE1",
            ObservedUrl = "https://www.crunchyroll.com/watch/EPISODE1",
            ObservedTitle = request.SeriesTitle, SeriesTitle = request.SeriesTitle,
            ProviderSeriesId = request.ProviderSeriesId, ProviderSeasonId = request.ProviderSeasonId,
            SeasonNumber = request.SeasonNumber, EpisodeNumber = 1
        }, default);
        var watchResponse = Assert.IsType<SubmitMediaObservationResponse>(Assert.IsType<OkObjectResult>(watch.Result).Value);
        Assert.Equal(MediaObservationStatuses.Matched, watchResponse.MatchStatus);
        Assert.Equal(movie.Id.ToString(), watchResponse.MatchedMediaTitleId);
    }

    [Fact]
    public async Task Submit_SingleEpisodicCatalogAndWatchPreferManualDirectMappingOverAutomaticSeason()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        var directTitle = fixture.AddTitle("Same Show", episodeCount: 12);
        fixture.AddTitle("Same Show", episodeCount: 12);
        var seasonTitle = fixture.AddTitle("Other Show", episodeCount: 12);
        await fixture.Db.SaveChangesAsync();

        var request = CreateRequest(directTitle.CanonicalTitle, "MANUALDIRECT", episodeCount: 1);
        var episode = Assert.Single(request.Episodes);
        var mappings = new MediaProviderSeasonMappingService(fixture.Db, NullLogger<MediaProviderSeasonMappingService>.Instance);
        await mappings.EstablishDirectAsync("crunchyroll", episode.ProviderEpisodeId, directTitle.Id, 3,
            MediaProviderSeasonMappingSources.Manual, default);
        Assert.NotNull(await mappings.EstablishAsync("crunchyroll", request.ProviderSeriesId,
            request.ProviderSeasonId, request.SeasonNumber, seasonTitle.Id, 0,
            MediaProviderSeasonMappingSources.Automatic, 1m, default, episodeStart: 1, episodeEnd: 12));

        var watch = await fixture.CreateReviewController().Submit(new SubmitMediaObservationRequest
        {
            SiteIdentifier = "crunchyroll", SiteMediaId = episode.ProviderEpisodeId,
            ObservedUrl = episode.ProviderUrl, ObservedTitle = request.SeriesTitle, SeriesTitle = request.SeriesTitle,
            ProviderSeriesId = request.ProviderSeriesId, ProviderSeasonId = request.ProviderSeasonId,
            SeasonNumber = request.SeasonNumber, EpisodeNumber = episode.EpisodeNumber
        }, default);
        var watchResponse = Assert.IsType<SubmitMediaObservationResponse>(Assert.IsType<OkObjectResult>(watch.Result).Value);
        Assert.Equal(MediaObservationStatuses.Matched, watchResponse.MatchStatus);
        Assert.Equal(directTitle.Id.ToString(), watchResponse.MatchedMediaTitleId);
        Assert.Equal(3, watchResponse.ResolvedProgress);
        Assert.False(watchResponse.RequiresResolution);

        var catalogResponse = GetResponse(await fixture.Controller.Submit(request, default));
        Assert.Equal(MediaCatalogObservationStatuses.Accepted, catalogResponse.Status);
        Assert.Equal(MediaObservationStatuses.Matched, catalogResponse.MatchStatus);
        Assert.Equal(directTitle.Id.ToString(), catalogResponse.MatchedMediaTitleId);
        Assert.Null(catalogResponse.ObservationId);
    }

    [Fact]
    public async Task Submit_PartialSingleEpisodeCatalogAndWatchPreferStoredManualSeasonOverAutomaticDirectMapping()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        var firstSeason = fixture.AddTitle("First Season", episodeCount: 12);
        var secondSeason = fixture.AddTitle("Second Season", episodeCount: 12);
        var directTitle = fixture.AddTitle("Direct Title", episodeCount: 12);
        var request = CreateRequest("Unmatched Provider Title", "STOREDSEASON", episodeCount: 1);
        var episode = Assert.Single(request.Episodes);
        request.ProviderSeasonId = null;
        request.SeasonNumber = null;
        request.SeasonTitle = null;
        fixture.Db.MediaProviderEpisodes.Add(new MediaProviderEpisode
        {
            Provider = "crunchyroll", ProviderEpisodeId = episode.ProviderEpisodeId,
            ProviderUrlPath = "/watch/EPISODE1", ProviderSeriesId = request.ProviderSeriesId,
            ProviderSeasonId = "SEASON2", ProviderSeasonNumber = 2, ProviderEpisodeNumber = episode.EpisodeNumber,
            SeenCount = 1
        });
        await fixture.Db.SaveChangesAsync();

        var mappings = new MediaProviderSeasonMappingService(fixture.Db, NullLogger<MediaProviderSeasonMappingService>.Instance);
        Assert.NotNull(await mappings.EstablishAsync("crunchyroll", request.ProviderSeriesId, "SEASON1", 1,
            firstSeason.Id, 0, MediaProviderSeasonMappingSources.Automatic, 1m, default));
        Assert.NotNull(await mappings.EstablishAsync("crunchyroll", request.ProviderSeriesId, "SEASON2", 2,
            secondSeason.Id, 0, MediaProviderSeasonMappingSources.Manual, 1m, default));
        await mappings.EstablishDirectAsync("crunchyroll", episode.ProviderEpisodeId, directTitle.Id, 3,
            MediaProviderSeasonMappingSources.Automatic, default);

        var watch = await fixture.CreateReviewController().Submit(new SubmitMediaObservationRequest
        {
            SiteIdentifier = "crunchyroll", SiteMediaId = episode.ProviderEpisodeId,
            ObservedUrl = episode.ProviderUrl, ObservedTitle = request.SeriesTitle, SeriesTitle = request.SeriesTitle,
            ProviderSeriesId = request.ProviderSeriesId, EpisodeNumber = episode.EpisodeNumber
        }, default);
        var watched = Assert.IsType<SubmitMediaObservationResponse>(Assert.IsType<OkObjectResult>(watch.Result).Value);
        Assert.Equal(MediaObservationStatuses.Matched, watched.MatchStatus);
        Assert.Equal(secondSeason.Id.ToString(), watched.MatchedMediaTitleId);
        Assert.Equal(episode.EpisodeNumber, watched.ResolvedProgress);

        var direct = await fixture.Db.MediaProviderEpisodeMappings.AsNoTracking()
            .SingleAsync(row => row.ProviderEpisodeId == episode.ProviderEpisodeId);
        Assert.Equal(directTitle.Id, direct.MediaTitleId);
        Assert.Equal(MediaProviderSeasonMappingSources.Automatic, direct.MappingSource);
        var catalog = GetResponse(await fixture.Controller.Submit(request, default));
        Assert.Equal(MediaObservationStatuses.Matched, catalog.MatchStatus);
        Assert.Equal(secondSeason.Id.ToString(), catalog.MatchedMediaTitleId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Submit_DirectWatchPublishesConsecutiveNextEpisodeForAnotherViewer(bool manualSuccessor)
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        var title = fixture.AddTitle("Direct Watch Show", 12);
        var viewer = TestUserFactory.Create(713, "next-episode-viewer@example.test");
        var entry = new MediaLibraryEntry
        {
            Id = Guid.NewGuid(), UserId = viewer.Id, MediaTitleId = title.Id,
            Status = MediaLibraryStatuses.Current, ProgressEpisodes = 1
        };
        fixture.Db.AddRange(viewer, entry);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.CreateReviewController().Submit(new SubmitMediaObservationRequest
        {
            SiteIdentifier = "crunchyroll", SiteMediaId = "DIRECT1",
            ObservedUrl = "https://www.crunchyroll.com/watch/DIRECT1",
            ObservedTitle = title.CanonicalTitle, SeriesTitle = title.CanonicalTitle, EpisodeNumber = 1,
            NextEpisodeProviderId = "DIRECT2", NextEpisodeUrl = "https://www.crunchyroll.com/watch/DIRECT2",
            NextEpisodeNumber = 2, WatchProgressPercent = 90
        }, default);
        var response = Assert.IsType<SubmitMediaObservationResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(MediaObservationStatuses.Matched, response.MatchStatus);
        Assert.Empty(fixture.Db.MediaProviderSeasonMappings);
        var mappings = new MediaProviderSeasonMappingService(fixture.Db, NullLogger<MediaProviderSeasonMappingService>.Instance);
        var catalog = new MediaEpisodeIdentityService(fixture.Db, mappings, NullLogger<MediaEpisodeIdentityService>.Instance);
        var playback = await catalog.ResolveContinueWatchingAsync(viewer.Id, title.Id, default);

        Assert.Equal("direct", playback!.Outcome);
        Assert.Equal(2, playback.EpisodeNumber);
        Assert.Equal("https://www.crunchyroll.com/watch/DIRECT2", playback.Url);

        if (manualSuccessor)
        {
            await mappings.EstablishDirectAsync("crunchyroll", "DIRECT3", title.Id, 3,
                MediaProviderSeasonMappingSources.Manual, default);
        }
        entry.ProgressEpisodes = 2;
        await fixture.Db.SaveChangesAsync();

        var second = await fixture.CreateReviewController().Submit(new SubmitMediaObservationRequest
        {
            SiteIdentifier = "crunchyroll", SiteMediaId = "DIRECT2",
            ObservedUrl = "https://www.crunchyroll.com/watch/DIRECT2",
            ObservedTitle = "Localized title", EpisodeNumber = 2,
            NextEpisodeProviderId = "DIRECT3", NextEpisodeUrl = "https://www.crunchyroll.com/watch/DIRECT3",
            NextEpisodeNumber = 3, WatchProgressPercent = 90
        }, default);
        var secondResponse = Assert.IsType<SubmitMediaObservationResponse>(Assert.IsType<OkObjectResult>(second.Result).Value);
        Assert.Equal(MediaObservationStatuses.Matched, secondResponse.MatchStatus);
        var successor = await fixture.Db.MediaProviderEpisodeMappings.AsNoTracking()
            .SingleAsync(row => row.ProviderEpisodeId == "DIRECT3");
        Assert.Equal(manualSuccessor ? MediaProviderSeasonMappingSources.Manual : MediaProviderSeasonMappingSources.Automatic,
            successor.MappingSource);
        Assert.Equal(title.Id, successor.MediaTitleId);
        Assert.Equal(3, successor.CanonicalEpisodeNumber);

        playback = await catalog.ResolveContinueWatchingAsync(viewer.Id, title.Id, default);
        Assert.Equal("direct", playback!.Outcome);
        Assert.Equal(3, playback.EpisodeNumber);
        Assert.Equal("https://www.crunchyroll.com/watch/DIRECT3", playback.Url);
    }

    [Fact]
    public async Task Submit_OrdinalOnlyCatalogBridgesLocalizedWatchAndPlayback()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        var title = fixture.AddTitle("Localized Show Season 2", 12);
        fixture.AddLibraryEntry(title, "54321");
        var viewer = TestUserFactory.Create(713, "localized-viewer@example.test");
        viewer.Settings = new UserSettings { PreferredMediaReleaseTrack = "dub:en" };
        fixture.Db.AddRange(viewer, new MediaLibraryEntry
        {
            Id = Guid.NewGuid(), UserId = viewer.Id, MediaTitleId = title.Id,
            Status = MediaLibraryStatuses.Current, ProgressEpisodes = 0
        });
        await fixture.Db.SaveChangesAsync();
        var request = CreateRequest(title.CanonicalTitle, "LOCALIZED", 1);
        request.ProviderSeasonId = null;
        request.SeasonNumber = 2;
        request.SeasonTitle = title.CanonicalTitle;
        request.Episodes[0].ProviderEpisodeId = "GE00340376JAJP";
        request.Episodes[0].ProviderUrl = "https://www.crunchyroll.com/watch/GE00340376JAJP";
        var submitted = GetResponse(await fixture.Controller.Submit(request, default));
        Assert.Equal(title.Id.ToString(), submitted.MatchedMediaTitleId);
        Assert.Equal("number:2", (await fixture.Db.MediaProviderSeasonMappings.SingleAsync()).ProviderSeasonKey);

        var result = await fixture.CreateReviewController().Submit(new SubmitMediaObservationRequest
        {
            SiteIdentifier = "crunchyroll", SiteMediaId = "GE00340376ENUS",
            ObservedUrl = "https://www.crunchyroll.com/watch/GE00340376ENUS",
            ObservedTitle = "Localized episode", EpisodeNumber = 1, WatchProgressPercent = 90
        }, default);
        var response = Assert.IsType<SubmitMediaObservationResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(MediaObservationStatuses.Matched, response.MatchStatus);
        Assert.Equal(1, await fixture.Db.MediaLibraryEntries.Where(row => row.UserId == fixture.UserId)
            .Select(row => row.ProgressEpisodes).SingleAsync());
        Assert.Empty(fixture.Db.MediaProviderEpisodeMappings);
        var mappings = new MediaProviderSeasonMappingService(fixture.Db, NullLogger<MediaProviderSeasonMappingService>.Instance);
        var catalog = new MediaEpisodeIdentityService(fixture.Db, mappings, NullLogger<MediaEpisodeIdentityService>.Instance);
        var episodes = await catalog.GetEpisodeCatalogAsync(title.Id, default);
        Assert.Contains(Assert.Single(episodes!.Episodes).Destinations,
            row => row.Url == "https://www.crunchyroll.com/watch/GE00340376ENUS");
        var playback = await catalog.ResolveContinueWatchingAsync(viewer.Id, title.Id, default);
        Assert.Equal("direct", playback!.Outcome);
        Assert.Equal("https://www.crunchyroll.com/watch/GE00340376ENUS", playback.Url);
    }

    [Fact]
    public async Task Submit_LongRunningSingleSeason_RecordsAll293ProviderEpisodes()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        fixture.AddTitle("Boruto: Naruto Next Generations", episodeCount: 293);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Controller.Submit(
            CreateRequest("Boruto: Naruto Next Generations", "GR75Q020Y", episodeCount: 293),
            CancellationToken.None);

        var response = GetResponse(result);
        Assert.Equal(MediaCatalogObservationStatuses.Accepted, response.Status);
        Assert.Equal(293, response.ObservedEpisodeCount);
        Assert.Equal(293, response.RecordedEpisodeCount);
        Assert.Equal(0, response.RejectedEpisodeCount);
        Assert.Equal(293, await fixture.Db.MediaProviderEpisodes.CountAsync());
    }

    [Fact]
    public async Task Submit_OverIngestionSafetyCap_IsRejected()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        var request = CreateRequest(
            "Unbounded Show",
            "GUNBOUND1",
            MediaCatalogObservationLimits.MaximumEpisodesPerObservation + 1);

        var result = await fixture.Controller.Submit(request, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var response = Assert.IsType<SubmitMediaCatalogObservationResponse>(badRequest.Value);
        Assert.Equal(MediaCatalogObservationStatuses.Rejected, response.Status);
        Assert.Empty(fixture.Db.MediaObservations);
        Assert.Empty(fixture.Db.MediaProviderEpisodes);
    }

    [Fact]
    public async Task Submit_ExtensionShapedPayload_KeepsSeasonOutOfSeriesMatchingTitle()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        var title = fixture.AddTitle("That Time I Got Reincarnated as a Slime", episodeCount: 24);
        await fixture.Db.SaveChangesAsync();
        var request = CreateRequest(
            "That Time I Got Reincarnated as a Slime",
            "GYZJ43JMR",
            episodeCount: 1);
        request.ProviderSeasonId = null;
        request.SeasonTitle = "Season 1";

        var result = await fixture.Controller.Submit(request, CancellationToken.None);

        var response = GetResponse(result);
        Assert.Equal(MediaCatalogObservationStatuses.Accepted, response.Status);
        Assert.Equal(title.Id.ToString(), response.MatchedMediaTitleId);
        Assert.Equal(1, response.RecordedEpisodeCount);
    }

    [Theory]
    [InlineData(2, "113693")]
    [InlineData(3, "121176")]
    [InlineData(4, "171110")]
    public async Task Submit_EstablishedProviderSeasonMapping_SelectsCanonicalTitleWithoutUsingOrdinal(
        int seasonNumber,
        string expectedAniListId)
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        var baseTitle = fixture.AddTitle("Ascendance of a Bookworm", 14, 2019, "TV");
        var seasonTwoTitle = fixture.AddTitle("Ascendance of a Bookworm Part 2", 12, 2020, "TV");
        fixture.AddTitle("Ascendance of a Bookworm Side Story", 1, 2020, "OVA");
        var seasonThreeTitle = fixture.AddTitle("Ascendance of a Bookworm Season 3", 10, 2022, "TV");
        var seasonFourTitle = fixture.AddTitle(
            "Ascendance of a Bookworm: Adopted Daughter of an Archduke",
            24,
            2026,
            "TV");
        fixture.AddLibraryEntry(baseTitle, "108268");
        fixture.AddLibraryEntry(seasonTwoTitle, "113693");
        fixture.AddLibraryEntry(seasonThreeTitle, "121176");
        fixture.AddLibraryEntry(seasonFourTitle, "171110");
        fixture.AddSequel(baseTitle, seasonTwoTitle);
        fixture.AddSequel(seasonTwoTitle, seasonThreeTitle);
        fixture.AddSequel(seasonThreeTitle, seasonFourTitle);
        await fixture.Db.SaveChangesAsync();

        var request = CreateRequest(
            "Ascendance of a Bookworm",
            "G6793XKZY",
            episodeCount: 3);
        request.ProviderSeasonId = $"BOOKWORM{seasonNumber}";
        request.SeasonTitle = $"Season {seasonNumber}";
        request.SeasonNumber = 99;
        var expectedTitle = expectedAniListId switch
        {
            "113693" => seasonTwoTitle,
            "121176" => seasonThreeTitle,
            _ => seasonFourTitle
        };
        fixture.AddSeasonMapping(
            request.ProviderSeriesId,
            request.ProviderSeasonId,
            request.SeasonNumber,
            expectedTitle,
            0);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Controller.Submit(request, CancellationToken.None);

        var response = GetResponse(result);
        Assert.Equal(MediaCatalogObservationStatuses.Accepted, response.Status);
        Assert.Equal(expectedTitle.Id.ToString(), response.MatchedMediaTitleId);
        Assert.Equal(3, response.RecordedEpisodeCount);
        var mapping = await fixture.Db.MediaProviderSeasonMappings.SingleAsync();
        Assert.Equal(expectedTitle.Id, mapping.MediaTitleId);
        Assert.Equal("manual", mapping.MappingSource);
    }

    [Fact]
    public async Task Submit_ProviderSeasonNumberAlone_DoesNotChooseAFranchisePosition()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        var seasonOne = fixture.AddTitle("Example", 12, 2024, "TV");
        var seasonTwo = fixture.AddTitle("Example Season 2", 12, 2025, "TV");
        fixture.AddLibraryEntry(seasonOne, "100");
        fixture.AddLibraryEntry(seasonTwo, "200");
        fixture.AddSequel(seasonOne, seasonTwo);
        await fixture.Db.SaveChangesAsync();
        var request = CreateRequest("Example", "EXAMPLE", 3);
        request.ProviderSeasonId = "EXAMPLE-SEASON";
        request.SeasonNumber = 2;

        var response = GetResponse(await fixture.Controller.Submit(request, CancellationToken.None));

        Assert.Equal(MediaCatalogObservationStatuses.PendingMatch, response.Status);
        Assert.Equal(MediaObservationStatuses.Ambiguous, response.MatchStatus);
        Assert.Null(response.MatchedMediaTitleId);
        Assert.Equal(3, await fixture.Db.MediaProviderEpisodes.CountAsync());
        Assert.Empty(fixture.Db.MediaProviderSeasonMappings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Submit_CumulativeCrunchyrollSeason_MapsEpisodesToSeasonLocalNumbers(bool sameProviderSeason)
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        var seasonOne = fixture.AddTitle("Wistoria: Wand and Sword", 12, 2024, "TV");
        var seasonTwo = fixture.AddTitle("Wistoria: Wand and Sword Season 2", 12, 2026, "TV");
        fixture.AddLibraryEntry(seasonOne, "174576");
        fixture.AddLibraryEntry(seasonTwo, "182300");
        fixture.AddSequel(seasonOne, seasonTwo);
        await fixture.Db.SaveChangesAsync();

        var request = CreateRequest("Wistoria: Wand and Sword", "GW4HM7WK9", episodeCount: 1);
        request.ProviderSeasonId = "WISTORIA2";
        request.SeasonTitle = "Season 2";
        request.SeasonNumber = 2;
        if (sameProviderSeason)
        {
            var mappings = new MediaProviderSeasonMappingService(fixture.Db, NullLogger<MediaProviderSeasonMappingService>.Instance);
            var first = await mappings.EstablishAsync("crunchyroll", request.ProviderSeriesId,
                request.ProviderSeasonId, request.SeasonNumber, seasonOne.Id, 0,
                MediaProviderSeasonMappingSources.Automatic, 1m, default);
            Assert.Null(first!.ProviderEpisodeEnd);
        }
        request.Episodes = Enumerable.Range(13, 12)
            .Select(number => new MediaCatalogEpisodeObservationDto
            {
                ProviderEpisodeId = number == 22 ? "GE00340376ENUS" : $"WISTORIA{number}",
                ProviderUrl = number == 22
                    ? "https://www.crunchyroll.com/watch/GE00340376ENUS/hoping-blooming-thundering"
                    : $"https://www.crunchyroll.com/watch/WISTORIA{number}/episode-{number}",
                EpisodeNumber = number,
                EpisodeTitle = number == 22 ? "Hoping, Blooming, Thundering" : $"Episode {number}"
            })
            .ToList();

        var result = await fixture.Controller.Submit(request, CancellationToken.None);

        var response = GetResponse(result);
        Assert.Equal(MediaCatalogObservationStatuses.Accepted, response.Status);
        Assert.Equal(seasonTwo.Id.ToString(), response.MatchedMediaTitleId);
        Assert.Equal(12, response.RecordedEpisodeCount);
        Assert.Empty(fixture.Db.MediaObservations);
        var episode22 = await fixture.Db.MediaProviderEpisodes
            .SingleAsync(episode => episode.ProviderEpisodeId == "GE00340376ENUS");
        var mapping = await fixture.Db.MediaProviderSeasonMappings.SingleAsync(row => row.MediaTitleId == seasonTwo.Id);
        Assert.Equal(10, episode22.ProviderEpisodeNumber + mapping.EpisodeOffset);
        Assert.Equal(22, episode22.ProviderEpisodeNumber);
        Assert.Equal(13, mapping.ProviderEpisodeStart);
        Assert.Equal(24, mapping.ProviderEpisodeEnd);
        if (sameProviderSeason)
        {
            var first = await fixture.Db.MediaProviderSeasonMappings.SingleAsync(row => row.MediaTitleId == seasonOne.Id);
            Assert.Equal(1, first.ProviderEpisodeStart ?? 1);
            Assert.Equal(12, first.ProviderEpisodeEnd);
        }
    }


    [Fact]
    public Task Submit_RetiresFullyMappedSplitSeasonOnSqlite() =>
        MediaObservationsApiTests.WithSqliteObservationDatabaseAsync(AssertSplitSeasonCoverageAsync);

    [PostgresFact]
    public async Task Submit_RetiresFullyMappedSplitSeasonOnPostgres()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await AssertSplitSeasonCoverageAsync(schema.Db);
    }

    private static async Task AssertSplitSeasonCoverageAsync(ApplicationDbContext db)
    {
        const int userId = 712;
        const string series = "SPLITSEASON";
        var first = CreateRegressionTitle("First Split Title");
        var second = CreateRegressionTitle("Second Split Title");
        first.EpisodeCount = second.EpisodeCount = 12;
        db.AddRange(first, second, TestUserFactory.Create(userId, "split@example.test"));
        await db.SaveChangesAsync();
        var mappings = new MediaProviderSeasonMappingService(db, NullLogger<MediaProviderSeasonMappingService>.Instance);
        await mappings.EstablishAsync("crunchyroll", series, "SEASON1", 1, first.Id, 0,
            MediaProviderSeasonMappingSources.Manual, 1m, default, episodeStart: 1, episodeEnd: 12);
        await mappings.EstablishAsync("crunchyroll", series, "SEASON1", 1, second.Id, -12,
            MediaProviderSeasonMappingSources.Automatic, 1m, default, episodeStart: 13, episodeEnd: 24);

        var request = CreateRequest("Unknown Split Season", series, 24);
        var response = GetResponse(await CatalogFixture.CreateController(db, userId).Submit(request, default));
        Assert.Equal(MediaCatalogObservationStatuses.Accepted, response.Status);
        Assert.Null(response.ObservationId);
        Assert.Empty(await db.MediaObservations.ToListAsync());

        var identity = new MediaEpisodeIdentityService(db, mappings, NullLogger<MediaEpisodeIdentityService>.Instance);
        foreach (var (title, offset) in new[] { (first, 0), (second, 12) })
        {
            var catalog = Assert.IsType<MediaEpisodeCatalogDto>(await identity.GetEpisodeCatalogAsync(title.Id, default));
            Assert.Equal(Enumerable.Range(1, 12), catalog.Episodes.Select(episode => episode.EpisodeNumber));
            Assert.Equal(Enumerable.Range(1 + offset, 12).Select(n => $"https://www.crunchyroll.com/watch/EPISODE{n}"),
                catalog.Episodes.Select(episode => Assert.Single(episode.Destinations).Url));
        }
    }

    [Fact]
    public async Task Submit_CatalogBatchDoesNotMatchSeasonWhenItsLastEpisodeExceedsBounds()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        var title = fixture.AddTitle("Manually Mapped Title", 12);
        await fixture.Db.SaveChangesAsync();
        var mappings = new MediaProviderSeasonMappingService(fixture.Db, NullLogger<MediaProviderSeasonMappingService>.Instance);
        await mappings.EstablishAsync("crunchyroll", "FIRSTCOUR", "SEASON1", 1, title.Id, 0,
            MediaProviderSeasonMappingSources.Manual, 1m, default);

        var response = GetResponse(await fixture.Controller.Submit(CreateRequest("Unknown Provider Title", "FIRSTCOUR", 13), default));

        Assert.Equal(MediaCatalogObservationStatuses.PendingMatch, response.Status);
        Assert.NotNull(response.ObservationId);
        Assert.Equal(13, response.RecordedEpisodeCount);
        Assert.Null(response.MatchedMediaTitleId);
    }

    [Fact]
    public async Task Submit_UnmatchedSeries_KeepsPendingMatchWithRawProviderFacts()
    {
        await using var fixture = await CatalogFixture.CreateAsync();

        var result = await fixture.Controller.Submit(
            CreateRequest("Unknown Catalog Title", "GUNKNOWN1", episodeCount: 1),
            CancellationToken.None);

        var response = GetResponse(result);
        Assert.Equal(MediaCatalogObservationStatuses.PendingMatch, response.Status);
        Assert.Equal(MediaObservationStatuses.NoMatch, response.MatchStatus);
        Assert.Equal(1, response.RecordedEpisodeCount);
        Assert.Null(response.MatchedMediaTitleId);
        Assert.Single(fixture.Db.MediaProviderEpisodes);
        Assert.Empty(fixture.Db.MediaProviderSeasonMappings);
        Assert.Single(fixture.Db.MediaObservations);
    }

    [Fact]
    public async Task Submit_RepeatedSeason_DeduplicatesProvenanceAndIncrementsSeenCount()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        fixture.AddTitle("Wistoria: Wand and Sword", episodeCount: 12);
        await fixture.Db.SaveChangesAsync();
        var request = CreateRequest("Wistoria: Wand and Sword", "GW4HM7WK9", episodeCount: 2);

        await fixture.Controller.Submit(request, CancellationToken.None);
        var duplicateResult = await fixture.Controller.Submit(request, CancellationToken.None);

        var response = GetResponse(duplicateResult);
        Assert.Equal(MediaCatalogObservationStatuses.Accepted, response.Status);
        Assert.Equal(MediaObservationStatuses.Matched, response.MatchStatus);
        Assert.Empty(fixture.Db.MediaObservations);
        var identities = await fixture.Db.MediaProviderEpisodes.ToListAsync();
        Assert.Equal(2, identities.Count);
        Assert.All(identities, item => Assert.Equal(2, item.SeenCount));
    }

    [Fact]
    public async Task Submit_MixedUnsafeEpisodes_RecordsOnlySafeDistinctDestinations()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        fixture.AddTitle("Wistoria: Wand and Sword", episodeCount: 12);
        await fixture.Db.SaveChangesAsync();
        var request = CreateRequest("Wistoria: Wand and Sword", "GW4HM7WK9", episodeCount: 1);
        request.Episodes.Add(new MediaCatalogEpisodeObservationDto
        {
            ProviderEpisodeId = "EPISODE1",
            ProviderUrl = "https://www.crunchyroll.com/watch/EPISODE1/duplicate",
            EpisodeNumber = 1
        });
        request.Episodes.Add(new MediaCatalogEpisodeObservationDto
        {
            ProviderEpisodeId = "EVIL2",
            ProviderUrl = "https://crunchyroll.example/watch/EVIL2",
            EpisodeNumber = 2
        });

        var result = await fixture.Controller.Submit(request, CancellationToken.None);

        var response = GetResponse(result);
        Assert.Equal(MediaCatalogObservationStatuses.Accepted, response.Status);
        Assert.Equal(1, response.RecordedEpisodeCount);
        Assert.Equal(2, response.RejectedEpisodeCount);
        Assert.Single(fixture.Db.MediaProviderEpisodes);
    }

    [Fact]
    public async Task Submit_MixedNullEpisode_FiltersItBeforeIdentityRecording()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        fixture.AddTitle("Wistoria: Wand and Sword", episodeCount: 12);
        await fixture.Db.SaveChangesAsync();
        var request = CreateRequest("Wistoria: Wand and Sword", "GW4HM7WK9", episodeCount: 1);
        request.Episodes.Add(null!);

        var result = await fixture.Controller.Submit(request, CancellationToken.None);

        var response = GetResponse(result);
        Assert.Equal(MediaCatalogObservationStatuses.Accepted, response.Status);
        Assert.Equal(2, response.ObservedEpisodeCount);
        Assert.Equal(1, response.RecordedEpisodeCount);
        Assert.Equal(1, response.RejectedEpisodeCount);
        Assert.Single(fixture.Db.MediaProviderEpisodes);
    }

    [Fact]
    public async Task Submit_NoSafeEpisodes_ReturnsRejectedWithoutPersistingEvidence()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        var request = CreateRequest("Unsafe", "GUNSAFE1", episodeCount: 1);
        request.Episodes[0].ProviderUrl = "https://crunchyroll.example/watch/EPISODE1";

        var result = await fixture.Controller.Submit(request, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var response = Assert.IsType<SubmitMediaCatalogObservationResponse>(badRequest.Value);
        Assert.Equal(MediaCatalogObservationStatuses.Rejected, response.Status);
        Assert.NotNull(response.Error);
        Assert.Empty(fixture.Db.MediaObservations);
        Assert.Empty(fixture.Db.MediaProviderEpisodes);
    }

    [Fact]
    public async Task RecordObservation_ResolvedCatalogProvenance_ReplaysEpisodeRecording()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        var title = fixture.AddTitle("Wistoria: Wand and Sword", episodeCount: 12);
        var request = CreateRequest("Wistoria: Wand and Sword", "GW4HM7WK9", episodeCount: 2);
        var now = DateTimeOffset.UtcNow;
        var observation = new MediaObservation
        {
            Id = Guid.NewGuid(),
            UserId = fixture.UserId,
            SiteIdentifier = MediaObservationSiteIdentifiers.Crunchyroll,
            SiteMediaId = "catalog:resolved-later",
            ObservedUrl = request.SeriesUrl,
            ObservedTitle = request.SeriesTitle,
            ObservedAt = now,
            SeriesTitle = request.SeriesTitle,
            SeasonTitle = request.SeasonTitle,
            ProviderSeriesId = request.ProviderSeriesId,
            ProviderSeasonId = request.ProviderSeasonId,
            SeasonNumber = request.SeasonNumber,
            IsCatalogObservation = true,
            Episodes = request.Episodes.Select(episode => new MediaObservationEpisode
            {
                Id = Guid.NewGuid(),
                ProviderEpisodeId = episode.ProviderEpisodeId,
                ProviderUrl = episode.ProviderUrl,
                EpisodeNumber = episode.EpisodeNumber,
                EpisodeTitle = episode.EpisodeTitle,
                ReleaseTrack = episode.ReleaseTrack,
                AvailableSubtitleLanguageCodes = episode.AvailableSubtitleLanguageCodes.ToList(),
                AvailableAudioLanguageCodes = episode.AvailableAudioLanguageCodes.ToList()
            }).ToList(),
            MatchStatus = MediaObservationStatuses.Matched,
            MediaTitleId = title.Id,
            CreatedAt = now,
            UpdatedAt = now
        };
        fixture.Db.MediaObservations.Add(observation);
        await fixture.Db.SaveChangesAsync();

        var service = new MediaEpisodeIdentityService(
            fixture.Db,
            new MediaProviderSeasonMappingService(
                fixture.Db,
                NullLogger<MediaProviderSeasonMappingService>.Instance),
            NullLogger<MediaEpisodeIdentityService>.Instance);
        await service.RecordObservationAsync(observation, CancellationToken.None);

        Assert.Equal(2, await fixture.Db.MediaProviderEpisodes.CountAsync());
        Assert.Equal(0, await fixture.Db.MediaProviderOperations.CountAsync());
    }

    private static SubmitMediaCatalogObservationResponse GetResponse(
        ActionResult<SubmitMediaCatalogObservationResponse> result)
    {
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<SubmitMediaCatalogObservationResponse>(ok.Value);
    }

    private static SubmitMediaCatalogObservationRequest CreateRequest(
        string seriesTitle,
        string providerSeriesId,
        int episodeCount)
    {
        return new SubmitMediaCatalogObservationRequest
        {
            Provider = MediaObservationSiteIdentifiers.Crunchyroll,
            SeriesUrl = $"https://www.crunchyroll.com/series/{providerSeriesId}/show",
            ProviderSeriesId = providerSeriesId,
            SeriesTitle = seriesTitle,
            ProviderSeasonId = "SEASON1",
            SeasonTitle = "Season 1",
            SeasonNumber = 1,
            Episodes = Enumerable.Range(1, episodeCount)
                .Select(number => new MediaCatalogEpisodeObservationDto
                {
                    ProviderEpisodeId = $"EPISODE{number}",
                    ProviderUrl = $"https://www.crunchyroll.com/watch/EPISODE{number}/episode-{number}",
                    EpisodeNumber = number,
                    EpisodeTitle = $"Episode {number}"
                })
                .ToList(),
            ObservedAt = DateTimeOffset.Parse("2026-08-08T10:00:00Z"),
            ExtensionVersion = "0.2.0"
        };
    }

    private sealed class CatalogFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly Guid _relationsSnapshotId = Guid.NewGuid();

        private CatalogFixture(
            SqliteConnection connection,
            ApplicationDbContext db,
            MediaCatalogObservationsController controller)
        {
            _connection = connection;
            Db = db;
            Controller = controller;
        }

        public int UserId { get; } = 712;
        public ApplicationDbContext Db { get; }
        public MediaCatalogObservationsController Controller { get; }

        public MediaObservationsController CreateReviewController() => CreateReviewController(Db, Controller.ControllerContext);

        public static MediaObservationsController CreateReviewController(ApplicationDbContext db, ControllerContext context)
        {
            var seasons = new MediaProviderSeasonMappingService(db, NullLogger<MediaProviderSeasonMappingService>.Instance);
            var matching = new MediaObservationMatchingService(db, seasons, new MediaRelationGraphRefreshQueue(),
                NullLogger<MediaObservationMatchingService>.Instance);
            var registry = new MediaProviderRegistry([]);
            var processor = new MediaProviderOperationProcessor(db, registry, NullLogger<MediaProviderOperationProcessor>.Instance);
            var events = new MediaLibraryEventHub();
            var progress = new MediaObservationProgressService(db, processor, events,
                NullLogger<MediaObservationProgressService>.Instance, seasons, matching);
            var catalog = new MediaCatalogCanonicalizationService((DbContextOptions<ApplicationDbContext>)db.GetService<IDbContextOptions>(),
                NullLogger<MediaCatalogCanonicalizationService>.Instance);
            return new MediaObservationsController(db, CreateUserManager(db), matching, progress,
                new MediaEpisodeIdentityService(db, seasons, NullLogger<MediaEpisodeIdentityService>.Instance),
                seasons, registry, events, NullLogger<MediaObservationsController>.Instance, catalog, processor)
            {
                ControllerContext = context
            };
        }

        public static async Task<CatalogFixture> CreateAsync()
        {
            const int userId = 712;
            const string email = "catalog-observations.api@example.com";
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new ApplicationDbContext(options);
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(TestUserFactory.Create(userId, email));
            await db.SaveChangesAsync();

            return new CatalogFixture(connection, db, CreateController(db, userId));
        }

        public static MediaCatalogObservationsController CreateController(ApplicationDbContext db, int userId)
        {
            var seasonMappingService = new MediaProviderSeasonMappingService(
                db,
                NullLogger<MediaProviderSeasonMappingService>.Instance);
            return new MediaCatalogObservationsController(
                db,
                CreateUserManager(db),
                new MediaObservationMatchingService(
                    db,
                    seasonMappingService,
                    new MediaRelationGraphRefreshQueue(),
                    NullLogger<MediaObservationMatchingService>.Instance),
                new MediaEpisodeIdentityService(
                    db,
                    seasonMappingService,
                    NullLogger<MediaEpisodeIdentityService>.Instance),
                NullLogger<MediaCatalogObservationsController>.Instance)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(
                            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
                            authenticationType: "Test"))
                    }
                }
            };
        }

        public MediaTitle AddTitle(
            string title,
            int? episodeCount,
            int? startYear = null,
            string? format = null)
        {
            var now = DateTimeOffset.UtcNow;
            var mediaTitle = new MediaTitle
            {
                Id = Guid.NewGuid(),
                CanonicalTitle = title,
                MediaKind = MediaKinds.Anime,
                Format = format,
                EpisodeCount = episodeCount,
                StartYear = startYear,
                SupportsEpisodeProgress = true,
                PrimaryProgressDimension = MediaProgressDimensions.Episode,
                ReleaseStatusDimension = MediaProgressDimensions.Episode,
                CreatedAt = now,
                UpdatedAt = now
            };
            Db.MediaTitles.Add(mediaTitle);
            return mediaTitle;
        }

        public void AddLibraryEntry(MediaTitle title, string providerMediaId)
        {
            var now = DateTimeOffset.UtcNow;
            Db.MediaLibraryEntries.Add(new MediaLibraryEntry
            {
                Id = Guid.NewGuid(),
                UserId = UserId,
                MediaTitleId = title.Id,
                Status = MediaLibraryStatuses.Current,
                CreatedAt = now,
                UpdatedAt = now
            });
            Db.MediaProviderLinks.Add(new MediaProviderLink
            {
                Id = Guid.NewGuid(),
                MediaTitleId = title.Id,
                Provider = MediaObservationSiteIdentifiers.AniList,
                ExternalId = providerMediaId,
                LinkSource = MediaMappingSources.Imported,
                LastVerifiedAt = now,
                RelationsLastVerifiedAt = now,
                RelationsSnapshotId = _relationsSnapshotId,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        public void AddSequel(MediaTitle source, MediaTitle sequel)
        {
            var now = DateTimeOffset.UtcNow;
            Db.MediaTitleRelations.Add(new MediaTitleRelation
            {
                Id = Guid.NewGuid(),
                MediaTitleId = source.Id,
                RelatedMediaTitleId = sequel.Id,
                RelationType = MediaRelationTypes.Sequel,
                SourceProvider = "anilist",
                FirstSeenAt = now,
                LastVerifiedAt = now
            });
        }

        public void AddSeasonMapping(
            string providerSeriesId,
            string providerSeasonId,
            int? providerSeasonNumber,
            MediaTitle title,
            int episodeOffset)
        {
            var now = DateTimeOffset.UtcNow;
            Db.MediaProviderSeasonMappings.Add(new MediaProviderSeasonMapping
            {
                Id = Guid.NewGuid(),
                Provider = MediaObservationSiteIdentifiers.Crunchyroll,
                ProviderSeriesId = providerSeriesId.ToUpperInvariant(),
                ProviderSeasonId = providerSeasonId.ToUpperInvariant(),
                ProviderSeasonKey = MediaProviderSeasonMappingService.SeasonKey(providerSeasonId, providerSeasonNumber)!,
                ProviderSeasonNumber = providerSeasonNumber,
                MediaTitleId = title.Id,
                EpisodeOffset = episodeOffset,
                MappingSource = MediaProviderSeasonMappingSources.Manual,
                Confidence = 1m,
                FirstSeenAt = now,
                LastVerifiedAt = now
            });
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private static UserManager<User> CreateUserManager(ApplicationDbContext db)
    {
        var store = new UserStore<User, IdentityRole<int>, ApplicationDbContext, int>(db);
        return new UserManager<User>(
            store,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<User>(),
            [],
            [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<UserManager<User>>.Instance);
    }
}
