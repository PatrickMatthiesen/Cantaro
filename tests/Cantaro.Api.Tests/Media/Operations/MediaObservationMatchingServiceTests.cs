using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantaro.Api.Tests;

public class MediaObservationMatchingServiceTests
{
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
                NullLogger<MediaObservationMatchingService>.Instance);

            return new ObservationTestFixture(connection, dbContext, service);
        }

        public MediaTitle SeedTitle(string canonicalTitle, string mediaKind, string? aniListId = null)
        {
            var title = new MediaTitle
            {
                Id = Guid.NewGuid(),
                CanonicalTitle = canonicalTitle,
                MediaKind = mediaKind,
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

        public void SeedLibraryEntry(MediaTitle title)
        {
            DbContext.MediaLibraryEntries.Add(new MediaLibraryEntry
            {
                Id = Guid.NewGuid(),
                UserId = UserId,
                MediaTitleId = title.Id,
                Provider = "anilist",
                ProviderAccountId = "test-account",
                ProviderMediaId = Guid.NewGuid().ToString(),
                NormalizedStatus = MediaLibraryStatuses.Current,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

            DbContext.SaveChanges();
        }

        public MediaObservation SeedObservation(
            string siteIdentifier,
            string? siteMediaId,
            string observedTitle)
        {
            var observation = new MediaObservation
            {
                Id = Guid.NewGuid(),
                UserId = UserId,
                SiteIdentifier = siteIdentifier,
                ObservedUrl = $"https://{siteIdentifier}.co/anime/test",
                SiteMediaId = siteMediaId,
                ObservedTitle = observedTitle,
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
}
