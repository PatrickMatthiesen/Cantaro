using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class MediaEpisodeIdentityRemappingPolicyTests
{
    private static readonly DateTimeOffset ScanObservedAt = new(2026, 11, 20, 10, 2, 0, TimeSpan.Zero);

    [Fact]
    public async Task SeasonRemapMovesLocalizedAndOpaqueVariantsWithoutEditingRawEpisodes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = fixture.First;
        var second = fixture.Second;
        await fixture.MapAsync(first, 0, "automatic");
        await fixture.RecordScanAsync(
            ("GE00340376ENUS", 1, "A new chapter", new[] { "de" }),
            ("GE00340376JAJP", 1, "A new chapter", new[] { "en" }),
            ("OPAQUE123", 1, null, new[] { "fr" }));

        var before = await fixture.Catalog.GetEpisodeCatalogAsync(first.Id, default);
        Assert.Equal(3, Assert.Single(before!.Episodes).Destinations.Count);
        await fixture.MapAsync(second, 2, "manual");
        await fixture.MapAsync(first, 0, "automatic");

        Assert.Empty((await fixture.Catalog.GetEpisodeCatalogAsync(first.Id, default))!.Episodes);
        var after = await fixture.Catalog.GetEpisodeCatalogAsync(second.Id, default);
        var episode = Assert.Single(after!.Episodes);
        Assert.Equal(3, episode.EpisodeNumber);
        Assert.Equal("A new chapter", episode.Title);
        Assert.Equal(["de", "en", "fr"], episode.AvailableSubtitleLanguageCodes);
        Assert.Equal(3, episode.Destinations.Count);
        Assert.Equal(3, await fixture.Db.MediaProviderEpisodes.CountAsync());
        Assert.Empty(fixture.Db.MediaEpisodes);
    }

    [Fact]
    public async Task BookwormWatchRecoversScannedFactsAndBridgesNewEpisodesBySeasonNumber()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.MapAsync(fixture.First, 0, "automatic");
        await fixture.RecordScanAsync(("BOOKWORM1", 1, "Chapter One", new[] { "de" }));
        await fixture.RecordScanAsync(("BOOKWORM1", 1, null, new[] { "fr" }));
        var partial = Watch("BOOKWORM1", null, null);
        partial.ObservedAt = ScanObservedAt.AddMinutes(-1);
        partial.ProviderSeriesId = null;
        partial.EpisodeTitle = "Watch page title";
        await fixture.Catalog.RecordObservationAsync(partial, default);
        var match = await fixture.Mappings.ResolveAsync(partial, default);
        Assert.Equal(fixture.First.Id, match!.Title.Id);
        Assert.Equal(1, match.EpisodeNumber);
        var stored = await fixture.Db.MediaProviderEpisodes.AsNoTracking().SingleAsync();
        Assert.Equal("BOOKWORMSEASON", stored.ProviderSeasonId);
        Assert.Equal("Chapter One", stored.EpisodeTitle);
        Assert.Equal(["de", "fr"], stored.AvailableSubtitleLanguageCodes);
        Assert.Equal(3, stored.SeenCount);

        var newEpisode = Watch("BOOKWORM2", 2, 2);
        newEpisode.NextEpisodeProviderId = "BOOKWORM3";
        newEpisode.NextEpisodeUrl = "https://www.crunchyroll.com/watch/BOOKWORM3";
        newEpisode.NextEpisodeNumber = 3;
        await fixture.Catalog.RecordObservationAsync(newEpisode, default);
        var mapped = await fixture.Mappings.ResolveAsync(newEpisode, default);
        Assert.Equal(2, mapped!.EpisodeNumber);
        var raw = await fixture.Db.MediaProviderEpisodes.AsNoTracking().ToDictionaryAsync(row => row.ProviderEpisodeId);
        Assert.Equal("BOOKWORMSEASON", raw["BOOKWORM1"].ProviderSeasonId);
        Assert.Null(raw["BOOKWORM2"].ProviderSeasonId);
        Assert.Null(raw["BOOKWORM3"].ProviderSeasonId);
        var catalog = await fixture.Catalog.GetEpisodeCatalogAsync(fixture.First.Id, default);
        Assert.Equal([1, 2, 3], catalog!.Episodes.Select(episode => episode.EpisodeNumber));
    }

    [Fact]
    public async Task SharedScanSuppliesAnotherUsersPlaybackAndLanguageAvailability()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.MapAsync(fixture.First, 0, "automatic");
        fixture.Db.Users.AddRange(TestUserFactory.Create(23, "contributor@example.test"),
            TestUserFactory.Create(24, "viewer@example.test"));
        fixture.Db.UserSettings.Add(new UserSettings { UserId = 24, PreferredMediaReleaseTrack = "sub:de" });
        fixture.Db.MediaLibraryEntries.Add(new MediaLibraryEntry
        {
            Id = Guid.NewGuid(), UserId = 24, MediaTitleId = fixture.First.Id,
            Status = MediaLibraryStatuses.Current, ProgressEpisodes = 0
        });
        await fixture.Db.SaveChangesAsync();
        await fixture.RecordScanAsync(("BOOKWORM1", 1, "A World Without Books", new[] { "de" }));
        fixture.Db.MediaEpisodes.Add(new MediaEpisode
        {
            Id = Guid.NewGuid(), MediaTitleId = fixture.First.Id, EpisodeNumber = 1,
            Title = "SIMKL Episode Title", AvailableAudioLanguageCodes = ["en"]
        });
        await fixture.Db.SaveChangesAsync();

        var playback = await fixture.Catalog.ResolveContinueWatchingAsync(24, fixture.First.Id, default);
        Assert.Equal("https://www.crunchyroll.com/watch/BOOKWORM1", playback!.Url);
        var catalog = await fixture.Catalog.GetEpisodeCatalogAsync(fixture.First.Id, default);
        var episode = Assert.Single(catalog!.Episodes);
        Assert.Equal("SIMKL Episode Title", episode.Title);
        Assert.Equal(["de"], episode.AvailableSubtitleLanguageCodes);
        Assert.Equal(["en"], episode.AvailableAudioLanguageCodes);
        var library = await new MediaLibraryQueryService(fixture.Db).GetLibraryAsync(24, new(), default);
        Assert.Equal(1, Assert.Single(library.Items).AvailableReleasedCount);
        Assert.Equal(0, await fixture.Db.MediaLibraryEntries.Select(item => item.ProgressEpisodes).SingleAsync());
        Assert.Empty(fixture.Db.MediaObservations);
    }

    [Fact]
    public async Task InversePlaybackExcludesSeriesLessVariantWhenSiblingSeriesConflict()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.MapAsync(fixture.First, 0, "automatic");
        var userId = await fixture.AddViewerAsync(fixture.First);
        await fixture.RecordScanAsync(("GE00340376ENUS", 1, null, []));

        var seriesLessWatch = Watch("GE00340376JAJP", 1, null);
        seriesLessWatch.ProviderSeriesId = null;
        await fixture.Catalog.RecordObservationAsync(seriesLessWatch, default);

        var before = await fixture.Catalog.ResolveContinueWatchingAsync(userId, fixture.First.Id, default);
        Assert.Equal("https://www.crunchyroll.com/watch/GE00340376JAJP", before!.Url);

        var conflictingWatch = Watch("GE00340376DEDE", 1, 2);
        conflictingWatch.ProviderSeriesId = "OTHERSERIES";
        conflictingWatch.ProviderSeasonId = "OTHERSEASON";
        await fixture.Catalog.RecordObservationAsync(conflictingWatch, default);

        var after = await fixture.Catalog.ResolveContinueWatchingAsync(userId, fixture.First.Id, default);
        Assert.Equal("direct", after!.Outcome);
        Assert.Equal("https://www.crunchyroll.com/watch/GE00340376ENUS", after.Url);
    }

    [Fact]
    public async Task NamedManualSeasonBeatsAutomaticOrdinalMappingOnlyForValidEpisodes()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.First.EpisodeCount = 25;
        fixture.Second.EpisodeCount = 12;
        await fixture.Db.SaveChangesAsync();
        await fixture.Mappings.EstablishAsync("crunchyroll", "BOOKWORM", null, 2,
            fixture.First.Id, 0, "automatic", 1m, default);
        await fixture.Mappings.EstablishAsync("crunchyroll", "BOOKWORM", "BOOKWORMSEASON", null,
            fixture.Second.Id, -12, "manual", 1m, default, seasonTitle: "Bookworm Part 2");

        await AssertDestinationAsync(1, fixture.First, 1, "automatic");
        await AssertDestinationAsync(13, fixture.Second, 1, "manual");
        await AssertDestinationAsync(25, fixture.First, 25, "automatic");
        await AssertDestinationAsync(26, null, null, null);

        var mapped = await MediaProviderSeasonMappingService.GetMappedEpisodesAsync(fixture.Db,
            [fixture.First.Id, fixture.Second.Id], default);
        Assert.Collection(mapped.OrderBy(row => row.Episode.ProviderEpisodeNumber),
            row => Assert.Equal((fixture.First.Id, (int?)1, "BOOKWORM1"),
                (row.MediaTitleId, row.EpisodeNumber, row.Episode.ProviderEpisodeId)),
            row => Assert.Equal((fixture.Second.Id, (int?)1, "BOOKWORM13"),
                (row.MediaTitleId, row.EpisodeNumber, row.Episode.ProviderEpisodeId)),
            row => Assert.Equal((fixture.First.Id, (int?)25, "BOOKWORM25"),
                (row.MediaTitleId, row.EpisodeNumber, row.Episode.ProviderEpisodeId)));

        var firstViewer = await fixture.AddViewerAsync(fixture.First);
        var secondViewer = await fixture.AddViewerAsync(fixture.Second, firstViewer + 1);
        Assert.Equal("https://www.crunchyroll.com/watch/BOOKWORM1",
            (await fixture.Catalog.ResolveContinueWatchingAsync(firstViewer, fixture.First.Id, default))!.Url);
        Assert.Equal("https://www.crunchyroll.com/watch/BOOKWORM13",
            (await fixture.Catalog.ResolveContinueWatchingAsync(secondViewer, fixture.Second.Id, default))!.Url);

        async Task AssertDestinationAsync(int providerNumber, MediaTitle? title, int? canonicalNumber, string? source)
        {
            var watch = Watch($"BOOKWORM{providerNumber}", providerNumber, 2);
            watch.SeasonTitle = "Bookworm Part 2";
            await fixture.Catalog.RecordObservationAsync(watch, default);
            var match = await fixture.Mappings.ResolveAsync(watch, default);
            Assert.Equal(title?.Id, match?.Title.Id);
            Assert.Equal(source, match?.Source);
            Assert.Equal(canonicalNumber, match?.EpisodeNumber);
        }
    }

    [Fact]
    public async Task WatchResolvesByProviderSeasonTitleInBothDirectionsWithoutInferringOrdinal()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Mappings.EstablishAsync("crunchyroll", "BOOKWORM", null, 2,
            fixture.First.Id, 0, "automatic", 1m, default, seasonTitle: "Bookworm Part 2");
        await fixture.Mappings.EstablishAsync("crunchyroll", "BOOKWORM", null, 3,
            fixture.Second.Id, 0, "automatic", 1m, default, seasonTitle: "Bookworm Season 3");
        var userId = await fixture.AddViewerAsync(fixture.First);
        var watch = Watch("BOOKWORM1", 1, null);
        watch.UserId = userId;
        watch.SeasonTitle = "Bookworm Part 2";
        await fixture.Catalog.RecordObservationAsync(watch, default);
        Assert.Null((await fixture.Db.MediaProviderEpisodes.AsNoTracking().SingleAsync()).ProviderSeasonNumber);

        var match = await fixture.Mappings.ResolveAsync(watch, default);
        Assert.Equal(fixture.First.Id, match!.Title.Id);
        Assert.Equal(1, match.EpisodeNumber);
        Assert.Null((await fixture.Db.MediaProviderEpisodes.AsNoTracking().SingleAsync()).ProviderSeasonNumber);

        var playback = await fixture.Catalog.ResolveContinueWatchingAsync(userId, fixture.First.Id, default);
        Assert.Equal("direct", playback!.Outcome);
        Assert.Equal("https://www.crunchyroll.com/watch/BOOKWORM1", playback.Url);
    }

    [Fact]
    public async Task AutomaticSeasonScanOverridesEarlierAutomaticDirectWatchInBothDirections()
    {
        await using var fixture = await Fixture.CreateAsync();
        var userId = await fixture.AddViewerAsync(fixture.Second);
        var watch = Watch("BOOKWORM13", 13, null);
        watch.UserId = userId;
        watch.ProviderSeriesId = null;
        watch.ObservedTitle = fixture.First.CanonicalTitle;
        fixture.Db.MediaObservations.Add(watch);
        await fixture.Db.SaveChangesAsync();
        var matching = new MediaObservationMatchingService(fixture.Db, fixture.Mappings,
            new MediaRelationGraphRefreshQueue(), NullLogger<MediaObservationMatchingService>.Instance);
        await fixture.Catalog.RecordObservationAsync(watch, default);
        await matching.ProcessObservationAsync(watch, default);
        var direct = await fixture.Db.MediaProviderEpisodeMappings.SingleAsync();
        Assert.Equal(fixture.First.Id, direct.MediaTitleId);
        Assert.Equal("automatic", direct.MappingSource);

        await fixture.RecordScanAsync(("BOOKWORM13", 13, null, []));
        var scan = Watch("catalog", 13, 2);
        scan.IsCatalogObservation = true;
        scan.ProviderSeasonId = "BOOKWORMSEASON";
        scan.SeasonTitle = "Season 2 (13-24)";
        scan.Episodes.Add(new MediaObservationEpisode
        {
            ProviderEpisodeId = "BOOKWORM13", ProviderUrl = watch.ObservedUrl, EpisodeNumber = 13
        });
        await matching.EstablishMappingAsync(scan, fixture.Second, -12, "automatic", 1m, default);

        var match = await fixture.Mappings.ResolveAsync(watch, default);
        Assert.Equal(fixture.Second.Id, match!.Title.Id);
        Assert.Equal(1, match.EpisodeNumber);
        Assert.Empty((await fixture.Catalog.GetEpisodeCatalogAsync(fixture.First.Id, default))!.Episodes);
        var episode = Assert.Single((await fixture.Catalog.GetEpisodeCatalogAsync(fixture.Second.Id, default))!.Episodes);
        Assert.Equal(1, episode.EpisodeNumber);
        Assert.Equal(watch.ObservedUrl, Assert.Single(episode.Destinations).Url);
        var playback = await fixture.Catalog.ResolveContinueWatchingAsync(userId, fixture.Second.Id, default);
        Assert.Equal("direct", playback!.Outcome);
        Assert.Equal(watch.ObservedUrl, playback.Url);
    }

    [Theory]
    [InlineData(MediaProviderSeasonMappingSources.Automatic)]
    [InlineData(MediaProviderSeasonMappingSources.Manual)]
    public async Task OutOfRangeSeasonKeepsDirectWatchAndSharedPlayback(string seasonSource)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.First.EpisodeCount = 12;
        var watcherId = await fixture.AddViewerAsync(fixture.Second, 23);
        var otherViewerId = await fixture.AddViewerAsync(fixture.Second);
        var watch = Watch("BOOKWORM13", null, null);
        watch.UserId = watcherId;
        watch.ProviderSeriesId = null;
        watch.ObservedTitle = fixture.Second.CanonicalTitle;
        watch.ProgressHint = "1";
        fixture.Db.MediaObservations.Add(watch);
        await fixture.Db.SaveChangesAsync();
        var matching = new MediaObservationMatchingService(fixture.Db, fixture.Mappings,
            new MediaRelationGraphRefreshQueue(), NullLogger<MediaObservationMatchingService>.Instance);
        await fixture.Catalog.RecordObservationAsync(watch, default);
        await matching.ProcessObservationAsync(watch, default);
        var direct = await fixture.Db.MediaProviderEpisodeMappings.SingleAsync();
        Assert.Equal(fixture.Second.Id, direct.MediaTitleId);
        Assert.Equal(1, direct.CanonicalEpisodeNumber);
        Assert.Equal(MediaProviderSeasonMappingSources.Automatic, direct.MappingSource);

        await fixture.RecordScanAsync(Enumerable.Range(1, 13)
            .Select(number => ($"BOOKWORM{number}", number, (string?)null, Array.Empty<string>())).ToArray());
        await fixture.MapAsync(fixture.First, 0, seasonSource);
        await fixture.Mappings.EstablishDirectAsync("crunchyroll", "BOOKWORM12", fixture.Second.Id, 2,
            MediaProviderSeasonMappingSources.Automatic, default);

        var match = await fixture.Mappings.ResolveAsync(watch, default);
        Assert.NotNull(match);
        Assert.Equal(fixture.Second.Id, match.Title.Id);
        Assert.Equal(1, match.EpisodeNumber);
        var lastInRange = await fixture.Mappings.ResolveAsync(Watch("BOOKWORM12", 12, 2), default);
        Assert.NotNull(lastInRange);
        Assert.Equal(fixture.First.Id, lastInRange.Title.Id);
        Assert.Equal(12, lastInRange.EpisodeNumber);
        Assert.Equal(seasonSource, lastInRange.Source);
        var inRangeCatalog = await fixture.Catalog.GetEpisodeCatalogAsync(fixture.First.Id, default);
        Assert.Equal(Enumerable.Range(1, 12), inRangeCatalog!.Episodes.Select(item => item.EpisodeNumber));
        var episode = Assert.Single((await fixture.Catalog.GetEpisodeCatalogAsync(fixture.Second.Id, default))!.Episodes);
        Assert.Equal(1, episode.EpisodeNumber);
        Assert.Equal(watch.ObservedUrl, Assert.Single(episode.Destinations).Url);
        var playback = await fixture.Catalog.ResolveContinueWatchingAsync(otherViewerId, fixture.Second.Id, default);
        Assert.Equal("direct", playback!.Outcome);
        Assert.Equal(watch.ObservedUrl, playback.Url);
    }

    [Fact]
    public async Task UnidentifiedWatchCannotUseUnrelatedSeasonOrUnqualifiedNumberAlias()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Mappings.EstablishAsync("crunchyroll", "BOOKWORM", null, 1, fixture.First.Id,
            0, "automatic", 1m, default, seasonTitle: "Season 1");
        var watch = Watch("UNKNOWN", 1, null);
        watch.SeasonTitle = "Season 2";
        Assert.Null(await fixture.Mappings.ResolveAsync(watch, default));
        watch.SeasonTitle = "Season 1";
        watch.ProviderSeasonId = "UNKNOWNSEASON";
        Assert.Null(await fixture.Mappings.ResolveAsync(watch, default));
        watch.SeasonNumber = 1;
        Assert.Equal(fixture.First.Id, (await fixture.Mappings.ResolveAsync(watch, default))!.Title.Id);

        var first = Watch("KNOWN1", 1, 1);
        first.ProviderSeasonId = "SEASON1";
        var second = Watch("KNOWN2", 1, 2);
        second.ProviderSeasonId = "SEASON2";
        await fixture.Catalog.RecordObservationAsync(first, default);
        await fixture.Catalog.RecordObservationAsync(second, default);
        var unidentified = Watch("UNIDENTIFIED", 2, null);
        await fixture.Catalog.RecordObservationAsync(unidentified, default);
        Assert.Null(await fixture.Mappings.ResolveAsync(unidentified, default));
        Assert.Equal(1, Assert.Single((await fixture.Catalog.GetEpisodeCatalogAsync(fixture.First.Id, default))!.Episodes).EpisodeNumber);
    }

    [Theory]
    [InlineData(null, "SEASON2")]
    [InlineData("SEASON2", null)]
    public async Task SeasonAliasesRejectOverlapsAndReconcileEqualBounds(string? firstId, string? secondId)
    {
        await using var fixture = await Fixture.CreateAsync();
        var mapped = await fixture.Mappings.EstablishAsync("crunchyroll", "BOOKWORM", firstId, 2,
            fixture.First.Id, 0, "automatic", 1m, default, episodeStart: 1, episodeEnd: 12);
        Assert.Null(await fixture.Mappings.EstablishAsync("crunchyroll", "BOOKWORM", secondId, 2,
            fixture.Second.Id, 0, "automatic", 1m, default, episodeStart: 12, episodeEnd: 24));
        var updated = await fixture.Mappings.EstablishAsync("crunchyroll", "BOOKWORM", secondId, 2,
            fixture.Second.Id, 0, "manual", 1m, default, episodeStart: 1, episodeEnd: 12);
        Assert.Equal(mapped!.Id, updated!.Id);
        Assert.Equal("id:SEASON2", updated.ProviderSeasonKey);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Mappings.EstablishAsync("crunchyroll", "BOOKWORM", secondId, 2,
            fixture.Second.Id, 0, "manual", 1m, default, episodeStart: 12, episodeEnd: 24));
        await fixture.Mappings.EstablishAsync("crunchyroll", "BOOKWORM", null, 2,
            fixture.First.Id, 0, "automatic", 1m, default, episodeStart: 1, episodeEnd: 12);
        Assert.Equal(fixture.Second.Id, (await fixture.Db.MediaProviderSeasonMappings.SingleAsync()).MediaTitleId);
    }

    [Fact]
    public async Task SplitCourBoundsControlBothDirectionsAndDoNotOverlap()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.MapAsync(fixture.First, 0, "manual", 1, 12);
        await fixture.MapAsync(fixture.Second, -12, "manual", 13, 24);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.MapAsync(fixture.Second, 0, "manual", 12, 20));
        await fixture.RecordScanAsync(("LASTFIRST", 12, null, []), ("FIRSTNEXT", 13, null, []), ("AFTER", 25, null, []));

        var watch = Watch("FIRSTNEXT", 13, 2);
        var match = await fixture.Mappings.ResolveAsync(watch, default);
        Assert.Equal(fixture.Second.Id, match!.Title.Id);
        Assert.Equal(1, match.EpisodeNumber);
        Assert.Null(await fixture.Mappings.ResolveAsync(Watch("AFTER", 25, 2), default));
        var first = await fixture.Catalog.GetEpisodeCatalogAsync(fixture.First.Id, default);
        var second = await fixture.Catalog.GetEpisodeCatalogAsync(fixture.Second.Id, default);
        Assert.Equal(12, Assert.Single(first!.Episodes).EpisodeNumber);
        Assert.Equal("https://www.crunchyroll.com/watch/FIRSTNEXT",
            Assert.Single(Assert.Single(second!.Episodes).Destinations).Url);
    }

    [Fact]
    public async Task DirectManualMovieMappingIsSharedAndAutomaticWritesCannotChangeIt()
    {
        await using var fixture = await Fixture.CreateAsync();
        var season = await fixture.Mappings.EstablishAsync("crunchyroll", "BOOKWORM", "MOVIES", 1,
            fixture.Second.Id, 0, "manual", 1m, default);
        fixture.Second.IsCompletionOnly = true;
        fixture.Second.SupportsEpisodeProgress = false;
        await fixture.Db.SaveChangesAsync();
        var userId = await fixture.AddViewerAsync(fixture.Second);
        var fallback = await fixture.Catalog.ResolveContinueWatchingAsync(userId, fixture.Second.Id, default);
        Assert.Equal("series_fallback", fallback!.Outcome);
        Assert.Null(fallback.EpisodeNumber);
        await fixture.Db.MediaProviderSeasonMappings.Where(row => row.Id == season!.Id).ExecuteDeleteAsync();
        var unavailable = await fixture.Catalog.ResolveContinueWatchingAsync(userId, fixture.Second.Id, default);
        Assert.Equal("unavailable", unavailable!.Outcome);
        Assert.Null(unavailable.EpisodeNumber);
        var watch = Watch("MOVIE", null, null);
        watch.ProviderSeriesId = null;
        watch.EpisodeTitle = "The Movie";
        watch.ReleaseTrack = "dub:en";
        await fixture.Catalog.RecordObservationAsync(watch, default);
        await fixture.Mappings.EstablishDirectAsync("crunchyroll", "MOVIE", fixture.Second.Id, null, "manual", default);
        await fixture.Mappings.EstablishDirectAsync("crunchyroll", "MOVIE", fixture.First.Id, 1, "automatic", default);

        var mapping = await fixture.Mappings.ResolveAsync(watch, default);
        Assert.Equal(fixture.Second.Id, mapping!.Title.Id);
        Assert.Null(mapping.EpisodeNumber);
        Assert.Equal("manual", mapping.Source);
        var catalog = await fixture.Catalog.GetEpisodeCatalogAsync(fixture.Second.Id, default);
        var destination = Assert.Single(catalog!.StandaloneDestinations);
        Assert.Equal("https://www.crunchyroll.com/watch/MOVIE", destination.Url);
        Assert.Equal("The Movie", destination.Title);
        Assert.Equal(["en"], destination.AvailableAudioLanguageCodes);
        Assert.Equal(1, Assert.Single(catalog.ReleaseAvailability.Languages).DubReleasedEpisodes);
        var playback = await fixture.Catalog.ResolveContinueWatchingAsync(userId, fixture.Second.Id, default);
        Assert.Equal("direct", playback!.Outcome);
        Assert.Null(playback.EpisodeNumber);
    }

    [Fact]
    public async Task WatchOnlyDubIsAvailableInAnotherUsersLibrary()
    {
        await using var fixture = await Fixture.CreateAsync();
        var viewerId = await fixture.AddViewerAsync(fixture.First);
        fixture.Db.UserSettings.Add(new UserSettings { UserId = viewerId, PreferredMediaReleaseTrack = "dub:en" });
        fixture.Db.Users.Add(TestUserFactory.Create(23, "contributor@example.test"));
        await fixture.Db.SaveChangesAsync();
        await fixture.MapAsync(fixture.First, 0, "automatic");
        var watch = Watch("DUBONLY", 1, 2);
        watch.UserId = 23;
        watch.ReleaseTrack = "dub:en";
        await fixture.Catalog.RecordObservationAsync(watch, default);

        var library = await new MediaLibraryQueryService(fixture.Db).GetLibraryAsync(viewerId, new(), default);
        Assert.Equal(1, Assert.Single(library.Items).AvailableReleasedCount);
        var catalog = await fixture.Catalog.GetEpisodeCatalogAsync(fixture.First.Id, default);
        var availability = Assert.Single(catalog!.ReleaseAvailability.Languages);
        Assert.Equal("en", availability.LanguageCode);
        Assert.Equal(1, availability.DubReleasedEpisodes);
        Assert.Null(availability.SubReleasedEpisodes);
    }

    [Fact]
    public async Task LibraryTieBreakCanAdvancePrivateProgressWithoutPublishingMapping()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.First.CanonicalTitle = "Same Series Title";
        fixture.Second.CanonicalTitle = "Same Series Title";
        var user = TestUserFactory.Create(25, "tie-break@example.test");
        fixture.Db.Users.Add(user);
        fixture.Db.MediaLibraryEntries.Add(new MediaLibraryEntry
        {
            Id = Guid.NewGuid(), UserId = user.Id, MediaTitleId = fixture.First.Id,
            Status = MediaLibraryStatuses.Current, ProgressEpisodes = 0
        });
        var watch = Watch("PRIVATE1", 1, null);
        watch.UserId = user.Id;
        watch.ObservedTitle = "Same Series Title";
        watch.ProgressHint = "1";
        fixture.Db.MediaObservations.Add(watch);
        await fixture.Db.SaveChangesAsync();
        await fixture.Catalog.RecordObservationAsync(watch, default);
        var matching = new MediaObservationMatchingService(fixture.Db, fixture.Mappings,
            new MediaRelationGraphRefreshQueue(), NullLogger<MediaObservationMatchingService>.Instance);
        await matching.ProcessObservationAsync(watch, default);

        Assert.Equal(fixture.First.Id, watch.MediaTitleId);
        Assert.Equal(MediaObservationCandidateSources.LibraryTitleSearch,
            Assert.Single(watch.Candidates, candidate => candidate.IsAccepted).CandidateSource);
        await Progress(fixture.Db, fixture.Mappings, matching).TryEnqueueAutoProgressWithResultAsync(watch, default);
        Assert.Equal(1, await fixture.Db.MediaLibraryEntries.Select(row => row.ProgressEpisodes).SingleAsync());
        Assert.Empty(fixture.Db.MediaProviderSeasonMappings);
        Assert.Empty(fixture.Db.MediaProviderEpisodeMappings);
    }

    [Fact]
    public async Task SqliteRevalidatesProgressAfterConcurrentMappingEdit()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var editor = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(fixture.Db.Database.GetConnectionString()).Options);
        await AssertProgressRevalidatesAsync(fixture.Db, editor);
    }

    internal static async Task AssertProgressRevalidatesAsync(ApplicationDbContext watchDb, ApplicationDbContext editor)
    {
        var now = DateTimeOffset.UtcNow;
        var original = CreateTitle("Progress original");
        var corrected = CreateTitle("Progress corrected");
        var user = TestUserFactory.Create(26, "progress-map@example.test");
        watchDb.AddRange(user, original, corrected);
        var originalEntry = new MediaLibraryEntry
        {
            Id = Guid.NewGuid(), UserId = user.Id, MediaTitleId = original.Id,
            Status = MediaLibraryStatuses.Current, ProgressEpisodes = 3
        };
        var correctedEntry = new MediaLibraryEntry
        {
            Id = Guid.NewGuid(), UserId = user.Id, MediaTitleId = corrected.Id,
            Status = MediaLibraryStatuses.Current, ProgressEpisodes = 0
        };
        var link = new MediaProviderLink
        {
            Id = Guid.NewGuid(), MediaTitleId = corrected.Id, Provider = "anilist",
            ExternalId = "corrected-progress-id", LinkSource = MediaMappingSources.Imported
        };
        var account = new ConnectedServiceAccount
        {
            Id = 2626, UserId = user.Id, Service = "anilist", ExternalAccountId = "viewer-26",
            CreatedAt = now.UtcDateTime, UpdatedAt = now.UtcDateTime
        };
        var binding = new MediaLibraryProviderBinding
        {
            Id = Guid.NewGuid(), MediaLibraryEntryId = correctedEntry.Id,
            MediaProviderLinkId = link.Id, ConnectedServiceAccountId = account.Id,
            ProviderAccountId = account.ExternalAccountId
        };
        watchDb.AddRange(originalEntry, correctedEntry, link, account, binding);
        var observation = Watch("RACING13", 13, 2);
        observation.UserId = user.Id;
        observation.ProgressHint = "13";
        watchDb.Add(observation);
        await watchDb.SaveChangesAsync();

        var mappings = new MediaProviderSeasonMappingService(watchDb, NullLogger<MediaProviderSeasonMappingService>.Instance);
        var matching = new MediaObservationMatchingService(watchDb, mappings, new MediaRelationGraphRefreshQueue(),
            NullLogger<MediaObservationMatchingService>.Instance);
        var catalog = new MediaEpisodeIdentityService(watchDb, mappings, NullLogger<MediaEpisodeIdentityService>.Instance);
        await mappings.EstablishAsync("crunchyroll", "BOOKWORM", "BOOKWORMSEASON", 2,
            original.Id, 0, "automatic", 1m, default);
        var discovered = Watch("catalog", null, 2);
        discovered.IsCatalogObservation = true;
        discovered.ProviderSeasonId = "BOOKWORMSEASON";
        discovered.Episodes.Add(new MediaObservationEpisode
        {
            ProviderEpisodeId = "RACING13", ProviderUrl = "https://www.crunchyroll.com/watch/RACING13",
            EpisodeNumber = 13, EpisodeTitle = "Episode thirteen", AvailableAudioLanguageCodes = ["en"]
        });
        await catalog.RecordCatalogObservationAsync(discovered, default);
        discovered.Episodes.Single().AvailableAudioLanguageCodes = ["ja"];
        await catalog.RecordCatalogObservationAsync(discovered, default);
        await catalog.RecordObservationAsync(observation, default);
        var raw = await watchDb.MediaProviderEpisodes.AsNoTracking().SingleAsync();
        Assert.Equal(3, raw.SeenCount);
        Assert.Equal(["en", "ja"], raw.AvailableAudioLanguageCodes);
        await matching.ProcessObservationAsync(observation, default);
        Assert.Equal(original.Id, observation.MediaTitleId);
        Assert.Equal(13, observation.ResolvedProgress);

        var edit = new MediaProviderSeasonMappingService(editor, NullLogger<MediaProviderSeasonMappingService>.Instance);
        await edit.EstablishAsync("crunchyroll", "BOOKWORM", "BOOKWORMSEASON", 2,
            corrected.Id, -12, "manual", 1m, default);

        var progress = await Progress(watchDb, mappings, matching).TryEnqueueAutoProgressWithResultAsync(observation, default);
        Assert.True(progress.OutcomeSatisfied);
        Assert.Equal(1, progress.EnqueuedCount);
        Assert.Equal(corrected.Id, observation.MediaTitleId);
        Assert.Equal(3, await watchDb.MediaLibraryEntries.Where(row => row.Id == originalEntry.Id).Select(row => row.ProgressEpisodes).SingleAsync());
        Assert.Equal(1, await watchDb.MediaLibraryEntries.Where(row => row.Id == correctedEntry.Id).Select(row => row.ProgressEpisodes).SingleAsync());
        var operation = await watchDb.MediaProviderOperations.SingleAsync();
        Assert.Equal(binding.Id, operation.MediaLibraryProviderBindingId);
        var payload = System.Text.Json.JsonSerializer.Deserialize<AutoProgressUpdatePayload>(
            operation.PayloadJson, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Equal(1, payload!.ProgressEpisodes);
    }

    private static MediaObservationProgressService Progress(ApplicationDbContext db,
        MediaProviderSeasonMappingService mappings, MediaObservationMatchingService matching) => new(
        db, new MediaProviderOperationProcessor(db, new MediaProviderRegistry([]),
            NullLogger<MediaProviderOperationProcessor>.Instance), new MediaLibraryEventHub(),
        NullLogger<MediaObservationProgressService>.Instance, mappings, matching);

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(14)]
    [InlineData(16)]
    public async Task NextEpisodeCaptureRequiresTheNextProviderNumber(int? nextNumber)
    {
        await using var fixture = await Fixture.CreateAsync();
        var watch = Watch("WATCH", 14, 2);
        watch.ResolvedProgress = 2;
        watch.NextEpisodeProviderId = "NEXT";
        watch.NextEpisodeUrl = "https://www.crunchyroll.com/watch/NEXT";
        watch.NextEpisodeNumber = nextNumber;
        await fixture.Catalog.RecordObservationAsync(watch, default);
        Assert.Equal("WATCH", (await fixture.Db.MediaProviderEpisodes.SingleAsync()).ProviderEpisodeId);
    }

    [Fact]
    public async Task NextEpisodeCaptureRejectsDelayedWatchNumberThatDisagreesWithScannedNumber()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.RecordScanAsync(("WATCH", 14, null, []));
        var watch = Watch("WATCH", 2, 2);
        watch.ObservedAt = ScanObservedAt.AddMinutes(-1);
        watch.NextEpisodeProviderId = "NEXT";
        watch.NextEpisodeUrl = "https://www.crunchyroll.com/watch/NEXT";
        watch.NextEpisodeNumber = 3;
        await fixture.Catalog.RecordObservationAsync(watch, default);
        Assert.Equal(14, (await fixture.Db.MediaProviderEpisodes.SingleAsync()).ProviderEpisodeNumber);
    }

    private static MediaObservation Watch(string episodeId, int? number, int? seasonNumber) => new()
    {
        Id = Guid.NewGuid(), SiteIdentifier = "crunchyroll", SiteMediaId = episodeId,
        ObservedUrl = $"https://www.crunchyroll.com/watch/{episodeId}", ObservedTitle = "Ascendance of a Bookworm",
        ProviderSeriesId = "BOOKWORM", SeasonNumber = seasonNumber, EpisodeNumber = number,
        MatchStatus = MediaObservationStatuses.Pending
    };

    private static MediaTitle CreateTitle(string title) => new()
    {
        Id = Guid.NewGuid(), CanonicalTitle = title, MediaKind = MediaKinds.Anime,
        EpisodeCount = 24, SupportsEpisodeProgress = true,
        PrimaryProgressDimension = MediaProgressDimensions.Episode,
        ReleaseStatusDimension = MediaProgressDimensions.Episode
    };

    private sealed class Fixture(SqliteConnection connection, ApplicationDbContext db, MediaTitle first, MediaTitle second)
        : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = db;
        public MediaTitle First { get; } = first;
        public MediaTitle Second { get; } = second;
        public MediaProviderSeasonMappingService Mappings { get; } =
            new(db, NullLogger<MediaProviderSeasonMappingService>.Instance);
        public MediaEpisodeIdentityService Catalog => new(Db, Mappings, NullLogger<MediaEpisodeIdentityService>.Instance);

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection($"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
            await connection.OpenAsync();
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var first = CreateTitle("Ascendance of a Bookworm Part 2");
            var second = CreateTitle("Ascendance of a Bookworm Season 3");
            db.MediaTitles.AddRange(first, second);
            await db.SaveChangesAsync();
            return new Fixture(connection, db, first, second);
        }

        public Task MapAsync(MediaTitle title, int offset, string source, int? start = null, int? end = null) =>
            Mappings.EstablishAsync("crunchyroll", "BOOKWORM", "BOOKWORMSEASON", 2, title.Id,
                offset, source, 1m, default, episodeStart: start, episodeEnd: end);

        public async Task<int> AddViewerAsync(MediaTitle title, int userId = 24)
        {
            var user = TestUserFactory.Create(userId, $"viewer-{userId}@example.test");
            Db.Users.Add(user);
            Db.MediaLibraryEntries.Add(new MediaLibraryEntry
            {
                Id = Guid.NewGuid(), UserId = user.Id, MediaTitleId = title.Id,
                Status = MediaLibraryStatuses.Current, ProgressEpisodes = 0
            });
            await Db.SaveChangesAsync();
            return user.Id;
        }

        public Task RecordScanAsync(params (string Id, int Number, string? Title, string[] Subtitles)[] episodes)
        {
            var observation = Watch("catalog", null, 2);
            observation.UserId = 23;
            observation.ProviderSeasonId = "BOOKWORMSEASON";
            observation.IsCatalogObservation = true;
            observation.ObservedAt = ScanObservedAt;
            observation.Episodes = episodes.Select(item => new MediaObservationEpisode
            {
                ProviderEpisodeId = item.Id, ProviderUrl = $"https://www.crunchyroll.com/watch/{item.Id}",
                EpisodeNumber = item.Number, EpisodeTitle = item.Title, AvailableSubtitleLanguageCodes = [.. item.Subtitles]
            }).ToList();
            return Catalog.RecordCatalogObservationAsync(observation, default);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
