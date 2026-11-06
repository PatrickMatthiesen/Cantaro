using System.Net;
using System.Text.Json;
using Cantaro.Api.Configuration;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Cantaro.Api.Services.Spotify;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class ProviderCatalogOwnershipTests
{
    [Fact]
    public async Task ScopedSnapshotsDivergeWithoutDuplicatingSharedProviderIdentity()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.Users.AddRange(TestUserFactory.Create(1, "alice@example.test"), TestUserFactory.Create(2, "bob@example.test"));
        var item = new ProviderCatalogItem { Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = "same",
            FactsJson = "{}" };
        db.ProviderCatalogItems.Add(item);
        var alice = Observation(1, "Alice private title", item.Id);
        var bob = Observation(2, "Bob private title", item.Id);
        db.TrackObservations.AddRange(alice, bob);
        await db.SaveChangesAsync();
        alice.Title = "Alice changed";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Equal("Bob private title", (await db.TrackObservations.SingleAsync(x => x.OwnerUserId == 2)).Title);
        Assert.Single(await db.ProviderCatalogItems.ToArrayAsync());
        Assert.Equal(CatalogVisibility.Unknown, (await db.ProviderCatalogItems.SingleAsync()).VisibilityState);
        db.TrackObservations.Add(Observation(1, "Duplicate", item.Id));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MigrationRetainsReferencesDiscardsSnapshotsAndRefusesUnsafeDowngrade(bool populated)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        var db = schema.Db;
        var schemaName = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()).SearchPath!;
        Assert.Matches("^cantaro_sync_test_[a-f0-9]+$", schemaName);
        await db.Database.ExecuteSqlRawAsync($"DROP SCHEMA \"{schemaName}\" CASCADE");
        await db.Database.ExecuteSqlRawAsync($"CREATE SCHEMA \"{schemaName}\"");

        var migrator = db.GetService<IMigrator>();
        var previous = db.Database.GetMigrations().Last(migration =>
            !migration.EndsWith("SeparateProviderCatalogAndObservationOwnership", StringComparison.Ordinal));
        await migrator.MigrateAsync(previous);

        var originalId = Guid.NewGuid();
        var originalTrackId = Guid.NewGuid();
        var legacySourceMappingId = Guid.NewGuid();
        var orphanId = Guid.NewGuid();
        var ownerOnePlaylistId = Guid.NewGuid();
        var ownerTwoPlaylistId = Guid.NewGuid();
        var receiptPlaylistId = Guid.NewGuid();
        var receiptMappingId = Guid.NewGuid();
        var receiptEntryId = Guid.NewGuid();
        var ownerOneFirstEntryId = Guid.NewGuid();
        var ownerOneSecondEntryId = Guid.NewGuid();
        var ownerTwoEntryId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var updatedAt = new DateTimeOffset(2024, 2, 3, 4, 5, 6, TimeSpan.Zero);
        var firstAddedAt = new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var secondAddedAt = new DateTimeOffset(2025, 1, 3, 3, 4, 5, TimeSpan.Zero);
        var thirdAddedAt = new DateTimeOffset(2025, 1, 4, 3, 4, 5, TimeSpan.Zero);

        (string Baseline, string Pending) receipts = ("", "");
        if (populated)
        {
            receipts = await SeedOldOwnershipSchemaAsync(db, originalId, orphanId, originalTrackId, legacySourceMappingId,
                ownerOnePlaylistId, ownerTwoPlaylistId, receiptPlaylistId, receiptMappingId, receiptEntryId,
                ownerOneFirstEntryId, ownerOneSecondEntryId, ownerTwoEntryId,
                createdAt, updatedAt, firstAddedAt, secondAddedAt, thirdAddedAt);
        }

        await migrator.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Empty(await db.ProviderCatalogItems.ToArrayAsync());
        Assert.Empty(await db.TrackSourceIds.ToArrayAsync());

        var observations = await db.TrackObservations.OrderBy(observation => observation.OwnerUserId).ToArrayAsync();
        Assert.Equal(populated ? 3 : 0, observations.Length);
        if (populated)
        {
            var ownerOne = Assert.Single(observations.Where(observation => observation.OwnerUserId == 1));
            var ownerTwo = Assert.Single(observations.Where(observation => observation.OwnerUserId == 2));
            var receiptOwner = Assert.Single(observations.Where(observation => observation.OwnerUserId == 3));
            Assert.Equal(originalId, ownerOne.Id);
            Assert.NotEqual(ownerOne.Id, ownerTwo.Id);
            Assert.NotEqual(ownerOne.Id, receiptOwner.Id);
            Assert.All(observations, observation =>
            {
                Assert.Equal("spotify", observation.SourceType);
                Assert.Equal("same", observation.ExternalId);
                Assert.Equal(originalTrackId, observation.TrackId);
                Assert.Equal(createdAt, observation.CreatedAt);
                Assert.Equal(updatedAt, observation.UpdatedAt);
                Assert.Equal(string.Empty, observation.Title);
                Assert.Null(observation.RawMetadata);
                Assert.Null(observation.Artist);
                Assert.Null(observation.ThumbnailUrl);
                Assert.Null(observation.DurationSeconds);
                Assert.Null(observation.NormalizedTitle);
                Assert.Null(observation.NormalizedArtist);
                Assert.Null(observation.ResolutionNotes);
                Assert.Null(observation.AcceptedCandidateId);
                Assert.Equal(0, observation.MatchAttemptCount);
                Assert.Null(observation.LastMatchAttemptedAt);
                Assert.Null(observation.LastMatchError);
                Assert.Equal(TrackMatchingStatuses.Matched, observation.MatchStatus);
                Assert.Equal(originalTrackId, observation.TrackId);
            });
            Assert.False(await db.TrackObservations.AnyAsync(observation => observation.Id == orphanId));

            var entries = await db.PlaylistEntries.AsNoTracking().Include(entry => entry.Playlist)
                .OrderBy(entry => entry.Playlist!.UserId)
                .ThenBy(entry => entry.Position).ToArrayAsync();
            Assert.Equal(3, entries.Length);
            Assert.Collection(entries,
                entry => AssertEntry(entry, ownerOneFirstEntryId, ownerOnePlaylistId, 2, firstAddedAt, originalTrackId, ownerOne.Id),
                entry => AssertEntry(entry, ownerOneSecondEntryId, ownerOnePlaylistId, 6, secondAddedAt, originalTrackId, ownerOne.Id),
                entry => AssertEntry(entry, ownerTwoEntryId, ownerTwoPlaylistId, 4, thirdAddedAt, originalTrackId, ownerTwo.Id));

            var mapping = await db.ServicePlaylistMappings.SingleAsync(item => item.Id == receiptMappingId);
            Assert.Null(mapping.MatchingProgressJson);
            Assert.Equal(0, await db.PlaylistEntries.CountAsync(entry => entry.PlaylistId == receiptPlaylistId));
            AssertReceipt(mapping.BaselineJson, receipts.Baseline, "baseline-revision", receiptOwner.Id,
                originalTrackId, receiptEntryId, receiptPlaylistId, receiptMappingId);
            AssertReceipt(mapping.PendingWriteJson, receipts.Pending, "pending-revision", receiptOwner.Id,
                originalTrackId, receiptEntryId, receiptPlaylistId, receiptMappingId);

            var attempt = Assert.Single(await db.PlaylistExportAttempts.ToArrayAsync());
            Assert.Equal((3, "spotify", "spotify-owner-3", "remote-playlist", "remote-a"),
                (attempt.UserId, attempt.Service, attempt.ExternalAccountId, attempt.ServicePlaylistId, attempt.ExternalTrackId));
            await AssertDisconnectedAccountMakesNoHttpRequestAsync(db);

            ownerTwo.Title = "Bob refreshed private snapshot";
            await db.SaveChangesAsync();
            var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(previous));
            Assert.Contains("Unsafe downgrade", error.MessageText);
            Assert.Equal(3, await db.TrackObservations.CountAsync());
        }
        else
        {
            await migrator.MigrateAsync(previous);
            await migrator.MigrateAsync();
        }
    }

    private static async Task<(string Baseline, string Pending)> SeedOldOwnershipSchemaAsync(
        ApplicationDbContext db, Guid observationId,
        Guid orphanId, Guid trackId, Guid legacySourceMappingId, Guid ownerOnePlaylistId,
        Guid ownerTwoPlaylistId, Guid receiptPlaylistId,
        Guid mappingId, Guid receiptEntryId, Guid ownerOneFirstEntryId, Guid ownerOneSecondEntryId,
        Guid ownerTwoEntryId, DateTimeOffset createdAt, DateTimeOffset updatedAt,
        DateTimeOffset firstAddedAt, DateTimeOffset secondAddedAt, DateTimeOffset thirdAddedAt)
    {
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "AspNetUsers" ("Id", "UserName", "NormalizedUserName", "EmailConfirmed", "PhoneNumberConfirmed",
                "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
            VALUES (1, 'alice', 'ALICE', FALSE, FALSE, FALSE, FALSE, 0),
                (2, 'bob', 'BOB', FALSE, FALSE, FALSE, FALSE, 0),
                (3, 'receipt-owner', 'RECEIPT-OWNER', FALSE, FALSE, FALSE, FALSE, 0);
            INSERT INTO "ConnectedServiceAccounts" ("Id", "UserId", "Service", "ExternalAccountId", "ConnectionState", "TokenVersion")
            VALUES (303, 3, 'spotify', 'spotify-owner-3', 'disconnected', 4);
            """);

        await db.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO "Tracks" ("Id", "SearchTitle", "SearchArtist", "CanonicalMetadata", "Isrc", "MbidRecording",
                "VersionEvidence", "VersionFlags", "CreatedAt", "UpdatedAt")
            VALUES ({{trackId}}, 'Private title', 'Private artist', '{"title":"Private title"}', 'USABC2400001',
                'recording-1', '{}'::jsonb, 5, {{createdAt}}, {{updatedAt}});
            INSERT INTO "TrackObservations" ("Id", "SourceType", "ExternalId", "Title", "Artist", "ThumbnailUrl",
                "RawMetadata", "NormalizedTitle", "NormalizedArtist", "DurationSeconds", "MatchStatus", "TrackId",
                "MatchAttemptCount", "LastMatchAttemptedAt", "LastMatchError", "ResolutionNotes", "AcceptedCandidateId",
                "CreatedAt", "UpdatedAt")
            VALUES ({{observationId}}, 'spotify', 'same', 'Private snapshot', 'Private artist', 'https://private.test/art.jpg',
                '{"payload":"private"}', 'private snapshot', 'private artist', 181, 'matched', {{trackId}}, 9,
                {{updatedAt}}, 'old error', 'old diagnostic', {{Guid.NewGuid()}}, {{createdAt}}, {{updatedAt}}),
                ({{orphanId}}, 'youtube', 'orphan', 'Orphan private snapshot', 'Orphan artist', 'https://private.test/orphan.jpg',
                '{"payload":"orphan"}', 'orphan private snapshot', 'orphan artist', 220, 'pending', NULL, 3,
                {{updatedAt}}, 'orphan error', 'orphan diagnostic', NULL, {{createdAt}}, {{updatedAt}});
            INSERT INTO "TrackSourceIds" ("Id", "TrackId", "SourceType", "ExternalId", "Confidence", "IsOfficial",
                "LastVerifiedAt", "OriginMetadata", "PresentationKind", "PresentationConfidence")
            VALUES ({{legacySourceMappingId}}, {{trackId}}, 'spotify', 'legacy-source-id', 0.99, TRUE,
                {{updatedAt}}, '{"origin":"legacy"}', 4, 90);
            INSERT INTO "Playlists" ("Id", "UserId", "Name", "CreatedAt", "UpdatedAt", "AllowDuplicateTracks", "SyncEnabled", "SyncRevision")
            VALUES ({{ownerOnePlaylistId}}, 1, 'Duplicate source entries', {{createdAt}}, {{updatedAt}}, TRUE, FALSE, 8),
                ({{ownerTwoPlaylistId}}, 2, 'Other owner entry', {{createdAt}}, {{updatedAt}}, FALSE, FALSE, 3),
                ({{receiptPlaylistId}}, 3, 'Receipt only', {{createdAt}}, {{updatedAt}}, FALSE, FALSE, 5);
            """);

        var baseline = ReceiptJson(trackId, observationId, "baseline-revision", reverse: false);
        var pending = ReceiptJson(trackId, observationId, "pending-revision", reverse: true);
        var progress = ProgressJson(receiptEntryId, receiptPlaylistId, mappingId);

        await db.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO "PlaylistEntries" ("Id", "PlaylistId", "TrackId", "TrackObservationId", "Position", "AddedAt", "SourceService")
            VALUES ({{ownerOneFirstEntryId}}, {{ownerOnePlaylistId}}, {{trackId}}, {{observationId}}, 2, {{firstAddedAt}}, 'spotify'),
                ({{ownerOneSecondEntryId}}, {{ownerOnePlaylistId}}, {{trackId}}, {{observationId}}, 6, {{secondAddedAt}}, 'spotify'),
                ({{ownerTwoEntryId}}, {{ownerTwoPlaylistId}}, {{trackId}}, {{observationId}}, 4, {{thirdAddedAt}}, 'spotify');
            INSERT INTO "ServicePlaylistMappings" ("Id", "PlaylistId", "ConnectedServiceAccountId", "UserId",
                "ExternalAccountId", "Service", "ServicePlaylistId", "SyncMode", "State", "BaselineJson", "PendingWriteJson",
                "MatchingProgressJson")
            VALUES ({{mappingId}}, {{receiptPlaylistId}}, 303, 3, 'spotify-owner-3', 'spotify', 'remote-playlist',
                'bidirectional', 'active', {{baseline}}, {{pending}}, {{progress}});
            INSERT INTO "PlaylistExportAttempts" ("UserId", "Service", "ExternalAccountId", "ServicePlaylistId", "ExternalTrackId")
            VALUES (3, 'spotify', 'spotify-owner-3', 'remote-playlist', 'remote-a');
            """);
        return (baseline, pending);
    }

    private static string ReceiptJson(Guid trackId, Guid observationId, string providerRevision, bool reverse)
    {
        var first = new { TrackId = trackId, ObservationId = observationId, ExternalId = "remote-a", Service = "spotify" };
        var second = new { TrackId = trackId, ObservationId = observationId, ExternalId = "remote-b", Service = "spotify" };
        return JsonSerializer.Serialize(new
        {
            Items = reverse ? new[] { second, first } : new[] { first, second },
            ProviderRevision = providerRevision
        });
    }

    private static string ProgressJson(Guid entryId, Guid playlistId, Guid mappingId)
    {
        var scope = new
        {
            UserId = 3,
            PlaylistId = playlistId,
            MappingId = mappingId,
            ConnectedServiceAccountId = 303,
            ExternalAccountId = "spotify-owner-3",
            Service = "spotify",
            ServicePlaylistId = "remote-playlist",
            EntryId = entryId
        };
        var proof = new
        {
            Fingerprint = "legacy-fingerprint",
            ExternalId = "remote-a",
            ProofVersion = 3,
            Scope = scope,
            ManualConfirmation = false
        };
        return JsonSerializer.Serialize(new Dictionary<Guid, object> { [entryId] = proof });
    }

    private static void AssertEntry(PlaylistEntry entry, Guid entryId, Guid playlistId, int position,
        DateTimeOffset addedAt, Guid trackId, Guid observationId)
    {
        Assert.Equal(entryId, entry.Id);
        Assert.Equal(playlistId, entry.PlaylistId);
        Assert.Equal(position, entry.Position);
        Assert.Equal(addedAt, entry.AddedAt);
        Assert.Equal(trackId, entry.TrackId);
        Assert.Equal(observationId, entry.TrackObservationId);
        Assert.Equal("spotify", entry.SourceService);
    }

    private static void AssertReceipt(string? actualJson, string originalJson, string revision,
        Guid observationId, Guid receiptTrackId, Guid entryId, Guid playlistId, Guid mappingId)
    {
        Assert.NotNull(actualJson);
        var actual = PlaylistSyncBaseline.Parse(actualJson)!;
        var original = JsonSerializer.Deserialize<PlaylistSyncBaseline>(originalJson)!;
        Assert.Equal(revision, actual.ProviderRevision);
        Assert.Equal(original.Items.Select(item => item.ExternalId), actual.Items.Select(item => item.ExternalId));
        Assert.All(actual.Items, item =>
        {
            Assert.Equal(receiptTrackId, item.TrackId);
            Assert.Equal(observationId, item.ObservationId);
            Assert.Equal("spotify", item.Service);
        });
        var bound = Assert.Single(actual.Items.Where(item => item.ExternalId == "remote-a"));
        Assert.Equal(new PlaylistDestinationBinding(3, playlistId, mappingId, 303, "spotify-owner-3",
                "spotify", "remote-playlist", entryId, receiptTrackId), bound.DestinationBinding);
        Assert.Null(actual.Items.Single(item => item.ExternalId == "remote-b").DestinationBinding);
    }

    private static async Task AssertDisconnectedAccountMakesNoHttpRequestAsync(ApplicationDbContext db)
    {
        var handler = new CountingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.spotify.com") };
        var options = Options.Create(new SpotifyOptions { ClientId = "test", ClientSecret = "test" });
        var api = new SpotifyApiClient(http, options, new NoDelay());
        var encryption = new TokenEncryptionService(new EphemeralDataProtectionProvider());
        var tokens = new SpotifyTokenManager(db, api, encryption, TimeProvider.System, null!);
        var provider = new SpotifyPlaylistSyncProvider(db, api, tokens, [],
            new TrackMatchingService(db, [], NullLogger<TrackMatchingService>.Instance),
            new PlaylistCanonicalReconciliationService(db), Options.Create(new TrackMatchingOptions()));
        var error = await Assert.ThrowsAsync<PlatformApiException>(() =>
            provider.ReadAsync(new PlatformAccountContext(3, 303, "spotify-owner-3"), "remote-playlist", default));
        Assert.Equal("spotify_account_changed", error.Code);
        Assert.Equal(0, handler.RequestCount);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }

    private sealed class NoDelay : ISpotifyRetryDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static TrackObservation Observation(int owner, string title, Guid itemId) => new()
    {
        Id = Guid.NewGuid(), OwnerUserId = owner, SourceType = "youtube", ExternalId = "same", Title = title,
        ProviderCatalogItemId = itemId, MatchStatus = "pending"
    };
}
