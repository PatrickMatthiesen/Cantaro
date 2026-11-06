using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Cantaro.Api.Services.Spotify;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class SpotifyTrackResolverTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task ResolveAsync_AccountSnapshotWithoutCatalogReferenceCreatesBlankCanonicalTrack()
    {
        await using var fixture = await ResolverFixture.CreateAsync();
        var user = TestUserFactory.Create(2101, "spotify-private@example.com");
        fixture.DbContext.Users.Add(user);
        await fixture.DbContext.SaveChangesAsync();

        var track = await fixture.Resolver.ResolveAsync(
            Snapshot("spotify-private", "USSP02600001", title: "Account title", artist: "Account artist"),
            Now, CancellationToken.None, user.Id);
        await fixture.DbContext.SaveChangesAsync();

        Assert.Null(track.PreferredCatalogItemId);
        Assert.Null(track.Isrc);
        Assert.Null(track.MbidRecording);
        Assert.Empty(track.SourceIds);
        Assert.Empty(track.ArtistCredits);
        Assert.Empty(await fixture.DbContext.TrackSourceIds.ToListAsync());
    }

    [Fact]
    public async Task ResolveAsync_UsesFreshVerifiedCatalogFactsInsteadOfAccountPayload()
    {
        await using var fixture = await ResolverFixture.CreateAsync();
        var facts = Facts("Catalog title", "Catalog artist", "USSP02600001", TrackVersionFlags.RadioEdit);
        var item = fixture.AddCatalogItem("spotify", "spotify-public", facts);
        await fixture.DbContext.SaveChangesAsync();

        var track = await fixture.Resolver.ResolveAsync(
            Snapshot("spotify-public", "ACCOUNT-ISRC", title: "Account title", artist: "Account artist") with
            {
                ProviderCatalogItemId = item.Id,
                ProviderCatalogRevision = item.Revision,
                ArtistIdentities = [Credit("Account artist", "account-artist")]
            },
            Now, CancellationToken.None);
        await fixture.DbContext.SaveChangesAsync();

        var stored = await fixture.DbContext.Tracks.AsNoTracking().SingleAsync(row => row.Id == track.Id);
        Assert.Equal(item.Id, stored.PreferredCatalogItemId);
        Assert.Null(stored.CanonicalMetadata);
        Assert.Equal("Catalog title", stored.SearchTitle);
        Assert.Equal("Catalog artist", stored.SearchArtist);
        var storedFacts = JsonSerializer.Deserialize<ProviderCatalogFacts>((await fixture.DbContext.TrackSourceIds
            .Include(row => row.ProviderCatalogItem).SingleAsync()).ProviderCatalogItem!.FactsJson);
        Assert.Equal("Catalog title", storedFacts?.Title);
        Assert.Equal("Catalog artist", storedFacts?.Artist);
        Assert.Equal("USSP02600001", stored.Isrc);
        Assert.Equal(TrackVersionFlags.RadioEdit, stored.VersionFlags);
        var association = await fixture.DbContext.TrackSourceIds.Include(row => row.ProviderCatalogItem).SingleAsync();
        Assert.Equal(RecordingAssociationState.Verified, association.AssociationState);
        Assert.Equal(item.Id, association.ProviderCatalogItemId);
        Assert.Equal(item.Revision, association.CatalogRevision);
        Assert.Equal("spotify-public", association.ExternalId);
        Assert.Equal("Catalog artist", (await fixture.DbContext.TrackArtistCredits.Include(row => row.Artist)
            .SingleAsync()).Artist!.Name);
    }

    [Fact]
    public async Task ResolveAsync_ReusesExactVerifiedGlobalIdAcrossUsers()
    {
        await using var fixture = await ResolverFixture.CreateAsync();
        var alice = TestUserFactory.Create(2102, "spotify-alice@example.com");
        var bob = TestUserFactory.Create(2103, "spotify-bob@example.com");
        fixture.DbContext.Users.AddRange(alice, bob);
        var track = fixture.AddTrackWithPublicAssociation("spotify", "shared-id", Facts("Shared title", "Shared artist", "USSP02600001"));
        fixture.AddObservation(alice.Id, track.Id, "spotify", "alice-private-id", "Alice title", "Alice artist", 180);
        await fixture.DbContext.SaveChangesAsync();
        fixture.DbContext.ChangeTracker.Clear();

        var resolved = await fixture.Resolver.ResolveAsync(
            Snapshot("shared-id", "OTHER-ISRC", title: "Bob's account title", artist: "Bob's artist"),
            Now, CancellationToken.None, bob.Id);
        await fixture.DbContext.SaveChangesAsync();

        Assert.Equal(track.Id, resolved.Id);
        Assert.Equal(1, await fixture.DbContext.Tracks.CountAsync());
        Assert.Null(resolved.CanonicalMetadata);
        Assert.Equal("Shared title", resolved.SearchTitle);
        Assert.NotEqual("Bob's account title", resolved.SearchTitle);
        Assert.Equal("Shared title", JsonSerializer.Deserialize<ProviderCatalogFacts>((await fixture.DbContext.TrackSourceIds
            .Include(row => row.ProviderCatalogItem).SingleAsync()).ProviderCatalogItem!.FactsJson)!.Title);
    }

    [Fact]
    public async Task ResolveAsync_PrivateObservationLinkDoesNotVerifyItsCanonicalAssociation()
    {
        await using var fixture = await ResolverFixture.CreateAsync();
        var alice = TestUserFactory.Create(2191, "private-origin-alice@example.com");
        var bob = TestUserFactory.Create(2192, "private-origin-bob@example.com");
        fixture.DbContext.Users.AddRange(alice, bob);
        var track = fixture.AddTrackWithSong(null);
        fixture.AddObservation(alice.Id, track.Id, "youtube", "private-studio", "Alice private title", "Artist", 180);
        fixture.AddObservation(bob.Id, track.Id, "spotify", "public-live", "Bob private title", "Artist", 180);
        var item = fixture.AddCatalogItem("spotify", "public-live", Facts("Song (Live)", "Artist", "USLIVE260001", TrackVersionFlags.Live));
        await fixture.DbContext.SaveChangesAsync();
        fixture.DbContext.ChangeTracker.Clear();

        var resolved = await fixture.Resolver.ResolveAsync(Snapshot("public-live", "USLIVE260001") with
        {
            ProviderCatalogItemId = item.Id, ProviderCatalogRevision = item.Revision
        }, Now, default, bob.Id);
        await fixture.DbContext.SaveChangesAsync();

        Assert.Equal(track.Id, resolved.Id);
        Assert.Null(resolved.PreferredCatalogItemId);
        Assert.Null(resolved.Isrc);
        Assert.Empty(await fixture.DbContext.TrackSourceIds.ToListAsync());
        Assert.Equal("Alice private title", (await fixture.DbContext.TrackObservations.SingleAsync(row => row.OwnerUserId == alice.Id)).Title);
        Assert.NotNull(ProviderCatalogPolicy.ReadFacts(await fixture.DbContext.ProviderCatalogItems.SingleAsync(), Now));
    }

    [Fact]
    public async Task ResolveAsync_ReusesCanonicalForDifferentVerifiedPublicIdsWithSameRecordingFacts()
    {
        await using var fixture = await ResolverFixture.CreateAsync();
        var facts = Facts("Shared recording", "Shared artist", "USSP02600001");
        var firstItem = fixture.AddCatalogItem("spotify", "spotify-version-one", facts);
        await fixture.DbContext.SaveChangesAsync();
        var first = await fixture.Resolver.ResolveAsync(Snapshot("spotify-version-one", "ignored", "Shared recording", "Shared artist") with
        {
            ProviderCatalogItemId = firstItem.Id, ProviderCatalogRevision = firstItem.Revision
        }, Now, CancellationToken.None);
        await fixture.DbContext.SaveChangesAsync();

        var secondItem = fixture.AddCatalogItem("spotify", "spotify-version-two", facts);
        await fixture.DbContext.SaveChangesAsync();
        fixture.DbContext.ChangeTracker.Clear();
        var second = await fixture.Resolver.ResolveAsync(Snapshot("spotify-version-two", "USSP02600001", "Shared recording", "Shared artist") with
        {
            ProviderCatalogItemId = secondItem.Id, ProviderCatalogRevision = secondItem.Revision
        }, Now, CancellationToken.None);
        await fixture.DbContext.SaveChangesAsync();

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await fixture.DbContext.Tracks.CountAsync());
        Assert.Equal(2, await fixture.DbContext.TrackSourceIds.CountAsync());
        Assert.All(await fixture.DbContext.TrackSourceIds.ToListAsync(), row =>
        {
            Assert.Equal(first.Id, row.TrackId);
            Assert.Equal(RecordingAssociationState.Verified, row.AssociationState);
        });
    }

    [Fact]
    public async Task ResolveAsync_RevalidatesSameProviderOriginAfterCatalogArtworkRevision()
    {
        await using var fixture = await ResolverFixture.CreateAsync();
        var originalFacts = Facts("Verified title", "Verified artist", "USSP02600001");
        var item = fixture.AddCatalogItem("spotify", "stable-provider-id", originalFacts);
        await fixture.DbContext.SaveChangesAsync();
        var first = await fixture.Resolver.ResolveAsync(Snapshot("stable-provider-id", "USSP02600001",
            "Verified title", "Verified artist") with
        {
            ProviderCatalogItemId = item.Id, ProviderCatalogRevision = item.Revision
        }, Now, CancellationToken.None);
        await fixture.DbContext.SaveChangesAsync();

        var updatedFacts = CopyFacts(originalFacts, thumbnailUrl: "https://i.scdn.co/image/refreshed");
        item.FactsJson = JsonSerializer.Serialize(updatedFacts);
        item.Revision++;
        item.VerifiedAt = Now;
        item.RevalidateAfter = Now.AddDays(1);
        await fixture.DbContext.SaveChangesAsync();
        fixture.DbContext.ChangeTracker.Clear();

        var refreshedItem = await fixture.DbContext.ProviderCatalogItems.SingleAsync();
        var refreshed = await fixture.Resolver.ResolveAsync(Snapshot("stable-provider-id", "ACCOUNT-ISRC",
            "Account title changed", "Account artist changed") with
        {
            ProviderCatalogItemId = refreshedItem.Id, ProviderCatalogRevision = refreshedItem.Revision
        }, Now, CancellationToken.None);
        await fixture.DbContext.SaveChangesAsync();

        Assert.Equal(first.Id, refreshed.Id);
        Assert.Equal(item.Id, refreshed.PreferredCatalogItemId);
        var mapping = await fixture.DbContext.TrackSourceIds.SingleAsync();
        Assert.Equal(first.Id, mapping.TrackId);
        Assert.Equal(refreshedItem.Revision, mapping.CatalogRevision);
        Assert.Equal(RecordingAssociationState.Verified, mapping.AssociationState);
        var refreshedFacts = JsonSerializer.Deserialize<ProviderCatalogFacts>((await fixture.DbContext.ProviderCatalogItems
            .SingleAsync()).FactsJson)!;
        Assert.Equal("https://i.scdn.co/image/refreshed", refreshedFacts.ThumbnailUrl);
        Assert.Equal("Verified title", refreshedFacts.Title);
    }

    [Theory]
    [InlineData("isrc")]
    [InlineData("version")]
    public async Task ResolveAsync_RejectsExactRevalidationWhenStableEvidenceChanges(string change)
    {
        await using var fixture = await ResolverFixture.CreateAsync();
        var originalFacts = Facts("Verified title", "Verified artist", "USSP02600001");
        var item = fixture.AddCatalogItem("spotify", "stable-provider-id", originalFacts);
        await fixture.DbContext.SaveChangesAsync();
        var original = await fixture.Resolver.ResolveAsync(Snapshot("stable-provider-id", "USSP02600001",
            "Verified title", "Verified artist") with
        {
            ProviderCatalogItemId = item.Id, ProviderCatalogRevision = item.Revision
        }, Now, CancellationToken.None);
        await fixture.DbContext.SaveChangesAsync();

        var changedFacts = change == "isrc"
            ? CopyFacts(originalFacts, isrc: "USSP02600002")
            : CopyFacts(originalFacts, versionFlags: TrackVersionFlags.Acoustic);
        item.FactsJson = JsonSerializer.Serialize(changedFacts);
        item.Revision++;
        item.VerifiedAt = Now;
        item.RevalidateAfter = Now.AddDays(1);
        await fixture.DbContext.SaveChangesAsync();
        fixture.DbContext.ChangeTracker.Clear();

        var refreshedItem = await fixture.DbContext.ProviderCatalogItems.SingleAsync();
        var rejected = await fixture.Resolver.ResolveAsync(Snapshot("stable-provider-id", "ACCOUNT-ISRC",
            "Account title changed", "Account artist changed") with
        {
            ProviderCatalogItemId = refreshedItem.Id, ProviderCatalogRevision = refreshedItem.Revision
        }, Now, CancellationToken.None);
        await fixture.DbContext.SaveChangesAsync();

        Assert.NotEqual(original.Id, rejected.Id);
        Assert.Null(rejected.PreferredCatalogItemId);
        Assert.Equal(2, await fixture.DbContext.Tracks.CountAsync());
        var mapping = await fixture.DbContext.TrackSourceIds.SingleAsync();
        Assert.Equal(original.Id, mapping.TrackId);
        Assert.Equal(1, mapping.CatalogRevision);
        Assert.Equal(RecordingAssociationState.Verified, mapping.AssociationState);
    }

    [Fact]
    public async Task ResolveAsync_IgnoresLegacyIsrcAndUnknownGlobalMapping()
    {
        await using var fixture = await ResolverFixture.CreateAsync();
        var legacy = fixture.AddTrackWithSong("USSP02600001");
        fixture.DbContext.TrackSourceIds.Add(new TrackSourceId
        {
            Id = Guid.NewGuid(), TrackId = legacy.Id, SourceType = "spotify", ExternalId = "legacy-id",
            Confidence = 1m, LastVerifiedAt = Now
        });
        await fixture.DbContext.SaveChangesAsync();
        fixture.DbContext.ChangeTracker.Clear();

        var resolved = await fixture.Resolver.ResolveAsync(Snapshot("legacy-id", "USSP02600001"), Now, CancellationToken.None);
        await fixture.DbContext.SaveChangesAsync();

        Assert.NotEqual(legacy.Id, resolved.Id);
        Assert.Null(resolved.PreferredCatalogItemId);
        Assert.Null(resolved.Isrc);
        var legacyMapping = await fixture.DbContext.TrackSourceIds.SingleAsync(row => row.ExternalId == "legacy-id");
        Assert.Equal(RecordingAssociationState.Unknown, legacyMapping.AssociationState);
        Assert.Null(legacyMapping.ProviderCatalogItemId);
    }

    [Fact]
    public async Task ResolveAsync_ReusesOnlyTheRequestersPrivateObservation()
    {
        await using var fixture = await ResolverFixture.CreateAsync();
        var alice = TestUserFactory.Create(2104, "spotify-observation-alice@example.com");
        var bob = TestUserFactory.Create(2105, "spotify-observation-bob@example.com");
        fixture.DbContext.Users.AddRange(alice, bob);
        var aliceTrack = fixture.AddTrackWithSong(null);
        var referenceTrack = fixture.AddTrackWithSong(null);
        fixture.AddObservation(alice.Id, aliceTrack.Id, "spotify", "alice-id", "Private title", "Private artist", 180);
        fixture.AddObservation(alice.Id, referenceTrack.Id, "spotify", "expired-id", "Expired title", "Expired artist", 180,
            referenceOnly: true);
        await fixture.DbContext.SaveChangesAsync();
        fixture.DbContext.ChangeTracker.Clear();

        var aliceResolved = await fixture.Resolver.ResolveAsync(
            Snapshot("alice-id", null, title: "Private title", artist: "Private artist"), Now, CancellationToken.None, alice.Id);
        var bobResolved = await fixture.Resolver.ResolveAsync(
            Snapshot("alice-id", null, title: "Private title", artist: "Private artist"), Now, CancellationToken.None, bob.Id);
        var referenceResolved = await fixture.Resolver.ResolveAsync(
            Snapshot("expired-id", null, title: "Expired title", artist: "Expired artist"), Now, CancellationToken.None, alice.Id);
        await fixture.DbContext.SaveChangesAsync();

        Assert.Equal(aliceTrack.Id, aliceResolved.Id);
        Assert.NotEqual(aliceTrack.Id, bobResolved.Id);
        Assert.NotEqual(referenceTrack.Id, referenceResolved.Id);
        Assert.Null(bobResolved.PreferredCatalogItemId);
        Assert.Null(referenceResolved.PreferredCatalogItemId);
    }

    [Theory]
    [InlineData("wrong-key")]
    [InlineData("stale-revision")]
    public async Task ResolveAsync_ForgedOrStaleCatalogReferenceCannotPromoteSnapshot(string referenceFault)
    {
        await using var fixture = await ResolverFixture.CreateAsync();
        var item = fixture.AddCatalogItem("spotify", "catalog-key", Facts("Catalog title", "Catalog artist", "USSP02600001"));
        await fixture.DbContext.SaveChangesAsync();
        var snapshot = Snapshot("account-key", "USSP02600001", title: "Account title", artist: "Account artist") with
        {
            ProviderCatalogItemId = item.Id,
            ProviderCatalogRevision = referenceFault == "stale-revision" ? item.Revision - 1 : item.Revision
        };

        var track = await fixture.Resolver.ResolveAsync(snapshot, Now, CancellationToken.None);
        await fixture.DbContext.SaveChangesAsync();

        Assert.Null(track.PreferredCatalogItemId);
        Assert.Null(track.Isrc);
        Assert.Empty(await fixture.DbContext.TrackSourceIds.ToListAsync());
        Assert.Equal(1, await fixture.DbContext.Tracks.CountAsync());
        if (referenceFault == "wrong-key") Assert.Equal("catalog-key", item.ExternalId);
    }

    [Theory]
    [InlineData("withdrawn")]
    [InlineData("expired")]
    public async Task ResolveAsync_WithdrawnOrExpiredCatalogItemCannotPromoteSnapshot(string catalogState)
    {
        await using var fixture = await ResolverFixture.CreateAsync();
        var item = fixture.AddCatalogItem("spotify", "spotify-id", Facts("Catalog title", "Catalog artist", "USSP02600001"));
        if (catalogState == "withdrawn") item.VisibilityState = CatalogVisibility.Withdrawn;
        else item.RevalidateAfter = Now.AddSeconds(-1);
        await fixture.DbContext.SaveChangesAsync();

        var track = await fixture.Resolver.ResolveAsync(Snapshot("spotify-id", "ACCOUNT-ISRC" ) with
        {
            ProviderCatalogItemId = item.Id, ProviderCatalogRevision = item.Revision
        }, Now, CancellationToken.None);
        await fixture.DbContext.SaveChangesAsync();

        Assert.Null(track.PreferredCatalogItemId);
        Assert.Null(track.Isrc);
        Assert.Empty(await fixture.DbContext.TrackSourceIds.ToListAsync());
    }

    [Theory]
    [InlineData("isrc")]
    [InlineData("version")]
    public async Task ResolveAsync_DoesNotAssociateCatalogItemWhenStableEvidenceConflicts(string conflictKind)
    {
        await using var fixture = await ResolverFixture.CreateAsync();
        var owner = TestUserFactory.Create(2106, $"spotify-conflict-{conflictKind}@example.com");
        fixture.DbContext.Users.Add(owner);
        var existingFacts = Facts("Same title", "Same artist", "USSP02600001", TrackVersionFlags.None);
        var existing = fixture.AddTrackWithPublicAssociation("musicbrainz", "recording-one", existingFacts);
        fixture.AddObservation(owner.Id, existing.Id, "spotify", "incoming-id", "Same title", "Same artist", 180);
        var proposedFacts = conflictKind == "isrc"
            ? Facts("Same title", "Same artist", "USSP02600002", TrackVersionFlags.None)
            : Facts("Same title", "Same artist", "USSP02600001", TrackVersionFlags.Acoustic);
        var proposedItem = fixture.AddCatalogItem("spotify", "incoming-id", proposedFacts);
        await fixture.DbContext.SaveChangesAsync();
        fixture.DbContext.ChangeTracker.Clear();

        var resolved = await fixture.Resolver.ResolveAsync(Snapshot("incoming-id", "ACCOUNT-ISRC", title: "Same title", artist: "Same artist") with
        {
            ProviderCatalogItemId = proposedItem.Id, ProviderCatalogRevision = proposedItem.Revision
        }, Now, CancellationToken.None, owner.Id);
        await fixture.DbContext.SaveChangesAsync();

        Assert.Equal(existing.Id, resolved.Id);
        Assert.Equal("recording-one", Assert.Single(await fixture.DbContext.TrackSourceIds
            .Where(row => row.TrackId == existing.Id).ToListAsync()).ExternalId);
        Assert.Equal(existing.PreferredCatalogItemId, resolved.PreferredCatalogItemId);
        Assert.Equal(2, await fixture.DbContext.ProviderCatalogItems.CountAsync());
    }

    [Fact]
    public async Task ResolveAsync_DoesNotSilentlyRepointAnExistingProviderMapping()
    {
        await using var fixture = await ResolverFixture.CreateAsync();
        var owner = TestUserFactory.Create(2107, "spotify-no-repoint@example.com");
        fixture.DbContext.Users.Add(owner);
        var staleTrack = fixture.AddTrackWithSong(null);
        var correctFacts = Facts("Same title", "Same artist", "USSP02600001");
        var correctTrack = fixture.AddTrackWithPublicAssociation("musicbrainz", "recording-correct", correctFacts);
        fixture.AddObservation(owner.Id, correctTrack.Id, "spotify", "spotify-existing-key", "Same title", "Same artist", 180);
        fixture.DbContext.TrackSourceIds.Add(new TrackSourceId
        {
            Id = Guid.NewGuid(), TrackId = staleTrack.Id, SourceType = "spotify", ExternalId = "spotify-existing-key",
            AssociationState = RecordingAssociationState.Unknown
        });
        var item = fixture.AddCatalogItem("spotify", "spotify-existing-key", correctFacts);
        await fixture.DbContext.SaveChangesAsync();
        fixture.DbContext.ChangeTracker.Clear();

        var resolved = await fixture.Resolver.ResolveAsync(Snapshot("spotify-existing-key", "ACCOUNT-ISRC",
            "Same title", "Same artist") with
        {
            ProviderCatalogItemId = item.Id, ProviderCatalogRevision = item.Revision
        }, Now, CancellationToken.None, owner.Id);
        await fixture.DbContext.SaveChangesAsync();

        Assert.Equal(correctTrack.Id, resolved.Id);
        var mapping = await fixture.DbContext.TrackSourceIds.SingleAsync(row => row.ExternalId == "spotify-existing-key");
        Assert.Equal(staleTrack.Id, mapping.TrackId);
        Assert.Equal(RecordingAssociationState.Conflicted, mapping.AssociationState);
        Assert.Null(mapping.ProviderCatalogItemId);
        Assert.Equal(correctTrack.PreferredCatalogItemId, (await fixture.DbContext.Tracks
            .SingleAsync(row => row.Id == correctTrack.Id)).PreferredCatalogItemId);
    }

    private static SpotifyTrackSnapshot Snapshot(string id, string? isrc, string title = "Track title", string artist = "Artist") =>
        new(id, title, artist, [artist], "Account album", "https://i.scdn.co/image/account", $"https://open.spotify.com/track/{id}",
            "https://open.spotify.com/album/account", isrc, 180, Now, 0)
        {
            ArtistIdentities = [Credit(artist, $"account-{id}")]
        };

    private static ProviderCatalogFacts Facts(string title, string artist, string? isrc,
        TrackVersionFlags flags = TrackVersionFlags.None) => new()
    {
        Title = title, Artist = artist, Albums = ["Catalog album"], ThumbnailUrl = "https://i.scdn.co/image/catalog",
        ProviderUrl = "https://open.spotify.com/track/catalog", Isrc = isrc, DurationSeconds = 180,
        ArtistCredits = [Credit(artist, "catalog-artist")], VersionFlags = flags
    };

    private static ProviderCatalogFacts CopyFacts(ProviderCatalogFacts source, string? thumbnailUrl = null,
        string? isrc = null, TrackVersionFlags? versionFlags = null) => new()
    {
        Title = source.Title, Artist = source.Artist, Albums = source.Albums,
        ThumbnailUrl = thumbnailUrl ?? source.ThumbnailUrl, ProviderUrl = source.ProviderUrl,
        DurationSeconds = source.DurationSeconds, Isrc = isrc ?? source.Isrc,
        MusicBrainzRecordingId = source.MusicBrainzRecordingId, ArtistCredits = source.ArtistCredits,
        VersionFlags = versionFlags ?? source.VersionFlags
    };

    private static ArtistCreditMetadata Credit(string name, string externalId) => new()
    {
        Name = name, Provider = "spotify", ExternalId = externalId, Role = TrackArtistRole.Primary
    };

    private sealed class ResolverFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private ResolverFixture(SqliteConnection connection, ApplicationDbContext dbContext)
        {
            _connection = connection;
            DbContext = dbContext;
            Resolver = new SpotifyTrackResolver(dbContext);
        }

        public ApplicationDbContext DbContext { get; }
        public SpotifyTrackResolver Resolver { get; }

        public static async Task<ResolverFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
            var db = new ApplicationDbContext(options);
            await db.Database.EnsureCreatedAsync();
            return new ResolverFixture(connection, db);
        }

        public ProviderCatalogItem AddCatalogItem(string source, string id, ProviderCatalogFacts facts)
        {
            var item = new ProviderCatalogItem
            {
                Id = Guid.NewGuid(), SourceType = source, ExternalId = id, Revision = 1,
                FactsJson = JsonSerializer.Serialize(facts), VerificationMethod = source == "spotify"
                    ? CatalogVerificationMethods.SpotifyApplication : CatalogVerificationMethods.MusicBrainz,
                VisibilityState = CatalogVisibility.Public, VerifiedAt = Now, RevalidateAfter = Now.AddDays(1)
            };
            DbContext.ProviderCatalogItems.Add(item);
            return item;
        }

        public Track AddTrackWithPublicAssociation(string source, string id, ProviderCatalogFacts facts)
        {
            var track = AddTrackWithSong(null);
            var item = AddCatalogItem(source, id, facts);
            track.PreferredCatalogItem = item;
            track.PreferredCatalogItemId = item.Id;
            track.SearchTitle = facts.Title;
            track.SearchArtist = facts.Artist;
            track.Isrc = facts.Isrc;
            track.VersionFlags = facts.VersionFlags;
            track.SourceIds.Add(new TrackSourceId
            {
                Id = Guid.NewGuid(), TrackId = track.Id, SourceType = source, ExternalId = id,
                ProviderCatalogItem = item, ProviderCatalogItemId = item.Id, CatalogRevision = item.Revision,
                AssociationState = RecordingAssociationState.Verified, AssociationMethod = "test_fixture",
                AssociationVerifiedAt = Now
            });
            return track;
        }

        public Track AddTrackWithSong(string? isrc)
        {
            var song = new Song { Id = Guid.NewGuid(), CreatedAt = Now, UpdatedAt = Now };
            var track = new Track { Id = Guid.NewGuid(), Isrc = isrc, CreatedAt = Now, UpdatedAt = Now };
            DbContext.AddRange(song, track, new SongTrack { SongId = song.Id, Song = song, TrackId = track.Id, Track = track });
            return track;
        }

        public void AddObservation(int ownerId, Guid trackId, string source, string id, string title, string artist,
            int duration, bool referenceOnly = false)
        {
            DbContext.TrackObservations.Add(new TrackObservation
            {
                Id = Guid.NewGuid(), OwnerUserId = ownerId,
                SourceType = source, ExternalId = id, Title = referenceOnly ? "" : title, Artist = referenceOnly ? null : artist,
                NormalizedTitle = TrackTextNormalizer.Normalize(title), NormalizedArtist = TrackTextNormalizer.Normalize(artist),
                DurationSeconds = duration, MatchStatus = TrackMatchingStatuses.Matched, TrackId = trackId,
                CreatedAt = Now, UpdatedAt = Now
            });
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
