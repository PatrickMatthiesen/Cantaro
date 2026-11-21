using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantaro.Api.Tests;

public class MediaEpisodeIdentityServiceTests
{
    [Fact]
    public async Task CacheProviderEpisodeCatalog_PersistsSeasonMetadataAndSpecialsWithoutChangingProgress()
    {
        await using var fixture = await EpisodeIdentityFixture.CreateAsync(
            episodeCount: 4,
            progressEpisodes: 2);
        var now = DateTimeOffset.UtcNow;
        fixture.Db.MediaProviderLinks.Add(new MediaProviderLink
        {
            Id = Guid.NewGuid(),
            MediaTitleId = fixture.TitleId,
            Provider = "simkl",
            ExternalId = "tv:123",
            LinkSource = MediaMappingSources.Imported,
            CreatedAt = now,
            UpdatedAt = now
        });
        await fixture.Db.SaveChangesAsync();
        var regular = new MediaProviderWatchedEpisode[]
        {
            new() { SeasonNumber = 1, EpisodeNumber = 1, ProviderEpisodeId = "101", Title = "Pilot" },
            new() { SeasonNumber = 1, EpisodeNumber = 2, ProviderEpisodeId = "102", Title = "Second episode" },
            new() { SeasonNumber = 2, EpisodeNumber = 1, ProviderEpisodeId = "201", Title = "Season two premiere" },
            new() { SeasonNumber = 2, EpisodeNumber = 2, ProviderEpisodeId = "202", Title = "Finale" }
        };
        var specials = new MediaProviderWatchedEpisode[]
        {
            new() { SeasonNumber = 0, EpisodeNumber = 1, ProviderEpisodeId = "S1", Title = "Holiday Special" }
        };

        await fixture.Service.CacheProviderEpisodeCatalogAsync(
            fixture.TitleId,
            "simkl",
            "tv:123",
            regular,
            specials,
            expectedEpisodeCount: 4,
            CancellationToken.None);
        await fixture.Db.SaveChangesAsync();
        await fixture.Service.CacheProviderEpisodeCatalogAsync(
            fixture.TitleId,
            "simkl",
            "tv:123",
            regular,
            specials,
            expectedEpisodeCount: 4,
            CancellationToken.None);
        await fixture.Db.SaveChangesAsync();

        var catalog = await fixture.Service.GetEpisodeCatalogAsync(fixture.TitleId, CancellationToken.None);
        Assert.NotNull(catalog);
        Assert.Equal(4, await fixture.Db.MediaEpisodes.CountAsync());
        Assert.Equal(4, await fixture.Db.MediaEpisodeProviderContents.CountAsync(content => content.Provider == "simkl"));
        Assert.Empty(catalog.Episodes.SelectMany(episode => episode.Destinations));
        Assert.Equal(
            [(1, 1, 1), (2, 1, 2), (3, 2, 1), (4, 2, 2)],
            catalog.Episodes.Select(episode =>
                (episode.EpisodeNumber, episode.SeasonNumber, episode.SeasonEpisodeNumber)));
        Assert.Equal(
            ["Pilot", "Second episode", "Season two premiere", "Finale"],
            catalog.Episodes.Select(episode => episode.Title));
        var special = Assert.Single(catalog.Specials);
        Assert.Equal(1, special.SpecialEpisodeNumber);
        Assert.Equal("Holiday Special", special.Title);
        Assert.Equal(2, (await fixture.Db.MediaLibraryEntries.SingleAsync()).ProgressEpisodes);

        await fixture.Service.CacheProviderEpisodeCatalogAsync(
            fixture.TitleId,
            "simkl",
            "tv:123",
            [
                new MediaProviderWatchedEpisode { SeasonNumber = 1, EpisodeNumber = 1, ProviderEpisodeId = "101" },
                new MediaProviderWatchedEpisode { SeasonNumber = 1, EpisodeNumber = 3, ProviderEpisodeId = "103" }
            ],
            specials,
            expectedEpisodeCount: 4,
            CancellationToken.None);
        await fixture.Db.SaveChangesAsync();

        var cachedAfterPartialRefresh = await fixture.Service.GetEpisodeCatalogAsync(
            fixture.TitleId,
            CancellationToken.None);
        Assert.Equal(
            [(1, 1, 1), (2, 1, 2), (3, 2, 1), (4, 2, 2)],
            cachedAfterPartialRefresh!.Episodes.Select(episode =>
                (episode.EpisodeNumber, episode.SeasonNumber, episode.SeasonEpisodeNumber)));

        await fixture.Service.CacheProviderEpisodeCatalogAsync(
            fixture.TitleId,
            "simkl",
            "tv:123",
            [
                new MediaProviderWatchedEpisode { SeasonNumber = 1, EpisodeNumber = 1, ProviderEpisodeId = "CHANGED" },
                new MediaProviderWatchedEpisode { SeasonNumber = 1, EpisodeNumber = 2, ProviderEpisodeId = "102" },
                new MediaProviderWatchedEpisode { SeasonNumber = 2, EpisodeNumber = 1, ProviderEpisodeId = "201" },
                new MediaProviderWatchedEpisode { SeasonNumber = 2, EpisodeNumber = 2, ProviderEpisodeId = "202" }
            ],
            specials,
            expectedEpisodeCount: 4,
            CancellationToken.None);
        await fixture.Db.SaveChangesAsync();

        Assert.Equal(
            ["101", "102", "201", "202"],
            await fixture.Db.MediaEpisodeProviderContents
                .Where(content => content.Provider == "simkl")
                .OrderBy(content => content.ProviderSequenceNumber)
                .Select(content => content.ProviderContentKey)
                .ToListAsync());
    }

