using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class PlaylistExportAttemptMigrationTests
{
    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FullMigrationChainRetainsOnlyRecoverableScopedReceipts(bool populated)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        var db = schema.Db;
        var name = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()).SearchPath!;
        Assert.Matches("^cantaro_sync_test_[a-f0-9]+$", name);
        // This fixture owns the private schema. Start with no tables so the real
        // migration chain, rather than EnsureCreated, supplies every default/FK.
        var dropSchema = $"DROP SCHEMA \"{name}\" CASCADE";
        var createSchema = $"CREATE SCHEMA \"{name}\"";
        await db.Database.ExecuteSqlRawAsync(dropSchema);
        await db.Database.ExecuteSqlRawAsync(createSchema);
        var migrator = db.GetService<IMigrator>();
        const string previous = "20260929205853_CacheSpotifyCatalogSearches";
        if (populated)
        {
            await migrator.MigrateAsync(previous);
            db.Users.Add(TestUserFactory.Create(9001, "export-migration@example.com"));
            var track = new Track { Id = Guid.NewGuid(), SearchTitle = "Canonical A" };
            db.Tracks.Add(track);
            foreach (var service in new[] { "spotify", "youtube" })
            {
                var account = new ConnectedServiceAccount { UserId = 9001, Service = service,
                    ExternalAccountId = "external-account", ConnectionState = "disconnected" };
                db.ConnectedServiceAccounts.Add(account);
                await db.SaveChangesAsync();
                foreach (var kind in new[] { "baseline", "pending", "pending-bare", "legacy", "duplicates", "foreign", "bad-json", "bare", "foreign-proof", "placeholder" })
                {
                    var playlist = new Playlist { Id = Guid.NewGuid(), UserId = 9001, Name = kind };
                    var entry = new PlaylistEntry { Id = Guid.NewGuid(), PlaylistId = playlist.Id, TrackId = track.Id, Position = 0 };
                    var link = new ServicePlaylistMapping { Id = Guid.NewGuid(), PlaylistId = playlist.Id, UserId = 9001,
                        ConnectedServiceAccountId = account.Id, ExternalAccountId = account.ExternalAccountId,
                        Service = service, ServicePlaylistId = kind == "placeholder" ? "pending:unknown" : kind,
                        SyncMode = "bidirectional", State = "unlinked" };
                    var item = PlaylistSyncItem.From(entry) with { ExternalId = "chosen-id", Service = service,
                        DestinationBinding = PlaylistDestinationBinding.From(link, entry) };
                    if (kind == "foreign") item = item with { DestinationBinding = item.DestinationBinding! with { ExternalAccountId = "foreign" } };
                    if (kind is "legacy" or "bare" or "foreign-proof" or "pending-bare") item = item with { DestinationBinding = null };
                    var receipt = new PlaylistSyncBaseline(kind == "duplicates" ? [item, item] : [item]).Serialize();
                    if (kind is "pending" or "pending-bare") link.PendingWriteJson = receipt;
                    else link.BaselineJson = kind == "bad-json" ? "{malformed" : receipt;
                    if (kind == "duplicates") link.PendingWriteJson = receipt;
                    if (kind is "legacy" or "foreign-proof")
                    {
                        var proof = PlaylistMatchingProgress.Create(link, entry, "chosen-id");
                        if (kind == "foreign-proof") proof = proof with { Scope = proof.Scope! with { ExternalAccountId = "foreign" } };
                        link.MatchingProgressJson = PlaylistMatchingProgress.Serialize(new() { [entry.Id] = proof });
                    }
                    db.AddRange(playlist, entry, link);
                }
                await db.SaveChangesAsync();
            }
            // Backfill suppression survives metadata-proof expiry and grants no
            // new permission to export these IDs after migration.
            track.SearchTitle += " changed after proof";
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }
        await migrator.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        var records = await db.PlaylistExportAttempts.AsNoTracking().ToArrayAsync();
        Assert.Equal(populated ? 10 : 0, records.Length);
        Assert.All(records, row =>
        {
            Assert.Equal((9001, "external-account", "chosen-id"), (row.UserId, row.ExternalAccountId, row.ExternalTrackId));
            Assert.Contains(row.ServicePlaylistId, new[] { "baseline", "pending", "pending-bare", "legacy", "duplicates" });
        });
        if (populated)
        {
            await db.Playlists.ExecuteDeleteAsync();
            await db.ConnectedServiceAccounts.ExecuteDeleteAsync();
            Assert.Equal(10, await db.PlaylistExportAttempts.CountAsync());
            await db.Users.ExecuteDeleteAsync();
            Assert.Empty(await db.PlaylistExportAttempts.ToArrayAsync());
        }
        // Executing down/up also proves the schema can be deployed again. An
        // intentionally rolled-back ledger cannot restore erased attempt history.
        await migrator.MigrateAsync(previous);
        await migrator.MigrateAsync();
        Assert.Empty(await db.PlaylistExportAttempts.ToArrayAsync());
        using var pool = new NpgsqlConnection(db.Database.GetConnectionString());
        NpgsqlConnection.ClearPool(pool);
    }
}
