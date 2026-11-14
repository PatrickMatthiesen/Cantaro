using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Cantaro.Api.Services.Spotify;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class ArtistReconciliationServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private const string Mbid = "0d5f4b04-3be8-427a-8c24-23b1454b2f31";

    [Fact]
    public async Task SpotifyImport_ReusesStableIdentityAcrossTracksAndRepeatedImports()
    {
        await using var fixture = await Fixture.CreateAsync();
        var resolver = new SpotifyTrackResolver(fixture.Db);
        var snapshot = await fixture.AddPublicCatalogSnapshotAsync(Snapshot("one", "First spelling", "spotify-artist"));
        var first = await resolver.ResolveAsync(snapshot, Now, default);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        await resolver.ResolveAsync(snapshot, Now, default);
        var secondSnapshot = await fixture.AddPublicCatalogSnapshotAsync(Snapshot("two", "Alias", "spotify-artist"));
        await resolver.ResolveAsync(secondSnapshot, Now, default);
        await fixture.Db.SaveChangesAsync();

        Assert.Equal(2, await fixture.Db.Tracks.CountAsync());
        Assert.Equal(2, await fixture.Db.TrackArtistCredits.CountAsync());
        var artist = await fixture.Db.Artists.SingleAsync();
        var link = await fixture.Db.ArtistProviderLinks.SingleAsync();
        Assert.Equal(artist.Id, link.ArtistId);
        Assert.Equal("spotify-artist", link.ExternalId);
        Assert.Equal("https://open.spotify.com/artist/spotify-artist", link.ProfileUrl);
        Assert.Equal("https://images/artist", link.ArtworkUrl);
        Assert.Null(link.RawMetadata);
        Assert.Equal(CatalogVerificationMethods.SpotifyApplication, link.VerificationMethod);
        Assert.NotNull(link.VerifiedAt);
        Assert.True(link.RevalidateAfter > link.VerifiedAt);
        var credit = await fixture.Db.TrackArtistCredits.SingleAsync(c => c.TrackId == first.Id);
        Assert.Equal(snapshot.ProviderCatalogItemId, credit.ProviderCatalogItemId);
        Assert.Equal(snapshot.ProviderCatalogRevision, credit.ProviderCatalogRevision);
        Assert.Equal(0, credit.SourceCreditPosition);
    }

    [Fact]
    public async Task SameNames_WithDistinctProviderIdsStaySeparate_IncludingWithinOneCollaboration()
    {
        await using var fixture = await Fixture.CreateAsync();
        var resolver = new SpotifyTrackResolver(fixture.Db);
        await resolver.ResolveAsync(Snapshot("one", "The Twins", "a"), Now, default);
        await resolver.ResolveAsync(Snapshot("two", "The Twins", "b"), Now, default);
        var collaboration = fixture.AddTrack();
        await fixture.Import(collaboration, [Spotify("The Twins", "a"), Spotify("The Twins", "b")]);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        collaboration = await fixture.Db.Tracks.SingleAsync(t => t.Id == collaboration.Id);
        await fixture.Import(collaboration, [Spotify("The Twins", "a"), Spotify("The Twins", "b")]);
        await fixture.Db.SaveChangesAsync();

        Assert.Equal(2, await fixture.Db.Artists.CountAsync());
        Assert.Equal(2, collaboration.ArtistCredits.Count);
        Assert.Equal(2, collaboration.ArtistCredits.Select(c => c.ArtistId).Distinct().Count());
        Assert.Empty(await fixture.Db.ArtistReconciliationConflicts.ToListAsync());
    }

    [Fact]
    public async Task NamesAlone_DoNotMergeArtistsAcrossTracks_ButRepeatedImportsAreIdempotent()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = fixture.AddTrack();
        var second = fixture.AddTrack();
        ArtistCreditMetadata[] credits = [new() { Name = "The Twins" }];
        await fixture.Import(first, credits);
        await fixture.Import(second, credits);
        await fixture.Db.SaveChangesAsync();
        await fixture.Import(first, credits);
        await fixture.Db.SaveChangesAsync();
        Assert.Equal(2, await fixture.Db.Artists.CountAsync());
        Assert.Equal(2, await fixture.Db.TrackArtistCredits.CountAsync());
    }

    [Fact]
    public async Task ExistingTrack_NormalizedCreditNameCanGainProviderAndMusicBrainzIdentity()
    {
        await using var fixture = await Fixture.CreateAsync();
        var track = fixture.AddTrack();
        await fixture.Import(track, [new() { Name = "ARTIST" }]);
        await fixture.Db.SaveChangesAsync();
        var originalArtist = track.ArtistCredits.Single().ArtistId;
        await fixture.Import(track, [Spotify("Artist", "spotify-a")]);
        await fixture.Import(track, [MusicBrainz("Artist", Mbid)]);
        await fixture.Db.SaveChangesAsync();
        Assert.Equal(originalArtist, track.ArtistCredits.Single().ArtistId);
        Assert.Equal(Mbid, (await fixture.Db.ArtistProviderLinks.SingleAsync(l => l.Provider == "musicbrainz")).ExternalId);
        Assert.Equal(2, await fixture.Db.ArtistProviderLinks.CountAsync());
        Assert.Empty(await fixture.Db.ArtistReconciliationConflicts.ToListAsync());
    }

    [Fact]
    public async Task ConflictingProviderId_IsRetainedForReview_AndRepeatedEvidenceIsDeduplicated()
    {
        await using var fixture = await Fixture.CreateAsync();
        var track = fixture.AddTrack();
        await fixture.Import(track, [Spotify("Artist", "a")]);
        await fixture.Db.SaveChangesAsync();
        var original = track.ArtistCredits.Single().ArtistId;
        await fixture.Import(track, [Spotify("Artist", "b")]);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        track = await fixture.Db.Tracks.SingleAsync(t => t.Id == track.Id);
        await fixture.Import(track, [Spotify("Artist", "b")]);
        await fixture.Db.SaveChangesAsync();
        Assert.Equal(original, track.ArtistCredits.Single().ArtistId);
        Assert.Equal("a", (await fixture.Db.ArtistProviderLinks.SingleAsync()).ExternalId);
        var conflict = await fixture.Db.ArtistReconciliationConflicts.SingleAsync();
        Assert.Contains("\"ExternalId\":\"b\"", conflict.IdentityConflictJson);
        Assert.Contains(original.ToString(), conflict.IdentityConflictJson);
        Assert.Single(await fixture.Db.Artists.ToListAsync());
    }

    [Fact]
    public async Task ConflictingStableIdentities_DoNotMoveProviderLinksOrMergeExistingArtists()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = fixture.AddTrack();
        var second = fixture.AddTrack();
        await fixture.Import(first, [Spotify("Artist", "a")]);
        await fixture.Import(second, [MusicBrainz("Artist", Mbid)]);
        await fixture.Db.SaveChangesAsync();
        var original = first.ArtistCredits.Single().ArtistId;
        // Both IDs are already owned by different artists, even if their names agree.
        await fixture.Import(first, [Spotify("Artist", "a") with { MusicBrainzArtistId = Mbid }]);
        await fixture.Import(first, [MusicBrainz("Artist", Mbid)]);
        await fixture.Db.SaveChangesAsync();
        Assert.Equal(original, first.ArtistCredits.Single().ArtistId);
        Assert.Equal(2, await fixture.Db.Artists.CountAsync());
        Assert.Equal(2, await fixture.Db.ArtistReconciliationConflicts.CountAsync());
        Assert.False(await fixture.Db.ArtistProviderLinks.AnyAsync(l => l.ArtistId == original && l.Provider == "musicbrainz"));
    }

    [Fact]
    public async Task ConflictingMusicBrainzId_DoesNotOverwriteExistingIdentity()
    {
        await using var fixture = await Fixture.CreateAsync();
        var track = fixture.AddTrack();
        await fixture.Import(track, [MusicBrainz("Artist", Mbid)]);
        await fixture.Db.SaveChangesAsync();
        await fixture.Import(track, [MusicBrainz("Artist", "82e3e1de-9243-4e58-b2d2-796c64f76c82")]);
        await fixture.Db.SaveChangesAsync();
        Assert.Equal(Mbid, (await fixture.Db.ArtistProviderLinks.SingleAsync(l => l.Provider == "musicbrainz")).ExternalId);
        Assert.Single(await fixture.Db.ArtistReconciliationConflicts.ToListAsync());
    }

    [Fact]
    public async Task LegacyAggregateCredit_IsReplacedByOrderedCreditsAndSuppliedRoles()
    {
        await using var fixture = await Fixture.CreateAsync();
        var track = fixture.AddTrack();
        await fixture.Import(track, [new() { Name = "Alpha feat. Beta" }]);
        await fixture.Db.SaveChangesAsync();
        ArtistCreditMetadata[] credits = [MusicBrainz("Alpha", Mbid) with { JoinPhrase = " feat. " },
            Spotify("Beta", "beta") with { Role = TrackArtistRole.Featured }];
        await new ArtistReconciliationService(fixture.Db).ReconcileAsync(track, credits, "musicbrainz", Now, default, "Alpha feat. Beta");
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        track = await fixture.Db.Tracks.SingleAsync(t => t.Id == track.Id);
        await fixture.Import(track, credits);
        await fixture.Db.SaveChangesAsync();
        var ordered = track.ArtistCredits.OrderBy(c => c.Position).ToArray();
        Assert.Equal(["Alpha", "Beta"], ordered.Select(c => c.CreditedName).ToArray());
        Assert.Equal(" feat. ", ordered[0].JoinPhrase);
        Assert.Equal(TrackArtistRole.Featured, ordered[1].Role);
        Assert.Empty(await fixture.Db.ArtistReconciliationConflicts.ToListAsync());
    }

    [Fact]
    public async Task ExistingCredit_KeepsSuppliedRoleWhenAnotherProviderDoesNotSupplyRoles()
    {
        await using var fixture = await Fixture.CreateAsync();
        var track = fixture.AddTrack();
        await fixture.Import(track, [Spotify("Artist", "a") with { Role = TrackArtistRole.Remixer }]);
        await fixture.Db.SaveChangesAsync();
        await fixture.Import(track, [Spotify("Artist", "a")]);
        await fixture.Db.SaveChangesAsync();
        Assert.Equal(TrackArtistRole.Remixer, track.ArtistCredits.Single().Role);
    }

    [Theory]
    [InlineData("Alpha feat. Beta")]
    [InlineData("Alpha & Beta")]
    [InlineData("Unknown collaboration")]
    public async Task SpotifyImport_DoesNotPromoteAccountCollaborationFormatting(string legacyName)
    {
        await using var fixture = await Fixture.CreateAsync();
        var track = fixture.AddTrack();
        await fixture.Import(track, [new() { Name = legacyName }]);
        fixture.Db.ProviderCatalogItems.Add(new ProviderCatalogItem { Id = Guid.NewGuid(), TrackId = track.Id,
            SourceType = "spotify", ExternalId = "collaboration", FactsJson = "{}" });
        await fixture.Db.SaveChangesAsync();
        var snapshot = Snapshot("collaboration", "Alpha, Beta", "alpha") with {
            ArtistIdentities = [Spotify("Alpha", "alpha"), Spotify("Beta", "beta")] };
        var resolver = new SpotifyTrackResolver(fixture.Db);

        await resolver.ResolveAsync(snapshot, Now, default);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        await resolver.ResolveAsync(snapshot, Now, default);
        await fixture.Db.SaveChangesAsync();

        var credits = await fixture.Db.TrackArtistCredits.OrderBy(c => c.Position).ToArrayAsync();
        Assert.Equal([legacyName], credits.Select(c => c.CreditedName));
        Assert.Empty(await fixture.Db.ArtistProviderLinks.ToListAsync());
        Assert.Empty(await fixture.Db.ArtistReconciliationConflicts.ToListAsync());
    }

    [Fact]
    public async Task ExistingSingleAnonymousArtist_CanGainIdentityAndAnotherCollaborator()
    {
        await using var fixture = await Fixture.CreateAsync();
        var track = fixture.AddTrack();
        await fixture.Import(track, [new() { Name = "Alpha" }]);
        await fixture.Db.SaveChangesAsync();
        var originalArtistId = track.ArtistCredits.Single().ArtistId;
        await fixture.Import(track, [Spotify("Alpha", "alpha"), Spotify("Beta", "beta")]);
        await fixture.Db.SaveChangesAsync();

        var credits = track.ArtistCredits.OrderBy(c => c.Position).ToArray();
        Assert.Equal(["Alpha", "Beta"], credits.Select(c => c.CreditedName));
        Assert.Equal(originalArtistId, credits[0].ArtistId);
        Assert.Equal(2, await fixture.Db.Artists.CountAsync());
        Assert.Equal(2, await fixture.Db.ArtistProviderLinks.CountAsync());
        Assert.Empty(await fixture.Db.ArtistReconciliationConflicts.ToArrayAsync());
    }

    [Fact]
    public async Task ReorderedIdentities_RetainCanonicalOrderAndJoinPhrases_AndDeduplicateOrderingEvidence()
    {
        await using var fixture = await Fixture.CreateAsync();
        var track = fixture.AddTrack();
        await fixture.Import(track, [Spotify("Alpha", "a") with { JoinPhrase = " feat. " },
            Spotify("Beta", "b") with { JoinPhrase = "" }]);
        await fixture.Db.SaveChangesAsync();
        var reordered = new[] { Spotify("Beta", "b") with { JoinPhrase = " & " },
            Spotify("Alpha", "a") with { JoinPhrase = "" } };
        await fixture.Import(track, reordered);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        track = await fixture.Db.Tracks.SingleAsync(t => t.Id == track.Id);
        await fixture.Import(track, reordered);
        await fixture.Db.SaveChangesAsync();

        var credits = track.ArtistCredits.OrderBy(c => c.Position).ToArray();
        Assert.Equal(["Alpha", "Beta"], credits.Select(c => c.CreditedName));
        Assert.Equal([" feat. ", ""], credits.Select(c => c.JoinPhrase));
        Assert.Equal(2, await fixture.Db.Artists.CountAsync());
        var conflicts = await fixture.Db.ArtistReconciliationConflicts.ToArrayAsync();
        Assert.Equal(2, conflicts.Length);
        foreach (var conflict in conflicts)
        {
            Assert.Contains("order", conflict.Reason);
            using var json = JsonDocument.Parse(Assert.IsType<string>(conflict.IdentityConflictJson));
            var position = conflict.SourceCreditPosition;
            Assert.Equal(position == 0 ? "b" : "a", json.RootElement.GetProperty("ExternalId").GetString());
            Assert.False(json.RootElement.TryGetProperty("Name", out _));
            Assert.False(json.RootElement.TryGetProperty("JoinPhrase", out _));
        }
    }

    [Fact]
    public async Task MbidOnlyAndSpotifyPlusMbid_ResolveBothLinksAcrossRestartWithoutNameIdentity()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = fixture.AddTrack();
        await fixture.Import(first, [new() { Name = "Original", MusicBrainzArtistId = $" {Mbid.ToUpperInvariant()} " }]);
        await fixture.Db.SaveChangesAsync();
        var artistId = first.ArtistCredits.Single().ArtistId;
        fixture.Db.ChangeTracker.Clear();

        var second = fixture.AddTrack();
        await fixture.Import(second, [Spotify("Different credited name", "spotify-a") with { MusicBrainzArtistId = Mbid }]);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var third = fixture.AddTrack();
        await fixture.Import(third, [Spotify("Third spelling", "spotify-a")]);
        await fixture.Db.SaveChangesAsync();

        Assert.Equal(artistId, second.ArtistCredits.Single().ArtistId);
        Assert.Equal(artistId, third.ArtistCredits.Single().ArtistId);
        Assert.Single(await fixture.Db.Artists.ToListAsync());
        var links = await fixture.Db.ArtistProviderLinks.ToListAsync();
        Assert.Equal(2, links.Count);
        Assert.Contains(links, l => l.Provider == "musicbrainz" && l.ExternalId == Mbid && l.ArtistId == artistId);
        Assert.Contains(links, l => l.Provider == "spotify" && l.ExternalId == "spotify-a" && l.ArtistId == artistId);
    }

    [Fact]
    public async Task SuppliedMusicBrainzIdsThatDisagree_AreRejectedBeforeCreatingAnyIdentity()
    {
        await using var fixture = await Fixture.CreateAsync();
        var track = fixture.AddTrack();
        await fixture.Import(track, [MusicBrainz("Artist", Mbid) with {
            MusicBrainzArtistId = "82e3e1de-9243-4e58-b2d2-796c64f76c82" }]);
        await fixture.Db.SaveChangesAsync();
        Assert.Empty(await fixture.Db.Artists.ToListAsync());
        Assert.Empty(await fixture.Db.ArtistProviderLinks.ToListAsync());
        Assert.Empty(track.ArtistCredits);
        Assert.Contains("disagrees", (await fixture.Db.ArtistReconciliationConflicts.SingleAsync()).Reason);
    }

    [Fact]
    public async Task SourceReferences_RejectPartialEvidenceBeforeChangingTheTrack()
    {
        await using var fixture = await Fixture.CreateAsync();
        var track = fixture.AddTrack();
        var reconcile = new ArtistReconciliationService(fixture.Db);
        await Assert.ThrowsAsync<ArgumentException>(() => reconcile.ReconcileAsync(track, [Spotify("Artist", "a")],
            "spotify", Now, default, sourceItemId: Guid.NewGuid()));
        await Assert.ThrowsAsync<ArgumentException>(() => reconcile.ReconcileAsync(track, [Spotify("Artist", "a")],
            "spotify", Now, default, sourceRevision: 1));
        Assert.Empty(track.ArtistCredits);
        Assert.Empty(fixture.Db.ArtistProviderLinks.Local);
    }

    [Fact]
    public async Task CreditAndConflictReferences_PreserveCanonicalValuesAndExcludeCopiedMetadata()
    {
        await using var fixture = await Fixture.CreateAsync();
        var track = fixture.AddTrack();
        var snapshot = await fixture.AddPublicCatalogSnapshotAsync(Snapshot("source", "Artist", "a"));
        var reconcile = new ArtistReconciliationService(fixture.Db);
        await reconcile.ReconcileAsync(track, [Spotify("Artist", "a") with { JoinPhrase = " feat. ", Role = TrackArtistRole.Featured }],
            "spotify", Now, default, sourceItemId: snapshot.ProviderCatalogItemId, sourceRevision: snapshot.ProviderCatalogRevision);
        await fixture.Db.SaveChangesAsync();
        var incoming = Spotify("Artist", "b") with { RawMetadata = "secret-payload", ArtworkUrl = "secret-artwork", JoinPhrase = " & " };
        await reconcile.ReconcileAsync(track, [incoming], "spotify", Now, default,
            sourceItemId: snapshot.ProviderCatalogItemId, sourceRevision: snapshot.ProviderCatalogRevision);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        track = await fixture.Db.Tracks.SingleAsync(t => t.Id == track.Id);
        await reconcile.ReconcileAsync(track, [incoming with { RawMetadata = "changed-payload" }], "spotify", Now, default,
            sourceItemId: snapshot.ProviderCatalogItemId, sourceRevision: snapshot.ProviderCatalogRevision);
        await fixture.Db.SaveChangesAsync();

        var credit = track.ArtistCredits.Single();
        Assert.Equal(snapshot.ProviderCatalogItemId, credit.ProviderCatalogItemId);
        Assert.Equal(snapshot.ProviderCatalogRevision, credit.ProviderCatalogRevision);
        Assert.Equal(0, credit.SourceCreditPosition);
        Assert.Equal("Artist", credit.CreditedName);
        Assert.Equal(" feat. ", credit.JoinPhrase);
        Assert.Equal(TrackArtistRole.Featured, credit.Role);
        var conflict = await fixture.Db.ArtistReconciliationConflicts.SingleAsync();
        Assert.Equal(snapshot.ProviderCatalogItemId, conflict.ProviderCatalogItemId);
        Assert.Equal(snapshot.ProviderCatalogRevision, conflict.ProviderCatalogRevision);
        Assert.Equal(0, conflict.SourceCreditPosition);
        Assert.DoesNotContain("payload", conflict.IdentityConflictJson!);
        Assert.DoesNotContain("artwork", conflict.IdentityConflictJson!);
        Assert.DoesNotContain("JoinPhrase", conflict.IdentityConflictJson!);
        Assert.Contains("\"ExternalId\":\"b\"", conflict.IdentityConflictJson!);
    }

    private static ArtistCreditMetadata Spotify(string name, string id) => new() {
        Name = name, Provider = "spotify", ExternalId = id,
        ProfileUrl = $"https://open.spotify.com/artist/{id}", ArtworkUrl = "https://images/artist", RawMetadata = "{\"upstream\":true}" };
    private static ArtistCreditMetadata MusicBrainz(string name, string id) => new() {
        Name = name, Provider = "musicbrainz", ExternalId = id, MusicBrainzArtistId = id, SortName = $"{name}, sorted" };
    private static SpotifyTrackSnapshot Snapshot(string id, string name, string artistId) =>
        new(id, $"Song {id}", name, [name], null, null, $"https://open.spotify.com/track/{id}", null, null, 180, null, 0)
        { ArtistIdentities = [Spotify(name, artistId)] };

    private sealed class Fixture(SqliteConnection connection, ApplicationDbContext db) : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = db;
        public async Task<SpotifyTrackSnapshot> AddPublicCatalogSnapshotAsync(SpotifyTrackSnapshot snapshot)
        {
            var item = new ProviderCatalogItem
            {
                Id = Guid.NewGuid(),
                SourceType = "spotify",
                ExternalId = snapshot.Id,
                FactsJson = JsonSerializer.Serialize(new ProviderCatalogFacts
                {
                    Title = snapshot.Name,
                    Artist = snapshot.Artist,
                    Albums = snapshot.AlbumName is null ? [] : [snapshot.AlbumName],
                    ThumbnailUrl = snapshot.ImageUrl,
                    ProviderUrl = snapshot.ExternalUrl,
                    DurationSeconds = snapshot.DurationSeconds,
                    Isrc = snapshot.Isrc,
                    ArtistCredits = snapshot.ArtistIdentities
                }),
                VerificationMethod = CatalogVerificationMethods.SpotifyApplication,
                VerifiedAt = Now,
                RevalidateAfter = Now.AddHours(1),
                VisibilityState = CatalogVisibility.Public,
                Revision = 1
            };
            Db.ProviderCatalogItems.Add(item);
            await Db.SaveChangesAsync();
            return snapshot with { ProviderCatalogItemId = item.Id, ProviderCatalogRevision = item.Revision };
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            return new Fixture(connection, db);
        }
        public Track AddTrack()
        {
            var track = new Track { Id = Guid.NewGuid(), CreatedAt = Now, UpdatedAt = Now };
            Db.Tracks.Add(track);
            return track;
        }
        public Task Import(Track track, IReadOnlyList<ArtistCreditMetadata> credits) =>
            new ArtistReconciliationService(Db).ReconcileAsync(track, credits, credits[0].Provider ?? "unknown", Now, default);
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
