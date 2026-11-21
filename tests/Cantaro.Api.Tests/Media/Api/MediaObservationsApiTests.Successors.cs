using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantaro.Api.Tests;

public partial class MediaObservationsApiTests
{
    [Theory]
    [InlineData("series")]
    [InlineData("season")]
    [InlineData("ordinal")]
    public Task Submit_DelayedWatchDoesNotPublishSuccessorAcrossScopesOnSqlite(string conflict) =>
        WithSqliteObservationDatabaseAsync(db => AssertDelayedWatchDoesNotPublishSuccessorAsync(db, conflict));

    [PostgresTheory]
    [InlineData("series")]
    [InlineData("season")]
    [InlineData("ordinal")]
    public async Task Submit_DelayedWatchDoesNotPublishSuccessorAcrossScopesOnPostgres(string conflict)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await AssertDelayedWatchDoesNotPublishSuccessorAsync(schema.Db, conflict);
    }

    private static async Task AssertDelayedWatchDoesNotPublishSuccessorAsync(ApplicationDbContext db, string conflict)
    {
        const int delayedViewerId = 612;
        const string provider = MediaObservationSiteIdentifiers.Crunchyroll;
        const string episodeId = "DELAYEDWATCH4";
        const string successorId = "DELAYEDWATCH5";
        const string newSeries = "CURRENTCRSERIES";
        const string newSeason = "CURRENTCRSEASON";
        var observedAt = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
        var title = CreateSuccessorTestTitle("Winter Orchard");
        db.AddRange(title,
            TestUserFactory.Create(MediaObservationFixture.TestUserId, MediaObservationFixture.TestEmail),
            TestUserFactory.Create(delayedViewerId, "delayed-viewer@example.test"));
        await db.SaveChangesAsync();

        var mappings = new MediaProviderSeasonMappingService(db, NullLogger<MediaProviderSeasonMappingService>.Instance);
        Assert.True(await mappings.EstablishDirectAsync(provider, episodeId, title.Id, 4,
            MediaProviderSeasonMappingSources.Manual, default));

        var newer = await MediaObservationFixture.CreateController(db).Submit(new SubmitMediaObservationRequest
        {
            SiteIdentifier = provider, SiteMediaId = episodeId, ObservedUrl = $"https://www.crunchyroll.com/watch/{episodeId}",
            ObservedTitle = "Provider episode", EpisodeNumber = 4, ObservedAt = observedAt,
            ProviderSeriesId = newSeries, ProviderSeasonId = conflict == "ordinal" ? null : newSeason,
            SeasonNumber = conflict == "ordinal" ? 2 : null
        }, default);
        var initial = Assert.IsType<SubmitMediaObservationResponse>(Assert.IsType<OkObjectResult>(newer.Result).Value);
        Assert.Equal(MediaObservationStatuses.Matched, initial.MatchStatus);
        Assert.Equal(title.Id.ToString(), initial.MatchedMediaTitleId);
        Assert.Equal(4, initial.ResolvedProgress);
        Assert.False(await db.MediaProviderEpisodes.AnyAsync(row => row.ProviderEpisodeId == successorId));

        var result = await MediaObservationFixture.CreateController(db, userId: delayedViewerId).Submit(new SubmitMediaObservationRequest
        {
            SiteIdentifier = provider, SiteMediaId = episodeId, ObservedUrl = $"https://www.crunchyroll.com/watch/{episodeId}",
            ObservedTitle = "Provider episode", EpisodeNumber = 4, ObservedAt = observedAt.AddMinutes(-1),
            ProviderSeriesId = conflict == "series" ? "FORMERCRSERIES" : newSeries,
            ProviderSeasonId = conflict switch { "season" => "FORMERCRSEASON", "ordinal" => null, _ => newSeason },
            SeasonNumber = conflict == "ordinal" ? 1 : null,
            NextEpisodeProviderId = successorId, NextEpisodeUrl = $"https://www.crunchyroll.com/watch/{successorId}",
            NextEpisodeNumber = 5
        }, default);

        var delayed = Assert.IsType<SubmitMediaObservationResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.False(delayed.WasDeduplicated);
        Assert.Equal(MediaObservationStatuses.Matched, delayed.MatchStatus);
        Assert.Equal(title.Id.ToString(), delayed.MatchedMediaTitleId);
        Assert.Equal(4, delayed.ResolvedProgress);
        Assert.False(await db.MediaProviderEpisodeMappings.AnyAsync(row => row.ProviderEpisodeId == successorId));
        Assert.False(await db.MediaProviderEpisodes.AnyAsync(row => row.ProviderEpisodeId == successorId));
        var watched = await db.MediaProviderEpisodes.AsNoTracking().SingleAsync(row => row.ProviderEpisodeId == episodeId);
        Assert.Equal(newSeries, watched.ProviderSeriesId);
        Assert.Equal(conflict == "ordinal" ? null : newSeason, watched.ProviderSeasonId);
        Assert.Equal(conflict == "ordinal" ? (int?)2 : null, watched.ProviderSeasonNumber);
        Assert.Equal(4, watched.ProviderEpisodeNumber);
        Assert.Equal(observedAt, watched.LastSeenAt);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task CorrectedSuccessorScopeChangesAutomaticWatchAndPlaybackOnSqlite(bool catalogCorrection, bool manualOverride) =>
        WithSqliteObservationDatabaseAsync(db => AssertCorrectedSuccessorScopeAsync(db, catalogCorrection, manualOverride));

    [PostgresTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CorrectedSuccessorScopeChangesAutomaticWatchAndPlaybackOnPostgres(bool catalogCorrection, bool manualOverride)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await AssertCorrectedSuccessorScopeAsync(schema.Db, catalogCorrection, manualOverride);
    }

    private static async Task AssertCorrectedSuccessorScopeAsync(ApplicationDbContext db, bool catalogCorrection, bool manualOverride)
    {
        const int playbackViewerId = 612;
        const string provider = MediaObservationSiteIdentifiers.Crunchyroll;
        const string episodeId = "ORIGINALWATCH4";
        const string successorId = "CORRECTEDWATCH5";
        const string originalSeries = "ORIGINALCRSERIES";
        const string originalSeason = "ORIGINALCRSEASON";
        const string correctedSeries = "CORRECTEDCRSERIES";
        const string correctedSeason = "CORRECTEDCRSEASON";
        const string successorUrl = $"https://www.crunchyroll.com/watch/{successorId}";
        var observedAt = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
        var correctedAt = observedAt.AddMinutes(1);
        var original = CreateSuccessorTestTitle("Silver Harbor");
        var corrected = CreateSuccessorTestTitle("Winter Orchard");
        db.AddRange(original, corrected,
            TestUserFactory.Create(MediaObservationFixture.TestUserId, MediaObservationFixture.TestEmail),
            TestUserFactory.Create(playbackViewerId, "playback-viewer@example.test"),
            new MediaLibraryEntry
            {
                Id = Guid.NewGuid(), UserId = playbackViewerId, MediaTitleId = original.Id,
                ProgressEpisodes = 4, Status = MediaLibraryStatuses.Current
            });
        await db.SaveChangesAsync();

        var mappings = new MediaProviderSeasonMappingService(db, NullLogger<MediaProviderSeasonMappingService>.Instance);
        var identities = new MediaEpisodeIdentityService(db, mappings, NullLogger<MediaEpisodeIdentityService>.Instance);
        Assert.True(await mappings.EstablishDirectAsync(provider, episodeId, original.Id, 4,
            MediaProviderSeasonMappingSources.Manual, default));

        var watch = MediaObservationFixture.CreateController(db);
        var first = await watch.Submit(new SubmitMediaObservationRequest
        {
            SiteIdentifier = provider, SiteMediaId = episodeId, ObservedUrl = $"https://www.crunchyroll.com/watch/{episodeId}",
            ObservedTitle = original.CanonicalTitle, EpisodeNumber = 4, ObservedAt = observedAt,
            ProviderSeriesId = originalSeries, ProviderSeasonId = originalSeason, SeasonNumber = 1,
            NextEpisodeProviderId = successorId, NextEpisodeUrl = successorUrl, NextEpisodeNumber = 5
        }, default);
        var firstWatch = Assert.IsType<SubmitMediaObservationResponse>(Assert.IsType<OkObjectResult>(first.Result).Value);
        Assert.Equal(original.Id.ToString(), firstWatch.MatchedMediaTitleId);
        var inferred = await db.MediaProviderEpisodeMappings.AsNoTracking().SingleAsync(row => row.ProviderEpisodeId == successorId);
        Assert.Equal(MediaProviderSeasonMappingSources.Automatic, inferred.MappingSource);
        Assert.Equal(original.Id, inferred.MediaTitleId);
        Assert.Equal(5, inferred.CanonicalEpisodeNumber);

        var before = await identities.ResolveContinueWatchingAsync(playbackViewerId, original.Id, default);
        Assert.NotNull(before);
        Assert.Equal("direct", before.Outcome);
        Assert.Equal(successorUrl, before.Url);
        if (manualOverride)
        {
            Assert.True(await mappings.EstablishDirectAsync(provider, successorId, original.Id, 5,
                MediaProviderSeasonMappingSources.Manual, default));
        }

        Assert.Empty(await db.MediaProviderSeasonMappings.ToListAsync());
        if (catalogCorrection)
        {
            Assert.Equal(1, await identities.RecordCatalogObservationAsync(new MediaObservation
            {
                SiteIdentifier = provider, ObservedUrl = $"https://www.crunchyroll.com/series/{correctedSeries}",
                ObservedTitle = corrected.CanonicalTitle, ObservedAt = correctedAt, IsCatalogObservation = true,
                MatchStatus = MediaObservationStatuses.Pending,
                ProviderSeriesId = correctedSeries, ProviderSeasonId = correctedSeason, SeasonNumber = 2,
                Episodes = [new MediaObservationEpisode { ProviderEpisodeId = successorId, ProviderUrl = successorUrl, EpisodeNumber = 5 }]
            }, default));
            await AssertCorrectedScopeAndPlaybackAsync();

            if (!manualOverride)
            {
                var delayedCatalog = new MediaObservation
                {
                    Id = Guid.NewGuid(), UserId = MediaObservationFixture.TestUserId,
                    SiteIdentifier = provider, ObservedUrl = $"https://www.crunchyroll.com/series/{originalSeries}",
                    ObservedTitle = original.CanonicalTitle, SeriesTitle = original.CanonicalTitle,
                    ObservedAt = observedAt, IsCatalogObservation = true,
                    MatchStatus = MediaObservationStatuses.Pending,
                    ProviderSeriesId = originalSeries, ProviderSeasonId = originalSeason, SeasonNumber = 1,
                    Episodes = [new MediaObservationEpisode { ProviderEpisodeId = successorId, ProviderUrl = successorUrl, EpisodeNumber = 5 }]
                };
                Assert.Equal(1, await identities.RecordCatalogObservationAsync(delayedCatalog, default));
                db.MediaObservations.Add(delayedCatalog);
                await db.SaveChangesAsync();

                var matching = new MediaObservationMatchingService(db, mappings, new MediaRelationGraphRefreshQueue(),
                    NullLogger<MediaObservationMatchingService>.Instance);
                var processedCatalog = await matching.ProcessObservationAsync(delayedCatalog.Id, default);
                Assert.Null(processedCatalog.LastMatchError);
                Assert.Empty(await db.MediaProviderSeasonMappings.ToListAsync());
                await AssertCorrectedScopeAndPlaybackAsync();
            }
        }

        var second = await watch.Submit(new SubmitMediaObservationRequest
        {
            SiteIdentifier = provider, SiteMediaId = successorId, ObservedUrl = successorUrl,
            ObservedTitle = corrected.CanonicalTitle, EpisodeNumber = 5,
            ObservedAt = catalogCorrection ? correctedAt.AddMinutes(1) : correctedAt,
            ProviderSeriesId = catalogCorrection ? null : correctedSeries,
            ProviderSeasonId = catalogCorrection ? null : correctedSeason,
            SeasonNumber = catalogCorrection ? null : 2
        }, default);
        var correctedWatch = Assert.IsType<SubmitMediaObservationResponse>(Assert.IsType<OkObjectResult>(second.Result).Value);
        Assert.Equal(MediaObservationStatuses.Matched, correctedWatch.MatchStatus);
        Assert.Equal((manualOverride ? original.Id : corrected.Id).ToString(), correctedWatch.MatchedMediaTitleId);
        Assert.Equal(5, correctedWatch.ResolvedProgress);

        if (!catalogCorrection)
        {
            await AssertCorrectedScopeAndPlaybackAsync();
        }

        async Task AssertCorrectedScopeAndPlaybackAsync()
        {
            var raw = await db.MediaProviderEpisodes.AsNoTracking().SingleAsync(row => row.ProviderEpisodeId == successorId);
            Assert.Equal(correctedSeries, raw.ProviderSeriesId);
            Assert.Equal(correctedSeason, raw.ProviderSeasonId);
            Assert.Equal(2, raw.ProviderSeasonNumber);
            Assert.Equal(5, raw.ProviderEpisodeNumber);
            Assert.Equal(correctedAt, raw.LastSeenAt);

            var playback = await identities.ResolveContinueWatchingAsync(playbackViewerId, original.Id, default);
            Assert.NotNull(playback);
            Assert.Equal(5, playback.EpisodeNumber);
            Assert.Equal(manualOverride ? "direct" : "series_fallback", playback.Outcome);
            Assert.Equal(manualOverride ? successorUrl : $"https://www.crunchyroll.com/series/{originalSeries}", playback.Url);
            var catalog = await identities.GetEpisodeCatalogAsync(original.Id, default);
            Assert.NotNull(catalog);
            Assert.Equal(manualOverride, catalog.Episodes.SelectMany(episode => episode.Destinations)
                .Any(destination => destination.Url == successorUrl));
        }
    }

    private static MediaTitle CreateSuccessorTestTitle(string name) => new()
    {
        Id = Guid.NewGuid(), CanonicalTitle = name, MediaKind = MediaKinds.Anime,
        EpisodeCount = 12, SupportsEpisodeProgress = true,
        PrimaryProgressDimension = MediaProgressDimensions.Episode, ReleaseStatusDimension = MediaProgressDimensions.Episode
    };
}
