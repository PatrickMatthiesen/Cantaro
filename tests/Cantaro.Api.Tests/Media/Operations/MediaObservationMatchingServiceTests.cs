using System.Data.Common;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantaro.Api.Tests;

public class MediaObservationMatchingServiceTests
{
    [Fact]
    public async Task ProcessObservation_LoadsContinuityInBatchesInsteadOfOncePerCandidate()
    {
        var interceptor = new RelationQueryCountingInterceptor();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.UtcNow;
        db.Users.Add(TestUserFactory.Create(812, "batch-continuity@example.com"));
        for (var index = 1; index <= 20; index++)
        {
            var title = new MediaTitle
            {
                Id = Guid.NewGuid(),
                CanonicalTitle = $"Example Season {index}",
                MediaKind = MediaKinds.Anime,
                Format = MediaFormats.Tv,
                EpisodeCount = 12,
                PrimaryProgressDimension = MediaProgressDimensions.Episode,
                ReleaseStatusDimension = MediaProgressDimensions.Episode,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.MediaTitles.Add(title);
            db.MediaLibraryEntries.Add(new MediaLibraryEntry
            {
                Id = Guid.NewGuid(),
                UserId = 812,
                MediaTitleId = title.Id,
                Status = MediaLibraryStatuses.Current,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        var observation = new MediaObservation
        {
            Id = Guid.NewGuid(),
            UserId = 812,
            SiteIdentifier = MediaObservationSiteIdentifiers.Crunchyroll,
            ObservedUrl = "https://www.crunchyroll.com/series/EXAMPLE/example",
            ObservedTitle = "Example",
            RawPayload = "{\"seriesTitle\":\"Example\",\"observedEpisodes\":[{\"episodeNumber\":1}]}",
            MatchStatus = MediaObservationStatuses.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.MediaObservations.Add(observation);
        await db.SaveChangesAsync();
        interceptor.RelationQueryCount = 0;
        var seasonMappings = new MediaProviderSeasonMappingService(
            db,
            NullLogger<MediaProviderSeasonMappingService>.Instance);
        var service = new MediaObservationMatchingService(
            db,
            seasonMappings,
            new MediaRelationGraphRefreshQueue(),
            NullLogger<MediaObservationMatchingService>.Instance);

        await service.ProcessObservationAsync(observation, CancellationToken.None);

        Assert.InRange(interceptor.RelationQueryCount, 1, 4);
    }

    // -----------------------------------------------------------------------
    // Exact provider link match
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessObservation_MatchesExactly_WhenProviderLinkExists()
    {
        await using var fixture = await ObservationTestFixture.CreateAsync();

        var title = fixture.SeedTitle("Frieren: Beyond Journey's End", "anime", aniListId: "154587");

        var observation = fixture.SeedObservation(
            siteIdentifier: "anilist",
            siteMediaId: "154587",
            observedTitle: "Frieren: Beyond Journey's End");

        await fixture.Service.ProcessObservationAsync(observation.Id, CancellationToken.None);

        var persisted = await fixture.DbContext.MediaObservations
            .Include(o => o.Candidates)
            .SingleAsync(o => o.Id == observation.Id);

        Assert.Equal(MediaObservationStatuses.Matched, persisted.MatchStatus);
        Assert.Equal(title.Id, persisted.MediaTitleId);
        Assert.NotNull(persisted.AcceptedCandidateId);

        var accepted = persisted.Candidates.Single(c => c.IsAccepted);
        Assert.Equal(MediaObservationCandidateSources.ProviderLinkExact, accepted.CandidateSource);
        Assert.True(accepted.Score >= 0.90m);
    }

    // -----------------------------------------------------------------------
    // Fuzzy library title match
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessObservation_FindsFuzzyLibraryMatch_WhenTitleIsClose()
    {
        await using var fixture = await ObservationTestFixture.CreateAsync();

        var title = fixture.SeedTitle("Spy x Family", "anime");
        fixture.SeedLibraryEntry(title);

        var observation = fixture.SeedObservation(
            siteIdentifier: "crunchyroll",
            siteMediaId: null,
            observedTitle: "Spy x Family");

        await fixture.Service.ProcessObservationAsync(observation.Id, CancellationToken.None);

        var persisted = await fixture.DbContext.MediaObservations
            .Include(o => o.Candidates)
            .SingleAsync(o => o.Id == observation.Id);

        // Exact word overlap should match or be ambiguous (not no_match).
        Assert.NotEqual(MediaObservationStatuses.NoMatch, persisted.MatchStatus);
        Assert.NotEmpty(persisted.Candidates);

        var topCandidate = persisted.Candidates.OrderByDescending(c => c.Score).First();
        Assert.Equal(title.Id, topCandidate.MediaTitleId);
    }

    [Fact]
    public async Task ProcessObservation_UsesRawSeriesTitle_WhenObservedTitleIncludesEpisodeTitle()
    {
        await using var fixture = await ObservationTestFixture.CreateAsync();

        var title = fixture.SeedTitle("Witch Hat Atelier", "anime");
        fixture.SeedLibraryEntry(title);

        var observation = fixture.SeedObservation(
            siteIdentifier: "crunchyroll",
            siteMediaId: "GE00258188ENUS",
            observedTitle: "Witch Hat Atelier - E9 - A Nightmare Stained in Black",
            rawPayload: """
            {
              "seriesTitle": "Witch Hat Atelier",
              "episodeTitle": "E9 - A Nightmare Stained in Black",
              "episodeNumber": 9
            }
            """);

        await fixture.Service.ProcessObservationAsync(observation.Id, CancellationToken.None);

        var persisted = await fixture.DbContext.MediaObservations
            .Include(o => o.Candidates)
            .SingleAsync(o => o.Id == observation.Id);

        Assert.Equal(MediaObservationStatuses.Matched, persisted.MatchStatus);
        Assert.Equal(title.Id, persisted.MediaTitleId);
        Assert.NotEmpty(persisted.Candidates);
    }

    [Fact]
    public async Task ProcessObservation_UsesObservedSeasonOrdinal_WhenRawSeriesTitleIsTheBaseTitle()
    {
        await using var fixture = await ObservationTestFixture.CreateAsync();

        var baseTitle = fixture.SeedTitle(
            "Wistoria: Wand and Sword",
            MediaKinds.Anime,
            episodeCount: 8,
            format: MediaFormats.Tv);
        var seasonThree = fixture.SeedTitle(
            "Wistoria: Wand and Sword Season 3",
            MediaKinds.Anime,
            episodeCount: 8,
            format: MediaFormats.Tv);
        fixture.SeedSequel(baseTitle, seasonThree);

        var observation = fixture.SeedObservation(
            MediaObservationSiteIdentifiers.Crunchyroll,
            null,
            "Wistoria: Wand and Sword — Season 3",
            rawPayload: """
            {
              "seriesTitle": "Wistoria: Wand and Sword",
              "seasonNumber": 3,
              "observedEpisodes": [
                {"episodeNumber": 1}, {"episodeNumber": 2}, {"episodeNumber": 3}, {"episodeNumber": 4},
                {"episodeNumber": 5}, {"episodeNumber": 6}, {"episodeNumber": 7}, {"episodeNumber": 8}
              ]
            }
            """);

        await fixture.Service.ProcessObservationAsync(observation.Id, CancellationToken.None);

        var persisted = await fixture.DbContext.MediaObservations
            .Include(item => item.Candidates)
            .SingleAsync(item => item.Id == observation.Id);

        Assert.Equal(MediaObservationStatuses.Matched, persisted.MatchStatus);
        Assert.Equal(seasonThree.Id, persisted.MediaTitleId);
        Assert.NotEqual(baseTitle.Id, persisted.MediaTitleId);
    }

    [Fact]
    public async Task ProcessObservation_TreatsSeasonOneAsTheUnsuffixedBaseTitle()
    {
        await using var fixture = await ObservationTestFixture.CreateAsync();

        var baseTitle = fixture.SeedTitle(
            "BOFURI: I Don't Want to Get Hurt, so I'll Max Out My Defense.",
            MediaKinds.Anime,
            episodeCount: 8,
            format: MediaFormats.Tv);
        var seasonTwo = fixture.SeedTitle(
            "BOFURI: I Don't Want to Get Hurt, so I'll Max Out My Defense. Season 2",
            MediaKinds.Anime,
            episodeCount: 8,
            format: MediaFormats.Tv);
        fixture.SeedSequel(baseTitle, seasonTwo);

        var observation = fixture.SeedObservation(
            MediaObservationSiteIdentifiers.Crunchyroll,
            null,
            "BOFURI: I Don’t Want to Get Hurt, so I’ll Max Out My Defense. — Season 1",
            rawPayload: """
            {
              "seriesTitle": "BOFURI: I Don’t Want to Get Hurt, so I’ll Max Out My Defense.",
              "seasonNumber": 1,
              "observedEpisodes": [
                {"episodeNumber": 1}, {"episodeNumber": 2}, {"episodeNumber": 3}, {"episodeNumber": 4},
                {"episodeNumber": 5}, {"episodeNumber": 6}, {"episodeNumber": 7}, {"episodeNumber": 8}
              ]
            }
            """);

        await fixture.Service.ProcessObservationAsync(observation.Id, CancellationToken.None);

        var persisted = await fixture.DbContext.MediaObservations
            .Include(item => item.Candidates)
            .SingleAsync(item => item.Id == observation.Id);

        Assert.Equal(MediaObservationStatuses.Matched, persisted.MatchStatus);
        Assert.Equal(baseTitle.Id, persisted.MediaTitleId);
        Assert.NotEqual(seasonTwo.Id, persisted.MediaTitleId);
    }

    [Fact]
    public async Task ProcessObservation_PrefersSeasonEvidence_WhenCorrectSeasonHasUnknownEpisodeCount()
    {
        await using var fixture = await ObservationTestFixture.CreateAsync();

        var baseTitle = fixture.SeedTitle(
            "Wistoria: Wand and Sword",
            MediaKinds.Anime,
            episodeCount: 8,
            format: MediaFormats.Tv);
        var seasonFour = fixture.SeedTitle(
            "Wistoria: Wand and Sword Season 4",
            MediaKinds.Anime,
            episodeCount: null,
            format: MediaFormats.Tv);
        fixture.SeedSequel(baseTitle, seasonFour);

        var observation = fixture.SeedObservation(
            MediaObservationSiteIdentifiers.Crunchyroll,
            null,
            "Wistoria: Wand and Sword — Season 4",
            rawPayload: """
            {
              "seriesTitle": "Wistoria: Wand and Sword",
              "seasonNumber": 4,
              "observedEpisodes": [
                {"episodeNumber": 1}, {"episodeNumber": 2}, {"episodeNumber": 3}, {"episodeNumber": 4},
                {"episodeNumber": 5}, {"episodeNumber": 6}, {"episodeNumber": 7}, {"episodeNumber": 8}
              ]
            }
            """);

        await fixture.Service.ProcessObservationAsync(observation.Id, CancellationToken.None);

        var persisted = await fixture.DbContext.MediaObservations
            .Include(item => item.Candidates)
            .SingleAsync(item => item.Id == observation.Id);

        Assert.Equal(MediaObservationStatuses.Matched, persisted.MatchStatus);
        Assert.Equal(seasonFour.Id, persisted.MediaTitleId);
        Assert.NotEqual(baseTitle.Id, persisted.MediaTitleId);
    }

    [Fact]
    public async Task ProcessObservation_DoesNotForceASeasonFromAnOrdinalWithoutTitleEvidence()
    {
        await using var fixture = await ObservationTestFixture.CreateAsync();

        var seasonTwo = fixture.SeedTitle(
            "Wistoria: Wand and Sword Season 2",
            MediaKinds.Anime,
            episodeCount: 8,
            format: MediaFormats.Tv);
        var seasonThree = fixture.SeedTitle(
            "Wistoria: Wand and Sword Season 3",
            MediaKinds.Anime,
            episodeCount: 8,
            format: MediaFormats.Tv);
        fixture.SeedSequel(seasonTwo, seasonThree);

        var observation = fixture.SeedObservation(
            MediaObservationSiteIdentifiers.Crunchyroll,
            null,
            "Season 3",
            rawPayload: """
            {
              "seriesTitle": "An unrelated show",
              "seasonNumber": 3
            }
            """);

        await fixture.Service.ProcessObservationAsync(observation.Id, CancellationToken.None);

        var persisted = await fixture.DbContext.MediaObservations
            .SingleAsync(item => item.Id == observation.Id);

        Assert.NotEqual(MediaObservationStatuses.Matched, persisted.MatchStatus);
        Assert.Null(persisted.MediaTitleId);
        Assert.Null(persisted.AcceptedCandidateId);
    }

    [Fact]
    public async Task ProcessObservation_MatchesDrStoneSeasonTwo_WhenSeasonSynonymProvidesCanonicalEvidence()
    {
        await using var fixture = await ObservationTestFixture.CreateAsync();

        var baseTitle = fixture.SeedTitle(
            "Dr. STONE",
            MediaKinds.Anime,
            episodeCount: 11,
            format: MediaFormats.Tv);
        var seasonTwo = fixture.SeedTitle(
            "STONE WARS",
            MediaKinds.Anime,
            synonyms: ["Dr. STONE 2"],
            episodeCount: 11,
            format: MediaFormats.Tv);
        fixture.SeedSequel(baseTitle, seasonTwo);

        var observation = fixture.SeedObservation(
            MediaObservationSiteIdentifiers.Crunchyroll,
            null,
            "Dr. STONE — Season 2",
            rawPayload: $$"""
            {
              "seriesTitle": "Dr. STONE",
              "seasonNumber": 2,
              "observedEpisodes": [{{EpisodeEvidence(Enumerable.Range(1, 11))}}]
            }
            """);

        await fixture.Service.ProcessObservationAsync(observation.Id, CancellationToken.None);

        var persisted = await fixture.DbContext.MediaObservations
            .Include(item => item.Candidates)
            .SingleAsync(item => item.Id == observation.Id);

        Assert.Equal(MediaObservationStatuses.Matched, persisted.MatchStatus);
        Assert.Equal(seasonTwo.Id, persisted.MediaTitleId);
        Assert.NotEqual(baseTitle.Id, persisted.MediaTitleId);
    }

    [Fact]
    public async Task ProcessObservation_MatchesOnePieceEastBlueRange_WhenEpisodeCountIsUnknown()
    {
        await using var fixture = await ObservationTestFixture.CreateAsync();

        var onePiece = fixture.SeedTitle(
            "ONE PIECE",
            MediaKinds.Anime,
            episodeCount: null,
            format: MediaFormats.Tv);
        var observation = fixture.SeedObservation(
            MediaObservationSiteIdentifiers.Crunchyroll,
            null,
            "ONE PIECE — East Blue (1-61)",
            rawPayload: $$"""
            {
              "seriesTitle": "ONE PIECE",
              "providerSeasonId": "label:east-blue-1-61",
              "seasonTitle": "East Blue (1-61)",
              "observedEpisodes": [{{EpisodeEvidence(Enumerable.Range(1, 28))}}]
            }
            """);

        await fixture.Service.ProcessObservationAsync(observation.Id, CancellationToken.None);

        var persisted = await fixture.DbContext.MediaObservations
            .SingleAsync(item => item.Id == observation.Id);

        Assert.Equal(MediaObservationStatuses.Matched, persisted.MatchStatus);
        Assert.Equal(onePiece.Id, persisted.MediaTitleId);
    }

    [Fact]
    public async Task ProcessObservation_DoesNotAutoMatchOnePiece_WhenSeasonRangeDoesNotContainObservedEpisodes()
    {
        await using var fixture = await ObservationTestFixture.CreateAsync();

        fixture.SeedTitle(
            "ONE PIECE",
            MediaKinds.Anime,
            episodeCount: null,
            format: MediaFormats.Tv);
        var observation = fixture.SeedObservation(
            MediaObservationSiteIdentifiers.Crunchyroll,
            null,
            "ONE PIECE — Alabasta (62-143)",
            rawPayload: $$"""
            {
              "seriesTitle": "ONE PIECE",
              "providerSeasonId": "label:alabasta-62-143",
              "seasonTitle": "Alabasta (62-143)",
              "observedEpisodes": [{{EpisodeEvidence(Enumerable.Range(1, 28))}}]
            }
            """);

        await fixture.Service.ProcessObservationAsync(observation.Id, CancellationToken.None);

        var persisted = await fixture.DbContext.MediaObservations
            .SingleAsync(item => item.Id == observation.Id);

        Assert.NotEqual(MediaObservationStatuses.Matched, persisted.MatchStatus);
        Assert.Null(persisted.MediaTitleId);
        Assert.Null(persisted.AcceptedCandidateId);
    }

    [Fact]
    public async Task ProcessObservation_MatchesOnePieceOpenEndedElbaphRange_WhenObservedEpisodesAreInRange()
    {
        await using var fixture = await ObservationTestFixture.CreateAsync();

        var onePiece = fixture.SeedTitle(
            "ONE PIECE",
            MediaKinds.Anime,
            episodeCount: null,
            format: MediaFormats.Tv);
        var observation = fixture.SeedObservation(
            MediaObservationSiteIdentifiers.Crunchyroll,
            null,
            "ONE PIECE — Elbaph (1156-current)",
            rawPayload: $$"""
            {
              "seriesTitle": "ONE PIECE",
              "providerSeasonId": "label:elbaph-1156-current",
              "seasonTitle": "Elbaph (1156-current)",
              "observedEpisodes": [{{EpisodeEvidence(Enumerable.Range(1156, 19))}}]
            }
            """);

        await fixture.Service.ProcessObservationAsync(observation.Id, CancellationToken.None);

        var persisted = await fixture.DbContext.MediaObservations
            .SingleAsync(item => item.Id == observation.Id);

        Assert.Equal(MediaObservationStatuses.Matched, persisted.MatchStatus);
        Assert.Equal(onePiece.Id, persisted.MediaTitleId);
    }

    [Fact]
    public async Task ProcessObservation_UsesAniListSynonymAndExcludesMangaForStreamingSource()
    {
        await using var fixture = await ObservationTestFixture.CreateAsync();

        var anime = fixture.SeedTitle(
            "Honzuki no Gekokujou: Shisho ni Naru Tame ni wa Shudan wo Erandeiraremasen 4th Season",
            MediaKinds.Anime,
            synonyms: ["Ascendance of a Bookworm Season 4"]);
        fixture.SeedTitle("Ascendance of a Bookworm: Part 2", MediaKinds.Manga);

        var observation = fixture.SeedObservation(
            siteIdentifier: MediaObservationSiteIdentifiers.Crunchyroll,
            siteMediaId: null,
            observedTitle: "Ascendance of a Bookworm — Season 4",
            rawPayload: """{"seriesTitle":"Ascendance of a Bookworm Season 4"}""");

        await fixture.Service.ProcessObservationAsync(observation.Id, CancellationToken.None);

        var persisted = await fixture.DbContext.MediaObservations
            .Include(item => item.Candidates)
            .SingleAsync(item => item.Id == observation.Id);

        Assert.Equal(MediaObservationStatuses.Matched, persisted.MatchStatus);
        Assert.Equal(anime.Id, persisted.MediaTitleId);
        Assert.All(persisted.Candidates, candidate => Assert.NotEqual(MediaKinds.Manga, candidate.MediaKind));
    }

    [Fact]
    public async Task ProcessObservation_ExcludesReadingMediaForNetflixSource()
    {
        await using var fixture = await ObservationTestFixture.CreateAsync();
        fixture.SeedTitle("Blue Period", MediaKinds.Manga);
        var observation = fixture.SeedObservation("netflix", null, "Blue Period");

        await fixture.Service.ProcessObservationAsync(observation.Id, CancellationToken.None);

        var persisted = await fixture.DbContext.MediaObservations
            .Include(item => item.Candidates)
            .SingleAsync(item => item.Id == observation.Id);
        Assert.Equal(MediaObservationStatuses.NoMatch, persisted.MatchStatus);
        Assert.Empty(persisted.Candidates);
    }

    [Fact]
    public async Task ProcessObservation_AllowsMangaForUnrestrictedSource()
    {
        await using var fixture = await ObservationTestFixture.CreateAsync();
        var manga = fixture.SeedTitle("Blue Period", MediaKinds.Manga);
        var observation = fixture.SeedObservation(MediaObservationSiteIdentifiers.Unknown, null, "Blue Period");

        await fixture.Service.ProcessObservationAsync(observation.Id, CancellationToken.None);

        var persisted = await fixture.DbContext.MediaObservations.SingleAsync(item => item.Id == observation.Id);
        Assert.Equal(MediaObservationStatuses.Matched, persisted.MatchStatus);
        Assert.Equal(manga.Id, persisted.MediaTitleId);
    }

    // -----------------------------------------------------------------------
    // No match
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessObservation_ReturnsNoMatch_WhenNoCandidatesFound()
    {
        await using var fixture = await ObservationTestFixture.CreateAsync();

        // No titles in the database.
        var observation = fixture.SeedObservation(
            siteIdentifier: "unknown",
            siteMediaId: null,
            observedTitle: "A title that does not exist anywhere");

        await fixture.Service.ProcessObservationAsync(observation.Id, CancellationToken.None);

        var persisted = await fixture.DbContext.MediaObservations
            .SingleAsync(o => o.Id == observation.Id);

        Assert.Equal(MediaObservationStatuses.NoMatch, persisted.MatchStatus);
        Assert.Null(persisted.MediaTitleId);
    }

    // -----------------------------------------------------------------------
    // Ambiguous
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessObservation_ReturnsAmbiguous_WhenMultipleLowConfidenceCandidatesExist()
    {
        await using var fixture = await ObservationTestFixture.CreateAsync();

        // Two titles that share some words with "Attack on Titan".
        fixture.SeedTitle("Attack on Titan", "anime");
        fixture.SeedTitle("Attack on Titan: The Final Season", "anime");

        // Observation title that matches both partially.
        var observation = fixture.SeedObservation(
            siteIdentifier: "unknown",
            siteMediaId: null,
            observedTitle: "Attack on Titan Final");

        await fixture.Service.ProcessObservationAsync(observation.Id, CancellationToken.None);

        var persisted = await fixture.DbContext.MediaObservations
            .Include(o => o.Candidates)
            .SingleAsync(o => o.Id == observation.Id);

        // Should have found candidates.
        Assert.NotEmpty(persisted.Candidates);
        // Should not be auto-matched to a single title without confidence.
        Assert.NotEqual(MediaObservationStatuses.NoMatch, persisted.MatchStatus);
    }

    // -----------------------------------------------------------------------
    // Retry clears old candidates
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessObservation_ClearsOldCandidates_OnRetry()
    {
        await using var fixture = await ObservationTestFixture.CreateAsync();

        var observation = fixture.SeedObservation(
            siteIdentifier: "unknown",
            siteMediaId: null,
            observedTitle: "No match title xyzzy");

        // First run: no candidates.
        await fixture.Service.ProcessObservationAsync(observation.Id, CancellationToken.None);

        var firstAttempt = await fixture.DbContext.MediaObservations
            .Include(o => o.Candidates)
            .SingleAsync(o => o.Id == observation.Id);
        Assert.Empty(firstAttempt.Candidates);
        Assert.Equal(1, firstAttempt.MatchAttemptCount);

        // Add a title and retry.
        fixture.SeedTitle("No match title xyzzy", "anime");

        var reloaded = await fixture.DbContext.MediaObservations
            .Include(o => o.Candidates)
            .SingleAsync(o => o.Id == observation.Id);

        await fixture.Service.ProcessObservationAsync(reloaded.Id, CancellationToken.None);

        var secondAttempt = await fixture.DbContext.MediaObservations
            .Include(o => o.Candidates)
            .SingleAsync(o => o.Id == observation.Id);

        Assert.Equal(2, secondAttempt.MatchAttemptCount);
        Assert.NotNull(secondAttempt.LastMatchAttemptedAt);
    }

    // -----------------------------------------------------------------------
    // Attempt counter increments
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessObservation_DoesNotTrustAnotherUsersUnconfirmedEpisodeAssignment()
    {
        await using var fixture = await ObservationTestFixture.CreateAsync();
        var poisonedTitle = fixture.SeedTitle("Wrong global title", MediaKinds.Anime);
        var correctTitle = fixture.SeedTitle("Correct title", MediaKinds.Anime);
        const int secondUserId = 502;
        fixture.DbContext.Users.Add(TestUserFactory.Create(secondUserId, "second-user@example.com"));
        fixture.SeedLibraryEntry(correctTitle, secondUserId);
        var now = DateTimeOffset.UtcNow;
        var poisonedEpisode = new MediaEpisode
        {
            Id = Guid.NewGuid(),
            MediaTitleId = poisonedTitle.Id,
            EpisodeNumber = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
        fixture.DbContext.AddRange(
            poisonedEpisode,
            new MediaEpisodeProviderIdentity
            {
                Id = Guid.NewGuid(),
                MediaEpisodeId = poisonedEpisode.Id,
                Provider = MediaObservationSiteIdentifiers.Crunchyroll,
                ProviderEpisodeId = "POISONED1",
                ProviderUrlPath = "/watch/POISONED1",
                SeenCount = 1,
                FirstSeenAt = now,
                LastSeenAt = now,
                IsTrusted = false
            });
        await fixture.DbContext.SaveChangesAsync();
        var observation = fixture.SeedObservation(
            MediaObservationSiteIdentifiers.Crunchyroll,
            "POISONED1",
            "Correct title",
            userId: secondUserId);

        await fixture.Service.ProcessObservationAsync(observation.Id, CancellationToken.None);

        var persisted = await fixture.DbContext.MediaObservations.SingleAsync(item => item.Id == observation.Id);
        Assert.Equal(MediaObservationStatuses.Matched, persisted.MatchStatus);
        Assert.Equal(correctTitle.Id, persisted.MediaTitleId);
    }

    [Fact]
    public async Task ProcessObservation_IncrementsAttemptCount()
    {
        await using var fixture = await ObservationTestFixture.CreateAsync();

        var observation = fixture.SeedObservation(
            siteIdentifier: "unknown",
            siteMediaId: null,
            observedTitle: "irrelevant");

        for (var i = 1; i <= 3; i++)
        {
            var current = await fixture.DbContext.MediaObservations
                .Include(o => o.Candidates)
                .SingleAsync(o => o.Id == observation.Id);

            await fixture.Service.ProcessObservationAsync(current.Id, CancellationToken.None);

            var after = await fixture.DbContext.MediaObservations
                .SingleAsync(o => o.Id == observation.Id);

            Assert.Equal(i, after.MatchAttemptCount);
        }
    }

    // -----------------------------------------------------------------------
    // Fixture helpers
    // -----------------------------------------------------------------------

    private static string EpisodeEvidence(IEnumerable<int> episodeNumbers)
        => string.Join(", ", episodeNumbers.Select(number => $"{{\"episodeNumber\":{number}}}"));

    private sealed class ObservationTestFixture : IAsyncDisposable
    {
        private const int UserId = 501;

        private readonly SqliteConnection _connection;

        private ObservationTestFixture(
            SqliteConnection connection,
            ApplicationDbContext dbContext,
            MediaObservationMatchingService service)
        {
            _connection = connection;
            DbContext = dbContext;
            Service = service;
        }

        public ApplicationDbContext DbContext { get; }
        public MediaObservationMatchingService Service { get; }

        public static async Task<ObservationTestFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;

            var dbContext = new ApplicationDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            dbContext.Users.Add(TestUserFactory.Create(UserId, "obs-test@example.com"));
            await dbContext.SaveChangesAsync();

            var service = new MediaObservationMatchingService(
                dbContext,
                new MediaProviderSeasonMappingService(
                    dbContext,
                    NullLogger<MediaProviderSeasonMappingService>.Instance),
                new MediaRelationGraphRefreshQueue(),
                NullLogger<MediaObservationMatchingService>.Instance);

            return new ObservationTestFixture(connection, dbContext, service);
        }

        public MediaTitle SeedTitle(
            string canonicalTitle,
            string mediaKind,
            string? aniListId = null,
            IReadOnlyList<string>? synonyms = null,
            int? episodeCount = null,
            string? format = null)
        {
            var title = new MediaTitle
            {
                Id = Guid.NewGuid(),
                CanonicalTitle = canonicalTitle,
                Synonyms = synonyms is null ? [] : [.. synonyms],
                MediaKind = mediaKind,
                Format = format,
                EpisodeCount = episodeCount,
                PrimaryProgressDimension = MediaProgressDimensions.Episode,
                ReleaseStatusDimension = MediaProgressDimensions.Episode,
                SupportsEpisodeProgress = mediaKind == MediaKinds.Anime,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            DbContext.MediaTitles.Add(title);

            if (aniListId is not null)
            {
                DbContext.MediaProviderLinks.Add(new MediaProviderLink
                {
                    Id = Guid.NewGuid(),
                    MediaTitleId = title.Id,
                    Provider = "anilist",
                    ExternalId = aniListId,
                    LinkSource = MediaMappingSources.Imported,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            }

            DbContext.SaveChanges();
            return title;
        }

        public void SeedSequel(MediaTitle earlier, MediaTitle later)
        {
            var now = DateTimeOffset.UtcNow;
            var snapshotId = Guid.NewGuid();
            foreach (var title in new[] { earlier, later })
            {
                var link = DbContext.MediaProviderLinks
                    .SingleOrDefault(item => item.MediaTitleId == title.Id && item.Provider == "anilist");
                if (link is null)
                {
                    DbContext.MediaProviderLinks.Add(new MediaProviderLink
                    {
                        Id = Guid.NewGuid(),
                        MediaTitleId = title.Id,
                        Provider = "anilist",
                        ExternalId = $"test-{title.Id:N}",
                        LinkSource = MediaMappingSources.Imported,
                        CreatedAt = now,
                        UpdatedAt = now,
                        RelationsLastVerifiedAt = now,
                        RelationsSnapshotId = snapshotId
                    });
                }
                else
                {
                    link.RelationsLastVerifiedAt = now;
                    link.RelationsSnapshotId = snapshotId;
                }
            }

            DbContext.MediaTitleRelations.Add(new MediaTitleRelation
            {
                Id = Guid.NewGuid(),
                MediaTitleId = earlier.Id,
                RelatedMediaTitleId = later.Id,
                RelationType = MediaRelationTypes.Sequel,
                SourceProvider = "anilist",
                FirstSeenAt = now,
                LastVerifiedAt = now
            });
            DbContext.SaveChanges();
        }

        public void SeedLibraryEntry(MediaTitle title, int userId = UserId)
        {
            DbContext.MediaLibraryEntries.Add(new MediaLibraryEntry
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                MediaTitleId = title.Id,
                Status = MediaLibraryStatuses.Current,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

            DbContext.SaveChanges();
        }

        public MediaObservation SeedObservation(
            string siteIdentifier,
            string? siteMediaId,
            string observedTitle,
            string? rawPayload = null,
            int userId = UserId)
        {
            var observation = new MediaObservation
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SiteIdentifier = siteIdentifier,
                ObservedUrl = $"https://{siteIdentifier}.co/anime/test",
                SiteMediaId = siteMediaId,
                ObservedTitle = observedTitle,
                RawPayload = rawPayload,
                ObservedAt = DateTimeOffset.UtcNow,
                MatchStatus = MediaObservationStatuses.Pending,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            DbContext.MediaObservations.Add(observation);
            DbContext.SaveChanges();
            return observation;
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class RelationQueryCountingInterceptor : DbCommandInterceptor
    {
        public int RelationQueryCount { get; set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Count(command);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Count(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void Count(DbCommand command)
        {
            if (command.CommandText.Contains("MediaTitleRelations", StringComparison.Ordinal))
            {
                RelationQueryCount++;
            }
        }
    }
}
