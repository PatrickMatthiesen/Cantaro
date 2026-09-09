using System.Security.Claims;
using Cantaro.Api.Controllers;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public class MediaObservationsApiTests
{
    [Fact]
    public async Task Submit_MatchedWithoutLibraryProgress_RetainsActionableObservation()
    {
        await using var fixture = await MediaObservationFixture.CreateAsync();
        var title = new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = "Tracked Later",
            MediaKind = MediaKinds.Anime,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            SupportsEpisodeProgress = true,
            EpisodeCount = 12,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        fixture.Db.MediaTitles.Add(title);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Controller.Submit(new SubmitMediaObservationRequest
        {
            SiteIdentifier = MediaObservationSiteIdentifiers.Crunchyroll,
            ObservedUrl = "https://www.crunchyroll.com/watch/LATER/episode-3",
            SiteMediaId = "LATER",
            ObservedTitle = "Tracked Later - Episode 3",
            SeriesTitle = title.CanonicalTitle,
            EpisodeNumber = 3,
            ProgressHint = "3"
        }, CancellationToken.None);

        var response = Assert.IsType<SubmitMediaObservationResponse>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(MediaObservationStatuses.Matched, response.MatchStatus);
        Assert.Single(fixture.Db.MediaObservations);

        var summary = Assert.IsType<MediaObservationSummaryDto>(
            Assert.IsType<OkObjectResult>(
                (await fixture.Controller.GetSummary(CancellationToken.None)).Result).Value);
        Assert.Equal(1, summary.TotalUnresolved);
    }

    [Fact]
    public async Task Submit_RejectsUnboundedSeriesPageBatches()
    {
        await using var fixture = await MediaObservationFixture.CreateAsync();
        var episodes = Enumerable.Range(1, MediaCatalogObservationLimits.MaximumEpisodesPerObservation + 1)
            .Select(number => new ObservedProviderEpisodeDto
            {
                ProviderEpisodeId = $"EPISODE{number}",
                ProviderUrl = $"https://www.crunchyroll.com/watch/EPISODE{number}",
                EpisodeNumber = number
            })
            .ToList();

        var result = await fixture.Controller.Submit(new SubmitMediaObservationRequest
        {
            SiteIdentifier = MediaObservationSiteIdentifiers.Crunchyroll,
            ObservedUrl = "https://www.crunchyroll.com/series/SERIES1/show",
            SiteMediaId = "SERIES1:season-1",
            ObservedTitle = "Show Season 1",
            ObservedEpisodes = episodes
        }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(fixture.Db.MediaObservations);
    }

    [Fact]
    public async Task Submit_PersistsStructuredPlaybackMetadata()
    {
        await using var fixture = await MediaObservationFixture.CreateAsync();

        var result = await fixture.Controller.Submit(new SubmitMediaObservationRequest
        {
            SiteIdentifier = MediaObservationSiteIdentifiers.Crunchyroll,
            ObservedUrl = "https://www.crunchyroll.com/watch/GYVNM7N6Y/episode-7?token=SECRET#fragment",
            SiteMediaId = "GYVNM7N6Y",
            ObservedTitle = "Frieren - Episode 7 - Like a Fairy Tale",
            SeriesTitle = "Frieren",
            EpisodeTitle = "Like a Fairy Tale",
            EpisodeNumber = 7,
            SeasonTitle = "Season 1",
            SeasonNumber = 1,
            ProviderSeriesId = "GYEXQKJG6",
            NextEpisodeProviderId = "G31UXQ9K2",
            NextEpisodeUrl = "https://www.crunchyroll.com/watch/G31UXQ9K2/episode-8?token=SECRET#fragment",
            NextEpisodeNumber = 8,
            ObservedEpisodes =
            [
                new ObservedProviderEpisodeDto
                {
                    ProviderEpisodeId = "GYVNM7N6Y",
                    ProviderUrl = "https://www.crunchyroll.com/watch/GYVNM7N6Y/episode-7?token=SECRET#fragment",
                    EpisodeNumber = 7,
                    EpisodeTitle = "Like a Fairy Tale"
                }
            ],
            WatchProgressPercent = 85.5m,
            DurationSeconds = 1440m,
            PositionSeconds = 1231.2m,
            ObservedAt = DateTimeOffset.Parse("2026-04-28T10:30:00Z"),
            ExtensionVersion = "0.1.0"
        }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);

        var observation = await fixture.Db.MediaObservations
            .Include(item => item.Episodes)
            .SingleAsync();
        Assert.Equal("7", observation.ProgressHint);
        Assert.Equal(
            "https://www.crunchyroll.com/watch/GYVNM7N6Y/episode-7",
            observation.ObservedUrl);
        Assert.Equal("Frieren", observation.SeriesTitle);
        Assert.Equal("Like a Fairy Tale", observation.EpisodeTitle);
        Assert.Equal(7, observation.EpisodeNumber);
        Assert.Equal("Season 1", observation.SeasonTitle);
        Assert.Equal(1, observation.SeasonNumber);
        Assert.Equal("GYEXQKJG6", observation.ProviderSeriesId);
        Assert.Equal("G31UXQ9K2", observation.NextEpisodeProviderId);
        Assert.Equal(8, observation.NextEpisodeNumber);
        var observedEpisode = Assert.Single(observation.Episodes);
        Assert.Equal("GYVNM7N6Y", observedEpisode.ProviderEpisodeId);
        Assert.Equal(7, observedEpisode.EpisodeNumber);
        Assert.Equal(
            "https://www.crunchyroll.com/watch/GYVNM7N6Y/episode-7",
            observedEpisode.ProviderUrl);
        Assert.Equal(
            "https://www.crunchyroll.com/watch/G31UXQ9K2/episode-8",
            observation.NextEpisodeUrl);
        var observationEntity = fixture.Db.Model.FindEntityType(typeof(MediaObservation));
        Assert.Null(observationEntity?.FindProperty("RawPayload"));
        Assert.Null(observationEntity?.FindProperty("WatchProgressPercent"));
        Assert.Null(observationEntity?.FindProperty("DurationSeconds"));
        Assert.Null(observationEntity?.FindProperty("PositionSeconds"));
    }

    [Fact]
    public async Task Submit_CumulativeSeasonWatch_AdvancesSeasonLocalProgress()
    {
        await using var fixture = await MediaObservationFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var seasonOne = CreateWistoriaTitle("Wistoria: Wand and Sword", 2024, now);
        var seasonTwo = CreateWistoriaTitle("Wistoria: Wand and Sword Season 2", 2026, now);
        fixture.Db.MediaTitles.AddRange(seasonOne, seasonTwo);
        var relationSnapshotId = Guid.NewGuid();
        fixture.Db.MediaProviderLinks.AddRange(
            CreateWistoriaProviderLink(seasonOne, "wistoria-1", relationSnapshotId, now),
            CreateWistoriaProviderLink(seasonTwo, "wistoria-2", relationSnapshotId, now));
        fixture.Db.MediaLibraryEntries.AddRange(
            CreateWistoriaEntry(fixture.UserId, seasonOne, now),
            CreateWistoriaEntry(fixture.UserId, seasonTwo, now));
        fixture.Db.MediaTitleRelations.Add(new MediaTitleRelation
        {
            Id = Guid.NewGuid(),
            MediaTitleId = seasonOne.Id,
            RelatedMediaTitleId = seasonTwo.Id,
            RelationType = MediaRelationTypes.Sequel,
            SourceProvider = "anilist",
            FirstSeenAt = now,
            LastVerifiedAt = now
        });
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Controller.Submit(new SubmitMediaObservationRequest
        {
            SiteIdentifier = MediaObservationSiteIdentifiers.Crunchyroll,
            ObservedUrl = "https://www.crunchyroll.com/watch/GE00340376ENUS/hoping-blooming-thundering",
            SiteMediaId = "GE00340376ENUS",
            ObservedTitle = "E22 - Hoping, Blooming, Thundering",
            SeriesTitle = "Wistoria: Wand and Sword",
            EpisodeTitle = "E22 - Hoping, Blooming, Thundering",
            EpisodeNumber = 22,
            SeasonTitle = "Season 2",
            SeasonNumber = 2,
            ProviderSeriesId = "GW4HM7WK9",
            WatchProgressPercent = 85.1m,
            ObservedAt = now,
            ExtensionVersion = "0.1.0"
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<SubmitMediaObservationResponse>(ok.Value);
        Assert.Equal(MediaObservationStatuses.Matched, response.MatchStatus);
        Assert.Empty(fixture.Db.MediaObservations);
        var seasonTwoEntry = await fixture.Db.MediaLibraryEntries
            .SingleAsync(entry => entry.MediaTitleId == seasonTwo.Id);
        Assert.Equal(10, seasonTwoEntry.ProgressEpisodes);
        var identity = await fixture.Db.MediaEpisodeProviderIdentities
            .Include(item => item.Content)
                .ThenInclude(content => content!.MediaEpisode)
            .SingleAsync(item => item.ProviderEpisodeId == "GE00340376ENUS");
        Assert.Equal(10, identity.MediaEpisode!.EpisodeNumber);
        Assert.Equal(22, identity.ProviderEpisodeNumber);
    }

    [Fact]
    public async Task Submit_SeriesPageObservedEpisodes_UsesBatchRangeForCumulativeSeason()
    {
        await using var fixture = await MediaObservationFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var seasonOne = CreateWistoriaTitle("Wistoria: Wand and Sword", 2024, now);
        var seasonTwo = CreateWistoriaTitle("Wistoria: Wand and Sword Season 2", 2026, now);
        fixture.Db.MediaTitles.AddRange(seasonOne, seasonTwo);
        var relationSnapshotId = Guid.NewGuid();
        fixture.Db.MediaProviderLinks.AddRange(
            CreateWistoriaProviderLink(seasonOne, "wistoria-1", relationSnapshotId, now),
            CreateWistoriaProviderLink(seasonTwo, "wistoria-2", relationSnapshotId, now));
        fixture.Db.MediaLibraryEntries.AddRange(
            CreateWistoriaEntry(fixture.UserId, seasonOne, now),
            CreateWistoriaEntry(fixture.UserId, seasonTwo, now));
        fixture.Db.MediaTitleRelations.Add(new MediaTitleRelation
        {
            Id = Guid.NewGuid(),
            MediaTitleId = seasonOne.Id,
            RelatedMediaTitleId = seasonTwo.Id,
            RelationType = MediaRelationTypes.Sequel,
            SourceProvider = "anilist",
            FirstSeenAt = now,
            LastVerifiedAt = now
        });
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Controller.Submit(new SubmitMediaObservationRequest
        {
            SiteIdentifier = MediaObservationSiteIdentifiers.Crunchyroll,
            ObservedUrl = "https://www.crunchyroll.com/series/GW4HM7WK9/wistoria-wand-and-sword",
            ObservedTitle = "Wistoria: Wand and Sword",
            SeriesTitle = "Wistoria: Wand and Sword",
            SeasonTitle = "Season 2",
            SeasonNumber = 2,
            ProviderSeriesId = "GW4HM7WK9",
            ProviderSeasonId = "WISTORIA2",
            ObservedEpisodes = Enumerable.Range(13, 12).Select(number => new ObservedProviderEpisodeDto
            {
                ProviderEpisodeId = $"WISTORIA{number}",
                ProviderUrl = $"https://www.crunchyroll.com/watch/WISTORIA{number}/episode-{number}",
                EpisodeNumber = number,
                EpisodeTitle = $"Episode {number}"
            }).ToList(),
            ObservedAt = now,
            ExtensionVersion = "0.2.0"
        }, CancellationToken.None);

        var response = Assert.IsType<SubmitMediaObservationResponse>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(MediaObservationStatuses.Matched, response.MatchStatus);
        Assert.Equal(seasonTwo.Id.ToString(), response.MatchedMediaTitleId);
        Assert.Empty(fixture.Db.MediaObservations);
        Assert.Equal(12, await fixture.Db.MediaEpisodeProviderIdentities.CountAsync());
        Assert.Contains(
            fixture.Db.MediaEpisodeProviderIdentities
                .Include(identity => identity.Content)
                    .ThenInclude(content => content!.MediaEpisode),
            identity => identity.ProviderEpisodeNumber == 22
                && identity.MediaEpisode!.EpisodeNumber == 10);
    }

    [Fact]
    public async Task Retry_ClearsDerivedProgressWhenNoNewMatchIsAvailable()
    {
        await using var fixture = await MediaObservationFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var observation = new MediaObservation
        {
            Id = Guid.NewGuid(),
            UserId = fixture.UserId,
            SiteIdentifier = MediaObservationSiteIdentifiers.Crunchyroll,
            ObservedUrl = "https://www.crunchyroll.com/watch/UNKNOWN/unknown",
            ObservedTitle = "Unknown title",
            ProgressHint = "22",
            MatchStatus = MediaObservationStatuses.Matched,
            EpisodeOffset = -12,
            ResolvedProgress = 10,
            CreatedAt = now,
            UpdatedAt = now
        };
        fixture.Db.MediaObservations.Add(observation);
        await fixture.Db.SaveChangesAsync();

        await fixture.Controller.Retry(observation.Id, CancellationToken.None);

        Assert.Null(observation.EpisodeOffset);
        Assert.Null(observation.ResolvedProgress);
        Assert.Null(observation.MediaTitleId);
        Assert.Equal(MediaObservationStatuses.NoMatch, observation.MatchStatus);
    }

    [Fact]
    public async Task Retry_RemapsUntrustedCatalogIdentitiesToTheRematchedSeason()
    {
        await using var fixture = await MediaObservationFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var seasonOne = CreateWistoriaTitle("Wistoria: Wand and Sword", 2024, now);
        var seasonTwo = CreateWistoriaTitle("Wistoria: Wand and Sword Season 2", 2026, now);
        var relationSnapshotId = Guid.NewGuid();
        var catalogRequest = new SubmitMediaCatalogObservationRequest
        {
            Provider = MediaObservationSiteIdentifiers.Crunchyroll,
            SeriesUrl = "https://www.crunchyroll.com/series/GW4HM7WK9/wistoria-wand-and-sword",
            ProviderSeriesId = "GW4HM7WK9",
            SeriesTitle = seasonOne.CanonicalTitle,
            ProviderSeasonId = "WISTORIA2",
            SeasonTitle = "Season 2",
            SeasonNumber = 2,
            Episodes = Enumerable.Range(1, 2)
                .Select(number => new MediaCatalogEpisodeObservationDto
                {
                    ProviderEpisodeId = $"WISTORIA{number}",
                    ProviderUrl = $"https://www.crunchyroll.com/watch/WISTORIA{number}/episode-{number}",
                    EpisodeNumber = number,
                    EpisodeTitle = $"Episode {number}"
                })
                .ToList()
        };
        var observation = new MediaObservation
        {
            Id = Guid.NewGuid(),
            UserId = fixture.UserId,
            SiteIdentifier = MediaObservationSiteIdentifiers.Crunchyroll,
            SiteMediaId = "catalog:wistoria-season-2",
            ObservedUrl = catalogRequest.SeriesUrl,
            ObservedTitle = "Wistoria: Wand and Sword — Season 2",
            SeriesTitle = catalogRequest.SeriesTitle,
            SeasonTitle = catalogRequest.SeasonTitle,
            ProviderSeriesId = catalogRequest.ProviderSeriesId,
            ProviderSeasonId = catalogRequest.ProviderSeasonId,
            SeasonNumber = catalogRequest.SeasonNumber,
            IsCatalogObservation = true,
            Episodes = catalogRequest.Episodes.Select(episode => new MediaObservationEpisode
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
            MediaTitleId = seasonOne.Id,
            CreatedAt = now,
            UpdatedAt = now
        };
        var staleCandidate = new MediaObservationCandidate
        {
            Id = Guid.NewGuid(),
            MediaObservationId = observation.Id,
            CandidateSource = MediaObservationCandidateSources.CatalogTitleSearch,
            MediaTitleId = seasonOne.Id,
            Title = seasonOne.CanonicalTitle,
            MediaKind = seasonOne.MediaKind,
            Score = 1m,
            IsAccepted = true,
            CreatedAt = now
        };
        observation.AcceptedCandidateId = staleCandidate.Id;

        var wrongSeasonEpisodes = Enumerable.Range(1, 2)
            .Select(number => new MediaEpisode
            {
                Id = Guid.NewGuid(),
                MediaTitleId = seasonOne.Id,
                EpisodeNumber = number,
                CreatedAt = now,
                UpdatedAt = now
            })
            .ToList();
        var wrongSeasonIdentities = wrongSeasonEpisodes
            .Select((episode, index) => new MediaEpisodeProviderIdentity
            {
                Id = Guid.NewGuid(),
                MediaEpisodeId = episode.Id,
                Provider = MediaObservationSiteIdentifiers.Crunchyroll,
                ProviderSeriesId = catalogRequest.ProviderSeriesId,
                ProviderSeasonId = catalogRequest.ProviderSeasonId,
                ProviderEpisodeId = $"WISTORIA{index + 1}",
                ProviderSeasonNumber = catalogRequest.SeasonNumber,
                ProviderEpisodeNumber = index + 1,
                ProviderUrlPath = $"/watch/WISTORIA{index + 1}/episode-{index + 1}",
                SeenCount = 1,
                FirstSeenAt = now,
                LastSeenAt = now,
                IsTrusted = false
            })
            .ToList();

        fixture.Db.MediaTitles.AddRange(seasonOne, seasonTwo);
        fixture.Db.MediaProviderLinks.AddRange(
            CreateWistoriaProviderLink(seasonOne, "wistoria-1", relationSnapshotId, now),
            CreateWistoriaProviderLink(seasonTwo, "wistoria-2", relationSnapshotId, now));
        fixture.Db.MediaLibraryEntries.AddRange(
            CreateWistoriaEntry(fixture.UserId, seasonOne, now),
            CreateWistoriaEntry(fixture.UserId, seasonTwo, now));
        fixture.Db.MediaTitleRelations.Add(new MediaTitleRelation
        {
            Id = Guid.NewGuid(),
            MediaTitleId = seasonOne.Id,
            RelatedMediaTitleId = seasonTwo.Id,
            RelationType = MediaRelationTypes.Sequel,
            SourceProvider = "anilist",
            FirstSeenAt = now,
            LastVerifiedAt = now
        });
        fixture.Db.MediaEpisodes.AddRange(wrongSeasonEpisodes);
        fixture.Db.MediaEpisodeProviderIdentities.AddRange(wrongSeasonIdentities);
        fixture.Db.MediaObservations.Add(observation);
        fixture.Db.MediaObservationCandidates.Add(staleCandidate);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Controller.Retry(observation.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<MediaObservationDto>(ok.Value);
        Assert.Equal(MediaObservationStatuses.Matched, response.MatchStatus);
        Assert.Equal(seasonTwo.Id.ToString(), response.MediaTitleId);

        var persistedIdentities = await fixture.Db.MediaEpisodeProviderIdentities
            .Include(identity => identity.Content)
                .ThenInclude(content => content!.MediaEpisode)
            .ToListAsync();
        Assert.Equal(2, persistedIdentities.Count);
        Assert.All(persistedIdentities, identity =>
            Assert.Equal(seasonTwo.Id, identity.MediaEpisode!.MediaTitleId));
        Assert.All(persistedIdentities, identity => Assert.False(identity.IsTrusted));
        Assert.Empty(await fixture.Db.MediaEpisodeProviderIdentities
            .Where(identity => identity.Content!.MediaEpisode!.MediaTitleId == seasonOne.Id)
            .ToListAsync());
    }

    private static MediaTitle CreateWistoriaTitle(string title, int startYear, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        CanonicalTitle = title,
        MediaKind = MediaKinds.Anime,
        Format = MediaFormats.Tv,
        EpisodeCount = 12,
        StartYear = startYear,
        SupportsEpisodeProgress = true,
        PrimaryProgressDimension = MediaProgressDimensions.Episode,
        ReleaseStatusDimension = MediaProgressDimensions.Episode,
        CreatedAt = now,
        UpdatedAt = now
    };

    private static MediaProviderLink CreateWistoriaProviderLink(
        MediaTitle title,
        string externalId,
        Guid relationSnapshotId,
        DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        MediaTitleId = title.Id,
        Provider = MediaObservationSiteIdentifiers.AniList,
        ExternalId = externalId,
        LinkSource = MediaMappingSources.Imported,
        LastVerifiedAt = now,
        RelationsLastVerifiedAt = now,
        RelationsSnapshotId = relationSnapshotId,
        CreatedAt = now,
        UpdatedAt = now
    };

    private static MediaLibraryEntry CreateWistoriaEntry(
        int userId,
        MediaTitle title,
        DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        MediaTitleId = title.Id,
        Status = MediaLibraryStatuses.Current,
        ProgressEpisodes = 0,
        CreatedAt = now,
        UpdatedAt = now
    };

    [Fact]
    public async Task Submit_WhenNoMatch_SearchesProviderChoicesAndLogsThem()
    {
        await using var fixture = await MediaObservationFixture.CreateAsync(new SingleProviderRegistry(new SearchOnlyMediaProvider()));

        var result = await fixture.Controller.Submit(new SubmitMediaObservationRequest
        {
            SiteIdentifier = MediaObservationSiteIdentifiers.Crunchyroll,
            ObservedUrl = "https://www.crunchyroll.com/watch/NO_MATCH/episode-4",
            SiteMediaId = "NO_MATCH",
            ObservedTitle = "Frieren - Episode 4",
            SeriesTitle = "Frieren",
            EpisodeNumber = 4,
            ObservedAt = DateTimeOffset.Parse("2026-04-28T10:30:00Z"),
            ExtensionVersion = "0.1.0"
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<SubmitMediaObservationResponse>(ok.Value);

        Assert.True(response.RequiresResolution);
        Assert.Single(response.ProviderChoices);
        Assert.Equal("anilist", response.ProviderChoices[0].ProviderId);
        Assert.Equal("154587", response.ProviderChoices[0].ProviderMediaId);

        var observation = await fixture.Db.MediaObservations.SingleAsync();
        Assert.NotNull(observation.ProviderChoicesPayload);
        Assert.Contains("154587", observation.ProviderChoicesPayload);
    }

    [Fact]
    public async Task Submit_RefreshesKnownAniListSynonymsAndRetriesMatching()
    {
        var provider = new SearchOnlyMediaProvider(searchResults:
        [
            new MediaProviderSearchResult
            {
                ProviderId = "anilist",
                ProviderMediaId = "209974",
                Title = "Honzuki no Gekokujou 4th Season",
                Synonyms = ["Ascendance of a Bookworm Season 4"],
                MediaKind = MediaKinds.Anime,
                EpisodeCount = 12,
                PrimaryProgressDimension = MediaProgressDimensions.Episode,
                ReleaseStatusDimension = MediaProgressDimensions.Episode
            }
        ]);
        await using var fixture = await MediaObservationFixture.CreateAsync(new SingleProviderRegistry(provider));
        var now = DateTimeOffset.UtcNow;
        var title = new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = "Honzuki no Gekokujou 4th Season",
            MediaKind = MediaKinds.Anime,
            EpisodeCount = 12,
            SupportsEpisodeProgress = true,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            CreatedAt = now,
            UpdatedAt = now
        };
        fixture.Db.AddRange(
            title,
            new MediaProviderLink
            {
                Id = Guid.NewGuid(),
                MediaTitleId = title.Id,
                Provider = "anilist",
                ExternalId = "209974",
                LinkSource = MediaMappingSources.Imported,
                CreatedAt = now,
                UpdatedAt = now
            });
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Controller.Submit(new SubmitMediaObservationRequest
        {
            SiteIdentifier = MediaObservationSiteIdentifiers.Crunchyroll,
            ObservedUrl = "https://www.crunchyroll.com/series/BOOKWORM/ascendance-of-a-bookworm",
            ObservedTitle = "Ascendance of a Bookworm — Season 4",
            SeriesTitle = "Ascendance of a Bookworm Season 4",
            ObservedAt = now
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<SubmitMediaObservationResponse>(ok.Value);
        Assert.False(response.RequiresResolution);
        Assert.Equal(MediaObservationStatuses.Matched, response.MatchStatus);
        Assert.Equal(title.Id.ToString(), response.MatchedMediaTitleId);
        Assert.Equal(["Ascendance of a Bookworm Season 4"], (await fixture.Db.MediaTitles.SingleAsync()).Synonyms);
    }

    [Fact]
    public async Task Submit_WhenNoMatchAndProviderNotConnected_StoresObservationWithoutChoices()
    {
        await using var fixture = await MediaObservationFixture.CreateAsync(new SingleProviderRegistry(new SearchOnlyMediaProvider(connected: false)));

        var result = await fixture.Controller.Submit(new SubmitMediaObservationRequest
        {
            SiteIdentifier = MediaObservationSiteIdentifiers.Crunchyroll,
            ObservedUrl = "https://www.crunchyroll.com/watch/NO_PROVIDER/episode-4",
            SiteMediaId = "NO_PROVIDER",
            ObservedTitle = "Frieren - Episode 4",
            SeriesTitle = "Frieren",
            EpisodeNumber = 4,
            ObservedAt = DateTimeOffset.Parse("2026-04-28T10:30:00Z"),
            ExtensionVersion = "0.1.0"
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<SubmitMediaObservationResponse>(ok.Value);

        Assert.True(response.RequiresResolution);
        Assert.Empty(response.ProviderChoices);
        Assert.Equal("anilist_not_connected", response.ProviderChoicesUnavailableReason);

        var observation = await fixture.Db.MediaObservations.SingleAsync();
        Assert.Null(observation.ProviderChoicesPayload);
        Assert.Equal(MediaObservationStatuses.NoMatch, observation.MatchStatus);
    }

    [Fact]
    public async Task Submit_DeduplicatedMatchedObservation_AdvancesLocalProgress()
    {
        await using var fixture = await MediaObservationFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var title = new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = "The Genius Prince's Guide to Raising a Nation Out of Debt",
            MediaKind = MediaKinds.Anime,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            SupportsEpisodeProgress = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        var account = new ConnectedServiceAccount
        {
            Id = 6110,
            UserId = fixture.UserId,
            Service = "anilist",
            ExternalAccountId = "viewer-611",
            CreatedAt = now.UtcDateTime,
            UpdatedAt = now.UtcDateTime
        };
        var entry = new MediaLibraryEntry
        {
            Id = Guid.NewGuid(),
            UserId = fixture.UserId,
            MediaTitleId = title.Id,
            Status = MediaLibraryStatuses.Current,
            ProgressEpisodes = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
        var link = new MediaProviderLink
        {
            Id = Guid.NewGuid(), MediaTitleId = title.Id, Provider = "anilist", ExternalId = "129190",
            LinkSource = MediaMappingSources.Imported, CreatedAt = now, UpdatedAt = now
        };
        var binding = new MediaLibraryProviderBinding
        {
            Id = Guid.NewGuid(), MediaLibraryEntryId = entry.Id, MediaProviderLinkId = link.Id,
            ConnectedServiceAccountId = account.Id, ProviderAccountId = "viewer-611",
            LastRemoteUpdateAt = now.AddMinutes(-10), CreatedAt = now, UpdatedAt = now
        };
        var existingObservation = new MediaObservation
        {
            Id = Guid.NewGuid(),
            UserId = fixture.UserId,
            SiteIdentifier = MediaObservationSiteIdentifiers.Crunchyroll,
            ObservedUrl = "https://www.crunchyroll.com/watch/GWDU8WDNQ/old",
            SiteMediaId = "GWDU8WDNQ",
            ObservedTitle = "The Genius Prince's Guide to Raising a Nation Out of Debt - E1",
            ProgressHint = "1",
            ObservedAt = now.AddMinutes(-10),
            MatchStatus = MediaObservationStatuses.Matched,
            MediaTitleId = title.Id,
            CreatedAt = now.AddMinutes(-10),
            UpdatedAt = now.AddMinutes(-10)
        };

        fixture.Db.MediaTitles.Add(title);
        fixture.Db.ConnectedServiceAccounts.Add(account);
        fixture.Db.MediaProviderLinks.Add(link);
        fixture.Db.MediaLibraryEntries.Add(entry);
        fixture.Db.MediaLibraryProviderBindings.Add(binding);
        fixture.Db.MediaObservations.Add(existingObservation);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Controller.Submit(new SubmitMediaObservationRequest
        {
            SiteIdentifier = MediaObservationSiteIdentifiers.Crunchyroll,
            ObservedUrl = "https://www.crunchyroll.com/watch/GWDU8WDNQ/episode-5",
            SiteMediaId = "GWDU8WDNQ",
            ObservedTitle = "The Genius Prince's Guide to Raising a Nation Out of Debt - E5 - A Diabolical Scheme",
            EpisodeNumber = 5,
            ProviderSeriesId = "GYGENIUS1",
            NextEpisodeProviderId = "NEXTGEN6",
            NextEpisodeUrl = "https://www.crunchyroll.com/watch/NEXTGEN6/episode-6",
            NextEpisodeNumber = 6,
            WatchProgressPercent = 85.01m,
            DurationSeconds = 1446.03m,
            PositionSeconds = 1229.25m,
            ObservedAt = now,
            ExtensionVersion = "0.1.0"
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<SubmitMediaObservationResponse>(ok.Value);
        Assert.True(response.WasDeduplicated);

        var persistedEntry = await fixture.Db.MediaLibraryEntries.SingleAsync(item => item.Id == entry.Id);
        Assert.Equal(5, persistedEntry.ProgressEpisodes);
        Assert.Equal(MediaMutationSources.ObservationAutoProgress, persistedEntry.LastMutationSource);
        Assert.NotNull(persistedEntry.LastLocalEditAt);
        Assert.Equal(1, await fixture.Db.MediaProviderOperations.CountAsync());

        Assert.Null(await fixture.Db.MediaObservations.FindAsync(existingObservation.Id));

        var recordedEpisodes = await fixture.Db.MediaEpisodes
            .Include(item => item.ProviderContents)
                .ThenInclude(content => content.Variants)
            .OrderBy(item => item.EpisodeNumber)
            .ToListAsync();
        Assert.Equal([5, 6], recordedEpisodes.Select(item => item.EpisodeNumber));
        Assert.Equal("GWDU8WDNQ", recordedEpisodes[0].ProviderIdentities.Single().ProviderEpisodeId);
        Assert.Equal("NEXTGEN6", recordedEpisodes[1].ProviderIdentities.Single().ProviderEpisodeId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Submit_DeduplicatedCumulativeEpisode_UsesCompatibleProviderIdentityAndCorrectsSeasonProgress(
        bool initiallyTrusted)
    {
        await using var fixture = await MediaObservationFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var seasonOne = CreateWistoriaTitle("Wistoria: Wand and Sword", 2024, now);
        var seasonTwo = CreateWistoriaTitle("Wistoria: Wand and Sword Season 2", 2026, now);
        var seasonOneEntry = CreateWistoriaEntry(fixture.UserId, seasonOne, now);
        var seasonTwoEntry = CreateWistoriaEntry(fixture.UserId, seasonTwo, now);
        seasonTwoEntry.ProgressEpisodes = 6;
        var canonicalEpisode = new MediaEpisode
        {
            Id = Guid.NewGuid(),
            MediaTitleId = seasonTwo.Id,
            EpisodeNumber = 12,
            Title = "A Story of a Dream with No End",
            CreatedAt = now,
            UpdatedAt = now
        };
        var knownIdentity = new MediaEpisodeProviderIdentity
        {
            Id = Guid.NewGuid(),
            MediaEpisodeId = canonicalEpisode.Id,
            Provider = MediaObservationSiteIdentifiers.Crunchyroll,
            ProviderSeriesId = "GW4HM7WK9",
            ProviderEpisodeId = "GE00340382ENUS",
            ProviderSeasonNumber = 2,
            ProviderEpisodeNumber = 24,
            ProviderUrlPath = "/watch/GE00340382ENUS/a-story-of-a-dream-with-no-end",
            SeenCount = 1,
            FirstSeenAt = now.AddMinutes(-10),
            LastSeenAt = now.AddMinutes(-10),
            IsTrusted = initiallyTrusted,
            HasConflict = true
        };
        var existingObservation = new MediaObservation
        {
            Id = Guid.NewGuid(),
            UserId = fixture.UserId,
            SiteIdentifier = MediaObservationSiteIdentifiers.Crunchyroll,
            ObservedUrl = "https://www.crunchyroll.com/watch/GE00340382ENUS/a-story-of-a-dream-with-no-end",
            SiteMediaId = "GE00340382ENUS",
            ObservedTitle = "E24 - A Story of a Dream with No End",
            ProgressHint = "24",
            ObservedAt = now.AddMinutes(-10),
            MatchStatus = MediaObservationStatuses.Matched,
            MediaTitleId = seasonOne.Id,
            EpisodeOffset = 0,
            ResolvedProgress = 24,
            CreatedAt = now.AddMinutes(-10),
            UpdatedAt = now.AddMinutes(-10)
        };
        var staleCandidate = new MediaObservationCandidate
        {
            Id = Guid.NewGuid(),
            MediaObservationId = existingObservation.Id,
            CandidateSource = MediaObservationCandidateSources.CatalogTitleSearch,
            MediaTitleId = seasonOne.Id,
            Title = seasonOne.CanonicalTitle,
            MediaKind = seasonOne.MediaKind,
            Score = 1m,
            IsAccepted = true,
            CreatedAt = now.AddMinutes(-10)
        };
        existingObservation.AcceptedCandidateId = staleCandidate.Id;

        fixture.Db.MediaTitles.AddRange(seasonOne, seasonTwo);
        fixture.Db.MediaLibraryEntries.AddRange(seasonOneEntry, seasonTwoEntry);
        fixture.Db.MediaEpisodes.Add(canonicalEpisode);
        fixture.Db.MediaEpisodeProviderIdentities.Add(knownIdentity);
        fixture.Db.MediaObservations.Add(existingObservation);
        fixture.Db.MediaObservationCandidates.Add(staleCandidate);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Controller.Submit(new SubmitMediaObservationRequest
        {
            SiteIdentifier = MediaObservationSiteIdentifiers.Crunchyroll,
            ObservedUrl = "https://www.crunchyroll.com/watch/GE00340382ENUS/a-story-of-a-dream-with-no-end",
            SiteMediaId = "GE00340382ENUS",
            ObservedTitle = "E24 - A Story of a Dream with No End",
            SeriesTitle = "Wistoria: Wand and Sword",
            EpisodeTitle = "E24 - A Story of a Dream with No End",
            EpisodeNumber = 24,
            SeasonNumber = 2,
            ProviderSeriesId = "GW4HM7WK9",
            WatchProgressPercent = 86m,
            ObservedAt = now,
            ExtensionVersion = "0.1.0"
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<SubmitMediaObservationResponse>(ok.Value);
        Assert.True(response.WasDeduplicated);

        if (initiallyTrusted)
        {
            Assert.Equal(MediaObservationStatuses.NoMatch, response.MatchStatus);
            Assert.True(response.RequiresResolution);
            Assert.False(response.ProgressUpdated);
            Assert.Equal(6, seasonTwoEntry.ProgressEpisodes);
            Assert.True(knownIdentity.HasConflict);
            var summary = await fixture.Controller.GetSummary(CancellationToken.None);
            Assert.Equal(1, Assert.IsType<MediaObservationSummaryDto>(
                Assert.IsType<OkObjectResult>(summary.Result).Value).TotalUnresolved);

            var reviewObservation = await fixture.Db.MediaObservations
                .Include(item => item.Candidates)
                .SingleAsync(item => item.Id == existingObservation.Id);
            var explicitCandidate = new MediaObservationCandidate
            {
                Id = Guid.NewGuid(),
                MediaObservationId = reviewObservation.Id,
                CandidateSource = MediaObservationCandidateSources.CatalogTitleSearch,
                MediaTitleId = seasonTwo.Id,
                Title = seasonTwo.CanonicalTitle,
                MediaKind = seasonTwo.MediaKind,
                Score = 1m,
                CreatedAt = now
            };
            fixture.Db.MediaObservationCandidates.Add(explicitCandidate);
            await fixture.Db.SaveChangesAsync();

            var resolved = await fixture.Controller.Resolve(
                reviewObservation.Id,
                new ResolveMediaObservationRequest
                {
                    CandidateId = explicitCandidate.Id,
                    EpisodeOffset = -12,
                    AddToLibraryConfirmed = false
                },
                CancellationToken.None);
            var resolvedOk = Assert.IsType<OkObjectResult>(resolved.Result);
            var resolvedDto = Assert.IsType<MediaObservationDto>(resolvedOk.Value);
            Assert.Equal(MediaObservationStatuses.Matched, resolvedDto.MatchStatus);
            Assert.Equal(seasonTwo.Id.ToString(), resolvedDto.MediaTitleId);
            Assert.Equal(-12, resolvedDto.EpisodeOffset);
            Assert.Equal(12, resolvedDto.ResolvedProgress);

            var resolvedIdentity = await fixture.Db.MediaEpisodeProviderIdentities
                .SingleAsync(item => item.ProviderEpisodeId == knownIdentity.ProviderEpisodeId);
            Assert.Equal(canonicalEpisode.Id, resolvedIdentity.MediaEpisodeId);
            Assert.False(resolvedIdentity.HasConflict);
            Assert.Equal(12, seasonTwoEntry.ProgressEpisodes);
            var resolvedSummary = await fixture.Controller.GetSummary(CancellationToken.None);
            Assert.Equal(0, Assert.IsType<MediaObservationSummaryDto>(
                Assert.IsType<OkObjectResult>(resolvedSummary.Result).Value).TotalUnresolved);
            return;
        }

        Assert.Equal(MediaObservationStatuses.Matched, response.MatchStatus);
        Assert.True(response.ProgressUpdated);

        Assert.Null(await fixture.Db.MediaObservations.FindAsync(existingObservation.Id));

        var establishedMapping = await fixture.Db.MediaProviderSeasonMappings.SingleAsync();
        Assert.Equal(seasonTwo.Id, establishedMapping.MediaTitleId);
        Assert.Equal(-12, establishedMapping.EpisodeOffset);
        Assert.Equal(MediaProviderSeasonMappingSources.ProviderEpisodeIdentity, establishedMapping.MappingSource);

        Assert.Equal(0, seasonOneEntry.ProgressEpisodes);
        Assert.Equal(12, seasonTwoEntry.ProgressEpisodes);
        Assert.True(knownIdentity.IsTrusted);
        Assert.False(knownIdentity.HasConflict);
    }

    [Fact]
    public async Task Resolve_WithOffset_StoresResolvedProgressAndOffset()
    {
        await using var fixture = await MediaObservationFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var title = new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = "Season Two Show",
            MediaKind = MediaKinds.Anime,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            CreatedAt = now,
            UpdatedAt = now
        };
        var observation = new MediaObservation
        {
            Id = Guid.NewGuid(),
            UserId = fixture.UserId,
            SiteIdentifier = MediaObservationSiteIdentifiers.Crunchyroll,
            ObservedUrl = "https://www.crunchyroll.com/watch/OFFSET/episode-16",
            SiteMediaId = "OFFSET",
            ObservedTitle = "Season Two Show - Episode 16",
            ProgressHint = "16",
            ObservedAt = now,
            MatchStatus = MediaObservationStatuses.Ambiguous,
            CreatedAt = now,
            UpdatedAt = now
        };
        var candidate = new MediaObservationCandidate
        {
            Id = Guid.NewGuid(),
            MediaObservationId = observation.Id,
            MediaTitleId = title.Id,
            CandidateSource = MediaObservationCandidateSources.LibraryTitleSearch,
            Title = title.CanonicalTitle,
            MediaKind = title.MediaKind,
            Score = 0.5m,
            CreatedAt = now
        };
        fixture.Db.MediaTitles.Add(title);
        fixture.Db.MediaLibraryEntries.Add(new MediaLibraryEntry
        {
            Id = Guid.NewGuid(),
            UserId = fixture.UserId,
            MediaTitleId = title.Id,
            Status = MediaLibraryStatuses.Current,
            ProgressEpisodes = 0,
            CreatedAt = now,
            UpdatedAt = now
        });
        fixture.Db.MediaObservations.Add(observation);
        fixture.Db.MediaObservationCandidates.Add(candidate);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Controller.Resolve(observation.Id, new ResolveMediaObservationRequest
        {
            CandidateId = candidate.Id,
            EpisodeOffset = -12
        }, CancellationToken.None);

        var resolvedResponse = Assert.IsType<MediaObservationDto>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(-12, resolvedResponse.EpisodeOffset);
        Assert.Equal(4, resolvedResponse.ResolvedProgress);
        Assert.Null(await fixture.Db.MediaObservations.FindAsync(observation.Id));
        Assert.Single(await fixture.Db.MediaObservationEpisodeOffsets.ToListAsync());
    }

    [Fact]
    public async Task Resolve_MalCrossReference_PreservesAniListCanonicalMetadata()
    {
        var provider = new SearchOnlyMediaProvider(
            providerId: MediaObservationSiteIdentifiers.MyAnimeList,
            titleDetails: new MediaProviderTitleDetails
            {
                ProviderId = MediaObservationSiteIdentifiers.MyAnimeList,
                ProviderMediaId = "anime:154587",
                Title = "MAL title",
                MediaKind = MediaKinds.Anime,
                PosterUrl = "https://example.test/mal.jpg",
                PrimaryProgressDimension = MediaProgressDimensions.Episode,
                ReleaseStatusDimension = MediaProgressDimensions.Episode,
                CrossReferences =
                [
                    new MediaProviderCrossReference
                    {
                        ProviderId = MediaObservationSiteIdentifiers.AniList,
                        ProviderMediaId = "154587"
                    }
                ]
            });
        await using var fixture = await MediaObservationFixture.CreateAsync(new SingleProviderRegistry(provider));
        var now = DateTimeOffset.UtcNow;
        var title = new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = "AniList title",
            PosterUrl = "https://example.test/anilist.jpg",
            MediaKind = MediaKinds.Anime,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            CreatedAt = now,
            UpdatedAt = now
        };
        var observation = new MediaObservation
        {
            Id = Guid.NewGuid(),
            UserId = fixture.UserId,
            SiteIdentifier = MediaObservationSiteIdentifiers.Crunchyroll,
            ObservedUrl = "https://www.crunchyroll.com/watch/MAL-CROSS-REF/episode-1",
            ObservedTitle = "AniList title - Episode 1",
            ProgressHint = "1",
            ObservedAt = now,
            MatchStatus = MediaObservationStatuses.NoMatch,
            CreatedAt = now,
            UpdatedAt = now
        };
        fixture.Db.AddRange(
            title,
            observation,
            new ConnectedServiceAccount
            {
                Id = 901,
                UserId = fixture.UserId,
                Service = MediaObservationSiteIdentifiers.MyAnimeList,
                ExternalAccountId = "viewer-901",
                CreatedAt = now.UtcDateTime,
                UpdatedAt = now.UtcDateTime
            },
            new MediaProviderLink
            {
                Id = Guid.NewGuid(),
                MediaTitleId = title.Id,
                Provider = MediaObservationSiteIdentifiers.AniList,
                ExternalId = "154587",
                LinkSource = MediaMappingSources.Imported,
                CreatedAt = now,
                UpdatedAt = now
            });
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Controller.Resolve(observation.Id, new ResolveMediaObservationRequest
        {
            ProviderId = MediaObservationSiteIdentifiers.MyAnimeList,
            ProviderMediaId = "anime:154587",
            AddToLibraryConfirmed = true
        }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        var persisted = await fixture.Db.MediaTitles.SingleAsync();
        Assert.Equal("AniList title", persisted.CanonicalTitle);
        Assert.Equal("https://example.test/anilist.jpg", persisted.PosterUrl);
        Assert.Contains(
            await fixture.Db.MediaProviderLinks.ToListAsync(),
            link => link.Provider == MediaObservationSiteIdentifiers.MyAnimeList);
    }

    private sealed class MediaObservationFixture : IAsyncDisposable
    {
        private MediaObservationFixture(
            SqliteConnection connection,
            ApplicationDbContext db,
            MediaObservationsController controller)
        {
            _connection = connection;
            Db = db;
            Controller = controller;
        }

        private readonly SqliteConnection _connection;

        public ApplicationDbContext Db { get; }
        public MediaObservationsController Controller { get; }
        public int UserId { get; } = 611;

        public static async Task<MediaObservationFixture> CreateAsync(IMediaProviderRegistry? registry = null)
        {
            const int userId = 611;
            const string email = "observations.api@example.com";

            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;

            var db = new ApplicationDbContext(options);
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(TestUserFactory.Create(userId, email));
            await db.SaveChangesAsync();

            var seasonMappingService = new MediaProviderSeasonMappingService(
                db,
                NullLogger<MediaProviderSeasonMappingService>.Instance);
            var matchingService = new MediaObservationMatchingService(
                db,
                seasonMappingService,
                new MediaRelationGraphRefreshQueue(),
                NullLogger<MediaObservationMatchingService>.Instance);
            var operationProcessor = new MediaProviderOperationProcessor(
                db,
                new EmptyMediaProviderRegistry(),
                NullLogger<MediaProviderOperationProcessor>.Instance);
            var progressService = new MediaObservationProgressService(
                db,
                operationProcessor,
                NullLogger<MediaObservationProgressService>.Instance);
            var episodeIdentityService = new MediaEpisodeIdentityService(
                db,
                seasonMappingService,
                NullLogger<MediaEpisodeIdentityService>.Instance);
            var userManager = CreateUserManager(db);

            var controller = new MediaObservationsController(
                db,
                userManager,
                matchingService,
                progressService,
                episodeIdentityService,
                seasonMappingService,
                registry ?? new EmptyMediaProviderRegistry(),
                NullLogger<MediaObservationsController>.Instance);

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(
                        new ClaimsIdentity(
                            [
                                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                                new Claim(ClaimTypes.Name, email),
                                new Claim(ClaimTypes.Email, email)
                            ],
                            authenticationType: "Test"))
                }
            };

            return new MediaObservationFixture(connection, db, controller);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class EmptyMediaProviderRegistry : IMediaProviderRegistry
    {
        public bool IsSupported(string providerId) => false;

        public IMediaProvider GetRequired(string providerId)
        {
            throw new NotSupportedException();
        }

        public IReadOnlyCollection<string> GetSupportedProviderIds() => [];
    }

    private sealed class SingleProviderRegistry(IMediaProvider provider) : IMediaProviderRegistry
    {
        public bool IsSupported(string providerId) => string.Equals(providerId, provider.ProviderId, StringComparison.OrdinalIgnoreCase);

        public IMediaProvider GetRequired(string providerId) => IsSupported(providerId) ? provider : throw new NotSupportedException();

        public IReadOnlyCollection<string> GetSupportedProviderIds() => [provider.ProviderId];
    }

    private sealed class SearchOnlyMediaProvider(
        bool connected = true,
        IReadOnlyList<MediaProviderSearchResult>? searchResults = null,
        string providerId = MediaObservationSiteIdentifiers.AniList,
        MediaProviderTitleDetails? titleDetails = null) : IMediaProvider
    {
        public string ProviderId => providerId;

        public Task<IReadOnlyList<MediaProviderSearchResult>> SearchAsync(int userId, MediaCatalogSearchRequest request, CancellationToken cancellationToken)
        {
            IReadOnlyList<MediaProviderSearchResult> results = searchResults ??
            [
                new MediaProviderSearchResult
                {
                    ProviderId = ProviderId,
                    ProviderMediaId = "154587",
                    Title = "Frieren: Beyond Journey's End",
                    MediaKind = MediaKinds.Anime,
                    EpisodeCount = 28,
                    PrimaryProgressDimension = MediaProgressDimensions.Episode,
                    ReleaseStatusDimension = MediaProgressDimensions.Episode
                }
            ];
            return Task.FromResult(results);
        }

        public Task<ConnectedServiceAccount?> GetConnectedAccountAsync(int userId, CancellationToken cancellationToken = default)
        {
            ConnectedServiceAccount? account = connected
                ? new ConnectedServiceAccount
                {
                    Id = 901,
                    UserId = userId,
                    Service = ProviderId,
                    ExternalAccountId = "viewer-901",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }
                : null;
            return Task.FromResult(account);
        }
        public string GetAuthorizationUrl(string redirectUri, string state, string codeChallenge) => throw new NotSupportedException();
        public Task<ConnectedServiceAccount> ExchangeCodeAndSaveAsync(int userId, string authorizationCode, string redirectUri, string codeVerifier, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DisconnectAsync(int userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MediaProviderLibraryImportResult> ImportLibraryAsync(int userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MediaProviderTitleDetails?> GetTitleDetailsAsync(int userId, string providerMediaId, CancellationToken cancellationToken)
            => Task.FromResult(titleDetails);
        public Task<MediaProviderMutationResult> UpdateProgressAsync(int userId, MediaProgressUpdateRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MediaProviderMutationResult> UpdateStatusAsync(int userId, MediaStatusUpdateRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new MediaProviderMutationResult
            {
                ProviderId = ProviderId,
                ProviderMediaId = request.ProviderMediaId,
                AppliedAt = DateTimeOffset.UtcNow,
                LastRemoteUpdateAt = DateTimeOffset.UtcNow
            });
        public Task<MediaProviderMutationResult> UpdateScoreAsync(int userId, MediaScoreUpdateRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MediaReleaseMetadata?> GetReleaseMetadataAsync(int userId, string providerMediaId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static UserManager<User> CreateUserManager(ApplicationDbContext db)
    {
        var store = new UserStore<User, IdentityRole<int>, ApplicationDbContext, int>(db);
        return new UserManager<User>(
            store,
            optionsAccessor: Options.Create(new IdentityOptions()),
            passwordHasher: new PasswordHasher<User>(),
            userValidators: [],
            passwordValidators: [],
            keyNormalizer: new UpperInvariantLookupNormalizer(),
            errors: new IdentityErrorDescriber(),
            services: new ServiceCollection().BuildServiceProvider(),
            logger: NullLogger<UserManager<User>>.Instance);
    }
}