    [Fact]
    public async Task CacheProviderEpisodeCatalog_FillsMissingTitlesWithoutReplacingLocalTitles()
    {
        await using var fixture = await EpisodeIdentityFixture.CreateAsync(episodeCount: 2);
        var catalog = new[]
        {
            new MediaProviderWatchedEpisode { SeasonNumber = 1, EpisodeNumber = 1, ProviderEpisodeId = "101" },
            new MediaProviderWatchedEpisode { SeasonNumber = 1, EpisodeNumber = 2, ProviderEpisodeId = "102" }
        };
        await fixture.Service.CacheProviderEpisodeCatalogAsync(
            fixture.TitleId, "simkl", "tv:123", catalog, [], 2, CancellationToken.None);
        await fixture.Db.SaveChangesAsync();

        var episodes = await fixture.Db.MediaEpisodes
            .OrderBy(episode => episode.EpisodeNumber).ToListAsync();
        episodes[0].Title = "Local title";
        await fixture.Db.SaveChangesAsync();

        await fixture.Service.CacheProviderEpisodeCatalogAsync(
            fixture.TitleId,
            "simkl",
            "tv:123",
            [
                new MediaProviderWatchedEpisode { SeasonNumber = 1, EpisodeNumber = 1, ProviderEpisodeId = "101", Title = "SIMKL title" },
                new MediaProviderWatchedEpisode { SeasonNumber = 1, EpisodeNumber = 2, ProviderEpisodeId = "102", Title = "Second title" }
            ],
            [],
            expectedEpisodeCount: 2,
            CancellationToken.None);
        await fixture.Db.SaveChangesAsync();

        var refreshed = await fixture.Db.MediaEpisodes
            .AsNoTracking()
            .OrderBy(episode => episode.EpisodeNumber)
            .ToListAsync();
        Assert.Equal("Local title", refreshed[0].Title);
        Assert.Equal("Second title", refreshed[1].Title);
    }

    [Fact]
    public async Task RecordObservation_CreatesCurrentAndRenderedNextDestination()
    {
        await using var fixture = await EpisodeIdentityFixture.CreateAsync(progressEpisodes: 7);
        var observation = fixture.MakeObservation(
            resolvedProgress: 7,
            providerEpisodeId: "CURRENT7",
            observedUrl: "https://www.crunchyroll.com/watch/CURRENT7/episode-7",
            nextEpisodeId: "NEXT8",
            nextEpisodeUrl: "https://www.crunchyroll.com/watch/NEXT8/episode-8");

        await fixture.Service.RecordObservationAsync(observation, CancellationToken.None);

        var catalog = await fixture.Service.GetEpisodeCatalogAsync(fixture.TitleId, CancellationToken.None);
        Assert.Collection(catalog!.Episodes,
            item => Assert.Equal("https://www.crunchyroll.com/watch/CURRENT7", Assert.Single(item.Destinations).Url),
            item => Assert.Equal("https://www.crunchyroll.com/watch/NEXT8", Assert.Single(item.Destinations).Url));
    }


