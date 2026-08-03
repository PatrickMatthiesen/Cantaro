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

public class MediaLibraryApiTests
{
    [Fact]
    public async Task GetLibrary_ReturnsPagedItemsWithFilterAndSort()
    {
        await using var fixture = await MediaLibraryFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;

        var anime = MakeTitle("Fullmetal Alchemist: Brotherhood", MediaKinds.Anime, now);
        var manga = MakeTitle("One Piece", MediaKinds.Manga, now);
        fixture.Db.MediaTitles.AddRange(anime, manga);
        await fixture.Db.SaveChangesAsync();

        fixture.Db.MediaLibraryEntries.AddRange(
            MakeEntry(fixture.UserId, anime, MediaLibraryStatuses.Completed, now),
            MakeEntry(fixture.UserId, manga, MediaLibraryStatuses.Current, now));
        await fixture.Db.SaveChangesAsync();

        // No filter — returns both entries
        var allResult = await fixture.Controller.GetLibrary(
            status: null, mediaKind: null, provider: null, listName: null,
            sortBy: "title", sortDir: "asc",
            page: 1, pageSize: 10, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(allResult.Result);
        var page = Assert.IsType<MediaLibraryPageDto>(ok.Value);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal("Fullmetal Alchemist: Brotherhood", page.Items[0].CanonicalTitle);

        // Filter by status=completed
        var completedResult = await fixture.Controller.GetLibrary(
            status: MediaLibraryStatuses.Completed, mediaKind: null, provider: null, listName: null,
            sortBy: null, sortDir: null,
            page: 1, pageSize: 10, cancellationToken: CancellationToken.None);

        var completedOk = Assert.IsType<OkObjectResult>(completedResult.Result);
        var completedPage = Assert.IsType<MediaLibraryPageDto>(completedOk.Value);
        Assert.Equal(1, completedPage.TotalCount);
        Assert.Equal("Fullmetal Alchemist: Brotherhood", completedPage.Items[0].CanonicalTitle);
    }

    [Fact]
    public async Task GetEntry_ReturnsFullDetailIncludingProviderLinks()
    {
        await using var fixture = await MediaLibraryFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;

        var title = MakeTitle("Hunter x Hunter", MediaKinds.Anime, now);
        fixture.Db.MediaTitles.Add(title);
        await fixture.Db.SaveChangesAsync();

        fixture.Db.MediaProviderLinks.Add(new MediaProviderLink
        {
            Id = Guid.NewGuid(),
            MediaTitleId = title.Id,
            Provider = "anilist",
            ExternalId = "11061",
            ExternalUrl = "https://anilist.co/anime/11061",
            LinkSource = MediaMappingSources.Imported,
            CreatedAt = now,
            UpdatedAt = now
        });

        var entry = MakeEntry(fixture.UserId, title, MediaLibraryStatuses.Completed, now);
        entry.ProgressEpisodes = 148;
        fixture.Db.MediaLibraryEntries.Add(entry);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Controller.GetEntry(entry.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var detail = Assert.IsType<MediaLibraryEntryDetailDto>(ok.Value);

        Assert.Equal("Hunter x Hunter", detail.Title.CanonicalTitle);
        Assert.Equal(148, detail.ProgressEpisodes);
        Assert.Single(detail.ProviderLinks);
        Assert.Equal("anilist", detail.ProviderLinks[0].Provider);
        Assert.Equal("11061", detail.ProviderLinks[0].ExternalId);
    }

    [Fact]
    public async Task GetEntry_ReturnsNotFoundForMissingEntry()
    {
        await using var fixture = await MediaLibraryFixture.CreateAsync();

        var result = await fixture.Controller.GetEntry(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task LinkProvider_CreatesNewLinkAndReturnsNoContent()
    {
        await using var fixture = await MediaLibraryFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;

        var title = MakeTitle("Demon Slayer", MediaKinds.Anime, now);
        fixture.Db.MediaTitles.Add(title);
        await fixture.Db.SaveChangesAsync();

        var entry = MakeEntry(fixture.UserId, title, MediaLibraryStatuses.Current, now);
        fixture.Db.MediaLibraryEntries.Add(entry);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Controller.LinkProvider(
            entry.Id,
            new MediaLinkRequestDto { ProviderId = "anilist", ProviderMediaId = "101922" },
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);

        var link = await fixture.Db.MediaProviderLinks.SingleAsync();
        Assert.Equal("anilist", link.Provider);
        Assert.Equal("101922", link.ExternalId);
    }

    [Fact]
    public async Task LinkProvider_ReturnsConflictWhenExternalIdAlreadyLinkedToDifferentTitle()
    {
        await using var fixture = await MediaLibraryFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;

        var titleA = MakeTitle("Title A", MediaKinds.Anime, now);
        var titleB = MakeTitle("Title B", MediaKinds.Anime, now);
        fixture.Db.MediaTitles.AddRange(titleA, titleB);
        await fixture.Db.SaveChangesAsync();

        fixture.Db.MediaProviderLinks.Add(new MediaProviderLink
        {
            Id = Guid.NewGuid(),
            MediaTitleId = titleA.Id,
            Provider = "anilist",
            ExternalId = "44444",
            LinkSource = MediaMappingSources.Imported,
            MediaTitle = titleA,
            CreatedAt = now,
            UpdatedAt = now
        });

        var entryB = MakeEntry(fixture.UserId, titleB, MediaLibraryStatuses.Planned, now);
        fixture.Db.MediaLibraryEntries.Add(entryB);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Controller.LinkProvider(
            entryB.Id,
            new MediaLinkRequestDto { ProviderId = "anilist", ProviderMediaId = "44444", ForceRelink = false },
            CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var dto = Assert.IsType<MediaLinkConflictDto>(conflict.Value);
        Assert.Equal(titleA.Id, dto.ConflictingMediaTitleId);
        Assert.Contains("Title A", dto.ConflictingCanonicalTitle);
    }

    [Fact]
    public async Task LinkProvider_ForceRelinkOverridesConflict()
    {
        await using var fixture = await MediaLibraryFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;

        var titleA = MakeTitle("Title A", MediaKinds.Anime, now);
        var titleB = MakeTitle("Title B", MediaKinds.Anime, now);
        fixture.Db.MediaTitles.AddRange(titleA, titleB);
        await fixture.Db.SaveChangesAsync();

        fixture.Db.MediaProviderLinks.Add(new MediaProviderLink
        {
            Id = Guid.NewGuid(),
            MediaTitleId = titleA.Id,
            Provider = "anilist",
            ExternalId = "44444",
            LinkSource = MediaMappingSources.Imported,
            MediaTitle = titleA,
            CreatedAt = now,
            UpdatedAt = now
        });

        var entryB = MakeEntry(fixture.UserId, titleB, MediaLibraryStatuses.Planned, now);
        fixture.Db.MediaLibraryEntries.Add(entryB);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Controller.LinkProvider(
            entryB.Id,
            new MediaLinkRequestDto { ProviderId = "anilist", ProviderMediaId = "44444", ForceRelink = true },
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);

        var link = await fixture.Db.MediaProviderLinks.SingleAsync();
        Assert.Equal(titleB.Id, link.MediaTitleId);
    }

    [Fact]
    public async Task UnlinkProvider_RemovesLinkAndReturnsNoContent()
    {
        await using var fixture = await MediaLibraryFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;

        var title = MakeTitle("Neon Genesis Evangelion", MediaKinds.Anime, now);
        fixture.Db.MediaTitles.Add(title);
        await fixture.Db.SaveChangesAsync();

        fixture.Db.MediaProviderLinks.Add(new MediaProviderLink
        {
            Id = Guid.NewGuid(),
            MediaTitleId = title.Id,
            Provider = "anilist",
            ExternalId = "30",
            LinkSource = MediaMappingSources.Imported,
            CreatedAt = now,
            UpdatedAt = now
        });

        var entry = MakeEntry(fixture.UserId, title, MediaLibraryStatuses.Completed, now);
        fixture.Db.MediaLibraryEntries.Add(entry);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Controller.UnlinkProvider(entry.Id, "anilist", CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(0, await fixture.Db.MediaProviderLinks.CountAsync());
    }

    [Fact]
    public async Task LinkProvider_ReturnsBadRequestWhenProviderIdMissing()
    {
        await using var fixture = await MediaLibraryFixture.CreateAsync();

        var result = await fixture.Controller.LinkProvider(
            Guid.NewGuid(),
            new MediaLinkRequestDto { ProviderId = "  ", ProviderMediaId = "123" },
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    private sealed class MediaLibraryFixture : IAsyncDisposable
    {
        private MediaLibraryFixture(
            SqliteConnection connection,
            ApplicationDbContext db,
            MediaLibraryController controller,
            int userId)
        {
            _connection = connection;
            Db = db;
            Controller = controller;
            UserId = userId;
        }

        private readonly SqliteConnection _connection;

        public ApplicationDbContext Db { get; }
        public MediaLibraryController Controller { get; }
        public int UserId { get; }

        public static async Task<MediaLibraryFixture> CreateAsync()
        {
            const int userId = 501;
            const string email = "library.api@example.com";

            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;

            var db = new ApplicationDbContext(options);
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(TestUserFactory.Create(userId, email));
            await db.SaveChangesAsync();

            var queryService = new MediaLibraryQueryService(db);
            var linkService = new MediaLibraryLinkService(db, NullLogger<MediaLibraryLinkService>.Instance);
            var episodeIdentityService = new MediaEpisodeIdentityService(
                db,
                NullLogger<MediaEpisodeIdentityService>.Instance);
            var userManager = CreateUserManager(db);

            var controller = new MediaLibraryController(
                db,
                queryService,
                linkService,
                episodeIdentityService,
                userManager);
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

            return new MediaLibraryFixture(connection, db, controller, userId);
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
            optionsAccessor: Options.Create(new IdentityOptions()),
            passwordHasher: new PasswordHasher<User>(),
            userValidators: [],
            passwordValidators: [],
            keyNormalizer: new UpperInvariantLookupNormalizer(),
            errors: new IdentityErrorDescriber(),
            services: new ServiceCollection().BuildServiceProvider(),
            logger: NullLogger<UserManager<User>>.Instance);
    }

    private static MediaTitle MakeTitle(string name, string kind, DateTimeOffset now)
    {
        return new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = name,
            MediaKind = kind,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static MediaLibraryEntry MakeEntry(int userId, MediaTitle title, string status, DateTimeOffset now)
    {
        return new MediaLibraryEntry
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            MediaTitleId = title.Id,
            MediaTitle = title,
            Provider = "anilist",
            ProviderAccountId = $"account-{userId}",
            ProviderMediaId = Guid.NewGuid().ToString("N")[..6],
            NormalizedStatus = status,
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
