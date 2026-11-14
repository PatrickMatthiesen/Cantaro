using System.Text.Json;
using Cantaro.Api.Configuration;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Cantaro.Api.Services.Lyrics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class OwnedTrackInputFallbackTests
{
    [Theory]
    [InlineData("spotify", true)]
    [InlineData("youtube", true)]
    [InlineData("spotify", false)]
    [InlineData("youtube", false)]
    public async Task CanonicalOnlyExportUsesExistingOwnerInput(string destination, bool owned)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        await AssertExportAsync(db, destination, owned);
    }

    [PostgresTheory]
    [InlineData("spotify", true)]
    [InlineData("youtube", true)]
    [InlineData("spotify", false)]
    [InlineData("youtube", false)]
    public async Task PostgresCanonicalOnlyExportUsesExistingOwnerInput(string destination, bool owned)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await AssertExportAsync(schema.Db, destination, owned);
    }

    private static async Task AssertExportAsync(ApplicationDbContext db, string destination, bool owned)
    {
        db.Users.AddRange(TestUserFactory.Create(42, "fallback-owner@example.test"), TestUserFactory.Create(84, "fallback-other@example.test"));
        var track = new Track { Id = Guid.NewGuid() };
        var sourcePlaylist = new Playlist { Id = Guid.NewGuid(), UserId = owned ? 42 : 84, Name = "Imported" };
        var targetPlaylist = new Playlist { Id = Guid.NewGuid(), UserId = 42, Name = "Added in Cantaro" };
        var input = PrivateInput(sourcePlaylist.UserId, "source", "Song", "Artist");
        input.SourceType = destination == "spotify" ? "youtube" : "spotify";
        input.TrackId = track.Id;
        db.AddRange(track, sourcePlaylist, targetPlaylist, input, new PlaylistEntry
        {
            Id = Guid.NewGuid(), OwnerUserId = sourcePlaylist.UserId, PlaylistId = sourcePlaylist.Id,
            TrackId = track.Id, TrackObservationId = input.Id, Position = 0
        });
        await db.SaveChangesAsync();
        Assert.True(await new MusicLibraryQueryService(db).AddCanonicalSongToPlaylistAsync(track.Id, targetPlaylist.Id, 42, default));
        db.ChangeTracker.Clear();
        var target = await db.PlaylistEntries.SingleAsync(entry => entry.PlaylistId == targetPlaylist.Id);
        Assert.Null(target.TrackObservationId);
        var searches = 0;
        var result = await PlaylistDestinationIdentityResolver.ResolveAsync(db, new TrackMatchingOptions(),
            (_, _, _) => throw new InvalidOperationException("The recording already exists."),
            (_, _, _) => throw new InvalidOperationException("Membership must not change."),
            new PlatformAccountContext(42, 7), destination, target, (evidence, _) =>
            {
                searches++;
                Assert.Equal("Song", evidence.Title);
                Assert.Equal("Artist", evidence.Artist);
                return Task.FromResult<IReadOnlyList<TrackMatchSearchCandidate>>([new()
                { CandidateSource = destination, ExternalId = "destination-id", Title = "Song", Artist = "Artist", DurationSeconds = 180 }]);
            }, default);
        Assert.Equal(owned ? "destination-id" : null, result);
        Assert.Equal(owned ? 1 : 0, searches);
        Assert.Empty(await db.ProviderCatalogItems.ToListAsync());
        Assert.Single(await db.TrackObservations.ToListAsync());
        var stored = await db.PlaylistEntries.AsNoTracking().SingleAsync(entry => entry.Id == target.Id);
        Assert.Equal(track.Id, stored.TrackId);
        Assert.Null(stored.TrackObservationId);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("withdrawn")]
    public async Task LyricsSkipUnusableEarlierInput(string invalidation)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        await AssertLyricsAsync(db, invalidation);
    }

    [PostgresTheory]
    [InlineData("expired")]
    [InlineData("withdrawn")]
    public async Task PostgresLyricsSkipUnusableEarlierInput(string invalidation)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await AssertLyricsAsync(schema.Db, invalidation);
    }

    private static async Task AssertLyricsAsync(ApplicationDbContext db, string invalidation)
    {
        db.Users.AddRange(TestUserFactory.Create(42, "lyrics-owner@example.test"), TestUserFactory.Create(84, "lyrics-other@example.test"));
        var track = new Track { Id = Guid.NewGuid() };
        var playlist = new Playlist { Id = Guid.NewGuid(), UserId = 42, Name = "Saved" };
        var otherPlaylist = new Playlist { Id = Guid.NewGuid(), UserId = 84, Name = "Other" };
        var now = DateTimeOffset.UtcNow;
        var item = new ProviderCatalogItem
        {
            Id = Guid.NewGuid(), SourceType = "spotify", ExternalId = "stale", Revision = 1,
            FactsJson = JsonSerializer.Serialize(new ProviderCatalogFacts { Title = "Stale Song", Artist = "Stale Artist" }),
            VisibilityState = invalidation == "withdrawn" ? CatalogVisibility.Withdrawn : CatalogVisibility.Public,
            VerifiedAt = now.AddDays(-2), RevalidateAfter = invalidation == "expired" ? now.AddDays(-1) : now.AddDays(1),
            VerificationMethod = CatalogVerificationMethods.SpotifyApplication
        };
        var stale = PrivateInput(42, "stale", "Stale Song", "Stale Artist");
        ObservationInput.SetPublic(stale, item);
        var usable = PrivateInput(42, "usable", "Usable Song", "Usable Artist");
        var outsider = PrivateInput(84, "outsider", "Other Song", "Other Artist");
        db.AddRange(track, playlist, otherPlaylist, item, stale, usable, outsider);
        foreach (var (observation, position, container) in new[] { (stale, 0, playlist), (usable, 1, playlist), (outsider, 0, otherPlaylist) })
            db.PlaylistEntries.Add(new PlaylistEntry
            {
                Id = Guid.NewGuid(), OwnerUserId = container.UserId, PlaylistId = container.Id,
                TrackId = track.Id, TrackObservationId = observation.Id, Position = position
            });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var provider = new CapturingLyricsProvider();
        var result = await new LyricsService(db, provider).GetLyricsAsync(42, track.Id, default);
        Assert.NotNull(result);
        Assert.Equal("Usable Song", provider.Lookup?.Title);
        Assert.Equal("Usable Artist", provider.Lookup?.Artist);
        Assert.Equal(180, provider.Lookup?.DurationSeconds);
        Assert.Null((await db.ProviderCatalogItems.SingleAsync()).TrackId);
    }

    private static TrackObservation PrivateInput(int owner, string id, string title, string artist) => new()
    {
        Id = Guid.NewGuid(), OwnerUserId = owner, InputKind = ObservationInputKind.Private,
        SourceType = "spotify", ExternalId = id, Title = title, Artist = artist, DurationSeconds = 180,
        MatchStatus = TrackMatchingStatuses.Matched
    };

    private sealed class CapturingLyricsProvider : ILyricsProvider
    {
        public LyricsLookup? Lookup { get; private set; }
        public Task<LyricsResult> GetLyricsAsync(LyricsLookup lookup, CancellationToken ct)
        {
            Lookup = lookup;
            return Task.FromResult(new LyricsResult { State = LyricsStates.Available, MatchStatus = LyricsMatchStatuses.Exact, Provider = "test", Attribution = "Test" });
        }
    }
}