    [Fact]
    public async Task RecordObservation_RepeatedIdentityIncrementsSeenCount()
    {
        await using var fixture = await EpisodeIdentityFixture.CreateAsync();
        var observation = fixture.MakeObservation(3, "EPISODE3", "https://www.crunchyroll.com/watch/EPISODE3");

        await fixture.Service.RecordObservationAsync(observation, CancellationToken.None);
        await fixture.Service.RecordObservationAsync(observation, CancellationToken.None);

        var identity = await fixture.Db.MediaProviderEpisodes.SingleAsync();
        Assert.Equal(2, identity.SeenCount);
    }

    [Fact]
    public async Task RecordObservation_GroupsLocalizedCrunchyrollVariantsByContentKey()
    {
        await using var fixture = await EpisodeIdentityFixture.CreateAsync();

        await fixture.Service.RecordObservationAsync(
            fixture.MakeObservation(3, "GE00375194ENUS", "https://www.crunchyroll.com/watch/GE00375194ENUS/example"),
            CancellationToken.None);
        await fixture.Service.RecordObservationAsync(
            fixture.MakeObservation(3, "GE00375194JAJP", "https://www.crunchyroll.com/watch/GE00375194JAJP/example"),
            CancellationToken.None);

        var variants = await fixture.Db.MediaProviderEpisodes.OrderBy(row => row.ProviderEpisodeId).ToListAsync();
        Assert.Equal(2, variants.Count);
        Assert.All(variants, row => Assert.Equal("GE00375194", row.ContentKey));
        Assert.Equal(new[] { "en-US", "ja-JP" }, variants.Select(row => row.AudioLocale));
        var catalog = await fixture.Service.GetEpisodeCatalogAsync(fixture.TitleId, CancellationToken.None);
        Assert.Equal(2, Assert.Single(catalog!.Episodes).Destinations.Count);
    }

    [Fact]
    public async Task ResolveContinueWatching_PrefersObservedEnglishDubOverMoreSeenJapaneseVariant()
    {
        await using var fixture = await EpisodeIdentityFixture.CreateAsync(progressEpisodes: 2);
        await fixture.SetPreferredReleaseTrackAsync("dub:en");
        var japanese = fixture.MakeObservation(
            3,
            "GE00374382JAJP",
            "https://www.crunchyroll.com/watch/GE00374382JAJP/example");
        await fixture.Service.RecordObservationAsync(japanese, CancellationToken.None);
        await fixture.Service.RecordObservationAsync(japanese, CancellationToken.None);
        await fixture.Service.RecordObservationAsync(
            fixture.MakeObservation(
                3,
                "GE00374382ENUS",
                "https://www.crunchyroll.com/watch/GE00374382ENUS/example"),
            CancellationToken.None);

        var destination = await fixture.Service.ResolveContinueWatchingAsync(
            fixture.UserId,
            fixture.TitleId,
            CancellationToken.None);

        Assert.NotNull(destination);
        Assert.Equal("direct", destination.Outcome);
        Assert.Equal("https://www.crunchyroll.com/watch/GE00374382ENUS", destination.Url);
    }

    [Fact]
    public async Task ResolveContinueWatching_PrefersObservedJapaneseVariantForEnglishSubtitles()
    {
        await using var fixture = await EpisodeIdentityFixture.CreateAsync(progressEpisodes: 2);
        await fixture.SetPreferredReleaseTrackAsync("sub:en");
        var english = fixture.MakeObservation(
            3,
            "GE00374382ENUS",
            "https://www.crunchyroll.com/watch/GE00374382ENUS/example");
        await fixture.Service.RecordObservationAsync(english, CancellationToken.None);
        await fixture.Service.RecordObservationAsync(english, CancellationToken.None);
        await fixture.Service.RecordObservationAsync(
            fixture.MakeObservation(
                3,
                "GE00374382JAJP",
                "https://www.crunchyroll.com/watch/GE00374382JAJP/example"),
            CancellationToken.None);

        var destination = await fixture.Service.ResolveContinueWatchingAsync(
            fixture.UserId,
            fixture.TitleId,
            CancellationToken.None);

        Assert.NotNull(destination);
        Assert.Equal("direct", destination.Outcome);
        Assert.Equal("https://www.crunchyroll.com/watch/GE00374382JAJP", destination.Url);
    }

