using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class TrackMatchQueueTests
{
    [Fact]
    public async Task EnqueueAsync_IsIdempotentAndBringsScheduleForward()
    {
        await using var fixture = await QueueFixture.CreateAsync();
        var observation = fixture.AddPendingObservation();
        await fixture.Db.SaveChangesAsync();

        await fixture.Queue.EnqueueAsync(observation.Id, CancellationToken.None, fixture.Now.AddMinutes(10));
        await fixture.Queue.EnqueueAsync(observation.Id, CancellationToken.None, fixture.Now.AddMinutes(2));
        await fixture.Db.SaveChangesAsync();

        var item = Assert.Single(await fixture.Db.TrackMatchQueueItems.ToListAsync());
        Assert.Equal(fixture.Now.AddMinutes(2).UtcDateTime, item.NextAttemptAt);
    }

    [Fact]
    public async Task Claim_RespectsScheduleAndLeaseToken()
    {
        await using var fixture = await QueueFixture.CreateAsync();
        var observation = fixture.AddPendingObservation();
        await fixture.Db.SaveChangesAsync();
        await fixture.Queue.EnqueueAsync(observation.Id, CancellationToken.None, fixture.Now.AddMinutes(1));
        await fixture.Db.SaveChangesAsync();

        Assert.Null(await fixture.Queue.TryClaimNextAsync(CancellationToken.None));
        fixture.Advance(TimeSpan.FromMinutes(1));
        var firstClaim = Assert.IsType<ClaimedTrackMatch>(await fixture.Queue.TryClaimNextAsync(CancellationToken.None));
        Assert.Null(await fixture.Queue.TryClaimNextAsync(CancellationToken.None));

        fixture.Advance(TimeSpan.FromMinutes(11));
        var reclaimed = Assert.IsType<ClaimedTrackMatch>(await fixture.Queue.TryClaimNextAsync(CancellationToken.None));
        Assert.NotEqual(firstClaim.LeaseId, reclaimed.LeaseId);

        await fixture.Queue.CompleteAsync(firstClaim, CancellationToken.None);
        Assert.Single(await fixture.Db.TrackMatchQueueItems.AsNoTracking().ToListAsync());
        await fixture.Queue.CompleteAsync(reclaimed, CancellationToken.None);
        Assert.Empty(await fixture.Db.TrackMatchQueueItems.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task RecoverPendingAsync_QueuesOnlyEligibleObservations()
    {
        await using var fixture = await QueueFixture.CreateAsync();
        var eligible = fixture.AddPendingObservation();
        fixture.AddPendingObservation(attemptCount: 5);
        fixture.AddPendingObservation(status: TrackMatchingStatuses.Ambiguous);
        await fixture.Db.SaveChangesAsync();

        await fixture.Queue.RecoverPendingAsync(maximumAttempts: 5, CancellationToken.None);

        var item = Assert.Single(await fixture.Db.TrackMatchQueueItems.AsNoTracking().ToListAsync());
        Assert.Equal(eligible.Id, item.TrackObservationId);
    }

    [Fact]
    public async Task DeferAsync_DoesNotConsumeRetryAttempt()
    {
        await using var fixture = await QueueFixture.CreateAsync();
        var observation = fixture.AddPendingObservation();
        await fixture.Db.SaveChangesAsync();
        await fixture.Queue.EnqueueAsync(observation.Id, CancellationToken.None);
        await fixture.Db.SaveChangesAsync();
        var claim = Assert.IsType<ClaimedTrackMatch>(await fixture.Queue.TryClaimNextAsync(CancellationToken.None));

        await fixture.Queue.DeferAsync(claim, fixture.Now.AddMinutes(5), CancellationToken.None);

        var item = Assert.Single(await fixture.Db.TrackMatchQueueItems.AsNoTracking().ToListAsync());
        Assert.Equal(0, item.RetryCount);
        Assert.Null(item.LeaseId);
        Assert.Equal(fixture.Now.AddMinutes(5).UtcDateTime, item.NextAttemptAt);
    }

    private sealed class QueueFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly MutableTimeProvider _timeProvider;

        private QueueFixture(SqliteConnection connection, ApplicationDbContext db, MutableTimeProvider timeProvider)
        {
            _connection = connection;
            Db = db;
            _timeProvider = timeProvider;
            Queue = new TrackMatchQueue(db, timeProvider);
        }

        public ApplicationDbContext Db { get; }
        public TrackMatchQueue Queue { get; }
        public DateTimeOffset Now => _timeProvider.GetUtcNow();

        public static async Task<QueueFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            return new QueueFixture(
                connection,
                db,
                new MutableTimeProvider(new DateTimeOffset(2026, 8, 30, 18, 0, 0, TimeSpan.Zero)));
        }

        public TrackObservation AddPendingObservation(
            int attemptCount = 0,
            string status = TrackMatchingStatuses.Pending)
        {
            var observation = new TrackObservation
            {
                Id = Guid.NewGuid(),
                SourceType = "youtube",
                ExternalId = Guid.NewGuid().ToString("N"),
                Title = "Queue test",
                MatchStatus = status,
                MatchAttemptCount = attemptCount,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            Db.TrackObservations.Add(observation);
            return observation;
        }

        public void Advance(TimeSpan duration) => _timeProvider.Advance(duration);

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
