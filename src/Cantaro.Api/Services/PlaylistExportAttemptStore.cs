using Cantaro.Api.Data;
using Cantaro.Api.Models;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Cantaro.Api.Services;

internal static class PlaylistExportAttemptStore
{
    public static async Task RecordAsync(ApplicationDbContext caller, PlatformAccountContext context,
        string service, string playlistId, IReadOnlyList<string> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return;
        // A separate committed transaction is essential: neither caller rollback
        // nor deletion of a link after an interrupted HTTP write may erase this.
        // Do not borrow the caller's transaction, connection or tracked edits.
        if (caller.Database.IsRelational() && caller.Database.CurrentTransaction is not null)
            throw new PlatformApiException("export_transaction_active",
                "Finish the local transaction before exporting this playlist.", 409);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>(
            (DbContextOptions<ApplicationDbContext>)caller.GetService<IDbContextOptions>())
            .UseModel(caller.Model).Options;
        ApplicationDbContext NewContext()
        {
            var result = new ApplicationDbContext(options);
            if (result.Database.IsRelational())
            {
                var connection = DbProviderFactories.GetFactory(caller.Database.GetDbConnection())!.CreateConnection()!;
                connection.ConnectionString = caller.Database.GetConnectionString();
                result.Database.SetDbConnection(connection, contextOwnsConnection: true);
            }
            return result;
        }
        await using var strategyDb = NewContext();
        var account = await PlaylistReadAccount.CaptureAsync(strategyDb, context, service, ct);
        await strategyDb.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            // Each retry has a fresh tracker. Idempotent inserts may be replayed,
            // including after a lost commit response. HTTP is never in this retry.
            await using var db = NewContext();
            await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
            await account.ValidateAsync(db, ct, lockUntilCommit: true);
            foreach (var id in ids.Distinct(StringComparer.Ordinal))
            {
                if (db.Database.IsRelational())
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO "PlaylistExportAttempts"
                            ("UserId", "Service", "ExternalAccountId", "ServicePlaylistId", "ExternalTrackId")
                        VALUES ({context.UserId}, {service}, {account.ExternalAccountId}, {playlistId}, {id})
                        ON CONFLICT ("UserId", "Service", "ExternalAccountId", "ServicePlaylistId", "ExternalTrackId") DO NOTHING
                        """, ct);
                else if (!await db.PlaylistExportAttempts.AnyAsync(item => item.UserId == context.UserId
                    && item.Service == service && item.ExternalAccountId == account.ExternalAccountId
                    && item.ServicePlaylistId == playlistId && item.ExternalTrackId == id, ct))
                    db.PlaylistExportAttempts.Add(new PlaylistExportAttempt { UserId = context.UserId,
                        Service = service, ExternalAccountId = account.ExternalAccountId,
                        ServicePlaylistId = playlistId, ExternalTrackId = id });
            }
            await db.SaveChangesAsync(ct);
            ct.ThrowIfCancellationRequested();
            if (transaction is not null) await transaction.CommitAsync(ct);
        });
    }

    // Capture independent evidence before materialization. Otherwise the read's
    // own observation/resolver writes could incorrectly corroborate themselves.
    public static async Task<Func<Task>> GuardReadAsync(ApplicationDbContext db, PlaylistReadAccount account,
        string playlistId, IReadOnlyList<PlaylistRemoteTrack> bound, CancellationToken ct)
    {
        var uncorroborated = new List<string>();
        foreach (var ids in bound.Where(item => item.ReadScope is null).Select(item => item.ExternalId)
            .Distinct(StringComparer.Ordinal).Chunk(256))
        {
            var established = await db.TrackSourceIds.AsNoTracking()
                .Where(item => item.SourceType == account.Service && ids.Contains(item.ExternalId))
                .Select(item => item.ExternalId).ToListAsync(ct);
            var observed = await db.TrackObservations.AsNoTracking()
                .Where(item => item.SourceType == account.Service && ids.Contains(item.ExternalId))
                .Select(item => item.ExternalId).ToListAsync(ct);
            uncorroborated.AddRange(ids.Except(established).Except(observed));
        }
        async Task ValidateAsync()
        {
            foreach (var ids in uncorroborated.Chunk(256))
                if (await db.PlaylistExportAttempts.AsNoTracking().AnyAsync(item => item.UserId == account.Context.UserId
                    && item.Service == account.Service && item.ExternalAccountId == account.ExternalAccountId
                    && item.ServicePlaylistId == playlistId && ids.Contains(item.ExternalTrackId), ct))
                    throw new PlatformApiException("remote_changed",
                        "Cantaro cannot verify the original track for a previous export in this playlist. Import stopped to protect the library.", 409);
        }
        await ValidateAsync();
        // Run again under the final account lock so an export committed while
        // materializing this read cannot race an independent-ingestion commit.
        return ValidateAsync;
    }

    public static async Task RejectLegacyReadAsync(ApplicationDbContext db, PlaylistReadAccount account,
        string playlistId, CancellationToken ct)
    {
        if (await db.ServicePlaylistMappings.AsNoTracking().AnyAsync(item =>
            item.ConnectedServiceAccountId == account.Context.ConnectedServiceAccountId
            && item.Service == account.Service && item.ServicePlaylistId == playlistId
            && (item.SyncMode == "from_cantaro" || item.BaselineJson != null || item.PendingWriteJson != null
                || item.MatchingProgressJson != null), ct)
            || await db.PlaylistExportAttempts.AsNoTracking().AnyAsync(item => item.UserId == account.Context.UserId
            && item.Service == account.Service && item.ExternalAccountId == account.ExternalAccountId
            && item.ServicePlaylistId == playlistId, ct))
            throw new PlatformApiException("outbound_mapping", "Sync this linked playlist from the Cantaro playlist.", 409);
    }
}