    [Fact]
    public async Task RecordObservation_GroupsOpaqueCrunchyrollVariantsByCanonicalEpisode()
    {
        await using var fixture = await EpisodeIdentityFixture.CreateAsync();

        await fixture.Service.RecordObservationAsync(
            fixture.MakeObservation(3, "G31UXQKKG", "https://www.crunchyroll.com/watch/G31UXQKKG/example"),
            CancellationToken.None);
        await fixture.Service.RecordObservationAsync(
            fixture.MakeObservation(3, "GRQW9GW7R", "https://www.crunchyroll.com/watch/GRQW9GW7R/example"),
            CancellationToken.None);

        Assert.All(await fixture.Db.MediaProviderEpisodes.ToListAsync(), row => Assert.Null(row.ContentKey));
        var catalog = await fixture.Service.GetEpisodeCatalogAsync(fixture.TitleId, CancellationToken.None);
        Assert.Equal(2, Assert.Single(catalog!.Episodes).Destinations.Count);
    }

    [Fact]
    public async Task ResolveContinueWatching_UsesObservedReleaseTrackForOpaqueCrunchyrollIds()
    {
        await using var fixture = await EpisodeIdentityFixture.CreateAsync(progressEpisodes: 2);
        await fixture.SetPreferredReleaseTrackAsync("dub:en");
        await fixture.Service.RecordObservationAsync(
            fixture.MakeSeriesObservation(
                new ObservedProviderEpisodeDto
                {
                    ProviderEpisodeId = "G31UXQKKG",
                    ProviderUrl = "https://www.crunchyroll.com/watch/G31UXQKKG/subbed",
                    EpisodeNumber = 3,
                    ReleaseTrack = "sub:en"
                }),
            CancellationToken.None);
        await fixture.Service.RecordObservationAsync(
            fixture.MakeSeriesObservation(
                new ObservedProviderEpisodeDto
                {
                    ProviderEpisodeId = "GRQW9GW7R",
                    ProviderUrl = "https://www.crunchyroll.com/watch/GRQW9GW7R/dubbed",
                    EpisodeNumber = 3,
                    ReleaseTrack = "dub:en"
                }),
            CancellationToken.None);

        var mappings = new MediaProviderSeasonMappingService(fixture.Db, NullLogger<MediaProviderSeasonMappingService>.Instance);
        await mappings.EstablishAsync("crunchyroll", "SERIES1", null, 4, fixture.TitleId, 0,
            MediaProviderSeasonMappingSources.Manual, 1m, CancellationToken.None);

        var destination = await fixture.Service.ResolveContinueWatchingAsync(
            fixture.UserId,
            fixture.TitleId,
            CancellationToken.None);

        Assert.NotNull(destination);
        Assert.Equal("https://www.crunchyroll.com/watch/GRQW9GW7R", destination.Url);
        Assert.Equal(
            ["dub:en", "sub:en"],
            (await fixture.Service.GetEpisodeCatalogAsync(fixture.TitleId, CancellationToken.None))!
                .Episodes.Single().Destinations.OrderBy(item => item.ReleaseTrack).Select(item => item.ReleaseTrack));
    }

    [Theory]
    [InlineData("dub:en", true)]
    [InlineData("sub:en", false)]
    public async Task PlaybackPrefersLanguageAvailabilityForOpaqueCrunchyrollIds(string preferredTrack, bool dubbed)
    {
        const string matchingId = "G31UXQKKG";
        const string unknownId = "GQ8BRAPFQ";
        const string otherId = "GRQW9GW7R";
        await using var fixture = await EpisodeIdentityFixture.CreateAsync(progressEpisodes: 2);
        await fixture.SetPreferredReleaseTrackAsync(preferredTrack);
        await RecordEpisodeAsync(matchingId, dubbed ? ["en"] : ["ja"], dubbed ? ["de"] : ["en"]);
        await RecordEpisodeAsync(unknownId, [], []);
        await RecordEpisodeAsync(otherId, dubbed ? ["ja"] : ["en"], dubbed ? ["en"] : ["de"]);
        await RecordEpisodeAsync(otherId, dubbed ? ["ja"] : ["en"], dubbed ? ["en"] : ["de"]);

        var raw = await fixture.Db.MediaProviderEpisodes.AsNoTracking().ToDictionaryAsync(row => row.ProviderEpisodeId);
        Assert.All(raw.Values, row =>
        {
            Assert.Null(row.ReleaseTrack);
            Assert.Null(row.AudioLocale);
        });
        Assert.True(raw[otherId].SeenCount > raw[matchingId].SeenCount);
        Assert.True(raw[otherId].LastSeenAt > raw[matchingId].LastSeenAt);

        var destination = await fixture.Service.ResolveContinueWatchingAsync(fixture.UserId, fixture.TitleId, default);
        Assert.NotNull(destination);
        Assert.Equal("direct", destination.Outcome);
        Assert.Equal(3, destination.EpisodeNumber);
        Assert.Equal(WatchUrl(matchingId), destination.Url);

        var catalog = await fixture.Service.GetEpisodeCatalogAsync(fixture.TitleId, default, preferredTrack);
        Assert.NotNull(catalog);
        Assert.Equal(
            new[] { WatchUrl(matchingId), WatchUrl(unknownId), WatchUrl(otherId) },
            Assert.Single(catalog.Episodes).Destinations.Select(item => item.Url));

        Task RecordEpisodeAsync(string id, List<string> audio, List<string> subtitles)
        {
            var observation = fixture.MakeSeriesObservation(new ObservedProviderEpisodeDto
            {
                ProviderEpisodeId = id, ProviderUrl = WatchUrl(id), EpisodeNumber = 3,
                AvailableAudioLanguageCodes = audio, AvailableSubtitleLanguageCodes = subtitles
            });
            observation.SeasonNumber = 1;
            return fixture.Service.RecordCatalogObservationAsync(observation, default);
        }

        static string WatchUrl(string id) => $"https://www.crunchyroll.com/watch/{id}";
    }

