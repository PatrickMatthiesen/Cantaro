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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class MatchingControllerTests
{
    [Fact]
    public async Task GetWorkQueue_ReportsOnlyDurableQueueRowsAndTheirOperationalState()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var user = TestUserFactory.Create(900, "matching-queue@example.com");
        db.Users.Add(user);
        var ready = AddObservation(db, "youtube", TrackMatchingStatuses.Pending, null, "Ready song");
        var scheduled = AddObservation(db, "youtube", TrackMatchingStatuses.Pending, "busy", "Scheduled song");
        var processing = AddObservation(db, "youtube", TrackMatchingStatuses.Pending, null, "Processing song");
        AddObservation(db, "youtube", TrackMatchingStatuses.Pending, null, "Not queued");
        var now = DateTime.UtcNow;
        db.TrackMatchQueueItems.AddRange(
            new TrackMatchQueueItem { TrackObservationId = ready.Id, NextAttemptAt = now.AddMinutes(-1) },
            new TrackMatchQueueItem { TrackObservationId = scheduled.Id, NextAttemptAt = now.AddMinutes(5), RetryCount = 2 },
            new TrackMatchQueueItem { TrackObservationId = processing.Id, NextAttemptAt = now.AddMinutes(-2), LeaseId = Guid.NewGuid(), LeaseExpiresAt = now.AddMinutes(8) });
        await db.SaveChangesAsync();

        using var userManager = CreateUserManager(db);
        var result = await CreateController(db, userManager, user.Id).GetWorkQueue(1, 20, CancellationToken.None);
        var page = Assert.IsType<TrackMatchWorkPageResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(1, page.ReadyCount);
        Assert.Equal(1, page.ScheduledCount);
        Assert.Equal(1, page.ProcessingCount);
        Assert.Equal("processing", page.Items[0].QueueStatus);
        Assert.DoesNotContain(page.Items, item => item.Title == "Not queued");
        Assert.Equal(2, page.Items.Single(item => item.Title == "Scheduled song").RetryCount);
    }

    [Fact]
    public async Task RetryFiltered_QueuesExactlyTheFilteredUnresolvedObservations()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var user = TestUserFactory.Create(901, "matching-review@example.com");
        db.Users.Add(user);
        var selected = AddObservation(db, "youtube", TrackMatchingStatuses.Pending, "MusicBrainz server busy", "Busy song");
        AddObservation(db, "youtube", TrackMatchingStatuses.Pending, null, "Clean song");
        AddObservation(db, "spotify", TrackMatchingStatuses.Ambiguous, "MusicBrainz server busy", "Busy song");
        AddObservation(db, "youtube", TrackMatchingStatuses.Matched, "MusicBrainz server busy", "Busy song");
        await db.SaveChangesAsync();

        using var userManager = CreateUserManager(db);
        var controller = CreateController(db, userManager, user.Id);
        var result = await controller.RetryFiltered(new MatchingQueueFilterRequest
        {
            Status = TrackMatchingStatuses.Pending,
            SourceType = "youtube",
            ErrorsOnly = true,
            Query = "busy"
        }, CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result.Result);
        Assert.Equal(1, Assert.IsType<MatchingBulkRetryResponse>(accepted.Value).QueuedCount);
        var queueItem = Assert.Single(await db.TrackMatchQueueItems.AsNoTracking().ToListAsync());
        Assert.Equal(selected.Id, queueItem.TrackObservationId);
        Assert.Equal(0, queueItem.RetryCount);
    }

    [Fact]
    public async Task ManualRetry_RearmsQueueCycleWithoutErasingLifetimeAttempts()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var user = TestUserFactory.Create(902, "matching-retry@example.com");
        db.Users.Add(user);
        var observation = AddObservation(db, "youtube", TrackMatchingStatuses.Pending, "busy", "Retry song");
        observation.MatchAttemptCount = 12;
        db.TrackMatchQueueItems.Add(new TrackMatchQueueItem
        {
            TrackObservationId = observation.Id,
            NextAttemptAt = DateTime.UtcNow.AddHours(1),
            RetryCount = 5,
            LeaseId = Guid.NewGuid(),
            LeaseExpiresAt = DateTime.UtcNow.AddMinutes(10)
        });
        await db.SaveChangesAsync();

        using var userManager = CreateUserManager(db);
        var controller = CreateController(db, userManager, user.Id);
        Assert.IsType<AcceptedResult>(await controller.Retry(observation.Id, CancellationToken.None));

        var persistedObservation = await db.TrackObservations.AsNoTracking().SingleAsync();
        var queueItem = await db.TrackMatchQueueItems.AsNoTracking().SingleAsync();
        Assert.Equal(12, persistedObservation.MatchAttemptCount);
        Assert.Equal(0, queueItem.RetryCount);
        Assert.Null(queueItem.LeaseId);
        Assert.Null(queueItem.LeaseExpiresAt);
    }

    private static TrackObservation AddObservation(
        ApplicationDbContext db,
        string source,
        string status,
        string? error,
        string title,
        Guid? trackId = null)
    {
        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(), SourceType = source, ExternalId = Guid.NewGuid().ToString("N"),
            Title = title, MatchStatus = status, LastMatchError = error, TrackId = trackId,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        db.TrackObservations.Add(observation);
        return observation;
    }

    private static MatchingController CreateController(ApplicationDbContext db, UserManager<User> manager, int userId) => new(
        db,
        manager,
        new TrackMatchingService(db, [], NullLogger<TrackMatchingService>.Instance),
        new TrackMatchQueue(db, TimeProvider.System),
        new MusicBrainzRequestGate(TimeProvider.System),
        new SongGroupingSuggestionService(db))
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "test"))
            }
        }
    };

    private static UserManager<User> CreateUserManager(ApplicationDbContext db)
    {
        var store = new UserStore<User, IdentityRole<int>, ApplicationDbContext, int>(db);
        return new UserManager<User>(store, Options.Create(new IdentityOptions()), new PasswordHasher<User>(), [], [],
            new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), null!, NullLogger<UserManager<User>>.Instance);
    }
}
