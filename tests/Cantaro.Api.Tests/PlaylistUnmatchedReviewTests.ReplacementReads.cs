using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed partial class PlaylistUnmatchedReviewTests
{
    public static IEnumerable<object[]> CrossLinkReadCases(bool postgres)
    {
        foreach (var service in new[] { "spotify", "youtube" })
        foreach (var oldFirst in new[] { true, false })
        foreach (var sameLocal in new[] { true, false })
            yield return [service, oldFirst, sameLocal, postgres];
    }

    [Theory]
    [MemberData(nameof(CrossLinkReadCases), false)]
    public Task CrossLinkRead_ProductionReattachScopesMembershipWithoutCopyingManualPermission(
        string service, bool oldFirst, bool sameLocal, bool postgres)
        => CheckCrossLinkReadAsync(service, oldFirst, sameLocal, postgres);

    [PostgresTheory]
    [MemberData(nameof(CrossLinkReadCases), true)]
    public Task CrossLinkRead_PostgresProductionReattachScopesMembershipWithoutCopyingManualPermission(
        string service, bool oldFirst, bool sameLocal, bool postgres)
        => CheckCrossLinkReadAsync(service, oldFirst, sameLocal, postgres);

    private static async Task CheckCrossLinkReadAsync(
        string service, bool oldFirst, bool sameLocal, bool postgres)
    {
        await using var fixture = await ReplacementReadFixture.CreateAsync(service, postgres);
        var scope = fixture.Scope;
        var oldId = oldFirst ? Guid.Empty : Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var previousId = scope.Link.Id;
        var originalLink = await scope.Db.ServicePlaylistMappings.AsNoTracking().SingleAsync();
        var progress = originalLink.MatchingProgressJson!;
        await scope.Db.ServicePlaylistMappings.ExecuteUpdateAsync(setters => setters.SetProperty(link => link.Id, oldId)
            .SetProperty(link => link.MatchingProgressJson, progress.Replace(previousId.ToString(), oldId.ToString()))
            .SetProperty(link => link.BaselineJson, originalLink.BaselineJson!.Replace(previousId.ToString(), oldId.ToString())));
        scope.Link.Id = oldId;
        scope.Db.ChangeTracker.Clear();
        await fixture.SyncAsync(scope.Playlist.Id);
        // Exercise occurrence assignment as well as mapping ordering.
        (await scope.Db.Playlists.SingleAsync()).AllowDuplicateTracks = true;
        await scope.Db.SaveChangesAsync();
        fixture.RemoteIds = ["chosen-id", "chosen-id"];
        await fixture.SyncAsync(scope.Playlist.Id);
        await using (var unlinkDb = scope.NewContext())
            await fixture.Lifecycle(unlinkDb).UnlinkAsync(scope.UserId, scope.Playlist.Id, oldId, false, false, default);

        var target = scope.Playlist.Id;
        if (!sameLocal)
        {
            target = Guid.NewGuid();
            await using var createDb = scope.NewContext();
            createDb.Playlists.Add(new Playlist { Id = target, UserId = scope.UserId, Name = "Moved copy", AllowDuplicateTracks = true });
            await createDb.SaveChangesAsync();
        }
        await using (var previewDb = scope.NewContext())
        {
            var lifecycle = fixture.Lifecycle(previewDb);
            var preview = await lifecycle.PreviewAttachAsync(scope.UserId, target, service, "playlist", default);
            if (!sameLocal)
            {
                var reads = 0;
                fixture.BeforeRead = () => { if (++reads == 2) throw new HttpRequestException("Initialization interrupted after its preview"); };
                await Assert.ThrowsAsync<HttpRequestException>(() => lifecycle.AttachAsync(scope.UserId, target, service,
                    "playlist", "bidirectional", "combine", preview.PreviewToken, default));
                fixture.BeforeRead = null;
            }
            else await lifecycle.AttachAsync(scope.UserId, target, service, "playlist", "bidirectional", "combine", preview.PreviewToken, default);
        }
        if (!sameLocal)
        {
            await using var retryDb = scope.NewContext();
            var pending = await retryDb.ServicePlaylistMappings.SingleAsync(link => link.PlaylistId == target);
            Assert.Equal("initialization_pending", pending.LastSyncStatus);
            Assert.Null(pending.BaselineJson);
            var lifecycle = fixture.Lifecycle(retryDb);
            var retry = await lifecycle.PreviewPendingInitializationAsync(scope.UserId, target, pending.Id, default);
            await lifecycle.RetryPendingInitializationAsync(scope.UserId, target, pending.Id, retry.PreviewToken, default);
        }
        await using (var verify = scope.NewContext())
        {
            await AssertReplacementReadIsolationAsync(scope, verify, service);
            var current = await verify.ServicePlaylistMappings.SingleAsync(link => link.PlaylistId == target && link.State == "active");
            if (sameLocal) Assert.Equal(oldId, current.Id);
            else Assert.True(oldFirst ? oldId.CompareTo(current.Id) < 0 : oldId.CompareTo(current.Id) > 0);
            await AssertOwnedReadReceiptsAsync(verify, current, sameLocal);
        }
        for (var pass = 0; pass < 3; pass++)
        {
            await fixture.SyncAsync(target);
            await using var verify = scope.NewContext();
            var current = await verify.ServicePlaylistMappings.SingleAsync(link => link.PlaylistId == target && link.State == "active");
            Assert.True(current.LastSyncStatus == "success", current.LastError);
            await AssertOwnedReadReceiptsAsync(verify, current, sameLocal);
            await AssertReplacementReadIsolationAsync(scope, verify, service);
            var entries = await verify.PlaylistEntries.Where(entry => entry.PlaylistId == target).OrderBy(entry => entry.Position).ToArrayAsync();
            Assert.Equal(2, entries.Length);
            Assert.Equal(new[] { 0, 1 }, entries.Select(entry => entry.Position));
            Assert.All(entries, entry => Assert.Equal((scope.Entry.TrackId, scope.Entry.TrackObservationId), (entry.TrackId, entry.TrackObservationId)));
            await fixture.Lifecycle(verify).PreviewAttachAsync(scope.UserId, target, service, "playlist", default);
            await verify.SaveChangesAsync();
        }
        if (!sameLocal)
        {
            // The observed membership never authorizes recreating an absent ID.
            fixture.RemoteIds = [];
            await using (var configure = scope.NewContext())
            {
                var configuredLink = await configure.ServicePlaylistMappings.SingleAsync(link => link.PlaylistId == target);
                configuredLink.SyncMode = "from_cantaro";
                await configure.SaveChangesAsync();
            }
            await fixture.SyncAsync(target);
            Assert.Empty(fixture.RemoteIds);
            await using var verify = scope.NewContext();
            await AssertReplacementReadIsolationAsync(scope, verify, service);
            var current = await verify.ServicePlaylistMappings.SingleAsync(link => link.PlaylistId == target);
            Assert.Equal("partial", current.LastSyncStatus);
            Assert.Equal(2, await verify.PlaylistEntries.CountAsync(entry => entry.PlaylistId == target));
            Assert.DoesNotContain(PlaylistMatchingProgress.Read(current.MatchingProgressJson).Values, proof => proof.ExternalId == "chosen-id");
        }
    }

    public static IEnumerable<object[]> ReplacementReadCases(bool postgres)
    {
        foreach (var service in new[] { "spotify", "youtube" })
        foreach (var pending in new[] { false, true })
        foreach (var create in new[] { false, true })
            yield return [service, pending, create, postgres];
    }

    [Theory]
    [MemberData(nameof(ReplacementReadCases), false)]
    public Task ReplacementRead_ProductionAttachAndCreateRetainOldRemoteProvenance(
        string service, bool pending, bool create, bool postgres)
        => CheckReplacementReadAsync(service, pending, create, postgres);

    [PostgresTheory]
    [MemberData(nameof(ReplacementReadCases), true)]
    public Task ReplacementRead_PostgresProductionAttachAndCreateRetainOldRemoteProvenance(
        string service, bool pending, bool create, bool postgres)
        => CheckReplacementReadAsync(service, pending, create, postgres);

    private static async Task CheckReplacementReadAsync(
        string service, bool pending, bool create, bool postgres)
    {
        await using var fixture = await ReplacementReadFixture.CreateAsync(service, postgres);
        var scope = fixture.Scope;
        await fixture.SyncAsync(scope.Playlist.Id);
        await using (var unlinkDb = scope.NewContext())
        {
            var old = await unlinkDb.ServicePlaylistMappings.SingleAsync();
            if (pending)
            {
                old.PendingWriteJson = old.BaselineJson;
                old.BaselineJson = new PlaylistSyncBaseline([]).Serialize();
                await unlinkDb.SaveChangesAsync();
            }
            await fixture.Lifecycle(unlinkDb).UnlinkAsync(scope.UserId, scope.Playlist.Id, old.Id, false, false, default);
        }
        fixture.RemoteIds = [];
        fixture.CreatedPlaylistId = "other-remote";
        await using (var attachDb = scope.NewContext())
        {
            if (create) await fixture.Coordinator(attachDb).CreateLinkAsync(scope.UserId, scope.Playlist.Id, service, _ => Task.CompletedTask, default);
            else
            {
                var lifecycle = fixture.Lifecycle(attachDb);
                var preview = await lifecycle.PreviewAttachAsync(scope.UserId, scope.Playlist.Id, service, "other-remote", default);
                await lifecycle.AttachAsync(scope.UserId, scope.Playlist.Id, service, "other-remote", "bidirectional", "combine", preview.PreviewToken, default);
            }
        }
        fixture.RemoteIds = ["chosen-id"];
        for (var pass = 0; pass < 3; pass++)
        {
            await using var readDb = scope.NewContext();
            await fixture.Lifecycle(readDb).PreviewAttachAsync(scope.UserId, scope.Playlist.Id, service, "playlist", default);
            await readDb.SaveChangesAsync();
            await using var verify = scope.NewContext();
            await AssertReplacementReadIsolationAsync(scope, verify, service);
            var historical = await verify.ServicePlaylistMappings.SingleAsync(link => link.Id == scope.Link.Id);
            Assert.Equal("unlinked", historical.State);
            Assert.NotNull(pending ? historical.PendingWriteJson : historical.BaselineJson);
            Assert.Equal("other-remote", (await verify.ServicePlaylistMappings.SingleAsync(link => link.State == "active")).ServicePlaylistId);
        }
        await using (var reattachDb = scope.NewContext())
        {
            var replacement = await reattachDb.ServicePlaylistMappings.SingleAsync(link => link.State == "active");
            var lifecycle = fixture.Lifecycle(reattachDb);
            await lifecycle.UnlinkAsync(scope.UserId, scope.Playlist.Id, replacement.Id, false, false, default);
            var preview = await lifecycle.PreviewAttachAsync(scope.UserId, scope.Playlist.Id, service, "playlist", default);
            await lifecycle.AttachAsync(scope.UserId, scope.Playlist.Id, service, "playlist", "bidirectional", "combine", preview.PreviewToken, default);
        }
        for (var pass = 0; pass < 2; pass++) await fixture.SyncAsync(scope.Playlist.Id);
        await using var final = scope.NewContext();
        await AssertReplacementReadIsolationAsync(scope, final, service);
        Assert.Equal("success", (await final.ServicePlaylistMappings.SingleAsync(link => link.Id == scope.Link.Id)).LastSyncStatus);
        Assert.Equal("unlinked", (await final.ServicePlaylistMappings.SingleAsync(link => link.ServicePlaylistId == "other-remote")).State);
    }

    [Theory]
    [InlineData("spotify")]
    [InlineData("youtube")]
    public async Task CrossLinkRead_InvalidHistoricalReceiptFailsClosedBeforeNewMappingExists(string service)
    {
        await using var fixture = await ReplacementReadFixture.CreateAsync(service, false);
        var scope = fixture.Scope;
        await fixture.SyncAsync(scope.Playlist.Id);
        await using var editDb = scope.NewContext();
        await fixture.Lifecycle(editDb).UnlinkAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, false, false, default);
        var old = await editDb.ServicePlaylistMappings.SingleAsync();
        old.BaselineJson = old.BaselineJson!.Replace("\"ExternalAccountId\":\"account\"", "\"ExternalAccountId\":\"foreign-account\"");
        old.MatchingProgressJson = null;
        var target = new Playlist { Id = Guid.NewGuid(), UserId = scope.UserId, Name = "Other" };
        editDb.Playlists.Add(target);
        await editDb.SaveChangesAsync();
        await using var readDb = scope.NewContext();
        var error = await Assert.ThrowsAsync<PlatformApiException>(() => fixture.Lifecycle(readDb)
            .PreviewAttachAsync(scope.UserId, target.Id, service, "playlist", default));
        Assert.Equal("remote_changed", error.Code);
        await readDb.SaveChangesAsync();
        await using var verify = scope.NewContext();
        Assert.False(await verify.ServicePlaylistMappings.AnyAsync(link => link.PlaylistId == target.Id));
        await AssertReplacementReadIsolationAsync(scope, verify, service);
    }

    [Theory]
    [InlineData("spotify", true)]
    [InlineData("spotify", false)]
    [InlineData("youtube", true)]
    [InlineData("youtube", false)]
    public async Task CrossLinkRead_ConflictingHistoricalRecordingsRejectInEitherMappingOrder(string service, bool secondFirst)
    {
        await using var fixture = await ReplacementReadFixture.CreateAsync(service, false);
        var scope = fixture.Scope;
        await fixture.SyncAsync(scope.Playlist.Id);
        var target = new Playlist { Id = Guid.NewGuid(), UserId = scope.UserId, Name = "Other recording" };
        await using (var setup = scope.NewContext())
        {
            await fixture.Lifecycle(setup).UnlinkAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, false, false, default);
            var track = new Track { Id = Guid.NewGuid(), SearchTitle = "Different recording" };
            var entry = new PlaylistEntry { Id = Guid.NewGuid(), PlaylistId = target.Id, TrackId = track.Id };
            var old = new ServicePlaylistMapping
            {
                Id = secondFirst ? Guid.Empty : Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                PlaylistId = target.Id, UserId = scope.UserId, ConnectedServiceAccountId = 51, ExternalAccountId = "account",
                Service = service, ServicePlaylistId = "playlist", State = "unlinked", SyncMode = "bidirectional"
            };
            old.BaselineJson = new PlaylistSyncBaseline([new(track.Id, null, "chosen-id", service,
                PlaylistDestinationBinding.From(old, entry))]).Serialize();
            setup.AddRange(target, track, entry, old);
            await setup.SaveChangesAsync();
        }
        await using (var readDb = scope.NewContext())
        {
            var error = await Assert.ThrowsAsync<PlatformApiException>(() => fixture.Lifecycle(readDb)
                .PreviewAttachAsync(scope.UserId, target.Id, service, "playlist", default));
            Assert.Equal("remote_changed", error.Code);
            await readDb.SaveChangesAsync();
        }
        await using var verify = scope.NewContext();
        Assert.Empty(await verify.TrackSourceIds.ToArrayAsync());
        Assert.Empty(await verify.TrackArtistCredits.ToArrayAsync());
        Assert.Empty(await verify.TrackMatchQueueItems.ToArrayAsync());
        Assert.False(await verify.TrackObservations.AnyAsync(observation => observation.SourceType == service));
        Assert.All(await verify.Tracks.ToArrayAsync(), track => Assert.Null(track.Isrc));
        Assert.All(await verify.ServicePlaylistMappings.ToArrayAsync(), link => Assert.Equal("unlinked", link.State));
    }

    [Theory]
    [InlineData("spotify")]
    [InlineData("youtube")]
    public async Task ReplacementRead_IndependentRemoteImportRemainsValidAfterUnlink(string service)
    {
        await using var fixture = await ReplacementReadFixture.CreateAsync(service, false);
        var scope = fixture.Scope;
        await fixture.SyncAsync(scope.Playlist.Id);
        await using (var unlinkDb = scope.NewContext())
            await fixture.Lifecycle(unlinkDb).UnlinkAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, false, false, default);
        await using (var importDb = scope.NewContext())
        {
            var id = await fixture.Coordinator(importDb).ImportAsync(new(scope.UserId, 51, "account"), service, "independent-playlist", default);
            Assert.NotEqual(scope.Playlist.Id, id);
        }
        await using (var readDb = scope.NewContext())
        {
            var snapshot = await fixture.Provider(readDb).ReadAsync(new(scope.UserId, 51, "account"), "playlist", default);
            var item = Assert.Single(snapshot.Tracks);
            Assert.True(item.TrackId is not null || item.ObservationId is not null);
            await fixture.Lifecycle(readDb).PreviewAttachAsync(scope.UserId, scope.Playlist.Id, service, "playlist", default);
            await readDb.SaveChangesAsync();
        }
        await using var verify = scope.NewContext();
        Assert.True(await verify.TrackObservations.AnyAsync(observation => observation.SourceType == service && observation.ExternalId == "chosen-id"));
        if (service == "spotify") Assert.True(await verify.TrackSourceIds.AnyAsync(source => source.SourceType == service && source.ExternalId == "chosen-id"));
        else Assert.True(await verify.TrackMatchQueueItems.AnyAsync());
        Assert.Equal("unlinked", (await verify.ServicePlaylistMappings.SingleAsync(link => link.Id == scope.Link.Id)).State);
    }

    [Theory]
    [InlineData("spotify")]
    [InlineData("youtube")]
    public async Task ReplacementRead_AnotherUsersIndependentReadDoesNotBorrowHistoricalReceipts(string service)
    {
        await using var fixture = await ReplacementReadFixture.CreateAsync(service, false);
        var scope = fixture.Scope;
        await fixture.SyncAsync(scope.Playlist.Id);
        const int otherUserId = 5802;
        await using (var setup = scope.NewContext())
        {
            // Give the real independently imported recording a distinct ISRC.
            (await setup.Tracks.SingleAsync()).Isrc = "USABC2400002";
            var original = await setup.ConnectedServiceAccounts.SingleAsync();
            setup.Users.Add(TestUserFactory.Create(otherUserId, "replacement-other@example.com"));
            setup.ConnectedServiceAccounts.Add(new ConnectedServiceAccount
            {
                Id = 52, UserId = otherUserId, Service = service, ExternalAccountId = "other-account", ConnectionState = "connected",
                EncryptedAccessToken = original.EncryptedAccessToken, EncryptedRefreshToken = original.EncryptedRefreshToken,
                TokenExpiresAt = original.TokenExpiresAt, Scopes = original.Scopes
            });
            await setup.SaveChangesAsync();
        }
        fixture.ProfileId = "other-account";
        await using (var readDb = scope.NewContext())
        {
            var snapshot = await fixture.Provider(readDb).ReadAsync(new(otherUserId, 52, "other-account"), "playlist", default);
            var item = Assert.Single(snapshot.Tracks);
            Assert.Null(item.DestinationBinding);
            Assert.NotEqual(scope.Entry.TrackId, item.TrackId);
            await readDb.SaveChangesAsync();
        }
        await using var verify = scope.NewContext();
        Assert.Equal("USABC2400002", (await verify.Tracks.SingleAsync(track => track.Id == scope.Entry.TrackId)).Isrc);
        Assert.False(await verify.TrackArtistCredits.AnyAsync(credit => credit.TrackId == scope.Entry.TrackId));
        Assert.False(await verify.TrackSourceIds.AnyAsync(source => source.TrackId == scope.Entry.TrackId && source.SourceType == service));
        Assert.True(await verify.TrackObservations.AnyAsync(observation => observation.SourceType == service && observation.ExternalId == "chosen-id"));
    }

    [Theory]
    [InlineData("spotify", "observation")]
    [InlineData("youtube", "observation")]
    [InlineData("spotify", "recording")]
    [InlineData("youtube", "recording")]
    public Task CrossLinkRead_EvidenceChangeAfterProviderReadRejectsNewMembership(string service, string change)
        => CheckChangedReadEvidenceAsync(service, change, false);

    [PostgresTheory]
    [InlineData("spotify", "observation")]
    [InlineData("youtube", "observation")]
    [InlineData("spotify", "recording")]
    [InlineData("youtube", "recording")]
    public Task CrossLinkRead_PostgresEvidenceChangeAfterProviderReadRejectsNewMembership(string service, string change)
        => CheckChangedReadEvidenceAsync(service, change, true);

    private static async Task CheckChangedReadEvidenceAsync(string service, string change, bool postgres)
    {
        await using var fixture = await ReplacementReadFixture.CreateAsync(service, postgres);
        var scope = fixture.Scope;
        await fixture.SyncAsync(scope.Playlist.Id);
        var target = new Playlist { Id = Guid.NewGuid(), UserId = scope.UserId, Name = "Moved copy" };
        await using (var setup = scope.NewContext())
        {
            await fixture.Lifecycle(setup).UnlinkAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, false, false, default);
            setup.Playlists.Add(target);
            await setup.SaveChangesAsync();
        }
        await using (var readDb = scope.NewContext())
        {
            var changed = false;
            var provider = new AfterLifecycleReadProvider(fixture.Provider(readDb), async read =>
            {
                if (read != 3) return; // After preview, attach revalidation and the initialization read.
                await using var update = scope.NewContext();
                var replacement = new Track { Id = Guid.NewGuid(), SearchTitle = "Reassigned source" };
                update.Tracks.Add(replacement);
                await update.SaveChangesAsync();
                if (change == "observation")
                    await update.TrackObservations.Where(observation => observation.Id == scope.Entry.TrackObservationId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(observation => observation.TrackId, replacement.Id));
                else
                {
                    update.TrackSourceIds.Add(new TrackSourceId { TrackId = replacement.Id, SourceType = service, ExternalId = "chosen-id" });
                    await update.SaveChangesAsync();
                }
                changed = true;
            });
            var lifecycle = fixture.Lifecycle(readDb, provider);
            var preview = await lifecycle.PreviewAttachAsync(scope.UserId, target.Id, service, "playlist", default);
            var error = await Assert.ThrowsAsync<PlatformApiException>(() => lifecycle.AttachAsync(scope.UserId, target.Id,
                service, "playlist", "bidirectional", "combine", preview.PreviewToken, default));
            Assert.True(changed);
            Assert.Equal("remote_changed", error.Code);
            await readDb.SaveChangesAsync();
        }
        await using var verify = scope.NewContext();
        var current = await verify.ServicePlaylistMappings.SingleAsync(link => link.PlaylistId == target.Id);
        Assert.Equal("paused", current.State);
        Assert.Equal("initialization_pending", current.LastSyncStatus);
        Assert.Null(current.BaselineJson);
        Assert.False(await verify.PlaylistEntries.AnyAsync(entry => entry.PlaylistId == target.Id));
        var mappings = await verify.TrackSourceIds.ToArrayAsync();
        Assert.Equal(change == "recording" ? 1 : 0, mappings.Length);
        Assert.DoesNotContain(mappings, source => source.TrackId == scope.Entry.TrackId);
        Assert.Empty(await verify.TrackMatchQueueItems.ToArrayAsync());
        Assert.Empty(await verify.TrackArtistCredits.ToArrayAsync());
        Assert.False(await verify.TrackObservations.AnyAsync(observation => observation.SourceType == service));
        Assert.All(await verify.Tracks.ToArrayAsync(), track => Assert.Null(track.Isrc));
        Assert.All(await verify.Playlists.ToArrayAsync(), playlist => Assert.Null(playlist.SyncLeaseId));
    }

    private sealed class AfterLifecycleReadProvider(IPlaylistSyncProvider provider, Func<int, Task> afterRead) : IPlaylistSyncProvider
    {
        private int _reads;
        public string PlatformId => provider.PlatformId;
        public Task<IReadOnlyList<PlaylistRemoteCatalogItem>> ListPlaylistsAsync(PlatformAccountContext account, CancellationToken ct)
            => provider.ListPlaylistsAsync(account, ct);
        public async Task<PlaylistRemoteSnapshot> ReadAsync(PlatformAccountContext account, string playlistId, CancellationToken ct)
        {
            var result = await provider.ReadAsync(account, playlistId, ct);
            await afterRead(++_reads);
            return result;
        }
        public Task<string?> ResolveAsync(PlatformAccountContext account, PlaylistEntry entry, CancellationToken ct)
            => provider.ResolveAsync(account, entry, ct);
        public Task RenameAsync(PlatformAccountContext account, string playlistId, string name, CancellationToken ct)
            => provider.RenameAsync(account, playlistId, name, ct);
        public Task DeleteAsync(PlatformAccountContext account, string playlistId, CancellationToken ct)
            => provider.DeleteAsync(account, playlistId, ct);
    }

    private static async Task AssertOwnedReadReceiptsAsync(ApplicationDbContext db, ServicePlaylistMapping link, bool sameLocal)
    {
        var entries = await db.PlaylistEntries.Where(entry => entry.PlaylistId == link.PlaylistId).ToDictionaryAsync(entry => entry.Id);
        var baseline = PlaylistSyncBaseline.Parse(link.BaselineJson)!;
        Assert.Equal(2, baseline.Items.Count);
        Assert.DoesNotContain("ReadScope", link.BaselineJson!);
        foreach (var item in baseline.Items)
        {
            var binding = Assert.IsType<PlaylistDestinationBinding>(item.DestinationBinding);
            Assert.Equal((link.UserId, link.PlaylistId, link.Id, link.ConnectedServiceAccountId, link.ExternalAccountId, link.Service, link.ServicePlaylistId),
                (binding.UserId, binding.PlaylistId, binding.MappingId, binding.ConnectedServiceAccountId, binding.ExternalAccountId, binding.Service, binding.ServicePlaylistId));
            Assert.True(entries.TryGetValue(binding.EntryId, out var entry));
            Assert.Equal((entry.TrackId, entry.TrackObservationId), (item.TrackId, item.ObservationId));
        }
        if (!sameLocal) Assert.Empty(PlaylistMatchingProgress.Read(link.MatchingProgressJson));
    }

    private static async Task AssertReplacementReadIsolationAsync(Scope scope, ApplicationDbContext verify, string service)
    {
        Assert.False(await verify.TrackSourceIds.AnyAsync(source => source.SourceType == service && source.ExternalId == "chosen-id"));
        Assert.False(await verify.TrackObservations.AnyAsync(observation => observation.SourceType == service && observation.ExternalId == "chosen-id"));
        Assert.Empty(await verify.TrackMatchQueueItems.ToArrayAsync());
        Assert.Empty(await verify.TrackArtistCredits.ToArrayAsync());
        var track = Assert.Single(await verify.Tracks.ToArrayAsync());
        Assert.Equal(scope.Entry.TrackId, track.Id);
        Assert.Null(track.Isrc);
        Assert.Null(JsonSerializer.Deserialize<TrackCanonicalMetadata>(track.CanonicalMetadata!)!.ThumbnailUrl);
        Assert.All(await verify.Playlists.ToArrayAsync(), playlist => Assert.Null(playlist.SyncLeaseId));
    }

    private sealed class ReplacementReadFixture : IAsyncDisposable
    {
        private readonly ProductionSpotifyReadScope? _spotify;
        private readonly ProductionYouTubeReadHandler? _youtube;
        private readonly IDataProtectionProvider _protection = new EphemeralDataProtectionProvider();
        private ReplacementReadFixture(Scope scope, string service, ProductionSpotifyReadScope? spotify = null, ProductionYouTubeReadHandler? youtube = null)
        {
            Scope = scope; Service = service; _spotify = spotify; _youtube = youtube;
            Writer = spotify?.Writer ?? (IPlaylistWriter)new ProductionYouTubeReadWriter(youtube!);
            if (spotify is not null) spotify.Handler.OnRead = _ => BeforeRead?.Invoke();
            else youtube!.OnItemsRead = () => BeforeRead?.Invoke();
        }
        public Scope Scope { get; }
        public string Service { get; }
        public IPlaylistWriter Writer { get; }
        public Action? BeforeRead { get; set; }
        public List<string> RemoteIds
        {
            get => _spotify?.Handler.RemoteIds ?? _youtube!.RemoteIds;
            set { if (_spotify is not null) _spotify.Handler.RemoteIds = value; else _youtube!.RemoteIds = value; }
        }
        public string ProfileId { set { if (_spotify is not null) _spotify.Handler.ProfileId = value; } }
        public string CreatedPlaylistId
        {
            set { if (_spotify is not null) _spotify.Writer.CreatedPlaylistId = value; else ((ProductionYouTubeReadWriter)Writer).CreatedPlaylistId = value; }
        }
        public IPlaylistSyncProvider Provider(ApplicationDbContext db) => _spotify is not null
            ? _spotify.ProviderFor(db) : CreateYouTubeReadProvider(db, _youtube!);
        public PlaylistSyncCoordinator Coordinator(ApplicationDbContext db)
            => new(db, [Provider(db)], [Writer], TimeProvider.System, NullLogger<PlaylistSyncCoordinator>.Instance);
        public PlaylistLinkLifecycleService Lifecycle(ApplicationDbContext db, IPlaylistSyncProvider? provider = null)
        {
            provider ??= Provider(db);
            var coordinator = new PlaylistSyncCoordinator(db, [provider], [Writer], TimeProvider.System, NullLogger<PlaylistSyncCoordinator>.Instance);
            return new(db, new PlatformRegistry([new ReadTestPlatform(db, Service)]), [provider], coordinator, _protection);
        }
        public async Task SyncAsync(Guid playlistId)
        {
            await using var db = Scope.NewContext();
            await Coordinator(db).RunAsync(Scope.UserId, playlistId, Service, default);
            await db.SaveChangesAsync();
        }
        public static async Task<ReplacementReadFixture> CreateAsync(string service, bool postgres, bool exportYouTube = true)
        {
            if (service == "spotify")
            {
                var spotify = await ProductionSpotifyReadScope.CreateAsync(postgres: postgres);
                return new(spotify.Scope, service, spotify: spotify);
            }
            var scope = await Scope.CreateAsync(service, relational: !postgres, postgres: postgres);
            var handler = new ProductionYouTubeReadHandler(scope.Playlist.Name);
            await InitializeYouTubeReadAsync(scope, handler, exportYouTube);
            return new(scope, service, youtube: handler);
        }
        public async ValueTask DisposeAsync()
        {
            var connectionString = Scope.Db.Database.IsNpgsql() ? Scope.Db.Database.GetConnectionString() : null;
            try
            {
                if (_spotify is not null) await _spotify.DisposeAsync();
                else { _youtube!.Dispose(); await Scope.DisposeAsync(); }
            }
            finally
            {
                // Each private schema has its own pool. Release its idle clients
                // when discarding the schema, even after a failed regression.
                if (connectionString is not null)
                {
                    using var connection = new Npgsql.NpgsqlConnection(connectionString);
                    Npgsql.NpgsqlConnection.ClearPool(connection);
                }
            }
        }
    }
}