    [Fact]
    public async Task ResolveContinueWatching_FallsBackToCrunchyrollSeriesWhenNextEpisodeIsUnknown()
    {
        await using var fixture = await EpisodeIdentityFixture.CreateAsync(progressEpisodes: 7);
        await fixture.Service.RecordObservationAsync(
            fixture.MakeObservation(7, "EPISODE7", "https://www.crunchyroll.com/watch/EPISODE7"),
            CancellationToken.None);

        var destination = await fixture.Service.ResolveContinueWatchingAsync(
            fixture.UserId,
            fixture.TitleId,
            CancellationToken.None);

        Assert.NotNull(destination);
        Assert.Equal("series_fallback", destination.Outcome);
        Assert.Equal(8, destination.EpisodeNumber);
        Assert.Equal("https://www.crunchyroll.com/series/SERIES1", destination.Url);
    }

    [Fact]
    public async Task GetEpisodeCatalog_ReturnsKnownEpisodesAndSeriesDestination()
    {
        await using var fixture = await EpisodeIdentityFixture.CreateAsync();
        await fixture.Service.RecordObservationAsync(
            fixture.MakeSeriesObservation(
                new ObservedProviderEpisodeDto
                {
                    ProviderEpisodeId = "EPISODE1",
                    ProviderUrl = "https://www.crunchyroll.com/watch/EPISODE1/one",
                    EpisodeNumber = 1,
                    EpisodeTitle = "One"
                },
                new ObservedProviderEpisodeDto
                {
                    ProviderEpisodeId = "EPISODE2",
                    ProviderUrl = "https://www.crunchyroll.com/watch/EPISODE2/two",
                    EpisodeNumber = 2,
                    EpisodeTitle = "Two"
                }),
            CancellationToken.None);

        var mappings = new MediaProviderSeasonMappingService(fixture.Db, NullLogger<MediaProviderSeasonMappingService>.Instance);
        await mappings.EstablishAsync("crunchyroll", "SERIES1", null, 4, fixture.TitleId, 0,
            MediaProviderSeasonMappingSources.Manual, 1m, CancellationToken.None);

        var catalog = await fixture.Service.GetEpisodeCatalogAsync(fixture.TitleId, CancellationToken.None);
        Assert.Equal("https://www.crunchyroll.com/series/SERIES1", Assert.Single(catalog!.SeriesDestinations).Url);
        Assert.Equal(2, catalog.Episodes.Count);
        Assert.All(catalog.Episodes, episode => Assert.Single(episode.Destinations));
    }


    [Fact]
    public async Task NextEpisodeBeyondCanonicalCountStaysInRawCatalog()
    {
        await using var fixture = await EpisodeIdentityFixture.CreateAsync(episodeCount: 12);
        var observation = fixture.MakeObservation(
            12,
            "FINALE12",
            "https://www.crunchyroll.com/watch/FINALE12",
            "OTHER1",
            "https://www.crunchyroll.com/watch/OTHER1");

        await fixture.Service.RecordObservationAsync(observation, CancellationToken.None);

        Assert.Equal(2, await fixture.Db.MediaProviderEpisodes.CountAsync());
        var catalog = await fixture.Service.GetEpisodeCatalogAsync(fixture.TitleId, CancellationToken.None);
        Assert.Equal(12, Assert.Single(catalog!.Episodes).EpisodeNumber);
    }

