using System.Data;
using System.Data.Common;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed partial class PlaylistUnmatchedReviewTests
{
    [Fact]
    public Task ConfirmationLease_SyncBlocksConfirmationUntilRetry() => SyncBlocksConfirmationAsync(false);

    [PostgresFact]
    public Task ConfirmationLease_PostgresSyncBlocksConfirmationUntilRetry() => SyncBlocksConfirmationAsync(true);

    private static async Task SyncBlocksConfirmationAsync(bool postgres)
    {
        await using var scope = await Scope.CreateAsync("spotify", relational: !postgres, postgres: postgres);
        // The original race needs two entries: a stale progress snapshot must exist
        // before the user confirms the second entry during the first lookup.
        scope.Entry.Position = 1;
        var first = scope.AddEntry("Other song", "other-source");
        first.Position = 0;
        first.Track!.SearchArtist = "Other artist";
        first.TrackObservation!.Artist = "Other artist";
        scope.Link.BaselineName = scope.Playlist.Name;
        scope.Link.BaselineJson = new PlaylistSyncBaseline([]).Serialize();
        await scope.Db.SaveChangesAsync();
        var token = scope.Token(Candidate("chosen-id"));
        await using var syncDb = scope.NewContext();
        await using var confirmDb = scope.NewContext();
        var review = scope.CreateReview(confirmDb);
        var provider = new ReviewResolverProvider(syncDb, scope.Playlist.Name) { PauseSearch = true };
        var writer = new ReviewWriter(provider);
        var run = Coordinator(syncDb, provider, writer).RunAsync(scope.UserId, scope.Playlist.Id, "spotify", default);
        try
        {
            await provider.SearchStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var before = (await confirmDb.ServicePlaylistMappings.AsNoTracking().SingleAsync()).MatchingProgressJson;
            await AssertBusyAsync(() => review.ConfirmAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id,
                scope.Entry.Id, token, default));
            Assert.Equal(before, (await confirmDb.ServicePlaylistMappings.AsNoTracking().SingleAsync()).MatchingProgressJson);
            Assert.False(await confirmDb.TrackSourceIds.AnyAsync(source => source.ExternalId == "chosen-id"));
            Assert.Equal(scope.Entry.TrackId, (await confirmDb.PlaylistEntries.AsNoTracking()
                .SingleAsync(entry => entry.Id == scope.Entry.Id)).TrackId);
        }
        finally
        {
            provider.ContinueSearch.TrySetResult();
            await run;
        }
        Assert.Empty(writer.Writes);
        // The same token remains current after the worker finishes. Retry through
        // the public service, then read and export using another fresh context.
        await review.ConfirmAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, scope.Entry.Id, token, default);
        await AssertReleasedAsync(confirmDb, scope.Playlist.Id);
        Assert.True(PlaylistMatchingProgress.Read((await confirmDb.ServicePlaylistMappings.AsNoTracking()
            .SingleAsync()).MatchingProgressJson)[scope.Entry.Id].ManualConfirmation);
        Assert.DoesNotContain(await review.ListAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, default),
            entry => entry.EntryId == scope.Entry.Id);
        await AssertExportsConfirmationAsync(scope);
    }

    [Fact]
    public Task ConfirmationLease_ConfirmationBlocksSync() => ConfirmationBlocksSyncAsync(false);

    [PostgresFact]
    public Task ConfirmationLease_PostgresConfirmationBlocksSync() => ConfirmationBlocksSyncAsync(true);

    private static async Task ConfirmationBlocksSyncAsync(bool postgres)
    {
        await using var scope = await Scope.CreateAsync("spotify", relational: !postgres, postgres: postgres);
        var token = scope.Token(Candidate("chosen-id"));
        var pause = new PauseConfirmationSave();
        await using var confirmDb = scope.NewContext(pause);
        await using var syncDb = scope.NewContext();
        var provider = new ReviewResolverProvider(syncDb, scope.Playlist.Name);
        var writer = new ReviewWriter(provider);
        var confirm = scope.CreateReview(confirmDb).ConfirmAsync(scope.UserId, scope.Playlist.Id,
            scope.Link.Id, scope.Entry.Id, token, default);
        try
        {
            await pause.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(IsolationLevel.Serializable, pause.Isolation);
            var leased = await syncDb.Playlists.AsNoTracking().SingleAsync();
            Assert.NotNull(leased.SyncLeaseId);
            Assert.True(leased.SyncLeaseExpiresAt > DateTimeOffset.UtcNow);
            await AssertBusyAsync(() => Coordinator(syncDb, provider, writer)
                .RunAsync(scope.UserId, scope.Playlist.Id, "spotify", default));
            Assert.Equal(0, provider.Resolutions);
            Assert.Empty(writer.Writes);
        }
        finally
        {
            pause.Continue.TrySetResult();
            await confirm;
        }
        await AssertReleasedAsync(syncDb, scope.Playlist.Id);
        await AssertExportsConfirmationAsync(scope);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ConfirmationLease_CancellationOrErrorReleasesLease(bool cancel) => ReleasesLeaseAsync(false, cancel);

    [PostgresFact]
    public async Task ConfirmationLease_PostgresCancellationAndErrorReleaseLease()
    {
        await ReleasesLeaseAsync(true, true);
        await ReleasesLeaseAsync(true, false);
    }

    [Fact]
    public Task ConfirmationLease_CancellationAfterLeaseUpdateReleasesLease()
        => CancellationAfterLeaseUpdateAsync(false);

    [PostgresFact]
    public Task ConfirmationLease_PostgresCancellationAfterLeaseUpdateReleasesLease()
        => CancellationAfterLeaseUpdateAsync(true);

    private static async Task CancellationAfterLeaseUpdateAsync(bool postgres)
    {
        await using var scope = await Scope.CreateAsync("spotify", relational: !postgres, postgres: postgres);
        scope.Link.BaselineName = scope.Playlist.Name;
        scope.Link.BaselineJson = new PlaylistSyncBaseline([]).Serialize();
        await scope.Db.SaveChangesAsync();
        var token = scope.Token(Candidate("chosen-id"));
        using var cancellation = new CancellationTokenSource();
        var boundary = new CancelAfterLeaseUpdate(cancellation);
        await using (var confirmDb = scope.NewContext(boundary))
        {
            if (postgres) Assert.True(confirmDb.Database.CreateExecutionStrategy().RetriesOnFailure);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.CreateReview(confirmDb)
                .ConfirmAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, scope.Entry.Id, token, cancellation.Token));
        }
        Assert.True(boundary.Reached);
        await using (var syncDb = scope.NewContext())
        {
            await AssertReleasedAsync(syncDb, scope.Playlist.Id);
            Assert.False(await syncDb.TrackSourceIds.AnyAsync(source => source.ExternalId == "chosen-id"));
            Assert.Empty(PlaylistMatchingProgress.Read((await syncDb.ServicePlaylistMappings.AsNoTracking()
                .SingleAsync()).MatchingProgressJson));
            var entry = await syncDb.PlaylistEntries.AsNoTracking().SingleAsync();
            Assert.Equal(scope.Entry.TrackId, entry.TrackId);
            Assert.Equal(scope.Entry.TrackObservationId, entry.TrackObservationId);
            // A separate worker can sync immediately, before any successful confirmation.
            var provider = new ReviewResolverProvider(syncDb, scope.Playlist.Name);
            var writer = new ReviewWriter(provider);
            await Coordinator(syncDb, provider, writer).RunAsync(scope.UserId, scope.Playlist.Id, "spotify", default);
            Assert.Equal(1, provider.Searches);
            Assert.Empty(writer.Writes);
            await AssertReleasedAsync(syncDb, scope.Playlist.Id);
        }
        Assert.Equal(2, boundary.CleanupTokens.Count);
        Assert.All(boundary.CleanupTokens, token => Assert.Equal(CancellationToken.None, token));
        // Retry the original proof with another fresh context, then export it.
        await using (var retryDb = scope.NewContext())
        {
            await scope.CreateReview(retryDb).ConfirmAsync(scope.UserId, scope.Playlist.Id,
                scope.Link.Id, scope.Entry.Id, token, default);
            await AssertReleasedAsync(retryDb, scope.Playlist.Id);
        }
        await AssertExportsConfirmationAsync(scope);
    }

    private static async Task ReleasesLeaseAsync(bool postgres, bool cancel)
    {
        await using var scope = await Scope.CreateAsync("spotify", relational: !postgres, postgres: postgres);
        var token = scope.Token(Candidate("chosen-id"));
        var pause = new PauseConfirmationSave { Fail = !cancel };
        using var cancellation = new CancellationTokenSource();
        await using (var confirmDb = scope.NewContext(pause))
        {
            var confirm = scope.CreateReview(confirmDb).ConfirmAsync(scope.UserId, scope.Playlist.Id,
                scope.Link.Id, scope.Entry.Id, token, cancellation.Token);
            try
            {
                await pause.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
                if (cancel) cancellation.Cancel();
            }
            finally { pause.Continue.TrySetResult(); }
            if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => confirm);
            else await Assert.ThrowsAsync<InvalidOperationException>(() => confirm);
        }
        await using var retryDb = scope.NewContext();
        await AssertReleasedAsync(retryDb, scope.Playlist.Id);
        Assert.False(await retryDb.TrackSourceIds.AnyAsync(source => source.ExternalId == "chosen-id"));
        Assert.Empty(PlaylistMatchingProgress.Read((await retryDb.ServicePlaylistMappings.AsNoTracking()
            .SingleAsync()).MatchingProgressJson));
        // This verifies release of both the process gate and the database lease.
        await scope.CreateReview(retryDb).ConfirmAsync(scope.UserId, scope.Playlist.Id,
            scope.Link.Id, scope.Entry.Id, token, default);
        await AssertExportsConfirmationAsync(scope);
    }

    [PostgresFact]
    public async Task ConfirmationLease_PostgresRejectsAnotherProcessLeaseAndRetriesAfterExpiry()
    {
        await using var scope = await Scope.CreateAsync("spotify", postgres: true);
        var token = scope.Token(Candidate("chosen-id"));
        var otherLease = Guid.NewGuid();
        // No in-process gate exists for this simulated remote worker. Both public
        // APIs must reject the persisted lease using the production SQL predicate.
        await scope.Db.Playlists.ExecuteUpdateAsync(setters => setters
            .SetProperty(item => item.SyncLeaseId, otherLease)
            .SetProperty(item => item.SyncLeaseExpiresAt, DateTimeOffset.UtcNow.AddMinutes(1)));
        await using var confirmDb = scope.NewContext();
        var review = scope.CreateReview(confirmDb);
        await AssertBusyAsync(() => review.ConfirmAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id,
            scope.Entry.Id, token, default));
        var provider = new ReviewResolverProvider(confirmDb, scope.Playlist.Name);
        await AssertBusyAsync(() => Coordinator(confirmDb, provider, new ReviewWriter(provider))
            .RunAsync(scope.UserId, scope.Playlist.Id, "spotify", default));
        Assert.Equal(otherLease, (await scope.Db.Playlists.AsNoTracking().SingleAsync()).SyncLeaseId);
        Assert.False(await scope.Db.TrackSourceIds.AnyAsync(source => source.ExternalId == "chosen-id"));
        await scope.Db.Playlists.ExecuteUpdateAsync(setters => setters
            .SetProperty(item => item.SyncLeaseExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        await review.ConfirmAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, scope.Entry.Id, token, default);
        await AssertReleasedAsync(confirmDb, scope.Playlist.Id);
        await AssertExportsConfirmationAsync(scope);
    }

    private static PlaylistSyncCoordinator Coordinator(ApplicationDbContext db, ReviewResolverProvider provider, ReviewWriter writer)
        => new(db, [provider], [writer], TimeProvider.System, NullLogger<PlaylistSyncCoordinator>.Instance);

    private static async Task AssertBusyAsync(Func<Task> operation)
    {
        var error = await Assert.ThrowsAsync<PlatformApiException>(operation);
        Assert.Equal("sync_in_progress", error.Code);
        Assert.Equal(409, error.StatusCode);
    }

    private static async Task AssertReleasedAsync(ApplicationDbContext db, Guid playlistId)
    {
        var playlist = await db.Playlists.AsNoTracking().SingleAsync(item => item.Id == playlistId);
        Assert.Null(playlist.SyncLeaseId);
        Assert.Null(playlist.SyncLeaseExpiresAt);
    }

    private static async Task AssertExportsConfirmationAsync(Scope scope)
    {
        await using var db = scope.NewContext();
        var link = await db.ServicePlaylistMappings.SingleAsync();
        link.BaselineName ??= scope.Playlist.Name;
        link.BaselineJson ??= new PlaylistSyncBaseline([]).Serialize();
        await db.SaveChangesAsync();
        var provider = new ReviewResolverProvider(db, scope.Playlist.Name);
        var writer = new ReviewWriter(provider);
        await Coordinator(db, provider, writer).RunAsync(scope.UserId, scope.Playlist.Id, "spotify", default);
        Assert.Equal(["chosen-id"], Assert.Single(writer.Writes));
        Assert.True(PlaylistMatchingProgress.Read((await db.ServicePlaylistMappings.AsNoTracking()
            .SingleAsync()).MatchingProgressJson)[scope.Entry.Id].ManualConfirmation);
        await AssertReleasedAsync(db, scope.Playlist.Id);
    }

    private sealed class CancelAfterLeaseUpdate(CancellationTokenSource cancellation) : DbCommandInterceptor
    {
        public bool Reached { get; private set; }
        public List<CancellationToken> CleanupTokens { get; } = [];

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            // ExecuteUpdate has completed on the real database. Cancel before
            // WithLeaseAsync can reload the tracked playlist, only on this first update.
            if (!Reached && result == 1 && command.CommandText.StartsWith("UPDATE \"Playlists\"", StringComparison.Ordinal)
                && command.CommandText.Contains("SyncLeaseExpiresAt", StringComparison.Ordinal))
            {
                Reached = true;
                cancellation.Cancel();
            }
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Reached) CleanupTokens.Add(cancellationToken);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class PauseConfirmationSave : SaveChangesInterceptor
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Fail { get; init; }
        public IsolationLevel? Isolation { get; private set; }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var db = eventData.Context!;
            if (db.ChangeTracker.Entries<TrackSourceId>().Any(entry => entry.State == EntityState.Added
                && entry.Entity.ExternalId == "chosen-id"))
            {
                Isolation = db.Database.CurrentTransaction!.GetDbTransaction().IsolationLevel;
                Started.TrySetResult();
                await Continue.Task.WaitAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (Fail) throw new InvalidOperationException("Injected confirmation save failure.");
            }
            return result;
        }
    }
}
