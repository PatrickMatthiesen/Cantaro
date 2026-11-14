using System.Data.Common;
using System.Text.Json;
using Cantaro.Api.Configuration;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Cantaro.Api.Services.Lyrics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class CandidateVersionAnchorTests
{
    private static TrackMatchingService Service(ApplicationDbContext db) => new(db, [], NullLogger<TrackMatchingService>.Instance);
    private static (Guid Observation, Guid Candidate, Guid Playlist) Add(ApplicationDbContext db, int owner = 42, bool isPublic = false)
    {
        if (!db.Users.Local.Any(u => u.Id == owner) && !db.Users.Any(u => u.Id == owner)) db.Users.Add(TestUserFactory.Create(owner, $"anchor-{owner}@example.test"));
        var playlist = new Playlist { Id = Guid.NewGuid(), UserId = owner, Name = "Versions" };
        var observation = new TrackObservation { Id = Guid.NewGuid(), OwnerUserId = owner,
            SourceType = "youtube", ExternalId = Guid.NewGuid().ToString(), InputKind = ObservationInputKind.Private,
            Title = "Song sped up", Artist = "Artist", DurationSeconds = 120, UpdatedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow, MatchStatus = TrackMatchingStatuses.Ambiguous };
        var candidate = new TrackResolutionCandidate { Id = Guid.NewGuid(), TrackObservationId = observation.Id,
            CandidateSource = "spotify", ExternalId = "original", Title = "Original melody", Artist = "Original artist", DurationSeconds = 180 };
        if (isPublic)
        {
            var item = db.ProviderCatalogItems.Local.FirstOrDefault(i => i.ExternalId == "original");
            if (item is null)
            {
                item = new ProviderCatalogItem { Id = Guid.NewGuid(), SourceType = "spotify", ExternalId = "original", Revision = 1,
                    FactsJson = "{}" };
                CatalogFixture.Verify(item, new ProviderCatalogFacts { Title = "Original melody", Artist = "Original artist",
                    DurationSeconds = 180, ThumbnailUrl = "https://example.test/art", Albums = ["Album", "Second album"],
                    ArtistCredits = [new ArtistCreditMetadata { Name = "Original artist" }] });
                item.TrackId = null;
                item.AssociationState = RecordingAssociationState.Unknown;
                db.Add(item);
            }
            candidate.ProviderCatalogItemId = item.Id; candidate.ProviderCatalogRevision = item.Revision;
            candidate.Title = null; candidate.Artist = null; candidate.DurationSeconds = null;
        }
        db.AddRange(playlist, observation, candidate, new PlaylistEntry { Id = Guid.NewGuid(), OwnerUserId = owner,
            PlaylistId = playlist.Id, TrackObservationId = observation.Id, Position = 0 });
        return (observation.Id, candidate.Id, playlist.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SearchDisplaysTheInputThatMatched(bool isPublic)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        await AssertSearchDisplayAsync(db, isPublic);
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostgresSearchDisplaysTheInputThatMatched(bool isPublic)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await AssertSearchDisplayAsync(schema.Db, isPublic);
    }

    private static async Task AssertSearchDisplayAsync(ApplicationDbContext db, bool isPublic)
    {
        var choice = Add(db, isPublic: isPublic);
        await db.SaveChangesAsync();
        await Service(db).AcceptCandidateAsVersionAsync(choice.Observation, choice.Candidate, TrackVersionFlags.Live, default);
        var original = (await db.TrackResolutionCandidates.SingleAsync(c => c.Id == choice.Candidate)).AnchorTrackId!.Value;
        var alternate = new ProviderCatalogItem
        { Id = Guid.NewGuid(), SourceType = "musicbrainz", ExternalId = "alternate", TrackId = original, Revision = 1, FactsJson = "{}" };
        CatalogFixture.Verify(alternate, new ProviderCatalogFacts
        { Title = "Alternate title", Artist = "Alternate artist", ThumbnailUrl = "https://example.test/alternate", Albums = ["Alternate album"] });
        db.ProviderCatalogItems.Add(alternate);
        await db.SaveChangesAsync();
        await new MusicLibraryQueryService(db).AddCanonicalSongToPlaylistAsync(original, choice.Playlist, 42, default);
        db.ChangeTracker.Clear();
        using var cache = new MediaProviderSearchCache();
        var search = new CantaroSearchService(db, new MediaProviderRegistry([]), cache, null!, NullLogger<CantaroSearchService>.Instance);
        foreach (var query in new[] { "Original melody", "melody Original", "Alternate title" })
        {
            var result = await search.SearchAsync(42, query, 1, false, default);
            var displayed = Assert.Single(result.Groups.Songs.Items);
            var sharedWins = query == "Alternate title";
            Assert.Equal($"track:{original}", displayed.Id);
            Assert.Equal(sharedWins ? "Alternate title" : "Original melody", displayed.Title);
            Assert.Equal(sharedWins ? "Alternate artist" : "Original artist", displayed.Subtitle);
            Assert.Equal(sharedWins ? "https://example.test/alternate" : isPublic ? "https://example.test/art" : null, displayed.ArtworkUrl);
            Assert.Equal(sharedWins ? "Alternate album" : isPublic ? "Album" : null, displayed.Detail);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcceptedOriginalSurvivesFreshReadsAndActualPlaylistUse(bool isPublic)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        await AssertUsableAsync(db, isPublic);
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostgresAcceptedOriginalSurvivesFreshReadsAndActualPlaylistUse(bool isPublic)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await AssertUsableAsync(schema.Db, isPublic);
    }

    private static async Task AssertUsableAsync(ApplicationDbContext db, bool isPublic)
    {
        var first = Add(db, isPublic: isPublic);
        var second = Add(db, isPublic: isPublic);
        await db.SaveChangesAsync();
        var version = await Service(db).AcceptCandidateAsVersionAsync(first.Observation, first.Candidate, TrackVersionFlags.SpedUp, default);
        db.ChangeTracker.Clear();
        var originalId = (await db.TrackResolutionCandidates.SingleAsync(c => c.Id == first.Candidate)).AnchorTrackId!.Value;
        var library = new MusicLibraryQueryService(db);
        var detail = await library.GetCanonicalSongAsync(originalId, 42, default);
        Assert.Equal("Original melody", detail?.Title);
        Assert.Equal("Original artist", detail?.Artist);
        Assert.Equal(180, detail?.DurationSeconds);
        if (isPublic) { Assert.Equal(new[] { "Album", "Second album" }, detail!.Albums); Assert.Single(detail.ArtistCredits); }
        var related = await library.GetCanonicalSongAsync(version.TrackId!.Value, 42, default);
        Assert.Equal("Original melody", Assert.Single(related!.RelatedRecordings).Title);
        Assert.False(Assert.Single(related.RelatedRecordings).InLibrary);
        await Service(db).AcceptCandidateAsVersionAsync(second.Observation, second.Candidate, TrackVersionFlags.Live, default);
        Assert.Equal(3, await db.Tracks.CountAsync()); Assert.Equal(1, await db.Songs.CountAsync());
        Assert.Equal(originalId, (await db.TrackResolutionCandidates.SingleAsync(c => c.Id == second.Candidate)).AnchorTrackId);
        Assert.True(await library.AddCanonicalSongToPlaylistAsync(originalId, first.Playlist, 42, default));
        db.ChangeTracker.Clear();
        Assert.Contains((await library.GetLibraryAsync(42, default)).Songs, s => s.Id == $"track:{originalId}" && s.Title == "Original melody");
        var lyrics = new CapturingLyrics();
        await new LyricsService(db, lyrics).GetLyricsAsync(42, originalId, default);
        Assert.Equal(originalId, lyrics.Lookup?.TrackId); Assert.Equal("Original melody", lyrics.Lookup?.Title);
        Assert.Equal(180, lyrics.Lookup?.DurationSeconds);
        using var cache = new MediaProviderSearchCache();
        var search = new CantaroSearchService(db, new MediaProviderRegistry([]), cache, null!, NullLogger<CantaroSearchService>.Instance);
        var results = await search.SearchAsync(42, "Original melody", 1, false, default);
        Assert.Equal(SearchGroupStatuses.Ok, results.Groups.Songs.Status);
        Assert.Equal($"track:{originalId}", Assert.Single(results.Groups.Songs.Items).Id);
        var entry = await db.PlaylistEntries.SingleAsync(e => e.PlaylistId == first.Playlist && e.TrackId == originalId);
        var calls = 0;
        var destination = await PlaylistDestinationIdentityResolver.ResolveAsync(db, new TrackMatchingOptions(),
            (_, _, _) => throw new Exception("Already materialized"), (_, _, _) => throw new Exception("Already reconciled"),
            new PlatformAccountContext(42, 7), "spotify", entry, (input, _) =>
            {
                calls++; Assert.Equal("Original melody", input.Title); Assert.Equal(180, input.DurationSeconds);
                return Task.FromResult<IReadOnlyList<TrackMatchSearchCandidate>>([new() { CandidateSource = "spotify", ExternalId = "searched",
                    Title = "Original melody", Artist = "Original artist", DurationSeconds = 180 }]);
            }, default);
        Assert.Equal("searched", destination); Assert.Equal(1, calls);
        Assert.All(await db.ProviderCatalogItems.ToListAsync(), item => Assert.Null(item.TrackId));
        Assert.All(await db.Tracks.ToListAsync(), track => { Assert.Null(track.Isrc); Assert.Null(track.MbidRecording); });
        if (isPublic) Assert.All(await db.TrackResolutionCandidates.ToListAsync(), c => { Assert.Null(c.Title); Assert.Null(c.Artist); });
        // An identical completed request acknowledges the stored result even after facts expire.
        if (isPublic) { (await db.ProviderCatalogItems.SingleAsync()).RevalidateAfter = DateTimeOffset.UtcNow.AddDays(-1); await db.SaveChangesAsync(); }
        db.ChangeTracker.Clear();
        Assert.Equal(version.TrackId, (await Service(db).AcceptCandidateAsVersionAsync(first.Observation, first.Candidate, TrackVersionFlags.SpedUp, default)).TrackId);
        if (isPublic)
        {
            Assert.Empty(await OwnedCandidateAnchorInput.LoadAsync(db, 42, [originalId], default));
            var staleSearch = await search.SearchAsync(42, "Original melody", 1, false, default);
            Assert.Empty(staleSearch.Groups.Songs.Items);
            var staleLyrics = new CapturingLyrics(); await new LyricsService(db, staleLyrics).GetLyricsAsync(42, originalId, default);
            Assert.Null(staleLyrics.Lookup);
            var denied = await PlaylistDestinationIdentityResolver.ResolveAsync(db, new TrackMatchingOptions(),
                (_, _, _) => throw new Exception(), (_, _, _) => throw new Exception(), new PlatformAccountContext(42, 8), "youtube", entry,
                (_, _) => throw new Exception("Expired facts must not search"), default);
            Assert.Null(denied);
        }
        Assert.Empty(await OwnedCandidateAnchorInput.LoadAsync(db, 84, [originalId], default));
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SerializableAcceptsRequireFreshRetry(bool sameObservation)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        var first = Add(schema.Db);
        var second = sameObservation ? first : Add(schema.Db);
        await schema.Db.SaveChangesAsync();
        var barrier = new ReadBarrier(sameObservation ? "FROM \"TrackObservations\"" : "FROM \"TrackResolutionCandidates\"");
        DbContextOptions<ApplicationDbContext> Options() => new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(schema.Db.Database.GetConnectionString(), o => o.EnableRetryOnFailure(1)).AddInterceptors(barrier).Options;
        await using var a = new ApplicationDbContext(Options());
        await using var b = new ApplicationDbContext(Options());
        async Task<Exception?> Accept(ApplicationDbContext db, (Guid Observation, Guid Candidate, Guid Playlist) choice)
        {
            try { await Service(db).AcceptCandidateAsVersionAsync(choice.Observation, choice.Candidate, TrackVersionFlags.SpedUp, default); return null; }
            catch (Exception e) { return e; }
        }
        var outcomes = await Task.WhenAll(Accept(a, first), Accept(b, second));
        Assert.Single(outcomes, e => e is null);
        Assert.IsType<InvalidOperationException>(Assert.Single(outcomes, e => e is not null));
        await using var fresh = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(schema.Db.Database.GetConnectionString()).Options);
        var retry = outcomes[0] is null ? second : first;
        await Service(fresh).AcceptCandidateAsVersionAsync(retry.Observation, retry.Candidate, TrackVersionFlags.SpedUp, default);
        Assert.Equal(sameObservation ? 2 : 3, await fresh.Tracks.CountAsync());
        Assert.Equal(1, await fresh.Songs.CountAsync());
        Assert.Single(await fresh.TrackResolutionCandidates.Where(c => c.AnchorTrackId != null).Select(c => c.AnchorTrackId).Distinct().ToListAsync());
    }

    private sealed class ReadBarrier(string marker) : DbCommandInterceptor
    {
        private int _arrivals;
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data,
            DbDataReader result, CancellationToken ct = default)
        {
            if (command.CommandText.Contains(marker, StringComparison.Ordinal) && command.CommandText.StartsWith("SELECT", StringComparison.Ordinal))
            {
                var arrival = Interlocked.Increment(ref _arrivals);
                if (arrival == 2) _ready.TrySetResult();
                if (arrival <= 2) await _ready.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
            }
            return result;
        }
    }

    [PostgresFact]
    public async Task FailureRollsBackGraphClaimAndPlaylistAndDirtyCallerIsPreserved()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        var choice = Add(schema.Db);
        await schema.Db.SaveChangesAsync();
        schema.Db.ChangeTracker.Clear();
        var originalUpdated = (await schema.Db.TrackObservations.SingleAsync()).UpdatedAt;
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(schema.Db.Database.GetConnectionString())
            .AddInterceptors(new FailGraphSave()).Options;
        await using (var failing = new ApplicationDbContext(options))
        {
            await Assert.ThrowsAsync<InjectedFailure>(() => Service(failing).AcceptCandidateAsVersionAsync(choice.Observation, choice.Candidate, TrackVersionFlags.Live, default));
            Assert.Empty(failing.ChangeTracker.Entries());
        }
        schema.Db.ChangeTracker.Clear();
        Assert.Empty(await schema.Db.Tracks.ToListAsync()); Assert.Empty(await schema.Db.Songs.ToListAsync());
        Assert.Null((await schema.Db.PlaylistEntries.SingleAsync()).TrackId);
        var unchanged = await schema.Db.TrackObservations.SingleAsync();
        Assert.Null(unchanged.TrackId); Assert.Null(unchanged.AcceptedCandidateId); Assert.Equal(originalUpdated, unchanged.UpdatedAt);
        Assert.False((await schema.Db.TrackResolutionCandidates.SingleAsync()).IsAccepted);
        unchanged.Title = "Pending caller edit";
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(schema.Db).AcceptCandidateAsVersionAsync(choice.Observation, choice.Candidate, TrackVersionFlags.Live, default));
        Assert.Equal(EntityState.Modified, schema.Db.Entry(unchanged).State); Assert.Equal("Pending caller edit", unchanged.Title);
        schema.Db.ChangeTracker.Clear();
        await using (var transaction = await schema.Db.Database.BeginTransactionAsync())
            await Assert.ThrowsAsync<InvalidOperationException>(() => Service(schema.Db).AcceptCandidateAsVersionAsync(choice.Observation, choice.Candidate, TrackVersionFlags.Live, default));
        await Service(schema.Db).AcceptCandidateAsVersionAsync(choice.Observation, choice.Candidate, TrackVersionFlags.Live, default);
        Assert.Equal(2, await schema.Db.Tracks.CountAsync());
    }
    private sealed class InjectedFailure : Exception;
    private sealed class FailGraphSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (data.Context!.ChangeTracker.Entries<Track>().Any(e => e.State == EntityState.Added)) throw new InjectedFailure();
            return ValueTask.FromResult(result);
        }
    }

    [PostgresTheory]
    [InlineData("expired")]
    [InlineData("withdrawn")]
    [InlineData("revision")]
    public async Task ChangedPublicCandidateCannotDisplayOrGrantNewReuse(string invalidation)
    {
        await using var schema = await PostgresTestSchema.CreateAsync(); var db = schema.Db;
        var first = Add(db, isPublic: true); var second = Add(db, isPublic: true);
        await db.SaveChangesAsync();
        await Service(db).AcceptCandidateAsVersionAsync(first.Observation, first.Candidate, TrackVersionFlags.Live, default);
        var original = (await db.TrackResolutionCandidates.SingleAsync(c => c.Id == first.Candidate)).AnchorTrackId!.Value;
        var item = await db.ProviderCatalogItems.SingleAsync();
        if (invalidation == "expired") item.RevalidateAfter = DateTimeOffset.UtcNow.AddMinutes(-1);
        if (invalidation == "withdrawn") item.VisibilityState = CatalogVisibility.Withdrawn;
        if (invalidation == "revision") item.Revision++;
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.Empty(await OwnedCandidateAnchorInput.LoadAsync(db, 42, [original], default));
        Assert.Equal("Unknown song", (await new MusicLibraryQueryService(db).GetCanonicalSongAsync(original, 42, default))?.Title);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).AcceptCandidateAsVersionAsync(second.Observation, second.Candidate, TrackVersionFlags.Live, default));
        Assert.Equal(2, await db.Tracks.CountAsync()); Assert.Equal(1, await db.Songs.CountAsync());
        Assert.NotNull(await Service(db).AcceptCandidateAsVersionAsync(first.Observation, first.Candidate, TrackVersionFlags.Live, default));
    }

    [PostgresFact]
    public async Task PrivateReferencesStayOwnerScopedAndUnusableEarlierChoiceCannotHideValidDisplay()
    {
        await using var schema = await PostgresTestSchema.CreateAsync(); var db = schema.Db;
        var first = Add(db); var other = Add(db, 84); var fresh = Add(db, isPublic: true);
        await db.SaveChangesAsync();
        var result = await Service(db).AcceptCandidateAsVersionAsync(first.Observation, first.Candidate, TrackVersionFlags.Live, default);
        var original = (await db.TrackResolutionCandidates.SingleAsync(c => c.Id == first.Candidate)).AnchorTrackId!.Value;
        await Service(db).AcceptCandidateAsVersionAsync(fresh.Observation, fresh.Candidate, TrackVersionFlags.Live, default);
        var expired = await db.TrackResolutionCandidates.SingleAsync(c => c.Id == fresh.Candidate);
        Assert.Equal(original, expired.AnchorTrackId);
        (await db.ProviderCatalogItems.SingleAsync()).RevalidateAfter = DateTimeOffset.UtcNow.AddDays(-1);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.Equal("Original melody", (await OwnedCandidateAnchorInput.LoadAsync(db, 42, [original], default))[original].Title);
        Assert.True(await new MusicLibraryQueryService(db).AddCanonicalSongToPlaylistAsync(original, other.Playlist, 84, default));
        Assert.Equal("Unknown song", (await new MusicLibraryQueryService(db).GetCanonicalSongAsync(original, 84, default))?.Title);
        var lyrics = new CapturingLyrics(); await new LyricsService(db, lyrics).GetLyricsAsync(84, original, default); Assert.Null(lyrics.Lookup);
        await Service(db).AcceptCandidateAsVersionAsync(other.Observation, other.Candidate, TrackVersionFlags.Live, default);
        Assert.NotEqual(original, (await db.TrackResolutionCandidates.SingleAsync(c => c.Id == other.Candidate)).AnchorTrackId);
        Assert.Equal(2, await db.Songs.CountAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).AcceptCandidateAsVersionAsync(first.Observation, other.Candidate, TrackVersionFlags.Live, default));
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactVerifiedAssociationWinsAndReusedMultipleSongsReject(bool multiSong)
    {
        await using var schema = await PostgresTestSchema.CreateAsync(); var db = schema.Db;
        var first = Add(db, isPublic: true); var second = Add(db, isPublic: true);
        await db.SaveChangesAsync();
        await Service(db).AcceptCandidateAsVersionAsync(first.Observation, first.Candidate, TrackVersionFlags.Live, default);
        var retained = (await db.TrackResolutionCandidates.SingleAsync(c => c.Id == first.Candidate)).AnchorTrackId!.Value;
        var exact = new Track { Id = Guid.NewGuid() }; var song = new Song { Id = Guid.NewGuid() };
        db.AddRange(exact, song, new SongTrack { SongId = song.Id, TrackId = exact.Id });
        CatalogFixture.Associate(await db.ProviderCatalogItems.SingleAsync(), exact.Id);
        if (multiSong) { var another = new Song { Id = Guid.NewGuid() }; db.AddRange(another, new SongTrack { SongId = another.Id, TrackId = exact.Id }); }
        await db.SaveChangesAsync();
        if (multiSong)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).AcceptCandidateAsVersionAsync(second.Observation, second.Candidate, TrackVersionFlags.Live, default));
            Assert.Equal(3, await db.Tracks.CountAsync());
        }
        else
        {
            await Service(db).AcceptCandidateAsVersionAsync(second.Observation, second.Candidate, TrackVersionFlags.Live, default);
            Assert.Equal(exact.Id, (await db.TrackResolutionCandidates.SingleAsync(c => c.Id == second.Candidate)).AnchorTrackId);
            Assert.NotEqual(retained, exact.Id);
        }
    }

    [PostgresFact]
    public async Task ConflictingAcceptedOriginalReferencesRequireReview()
    {
        await using var schema = await PostgresTestSchema.CreateAsync(); var db = schema.Db;
        var first = Add(db); var second = Add(db); var third = Add(db);
        await db.SaveChangesAsync();
        await Service(db).AcceptCandidateAsVersionAsync(first.Observation, first.Candidate, TrackVersionFlags.Live, default);
        await Service(db).AcceptCandidateAsVersionAsync(second.Observation, second.Candidate, TrackVersionFlags.Live, default);
        var conflicting = new Track { Id = Guid.NewGuid() }; db.Add(conflicting);
        (await db.TrackResolutionCandidates.SingleAsync(c => c.Id == second.Candidate)).AnchorTrackId = conflicting.Id;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).AcceptCandidateAsVersionAsync(third.Observation, third.Candidate, TrackVersionFlags.Live, default));
        Assert.Equal(4, await db.Tracks.CountAsync()); Assert.Equal(1, await db.Songs.CountAsync());
    }

    private sealed class CapturingLyrics : ILyricsProvider
    {
        public LyricsLookup? Lookup;
        public Task<LyricsResult> GetLyricsAsync(LyricsLookup lookup, CancellationToken ct)
        { Lookup = lookup; return Task.FromResult(new LyricsResult { State = LyricsStates.Available, MatchStatus = LyricsMatchStatuses.Exact, Provider = "test", Attribution = "test" }); }
    }
}
