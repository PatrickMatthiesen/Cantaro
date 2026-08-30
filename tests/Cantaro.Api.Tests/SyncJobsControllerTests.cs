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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class SyncJobsControllerTests
{
    [Fact]
    public async Task List_IsUserScopedAndCursorPaged()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"sync-job-history-{Guid.NewGuid()}")
            .Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        using var userManager = CreateUserManager(db);
        var user = TestUserFactory.Create(801, "sync-history@example.com");
        var otherUser = TestUserFactory.Create(802, "other-history@example.com");
        db.Users.AddRange(user, otherUser);

        var createdAt = DateTimeOffset.UtcNow;
        var newest = CreateJob(user.Id, Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), createdAt);
        var older = CreateJob(user.Id, Guid.Parse("11111111-1111-1111-1111-111111111111"), createdAt);
        db.MusicSyncJobs.AddRange(newest, older, CreateJob(otherUser.Id, Guid.NewGuid(), createdAt.AddMinutes(1)));
        await db.SaveChangesAsync();

        var services = new ServiceCollection().BuildServiceProvider();
        var controller = new SyncJobsController(
            db,
            userManager,
            new PlatformRegistry([]),
            new MusicSyncThrottleService(),
            services.GetRequiredService<IServiceScopeFactory>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())],
                        "test"))
                }
            }
        };

        var firstResult = await controller.List(null, null, null, 1, CancellationToken.None);
        var firstPage = Assert.IsType<MusicSyncJobPageResponse>(Assert.IsType<OkObjectResult>(firstResult.Result).Value);
        Assert.Equal(newest.Id, Assert.Single(firstPage.Items).Id);
        Assert.NotNull(firstPage.NextCursor);

        var secondResult = await controller.List(null, null, firstPage.NextCursor, 1, CancellationToken.None);
        var secondPage = Assert.IsType<MusicSyncJobPageResponse>(Assert.IsType<OkObjectResult>(secondResult.Result).Value);
        Assert.Equal(older.Id, Assert.Single(secondPage.Items).Id);
        Assert.Null(secondPage.NextCursor);
    }

    [Fact]
    public void ToResponse_SanitizesLegacyStoredExceptions()
    {
        var job = CreateJob(801, Guid.NewGuid(), DateTimeOffset.UtcNow);
        job.ResultsJson = JsonSerializer.Serialize(new[]
        {
            new BatchSyncResult
            {
                ServicePlaylistId = "playlist-1",
                PlaylistName = "Road songs",
                Success = false,
                Error = "Npgsql.PostgresException: secret details"
            }
        });

        var result = Assert.Single(SyncJobsController.ToResponse(job).Results);

        Assert.Equal("sync_failed", result.ErrorCode);
        Assert.Equal("Playlist could not be imported. Check the server logs for details.", result.ErrorMessage);
        Assert.DoesNotContain("Postgres", result.ErrorMessage, StringComparison.Ordinal);
    }

    private static MusicSyncJob CreateJob(int userId, Guid id, DateTimeOffset createdAt) => new()
    {
        Id = id,
        UserId = userId,
        Service = "youtube",
        PlaylistsJson = "[]",
        Status = MusicSyncJobStatuses.Completed,
        CreatedAt = createdAt,
        UpdatedAt = createdAt
    };

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
            null!,
            NullLogger<UserManager<User>>.Instance);
    }
}
