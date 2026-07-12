using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class TrackSourcePresentationTests
{
    [Fact]
    public async Task Model_AllowsIndependentNullablePresentationDimensions()
    {
        await using var fixture = await Fixture.CreateAsync();
        var track = NewTrack();
        var kindSource = NewSource(track, "kind");
        kindSource.PresentationKind = TrackSourcePresentationKinds.MusicVideo;
        kindSource.PresentationKindConfidence = 0m;
        kindSource.PresentationKindEvidenceSource = "test";
        kindSource.PresentationKindEvidenceIdentity = "kind:1";
        kindSource.PresentationKindEvidenceMethod = "manual";
        kindSource.PresentationKindClassifiedAt = fixture.Clock.GetUtcNow();
        kindSource.PresentationKindRevision = Guid.NewGuid();
        var authoritySource = NewSource(track, "authority");
        authoritySource.UploaderAuthority = TrackSourceUploaderAuthorities.Official;
        authoritySource.UploaderAuthorityConfidence = 1m;
        authoritySource.UploaderAuthorityEvidenceSource = "test";
        authoritySource.UploaderAuthorityEvidenceIdentity = "authority:1";
        authoritySource.UploaderAuthorityEvidenceMethod = "manual";
        authoritySource.UploaderAuthorityClassifiedAt = fixture.Clock.GetUtcNow();
        authoritySource.UploaderAuthorityRevision = Guid.NewGuid();
        fixture.Db.Tracks.Add(track);
        fixture.Db.TrackSourceIds.AddRange(
            NewSource(track, "minimal"),
            kindSource,
            authoritySource);

        await fixture.Db.SaveChangesAsync();

        var sources = await fixture.Db.TrackSourceIds.AsNoTracking().ToListAsync();
        Assert.Equal(3, sources.Count);
        Assert.Single(sources, source => source.PresentationKind is not null);
        Assert.Single(sources, source => source.UploaderAuthority is not null);
    }

    [Theory]
    [InlineData(-0.0001)]
    [InlineData(1.0001)]
    public async Task Model_RejectsOutOfRangeConfidence(double confidence)
    {
        await using var fixture = await Fixture.CreateAsync();
        var track = NewTrack();
        var source = NewSource(track, "invalid");
        source.PresentationKind = TrackSourcePresentationKinds.Audio;
        source.PresentationKindConfidence = (decimal)confidence;
        source.PresentationKindEvidenceSource = "test";
        source.PresentationKindEvidenceIdentity = "invalid";
        source.PresentationKindEvidenceMethod = "manual";
        source.PresentationKindClassifiedAt = fixture.Clock.GetUtcNow();
        source.PresentationKindRevision = Guid.NewGuid();
        fixture.Db.AddRange(track, source);

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Db.SaveChangesAsync());
    }

    [Theory]
    [InlineData(-0.0001)]
    [InlineData(1.0001)]
    public async Task Model_RejectsOutOfRangeUploaderAuthorityConfidence(double confidence)
    {
        await using var fixture = await Fixture.CreateAsync();
        var track = NewTrack();
        var source = NewSource(track, "invalid-authority");
        source.UploaderAuthority = TrackSourceUploaderAuthorities.User;
        source.UploaderAuthorityConfidence = (decimal)confidence;
        source.UploaderAuthorityEvidenceSource = "test";
        source.UploaderAuthorityEvidenceIdentity = "invalid";
        source.UploaderAuthorityEvidenceMethod = "manual";
        source.UploaderAuthorityClassifiedAt = fixture.Clock.GetUtcNow();
        source.UploaderAuthorityRevision = Guid.NewGuid();
        fixture.Db.AddRange(track, source);

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task Model_RejectsPartialBundlesAndPreservesSourceIdentityUniqueness()
    {
        await using var fixture = await Fixture.CreateAsync();
        var track = NewTrack();
        var partial = NewSource(track, "partial");
        partial.PresentationKind = TrackSourcePresentationKinds.Audio;
        fixture.Db.AddRange(track, partial);
        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Db.SaveChangesAsync());
        fixture.Db.ChangeTracker.Clear();

        track = NewTrack();
        var danglingVersion = NewSource(track, "dangling-version");
        danglingVersion.UploaderAuthorityMethodVersion = "1";
        fixture.Db.AddRange(track, danglingVersion);
        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Db.SaveChangesAsync());
        fixture.Db.ChangeTracker.Clear();

        track = NewTrack();
        fixture.Db.AddRange(track, NewSource(track, "duplicate"), NewSource(track, "duplicate"));
        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Db.SaveChangesAsync());
    }

    [Theory]
    [InlineData(" Music Video ", TrackSourcePresentationKinds.MusicVideo)]
    [InlineData("cover_art_audio", TrackSourcePresentationKinds.CoverArtAudio)]
    public void Vocabulary_NormalizesPresentationKinds(string value, string expected)
    {
        var vocabulary = new TrackSourcePresentationVocabulary();

        Assert.Equal(expected, vocabulary.NormalizePresentationKind(value));
    }

    [Theory]
    [InlineData(" OFFICIAL ", TrackSourceUploaderAuthorities.Official)]
    [InlineData("user", TrackSourceUploaderAuthorities.User)]
    public void Vocabulary_NormalizesUploaderAuthority(string value, string expected)
    {
        var vocabulary = new TrackSourcePresentationVocabulary();

        Assert.Equal(expected, vocabulary.NormalizeUploaderAuthority(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => vocabulary.NormalizeUploaderAuthority("unknown"));
    }

    [Fact]
    public async Task Service_UpdatesDimensionsIndependentlyAndReplaysIdempotently()
    {
        await using var fixture = await Fixture.CreateAsync();
        var source = await fixture.SeedSourceAsync();
        var kindProposal = Proposal(" Music Video ", "kind:1");

        var kindState = await fixture.Service.ApplyAsync(
            source.Id,
            new TrackSourcePresentationPatch(
                TrackSourceClassificationChange.Set(kindProposal),
                TrackSourceClassificationChange.Unchanged()));
        var firstClassifiedAt = kindState.PresentationKind!.ClassifiedAt;
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        var replay = await fixture.Service.ApplyAsync(
            source.Id,
            new TrackSourcePresentationPatch(
                TrackSourceClassificationChange.Set(kindProposal),
                TrackSourceClassificationChange.Unchanged()));
        Assert.Equal(firstClassifiedAt, replay.PresentationKind!.ClassifiedAt);

        var authorityState = await fixture.Service.ApplyAsync(
            source.Id,
            new TrackSourcePresentationPatch(
                TrackSourceClassificationChange.Unchanged(),
                TrackSourceClassificationChange.Set(Proposal("official", "authority:1"))));

        Assert.Equal(TrackSourcePresentationKinds.MusicVideo, authorityState.PresentationKind!.Value);
        Assert.Equal(TrackSourceUploaderAuthorities.Official, authorityState.UploaderAuthority!.Value);
        var stored = await fixture.Db.TrackSourceIds.AsNoTracking().SingleAsync();
        Assert.Equal(source.TrackId, stored.TrackId);
        Assert.Equal(source.SourceType, stored.SourceType);
        Assert.Equal(source.ExternalId, stored.ExternalId);
    }

    [Fact]
    public async Task Service_RequiresCurrentTokenForReplacementAndClear()
    {
        await using var fixture = await Fixture.CreateAsync();
        var source = await fixture.SeedSourceAsync();
        var initial = await fixture.Service.ApplyAsync(
            source.Id,
            KindPatch(TrackSourceClassificationChange.Set(Proposal("audio", "kind:1"))));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => fixture.Service.ApplyAsync(
            source.Id,
            KindPatch(TrackSourceClassificationChange.Set(Proposal("visualizer", "kind:2")))));

        var replaced = await fixture.Service.ApplyAsync(
            source.Id,
            KindPatch(TrackSourceClassificationChange.Set(
                Proposal("visualizer", "kind:2"),
                initial.PresentationKind!.Token)));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => fixture.Service.ApplyAsync(
            source.Id,
            KindPatch(TrackSourceClassificationChange.Clear(initial.PresentationKind.Token))));

        var cleared = await fixture.Service.ApplyAsync(
            source.Id,
            KindPatch(TrackSourceClassificationChange.Clear(replaced.PresentationKind!.Token)));
        Assert.Null(cleared.PresentationKind);
    }

    [Fact]
    public async Task Service_RevisionRejectsStaleTokenWithSameEvidenceAndClockTick()
    {
        await using var fixture = await Fixture.CreateAsync();
        var source = await fixture.SeedSourceAsync();
        var initialProposal = Proposal("audio", "same-evidence") with { Confidence = 0.7m };
        var initial = await fixture.Service.ApplyAsync(
            source.Id,
            KindPatch(TrackSourceClassificationChange.Set(initialProposal)));

        var replacement = await fixture.Service.ApplyAsync(
            source.Id,
            KindPatch(TrackSourceClassificationChange.Set(
                initialProposal with { Confidence = 0.9m, MethodVersion = "2" },
                initial.PresentationKind!.Token)));

        Assert.Equal(initial.PresentationKind.ClassifiedAt, replacement.PresentationKind!.ClassifiedAt);
        Assert.NotEqual(initial.PresentationKind.Revision, replacement.PresentationKind.Revision);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => fixture.Service.ApplyAsync(
            source.Id,
            KindPatch(TrackSourceClassificationChange.Clear(initial.PresentationKind.Token))));
    }

    [Fact]
    public async Task Service_RejectsMissingSourceUnknownValuesAndInvalidConfidence()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => fixture.Service.ApplyAsync(
            Guid.NewGuid(),
            KindPatch(TrackSourceClassificationChange.Set(Proposal("audio", "test")))));
        var source = await fixture.SeedSourceAsync();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Service.ApplyAsync(
            source.Id,
            KindPatch(TrackSourceClassificationChange.Set(Proposal("movie", "test")))));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Service.ApplyAsync(
            source.Id,
            KindPatch(TrackSourceClassificationChange.Set(Proposal("audio", "test") with
            {
                Confidence = 1.1m
            }))));
    }

    private static TrackSourcePresentationPatch KindPatch(TrackSourceClassificationChange kind) => new(
        kind,
        TrackSourceClassificationChange.Unchanged());

    private static TrackSourceClassificationProposal Proposal(string value, string evidenceIdentity) => new(
        value,
        0.9m,
        "provider-metadata",
        evidenceIdentity,
        "manual-review",
        "1");

    private static Track NewTrack()
    {
        var now = DateTimeOffset.UtcNow;
        return new Track { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now };
    }

    private static TrackSourceId NewSource(Track track, string externalId) => new()
    {
        Id = Guid.NewGuid(),
        TrackId = track.Id,
        SourceType = "youtube",
        ExternalId = externalId
    };

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(SqliteConnection connection, ApplicationDbContext db, MutableTimeProvider clock)
        {
            _connection = connection;
            Db = db;
            Clock = clock;
            Service = new TrackSourcePresentationClassificationService(
                db,
                new TrackSourcePresentationVocabulary(),
                clock);
        }

        public ApplicationDbContext Db { get; }
        public MutableTimeProvider Clock { get; }
        public TrackSourcePresentationClassificationService Service { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new ApplicationDbContext(options);
            await db.Database.EnsureCreatedAsync();
            var clock = new MutableTimeProvider(new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero));
            return new Fixture(connection, db, clock);
        }

        public async Task<TrackSourceId> SeedSourceAsync()
        {
            var track = NewTrack();
            var source = NewSource(track, Guid.NewGuid().ToString("N"));
            Db.AddRange(track, source);
            await Db.SaveChangesAsync();
            return source;
        }

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
        public void Advance(TimeSpan amount) => _now += amount;
    }
}
