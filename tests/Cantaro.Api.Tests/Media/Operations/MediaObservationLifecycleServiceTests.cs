using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Cantaro.Api.Tests;

public class MediaObservationLifecycleServiceTests
{
    [Fact]
    public async Task DeleteAfterDurableOutcome_PreservesSupersedingSubmission()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await AssertSupersedingSubmissionPreservedAsync(db);
    }

    [PostgresFact]
    public async Task DeleteAfterDurableOutcome_PreservesSupersedingSubmission_Postgres()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await AssertSupersedingSubmissionPreservedAsync(schema.Db);
    }

    private static async Task AssertSupersedingSubmissionPreservedAsync(ApplicationDbContext db)
    {
        var user = TestUserFactory.Create(714, "observation-owner@example.test");
        var now = DateTimeOffset.UnixEpoch.AddDays(1);
        var observation = new MediaObservation
        {
            Id = Guid.NewGuid(), UserId = user.Id, SiteIdentifier = "crunchyroll", SiteMediaId = "EPISODE2",
            ObservedTitle = "Matched submission", ObservedUrl = "https://www.crunchyroll.com/watch/EPISODE2",
            MatchStatus = MediaObservationStatuses.Matched, CreatedAt = now, UpdatedAt = now
        };
        db.AddRange(user, observation);
        await db.SaveChangesAsync();

        var options = (DbContextOptions<ApplicationDbContext>)db.GetService<IDbContextOptions>();
        await using var secondRequest = new ApplicationDbContext(options);
        var superseding = await secondRequest.MediaObservations.SingleAsync();
        superseding.UpdatedAt = now.AddSeconds(1);
        superseding.MatchStatus = MediaObservationStatuses.Ambiguous;
        superseding.ObservedTitle = "New unresolved submission";
        await secondRequest.SaveChangesAsync();

        var deleted = await new MediaObservationLifecycleService(db)
            .DeleteAfterDurableOutcomeAsync(observation, outcomeVerified: true);

        Assert.False(deleted);
        Assert.Equal(EntityState.Detached, db.Entry(observation).State);
        var remaining = await secondRequest.MediaObservations.AsNoTracking().SingleAsync();
        Assert.Equal(observation.Id, remaining.Id);
        Assert.Equal(superseding.UpdatedAt, remaining.UpdatedAt);
        Assert.Equal(MediaObservationStatuses.Ambiguous, remaining.MatchStatus);
        Assert.Equal(superseding.ObservedTitle, remaining.ObservedTitle);
    }
}
