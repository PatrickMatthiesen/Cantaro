using System.Security.Claims;
using System.Text.Json;
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
    public async Task Submit_RejectsUnboundedSeriesPageBatches()
    {
        await using var fixture = await MediaObservationFixture.CreateAsync();
        var episodes = Enumerable.Range(1, 101)
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
    public async Task Submit_PreservesStructuredPlaybackMetadataInRawPayload()
    {
        await using var fixture = await MediaObservationFixture.CreateAsync();

        var result = await fixture.Controller.Submit(new SubmitMediaObservationRequest
        {
            SiteIdentifier = MediaObservationSiteIdentifiers.Crunchyroll,
            ObservedUrl = "https://www.crunchyroll.com/watch/GYVNM7N6Y/episode-7",
            SiteMediaId = "GYVNM7N6Y",
            ObservedTitle = "Frieren - Episode 7 - Like a Fairy Tale",
            SeriesTitle = "Frieren",
            EpisodeTitle = "Like a Fairy Tale",
            EpisodeNumber = 7,
            SeasonTitle = "Season 1",
            SeasonNumber = 1,
            ProviderSeriesId = "GYEXQKJG6",
            NextEpisodeProviderId = "G31UXQ9K2",
            NextEpisodeUrl = "https://www.crunchyroll.com/watch/G31UXQ9K2/episode-8",
            NextEpisodeNumber = 8,
            ObservedEpisodes =
            [
                new ObservedProviderEpisodeDto
                {
                    ProviderEpisodeId = "GYVNM7N6Y",
                    ProviderUrl = "https://www.crunchyroll.com/watch/GYVNM7N6Y/episode-7",
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

        var observation = await fixture.Db.MediaObservations.SingleAsync();
        Assert.Equal("7", observation.ProgressHint);
        Assert.NotNull(observation.RawPayload);

        using var payload = JsonDocument.Parse(observation.RawPayload);
        var root = payload.RootElement;
        Assert.Equal("Frieren", root.GetProperty("seriesTitle").GetString());
        Assert.Equal("Like a Fairy Tale", root.GetProperty("episodeTitle").GetString());
        Assert.Equal(7, root.GetProperty("episodeNumber").GetInt32());
        Assert.Equal("Season 1", root.GetProperty("seasonTitle").GetString());
        Assert.Equal(1, root.GetProperty("seasonNumber").GetInt32());
        Assert.Equal("GYEXQKJG6", root.GetProperty("providerSeriesId").GetString());
        Assert.Equal("G31UXQ9K2", root.GetProperty("nextEpisodeProviderId").GetString());
        Assert.Equal(8, root.GetProperty("nextEpisodeNumber").GetInt32());
        var observedEpisode = root.GetProperty("observedEpisodes")[0];
        Assert.Equal("GYVNM7N6Y", observedEpisode.GetProperty("providerEpisodeId").GetString());
        Assert.Equal(7, observedEpisode.GetProperty("episodeNumber").GetInt32());
        Assert.Equal(85.5m, root.GetProperty("watchProgressPercent").GetDecimal());
        Assert.Equal(1440m, root.GetProperty("durationSeconds").GetDecimal());
        Assert.Equal(1231.2m, root.GetProperty("positionSeconds").GetDecimal());
    }

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
            ConnectedServiceAccountId = account.Id,
            Provider = "anilist",
            ProviderAccountId = "viewer-611",
            ProviderMediaId = "129190",
            NormalizedStatus = MediaLibraryStatuses.Current,
            ProgressEpisodes = 1,
            LastRemoteUpdateAt = now.AddMinutes(-10),
            CreatedAt = now,
            UpdatedAt = now
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
        fixture.Db.MediaLibraryEntries.Add(entry);
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

        var persistedObservation = await fixture.Db.MediaObservations.SingleAsync(item => item.Id == existingObservation.Id);
        Assert.Equal("5", persistedObservation.ProgressHint);
        Assert.Equal(5, persistedObservation.ResolvedProgress);

        var recordedEpisodes = await fixture.Db.MediaEpisodes
            .Include(item => item.ProviderIdentities)
            .OrderBy(item => item.EpisodeNumber)
            .ToListAsync();
        Assert.Equal([5, 6], recordedEpisodes.Select(item => item.EpisodeNumber));
        Assert.Equal("GWDU8WDNQ", recordedEpisodes[0].ProviderIdentities.Single().ProviderEpisodeId);
        Assert.Equal("NEXTGEN6", recordedEpisodes[1].ProviderIdentities.Single().ProviderEpisodeId);
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
        fixture.Db.MediaObservations.Add(observation);
        fixture.Db.MediaObservationCandidates.Add(candidate);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Controller.Resolve(observation.Id, new ResolveMediaObservationRequest
        {
            CandidateId = candidate.Id,
            EpisodeOffset = -12
        }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        var persisted = await fixture.Db.MediaObservations.SingleAsync(item => item.Id == observation.Id);
        Assert.Equal(-12, persisted.EpisodeOffset);
        Assert.Equal(4, persisted.ResolvedProgress);
        Assert.NotNull(persisted.ResolutionHistoryPayload);
        Assert.Single(await fixture.Db.MediaObservationEpisodeOffsets.ToListAsync());
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

            var matchingService = new MediaObservationMatchingService(db, NullLogger<MediaObservationMatchingService>.Instance);
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
                NullLogger<MediaEpisodeIdentityService>.Instance);
            var userManager = CreateUserManager(db);

            var controller = new MediaObservationsController(
                db,
                userManager,
                matchingService,
                progressService,
                episodeIdentityService,
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

    private sealed class SearchOnlyMediaProvider(bool connected = true) : IMediaProvider
    {
        public string ProviderId => "anilist";

        public Task<IReadOnlyList<MediaProviderSearchResult>> SearchAsync(int userId, MediaCatalogSearchRequest request, CancellationToken cancellationToken)
        {
            IReadOnlyList<MediaProviderSearchResult> results =
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
        public Task<MediaProviderTitleDetails?> GetTitleDetailsAsync(int userId, string providerMediaId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MediaProviderMutationResult> UpdateProgressAsync(int userId, MediaProgressUpdateRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MediaProviderMutationResult> UpdateStatusAsync(int userId, MediaStatusUpdateRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
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
