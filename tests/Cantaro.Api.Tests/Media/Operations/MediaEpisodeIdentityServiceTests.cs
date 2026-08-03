using System.Text.Json;
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

        var episodes = await fixture.Db.MediaEpisodes
            .Include(item => item.ProviderIdentities)
            .OrderBy(item => item.EpisodeNumber)
            .ToListAsync();
        Assert.Equal([7, 8], episodes.Select(item => item.EpisodeNumber));
        Assert.Equal("/watch/CURRENT7", episodes[0].ProviderIdentities.Single().ProviderUrlPath);
        Assert.Equal("/watch/NEXT8", episodes[1].ProviderIdentities.Single().ProviderUrlPath);

        var destination = await fixture.Service.ResolveContinueWatchingAsync(
            fixture.UserId,
            fixture.LibraryEntryId,
            CancellationToken.None);
        Assert.NotNull(destination);
        Assert.Equal("direct", destination.Outcome);
        Assert.Equal(8, destination.EpisodeNumber);
        Assert.Equal("https://www.crunchyroll.com/watch/NEXT8", destination.Url);
    }

    [Fact]
    public async Task RecordObservation_RepeatedIdentityIncrementsSeenCount()
    {
        await using var fixture = await EpisodeIdentityFixture.CreateAsync();
        var observation = fixture.MakeObservation(3, "EPISODE3", "https://www.crunchyroll.com/watch/EPISODE3");

        await fixture.Service.RecordObservationAsync(observation, CancellationToken.None);
        await fixture.Service.RecordObservationAsync(observation, CancellationToken.None);

        var identity = await fixture.Db.MediaEpisodeProviderIdentities.SingleAsync();
        Assert.Equal(2, identity.SeenCount);
        Assert.False(identity.HasConflict);
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
            fixture.LibraryEntryId,
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

        var catalog = await fixture.Service.GetEpisodeCatalogAsync(
            fixture.UserId,
            fixture.LibraryEntryId,
            CancellationToken.None);

        Assert.NotNull(catalog);
        Assert.Equal("crunchyroll", catalog.SeriesProvider);
        Assert.Equal("https://www.crunchyroll.com/series/SERIES1", catalog.SeriesUrl);
        Assert.Equal([1, 2], catalog.Episodes.Select(episode => episode.EpisodeNumber));
        Assert.Equal("https://www.crunchyroll.com/watch/EPISODE2", catalog.Episodes[1].Url);
        Assert.Equal("Two", catalog.Episodes[1].Title);
    }

    [Fact]
    public async Task RecordObservation_ConflictingCanonicalEpisodeDoesNotRemapIdentity()
    {
        await using var fixture = await EpisodeIdentityFixture.CreateAsync();
        var first = fixture.MakeObservation(3, "SAMEID", "https://www.crunchyroll.com/watch/SAMEID");
        var second = fixture.MakeObservation(4, "SAMEID", "https://www.crunchyroll.com/watch/SAMEID");

        await fixture.Service.RecordObservationAsync(first, CancellationToken.None);
        var originalEpisodeId = (await fixture.Db.MediaEpisodeProviderIdentities.SingleAsync()).MediaEpisodeId;
        await fixture.Service.RecordObservationAsync(second, CancellationToken.None);

        var identity = await fixture.Db.MediaEpisodeProviderIdentities.SingleAsync();
        Assert.Equal(originalEpisodeId, identity.MediaEpisodeId);
        Assert.True(identity.HasConflict);
        Assert.Equal(2, identity.SeenCount);
    }

    [Fact]
    public async Task RecordObservation_DoesNotCreateNextEpisodePastKnownFinale()
    {
        await using var fixture = await EpisodeIdentityFixture.CreateAsync(episodeCount: 12);
        var observation = fixture.MakeObservation(
            12,
            "FINALE12",
            "https://www.crunchyroll.com/watch/FINALE12",
            "OTHER1",
            "https://www.crunchyroll.com/watch/OTHER1");

        await fixture.Service.RecordObservationAsync(observation, CancellationToken.None);

        Assert.Equal(1, await fixture.Db.MediaEpisodes.CountAsync());
        Assert.Equal(12, (await fixture.Db.MediaEpisodes.SingleAsync()).EpisodeNumber);
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

        var episodes = await fixture.Db.MediaEpisodes
            .Include(item => item.ProviderIdentities)
            .OrderBy(item => item.EpisodeNumber)
            .ToListAsync();
        Assert.Equal([1, 2], episodes.Select(item => item.EpisodeNumber));
        Assert.Equal("One", episodes[0].Title);
        Assert.Equal("/watch/EPISODE2", episodes[1].ProviderIdentities.Single().ProviderUrlPath);
        Assert.All(
            episodes.SelectMany(item => item.ProviderIdentities),
            identity =>
            {
                Assert.Equal("SERIES1", identity.ProviderSeriesId);
                Assert.Equal(4, identity.ProviderSeasonNumber);
                Assert.Equal(1, identity.SeenCount);
            });
    }

    [Fact]
    public async Task RecordObservation_AppliesConfirmedOffsetAndRejectsUnsafeRenderedUrls()
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

        var episode = await fixture.Db.MediaEpisodes.SingleAsync();
        Assert.Equal(13, episode.EpisodeNumber);
        Assert.Equal("EPISODE1", (await fixture.Db.MediaEpisodeProviderIdentities.SingleAsync()).ProviderEpisodeId);
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
                Provider = "anilist",
                ProviderAccountId = "test-account",
                ProviderMediaId = "123",
                NormalizedStatus = MediaLibraryStatuses.Current,
                ProgressEpisodes = progressEpisodes,
                CreatedAt = now,
                UpdatedAt = now
            };

            db.Users.Add(TestUserFactory.Create(userId, "episode-identities@example.com"));
            db.MediaTitles.Add(title);
            db.MediaLibraryEntries.Add(entry);
            await db.SaveChangesAsync();

            return new EpisodeIdentityFixture(
                connection,
                db,
                new MediaEpisodeIdentityService(db, NullLogger<MediaEpisodeIdentityService>.Instance),
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
                RawPayload = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
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
                RawPayload = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                MatchStatus = MediaObservationStatuses.Matched,
                MediaTitleId = TitleId,
                CreatedAt = now,
                UpdatedAt = now
            };
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
