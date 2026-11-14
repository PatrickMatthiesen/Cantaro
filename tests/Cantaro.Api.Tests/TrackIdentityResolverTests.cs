using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class TrackIdentityResolverTests
{
    [Fact]
    public async Task ResolveExistingAsync_ReusesVerifiedPublicAssociationAcrossUsers()
    {
        await using var database = await CreateDatabaseAsync();
        var now = DateTimeOffset.UtcNow;
        var track = NewTrack();
        var item = new ProviderCatalogItem
        {
            Id = Guid.NewGuid(), SourceType = "spotify", ExternalId = "public-track",
            FactsJson = JsonSerializer.Serialize(new ProviderCatalogFacts
            {
                Title = "Public Song", Artist = "Public Artist", DurationSeconds = 180,
                ArtistCredits = [new ArtistCreditMetadata { Name = "Public Artist" }]
            }),
            VerificationMethod = "spotify_application_catalog", VerifiedAt = now.AddMinutes(-1),
            RevalidateAfter = now.AddDays(1), VisibilityState = CatalogVisibility.Public, Revision = 3
        };
        database.Db.Tracks.Add(track);
        database.Db.ProviderCatalogItems.Add(item);
        database.Db.ProviderCatalogItems.Add(CatalogFixture.Associate(item, track.Id, RecordingAssociationState.Verified, item.Revision));
        await database.Db.SaveChangesAsync();

        var resolver = new TrackIdentityResolver(database.Db);
        foreach (var ownerId in new[] { 42, 84 })
        {
            var match = await resolver.ResolveExistingAsync(new TrackIdentityQuery(
                "spotify", "public-track", "Public Song", "Public Artist", 180, OwnerUserId: ownerId),
                CancellationToken.None);
            Assert.NotNull(match);
            Assert.Equal(track.Id, match.Track.Id);
            Assert.Equal(TrackIdentityMatchKind.ExactSource, match.Kind);
        }
    }

    [Fact]
    public async Task ResolveExistingAsync_MatchesNormalizedQuotedPublicFactsAcrossFreshContext()
    {
        await using var database = await CreateDatabaseAsync();
        Guid trackId;
        {
            var now = DateTimeOffset.UtcNow;
            var track = NewTrack();
            trackId = track.Id;
            var item = new ProviderCatalogItem
            {
                Id = Guid.NewGuid(), SourceType = "spotify", ExternalId = "quoted-public-track",
                FactsJson = JsonSerializer.Serialize(new ProviderCatalogFacts
                {
                    Title = "I Said \"Hello\", Again!",
                    Artist = "AC/DC & Friends",
                    DurationSeconds = 180
                }),
                VerificationMethod = CatalogVerificationMethods.SpotifyApplication,
                VerifiedAt = now.AddMinutes(-1), RevalidateAfter = now.AddHours(1),
                VisibilityState = CatalogVisibility.Public, Revision = 1
            };
            database.Db.Tracks.Add(track);
            database.Db.ProviderCatalogItems.Add(item);
            database.Db.ProviderCatalogItems.Add(CatalogFixture.Associate(item, track.Id, RecordingAssociationState.Verified, item.Revision));
            await database.Db.SaveChangesAsync();
            Assert.Equal("i said hello again", item.NormalizedTitle);
            Assert.Equal("ac dc friends", item.NormalizedArtist);
        }

        await using var fresh = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(database.Db.Database.GetDbConnection()).Options);
        var match = await new TrackIdentityResolver(fresh).ResolveExistingAsync(new TrackIdentityQuery(
            "youtube", "independent-video", "i said hello again", "AC DC Friends", 180),
            CancellationToken.None);

        Assert.NotNull(match);
        Assert.Equal(trackId, match.Track.Id);
        Assert.Equal(TrackIdentityMatchKind.Metadata, match.Kind);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task ResolveExistingAsync_SharedIdentityPrecedesPrivateMetadataButPreservesExactOwnedMatch(
        bool exactOwnedSource, bool ambiguousSharedIdentity)
    {
        await using var database = await CreateDatabaseAsync();
        var now = DateTimeOffset.UtcNow;
        var shared = NewTrack();
        var privateTrack = NewTrack();
        database.Db.Tracks.AddRange(shared, privateTrack);
        database.Db.TrackObservations.Add(NewObservation(privateTrack.Id,
            exactOwnedSource ? "youtube" : "spotify", exactOwnedSource ? "incoming" : "private-item",
            "Same Song", "Same Artist", 180, ownerUserId: 42));
        foreach (var track in ambiguousSharedIdentity ? new[] { shared, NewTrack() } : new[] { shared })
        {
            if (track != shared) database.Db.Tracks.Add(track);
            var item = new ProviderCatalogItem
            {
                Id = Guid.NewGuid(), SourceType = "spotify", ExternalId = track.Id.ToString(),
                FactsJson = JsonSerializer.Serialize(new ProviderCatalogFacts
                {
                    Title = "Same Song", Artist = "Same Artist", DurationSeconds = 180
                }),
                VerificationMethod = CatalogVerificationMethods.SpotifyApplication,
                VerifiedAt = now.AddMinutes(-1), RevalidateAfter = now.AddDays(1),
                VisibilityState = CatalogVisibility.Public, Revision = 1
            };
            database.Db.ProviderCatalogItems.Add(CatalogFixture.Associate(item, track.Id,
                RecordingAssociationState.Verified, item.Revision));
        }
        await database.Db.SaveChangesAsync();
        database.Db.ChangeTracker.Clear();

        var match = await new TrackIdentityResolver(database.Db).ResolveExistingAsync(
            new TrackIdentityQuery("youtube", "incoming", "Same Song", "Same Artist", 180,
                OwnerUserId: 42), CancellationToken.None);

        if (ambiguousSharedIdentity)
        {
            Assert.Null(match);
            return;
        }
        Assert.NotNull(match);
        Assert.Equal(exactOwnedSource ? privateTrack.Id : shared.Id, match.Track.Id);
        Assert.Equal(exactOwnedSource ? TrackIdentityMatchKind.ExactSource : TrackIdentityMatchKind.Metadata,
            match.Kind);
    }

    [Fact]
    public async Task ResolveExistingAsync_ExpiredRowsDoNotConsumePublicCandidateLimit()
    {
        await using var database = await CreateDatabaseAsync();
        var now = DateTimeOffset.UtcNow;
        var rows = new List<(Track Track, ProviderCatalogItem Item, ProviderCatalogItem Source)>();
        for (var index = 0; index < 101; index++)
        {
            var track = NewTrack();
            var item = new ProviderCatalogItem
            {
                Id = Guid.NewGuid(), SourceType = "spotify", ExternalId = $"stale-{index:D3}",
                FactsJson = JsonSerializer.Serialize(new ProviderCatalogFacts
                {
                    Title = "Shared Result", Artist = "Shared Artist", Isrc = "USABC2400001", DurationSeconds = 180
                }),
                VerificationMethod = CatalogVerificationMethods.SpotifyApplication,
                VerifiedAt = now.AddDays(-60), RevalidateAfter = now.AddDays(-30),
                VisibilityState = CatalogVisibility.Public, Revision = 1
            };
            rows.Add((track, item, CatalogFixture.Associate(item, track.Id, RecordingAssociationState.Verified, item.Revision)));
        }

        var validTrack = NewTrack();
        var validItem = new ProviderCatalogItem
        {
            Id = Guid.NewGuid(), SourceType = "spotify", ExternalId = "valid-later-result",
            FactsJson = JsonSerializer.Serialize(new ProviderCatalogFacts
            {
                Title = "Shared Result", Artist = "Shared Artist", Isrc = "USABC2400001", DurationSeconds = 180
            }),
            VerificationMethod = CatalogVerificationMethods.SpotifyApplication,
            VerifiedAt = now.AddMinutes(-1), RevalidateAfter = now.AddHours(1),
            VisibilityState = CatalogVisibility.Public, Revision = 1
        };
        rows.Add((validTrack, validItem, CatalogFixture.Associate(validItem, validTrack.Id, RecordingAssociationState.Verified, validItem.Revision)));
        database.Db.Tracks.AddRange(rows.Select(row => row.Track));
        database.Db.ProviderCatalogItems.AddRange(rows.Select(row => row.Item));
        database.Db.ProviderCatalogItems.AddRange(rows.Select(row => row.Source));
        await database.Db.SaveChangesAsync();
        database.Db.ChangeTracker.Clear();

        var match = await new TrackIdentityResolver(database.Db).ResolveExistingAsync(new TrackIdentityQuery(
            "youtube", "new-video", "Shared Result", "Shared Artist", 180, Isrc: "USABC2400001"),
            CancellationToken.None);

        Assert.NotNull(match);
        Assert.Equal(validTrack.Id, match.Track.Id);
        Assert.Equal(TrackIdentityMatchKind.Metadata, match.Kind);
    }

    [Theory]
    [InlineData(160)]
    [InlineData(null)]
    public async Task ResolveExistingAsync_MetadataMatchDoesNotRequireSimilarDuration(int? incomingDuration)
    {
        await using var database = await CreateDatabaseAsync();
        var track = NewTrack();
        database.Db.Tracks.Add(track);
        database.Db.TrackObservations.Add(NewObservation(track.Id, "spotify", "spotify-track",
            "Everything Goes On", "Porter Robinson, League of Legends", 202, ownerUserId: 42));
        await database.Db.SaveChangesAsync();

        var resolver = new TrackIdentityResolver(database.Db);
        var bob = await resolver.ResolveExistingAsync(new TrackIdentityQuery(
            "youtube", "youtube-video", "Everything Goes On",
            "Porter Robinson, League of Legends", incomingDuration, OwnerUserId: 84), CancellationToken.None);
        Assert.Null(bob);
        var match = await resolver.ResolveExistingAsync(new TrackIdentityQuery(
            "youtube", "youtube-video", "Everything Goes On",
            "Porter Robinson, League of Legends", incomingDuration, OwnerUserId: 42), CancellationToken.None);

        Assert.NotNull(match);
        Assert.Equal(track.Id, match.Track.Id);
        Assert.Equal(TrackIdentityMatchKind.Metadata, match.Kind);
    }

    [Fact]
    public async Task ResolveExistingAsync_UsesDescriptionCreditsToReuseCanonicalTrack()
    {
        await using var database = await CreateDatabaseAsync();
        var track = NewTrack();
        database.Db.Tracks.Add(track);
        database.Db.TrackObservations.Add(NewObservation(track.Id, "spotify", "spotify-mortals",
            "Mortals Funk Remix", "LXNGVX, Warriyo", 146, ownerUserId: 42));
        await database.Db.SaveChangesAsync();

        var incomingMetadata = """{"OriginalTitle":"MORTALS FUNK REMIX // Victory Royale B**ch!","OriginalArtist":"MrMoMMusic","ChannelTitle":"MrMoMMusic","Description":"LXNGVX, Warriyo - Mortals Funk Remix"}""";
        var resolver = new TrackIdentityResolver(database.Db);
        var match = await resolver.ResolveExistingAsync(new TrackIdentityQuery(
            "youtube", "mortals-video", "MORTALS FUNK REMIX // Victory Royale B**ch!",
            "MrMoMMusic", 147, RawMetadata: incomingMetadata, OwnerUserId: 42), CancellationToken.None);

        Assert.NotNull(match);
        Assert.Equal(track.Id, match.Track.Id);
        Assert.Equal(TrackIdentityMatchKind.Metadata, match.Kind);
    }

    [Fact]
    public async Task ResolveExistingAsync_OwnerObservationIsrcConflictBlocksPrivateMetadataReuse()
    {
        await using var database = await CreateDatabaseAsync();
        var track = NewTrack();
        var observation = NewObservation(track.Id, "spotify", "owned-track", "Same song", "Same artist", 180,
            ownerUserId: 42);
        observation.PrivateInputJson = JsonSerializer.Serialize(new TrackObservationMetadata { Isrc = "USABC2400001" });
        database.Db.Tracks.Add(track);
        database.Db.TrackObservations.Add(observation);
        await database.Db.SaveChangesAsync();

        var match = await new TrackIdentityResolver(database.Db).ResolveExistingAsync(new TrackIdentityQuery(
            "youtube", "new-video", "Same song", "Same artist", 180,
            Isrc: "USABC2400002", OwnerUserId: 42), CancellationToken.None);

        Assert.Null(match);
    }

    [Fact]
    public async Task ResolveExistingAsync_IneligibleVersionDoesNotBlockUniqueTrackMatch()
    {
        await using var database = await CreateDatabaseAsync();
        var normal = NewTrack();
        var live = NewTrack();
        database.Db.Tracks.AddRange(normal, live);
        database.Db.TrackObservations.AddRange(
            NewObservation(normal.Id, "spotify", "normal", "Song", "Singer", 180, ownerUserId: 42),
            NewObservation(live.Id, "spotify", "live", "Song (Live)", "Singer", 180, ownerUserId: 42));
        await database.Db.SaveChangesAsync();

        var resolver = new TrackIdentityResolver(database.Db);
        var match = await resolver.ResolveExistingAsync(new TrackIdentityQuery(
            "youtube", "new-version", "Song", "Singer", 180, OwnerUserId: 42), CancellationToken.None);

        Assert.NotNull(match);
        Assert.Equal(normal.Id, match.Track.Id);
        Assert.Equal(TrackIdentityMatchKind.Metadata, match.Kind);
    }

    [Fact]
    public async Task ResolveExistingAsync_MetadataMatchDoesNotChooseBetweenDistinctTracks()
    {
        await using var database = await CreateDatabaseAsync();
        var first = NewTrack();
        var second = NewTrack();
        database.Db.Tracks.AddRange(first, second);
        database.Db.TrackObservations.AddRange(
            NewObservation(first.Id, "spotify", "spotify-track-1", "Everything Goes On", "Porter Robinson", 202, ownerUserId: 42),
            NewObservation(second.Id, "youtube", "youtube-video-2", "Everything Goes On", "Porter Robinson", 160, ownerUserId: 42));
        await database.Db.SaveChangesAsync();

        var resolver = new TrackIdentityResolver(database.Db);
        var match = await resolver.ResolveExistingAsync(new TrackIdentityQuery(
            "youtube", "new-youtube-video", "Everything Goes On", "Porter Robinson", 180,
            OwnerUserId: 42), CancellationToken.None);

        Assert.Null(match);
    }

    [Fact]
    public async Task ResolveExistingAsync_LegacySourceMappingAndTrackIsrcDoNotCreatePublicIdentity()
    {
        await using var database = await CreateDatabaseAsync();
        var stale = NewTrack(isrc: "USRC17607839");
        var intended = NewTrack(isrc: "USRC17607840");
        database.Db.Tracks.AddRange(stale, intended);
        database.Db.ProviderCatalogItems.Add(new ProviderCatalogItem { Id = Guid.NewGuid(), TrackId = stale.Id, SourceType = "youtube", ExternalId = "youtube-video", FactsJson = "{}" });
        await database.Db.SaveChangesAsync();

        var resolver = new TrackIdentityResolver(database.Db);
        var match = await resolver.ResolveExistingAsync(new TrackIdentityQuery(
            "youtube", "youtube-video", "Everything Goes On", "Porter Robinson", 160,
            Isrc: "USRC17607840"), CancellationToken.None);

        Assert.Null(match);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolveExistingAsync_LargePrivateLibraryStillChecksTheRelevantIdentities(bool competingIdentity)
    {
        await using var database = await CreateDatabaseAsync();
        await AssertLargePrivateLibraryAsync(database.Db, competingIdentity);
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolveExistingAsync_PostgresLargePrivateLibraryUsesBoundedCandidateBatches(bool competingIdentity)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        schema.Db.Users.AddRange(TestUserFactory.Create(42, "resolver-alice@example.test"),
            TestUserFactory.Create(84, "resolver-bob@example.test"));
        await schema.Db.SaveChangesAsync();
        await AssertLargePrivateLibraryAsync(schema.Db, competingIdentity);
    }

    private static async Task AssertLargePrivateLibraryAsync(ApplicationDbContext db, bool competingIdentity)
    {
        for (var index = 0; index < 125; index++)
        {
            var unrelated = NewTrack();
            db.AddRange(unrelated, NewObservation(unrelated.Id, "spotify", $"unrelated-{index}",
                $"Unrelated item {index}", "Different performer", 180));
        }
        var expected = NewTrack();
        db.AddRange(expected, NewObservation(expected.Id, "spotify", "intended",
            "Everything Goes On", "Porter Robinson", 180));
        var other = NewTrack();
        db.AddRange(other, NewObservation(other.Id, "youtube", "other-owner",
            "Everything Goes On", "Porter Robinson", 180, ownerUserId: 84));
        if (competingIdentity)
        {
            var competing = NewTrack();
            db.AddRange(competing, NewObservation(competing.Id, "youtube", "competing",
                "Everything Goes On", "Porter Robinson", 180));
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var match = await new TrackIdentityResolver(db).ResolveExistingAsync(new TrackIdentityQuery(
            "youtube", "new-import", "Everything Goes On", "Porter Robinson", 180, OwnerUserId: 42), default);

        if (competingIdentity) Assert.Null(match);
        else Assert.Equal(expected.Id, Assert.IsType<TrackIdentityMatch>(match).Track.Id);
    }

    private static async Task<DatabaseScope> CreateDatabaseAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Users.AddRange(TestUserFactory.Create(42, "resolver-alice@example.test"),
            TestUserFactory.Create(84, "resolver-bob@example.test"));
        await db.SaveChangesAsync();
        return new DatabaseScope(db, connection);
    }

    private static Track NewTrack(string? isrc = null) => new()
    {
        Id = Guid.NewGuid(),
        Isrc = isrc,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static TrackObservation NewObservation(
        Guid trackId,
        string sourceType,
        string externalId,
        string title,
        string artist,
        int durationSeconds,
        int ownerUserId = 42) => new()
    { InputKind = ObservationInputKind.Private,
        OwnerUserId = ownerUserId,
        Id = Guid.NewGuid(),
        TrackId = trackId,
        SourceType = sourceType,
        ExternalId = externalId,
        Title = title,
        Artist = artist,

        DurationSeconds = durationSeconds,
        MatchStatus = TrackMatchingStatuses.Matched,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private sealed class DatabaseScope(ApplicationDbContext db, SqliteConnection connection) : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = db;

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
