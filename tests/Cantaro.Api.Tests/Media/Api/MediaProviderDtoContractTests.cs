using System.Security.Claims;
using Cantaro.Api.Controllers;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.DataProtection;
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

public class MediaProviderDtoContractTests
{
    [Fact]
    public async Task Search_MapsServiceResultsToDtoContract()
    {
        var provider = new StubMediaProvider
        {
            SearchResults =
            [
                new MediaProviderSearchResult
                {
                    ProviderId = "anilist",
                    ProviderMediaId = "140960",
                    Title = "Spy x Family",
                    NativeTitle = "SPY x FAMILY",
                    MediaKind = MediaKinds.Anime,
                    Synopsis = "A spy starts a family.",
                    PosterUrl = "https://example.test/poster.jpg",
                    BackgroundUrl = "https://example.test/banner.jpg",
                    StartYear = 2022,
                    EpisodeCount = 25,
                    PrimaryProgressDimension = MediaProgressDimensions.Episode,
                    ReleaseStatusDimension = MediaProgressDimensions.Episode,
                    RawMetadata = "{\"provider\":\"internal\"}"
                }
            ]
        };

        await using var fixture = await MediaControllerFixture.CreateAsync(provider);

        var result = await fixture.Controller.Search("anilist", "spy", null, 25, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsAssignableFrom<IReadOnlyList<MediaProviderSearchResultDto>>(ok.Value);
        var item = Assert.Single(payload);

        Assert.Equal("140960", item.ProviderMediaId);
        Assert.Equal("Spy x Family", item.Title);
        Assert.Equal(MediaProgressDimensions.Episode, item.PrimaryProgressDimension);
        Assert.Null(typeof(MediaProviderSearchResultDto).GetProperty(nameof(MediaProviderSearchResult.RawMetadata)));
    }

    [Fact]
    public async Task GetTitleDetails_MapsServiceDetailsToDtoContract()
    {
        var provider = new StubMediaProvider
        {
            TitleDetails = new MediaProviderTitleDetails
            {
                ProviderId = "anilist",
                ProviderMediaId = "140960",
                Title = "Spy x Family",
                NativeTitle = "SPY x FAMILY",
                MediaKind = MediaKinds.Anime,
                Synopsis = "A spy starts a family.",
                PosterUrl = "https://example.test/poster.jpg",
                BackgroundUrl = "https://example.test/banner.jpg",
                StartYear = 2022,
                EpisodeCount = 25,
                PrimaryProgressDimension = MediaProgressDimensions.Episode,
                ReleaseStatusDimension = MediaProgressDimensions.Episode,
                AvailabilityLinks =
                [
                    new MediaProviderAvailabilityLink
                    {
                        ServiceId = "crunchyroll",
                        DisplayName = "Crunchyroll",
                        Url = "https://www.crunchyroll.com/series/GEXH3W8XG",
                        AvailabilityKind = "streaming",
                        Notes = "Legal streaming",
                        IconUrl = "https://example.test/crunchyroll.png"
                    }
                ],
                RawMetadata = "{\"release\":\"internal\"}"
            }
        };

        await using var fixture = await MediaControllerFixture.CreateAsync(provider);

        var result = await fixture.Controller.GetTitleDetails("anilist", "140960", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<MediaProviderTitleDetailsDto>(ok.Value);

        Assert.Equal("Spy x Family", payload.Title);
        Assert.Equal("SPY x FAMILY", payload.NativeTitle);
        var availability = Assert.Single(payload.AvailabilityLinks);
        Assert.Equal("crunchyroll", availability.ServiceId);
        Assert.Equal("Crunchyroll", availability.DisplayName);
        Assert.Equal("streaming", availability.AvailabilityKind);
        Assert.Equal("https://www.crunchyroll.com/series/GEXH3W8XG", availability.Url);
        Assert.Null(typeof(MediaProviderTitleDetailsDto).GetProperty(nameof(MediaProviderTitleDetails.RawMetadata)));
    }

    [Fact]
    public async Task GetReleaseMetadata_MapsServiceMetadataToDtoContract()
    {
        var provider = new StubMediaProvider
        {
            ReleaseMetadata = new MediaReleaseMetadata
            {
                ProviderId = "anilist",
                ProviderMediaId = "140960",
                ReleaseStatusDimension = MediaProgressDimensions.Episode,
                ReleasedCount = 25,
                TotalKnownCount = 25,
                NextReleaseLabel = "Finished",
                RawMetadata = "{\"next\":\"internal\"}"
            }
        };

        await using var fixture = await MediaControllerFixture.CreateAsync(provider);

        var result = await fixture.Controller.GetReleaseMetadata("anilist", "140960", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<MediaReleaseMetadataDto>(ok.Value);

        Assert.Equal(25, payload.ReleasedCount);
        Assert.Equal("Finished", payload.NextReleaseLabel);
        Assert.Null(typeof(MediaReleaseMetadataDto).GetProperty(nameof(MediaReleaseMetadata.RawMetadata)));
    }

    [Fact]
    public async Task UpdateProgress_ReturnsNoContentAndDeletesCompletedQueueRow()
    {
        var provider = new StubMediaProvider
        {
            ProgressMutationResult = new MediaProviderMutationResult
            {
                ProviderId = "anilist",
                ProviderMediaId = "140960",
                AppliedAt = DateTimeOffset.UtcNow,
                LastRemoteUpdateAt = DateTimeOffset.UtcNow,
                RawStatus = "CURRENT"
            }
        };
        await using var fixture = await MediaControllerFixture.CreateAsync(provider);
        var entry = await SeedMediaEntryAsync(fixture);

        var result = await fixture.Controller.UpdateProgress(
            entry.Id,
            new MediaProgressUpdateDto { ProgressEpisodes = 17 },
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);

        var persistedEntry = await fixture.DbContext.MediaLibraryEntries.SingleAsync(item => item.Id == entry.Id);
        Assert.Equal(17, persistedEntry.ProgressEpisodes);
        Assert.Equal(0, await fixture.DbContext.MediaProviderOperations.CountAsync());
    }

    [Fact]
    public async Task UpdateStatus_ReturnsAcceptedWhenOperationRemainsQueued()
    {
        var provider = new StubMediaProvider
        {
            ThrowOnStatusUpdate = true
        };
        await using var fixture = await MediaControllerFixture.CreateAsync(provider);
        var entry = await SeedMediaEntryAsync(fixture);

        var result = await fixture.Controller.UpdateStatus(
            entry.Id,
            new MediaStatusUpdateDto { Status = MediaLibraryStatuses.Completed },
            CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);

        var queuedOperation = await fixture.DbContext.MediaProviderOperations.SingleAsync();
        Assert.Equal(MediaProviderOperationStatuses.Retrying, queuedOperation.Status);
        Assert.Contains("simulated", queuedOperation.LastError, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class MediaControllerFixture : IAsyncDisposable
    {
        private MediaControllerFixture(
            SqliteConnection connection,
            ApplicationDbContext dbContext,
            MediaProvidersController controller,
            int userId)
        {
            _connection = connection;
            DbContext = dbContext;
            Controller = controller;
            UserId = userId;
        }

        private readonly SqliteConnection _connection;

        public ApplicationDbContext DbContext { get; }

        public MediaProvidersController Controller { get; }

        public int UserId { get; }

        public static async Task<MediaControllerFixture> CreateAsync(StubMediaProvider provider)
        {
            const int userId = 901;
            const string email = "media.dto@example.com";

            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;

            var dbContext = new ApplicationDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            dbContext.Users.Add(TestUserFactory.Create(userId, email));
            await dbContext.SaveChangesAsync();

            var registry = new MediaProviderRegistry([provider]);
            var importService = new MediaLibraryImportService(dbContext, NullLogger<MediaLibraryImportService>.Instance);
            var operationProcessor = new MediaProviderOperationProcessor(dbContext, registry, NullLogger<MediaProviderOperationProcessor>.Instance);
            var controller = new MediaProvidersController(
                dbContext,
                registry,
                importService,
                operationProcessor,
                CreateUserManager(dbContext),
                NullLogger<MediaProvidersController>.Instance,
                new PassthroughDataProtectionProvider(),
                new StubFrontendUrlResolver());

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(
                        new ClaimsIdentity(
                            [
                                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                                new Claim(ClaimTypes.Name, email)
                            ],
                            authenticationType: "Test"))
                }
            };

            return new MediaControllerFixture(connection, dbContext, controller, userId);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class StubMediaProvider : IMediaProvider
    {
        public string ProviderId => "anilist";

        public IReadOnlyList<MediaProviderSearchResult> SearchResults { get; init; } = [];

        public MediaProviderTitleDetails? TitleDetails { get; init; }

        public MediaReleaseMetadata? ReleaseMetadata { get; init; }

        public MediaProviderMutationResult? ProgressMutationResult { get; init; }

        public MediaProviderMutationResult? StatusMutationResult { get; init; }

        public bool ThrowOnProgressUpdate { get; init; }

        public bool ThrowOnStatusUpdate { get; init; }

        public Task<ConnectedServiceAccount?> GetConnectedAccountAsync(int userId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public string GetAuthorizationUrl(string redirectUri, string state, string codeChallenge)
        {
            throw new NotSupportedException();
        }

        public Task<ConnectedServiceAccount> ExchangeCodeAndSaveAsync(
            int userId,
            string authorizationCode,
            string redirectUri,
            string codeVerifier,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task DisconnectAsync(int userId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<MediaProviderLibraryImportResult> ImportLibraryAsync(int userId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<MediaProviderSearchResult>> SearchAsync(int userId, MediaCatalogSearchRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(SearchResults);
        }

        public Task<MediaProviderTitleDetails?> GetTitleDetailsAsync(int userId, string providerMediaId, CancellationToken cancellationToken)
        {
            return Task.FromResult(TitleDetails);
        }

        public Task<MediaProviderMutationResult> UpdateProgressAsync(int userId, MediaProgressUpdateRequest request, CancellationToken cancellationToken)
        {
            if (ThrowOnProgressUpdate)
            {
                throw new InvalidOperationException("Simulated provider write failure.");
            }

            return Task.FromResult(ProgressMutationResult ?? new MediaProviderMutationResult
            {
                ProviderId = ProviderId,
                ProviderMediaId = request.ProviderMediaId,
                AppliedAt = DateTimeOffset.UtcNow,
                LastRemoteUpdateAt = DateTimeOffset.UtcNow
            });
        }

        public Task<MediaProviderMutationResult> UpdateStatusAsync(int userId, MediaStatusUpdateRequest request, CancellationToken cancellationToken)
        {
            if (ThrowOnStatusUpdate)
            {
                throw new InvalidOperationException("Simulated provider write failure.");
            }

            return Task.FromResult(StatusMutationResult ?? new MediaProviderMutationResult
            {
                ProviderId = ProviderId,
                ProviderMediaId = request.ProviderMediaId,
                AppliedAt = DateTimeOffset.UtcNow,
                LastRemoteUpdateAt = DateTimeOffset.UtcNow,
                RawStatus = request.Status.ToUpperInvariant()
            });
        }

        public Task<MediaReleaseMetadata?> GetReleaseMetadataAsync(int userId, string providerMediaId, CancellationToken cancellationToken)
        {
            return Task.FromResult(ReleaseMetadata);
        }
    }

    private sealed class PassthroughDataProtectionProvider : IDataProtectionProvider
    {
        public IDataProtector CreateProtector(string purpose)
        {
            return new PassthroughDataProtector();
        }
    }

    private sealed class PassthroughDataProtector : IDataProtector
    {
        public IDataProtector CreateProtector(string purpose)
        {
            return this;
        }

        public byte[] Protect(byte[] plaintext)
        {
            return plaintext;
        }

        public byte[] Unprotect(byte[] protectedData)
        {
            return protectedData;
        }
    }

    private sealed class StubFrontendUrlResolver : IFrontendUrlResolver
    {
        public string GetFrontendUrl()
        {
            return "https://frontend.test";
        }

        public string GetCurrentRequestBaseUrl()
        {
            return "https://api.test";
        }

        public string GetCallbackUrl(string relativePath)
        {
            return $"https://api.test/{relativePath.TrimStart('/')}";
        }
    }

    private static UserManager<User> CreateUserManager(ApplicationDbContext dbContext)
    {
        var store = new UserStore<User, IdentityRole<int>, ApplicationDbContext, int>(dbContext);
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

    private static async Task<MediaLibraryEntry> SeedMediaEntryAsync(MediaControllerFixture fixture)
    {
        var now = DateTimeOffset.UtcNow;
        var account = new ConnectedServiceAccount
        {
            Id = 1901,
            UserId = fixture.UserId,
            Service = "anilist",
            ExternalAccountId = "viewer-901",
            DisplayName = "Queue Tester",
            CreatedAt = now.UtcDateTime,
            UpdatedAt = now.UtcDateTime
        };
        var title = new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = "Spy x Family",
            MediaKind = MediaKinds.Anime,
            SupportsEpisodeProgress = true,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            CreatedAt = now,
            UpdatedAt = now
        };
        var entry = new MediaLibraryEntry
        {
            Id = Guid.NewGuid(),
            UserId = fixture.UserId,
            MediaTitleId = title.Id,
            ConnectedServiceAccountId = account.Id,
            Provider = "anilist",
            ProviderAccountId = account.ExternalAccountId,
            ProviderMediaId = "140960",
            NormalizedStatus = MediaLibraryStatuses.Current,
            ProgressEpisodes = 12,
            LastRemoteUpdateAt = now.AddMinutes(-2),
            CreatedAt = now,
            UpdatedAt = now
        };

        fixture.DbContext.ConnectedServiceAccounts.Add(account);
        fixture.DbContext.MediaTitles.Add(title);
        fixture.DbContext.MediaLibraryEntries.Add(entry);
        await fixture.DbContext.SaveChangesAsync();

        return entry;
    }
}
