using System.Data.Common;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed partial class PlaylistUnmatchedReviewTests
{
    [PostgresTheory]
    [InlineData("spotify")]
    [InlineData("youtube")]
    public async Task DurableExport_PostgresConcurrentAttemptsUseRealUniqueIdentityAndSurviveAccountDeletion(string service)
    {
        await using var fixture = await ReplacementReadFixture.CreateAsync(service, true);
        var scope = fixture.Scope;
        var account = new PlatformAccountContext(scope.UserId, scope.Link.ConnectedServiceAccountId, "account");
        await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
        {
            await using var db = scope.NewContext();
            await PlaylistExportAttemptStore.RecordAsync(db, account, service, "playlist", ["chosen-id", "chosen-id", "second-id"], default);
        }));
        await using (var verify = scope.NewContext())
        {
            Assert.Equal(2, await verify.PlaylistExportAttempts.CountAsync());
            verify.PlaylistExportAttempts.Add(new PlaylistExportAttempt { UserId = scope.UserId,
                Service = service, ExternalAccountId = "account", ServicePlaylistId = "playlist", ExternalTrackId = "chosen-id" });
            var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => verify.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(duplicate.InnerException).SqlState);
        }
        await using (var deleteDb = scope.NewContext())
        {
            await fixture.Lifecycle(deleteDb).UnlinkAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, false, true, default);
            await deleteDb.TrackObservations.ExecuteDeleteAsync();
            await deleteDb.Tracks.ExecuteDeleteAsync();
            await deleteDb.ConnectedServiceAccounts.ExecuteDeleteAsync();
            Assert.Equal(2, await deleteDb.PlaylistExportAttempts.CountAsync());
            await deleteDb.Users.ExecuteDeleteAsync();
        }
        await using var persisted = scope.NewContext();
        Assert.Empty(await persisted.PlaylistExportAttempts.ToArrayAsync());
    }

    [PostgresTheory]
    [InlineData("transient")]
    [InlineData("permanent")]
    [InlineData("cancel")]
    public async Task DurableExport_PostgresAttemptTransactionRetriesIdempotentlyAndNeverCommitsCallerEdits(string failure)
    {
        await using var fixture = await ReplacementReadFixture.CreateAsync("spotify", true);
        var scope = fixture.Scope;
        using var cancellation = new CancellationTokenSource();
        var interceptor = new ExportInsertFailure(failure, cancellation);
        await using var caller = scope.NewContext(interceptor);
        var pending = new Track { Id = Guid.NewGuid(), SearchTitle = "Caller has not saved this" };
        caller.Tracks.Add(pending);
        var record = PlaylistExportAttemptStore.RecordAsync(caller,
            new(scope.UserId, scope.Link.ConnectedServiceAccountId, "account"), "spotify", "playlist", ["first-id", "second-id"], cancellation.Token);
        if (failure == "permanent") await Assert.ThrowsAsync<InvalidOperationException>(() => record);
        else if (failure == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => record);
        else await record;
        Assert.True(interceptor.Injected);
        await using (var verify = scope.NewContext())
        {
            Assert.False(await verify.Tracks.AnyAsync(track => track.Id == pending.Id));
            Assert.Equal(failure == "transient" ? 2 : 0, await verify.PlaylistExportAttempts.CountAsync());
            Assert.Equal(failure == "transient" ? 4 : 2, interceptor.InsertCount);
        }
        await caller.SaveChangesAsync();
        await using var persisted = scope.NewContext();
        Assert.True(await persisted.Tracks.AnyAsync(track => track.Id == pending.Id));
    }

    [PostgresTheory]
    [InlineData("token")]
    [InlineData("account")]
    [InlineData("disconnect")]
    public async Task DurableExport_PostgresAttemptRechecksCapturedAccountBeforeCommitting(string change)
    {
        await using var fixture = await ReplacementReadFixture.CreateAsync("spotify", true);
        var scope = fixture.Scope;
        var interceptor = new AccountAfterCapture(async () =>
        {
            await using var update = scope.NewContext();
            var account = await update.ConnectedServiceAccounts.SingleAsync();
            if (change == "token") account.TokenVersion++;
            else if (change == "account") account.ExternalAccountId = "replacement-account";
            else account.ConnectionState = "disconnected";
            await update.SaveChangesAsync();
        });
        await using var caller = scope.NewContext(interceptor);
        var error = await Assert.ThrowsAsync<PlatformApiException>(() => PlaylistExportAttemptStore.RecordAsync(caller,
            new(scope.UserId, scope.Link.ConnectedServiceAccountId, "account"), "spotify", "playlist", ["chosen-id"], default));
        Assert.True(interceptor.Changed);
        Assert.Equal("spotify_account_changed", error.Code);
        await using var persisted = scope.NewContext();
        Assert.Empty(await persisted.PlaylistExportAttempts.ToArrayAsync());
    }

    [PostgresFact]
    public async Task DurableExport_PostgresUncommittedCallerTransactionCannotPretendAnAttemptIsDurable()
    {
        await using var fixture = await ReplacementReadFixture.CreateAsync("spotify", true);
        var scope = fixture.Scope;
        await using var caller = scope.NewContext();
        var pending = new Track { Id = Guid.NewGuid(), SearchTitle = "Caller transaction" };
        await new PlaylistSyncCoordinator.PlaylistTransactionStrategy(caller).ExecuteAsync(async () =>
        {
            await using var transaction = await caller.Database.BeginTransactionAsync();
            caller.Tracks.Add(pending);
            await caller.SaveChangesAsync();
            var error = await Assert.ThrowsAsync<PlatformApiException>(() => PlaylistExportAttemptStore.RecordAsync(caller,
                new(scope.UserId, scope.Link.ConnectedServiceAccountId, "account"), "spotify", "playlist", ["chosen-id"], default));
            Assert.Equal("export_transaction_active", error.Code);
            await transaction.CommitAsync();
        });
        await using var persisted = scope.NewContext();
        Assert.Empty(await persisted.PlaylistExportAttempts.ToArrayAsync());
        Assert.True(await persisted.Tracks.AnyAsync(track => track.Id == pending.Id));
    }

    private sealed class ExportInsertFailure(string failure, CancellationTokenSource cancellation) : DbCommandInterceptor
    {
        public bool Injected { get; private set; }
        public int InsertCount { get; private set; }
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData data,
            int result, CancellationToken ct = default)
        {
            if (command.CommandText.StartsWith("INSERT INTO \"PlaylistExportAttempts\"", StringComparison.Ordinal))
            {
                InsertCount++;
                if (!Injected && InsertCount == 2)
                {
                    Injected = true;
                    if (failure == "transient") throw new NpgsqlException("Lost attempt transaction", new TimeoutException());
                    if (failure == "cancel") { cancellation.Cancel(); cancellation.Token.ThrowIfCancellationRequested(); }
                    throw new InvalidOperationException("Attempt store unavailable");
                }
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class AccountAfterCapture(Func<Task> update) : DbCommandInterceptor
    {
        public bool Changed { get; private set; }
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data,
            DbDataReader result, CancellationToken ct = default)
        {
            if (!Changed && command.CommandText.Contains("ConnectedServiceAccounts", StringComparison.Ordinal))
            {
                Changed = true;
                await update();
            }
            return result;
        }
    }
}
