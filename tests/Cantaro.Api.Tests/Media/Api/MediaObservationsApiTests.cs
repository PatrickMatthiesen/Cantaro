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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Cantaro.Api.Tests;

public partial class MediaObservationsApiTests
{
    [Theory]
    [InlineData("retained")]
    [InlineData("after-insert")]
    [InlineData("before-update")]
    [InlineData("before-child-insert")]
    public async Task Submit_RecoversFromDuplicateWatchRaceOnSqlite(string interleaving)
    {
        var connectionString = $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connectionString).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await AssertDuplicateWatchRecoveryAsync(db, interleaving);
    }

    [PostgresTheory]
    [InlineData("retained")]
    [InlineData("after-insert")]
    [InlineData("before-update")]
    [InlineData("before-child-insert")]
    public async Task Submit_RecoversFromDuplicateWatchRaceOnPostgres(string interleaving)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await AssertDuplicateWatchRecoveryAsync(schema.Db, interleaving);
    }

    private static async Task AssertDuplicateWatchRecoveryAsync(ApplicationDbContext db, string interleaving)
    {
        db.Users.Add(TestUserFactory.Create(MediaObservationFixture.TestUserId, MediaObservationFixture.TestEmail));
        await db.SaveChangesAsync();
        var winner = MediaObservationFixture.CreateController(db);
        var request = new SubmitMediaObservationRequest
        {
            SiteIdentifier = "crunchyroll", SiteMediaId = " DUPLICATE2 ",
            ObservedUrl = "https://www.crunchyroll.com/watch/DUPLICATE2",
            ObservedTitle = "Duplicate watch", EpisodeNumber = 2,
            ObservedAt = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero)
        };
        string? winnerId = null;
        async Task InsertWinnerAsync()
        {
            var result = await winner.Submit(request, default);
            var response = Assert.IsType<SubmitMediaObservationResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
            Assert.False(response.WasDeduplicated);
            winnerId = response.ObservationId;
        }

        async Task CompleteWinnerAsync()
        {
            var title = new MediaTitle
            {
                Id = Guid.NewGuid(), CanonicalTitle = "Duplicate watch", MediaKind = MediaKinds.Anime,
                PrimaryProgressDimension = MediaProgressDimensions.Episode, SupportsEpisodeProgress = true,
                ReleaseStatusDimension = MediaProgressDimensions.Episode,
                EpisodeCount = 12
            };
            db.MediaTitles.Add(title);
            db.MediaLibraryEntries.Add(new MediaLibraryEntry
            {
                Id = Guid.NewGuid(), UserId = MediaObservationFixture.TestUserId,
                MediaTitleId = title.Id, Status = MediaLibraryStatuses.Current, ProgressEpisodes = 1
            });
            await db.SaveChangesAsync();
            var mappings = new MediaProviderSeasonMappingService(db, NullLogger<MediaProviderSeasonMappingService>.Instance);
            await mappings.EstablishDirectAsync("crunchyroll", "DUPLICATE2", title.Id, 2,
                MediaProviderSeasonMappingSources.Manual, default);
            var response = Assert.IsType<SubmitMediaObservationResponse>(
                Assert.IsType<OkObjectResult>((await winner.Submit(request, default)).Result).Value);
            Assert.True(response.WasDeduplicated);
            Assert.True(response.ProgressUpdated);
            Assert.Empty(await db.MediaObservations.ToListAsync());
        }

        var winnerAlreadyExists = interleaving is "before-update" or "before-child-insert";
        if (winnerAlreadyExists)
        {
            await InsertWinnerAsync();
        }
        var collision = new DuplicateObservationSaveInterceptor(
            winnerAlreadyExists ? CompleteWinnerAsync : InsertWinnerAsync,
            winnerAlreadyExists ? EntityState.Modified : EntityState.Added,
            interleaving == "after-insert" ? CompleteWinnerAsync : null);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>(
                (DbContextOptions<ApplicationDbContext>)db.GetService<IDbContextOptions>())
            .AddInterceptors(collision).Options;
        await using var contenderDb = new ApplicationDbContext(options);
        var contender = MediaObservationFixture.CreateController(contenderDb);
        var recovered = await contender.Submit(new SubmitMediaObservationRequest
        {
            SiteIdentifier = request.SiteIdentifier, SiteMediaId = "duplicate2",
            ObservedUrl = request.ObservedUrl, ObservedTitle = "Updated duplicate watch", EpisodeNumber = 2,
            ObservedAt = request.ObservedAt!.Value.AddMinutes(1),
            ReleaseTrack = "dub:en",
            ObservedEpisodes = interleaving == "before-update" ? [] :
            [
                new ObservedProviderEpisodeDto
                {
                    ProviderEpisodeId = "DUPLICATE3",
                    ProviderUrl = "https://www.crunchyroll.com/watch/DUPLICATE3", EpisodeNumber = 3
                }
            ]
        }, default);

        var ok = Assert.IsType<OkObjectResult>(recovered.Result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        var duplicate = Assert.IsType<SubmitMediaObservationResponse>(ok.Value);
        Assert.NotNull(winnerId);
        Assert.Equal(winnerAlreadyExists ? 0 : 1, collision.UniqueViolationCount);
        Assert.Equal(interleaving == "before-child-insert" ? 1 : 0, collision.ForeignKeyViolationCount);
        Assert.Equal(interleaving == "before-update" ? 1 : 0, collision.ConcurrencyViolationCount);
        if (interleaving == "retained")
        {
            Assert.True(duplicate.WasDeduplicated);
            Assert.Equal(winnerId, duplicate.ObservationId);
            var persisted = await contenderDb.MediaObservations.AsNoTracking().Include(row => row.Episodes).SingleAsync();
            Assert.Equal("Updated duplicate watch", persisted.ObservedTitle);
            Assert.Equal(request.ObservedAt.Value.AddMinutes(1), persisted.ObservedAt);
            Assert.Equal("DUPLICATE3", Assert.Single(persisted.Episodes).ProviderEpisodeId);
        }
        else
        {
            Assert.False(duplicate.WasDeduplicated);
            Assert.NotEqual(winnerId, duplicate.ObservationId);
            Assert.Empty(await contenderDb.MediaObservations.ToListAsync());
            Assert.Equal(2, await contenderDb.MediaLibraryEntries.Select(entry => entry.ProgressEpisodes).SingleAsync());
        }

        var rawEpisode = await contenderDb.MediaProviderEpisodes.AsNoTracking()
            .SingleAsync(row => row.ProviderEpisodeId == "DUPLICATE2");
        Assert.Equal("dub:en", rawEpisode.ReleaseTrack);
        Assert.Contains("en", rawEpisode.AvailableAudioLanguageCodes);
    }

    internal sealed class DuplicateObservationSaveInterceptor(Func<Task> beforeSave, EntityState observedState,
        Func<Task>? afterUniqueViolation) : SaveChangesInterceptor
    {
        private bool _interleaved;
        public int UniqueViolationCount { get; private set; }
        public int ForeignKeyViolationCount { get; private set; }
        public int ConcurrencyViolationCount { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!_interleaved && eventData.Context!.ChangeTracker.Entries<MediaObservation>()
                    .Any(entry => entry.State == observedState))
            {
                _interleaved = true;
                await beforeSave();
            }
            return result;
        }

        public override async Task SaveChangesFailedAsync(DbContextErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Exception.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 }
                or PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                UniqueViolationCount++;
                if (afterUniqueViolation is not null)
                {
                    await afterUniqueViolation();
                }
            }
            else if (eventData.Exception.InnerException is SqliteException { SqliteExtendedErrorCode: 787 }
                or PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
            {
                ForeignKeyViolationCount++;
            }
        }

        public override ValueTask<InterceptionResult> ThrowingConcurrencyExceptionAsync(
            ConcurrencyExceptionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            ConcurrencyViolationCount++;
            return ValueTask.FromResult(result);
        }
    }

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
        fixture.Db.MediaProviderEpisodes.Add(new MediaProviderEpisode
        {
            Provider = "crunchyroll", ProviderEpisodeId = "GE00340376ENUS",
            ProviderSeriesId = "GW4HM7WK9", ProviderSeasonId = "WISTORIA2", ProviderSeasonNumber = 2,
            ProviderEpisodeNumber = 22, ProviderUrlPath = "/watch/GE00340376ENUS",
            SeenCount = 1, FirstSeenAt = now, LastSeenAt = now
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
            EpisodeNumber = 1,
            SeasonTitle = "Season 2",
            SeasonNumber = 2,
            ProviderSeriesId = "GW4HM7WK9",
            WatchProgressPercent = 85.1m,
            ObservedAt = now.AddMinutes(-1),
            ExtensionVersion = "0.1.0"
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<SubmitMediaObservationResponse>(ok.Value);
        Assert.Equal(MediaObservationStatuses.Matched, response.MatchStatus);
        Assert.Empty(fixture.Db.MediaObservations);
        var seasonTwoEntry = await fixture.Db.MediaLibraryEntries
            .SingleAsync(entry => entry.MediaTitleId == seasonTwo.Id);
        Assert.Equal(10, seasonTwoEntry.ProgressEpisodes);
        var identity = await fixture.Db.MediaProviderEpisodes
            .SingleAsync(item => item.ProviderEpisodeId == "GE00340376ENUS");
        Assert.Null(response.ObservationId);
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
        Assert.Equal(12, await fixture.Db.MediaProviderEpisodes.CountAsync());
        var mapping = await fixture.Db.MediaProviderSeasonMappings.SingleAsync();
        Assert.Equal(-12, mapping.EpisodeOffset);
        Assert.Equal(13, mapping.ProviderEpisodeStart);
        Assert.Equal(24, mapping.ProviderEpisodeEnd);
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
    public async Task Submit_RefreshesProviderChoicesWhenSeriesTitleChanges()
    {
        var queries = new List<string>();
        var provider = new SearchOnlyMediaProvider
        {
            Search = query =>
            {
                queries.Add(query);
                return
                [
                    new MediaProviderSearchResult
                    {
                        ProviderId = "anilist", ProviderMediaId = queries.Count.ToString(), Title = query,
                        MediaKind = MediaKinds.Anime, PrimaryProgressDimension = MediaProgressDimensions.Episode,
                        ReleaseStatusDimension = MediaProgressDimensions.Episode
                    }
                ];
            }
        };
        await using var fixture = await MediaObservationFixture.CreateAsync(new SingleProviderRegistry(provider));
        var request = new SubmitMediaObservationRequest
        {
            SiteIdentifier = "crunchyroll", SiteMediaId = "CORRECTEDTITLE",
            ObservedUrl = "https://www.crunchyroll.com/watch/CORRECTEDTITLE", ObservedTitle = "Episode 4",
            SeriesTitle = "Original series title", EpisodeNumber = 4, ObservedAt = DateTimeOffset.UtcNow.AddMinutes(-2)
        };

        var first = await fixture.Controller.Submit(request, default);
        Assert.Equal("1", GetChoice(first).ProviderMediaId);
        request.ObservedTitle = "Corrected episode title";
        request.ObservedAt = request.ObservedAt!.Value.AddMinutes(1);
        var unchangedQuery = await fixture.Controller.Submit(request, default);
        Assert.Equal("1", GetChoice(unchangedQuery).ProviderMediaId);
        request.SeriesTitle = "Corrected series title";
        request.ObservedAt = request.ObservedAt.Value.AddSeconds(30);
        var corrected = await fixture.Controller.Submit(request, default);

        Assert.Equal("2", GetChoice(corrected).ProviderMediaId);
        Assert.Equal(["Original series title", "Corrected series title"], queries);

        static MediaObservationProviderChoiceDto GetChoice(ActionResult<SubmitMediaObservationResponse> result) =>
            Assert.Single(Assert.IsType<SubmitMediaObservationResponse>(Assert.IsType<OkObjectResult>(result.Result).Value).ProviderChoices);
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
            SeriesTitle = title.CanonicalTitle,
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

        var recordedEpisodes = await fixture.Db.MediaProviderEpisodes
            .OrderBy(item => item.ProviderEpisodeNumber).ToListAsync();
        Assert.Equal([5, 6], recordedEpisodes.Select(item => item.ProviderEpisodeNumber));
        Assert.Equal(["GWDU8WDNQ", "NEXTGEN6"], recordedEpisodes.Select(item => item.ProviderEpisodeId));
    }


    [Theory]
    [InlineData(false, 16, -12, null, false)]
    [InlineData(true, 16, -12, null, false)]
    [InlineData(true, 4, -1, 0, false)]
    [InlineData(true, 13, 0, 12, true)]
    [InlineData(false, 13, -13, 12, true)]
    public async Task Resolve_WithOffset_ValidatesAndStoresProgress(
        bool hasContainer, int episodeNumber, int offset, int? episodeCount, bool outOfRange)
    {
        await using var fixture = await MediaObservationFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var title = new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = "Season Two Show",
            MediaKind = MediaKinds.Anime,
            EpisodeCount = episodeCount,
            SupportsEpisodeProgress = true,
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
            ObservedUrl = $"https://www.crunchyroll.com/watch/OFFSET/episode-{episodeNumber}",
            SiteMediaId = "OFFSET",
            ObservedTitle = $"{title.CanonicalTitle} - Episode {episodeNumber}",
            ProgressHint = episodeNumber.ToString(),
            EpisodeNumber = hasContainer ? episodeNumber : null,
            ProviderSeriesId = hasContainer ? "SEASONSHOW" : null,
            ProviderSeasonId = hasContainer ? "SEASON2" : null,
            SeasonNumber = hasContainer ? 2 : null,
            SeasonTitle = hasContainer ? "Season 2 (29-40)" : null,
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
            EpisodeOffset = offset
        }, CancellationToken.None);

        if (outOfRange)
        {
            Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.Equal(0, await fixture.Db.MediaLibraryEntries.Select(row => row.ProgressEpisodes).SingleAsync());
            Assert.Empty(fixture.Db.MediaProviderOperations);
            Assert.Empty(fixture.Db.MediaProviderSeasonMappings);
            Assert.Empty(fixture.Db.MediaProviderEpisodeMappings);
            Assert.Equal(MediaObservationStatuses.Ambiguous, observation.MatchStatus);
            return;
        }

        var resolvedResponse = Assert.IsType<MediaObservationDto>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(offset, resolvedResponse.EpisodeOffset);
        Assert.Equal(episodeNumber + offset, resolvedResponse.ResolvedProgress);
        Assert.Null(await fixture.Db.MediaObservations.FindAsync(observation.Id));
        if (hasContainer)
        {
            var season = await fixture.Db.MediaProviderSeasonMappings.SingleAsync();
            Assert.Equal(MediaProviderSeasonMappingSources.Manual, season.MappingSource);
            Assert.Equal(1 - offset, season.ProviderEpisodeStart);
            Assert.Null(season.ProviderEpisodeEnd);
        }
        else
        {
            var direct = await fixture.Db.MediaProviderEpisodeMappings.SingleAsync();
            Assert.Equal(MediaProviderSeasonMappingSources.Manual, direct.MappingSource);
            Assert.Equal(episodeNumber + offset, direct.CanonicalEpisodeNumber);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Submit_SeasonlessWatchDoesNotUseFallbackAcrossNumberedSeasons(bool secondSeasonOnlyInMappings)
    {
        await using var fixture = await MediaObservationFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var title = CreateWistoriaTitle("Mapped First Season", 2026, now);
        var entry = CreateWistoriaEntry(fixture.UserId, title, now);
        fixture.Db.AddRange(title, entry);
        var observedSeasonCount = secondSeasonOnlyInMappings ? 1 : 2;
        for (var season = 1; season <= observedSeasonCount; season++)
        {
            fixture.Db.MediaProviderEpisodes.Add(new MediaProviderEpisode
            {
                Provider = "crunchyroll", ProviderSeriesId = "NUMBERED", ProviderSeasonNumber = season,
                ProviderEpisodeNumber = 1, ProviderEpisodeId = $"NUMBERED{season}",
                ProviderUrlPath = $"/watch/NUMBERED{season}", SeenCount = 1, FirstSeenAt = now, LastSeenAt = now
            });
        }
        await fixture.Db.SaveChangesAsync();
        var mappings = new MediaProviderSeasonMappingService(fixture.Db, NullLogger<MediaProviderSeasonMappingService>.Instance);
        await mappings.EstablishAsync("crunchyroll", "NUMBERED", null, 1, title.Id, 0,
            MediaProviderSeasonMappingSources.Automatic, 1m, default);
        if (secondSeasonOnlyInMappings)
        {
            await mappings.EstablishAsync("crunchyroll", "NUMBERED", null, 2, title.Id, -12,
                MediaProviderSeasonMappingSources.Automatic, 1m, default, episodeStart: 13, episodeEnd: 24);
        }

        var result = await fixture.Controller.Submit(new SubmitMediaObservationRequest
        {
            SiteIdentifier = "crunchyroll", SiteMediaId = "SEASONLESS3", ProviderSeriesId = "NUMBERED",
            ObservedUrl = "https://www.crunchyroll.com/watch/SEASONLESS3", ObservedTitle = "Unidentified episode",
            EpisodeNumber = 3, WatchProgressPercent = 90
        }, default);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(0, await fixture.Db.MediaLibraryEntries.Select(row => row.ProgressEpisodes).SingleAsync());
        var episodes = await MediaProviderSeasonMappingService.GetMappedEpisodesAsync(fixture.Db, [title.Id], default);
        Assert.Equal("NUMBERED1", Assert.Single(episodes).Episode.ProviderEpisodeId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Resolve_ConfirmedProviderAddRejectsInvalidMappingBeforeWritingEitherLibrary(bool overlapsExistingRange)
    {
        var provider = new SearchOnlyMediaProvider(titleDetails: new MediaProviderTitleDetails
        {
            ProviderId = "anilist", ProviderMediaId = "1234", Title = "New Provider Title",
            MediaKind = MediaKinds.Anime, EpisodeCount = 12,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode
        });
        await using var fixture = await MediaObservationFixture.CreateAsync(new SingleProviderRegistry(provider));
        var now = DateTimeOffset.UtcNow;
        var observation = new MediaObservation
        {
            Id = Guid.NewGuid(), UserId = fixture.UserId, SiteIdentifier = "crunchyroll",
            SiteMediaId = "OFFSET13", ObservedUrl = "https://www.crunchyroll.com/watch/OFFSET13",
            ObservedTitle = "New Provider Title", EpisodeNumber = 12,
            MatchStatus = MediaObservationStatuses.NoMatch, ObservedAt = now, CreatedAt = now, UpdatedAt = now
        };
        if (overlapsExistingRange)
        {
            observation.ProviderSeriesId = "RANGED";
            observation.ProviderSeasonId = "SEASON1";
            observation.SeasonTitle = "Part 2 (11-12)";
            var previous = CreateWistoriaTitle("Previous Title", 2025, now);
            fixture.Db.MediaTitles.Add(previous);
            await fixture.Db.SaveChangesAsync();
            var mappings = new MediaProviderSeasonMappingService(fixture.Db, NullLogger<MediaProviderSeasonMappingService>.Instance);
            await mappings.EstablishAsync("crunchyroll", "RANGED", "SEASON1", null, previous.Id, 0,
                MediaProviderSeasonMappingSources.Manual, 1m, default, episodeStart: 1, episodeEnd: 12);
        }
        fixture.Db.AddRange(observation, (await provider.GetConnectedAccountAsync(fixture.UserId))!);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Controller.Resolve(observation.Id, new ResolveMediaObservationRequest
        {
            ProviderId = "anilist", ProviderMediaId = "1234", AddToLibraryConfirmed = true,
            EpisodeOffset = overlapsExistingRange ? 0 : 1
        }, default);

        var error = Assert.IsType<BadRequestObjectResult>(result.Result);
        if (overlapsExistingRange)
        {
            Assert.Contains("overlap", System.Text.Json.JsonSerializer.Serialize(error.Value));
            Assert.Equal(12, (await fixture.Db.MediaProviderSeasonMappings.SingleAsync()).ProviderEpisodeEnd);
        }
        Assert.Equal(0, provider.StatusUpdateCallCount);
        Assert.Empty(fixture.Db.MediaLibraryEntries);
        Assert.Empty(fixture.Db.MediaLibraryProviderBindings);
        Assert.Empty(fixture.Db.MediaProviderLinks);
        Assert.Empty(fixture.Db.MediaProviderOperations);
        Assert.Equal(overlapsExistingRange ? 1 : 0, await fixture.Db.MediaTitles.CountAsync());
        var persisted = await fixture.Db.MediaObservations.AsNoTracking().SingleAsync();
        Assert.Equal(MediaObservationStatuses.NoMatch, persisted.MatchStatus);
        Assert.Null(persisted.MediaTitleId);
        Assert.Null(persisted.ResolvedProgress);
    }

    [Theory]
    [InlineData(MediaKinds.Manga, MediaProgressDimensions.Chapter)]
    [InlineData(MediaKinds.Anime, MediaProgressDimensions.Chapter)]
    [InlineData(MediaKinds.Anime, MediaProgressDimensions.Volume)]
    public async Task Resolve_CrunchyrollRejectsReadingTitlesBeforeLibraryWrites(string kind, string dimension)
    {
        var provider = new SearchOnlyMediaProvider(titleDetails: new MediaProviderTitleDetails
        {
            ProviderId = "anilist", ProviderMediaId = "1234", Title = "Provider Reading Title",
            MediaKind = kind, ChapterCount = 20, VolumeCount = 5,
            PrimaryProgressDimension = dimension, ReleaseStatusDimension = dimension
        });
        await using var fixture = await MediaObservationFixture.CreateAsync(new SingleProviderRegistry(provider));
        var now = DateTimeOffset.UtcNow;
        var title = CreateWistoriaTitle("Existing Reading Title", 2026, now);
        title.MediaKind = kind;
        title.PrimaryProgressDimension = dimension;
        var entry = new MediaLibraryEntry
        {
            Id = Guid.NewGuid(), UserId = fixture.UserId, MediaTitleId = title.Id,
            Status = MediaLibraryStatuses.Current, ProgressChapters = 3, ProgressVolumes = 1,
            CreatedAt = now, UpdatedAt = now
        };
        var observation = new MediaObservation
        {
            Id = Guid.NewGuid(), UserId = fixture.UserId, SiteIdentifier = "crunchyroll",
            SiteMediaId = "READING8", ObservedUrl = "https://www.crunchyroll.com/watch/READING8",
            ObservedTitle = "Reading Title", EpisodeNumber = 8,
            MatchStatus = MediaObservationStatuses.Ambiguous, ObservedAt = now, CreatedAt = now, UpdatedAt = now
        };
        var candidate = new MediaObservationCandidate
        {
            Id = Guid.NewGuid(), MediaObservationId = observation.Id, MediaTitleId = title.Id,
            CandidateSource = MediaObservationCandidateSources.LibraryTitleSearch, Title = title.CanonicalTitle,
            MediaKind = kind, Score = 0.5m
        };
        fixture.Db.AddRange(title, entry, observation, candidate, (await provider.GetConnectedAccountAsync(fixture.UserId))!);
        await fixture.Db.SaveChangesAsync();

        Assert.IsType<BadRequestObjectResult>((await fixture.Controller.Resolve(observation.Id,
            new() { CandidateId = candidate.Id }, default)).Result);
        Assert.IsType<BadRequestObjectResult>((await fixture.Controller.Resolve(observation.Id,
            new() { ProviderId = "anilist", ProviderMediaId = "1234", AddToLibraryConfirmed = true }, default)).Result);

        var persisted = await fixture.Db.MediaLibraryEntries.AsNoTracking().SingleAsync();
        Assert.Equal(3, persisted.ProgressChapters);
        Assert.Equal(1, persisted.ProgressVolumes);
        Assert.Equal(0, provider.StatusUpdateCallCount);
        Assert.Empty(fixture.Db.MediaLibraryProviderBindings);
        Assert.Empty(fixture.Db.MediaProviderOperations);
        Assert.Empty(fixture.Db.MediaProviderSeasonMappings);
        Assert.Empty(fixture.Db.MediaProviderEpisodeMappings);
        Assert.Null((await fixture.Db.MediaObservations.AsNoTracking().SingleAsync()).MediaTitleId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Resolve_ConfirmedProviderAddCommitsBeforeDispatchingStatus(bool transientFailure)
    {
        var provider = new SearchOnlyMediaProvider(titleDetails: new MediaProviderTitleDetails
        {
            ProviderId = "anilist", ProviderMediaId = "1234", Title = "New Provider Title",
            MediaKind = MediaKinds.Anime, EpisodeCount = 12,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode
        }, failStatusUpdate: transientFailure);
        await using var fixture = await MediaObservationFixture.CreateAsync(new SingleProviderRegistry(provider));
        var observation = new MediaObservation
        {
            Id = Guid.NewGuid(), UserId = fixture.UserId, SiteIdentifier = "crunchyroll",
            SiteMediaId = "NEWTITLE1", ObservedUrl = "https://www.crunchyroll.com/watch/NEWTITLE1",
            ProviderSeriesId = "NEWTITLE", ProviderSeasonId = "SEASON1", EpisodeNumber = 1,
            ObservedTitle = "New Provider Title", MatchStatus = MediaObservationStatuses.NoMatch
        };
        fixture.Db.AddRange(observation, (await provider.GetConnectedAccountAsync(fixture.UserId))!);
        await fixture.Db.SaveChangesAsync();
        var statusSawCommittedDecision = false;
        provider.BeforeStatusUpdate = async () =>
        {
            Assert.Null(fixture.Db.Database.CurrentTransaction);
            Assert.Equal(MediaObservationStatuses.Matched, await fixture.Db.MediaObservations
                .Select(row => row.MatchStatus).SingleAsync());
            Assert.Equal(1, await fixture.Db.MediaLibraryEntries.CountAsync());
            Assert.Equal(1, await fixture.Db.MediaProviderSeasonMappings.CountAsync());
            statusSawCommittedDecision = true;
        };

        var result = await fixture.Controller.Resolve(observation.Id, new ResolveMediaObservationRequest
        {
            ProviderId = "anilist", ProviderMediaId = "1234", AddToLibraryConfirmed = true
        }, default);

        var response = Assert.IsType<MediaObservationDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        var entry = await fixture.Db.MediaLibraryEntries.SingleAsync();
        var binding = await fixture.Db.MediaLibraryProviderBindings.Include(item => item.MediaProviderLink).SingleAsync();
        var mapping = await fixture.Db.MediaProviderSeasonMappings.SingleAsync();
        Assert.Equal(MediaObservationStatuses.Matched, response.MatchStatus);
        Assert.Equal(entry.Id, binding.MediaLibraryEntryId);
        Assert.Equal("1234", binding.MediaProviderLink!.ExternalId);
        Assert.Equal(entry.MediaTitleId, mapping.MediaTitleId);
        Assert.Equal(MediaProviderSeasonMappingSources.Manual, mapping.MappingSource);
        Assert.Equal(1, provider.StatusUpdateCallCount);
        Assert.Equal(MediaLibraryStatuses.Current, provider.LastStatusUpdate);
        Assert.True(statusSawCommittedDecision);
        var operations = await fixture.Db.MediaProviderOperations.AsNoTracking()
            .Where(row => row.OperationType == MediaProviderOperationTypes.UpdateStatus).ToListAsync();
        if (transientFailure)
        {
            var queued = Assert.Single(operations);
            Assert.Equal(MediaProviderOperationStatuses.Retrying, queued.Status);
            Assert.NotNull(queued.NextAttemptAt);
            Assert.Null(binding.LastSyncedAt);
        }
        else
        {
            Assert.Empty(operations);
            Assert.NotNull(binding.LastSyncedAt);
        }
    }

    [Theory]
    [InlineData(15, 3)]
    [InlineData(12, null)]
    [InlineData(25, null)]
    public async Task Submit_DuplicateNetflixObservationKeepsManualResolution(int episodeNumber, int? expectedProgress)
    {
        await using var fixture = await MediaObservationFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var title = CreateWistoriaTitle("Manually selected title", 2025, now);
        var observation = new MediaObservation
        {
            Id = Guid.NewGuid(), UserId = fixture.UserId, SiteIdentifier = "netflix",
            SiteMediaId = "1234", ObservedUrl = "https://www.netflix.com/watch/1234",
            ObservedTitle = "Unmatched title", ProgressHint = "14",
            MatchStatus = MediaObservationStatuses.Ambiguous
        };
        var candidate = new MediaObservationCandidate
        {
            Id = Guid.NewGuid(), MediaObservationId = observation.Id, MediaTitleId = title.Id,
            CandidateSource = MediaObservationCandidateSources.LibraryTitleSearch, Title = title.CanonicalTitle,
            MediaKind = title.MediaKind, Score = 0.5m
        };
        fixture.Db.AddRange(title, observation, candidate);
        await fixture.Db.SaveChangesAsync();
        Assert.IsType<OkObjectResult>((await fixture.Controller.Resolve(observation.Id, new ResolveMediaObservationRequest
        {
            CandidateId = candidate.Id, EpisodeOffset = -12
        }, default)).Result);
        Assert.Empty(fixture.Db.MediaLibraryEntries);

        var result = await fixture.Controller.Submit(new SubmitMediaObservationRequest
        {
            SiteIdentifier = "netflix", SiteMediaId = "1234", ObservedUrl = observation.ObservedUrl,
            ObservedTitle = observation.ObservedTitle, EpisodeNumber = episodeNumber
        }, default);

        var response = Assert.IsType<SubmitMediaObservationResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.True(response.WasDeduplicated);
        Assert.Equal(MediaObservationStatuses.Matched, response.MatchStatus);
        Assert.Equal(expectedProgress, response.ResolvedProgress);
        var persisted = await fixture.Db.MediaObservations.AsNoTracking().SingleAsync();
        Assert.Equal(title.Id, persisted.MediaTitleId);
        Assert.Equal(candidate.Id, persisted.AcceptedCandidateId);
        Assert.Equal(-12, persisted.EpisodeOffset);
        Assert.Equal(expectedProgress, persisted.ResolvedProgress);
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
            SupportsEpisodeProgress = true,
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

    [Theory]
    [InlineData(MediaObservationSiteIdentifiers.AniList, MediaProgressDimensions.Chapter, 0, true)]
    [InlineData(MediaObservationSiteIdentifiers.Crunchyroll, MediaProgressDimensions.Episode, 0, true)]
    [InlineData(MediaObservationSiteIdentifiers.Crunchyroll, MediaProgressDimensions.CompletionOnly, 0, true)]
    [InlineData(MediaObservationSiteIdentifiers.AniList, MediaProgressDimensions.Chapter, 1, false)]
    [InlineData(MediaObservationSiteIdentifiers.Crunchyroll, MediaProgressDimensions.Episode, -1, false)]
    [InlineData(MediaObservationSiteIdentifiers.Crunchyroll, MediaProgressDimensions.Chapter, 0, false)]
    public async Task Resolve_ConfirmedTitleOnlyProviderSelection(string site, string dimension, int offset, bool succeeds)
    {
        var reading = dimension == MediaProgressDimensions.Chapter;
        var completionOnly = dimension == MediaProgressDimensions.CompletionOnly;
        var provider = new SearchOnlyMediaProvider(titleDetails: new MediaProviderTitleDetails
        {
            ProviderId = MediaObservationSiteIdentifiers.AniList,
            ProviderMediaId = "154587",
            Title = "Title without observed progress",
            MediaKind = reading ? MediaKinds.Manga : completionOnly ? MediaKinds.Movie : MediaKinds.Anime,
            PrimaryProgressDimension = dimension,
            ReleaseStatusDimension = dimension
        });
        await using var fixture = await MediaObservationFixture.CreateAsync(new SingleProviderRegistry(provider));
        var now = DateTimeOffset.UtcNow;
        var observation = new MediaObservation
        {
            Id = Guid.NewGuid(), UserId = fixture.UserId, SiteIdentifier = site,
            SiteMediaId = site == MediaObservationSiteIdentifiers.Crunchyroll
                ? completionOnly ? "SERIES1" : "EPISODE1" : "154587",
            ObservedUrl = site == MediaObservationSiteIdentifiers.Crunchyroll
                ? completionOnly ? "https://www.crunchyroll.com/series/SERIES1" : "https://www.crunchyroll.com/watch/EPISODE1"
                : "https://anilist.co/manga/154587",
            ObservedTitle = "Title without observed progress",
            MatchStatus = MediaObservationStatuses.NoMatch, ObservedAt = now, CreatedAt = now, UpdatedAt = now
        };
        fixture.Db.AddRange(observation, (await provider.GetConnectedAccountAsync(fixture.UserId))!);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Controller.Resolve(observation.Id, new ResolveMediaObservationRequest
        {
            ProviderId = MediaObservationSiteIdentifiers.AniList,
            ProviderMediaId = "154587",
            AddToLibraryConfirmed = true,
            EpisodeOffset = offset
        }, default);

        Assert.Empty(await fixture.Db.MediaProviderEpisodeMappings.ToListAsync());
        Assert.Empty(await fixture.Db.MediaProviderSeasonMappings.ToListAsync());
        if (!succeeds)
        {
            Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.Empty(await fixture.Db.MediaLibraryEntries.ToListAsync());
            Assert.Equal(0, provider.StatusUpdateCallCount);
            return;
        }

        var response = Assert.IsType<MediaObservationDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        var entry = await fixture.Db.MediaLibraryEntries.SingleAsync();
        Assert.Equal(MediaObservationStatuses.Matched, response.MatchStatus);
        Assert.Null(response.ResolvedProgress);
        Assert.Equal(entry.Id.ToString(), response.ResolvedLibraryEntryId);
        Assert.Equal(completionOnly ? (int?)null : 0, reading ? entry.ProgressChapters : entry.ProgressEpisodes);
        Assert.Equal(MediaLibraryStatuses.Current, entry.Status);
        Assert.Equal(1, provider.StatusUpdateCallCount);
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
        public const int TestUserId = 611;
        public const string TestEmail = "observations.api@example.com";
        public int UserId => TestUserId;

        public static async Task<MediaObservationFixture> CreateAsync(IMediaProviderRegistry? registry = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;

            var db = new ApplicationDbContext(options);
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(TestUserFactory.Create(TestUserId, TestEmail));
            await db.SaveChangesAsync();
            return new MediaObservationFixture(connection, db, CreateController(db, registry));
        }

        public static MediaObservationsController CreateController(ApplicationDbContext db,
            IMediaProviderRegistry? registry = null, int userId = TestUserId)
        {
            var seasonMappingService = new MediaProviderSeasonMappingService(
                db,
                NullLogger<MediaProviderSeasonMappingService>.Instance);
            var matchingService = new MediaObservationMatchingService(
                db,
                seasonMappingService,
                new MediaRelationGraphRefreshQueue(),
                NullLogger<MediaObservationMatchingService>.Instance);
            registry ??= new EmptyMediaProviderRegistry();
            var operationProcessor = new MediaProviderOperationProcessor(
                db,
                registry,
                NullLogger<MediaProviderOperationProcessor>.Instance);
            var eventHub = new MediaLibraryEventHub();
            var progressService = new MediaObservationProgressService(
                db,
                operationProcessor,
                eventHub,
                NullLogger<MediaObservationProgressService>.Instance, seasonMappingService, matchingService);
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
                    User = new ClaimsPrincipal(
                        new ClaimsIdentity(
                            [
                                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                                new Claim(ClaimTypes.Name, TestEmail),
                                new Claim(ClaimTypes.Email, TestEmail)
                            ],
                            authenticationType: "Test"))
                }
            };

            return controller;
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
        MediaProviderTitleDetails? titleDetails = null,
        bool failStatusUpdate = false) : IMediaProvider
    {
        public string ProviderId => providerId;
        public int StatusUpdateCallCount { get; private set; }
        public string? LastStatusUpdate { get; private set; }
        public Func<Task>? BeforeStatusUpdate { get; set; }
        public Func<string, IReadOnlyList<MediaProviderSearchResult>>? Search { get; init; }

        public Task<IReadOnlyList<MediaProviderSearchResult>> SearchAsync(int userId, MediaCatalogSearchRequest request, CancellationToken cancellationToken)
        {
            IReadOnlyList<MediaProviderSearchResult> results = Search?.Invoke(request.Query) ?? searchResults ??
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
        public async Task<MediaProviderMutationResult> UpdateStatusAsync(int userId, MediaStatusUpdateRequest request, CancellationToken cancellationToken)
        {
            StatusUpdateCallCount++;
            LastStatusUpdate = request.Status;
            if (BeforeStatusUpdate is not null)
            {
                await BeforeStatusUpdate();
            }
            if (failStatusUpdate)
            {
                throw new HttpRequestException("Provider temporarily unavailable.");
            }
            return new MediaProviderMutationResult
            {
                ProviderId = ProviderId,
                ProviderMediaId = request.ProviderMediaId,
                AppliedAt = DateTimeOffset.UtcNow,
                LastRemoteUpdateAt = DateTimeOffset.UtcNow
            };
        }
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
