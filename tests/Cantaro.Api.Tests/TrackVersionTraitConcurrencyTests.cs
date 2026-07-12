using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class TrackVersionTraitConcurrencyTests
{
    [Fact]
    public async Task ConcurrentRevokes_PreserveFirstCommittedAudit()
    {
        await using var database = await FileDatabase.CreateAsync();
        var seeded = await database.SeedAssertionAsync();
        await using var losingContext = database.CreateContext();
        await using var winningContext = database.CreateContext();
        var blocker = new BlockingTimeProvider(database.Now.AddMinutes(2));
        var losingService = Service(losingContext, blocker);
        var winningService = Service(winningContext, new FixedTimeProvider(database.Now.AddMinutes(1)));

        var losingTask = Task.Run(() => losingService.RevokeAsync(
            seeded.TraitId,
            new RevokeTrackVersionTraitCommand("user", "loser", "Later revocation.")));
        blocker.WaitUntilBlocked();
        var winner = await winningService.RevokeAsync(
            seeded.TraitId,
            new RevokeTrackVersionTraitCommand("user", "winner", "First committed revocation."));
        blocker.Release();
        var loser = await losingTask;

        Assert.Equal("winner", winner?.RevokedById);
        Assert.Equal("winner", loser?.RevokedById);
        await using var assertContext = database.CreateContext();
        var stored = await assertContext.TrackVersionTraits.AsNoTracking().SingleAsync();
        Assert.Equal("winner", stored.RevokedById);
        Assert.Equal("First committed revocation.", stored.RevocationReason);
    }

    [Fact]
    public async Task ManualRevokeRacingSupersession_IsNotOverwrittenOrResurrected()
    {
        await using var database = await FileDatabase.CreateAsync();
        var seeded = await database.SeedAssertionAsync();
        await using var supersedingContext = database.CreateContext();
        await using var reviewContext = database.CreateContext();
        var blocker = new BlockingTimeProvider(database.Now.AddMinutes(2));
        var supersedingService = Service(supersedingContext, blocker);
        var reviewService = Service(reviewContext, new FixedTimeProvider(database.Now.AddMinutes(1)));

        var supersedingTask = Task.Run(() => supersedingService.AssertAsync(
            Command(seeded.TrackId, 0.7m) with { MethodVersion = "2" }));
        blocker.WaitUntilBlocked();
        await reviewService.RevokeAsync(
            seeded.TraitId,
            new RevokeTrackVersionTraitCommand("user", "reviewer", "Rejected during review."));
        blocker.Release();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => supersedingTask);
        await using var assertContext = database.CreateContext();
        var rows = await assertContext.TrackVersionTraits.AsNoTracking().ToListAsync();
        var stored = Assert.Single(rows);
        Assert.Equal("reviewer", stored.RevokedById);
        Assert.Equal("Rejected during review.", stored.RevocationReason);
        Assert.DoesNotContain(rows, row => row.RevokedAt == null);
    }

    [Fact]
    public async Task ConcurrentIdenticalAssertions_ConvergeOnOneActiveRow()
    {
        await using var database = await FileDatabase.CreateAsync();
        var trackId = await database.SeedTrackAsync();
        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var blocker = new BlockingTimeProvider(database.Now.AddMinutes(1));
        var firstService = Service(firstContext, blocker);
        var secondService = Service(secondContext, new FixedTimeProvider(database.Now));

        var firstTask = Task.Run(() => firstService.AssertAsync(Command(trackId, 0.9m)));
        blocker.WaitUntilBlocked();
        var second = await secondService.AssertAsync(Command(trackId, 0.9m));
        blocker.Release();
        var first = await firstTask;

        Assert.Equal(second.Id, first.Id);
        await using var assertContext = database.CreateContext();
        Assert.Single(await assertContext.TrackVersionTraits
            .Where(trait => trait.RevokedAt == null)
            .ToListAsync());
    }

    [Fact]
    public async Task FailedReplacementInsert_RollsBackRevocationOfOldAssertion()
    {
        await using var database = await FileDatabase.CreateAsync();
        var seeded = await database.SeedAssertionAsync();
        await using var failingContext = database.CreateContext(new FailTraitInsertInterceptor());
        var service = Service(failingContext, new FixedTimeProvider(database.Now.AddMinutes(1)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AssertAsync(
            Command(seeded.TrackId, 0.7m) with { MethodVersion = "2" }));

        await using var assertContext = database.CreateContext();
        var rows = await assertContext.TrackVersionTraits.AsNoTracking().ToListAsync();
        var stored = Assert.Single(rows);
        Assert.Equal(seeded.TraitId, stored.Id);
        Assert.Null(stored.RevokedAt);
        Assert.Null(stored.RevocationReason);
    }

    private static TrackVersionTraitEvidenceService Service(
        ApplicationDbContext db,
        TimeProvider timeProvider) => new(
        db,
        new TrackVersionTraitVocabulary(),
        timeProvider);

    private static AssertTrackVersionTraitCommand Command(Guid trackId, decimal confidence) => new(
        trackId,
        TrackVersionTraitKeys.Live,
        confidence,
        "provider-observation",
        "observation:1",
        "title-parser",
        "1",
        "system",
        "tests");

    private sealed class FileDatabase : IAsyncDisposable
    {
        private FileDatabase(string path)
        {
            Path = path;
            Now = new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);
        }

        private string Path { get; }
        public DateTimeOffset Now { get; }

        public static async Task<FileDatabase> CreateAsync()
        {
            var database = new FileDatabase(System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"cantaro-trait-concurrency-{Guid.NewGuid():N}.db"));
            await using var db = database.CreateContext();
            await db.Database.EnsureCreatedAsync();
            return database;
        }

        public ApplicationDbContext CreateContext(params IInterceptor[] interceptors)
        {
            var builder = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite($"Data Source={Path};Cache=Shared;Default Timeout=10");
            if (interceptors.Length > 0)
            {
                builder.AddInterceptors(interceptors);
            }

            var options = builder.Options;
            return new ApplicationDbContext(options);
        }

        public async Task<Guid> SeedTrackAsync()
        {
            await using var db = CreateContext();
            var track = new Track { Id = Guid.NewGuid(), CreatedAt = Now, UpdatedAt = Now };
            db.Tracks.Add(track);
            await db.SaveChangesAsync();
            return track.Id;
        }

        public async Task<(Guid TrackId, Guid TraitId)> SeedAssertionAsync()
        {
            var trackId = await SeedTrackAsync();
            await using var db = CreateContext();
            var assertion = await Service(db, new FixedTimeProvider(Now))
                .AssertAsync(Command(trackId, 0.9m));
            return (trackId, assertion.Id);
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FailTraitInsertInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<TrackVersionTrait>()
                .Any(entry => entry.State == EntityState.Added) == true)
            {
                throw new InvalidOperationException("Injected trait insertion failure.");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class BlockingTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private readonly ManualResetEventSlim _blocked = new(false);
        private readonly ManualResetEventSlim _release = new(false);
        private int _hasBlocked;

        public override DateTimeOffset GetUtcNow()
        {
            if (Interlocked.Exchange(ref _hasBlocked, 1) == 0)
            {
                _blocked.Set();
                if (!_release.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("Timed out waiting to release the coordinated writer.");
                }
            }

            return utcNow;
        }

        public void WaitUntilBlocked()
        {
            if (!_blocked.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("Timed out waiting for the coordinated writer.");
            }
        }

        public void Release() => _release.Set();
    }
}
