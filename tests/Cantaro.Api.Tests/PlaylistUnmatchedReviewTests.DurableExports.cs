using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Cantaro.Api.Services.Spotify;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed partial class PlaylistUnmatchedReviewTests
{
    public static IEnumerable<object[]> DurableExportHistoryCases(bool postgres)
    {
        foreach (var service in new[] { "spotify", "youtube" })
        foreach (var history in new[] { "delete", "remove" })
        foreach (var read in new[] { "provider", "preview", "import" })
            yield return [service, history, read, postgres];
    }

    [Theory]
    [MemberData(nameof(DurableExportHistoryCases), false)]
    public Task DurableExport_ProductionReadsRejectForgottenScopedExports(string service, string history, string read, bool postgres)
        => CheckDurableExportHistoryAsync(service, history, read, postgres);

    [PostgresTheory]
    [MemberData(nameof(DurableExportHistoryCases), true)]
    public Task DurableExport_PostgresProductionReadsRejectForgottenScopedExports(string service, string history, string read, bool postgres)
        => CheckDurableExportHistoryAsync(service, history, read, postgres);

    private static async Task CheckDurableExportHistoryAsync(string service, string history, string read, bool postgres)
    {
        await using var fixture = await ReplacementReadFixture.CreateAsync(service, postgres);
        var scope = fixture.Scope;
        await fixture.SyncAsync(scope.Playlist.Id);
        Assert.Equal(new[] { "chosen-id" }, fixture.RemoteIds);
        await using (var lifecycleDb = scope.NewContext())
        {
            if (history == "delete")
                await fixture.Lifecycle(lifecycleDb).UnlinkAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, false, true, default);
            else
            {
                lifecycleDb.PlaylistEntries.RemoveRange(await lifecycleDb.PlaylistEntries.ToArrayAsync());
                await lifecycleDb.SaveChangesAsync();
            }
        }
        if (history == "remove")
        {
            await fixture.SyncAsync(scope.Playlist.Id);
            Assert.Empty(fixture.RemoteIds);
            await using var lifecycleDb = scope.NewContext();
            var link = await lifecycleDb.ServicePlaylistMappings.SingleAsync();
            Assert.Empty(PlaylistSyncBaseline.Parse(link.BaselineJson)!.Items);
            Assert.Empty(PlaylistMatchingProgress.Read(link.MatchingProgressJson));
            Assert.Null(link.PendingWriteJson);
            await fixture.Lifecycle(lifecycleDb).UnlinkAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, false, false, default);
            // Import requires no retained link. Its ordinary missing-link branch
            // is also what local deletion exercises, without altering the remote.
            if (read == "import")
            {
                lifecycleDb.ServicePlaylistMappings.Remove(link);
                await lifecycleDb.SaveChangesAsync();
            }
        }
        await using (var verify = scope.NewContext())
        {
            if (history == "delete")
            {
                Assert.False(await verify.Playlists.AnyAsync());
                Assert.False(await verify.ServicePlaylistMappings.AnyAsync());
                Assert.False(await verify.PlaylistEntries.AnyAsync());
            }
            await AssertReplacementReadIsolationAsync(scope, verify, service);
        }
        fixture.RemoteIds = ["chosen-id", "chosen-id"];
        await using var readDb = scope.NewContext();
        var account = new PlatformAccountContext(scope.UserId, scope.Link.ConnectedServiceAccountId, "account");
        var target = new Playlist { Id = Guid.NewGuid(), UserId = scope.UserId, Name = "Another local playlist" };
        readDb.Playlists.Add(target);
        await readDb.SaveChangesAsync();
        PlatformApiException? rejected = null;
        try
        {
            if (read == "provider") await fixture.Provider(readDb).ReadAsync(account, "playlist", default);
            else if (read == "preview") await fixture.Lifecycle(readDb).PreviewAttachAsync(scope.UserId, target.Id, service, "playlist", default);
            else await fixture.Coordinator(readDb).ImportAsync(account, service, "playlist", default);
        }
        catch (PlatformApiException error) { rejected = error; }
        await readDb.SaveChangesAsync();
        await using var persisted = scope.NewContext();
        // Check persisted pollution before the expected rejection so the before
        // run records the concrete provider-ingestion gap, not just an API change.
        await AssertReplacementReadIsolationAsync(scope, persisted, service);
        Assert.NotNull(rejected);
        Assert.Equal("remote_changed", rejected.Code);
        Assert.False(await persisted.PlaylistEntries.AnyAsync(entry => entry.PlaylistId == target.Id));
    }

    public static IEnumerable<object[]> DurableExportFailureCases(bool postgres)
    {
        foreach (var service in new[] { "spotify", "youtube" })
        foreach (var failure in new[] { "before", "after", "cancel" })
            yield return [service, failure, postgres];
    }

    [Theory]
    [MemberData(nameof(DurableExportFailureCases), false)]
    public Task DurableExport_AttemptIsCommittedBeforeFailedOrCancelledWrite(string service, string failure, bool postgres)
        => CheckDurableExportFailureAsync(service, failure, postgres);

    [PostgresTheory]
    [MemberData(nameof(DurableExportFailureCases), true)]
    public Task DurableExport_PostgresAttemptIsCommittedBeforeFailedOrCancelledWrite(string service, string failure, bool postgres)
        => CheckDurableExportFailureAsync(service, failure, postgres);

    private static async Task CheckDurableExportFailureAsync(string service, string failure, bool postgres)
    {
        await using var fixture = await ReplacementReadFixture.CreateAsync(service, postgres, exportYouTube: false);
        var scope = fixture.Scope;
        using var cancellation = new CancellationTokenSource();
        var called = false;
        var writer = new DurableExportWriter(fixture.Writer, async ids =>
        {
            await using var verify = scope.NewContext();
            var row = Assert.Single(await verify.PlaylistExportAttempts.ToArrayAsync());
            Assert.Equal((scope.UserId, service, "account", "playlist", "chosen-id"),
                (row.UserId, row.Service, row.ExternalAccountId, row.ServicePlaylistId, row.ExternalTrackId));
            Assert.Equal(new[] { "chosen-id" }, ids);
            Assert.Empty(fixture.RemoteIds);
            called = true;
            if (failure == "before") throw new HttpRequestException("Provider rejected before mutation");
        }, () =>
        {
            if (failure == "cancel") { cancellation.Cancel(); cancellation.Token.ThrowIfCancellationRequested(); }
            throw new HttpRequestException("Provider acknowledgement lost after mutation");
        });
        await using (var writeDb = scope.NewContext())
        {
            var coordinator = new PlaylistSyncCoordinator(writeDb, [fixture.Provider(writeDb)], [writer],
                TimeProvider.System, NullLogger<PlaylistSyncCoordinator>.Instance);
            if (failure == "cancel")
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.RunAsync(scope.UserId, scope.Playlist.Id, service, cancellation.Token));
            else await coordinator.RunAsync(scope.UserId, scope.Playlist.Id, service, default);
            await writeDb.SaveChangesAsync();
        }
        Assert.True(called);
        await using (var lifecycleDb = scope.NewContext())
        {
            Assert.Single(await lifecycleDb.PlaylistExportAttempts.ToArrayAsync());
            Assert.Null((await lifecycleDb.Playlists.SingleAsync()).SyncLeaseId);
            await fixture.Lifecycle(lifecycleDb).UnlinkAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, false, true, default);
        }
        fixture.RemoteIds = ["chosen-id"];
        await using var readDb = scope.NewContext();
        await Assert.ThrowsAsync<PlatformApiException>(() => fixture.Provider(readDb)
            .ReadAsync(new(scope.UserId, scope.Link.ConnectedServiceAccountId, "account"), "playlist", default));
        await readDb.SaveChangesAsync();
        await using var persisted = scope.NewContext();
        await AssertReplacementReadIsolationAsync(scope, persisted, service);
        Assert.Single(await persisted.PlaylistExportAttempts.ToArrayAsync());
    }

    public static IEnumerable<object[]> DurableExportScopeCases(bool postgres)
    {
        foreach (var service in new[] { "spotify", "youtube" })
        foreach (var scope in new[] { "same", "reconnect", "account", "user", "remote", "service", "recording", "observation" })
            yield return [service, scope, postgres];
    }

    [Theory]
    [MemberData(nameof(DurableExportScopeCases), false)]
    public Task DurableExport_ExactReadScopeAndIndependentEvidence(string service, string scopeChange, bool postgres)
        => CheckDurableExportScopeAsync(service, scopeChange, postgres);

    [PostgresTheory]
    [MemberData(nameof(DurableExportScopeCases), true)]
    public Task DurableExport_PostgresExactReadScopeAndIndependentEvidence(string service, string scopeChange, bool postgres)
        => CheckDurableExportScopeAsync(service, scopeChange, postgres);

    private static async Task CheckDurableExportScopeAsync(string service, string scopeChange, bool postgres)
    {
        await using var fixture = await ReplacementReadFixture.CreateAsync(service, postgres);
        var scope = fixture.Scope;
        await fixture.SyncAsync(scope.Playlist.Id);
        await using (var deleteDb = scope.NewContext())
            await fixture.Lifecycle(deleteDb).UnlinkAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, false, true, default);
        var account = new PlatformAccountContext(scope.UserId, scope.Link.ConnectedServiceAccountId, "account");
        var remote = "playlist";
        await using (var setup = scope.NewContext())
        {
            var saved = await setup.ConnectedServiceAccounts.SingleAsync();
            if (scopeChange is "reconnect" or "user")
            {
                if (scopeChange == "reconnect") setup.ConnectedServiceAccounts.Remove(saved);
                else setup.Users.Add(TestUserFactory.Create(scope.UserId + 1, "independent-durable@example.com"));
                await setup.SaveChangesAsync();
                var userId = scopeChange == "reconnect" ? scope.UserId : scope.UserId + 1;
                setup.ConnectedServiceAccounts.Add(new ConnectedServiceAccount { Id = 52, UserId = userId,
                    Service = service, ExternalAccountId = "account", ConnectionState = "connected",
                    EncryptedAccessToken = saved.EncryptedAccessToken, Scopes = saved.Scopes,
                    TokenExpiresAt = saved.TokenExpiresAt, TokenVersion = saved.TokenVersion + 1 });
                account = new(userId, 52, "account");
            }
            else if (scopeChange == "account")
            {
                saved.ExternalAccountId = "other-account";
                saved.TokenVersion++;
                fixture.ProfileId = "other-account";
                account = account with { ExpectedExternalAccountId = "other-account" };
            }
            else if (scopeChange == "remote") remote = "other-remote";
            else if (scopeChange == "service")
            {
                var row = await setup.PlaylistExportAttempts.SingleAsync();
                setup.PlaylistExportAttempts.Remove(row);
                setup.PlaylistExportAttempts.Add(new PlaylistExportAttempt { UserId = row.UserId,
                    Service = service == "spotify" ? "youtube" : "spotify", ExternalAccountId = row.ExternalAccountId,
                    ServicePlaylistId = row.ServicePlaylistId, ExternalTrackId = row.ExternalTrackId });
            }
            else if (scopeChange == "recording")
                setup.TrackSourceIds.Add(new TrackSourceId { TrackId = scope.Entry.TrackId!.Value, SourceType = service, ExternalId = "chosen-id" });
            else if (scopeChange == "observation")
                setup.TrackObservations.Add(new TrackObservation { Id = Guid.NewGuid(), SourceType = service,
                    ExternalId = "chosen-id", Title = "Previously independent observation", TrackId = scope.Entry.TrackId,
                    MatchStatus = TrackMatchingStatuses.Matched });
            await setup.SaveChangesAsync();
            Assert.Single(await setup.PlaylistExportAttempts.ToArrayAsync());
        }
        await using var readDb = scope.NewContext();
        if (scopeChange is "same" or "reconnect")
        {
            var rejected = await Assert.ThrowsAsync<PlatformApiException>(() => fixture.Provider(readDb).ReadAsync(account, remote, default));
            Assert.Equal("remote_changed", rejected.Code);
            await readDb.SaveChangesAsync();
            await using var verify = scope.NewContext();
            await AssertReplacementReadIsolationAsync(scope, verify, service);
        }
        else
        {
            var read = await fixture.Provider(readDb).ReadAsync(account, remote, default);
            Assert.Equal("chosen-id", Assert.Single(read.Tracks).ExternalId);
            await readDb.SaveChangesAsync();
            await using var verify = scope.NewContext();
            var observation = await verify.TrackObservations.SingleAsync(item => item.SourceType == service && item.ExternalId == "chosen-id");
            if (scopeChange is "recording" or "observation") Assert.Equal(scope.Entry.TrackId, observation.TrackId);
            else if (service == "spotify") Assert.NotNull(observation.TrackId);
            else Assert.Null(observation.TrackId);
        }
    }

    [Theory]
    [InlineData("spotify", false)]
    [InlineData("youtube", false)]
    public Task DurableExport_DeduplicatesOccurrencesAndRetainsReplacedIdsWithoutRenewingAuthority(string service, bool postgres)
        => CheckDurableExportReplacementAsync(service, postgres);

    [PostgresTheory]
    [InlineData("spotify", true)]
    [InlineData("youtube", true)]
    public Task DurableExport_PostgresDeduplicatesOccurrencesAndRetainsReplacedIdsWithoutRenewingAuthority(string service, bool postgres)
        => CheckDurableExportReplacementAsync(service, postgres);

    private static async Task CheckDurableExportReplacementAsync(string service, bool postgres)
    {
        await using var fixture = await ReplacementReadFixture.CreateAsync(service, postgres);
        var scope = fixture.Scope;
        await using (var setup = scope.NewContext())
        {
            (await setup.Playlists.SingleAsync()).AllowDuplicateTracks = true;
            var duplicate = new PlaylistEntry { Id = Guid.NewGuid(), PlaylistId = scope.Playlist.Id,
                TrackId = scope.Entry.TrackId, TrackObservationId = scope.Entry.TrackObservationId, Position = 1 };
            setup.PlaylistEntries.Add(duplicate);
            await setup.SaveChangesAsync();
            await scope.CreateReview(setup).ConfirmAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, duplicate.Id,
                scope.Token(new TrackMatchSearchCandidate { CandidateSource = service, ExternalId = "chosen-id",
                    Title = "Everything Goes On", Artist = "Porter Robinson", ArtistCredits = ["Porter Robinson"], DurationSeconds = 160 },
                    entryId: duplicate.Id), default);
        }
        for (var retry = 0; retry < 3; retry++) await fixture.SyncAsync(scope.Playlist.Id);
        Assert.Equal(new[] { "chosen-id", "chosen-id" }, fixture.RemoteIds);
        await using (var setup = scope.NewContext())
        {
            Assert.Single(await setup.PlaylistExportAttempts.ToArrayAsync());
            var duplicate = await setup.PlaylistEntries.SingleAsync(entry => entry.Id != scope.Entry.Id);
            setup.PlaylistEntries.Remove(duplicate);
            // A user can change a choice after its metadata proof expires.
            (await setup.Tracks.SingleAsync()).SearchTitle += " revised";
            await setup.SaveChangesAsync();
            var candidate = new TrackMatchSearchCandidate { CandidateSource = service, ExternalId = "replacement-id",
                Title = "Everything Goes On", Artist = "Porter Robinson", ArtistCredits = ["Porter Robinson"], DurationSeconds = 160 };
            await scope.CreateReview(setup).ConfirmAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, scope.Entry.Id, scope.Token(candidate), default);
        }
        await fixture.SyncAsync(scope.Playlist.Id);
        Assert.Equal(new[] { "replacement-id" }, fixture.RemoteIds);
        await using (var setup = scope.NewContext())
        {
            Assert.Equal(new[] { "chosen-id", "replacement-id" }, await setup.PlaylistExportAttempts.OrderBy(row => row.ExternalTrackId).Select(row => row.ExternalTrackId).ToArrayAsync());
            var link = await setup.ServicePlaylistMappings.SingleAsync();
            Assert.Equal("replacement-id", Assert.Single(PlaylistSyncBaseline.Parse(link.BaselineJson)!.Items).ExternalId);
            Assert.DoesNotContain(PlaylistMatchingProgress.Read(link.MatchingProgressJson).Values, proof => proof.ExternalId == "chosen-id");
            // Expire the current choice while retaining its local membership.
            (await setup.Tracks.SingleAsync()).SearchTitle += " edited";
            link.SyncMode = "from_cantaro";
            await setup.SaveChangesAsync();
        }
        fixture.RemoteIds = [];
        await fixture.SyncAsync(scope.Playlist.Id);
        Assert.Empty(fixture.RemoteIds);
        await using (var verify = scope.NewContext())
        {
            Assert.Equal("partial", (await verify.ServicePlaylistMappings.SingleAsync()).LastSyncStatus);
            Assert.Single(await verify.PlaylistEntries.ToArrayAsync());
            Assert.Equal(2, await verify.PlaylistExportAttempts.CountAsync());
        }
        fixture.RemoteIds = ["chosen-id"];
        await using var readDb = scope.NewContext();
        var error = await Assert.ThrowsAsync<PlatformApiException>(() => fixture.Provider(readDb)
            .ReadAsync(new(scope.UserId, scope.Link.ConnectedServiceAccountId, "account"), "playlist", default));
        Assert.Equal("remote_changed", error.Code);
        await using var persisted = scope.NewContext();
        Assert.Empty(await persisted.TrackSourceIds.ToArrayAsync());
        Assert.False(await persisted.TrackObservations.AnyAsync(row => row.SourceType == service));
        Assert.Empty(await persisted.TrackArtistCredits.ToArrayAsync());
        Assert.Empty(await persisted.TrackMatchQueueItems.ToArrayAsync());
    }

    [PostgresTheory]
    [InlineData("spotify")]
    [InlineData("youtube")]
    public async Task DurableExport_PostgresReadRollbackIfAttemptCommitsDuringMaterialization(string service)
    {
        await using var fixture = await ReplacementReadFixture.CreateAsync(service, true, exportYouTube: false);
        var scope = fixture.Scope;
        await using (var deleteDb = scope.NewContext())
            await fixture.Lifecycle(deleteDb).UnlinkAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, false, true, default);
        fixture.RemoteIds = ["chosen-id"];
        var account = new PlatformAccountContext(scope.UserId, scope.Link.ConnectedServiceAccountId, "account");
        var interceptor = new AttemptDuringReadSave(service, async () =>
        {
            await using var exportDb = scope.NewContext();
            await PlaylistExportAttemptStore.RecordAsync(exportDb, account, service, "playlist", ["chosen-id"], default);
        });
        await using var readDb = scope.NewContext(interceptor);
        var unrelated = new Track { Id = Guid.NewGuid(), SearchTitle = "Caller pending edit" };
        readDb.Tracks.Add(unrelated);
        var error = await Assert.ThrowsAsync<PlatformApiException>(() => fixture.Provider(readDb).ReadAsync(account, "playlist", default));
        Assert.Equal("remote_changed", error.Code);
        Assert.True(interceptor.Recorded);
        await using (var verify = scope.NewContext())
        {
            await AssertReplacementReadIsolationAsync(scope, verify, service);
            Assert.Single(await verify.PlaylistExportAttempts.ToArrayAsync());
        }
        await readDb.SaveChangesAsync();
        await using var persisted = scope.NewContext();
        Assert.True(await persisted.Tracks.AnyAsync(track => track.Id == unrelated.Id));
        Assert.False(await persisted.TrackObservations.AnyAsync(row => row.SourceType == service));
        Assert.Empty(await persisted.TrackSourceIds.ToArrayAsync());
        Assert.Empty(await persisted.TrackMatchQueueItems.ToArrayAsync());
    }

    private sealed class AttemptDuringReadSave(string service, Func<Task> record) : SaveChangesInterceptor
    {
        public bool Recorded { get; private set; }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (!Recorded && data.Context!.ChangeTracker.Entries<TrackObservation>().Any(entry => entry.Entity.SourceType == service
                && entry.Entity.ExternalId == "chosen-id"))
            {
                Recorded = true;
                await record();
            }
            return result;
        }
    }

    [Theory]
    [InlineData("spotify", false)]
    [InlineData("youtube", false)]
    public Task DurableExport_LegacyFallbackRejectsRetainedRemoteBeforeIngestion(string service, bool postgres)
        => CheckDurableExportLegacyAsync(service, postgres);

    [PostgresTheory]
    [InlineData("spotify", true)]
    [InlineData("youtube", true)]
    public Task DurableExport_PostgresLegacyFallbackRejectsRetainedRemoteBeforeIngestion(string service, bool postgres)
        => CheckDurableExportLegacyAsync(service, postgres);

    private static async Task CheckDurableExportLegacyAsync(string service, bool postgres)
    {
        await using var fixture = await ReplacementReadFixture.CreateAsync(service, postgres);
        var scope = fixture.Scope;
        await fixture.SyncAsync(scope.Playlist.Id);
        await using (var deleteDb = scope.NewContext())
            await fixture.Lifecycle(deleteDb).UnlinkAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, false, true, default);
        await using var caller = scope.NewContext();
        var pending = new Track { Id = Guid.NewGuid(), SearchTitle = "Caller pending legacy edit" };
        caller.Tracks.Add(pending);
        var account = new PlatformAccountContext(scope.UserId, scope.Link.ConnectedServiceAccountId, "account");
        // No HTTP service is supplied: the retained scope must reject first.
        var read = service == "spotify"
            ? new SpotifyPlaylistSyncService(caller, null!, null!, null!, TimeProvider.System,
                NullLogger<SpotifyPlaylistSyncService>.Instance).SyncPlaylistAsync(account, "playlist", default)
            : new YouTubePlaylistSyncService(caller, null!, new TrackMatchQueue(caller, TimeProvider.System), null!,
                NullLogger<YouTubePlaylistSyncService>.Instance).SyncYouTubePlaylistAsync(account, "playlist", default);
        var error = await Assert.ThrowsAsync<PlatformApiException>(() => read);
        Assert.Equal("outbound_mapping", error.Code);
        await using (var verify = scope.NewContext()) await AssertReplacementReadIsolationAsync(scope, verify, service);
        await caller.SaveChangesAsync();
        await using var persisted = scope.NewContext();
        Assert.True(await persisted.Tracks.AnyAsync(track => track.Id == pending.Id));
        Assert.False(await persisted.TrackObservations.AnyAsync(row => row.SourceType == service));
        Assert.Empty(await persisted.TrackSourceIds.ToArrayAsync());
    }

    private sealed class DurableExportWriter(IPlaylistWriter inner, Func<IReadOnlyList<string>, Task> before, Action after) : IPlaylistWriter
    {
        public string PlatformId => inner.PlatformId;
        public Task ValidateCreationAsync(PlatformAccountContext account, CancellationToken ct) => inner.ValidateCreationAsync(account, ct);
        public Task<string> CreatePlaylistAsync(PlatformAccountContext account, string name, CancellationToken ct) => inner.CreatePlaylistAsync(account, name, ct);
        public Task ValidateDestinationAsync(PlatformAccountContext account, string id, CancellationToken ct) => inner.ValidateDestinationAsync(account, id, ct);
        public async Task ReconcileAsync(PlatformAccountContext account, string id, IReadOnlyList<string> ids, CancellationToken ct)
        {
            await before(ids);
            await inner.ReconcileAsync(account, id, ids, ct);
            after();
        }
    }

    [Theory]
    [InlineData("spotify", false)]
    [InlineData("youtube", false)]
    public Task DurableExport_UnchangedApprovedExportRetainsProvenanceAfterDeletion(string service, bool postgres)
        => CheckUnchangedApprovedExportAsync(service, postgres);

    [PostgresTheory]
    [InlineData("spotify", true)]
    [InlineData("youtube", true)]
    public Task DurableExport_PostgresUnchangedApprovedExportRetainsProvenanceAfterDeletion(string service, bool postgres)
        => CheckUnchangedApprovedExportAsync(service, postgres);

    private static async Task CheckUnchangedApprovedExportAsync(string service, bool postgres)
    {
        await using var fixture = await ReplacementReadFixture.CreateAsync(service, postgres, exportYouTube: false);
        var scope = fixture.Scope;
        fixture.RemoteIds = ["chosen-id"];
        var mutated = false;
        var writer = new DurableExportWriter(fixture.Writer, _ => { mutated = true; return Task.CompletedTask; }, () => { });
        await using (var syncDb = scope.NewContext())
            await new PlaylistSyncCoordinator(syncDb, [fixture.Provider(syncDb)], [writer], TimeProvider.System,
                NullLogger<PlaylistSyncCoordinator>.Instance).RunAsync(scope.UserId, scope.Playlist.Id, service, default);
        Assert.False(mutated);
        await using (var deleteDb = scope.NewContext())
        {
            Assert.NotNull(Assert.Single(PlaylistSyncBaseline.Parse((await deleteDb.ServicePlaylistMappings.SingleAsync()).BaselineJson)!.Items).DestinationBinding);
            await fixture.Lifecycle(deleteDb).UnlinkAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, false, true, default);
        }
        await using var readDb = scope.NewContext();
        PlatformApiException? rejected = null;
        try { await fixture.Provider(readDb).ReadAsync(new(scope.UserId, scope.Link.ConnectedServiceAccountId, "account"), "playlist", default); }
        catch (PlatformApiException error) { rejected = error; }
        await readDb.SaveChangesAsync();
        await using var persisted = scope.NewContext();
        await AssertReplacementReadIsolationAsync(scope, persisted, service);
        Assert.Equal("remote_changed", rejected?.Code);
        Assert.Single(await persisted.PlaylistExportAttempts.ToArrayAsync());
    }
}
