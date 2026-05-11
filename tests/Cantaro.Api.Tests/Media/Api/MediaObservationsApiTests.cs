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
        Assert.Equal(85.5m, root.GetProperty("watchProgressPercent").GetDecimal());
        Assert.Equal(1440m, root.GetProperty("durationSeconds").GetDecimal());
        Assert.Equal(1231.2m, root.GetProperty("positionSeconds").GetDecimal());
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

        public static async Task<MediaObservationFixture> CreateAsync()
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
            var userManager = CreateUserManager(db);

            var controller = new MediaObservationsController(
                db,
                userManager,
                matchingService,
                progressService,
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