    [Fact]
    public async Task RecordObservation_CreatesRenderedSeasonDestinationsInOneBatch()
    {
        await using var fixture = await EpisodeIdentityFixture.CreateAsync(episodeCount: 12);
        var observation = fixture.MakeSeriesObservation(
            new ObservedProviderEpisodeDto
            {
                ProviderEpisodeId = "EPISODE1",
                ProviderUrl = "https://www.crunchyroll.com/watch/EPISODE1/one",
                EpisodeNumber = 1,
                EpisodeTitle = "One"
            },
            new ObservedProviderEpisodeDto
            {
                ProviderEpisodeId = "EPISODE2",
                ProviderUrl = "https://www.crunchyroll.com/watch/EPISODE2/two",
                EpisodeNumber = 2,
                EpisodeTitle = "Two"
            });

        await fixture.Service.RecordObservationAsync(observation, CancellationToken.None);

        Assert.Equal(2, await fixture.Db.MediaProviderEpisodes.CountAsync());
        Assert.Empty(fixture.Db.MediaEpisodes);
        var rows = await fixture.Db.MediaProviderEpisodes.OrderBy(row => row.ProviderEpisodeNumber).ToListAsync();
        Assert.Equal("One", rows[0].EpisodeTitle);
    }

    [Fact]
    public async Task RecordCatalogObservation_KeepsRawEpisodesPastCanonicalCount()
    {
        await using var fixture = await EpisodeIdentityFixture.CreateAsync(episodeCount: 12);
        var payload = new SubmitMediaCatalogObservationRequest
        {
            Provider = MediaObservationSiteIdentifiers.Crunchyroll,
            SeriesUrl = "https://www.crunchyroll.com/series/SERIES1/destination-test",
            ProviderSeriesId = "SERIES1",
            SeriesTitle = "Destination Test",
            ProviderSeasonId = "SEASON1",
            SeasonTitle = "Season 1",
            SeasonNumber = 1,
            Episodes = Enumerable.Range(1, 15)
                .Select(number => new MediaCatalogEpisodeObservationDto
                {
                    ProviderEpisodeId = $"EPISODE{number}",
                    ProviderUrl = $"https://www.crunchyroll.com/watch/EPISODE{number}/episode-{number}",
                    EpisodeNumber = number
                })
                .ToList()
        };
        var now = DateTimeOffset.UtcNow;
        var observation = new MediaObservation
        {
            Id = Guid.NewGuid(),
            UserId = fixture.UserId,
            SiteIdentifier = MediaObservationSiteIdentifiers.Crunchyroll,
            SiteMediaId = "catalog:trusted-bound",
            IsCatalogObservation = true,
            SeriesTitle = payload.SeriesTitle,
            ProviderSeriesId = payload.ProviderSeriesId,
            ProviderSeasonId = payload.ProviderSeasonId,
            SeasonNumber = payload.SeasonNumber,
            Episodes = payload.Episodes.Select(episode => new MediaObservationEpisode
            {
                Id = Guid.NewGuid(),
                ProviderEpisodeId = episode.ProviderEpisodeId,
                ProviderUrl = episode.ProviderUrl,
                EpisodeNumber = episode.EpisodeNumber
            }).ToList(),
            ObservedUrl = payload.SeriesUrl,
            ObservedTitle = payload.SeriesTitle,
            ObservedAt = now,
            MatchStatus = MediaObservationStatuses.Matched,
            MediaTitleId = fixture.TitleId,
            CreatedAt = now,
            UpdatedAt = now
        };

        var recorded = await fixture.Service.RecordCatalogObservationAsync(
            observation,
            CancellationToken.None);

        Assert.Equal(15, recorded);
        Assert.Equal(15, await fixture.Db.MediaProviderEpisodes.CountAsync());
        Assert.Contains(fixture.Db.MediaProviderEpisodes, episode => episode.ProviderEpisodeNumber == 15);
    }

