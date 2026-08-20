using System.Security.Claims;
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

public class MediaLibraryApiTests
{
    [Fact]
    public void CanonicalTitleAndOptionalViewerEndpoints_ArePublicWhileLibraryRequiresAuthentication()
    {
        var controllerType = typeof(MediaTitlesController);
        Assert.NotNull(controllerType.GetMethod(nameof(MediaTitlesController.GetTitle))!
            .GetCustomAttributes(typeof(AllowAnonymousAttribute), true).SingleOrDefault());
        Assert.NotNull(controllerType.GetMethod(nameof(MediaTitlesController.GetEpisodes))!
            .GetCustomAttributes(typeof(AllowAnonymousAttribute), true).SingleOrDefault());
        Assert.NotNull(controllerType.GetMethod(nameof(MediaTitlesController.GetViewerState))!
            .GetCustomAttributes(typeof(AllowAnonymousAttribute), true).SingleOrDefault());
        Assert.NotNull(typeof(MediaLibraryController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).SingleOrDefault());
    }

    [Fact]
    public async Task GetTitle_ReturnsCanonicalMediaWithoutLibraryEntry()
    {
        await using var fixture = await Fixture.CreateAsync();
        var title = fixture.MakeTitle("One Piece");
        title.Synonyms = ["ワンピース", "Wan Pīsu"];
        fixture.Db.Add(title);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Titles.GetTitle(title.Id, CancellationToken.None);

        var dto = Assert.IsType<MediaTitleDetailDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(title.Id, dto.Id);
        Assert.Equal("One Piece", dto.CanonicalTitle);
        Assert.Equal(["ワンピース", "Wan Pīsu"], dto.Synonyms);
        Assert.Empty(fixture.Db.MediaLibraryEntries);
    }

    [Fact]
    public async Task GetViewerState_ReturnsJsonNullWhenTitleIsNotInUsersLibrary()
    {
        await using var fixture = await Fixture.CreateAsync();
        var title = fixture.MakeTitle("Untracked");
        fixture.Db.Add(title);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Titles.GetViewerState(title.Id, CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result.Result);
        Assert.Equal(StatusCodes.Status200OK, json.StatusCode);
        Assert.Null(json.Value);
    }

    [Fact]
    public async Task GetViewerState_ReturnsJsonNullForAnonymousVisitor()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Titles.ControllerContext.HttpContext.User = new ClaimsPrincipal();

        var result = await fixture.Titles.GetViewerState(Guid.NewGuid(), CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result.Result);
        Assert.Equal(StatusCodes.Status200OK, json.StatusCode);
        Assert.Null(json.Value);
    }

    [Fact]
    public async Task LibraryAndViewerState_UseCanonicalMediaTitleId()
    {
        await using var fixture = await Fixture.CreateAsync();
        var title = fixture.MakeTitle("Tracked");
        var entry = new MediaLibraryEntry
        {
            Id = Guid.NewGuid(),
            UserId = fixture.UserId,
            MediaTitleId = title.Id,
            Status = MediaLibraryStatuses.Current,
            ProgressEpisodes = 3,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        fixture.Db.AddRange(title, entry);
        await fixture.Db.SaveChangesAsync();

        var viewerResult = await fixture.Titles.GetViewerState(title.Id, CancellationToken.None);
        var viewer = Assert.IsType<MediaViewerStateDto>(Assert.IsType<JsonResult>(viewerResult.Result).Value);
        var libraryResult = await fixture.Library.GetLibrary(
            null, null, null, null, null, null, cancellationToken: CancellationToken.None);
        var page = Assert.IsType<MediaLibraryPageDto>(Assert.IsType<OkObjectResult>(libraryResult.Result).Value);

        Assert.Equal(entry.Id, viewer.Id);
        Assert.Equal(title.Id, viewer.MediaTitleId);
        Assert.Equal(title.Id, Assert.Single(page.Items).MediaTitleId);
    }

    [Fact]
    public async Task AddToLibrary_CreatesOnlyViewerStateWithoutProviderBinding()
    {
        await using var fixture = await Fixture.CreateAsync();
        var title = fixture.MakeTitle("Local tracking");
        fixture.Db.Add(title);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Titles.AddToLibrary(
            title.Id,
            new MediaViewerStateCreateDto { Status = MediaLibraryStatuses.Planned },
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var state = Assert.IsType<MediaViewerStateDto>(created.Value);
        Assert.Equal(title.Id, state.MediaTitleId);
        Assert.Equal(MediaLibraryStatuses.Planned, state.Status);
        Assert.Equal(0, state.ProgressEpisodes);
        Assert.Empty(state.ProviderBindings);
        Assert.Empty(fixture.Db.MediaLibraryProviderBindings);
    }

    [Fact]
    public async Task AddToLibrary_IsIdempotentForUserAndTitle()
    {
        await using var fixture = await Fixture.CreateAsync();
        var title = fixture.MakeTitle("One state");
        fixture.Db.Add(title);
        await fixture.Db.SaveChangesAsync();
        var request = new MediaViewerStateCreateDto { Status = MediaLibraryStatuses.Current };

        await fixture.Titles.AddToLibrary(title.Id, request, CancellationToken.None);
        var second = await fixture.Titles.AddToLibrary(title.Id, request, CancellationToken.None);

        Assert.IsType<OkObjectResult>(second.Result);
        Assert.Equal(1, await fixture.Db.MediaLibraryEntries.CountAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(SqliteConnection connection, ApplicationDbContext db, UserManager<User> manager, int userId)
        {
            _connection = connection;
            Db = db;
            UserId = userId;
            var query = new MediaLibraryQueryService(db);
            Library = new MediaLibraryController(query, manager);
            Titles = new MediaTitlesController(
                db,
                query,
                new MediaEpisodeIdentityService(
                    db,
                    new MediaProviderSeasonMappingService(
                        db,
                        NullLogger<MediaProviderSeasonMappingService>.Instance),
                    NullLogger<MediaEpisodeIdentityService>.Instance),
                new MediaLibraryLinkService(db, NullLogger<MediaLibraryLinkService>.Instance),
                manager);
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test"));
            Library.ControllerContext = new() { HttpContext = new DefaultHttpContext { User = principal } };
            Titles.ControllerContext = new() { HttpContext = new DefaultHttpContext { User = principal } };
        }

        public ApplicationDbContext Db { get; }
        public MediaLibraryController Library { get; }
        public MediaTitlesController Titles { get; }
        public int UserId { get; }

        public static async Task<Fixture> CreateAsync()
        {
            const int userId = 501;
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(TestUserFactory.Create(userId, "media-api@example.test"));
            await db.SaveChangesAsync();
            return new Fixture(connection, db, CreateUserManager(db), userId);
        }

        public MediaTitle MakeTitle(string name) => new()
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = name,
            SortTitle = name,
            MediaKind = MediaKinds.Anime,
            SupportsEpisodeProgress = true,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

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
