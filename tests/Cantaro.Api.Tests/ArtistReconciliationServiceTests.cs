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
        var snapshot = Snapshot("one", "First spelling", "spotify-artist");
        var first = await resolver.ResolveAsync(snapshot, Now, default);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        await resolver.ResolveAsync(snapshot, Now, default);
        await resolver.ResolveAsync(Snapshot("two", "Alias", "spotify-artist"), Now, default);
        await fixture.Db.SaveChangesAsync();

        Assert.Equal(2, await fixture.Db.Tracks.CountAsync());
        Assert.Equal(2, await fixture.Db.TrackArtistCredits.CountAsync());
        var artist = await fixture.Db.Artists.SingleAsync();
        var link = await fixture.Db.ArtistProviderLinks.SingleAsync();
        Assert.Equal(artist.Id, link.ArtistId);
        Assert.Equal("spotify-artist", link.ExternalId);
        Assert.Equal("https://open.spotify.com/artist/spotify-artist", link.ProfileUrl);
        Assert.Equal("https://images/artist", link.ArtworkUrl);
        Assert.Contains("upstream", link.RawMetadata);
        var credit = await fixture.Db.TrackArtistCredits.SingleAsync(c => c.TrackId == first.Id);
        Assert.Contains("spotify", credit.Provenance);
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
        Assert.Equal(Mbid, (await fixture.Db.Artists.SingleAsync()).MusicBrainzArtistId);
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
        Assert.Contains("\"ExternalId\":\"b\"", conflict.Evidence);
        Assert.Contains(original.ToString(), conflict.Evidence);
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
        Assert.Null((await fixture.Db.Artists.SingleAsync(a => a.Id == original)).MusicBrainzArtistId);
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
        Assert.Equal(Mbid, (await fixture.Db.Artists.SingleAsync()).MusicBrainzArtistId);
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