    [Fact]
    public async Task RecordObservation_KeepsRawNumberAndRejectsUnsafeRenderedUrls()
    {
        await using var fixture = await EpisodeIdentityFixture.CreateAsync(episodeCount: 24);
        var observation = fixture.MakeSeriesObservation(
            new ObservedProviderEpisodeDto
            {
                ProviderEpisodeId = "EPISODE1",
                ProviderUrl = "https://www.crunchyroll.com/watch/EPISODE1/one",
                EpisodeNumber = 1
            },
            new ObservedProviderEpisodeDto
            {
                ProviderEpisodeId = "EVIL2",
                ProviderUrl = "https://crunchyrollx.com/watch/EVIL2/two",
                EpisodeNumber = 2
            });
        observation.EpisodeOffset = 12;

        await fixture.Service.RecordObservationAsync(observation, CancellationToken.None);

        var episode = await fixture.Db.MediaProviderEpisodes.SingleAsync();
        Assert.Equal("EPISODE1", episode.ProviderEpisodeId);
        Assert.Equal(1, episode.ProviderEpisodeNumber);
    }

    [Theory]
    [InlineData("http://www.crunchyroll.com/watch/SAFEID")]
    [InlineData("https://crunchyrollx.com/watch/SAFEID")]
    [InlineData("https://crunchyroll.com.evil.example/watch/SAFEID")]
    [InlineData("https://crunchyroll.com@evil.example/watch/SAFEID")]
    [InlineData("https://www.crunchyroll.com:444/watch/SAFEID")]
    [InlineData("https://www.crunchyroll.com/watch/OTHERID")]
    public void DestinationPolicy_RejectsUnsafeOrMismatchedUrls(string url)
    {
        Assert.False(MediaDestinationUrlPolicy.TryNormalizePath("crunchyroll", url, "SAFEID", out _));
    }

    [Theory]
    [InlineData("https://www.crunchyroll.com/watch/SAFEID/episode-1")]
    [InlineData("https://www.crunchyroll.com/da/watch/SAFEID/episode-1")]
    [InlineData("https://crunchyroll.com/watch/SAFEID")]
    public void DestinationPolicy_NormalizesSupportedUrls(string url)
    {
        Assert.True(MediaDestinationUrlPolicy.TryNormalizePath("crunchyroll", url, "safeid", out var path));
        Assert.Equal("/watch/SAFEID", path);
    }

    [Theory]
    [InlineData("crunchyroll", "SERIES1", "https://www.crunchyroll.com/series/SERIES1")]
    [InlineData("crunchyroll", "series2", "https://www.crunchyroll.com/series/SERIES2")]
    [InlineData("crunchyroll", "../watch/EVIL", null)]
    [InlineData("other", "SERIES1", null)]
    public void DestinationPolicy_BuildsOnlySafeSeriesUrls(string provider, string seriesId, string? expected)
    {
        Assert.Equal(expected, MediaDestinationUrlPolicy.BuildSeriesUrl(provider, seriesId));
    }

    private sealed class EpisodeIdentityFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private EpisodeIdentityFixture(
            SqliteConnection connection,
            ApplicationDbContext db,
            MediaEpisodeIdentityService service,
            int userId,
            Guid titleId,
            Guid libraryEntryId)
        {
            _connection = connection;
            Db = db;
            Service = service;
            UserId = userId;
            TitleId = titleId;
            LibraryEntryId = libraryEntryId;
        }

        public ApplicationDbContext Db { get; }
        public MediaEpisodeIdentityService Service { get; }
        public int UserId { get; }
        public Guid TitleId { get; }
        public Guid LibraryEntryId { get; }

        public static async Task<EpisodeIdentityFixture> CreateAsync(
            int? episodeCount = 24,
            int? progressEpisodes = 0)
        {
            const int userId = 901;
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseSqlite(connection)
                    .Options);
            await db.Database.EnsureCreatedAsync();

