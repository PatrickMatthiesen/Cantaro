using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class TrackVersionTraitEvidenceServiceTests
{
    [Theory]
    [InlineData(" LIVE ", TrackVersionTraitKeys.Live)]
    [InlineData("A Cappella", TrackVersionTraitKeys.ACappella)]
    [InlineData("a_cappella", TrackVersionTraitKeys.ACappella)]
    [InlineData("ORCHESTRAL", TrackVersionTraitKeys.Orchestral)]
    public void Vocabulary_NormalizesKnownKeys(string value, string expected)
    {
        var vocabulary = new TrackVersionTraitVocabulary();

        Assert.Equal(expected, vocabulary.Normalize(value));
    }

    [Fact]
    public void Vocabulary_RejectsUnknownKeysAndKeepsCompatibilityAdvisory()
    {
        var vocabulary = new TrackVersionTraitVocabulary();

        Assert.Throws<ArgumentOutOfRangeException>(() => vocabulary.Normalize("invented"));
        Assert.True(vocabulary.ArePotentiallyConflicting(
            TrackVersionTraitKeys.Original,
            TrackVersionTraitKeys.Remix));
        Assert.False(vocabulary.ArePotentiallyConflicting(
            TrackVersionTraitKeys.Live,
            TrackVersionTraitKeys.Acoustic));
    }

    [Fact]
    public async Task Assert_IsIdempotent_AllowsCombinedTraits_AndDoesNotChangeSongMembership()
    {
        await using var fixture = await Fixture.CreateAsync();
        var songId = Guid.NewGuid();
        var track = NewTrack(songId);
        fixture.Db.Songs.Add(new Song
        {
            Id = songId,
            CreatedAt = fixture.Clock.GetUtcNow(),
            UpdatedAt = fixture.Clock.GetUtcNow()
        });
        fixture.Db.Tracks.Add(track);
        await fixture.Db.SaveChangesAsync();

        var live = await fixture.Service.AssertAsync(Command(track.Id, " LIVE ", 0.8m, "observation:1"));
        var replay = await fixture.Service.AssertAsync(Command(track.Id, TrackVersionTraitKeys.Live, 0.8m, "observation:1"));
        await fixture.Service.AssertAsync(Command(track.Id, TrackVersionTraitKeys.Acoustic, 0.9m, "observation:1"));

        Assert.Equal(live.Id, replay.Id);
        Assert.Equal(2, await fixture.Db.TrackVersionTraits.CountAsync());
        Assert.Equal(songId, (await fixture.Db.Tracks.AsNoTracking().SingleAsync()).SongId);
    }

    [Fact]
    public async Task Assert_ChangedEvidenceSupersedesOldAssertionTransactionally()
    {
        await using var fixture = await Fixture.CreateAsync();
        var track = NewTrack();
        fixture.Db.Tracks.Add(track);
        await fixture.Db.SaveChangesAsync();
        var original = await fixture.Service.AssertAsync(Command(track.Id, TrackVersionTraitKeys.Live, 0.7m, "observation:1"));
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));

        var replacement = await fixture.Service.AssertAsync(
            Command(track.Id, TrackVersionTraitKeys.Live, 0.9m, "observation:1") with
            {
                MethodVersion = "2"
            });

        fixture.Db.ChangeTracker.Clear();
        var rows = (await fixture.Db.TrackVersionTraits.ToListAsync())
            .OrderBy(x => x.CreatedAt)
            .ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(original.Id, replacement.SupersedesTraitId);
        Assert.NotNull(rows[0].RevokedAt);
        Assert.Equal("superseded by updated evidence.", rows[0].RevocationReason, ignoreCase: true);
        Assert.Null(rows[1].RevokedAt);
    }

    [Fact]
    public async Task ReadsRetainHistoryAndFlagConflictingActiveEvidenceWithoutRejectingIt()
    {
        await using var fixture = await Fixture.CreateAsync();
        var track = NewTrack();
        fixture.Db.Tracks.Add(track);
        await fixture.Db.SaveChangesAsync();
        var original = await fixture.Service.AssertAsync(
            Command(track.Id, TrackVersionTraitKeys.Original, 0.8m, "manual:1"));
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        var remix = await fixture.Service.AssertAsync(
            Command(track.Id, TrackVersionTraitKeys.Remix, 0.7m, "parser:1"));

        var active = await fixture.Service.GetActiveAsync(track.Id);
        Assert.Equal(2, active.Count);
        Assert.All(active, assertion => Assert.True(assertion.HasPotentialConflict));

        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        await fixture.Service.RevokeAsync(
            remix.Id,
            new RevokeTrackVersionTraitCommand("user", "user:1", "Rejected during review."));

        active = await fixture.Service.GetActiveAsync(track.Id);
        Assert.Single(active);
        Assert.Equal(original.Id, active[0].Id);
        Assert.False(active[0].HasPotentialConflict);
        var history = await fixture.Service.GetHistoryAsync(track.Id);
        Assert.Equal(2, history.Count);
        Assert.False(history[0].IsActive);
        Assert.Equal("Rejected during review.", history[0].RevocationReason);
    }

    [Fact]
    public async Task Assert_RejectsMissingTrackUnknownTraitAndOutOfRangeConfidence()
    {
        await using var fixture = await Fixture.CreateAsync();
        var missingTrackId = Guid.NewGuid();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => fixture.Service.AssertAsync(
            Command(missingTrackId, TrackVersionTraitKeys.Live, 0.8m, "test")));

        var track = NewTrack();
        fixture.Db.Tracks.Add(track);
        await fixture.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Service.AssertAsync(
            Command(track.Id, "invented", 0.8m, "test")));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Service.AssertAsync(
            Command(track.Id, TrackVersionTraitKeys.Live, 1.1m, "test")));
    }

    private static AssertTrackVersionTraitCommand Command(
        Guid trackId,
        string traitKey,
        decimal confidence,
        string evidenceIdentity) => new(
        trackId,
        traitKey,
        confidence,
        "provider-observation",
        evidenceIdentity,
        "title-parser",
        "1",
        "system",
        "tests");

    private static Track NewTrack(Guid? songId = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new Track
        {
            Id = Guid.NewGuid(),
            SongId = songId,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(
            SqliteConnection connection,
            ApplicationDbContext db,
            MutableTimeProvider clock)
        {
            _connection = connection;
            Db = db;
            Clock = clock;
            Service = new TrackVersionTraitEvidenceService(
                db,
                new TrackVersionTraitVocabulary(),
                clock);
        }

        public ApplicationDbContext Db { get; }
        public MutableTimeProvider Clock { get; }
        public TrackVersionTraitEvidenceService Service { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new ApplicationDbContext(options);
            await db.Database.EnsureCreatedAsync();
            return new Fixture(
                connection,
                db,
                new MutableTimeProvider(new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero)));
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; private set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;

        public void Advance(TimeSpan amount) => UtcNow += amount;
    }
}
