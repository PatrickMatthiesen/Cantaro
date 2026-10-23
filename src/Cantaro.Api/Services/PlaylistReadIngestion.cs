using Cantaro.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Cantaro.Api.Services;

// A rejected read must leave neither persisted metadata nor changes that a
// coordinator's error save could commit. The caller keeps its own tracker.
internal static class PlaylistReadIngestion
{
    internal static ApplicationDbContext CreateContext(ApplicationDbContext caller)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>(
            (DbContextOptions<ApplicationDbContext>)caller.GetService<IDbContextOptions>())
            .UseModel(caller.Model).Options;
        return new ApplicationDbContext(options);
    }

    public static async Task<T> RunAsync<T>(ApplicationDbContext caller, PlaylistReadAccount account,
        Func<ApplicationDbContext, Task<T>> ingest, CancellationToken ct, Func<Task>? validateBeforeCommit = null)
    {
        await using var db = CreateContext(caller);
        // HTTP has already completed. Do not replay materialization with tracked
        // entities after a rollback, including resolver-internal staged saves.
        return await new PlaylistSyncCoordinator.PlaylistTransactionStrategy(db).ExecuteAsync(async () =>
        {
            var ambient = caller.Database.IsRelational() ? caller.Database.CurrentTransaction : null;
            if (ambient is not null)
                db.Database.SetDbConnection(caller.Database.GetDbConnection(), contextOwnsConnection: false);
            await using var transaction = !db.Database.IsRelational() ? null : ambient is null
                ? await db.Database.BeginTransactionAsync(ct)
                : await db.Database.UseTransactionAsync(ambient.GetDbTransaction(), ct);
            var savepoint = "playlist_read_" + Guid.NewGuid().ToString("N");
            if (ambient is not null)
                await transaction!.CreateSavepointAsync(savepoint, ct);
            try
            {
                await account.ValidateAsync(db, ct);
                var result = await ingest(db);
                await db.SaveChangesAsync(ct);
                await account.ValidateAsync(db, ct, lockUntilCommit: true);
                if (validateBeforeCommit is not null) await validateBeforeCommit();
                ct.ThrowIfCancellationRequested();
                if (ambient is not null) await transaction!.ReleaseSavepointAsync(savepoint, ct);
                else if (transaction is not null) await transaction.CommitAsync(ct);
                return result;
            }
            catch
            {
                // Cleanup must run even when the caller cancelled the read.
                if (ambient is not null)
                    await transaction!.RollbackToSavepointAsync(savepoint, CancellationToken.None);
                else if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        });
    }
}

internal sealed record PlaylistReadAccount(PlatformAccountContext Context, string Service,
    string ExternalAccountId, long TokenVersion)
{
    public static async Task<PlaylistReadAccount> CaptureAsync(ApplicationDbContext db,
        PlatformAccountContext context, string service, CancellationToken ct)
    {
        var account = await db.ConnectedServiceAccounts.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == context.ConnectedServiceAccountId && item.UserId == context.UserId
            && item.Service == service && item.ConnectionState == "connected"
            && (context.ExpectedExternalAccountId == null || item.ExternalAccountId == context.ExpectedExternalAccountId), ct)
            ?? throw Changed(service);
        return new(context, service, account.ExternalAccountId, account.TokenVersion);
    }

    public async Task ValidateAsync(ApplicationDbContext db, CancellationToken ct, bool lockUntilCommit = false)
    {
        var current = db.ConnectedServiceAccounts.Where(item => item.Id == Context.ConnectedServiceAccountId
            && item.UserId == Context.UserId && item.Service == Service && item.ConnectionState == "connected"
            && item.ExternalAccountId == ExternalAccountId && item.TokenVersion == TokenVersion);
        // A conditional no-op update holds the account row through commit. A
        // concurrent disconnect/replacement cannot pass the last check then race
        // the metadata commit. Nonrelational stores have no transaction locks.
        var valid = lockUntilCommit && db.Database.IsRelational()
            ? await current.ExecuteUpdateAsync(set => set.SetProperty(item => item.TokenVersion, item => item.TokenVersion), ct) == 1
            : await current.AsNoTracking().AnyAsync(ct);
        if (!valid) throw Changed(Service);
    }

    private static PlatformApiException Changed(string service)
        => new(service + "_account_changed", "The connected account changed during the playlist read. Refresh before syncing.", 409);
}
