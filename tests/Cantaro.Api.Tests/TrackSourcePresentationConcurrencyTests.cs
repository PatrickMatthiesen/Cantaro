using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class TrackSourcePresentationConcurrencyTests
{
    [Fact]
    public async Task ConcurrentIndependentDimensionUpdates_PreserveBothValues()
    {
        await using var database = await FileDatabase.CreateAsync();
        await using var kindContext = database.CreateContext();
        await using var authorityContext = database.CreateContext();
        var blocker = new BlockingTimeProvider(database.Now.AddMinutes(1));
        var kindService = Service(kindContext, blocker);
        var authorityService = Service(authorityContext, new FixedTimeProvider(database.Now));

        var kindTask = Task.Run(() => kindService.ApplyAsync(
            database.SourceId,
            new TrackSourcePresentationPatch(
                TrackSourceClassificationChange.Set(Proposal("music-video", "kind:1")),
                TrackSourceClassificationChange.Unchanged())));
        blocker.WaitUntilBlocked();
        await authorityService.ApplyAsync(
            database.SourceId,
            new TrackSourcePresentationPatch(
                TrackSourceClassificationChange.Unchanged(),
                TrackSourceClassificationChange.Set(Proposal("official", "authority:1"))));
        blocker.Release();
        await kindTask;

        await using var assertContext = database.CreateContext();
        var stored = await assertContext.TrackSourceIds.AsNoTracking().SingleAsync();
        Assert.Equal(TrackSourcePresentationKinds.MusicVideo, stored.PresentationKind);
        Assert.Equal(TrackSourceUploaderAuthorities.Official, stored.UploaderAuthority);
    }

    [Fact]
    public async Task ConcurrentSameDimensionUpdates_RejectStaleWriterWithoutOverwritingWinner()
    {
        await using var database = await FileDatabase.CreateAsync();
        TrackSourceClassificationToken initialToken;
        await using (var seedContext = database.CreateContext())
        {
            var state = await Service(seedContext, new FixedTimeProvider(database.Now)).ApplyAsync(
                database.SourceId,
                KindPatch(TrackSourceClassificationChange.Set(Proposal("audio", "initial"))));
            initialToken = state.PresentationKind!.Token;
        }

        await using var losingContext = database.CreateContext();
        await using var winningContext = database.CreateContext();
        var blocker = new BlockingTimeProvider(database.Now.AddMinutes(2));
        var losingService = Service(losingContext, blocker);
        var winningService = Service(winningContext, new FixedTimeProvider(database.Now.AddMinutes(1)));
        var losingTask = Task.Run(() => losingService.ApplyAsync(
            database.SourceId,
            KindPatch(TrackSourceClassificationChange.Set(
                Proposal("visualizer", "loser"),
                initialToken))));
        blocker.WaitUntilBlocked();
        await winningService.ApplyAsync(
            database.SourceId,
            KindPatch(TrackSourceClassificationChange.Set(
                Proposal("lyric-video", "winner"),
                initialToken)));
        blocker.Release();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => losingTask);
        await using var assertContext = database.CreateContext();
        var stored = await assertContext.TrackSourceIds.AsNoTracking().SingleAsync();
        Assert.Equal(TrackSourcePresentationKinds.LyricVideo, stored.PresentationKind);
        Assert.Equal("winner", stored.PresentationKindEvidenceIdentity);
    }

    [Fact]
    public async Task TwoDimensionPatch_RollsBackFirstUpdateWhenSecondTokenIsStale()
    {
        await using var database = await FileDatabase.CreateAsync();
        TrackSourcePresentationDto initial;
        await using (var seedContext = database.CreateContext())
        {
            initial = await Service(seedContext, new FixedTimeProvider(database.Now)).ApplyAsync(
                database.SourceId,
                new TrackSourcePresentationPatch(
                    TrackSourceClassificationChange.Set(Proposal("audio", "kind:initial")),
                    TrackSourceClassificationChange.Set(Proposal("official", "authority:initial"))));
        }

        await using (var winnerContext = database.CreateContext())
        {
            await Service(winnerContext, new FixedTimeProvider(database.Now.AddMinutes(1))).ApplyAsync(
                database.SourceId,
                new TrackSourcePresentationPatch(
                    TrackSourceClassificationChange.Unchanged(),
                    TrackSourceClassificationChange.Set(
                        Proposal("user", "authority:winner"),
                        initial.UploaderAuthority!.Token)));
        }

        await using (var staleContext = database.CreateContext())
        {
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
                Service(staleContext, new FixedTimeProvider(database.Now.AddMinutes(2))).ApplyAsync(
                    database.SourceId,
                    new TrackSourcePresentationPatch(
                        TrackSourceClassificationChange.Set(
                            Proposal("visualizer", "kind:loser"),
                            initial.PresentationKind!.Token),
                        TrackSourceClassificationChange.Set(
                            Proposal("user", "authority:loser"),
                            initial.UploaderAuthority!.Token))));
        }

        await using var assertContext = database.CreateContext();
        var stored = await assertContext.TrackSourceIds.AsNoTracking().SingleAsync();
        Assert.Equal(TrackSourcePresentationKinds.Audio, stored.PresentationKind);
        Assert.Equal("kind:initial", stored.PresentationKindEvidenceIdentity);
        Assert.Equal(TrackSourceUploaderAuthorities.User, stored.UploaderAuthority);
        Assert.Equal("authority:winner", stored.UploaderAuthorityEvidenceIdentity);
    }

    private static TrackSourcePresentationClassificationService Service(
        ApplicationDbContext db,
        TimeProvider timeProvider) => new(
        db,
        new TrackSourcePresentationVocabulary(),
        timeProvider);

    private static TrackSourcePresentationPatch KindPatch(TrackSourceClassificationChange kind) => new(
        kind,
        TrackSourceClassificationChange.Unchanged());

    private static TrackSourceClassificationProposal Proposal(string value, string identity) => new(
        value,
        0.9m,
        "test",
        identity,
        "manual-review",
        "1");

    private sealed class FileDatabase : IAsyncDisposable
    {
        private FileDatabase(string path, Guid sourceId)
        {
            Path = path;
            SourceId = sourceId;
            Now = new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);
        }

        private string Path { get; }
        public Guid SourceId { get; }
        public DateTimeOffset Now { get; }

        public static async Task<FileDatabase> CreateAsync()
        {
            var database = new FileDatabase(
                System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"cantaro-presentation-concurrency-{Guid.NewGuid():N}.db"),
                Guid.NewGuid());
            await using var db = database.CreateContext();
            await db.Database.EnsureCreatedAsync();
            var track = new Track
            {
                Id = Guid.NewGuid(),
                CreatedAt = database.Now,
                UpdatedAt = database.Now
            };
            db.AddRange(
                track,
                new TrackSourceId
                {
                    Id = database.SourceId,
                    TrackId = track.Id,
                    SourceType = "youtube",
                    ExternalId = "video-1"
                });
            await db.SaveChangesAsync();
            return database;
        }

        public ApplicationDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite($"Data Source={Path};Cache=Shared;Default Timeout=10")
                .Options;
            return new ApplicationDbContext(options);
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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class BlockingTimeProvider(DateTimeOffset now) : TimeProvider
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

            return now;
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
