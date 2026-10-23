using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using System.Data.Common;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed partial class PlaylistUnmatchedReviewTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task RejectedSpotifyRead_ExistingTransactionKeepsCallerWritesAndRollsBackOnlyRejectedRead(bool reject)
        => AssertExistingReadTransactionAsync(reject, postgres: false);

    [PostgresFact]
    public async Task RejectedSpotifyRead_PostgresExistingTransactionUsesSavepointWithRetryEnabled()
    {
        await AssertExistingReadTransactionAsync(reject: true, postgres: true);
        await AssertExistingReadTransactionAsync(reject: false, postgres: true);
    }

    private static async Task AssertExistingReadTransactionAsync(bool reject, bool postgres)
    {
        await using var fixture = await ProductionSpotifyReadScope.CreateAsync(postgres: postgres);
        await fixture.SyncAsync();
        fixture.Handler.RemoteIds = ["chosen-id", "fresh-id"];
        var failure = new PartialReadFailure("fresh-id", true, null);
        await using var reader = fixture.Scope.NewContext(reject ? [failure] : []);
        var unrelated = new Track { Id = Guid.NewGuid(), SearchTitle = "Caller transaction recording" };
        await new PlaylistSyncCoordinator.PlaylistTransactionStrategy(reader).ExecuteAsync(async () =>
        {
            await using var transaction = await reader.Database.BeginTransactionAsync();
            reader.Tracks.Add(unrelated);
            await reader.SaveChangesAsync();
            var read = fixture.ProviderFor(reader).ReadAsync(fixture.Account, "independent-playlist", default);
            if (reject) await Assert.ThrowsAsync<InvalidOperationException>(() => read);
            else Assert.Equal(2, (await read).Tracks.Count);
            await reader.SaveChangesAsync();
            await transaction.CommitAsync();
        });
        await using var verify = fixture.Scope.NewContext();
        Assert.True(await verify.Tracks.AnyAsync(t => t.Id == unrelated.Id));
        if (reject) await AssertRejectedReadIsolationAsync(fixture, verify);
        else Assert.True(await verify.TrackSourceIds.AnyAsync(s => s.ExternalId == "chosen-id"));
    }

    [Fact]
    public async Task RetainedSpotifyRead_LifecycleDisconnectAndReconnectPreviewKeepReadOnlyProvenance()
    {
        await using var fixture = await ProductionSpotifyReadScope.CreateAsync();
        await fixture.SyncAsync();
        var lifecycle = SpotifyReadLifecycle(fixture);
        await lifecycle.DisconnectAsync(fixture.Account.UserId, "spotify", [new(fixture.Scope.Link.Id, false, false)], default);
        Assert.Equal("paused", (await fixture.Db.ServicePlaylistMappings.AsNoTracking().SingleAsync()).State);
        await Assert.ThrowsAsync<PlatformApiException>(() => fixture.Provider.ReadAsync(fixture.Account, "playlist", default));
        using (var other = fixture.Scope.NewContext())
            await other.ConnectedServiceAccounts.ExecuteUpdateAsync(s => s.SetProperty(a => a.ConnectionState, "connected")
                .SetProperty(a => a.TokenVersion, a => a.TokenVersion + 1));
        fixture.Db.ChangeTracker.Clear();
        var preview = await lifecycle.PreviewAttachAsync(fixture.Account.UserId, fixture.Scope.Playlist.Id, "spotify", "playlist", default);
        Assert.Equal(0, preview.Additions);
        Assert.Equal(0, preview.Removals);
        await fixture.Db.SaveChangesAsync();
        await using var verify = fixture.Scope.NewContext();
        await AssertRejectedReadIsolationAsync(fixture, verify);
        Assert.Equal("paused", (await verify.ServicePlaylistMappings.SingleAsync()).State);
        Assert.Single(fixture.Writer.Writes);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RejectedSpotifyRead_PartialMaterializationPreservesCallerChangesAndCannotBeSavedLater(bool stagedSave, bool cancel)
    {
        await using var fixture = await ProductionSpotifyReadScope.CreateAsync();
        await fixture.SyncAsync();
        fixture.Handler.RemoteIds = ["chosen-id", "fresh-id"];
        using var cancellation = new CancellationTokenSource();
        var failure = new PartialReadFailure("fresh-id", stagedSave, cancel ? cancellation : null);
        await using var reader = fixture.Scope.NewContext(failure);
        var original = await reader.Tracks.SingleAsync();
        original.SearchTitle = "Caller edit";
        var unrelated = new Track { Id = Guid.NewGuid(), SearchTitle = "Caller new recording" };
        reader.Tracks.Add(unrelated);
        var read = fixture.ProviderFor(reader).ReadAsync(fixture.Account, "independent-playlist", cancellation.Token);
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        else await Assert.ThrowsAsync<InvalidOperationException>(() => read);
        Assert.Equal(1, failure.Failures);
        Assert.Equal(EntityState.Modified, reader.Entry(original).State);
        Assert.Equal(EntityState.Added, reader.Entry(unrelated).State);
        Assert.Equal("Caller edit", original.SearchTitle);
        Assert.Empty(reader.ChangeTracker.Entries<TrackSourceId>());
        await reader.SaveChangesAsync();
        await using var verify = fixture.Scope.NewContext();
        await AssertRejectedReadIsolationAsync(fixture, verify);
        Assert.Equal("Caller edit", (await verify.Tracks.SingleAsync(t => t.Id == original.Id)).SearchTitle);
        Assert.True(await verify.Tracks.AnyAsync(t => t.Id == unrelated.Id));
        Assert.Equal(2, await verify.Tracks.CountAsync());
        Assert.False(await verify.TrackSourceIds.AnyAsync(s => s.ExternalId == "fresh-id"));
    }

    [Fact]
    public async Task RejectedSpotifyRead_CancelledCoordinatorRollsBackStagedMaterializationAndReleasesLease()
    {
        await using var fixture = await ProductionSpotifyReadScope.CreateAsync();
        await fixture.SyncAsync();
        fixture.Handler.RemoteIds = ["chosen-id", "fresh-id", "broken-id"];
        using var cancellation = new CancellationTokenSource();
        var failure = new PartialReadFailure("broken-id", true, cancellation);
        await using var reader = fixture.Scope.NewContext(failure);
        var coordinator = new PlaylistSyncCoordinator(reader, [fixture.ProviderFor(reader)], [fixture.Writer],
            TimeProvider.System, NullLogger<PlaylistSyncCoordinator>.Instance);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.RunAsync(fixture.Account.UserId,
            fixture.Scope.Playlist.Id, "spotify", cancellation.Token));
        await reader.SaveChangesAsync();
        await using var verify = fixture.Scope.NewContext();
        Assert.Equal(1, failure.Failures);
        Assert.Null((await verify.Playlists.SingleAsync()).SyncLeaseId);
        Assert.Equal("pending", (await verify.ServicePlaylistMappings.SingleAsync()).LastSyncStatus);
        await AssertRejectedReadIsolationAsync(fixture, verify);
        Assert.Single(await verify.Tracks.ToArrayAsync());
        Assert.Empty(await verify.TrackSourceIds.ToArrayAsync());
        // The same lease can be acquired again after cancellation.
        fixture.Handler.RemoteIds = ["chosen-id"];
        await fixture.SyncAsync();
        Assert.Equal("success", (await verify.ServicePlaylistMappings.AsNoTracking().SingleAsync()).LastSyncStatus);
    }

    [PostgresFact]
    public async Task RejectedSpotifyRead_PostgresLateAccountChangesRollBackSavedMetadataWithRetryEnabled()
    {
        foreach (var change in new[] { "external", "token", "disconnected" })
        {
            await using var fixture = await ProductionSpotifyReadScope.CreateAsync(postgres: true);
            await fixture.SyncAsync();
            fixture.Handler.RemoteIds.Add("fresh-id");
            var late = new AccountDuringSave(async () =>
            {
                await using var other = fixture.Scope.NewContext();
                if (change == "external") await other.ConnectedServiceAccounts.ExecuteUpdateAsync(s => s.SetProperty(a => a.ExternalAccountId, "replacement"));
                else if (change == "token") await other.ConnectedServiceAccounts.ExecuteUpdateAsync(s => s.SetProperty(a => a.TokenVersion, a => a.TokenVersion + 1));
                else await other.ConnectedServiceAccounts.ExecuteUpdateAsync(s => s.SetProperty(a => a.ConnectionState, "disconnected"));
            });
            await using var reader = fixture.Scope.NewContext(late);
            await new PlaylistSyncCoordinator(reader, [fixture.ProviderFor(reader)], [fixture.Writer], TimeProvider.System,
                NullLogger<PlaylistSyncCoordinator>.Instance).RunAsync(fixture.Account.UserId, fixture.Scope.Playlist.Id, "spotify", default);
            await reader.SaveChangesAsync();
            await using var verify = fixture.Scope.NewContext();
            Assert.True(late.Changed);
            Assert.Equal("error", (await verify.ServicePlaylistMappings.SingleAsync()).LastSyncStatus);
            Assert.Null((await verify.Playlists.SingleAsync()).SyncLeaseId);
            await AssertRejectedReadIsolationAsync(fixture, verify);
            Assert.Empty(await verify.TrackSourceIds.ToArrayAsync());
            Assert.Single(await verify.Tracks.ToArrayAsync());
            Assert.Single(fixture.Writer.Writes);
        }
    }

    private sealed class PartialReadFailure(string externalId, bool stagedSave, CancellationTokenSource? cancel) : DbCommandInterceptor
    {
        public int Failures { get; private set; }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (Failures != 0 || !command.CommandText.Contains("TrackObservations", StringComparison.Ordinal)
                || !command.Parameters.Cast<DbParameter>().Any(p => Equals(p.Value, externalId))
                || eventData.Context is not { } db || !db.ChangeTracker.Entries<TrackSourceId>().Any(e => e.State == EntityState.Added))
                return result;
            Failures++;
            if (stagedSave) await db.SaveChangesAsync(ct);
            if (cancel is not null) { cancel.Cancel(); ct.ThrowIfCancellationRequested(); }
            throw new InvalidOperationException("Rejected after the first real Spotify track materialized.");
        }
    }

    private sealed class AccountDuringSave(Func<Task> change) : SaveChangesInterceptor
    {
        public bool Changed { get; private set; }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (!Changed && eventData.Context!.ChangeTracker.Entries<TrackSourceId>().Any(e => e.State == EntityState.Added))
            {
                Changed = true;
                await change();
            }
            return result;
        }
    }
    [Theory]
    [InlineData("external")]
    [InlineData("token")]
    [InlineData("disconnected")]
    public async Task RejectedSpotifyRead_AccountChangesDuringHttpCannotEscapeThroughCoordinatorSave(string change)
    {
        await using var fixture = await ProductionSpotifyReadScope.CreateAsync();
        await fixture.SyncAsync();
        var nextRead = fixture.Handler.Reads + 1;
        fixture.Handler.OnRead = number =>
        {
            if (number != nextRead) return;
            using var other = fixture.Scope.NewContext();
            if (change == "external") other.ConnectedServiceAccounts.ExecuteUpdate(set => set.SetProperty(a => a.ExternalAccountId, "replacement"));
            else if (change == "token") other.ConnectedServiceAccounts.ExecuteUpdate(set => set.SetProperty(a => a.TokenVersion, a => a.TokenVersion + 1));
            else other.ConnectedServiceAccounts.ExecuteUpdate(set => set.SetProperty(a => a.ConnectionState, "disconnected"));
        };
        await fixture.SyncAsync();
        await fixture.Db.SaveChangesAsync(); // The coordinator and a later caller must both be safe.
        await using var verify = fixture.Scope.NewContext();
        Assert.Equal("error", (await verify.ServicePlaylistMappings.SingleAsync()).LastSyncStatus);
        Assert.Null((await verify.Playlists.SingleAsync()).SyncLeaseId);
        await AssertRejectedReadIsolationAsync(fixture, verify);
        Assert.Single(fixture.Writer.Writes);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task RetainedSpotifyRead_RealMissingTransitionAndLifecyclePreviewPreserveExportProvenance(bool addNew, bool pending)
    {
        await using var fixture = await ProductionSpotifyReadScope.CreateAsync();
        fixture.Writer.ThrowAfterWrite = pending;
        await fixture.SyncAsync();
        fixture.Handler.MissingNextRead = true;
        await fixture.SyncAsync();
        await using (var missing = fixture.Scope.NewContext())
        {
            var link = await missing.ServicePlaylistMappings.SingleAsync();
            Assert.Equal("missing", link.State);
            Assert.Equal("error", link.LastSyncStatus);
            Assert.NotNull(link.BaselineJson);
            Assert.Null((await missing.Playlists.SingleAsync()).SyncLeaseId);
        }
        if (addNew) fixture.Handler.RemoteIds.Add("fresh-id");
        fixture.Db.ChangeTracker.Clear();
        var lifecycle = SpotifyReadLifecycle(fixture);
        var preview = await lifecycle.PreviewAttachAsync(fixture.Account.UserId, fixture.Scope.Playlist.Id,
            "spotify", "playlist", default);
        Assert.Equal(addNew ? 2 : 1, preview.RemoteTrackCount);
        Assert.Equal(addNew ? 1 : 0, preview.Additions);
        Assert.Equal(0, preview.Removals);
        await fixture.Db.SaveChangesAsync();
        await using var verify = fixture.Scope.NewContext();
        await AssertRejectedReadIsolationAsync(fixture, verify);
        Assert.Equal("missing", (await verify.ServicePlaylistMappings.SingleAsync()).State);
        Assert.Single(fixture.Writer.Writes);
        if (addNew) Assert.True(await verify.TrackSourceIds.AnyAsync(s => s.ExternalId == "fresh-id"));
    }

    [Fact]
    public async Task RetainedSpotifyRead_UnlinkPreviewAndReattachNeverIngestExportedMetadata()
    {
        await using var fixture = await ProductionSpotifyReadScope.CreateAsync();
        await fixture.SyncAsync();
        var lifecycle = SpotifyReadLifecycle(fixture);
        await lifecycle.UnlinkAsync(fixture.Account.UserId, fixture.Scope.Playlist.Id, fixture.Scope.Link.Id, false, false, default);
        Assert.Equal("unlinked", (await fixture.Db.ServicePlaylistMappings.AsNoTracking().SingleAsync()).State);
        var entry = await fixture.Db.PlaylistEntries.Include(e => e.TrackObservation).Include(e => e.Track).SingleAsync();
        // Retained membership supplies no outbound authority while inactive.
        Assert.Null(await fixture.Provider.ResolveAsync(fixture.Account, entry, default));
        var preview = await lifecycle.PreviewAttachAsync(fixture.Account.UserId, fixture.Scope.Playlist.Id,
            "spotify", "playlist", default);
        await lifecycle.AttachAsync(fixture.Account.UserId, fixture.Scope.Playlist.Id, "spotify", "playlist",
            "bidirectional", "combine", preview.PreviewToken, default);
        await fixture.Db.SaveChangesAsync();
        await using var verify = fixture.Scope.NewContext();
        await AssertRejectedReadIsolationAsync(fixture, verify);
        Assert.Equal("active", (await verify.ServicePlaylistMappings.SingleAsync()).State);
        Assert.Null((await verify.Playlists.SingleAsync()).SyncLeaseId);
        Assert.Single(fixture.Writer.Writes);
    }

    [Theory]
    [InlineData("account")]
    [InlineData("observation")]
    [InlineData("entry")]
    [InlineData("receipt")]
    public async Task RetainedSpotifyRead_InvalidRetainedScopeRejectsInsteadOfImportingDestination(string change)
    {
        await using var fixture = await ProductionSpotifyReadScope.CreateAsync();
        await fixture.SyncAsync();
        using (var other = fixture.Scope.NewContext())
        {
            if (change == "account")
                await other.ConnectedServiceAccounts.ExecuteUpdateAsync(s => s.SetProperty(a => a.ExternalAccountId, "replacement"));
            else if (change == "receipt")
            {
                var link = await other.ServicePlaylistMappings.SingleAsync();
                link.BaselineJson = link.BaselineJson!.Replace("\"ExternalAccountId\":\"account\"", "\"ExternalAccountId\":\"different\"");
                link.MatchingProgressJson = null;
                await other.SaveChangesAsync();
            }
            else
            {
                var track = new Track { Id = Guid.NewGuid(), SearchTitle = "Different recording" };
                other.Tracks.Add(track);
                await other.SaveChangesAsync();
                if (change == "observation") await other.TrackObservations.ExecuteUpdateAsync(s => s.SetProperty(o => o.TrackId, track.Id));
                else await other.PlaylistEntries.ExecuteUpdateAsync(s => s.SetProperty(e => e.TrackId, track.Id));
            }
        }
        fixture.Db.ChangeTracker.Clear();
        var account = fixture.Account;
        if (change == "account")
        {
            fixture.Handler.ProfileId = "replacement";
            account = account with { ExpectedExternalAccountId = "replacement" };
        }
        var error = await Assert.ThrowsAsync<PlatformApiException>(() => fixture.Provider.ReadAsync(account, "playlist", default));
        Assert.Equal("remote_changed", error.Code);
        await fixture.Db.SaveChangesAsync();
        await using var verify = fixture.Scope.NewContext();
        Assert.False(await verify.TrackSourceIds.AnyAsync(s => s.ExternalId == "chosen-id"));
        Assert.False(await verify.TrackObservations.AnyAsync(o => o.SourceType == "spotify"));
        Assert.False(await verify.TrackArtistCredits.AnyAsync());
        Assert.Null((await verify.Tracks.SingleAsync(t => t.Id == fixture.Scope.Entry.TrackId)).Isrc);
    }

    private static PlaylistLinkLifecycleService SpotifyReadLifecycle(ProductionSpotifyReadScope fixture)
        => new(fixture.Db, new PlatformRegistry([new ReadTestPlatform(fixture.Db, "spotify")]),
            [fixture.Provider], fixture.Coordinator(), new EphemeralDataProtectionProvider());

    private static async Task AssertRejectedReadIsolationAsync(ProductionSpotifyReadScope fixture, ApplicationDbContext verify)
    {
        Assert.False(await verify.TrackSourceIds.AnyAsync(s => s.SourceType == "spotify" && s.ExternalId == "chosen-id"));
        Assert.False(await verify.TrackObservations.AnyAsync(s => s.SourceType == "spotify" && s.ExternalId == "chosen-id"));
        var track = await verify.Tracks.SingleAsync(t => t.Id == fixture.Scope.Entry.TrackId);
        Assert.Null(track.Isrc);
        Assert.Null(System.Text.Json.JsonSerializer.Deserialize<TrackCanonicalMetadata>(track.CanonicalMetadata!)!.ThumbnailUrl);
        Assert.False(await verify.TrackArtistCredits.AnyAsync(c => c.TrackId == track.Id));
        var entry = await verify.PlaylistEntries.SingleAsync(e => e.Id == fixture.Scope.Entry.Id);
        Assert.Equal((fixture.Scope.Entry.TrackId, fixture.Scope.Entry.TrackObservationId), (entry.TrackId, entry.TrackObservationId));
        Assert.Equal(track.Id, (await verify.TrackObservations.SingleAsync(o => o.Id == entry.TrackObservationId)).TrackId);
    }

    private sealed class ReadTestPlatform(ApplicationDbContext db, string service) : IPlatformService
    {
        public string PlatformId => service;
        public Task<ConnectedServiceAccount?> GetConnectedAccountAsync(int userId)
            => db.ConnectedServiceAccounts.SingleOrDefaultAsync(a => a.UserId == userId && a.Service == service);
        public bool TryValidatePlaylistId(string id, out string? error) { error = null; return true; }
        public string GetAuthorizationUrl(string uri, string state) => throw new NotSupportedException();
        public Task<ConnectedServiceAccount> ExchangeCodeAndSaveAsync(int userId, string code, string uri) => throw new NotSupportedException();
        public async Task DisconnectAsync(int userId)
        {
            var account = await db.ConnectedServiceAccounts.SingleAsync(a => a.UserId == userId && a.Service == service);
            account.ConnectionState = "disconnected";
            account.TokenVersion++;
            foreach (var link in await db.ServicePlaylistMappings.Where(l => l.UserId == userId && l.Service == service && l.State == "active").ToListAsync())
            {
                link.State = "paused";
                link.LastError = "account_disconnected";
            }
            await db.SaveChangesAsync();
        }
        public Task<IReadOnlyList<PlatformPlaylistDto>> GetPlaylistsAsync(int userId) => throw new NotSupportedException();
        public Task<IReadOnlyList<PlatformSongDto>> GetPlaylistSongsAsync(int userId, string id) => throw new NotSupportedException();
        public Task<Guid> SyncPlaylistAsync(PlatformAccountContext account, string id, CancellationToken ct) => throw new NotSupportedException();
    }
}