            var now = DateTimeOffset.UtcNow;
            var title = new MediaTitle
            {
                Id = Guid.NewGuid(),
                CanonicalTitle = "Destination Test",
                MediaKind = MediaKinds.Anime,
                EpisodeCount = episodeCount,
                SupportsEpisodeProgress = true,
                PrimaryProgressDimension = MediaProgressDimensions.Episode,
                ReleaseStatusDimension = MediaProgressDimensions.Episode,
                CreatedAt = now,
                UpdatedAt = now
            };
            var entry = new MediaLibraryEntry
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                MediaTitleId = title.Id,
                Status = MediaLibraryStatuses.Current,
                ProgressEpisodes = progressEpisodes,
                CreatedAt = now,
                UpdatedAt = now
            };

            db.Users.Add(TestUserFactory.Create(userId, "episode-identities@example.com"));
            db.MediaTitles.Add(title);
            db.MediaLibraryEntries.Add(entry);
            await db.SaveChangesAsync();

            var mappings = new MediaProviderSeasonMappingService(db, NullLogger<MediaProviderSeasonMappingService>.Instance);
            await mappings.EstablishAsync("crunchyroll", "SERIES1", null, 1, title.Id, 0,
                MediaProviderSeasonMappingSources.Manual, 1m, CancellationToken.None);

            return new EpisodeIdentityFixture(
                connection,
                db,
                new MediaEpisodeIdentityService(
                    db,
                    new MediaProviderSeasonMappingService(
                        db,
                        NullLogger<MediaProviderSeasonMappingService>.Instance),
                    NullLogger<MediaEpisodeIdentityService>.Instance),
                userId,
                title.Id,
                entry.Id);
        }

        public MediaObservation MakeObservation(
            int resolvedProgress,
            string providerEpisodeId,
            string observedUrl,
            string? nextEpisodeId = null,
            string? nextEpisodeUrl = null)
        {
            var payload = new SubmitMediaObservationRequest
            {
                SiteIdentifier = MediaObservationSiteIdentifiers.Crunchyroll,
                SiteMediaId = providerEpisodeId,
                ObservedUrl = observedUrl,
                ObservedTitle = $"Destination Test - Episode {resolvedProgress}",
                EpisodeTitle = $"Episode {resolvedProgress}",
                EpisodeNumber = resolvedProgress,
                SeasonNumber = 1,
                ProviderSeriesId = "SERIES1",
                NextEpisodeProviderId = nextEpisodeId,
                NextEpisodeUrl = nextEpisodeUrl,
                NextEpisodeNumber = nextEpisodeId is null ? null : resolvedProgress + 1
            };
            var now = DateTimeOffset.UtcNow;
            return new MediaObservation
            {
                Id = Guid.NewGuid(),
                UserId = UserId,
                SiteIdentifier = MediaObservationSiteIdentifiers.Crunchyroll,
                SiteMediaId = providerEpisodeId,
                ObservedUrl = observedUrl,
                ObservedTitle = payload.ObservedTitle,
                ProgressHint = resolvedProgress.ToString(),
                ObservedAt = now,
                SeriesTitle = "Destination Test",
                EpisodeTitle = payload.EpisodeTitle,
                EpisodeNumber = payload.EpisodeNumber,
                SeasonNumber = payload.SeasonNumber,
                ProviderSeriesId = payload.ProviderSeriesId,
                NextEpisodeProviderId = payload.NextEpisodeProviderId,
                NextEpisodeUrl = payload.NextEpisodeUrl,
                NextEpisodeNumber = payload.NextEpisodeNumber,
                MatchStatus = MediaObservationStatuses.Matched,
                MediaTitleId = TitleId,
                ResolvedProgress = resolvedProgress,
                CreatedAt = now,
                UpdatedAt = now
            };
        }

        public MediaObservation MakeSeriesObservation(params ObservedProviderEpisodeDto[] episodes)
        {
            var payload = new SubmitMediaObservationRequest
            {
                SiteIdentifier = MediaObservationSiteIdentifiers.Crunchyroll,
                SiteMediaId = "SERIES1:season-4",
                ObservedUrl = "https://www.crunchyroll.com/series/SERIES1/destination-test",
                ObservedTitle = "Destination Test Season 4",
                SeriesTitle = "Destination Test Season 4",
                SeasonTitle = "Season 4",
                SeasonNumber = 4,
                ProviderSeriesId = "SERIES1",
                ObservedEpisodes = [.. episodes]
            };
            var now = DateTimeOffset.UtcNow;
            return new MediaObservation
            {
                Id = Guid.NewGuid(),
                UserId = UserId,
                SiteIdentifier = MediaObservationSiteIdentifiers.Crunchyroll,
                SiteMediaId = payload.SiteMediaId,
                ObservedUrl = payload.ObservedUrl,
                ObservedTitle = payload.ObservedTitle,
                ObservedAt = now,
                SeriesTitle = payload.SeriesTitle,
                SeasonTitle = payload.SeasonTitle,
                SeasonNumber = payload.SeasonNumber,
                ProviderSeriesId = payload.ProviderSeriesId,
                IsCatalogObservation = false,
                Episodes = payload.ObservedEpisodes.Select(episode => new MediaObservationEpisode
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
                MediaTitleId = TitleId,
                CreatedAt = now,
                UpdatedAt = now
            };
        }

        public async Task SetPreferredReleaseTrackAsync(string preferredTrack)
        {
            Db.UserSettings.Add(new UserSettings
            {
                UserId = UserId,
                PreferredMediaReleaseTrack = preferredTrack,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await Db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
