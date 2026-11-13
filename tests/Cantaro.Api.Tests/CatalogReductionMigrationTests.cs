using System.Security.Cryptography;
using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class CatalogReductionMigrationTests
{
    [PostgresFact]
    public async Task CurrentSchemaUpgradePreservesMembershipChoicesReceiptsAndPrivateInput()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        var db = schema.Db;
        var schemaName = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()).SearchPath!;
        Assert.Matches("^cantaro_sync_test_[a-f0-9]+$", schemaName);
        await db.Database.ExecuteSqlRawAsync($"DROP SCHEMA \"{schemaName}\" CASCADE");
        await db.Database.ExecuteSqlRawAsync($"CREATE SCHEMA \"{schemaName}\"");
        await db.GetService<IMigrator>().MigrateAsync(CatalogReductionMigrator.PreviousMigration);

        var trackId = Guid.NewGuid(); var catalogId = Guid.NewGuid();
        var publicId = Guid.NewGuid(); var privateId = Guid.NewGuid(); var referenceId = Guid.NewGuid();
        var playlistId = Guid.NewGuid(); var otherPlaylistId = Guid.NewGuid(); var linkId = Guid.NewGuid();
        var firstId = Guid.NewGuid(); var duplicateId = Guid.NewGuid(); var otherId = Guid.NewGuid(); var unavailableId = Guid.NewGuid();
        var candidateId = Guid.NewGuid(); var artistId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
        const string mbid = "A7A91E60-5031-4FB1-8532-8D533E151E61";
        const string title = "Música <studio>";
        const string isrc = "USABC2400001";
        var publicJson = JsonSerializer.Serialize(new { Isrc = isrc });
        var privateJson = JsonSerializer.Serialize(new { Description = "Owner two's private input", AccessToken = "must-be-removed" });
        var facts = JsonSerializer.Serialize(new ProviderCatalogFacts { Title = title, Artist = "Artist", DurationSeconds = 181, Isrc = isrc });
        var scope = new PlaylistMatchingScope(1, playlistId, linkId, 101, "bound-account", "spotify", "remote", firstId);
        // This is the exact old anonymous-type serialization, independent of
        // the conversion implementation. Unicode/HTML escaping is intentional.
        var oldFingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            TrackId = (Guid?)trackId, TrackObservationId = (Guid?)publicId,
            Title = title, Artist = "Artist", DurationSeconds = (int?)181, RawMetadata = publicJson,
            OwnerUserId = (int?)1, ObservationTrackId = (Guid?)trackId,
            CanonicalMetadata = (string?)null, SearchTitle = title, SearchArtist = "Artist", Isrc = isrc,
            MbidRecording = (string?)null, VersionFlags = TrackVersionFlags.None,
            PreferredCatalogItemId = (Guid?)null, Sources = (object?)null
        })));
        var progress = PlaylistMatchingProgress.Serialize(new()
        {
            [firstId] = new(oldFingerprint, "chosen", 3, scope, true),
            [duplicateId] = new("stale", "must-not-authorize", 3, scope with { EntryId = duplicateId }, true),
            [unavailableId] = new("stale", "foreign-choice", 3, scope with { EntryId = unavailableId, UserId = 2 }, true),
            [otherId] = new("stale", "foreign-entry", 3, scope with { EntryId = otherId }, true)
        });
        const string baseline = "{\"Items\":[{\"ExternalId\":\"chosen\"},{\"ExternalId\":\"chosen\"}],\"ProviderRevision\":\"old\"}";
        const string pending = "{\"Items\":[{\"ExternalId\":\"pending\"}],\"ProviderRevision\":\"unfinished\"}";
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "AspNetUsers" ("Id", "UserName", "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
            VALUES (1, 'one', false, false, false, false, 0), (2, 'two', false, false, false, false, 0);
            INSERT INTO "ConnectedServiceAccounts" ("Id", "UserId", "Service", "ExternalAccountId", "ConnectionState", "TokenVersion")
            VALUES (101, 1, 'spotify', 'bound-account', 'connected', 1);
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Tracks" ("Id", "SearchTitle", "SearchArtist", "Isrc", "VersionFlags", "CreatedAt", "UpdatedAt")
            VALUES ({trackId}, {title}, 'Artist', {isrc}, 0, {now}, {now});
            INSERT INTO "ProviderCatalogItems" ("Id", "SourceType", "ExternalId", "FactsJson", "VerificationMethod", "VerifiedAt", "RevalidateAfter", "VisibilityState", "Revision")
            VALUES ({catalogId}, 'spotify', 'shared-item', {facts}, 'spotify_application_catalog', {now}, {now.AddYears(10)}, 1, 1);
            INSERT INTO "TrackSourceIds" ("Id", "TrackId", "SourceType", "ExternalId", "ProviderCatalogItemId", "AssociationState", "AssociationMethod", "AssociationVerifiedAt", "CatalogRevision", "IndependentEvidenceJson", "Revision", "PresentationKind")
            VALUES ({Guid.NewGuid()}, {trackId}, 'spotify', 'shared-item', {catalogId}, 1, 'provider_origin', {now}, 1, {facts}, 4, 0);
            INSERT INTO "TrackObservations" ("Id", "OwnerUserId", "SourceType", "ExternalId", "ProviderCatalogItemId", "Title", "Artist", "DurationSeconds", "RawMetadata", "MatchStatus", "TrackId", "MatchAttemptCount", "CreatedAt", "UpdatedAt")
            VALUES ({publicId}, 1, 'spotify', 'shared-item', {catalogId}, {title}, 'Artist', 181, {publicJson}, 'matched', {trackId}, 1, {now}, {now}),
                   ({privateId}, 2, 'spotify', 'shared-item', {catalogId}, 'Private title', 'Private artist', 182, {privateJson}, 'pending', NULL, 3, {now}, {now}),
                   ({referenceId}, 1, 'youtube', 'unavailable', NULL, '', NULL, NULL, NULL, 'pending', NULL, 0, {now}, {now});
            INSERT INTO "Playlists" ("Id", "UserId", "Name", "AllowDuplicateTracks", "SyncEnabled", "SyncRevision", "CreatedAt", "UpdatedAt")
            VALUES ({playlistId}, 1, 'Duplicates', true, false, 7, {now}, {now}), ({otherPlaylistId}, 2, 'Private', false, false, 2, {now}, {now});
            INSERT INTO "PlaylistEntries" ("Id", "PlaylistId", "TrackId", "TrackObservationId", "Position", "AddedAt")
            VALUES ({firstId}, {playlistId}, {trackId}, {publicId}, 2, {now}),
                   ({duplicateId}, {playlistId}, {trackId}, {publicId}, 6, {now}),
                   ({unavailableId}, {playlistId}, NULL, {referenceId}, 8, {now}),
                   ({otherId}, {otherPlaylistId}, NULL, {privateId}, 4, {now});
            INSERT INTO "ServicePlaylistMappings" ("Id", "PlaylistId", "UserId", "ConnectedServiceAccountId", "ExternalAccountId", "Service", "ServicePlaylistId", "SyncMode", "State", "BaselineJson", "PendingWriteJson", "MatchingProgressJson")
            VALUES ({linkId}, {playlistId}, 1, 101, 'bound-account', 'spotify', 'remote', 'bidirectional', 'active', {baseline}, {pending}, {progress});
            INSERT INTO "PlaylistExportAttempts" ("UserId", "Service", "ExternalAccountId", "ServicePlaylistId", "ExternalTrackId")
            VALUES (1, 'spotify', 'bound-account', 'remote', 'chosen');
            INSERT INTO "Artists" ("Id", "Name", "MusicBrainzArtistId", "CreatedAt", "UpdatedAt")
            VALUES ({artistId}, 'Artist', {mbid}, {now}, {now});
            INSERT INTO "TrackMatchQueueItems" ("TrackObservationId", "NextAttemptAt", "RetryCount") VALUES ({privateId}, {now.UtcDateTime}, 3);
            """);
        var candidateJson = JsonSerializer.Serialize(new { ProviderCatalogItemId = catalogId, ProviderCatalogRevision = 1, ProviderRawMetadata = facts });
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "TrackResolutionCandidates" ("Id", "TrackObservationId", "CandidateSource", "ExternalId", "Title", "Artist", "Isrc", "DurationSeconds", "Score", "IsAccepted", "CreatedAt", "RawMetadata")
            VALUES ({candidateId}, {publicId}, 'spotify', 'shared-item', {title}, 'Artist', {isrc}, 181, 1, true, {now}, {candidateJson});
            UPDATE "TrackObservations" SET "AcceptedCandidateId" = {candidateId} WHERE "Id" = {publicId};
            """);

        await CatalogReductionMigrator.ApplyAsync(db);
        Assert.False(db.Database.HasPendingModelChanges());
        var entries = await db.PlaylistEntries.AsNoTracking().OrderBy(e => e.Position).ToArrayAsync();
        Assert.Equal(new[] { firstId, otherId, duplicateId, unavailableId }, entries.Select(e => e.Id));
        Assert.Equal(new[] { 2, 4, 6, 8 }, entries.Select(e => e.Position));
        Assert.All(entries, e => Assert.Equal(now, e.AddedAt));
        Assert.All(entries, e => Assert.Equal(e.PlaylistId == playlistId ? 1 : 2, e.OwnerUserId));
        var catalog = Assert.Single(await db.ProviderCatalogItems.AsNoTracking().ToArrayAsync());
        Assert.Equal((catalogId, trackId, 1L, 4L), (catalog.Id, catalog.TrackId!.Value, catalog.Revision, catalog.AssociationRevision));
        Assert.Equal(1L, catalog.AssociationFactsRevision);
        var observations = await db.TrackObservations.AsNoTracking().ToDictionaryAsync(o => o.Id);
        Assert.Equal(ObservationInputKind.Public, observations[publicId].InputKind);
        Assert.Null(observations[publicId].Title); Assert.Null(observations[publicId].PrivateInputJson);
        Assert.Equal(ObservationInputKind.Private, observations[privateId].InputKind);
        Assert.Contains("Owner two", observations[privateId].PrivateInputJson);
        Assert.DoesNotContain("must-be-removed", observations[privateId].PrivateInputJson);
        Assert.DoesNotContain("Private title", observations[privateId].PrivateInputJson);
        Assert.Equal(ObservationInputKind.Reference, observations[referenceId].InputKind);
        Assert.Null(observations[referenceId].Title);
        var candidate = Assert.Single(await db.TrackResolutionCandidates.AsNoTracking().ToArrayAsync());
        Assert.Equal(catalogId, candidate.ProviderCatalogItemId); Assert.Null(candidate.Title);
        Assert.DoesNotContain("ProviderRawMetadata", candidate.EvaluationJson);
        Assert.Equal(candidateId, observations[publicId].AcceptedCandidateId);
        Assert.Equal(3, (await db.TrackMatchQueueItems.SingleAsync()).RetryCount);
        var artistLink = Assert.Single(await db.ArtistProviderLinks.AsNoTracking().ToArrayAsync());
        Assert.Equal((artistId, "musicbrainz", mbid.ToLowerInvariant()), (artistLink.ArtistId, artistLink.Provider, artistLink.ExternalId));
        var link = await db.ServicePlaylistMappings.AsNoTracking().SingleAsync();
        Assert.Equal(baseline, link.BaselineJson); Assert.Equal(pending, link.PendingWriteJson);
        Assert.Equal("bound-account", link.ExternalAccountId);
        var proofs = PlaylistMatchingProgress.Read(link.MatchingProgressJson);
        var first = await db.PlaylistEntries.AsNoTracking().Include(e => e.TrackObservation).ThenInclude(o => o!.ProviderCatalogItem)
            .Include(e => e.Track).ThenInclude(t => t!.ProviderItems).SingleAsync(e => e.Id == firstId);
        Assert.True(PlaylistMatchingProgress.CanReuse(proofs[firstId], first, link));
        Assert.Equal(4, proofs[firstId].ProofVersion); Assert.True(proofs[firstId].ManualConfirmation);
        Assert.Equal(4, proofs[duplicateId].ProofVersion);
        Assert.Equal("invalidated:v3:stale", proofs[duplicateId].Fingerprint);
        var duplicate = await db.PlaylistEntries.AsNoTracking().Include(e => e.TrackObservation).ThenInclude(o => o!.ProviderCatalogItem)
            .Include(e => e.Track).ThenInclude(t => t!.ProviderItems).SingleAsync(e => e.Id == duplicateId);
        Assert.False(PlaylistMatchingProgress.CanReuse(proofs[duplicateId], duplicate, link));
        Assert.Equal(3, proofs[unavailableId].ProofVersion);
        Assert.Equal(3, proofs[otherId].ProofVersion);
        Assert.Single(await db.PlaylistExportAttempts.ToArrayAsync());
        await CatalogReductionMigrator.ApplyAsync(db);
        Assert.Equal(4, await db.PlaylistEntries.CountAsync());
        // Rematching or clearing the source job must not replace either user's
        // entry-specific choice, even when its export proof had already expired.
        var rematched = new Track { Id = Guid.NewGuid() };
        db.Tracks.Add(rematched);
        (await db.TrackObservations.SingleAsync(o => o.Id == publicId)).TrackId = rematched.Id;
        await db.SaveChangesAsync();
        var reconciler = new PlaylistCanonicalReconciliationService(db);
        await reconciler.ReconcileObservationAsync(publicId, rematched.Id, default);
        await db.SaveChangesAsync();
        await reconciler.ReconcileObservationAsync(publicId, null, default);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var chosenEntries = await db.PlaylistEntries.Where(e => e.Id == firstId || e.Id == duplicateId).ToArrayAsync();
        Assert.Equal(2, chosenEntries.Length);
        Assert.All(chosenEntries, e => Assert.Equal(trackId, e.TrackId));
        Assert.Equal(4, await db.PlaylistEntries.CountAsync());
    }
}
