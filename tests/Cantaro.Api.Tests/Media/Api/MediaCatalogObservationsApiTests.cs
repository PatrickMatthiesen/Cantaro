using System.Security.Claims;
using System.Text.Json;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public class MediaCatalogObservationsApiTests
{
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

        var observation = await fixture.Db.MediaObservations.SingleAsync();
        Assert.Equal(fixture.UserId, observation.UserId);
        Assert.Null(observation.ProgressHint);
        Assert.Null(observation.ResolvedProgress);
        Assert.Equal(0, await fixture.Db.MediaProviderOperations.CountAsync());

        var identities = await fixture.Db.MediaEpisodeProviderIdentities
            .OrderBy(item => item.ProviderEpisodeNumber)
            .ToListAsync();
        Assert.Equal(2, identities.Count);
        Assert.All(identities, item => Assert.Equal("GW4HM7WK9", item.ProviderSeriesId));
        Assert.All(identities, item => Assert.Equal("SEASON1", item.ProviderSeasonId));
    }

    [Fact]
    public async Task Submit_LongRunningSingleSeason_RecordsAll293TrustedEpisodes()
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
        Assert.Equal(293, await fixture.Db.MediaEpisodeProviderIdentities.CountAsync());
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
        Assert.Empty(fixture.Db.MediaEpisodeProviderIdentities);
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
        Assert.DoesNotContain(
            fixture.Db.MediaEpisodeProviderIdentities,
            identity => identity.MediaEpisode!.MediaTitleId == baseTitle.Id);
        Assert.All(
            fixture.Db.MediaEpisodeProviderIdentities,
            identity => Assert.Equal(expectedTitle.Id, identity.MediaEpisode!.MediaTitleId));
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
        Assert.Empty(fixture.Db.MediaEpisodeProviderIdentities);
    }

    [Fact]
    public async Task Submit_CumulativeCrunchyrollSeason_MapsEpisodesToSeasonLocalNumbers()
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
        var observation = await fixture.Db.MediaObservations.SingleAsync();
        Assert.Equal(-12, observation.EpisodeOffset);
        var episode22 = await fixture.Db.MediaEpisodeProviderIdentities
            .Include(identity => identity.MediaEpisode)
            .SingleAsync(identity => identity.ProviderEpisodeId == "GE00340376ENUS");
        Assert.Equal(10, episode22.MediaEpisode!.EpisodeNumber);
        Assert.Equal(22, episode22.ProviderEpisodeNumber);
    }

    [Fact]
    public async Task Submit_DuplicateCorrectedSeasonMatch_RemapsObservedIdentitiesOnly()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        var baseTitle = fixture.AddTitle("Ascendance of a Bookworm", 14, 2019, "TV");
        fixture.AddLibraryEntry(baseTitle, "108268");
        await fixture.Db.SaveChangesAsync();
        var request = CreateRequest("Ascendance of a Bookworm", "G6793XKZY", 3);
        request.ProviderSeasonId = "BOOKWORM4";
        request.SeasonTitle = "Season 4";
        request.SeasonNumber = 1;

        await fixture.Controller.Submit(request, CancellationToken.None);
        var initiallyRecorded = await fixture.Db.MediaEpisodeProviderIdentities
            .Include(identity => identity.MediaEpisode)
            .Where(identity => identity.ProviderSeasonId == "BOOKWORM4")
            .ToListAsync();
        Assert.All(initiallyRecorded, identity => Assert.Equal(baseTitle.Id, identity.MediaEpisode!.MediaTitleId));
        Assert.Empty(fixture.Db.MediaProviderSeasonMappings);

        var unrelatedEpisode = new MediaEpisode
        {
            Id = Guid.NewGuid(),
            MediaTitleId = baseTitle.Id,
            EpisodeNumber = 10,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var unrelatedIdentity = new MediaEpisodeProviderIdentity
        {
            Id = Guid.NewGuid(),
            MediaEpisodeId = unrelatedEpisode.Id,
            Provider = MediaObservationSiteIdentifiers.Crunchyroll,
            ProviderSeriesId = "G6793XKZY",
            ProviderSeasonId = "BOOKWORM1",
            ProviderEpisodeId = "UNRELATED1",
            ProviderUrlPath = "/watch/UNRELATED1/unrelated",
            SeenCount = 1,
            FirstSeenAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow
        };
        fixture.Db.MediaEpisodes.Add(unrelatedEpisode);
        fixture.Db.MediaEpisodeProviderIdentities.Add(unrelatedIdentity);

        var seasonTwo = fixture.AddTitle("Ascendance of a Bookworm Part 2", 12, 2020, "TV");
        var seasonThree = fixture.AddTitle("Ascendance of a Bookworm Season 3", 10, 2022, "TV");
        var seasonFour = fixture.AddTitle(
            "Ascendance of a Bookworm: Adopted Daughter of an Archduke",
            24,
            2026,
            "TV");
        fixture.AddLibraryEntry(seasonTwo, "113693");
        fixture.AddLibraryEntry(seasonThree, "121176");
        fixture.AddLibraryEntry(seasonFour, "171110");
        fixture.AddSequel(baseTitle, seasonTwo);
        fixture.AddSequel(seasonTwo, seasonThree);
        fixture.AddSequel(seasonThree, seasonFour);
        await fixture.Db.SaveChangesAsync();

        fixture.AddSeasonMapping(
            "G6793XKZY",
            "BOOKWORM4",
            4,
            seasonFour,
            0);
        await fixture.Db.SaveChangesAsync();
        request.SeasonNumber = 4;

        var duplicateResult = await fixture.Controller.Submit(request, CancellationToken.None);

        var response = GetResponse(duplicateResult);
        Assert.Equal(seasonFour.Id.ToString(), response.MatchedMediaTitleId);
        var corrected = await fixture.Db.MediaEpisodeProviderIdentities
            .Include(identity => identity.MediaEpisode)
            .Where(identity => identity.ProviderSeasonId == "BOOKWORM4")
            .ToListAsync();
        Assert.All(corrected, identity => Assert.Equal(seasonFour.Id, identity.MediaEpisode!.MediaTitleId));
        var preserved = await fixture.Db.MediaEpisodeProviderIdentities
            .Include(identity => identity.MediaEpisode)
            .SingleAsync(identity => identity.Id == unrelatedIdentity.Id);
        Assert.Equal(baseTitle.Id, preserved.MediaEpisode!.MediaTitleId);
    }

    [Fact]
    public async Task Submit_UnmatchedSeries_ReturnsPendingMatchWithoutRecordingDestinations()
    {
        await using var fixture = await CatalogFixture.CreateAsync();

        var result = await fixture.Controller.Submit(
            CreateRequest("Unknown Catalog Title", "GUNKNOWN1", episodeCount: 1),
            CancellationToken.None);

        var response = GetResponse(result);
        Assert.Equal(MediaCatalogObservationStatuses.PendingMatch, response.Status);
        Assert.Equal(MediaObservationStatuses.NoMatch, response.MatchStatus);
        Assert.Equal(0, response.RecordedEpisodeCount);
        Assert.Null(response.MatchedMediaTitleId);
        Assert.Empty(fixture.Db.MediaEpisodeProviderIdentities);
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
        Assert.Equal(MediaCatalogObservationStatuses.Deduplicated, response.Status);
        Assert.Equal(MediaObservationStatuses.Matched, response.MatchStatus);
        Assert.Single(fixture.Db.MediaObservations);
        var identities = await fixture.Db.MediaEpisodeProviderIdentities.ToListAsync();
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
        Assert.Single(fixture.Db.MediaEpisodeProviderIdentities);
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
        Assert.Single(fixture.Db.MediaEpisodeProviderIdentities);
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
        Assert.Empty(fixture.Db.MediaEpisodeProviderIdentities);
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
            RawPayload = JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
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

        Assert.Equal(2, await fixture.Db.MediaEpisodeProviderIdentities.CountAsync());
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

            var seasonMappingService = new MediaProviderSeasonMappingService(
                db,
                NullLogger<MediaProviderSeasonMappingService>.Instance);
            var controller = new MediaCatalogObservationsController(
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

            return new CatalogFixture(connection, db, controller);
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
