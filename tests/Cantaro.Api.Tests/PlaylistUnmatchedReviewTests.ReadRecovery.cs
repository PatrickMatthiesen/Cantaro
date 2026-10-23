using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed partial class PlaylistUnmatchedReviewTests
{
    [Theory]
    [InlineData("cantaro")]
    [InlineData("platform")]
    public Task ScopedDestination_OrderResolutionRetainsCanonicalEntryAndSource(string use)
        => ResolveScopedOrderAsync(use);

    private static async Task ResolveScopedOrderAsync(string use, bool postgres = false)
    {
        await using var scope = await Scope.CreateAsync("youtube", relational: !postgres, postgres: postgres);
        var (provider, writer) = await ExportScopedDestinationAsync(scope, manual: true);
        var c = scope.AddEntry("Track C", "direct-c");
        var d = scope.AddEntry("Track D", "direct-d");
        d.Position = 2;
        await scope.Db.SaveChangesAsync();
        await RunDestinationSyncAsync(scope, provider, writer);
        Assert.Equal(["chosen-id", "direct-c", "direct-d"], provider.RemoteIds);
        var entries = await scope.Db.PlaylistEntries.OrderBy(item => item.Position).ToArrayAsync();
        foreach (var entry in entries) entry.Position += 10;
        await scope.Db.SaveChangesAsync();
        foreach (var entry in entries) entry.Position = entry.Id == c.Id ? 0 : entry.Id == scope.Entry.Id ? 1 : 2;
        await scope.Db.SaveChangesAsync();
        provider.RemoteIds = ["chosen-id", "direct-d", "direct-c"];
        await RunDestinationSyncAsync(scope, provider, writer);
        Assert.Equal("order_conflict", (await scope.Db.ServicePlaylistMappings.SingleAsync()).LastSyncStatus);
        scope.Db.ChangeTracker.Clear();
        await new PlaylistSyncCoordinator(scope.Db, [provider], [writer], TimeProvider.System,
            NullLogger<PlaylistSyncCoordinator>.Instance)
            .ResolveOrderAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, use, default);
        var resolved = await scope.Db.PlaylistEntries.AsNoTracking().OrderBy(item => item.Position).ToArrayAsync();
        Assert.Equal(use == "platform" ? new[] { scope.Entry.Id, d.Id, c.Id } : [c.Id, scope.Entry.Id, d.Id],
            resolved.Select(item => item.Id));
        var original = Assert.Single(resolved, item => item.Id == scope.Entry.Id);
        Assert.Equal((scope.Entry.TrackId, scope.Entry.TrackObservationId), (original.TrackId, original.TrackObservationId));
        await RunDestinationSyncAsync(scope, provider, writer);
        Assert.Equal("success", (await scope.Db.ServicePlaylistMappings.SingleAsync()).LastSyncStatus);
        Assert.Equal(use == "platform" ? new[] { "chosen-id", "direct-d", "direct-c" } : ["direct-c", "chosen-id", "direct-d"],
            provider.RemoteIds);
        await AssertDestinationRemainsScopedAsync(scope);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task ScopedDestination_InterruptedWriteRecoversOnlyPresentMembership(bool present)
        => RecoverScopedWriteAsync(present);

    private static async Task RecoverScopedWriteAsync(bool present, bool postgres = false)
    {
        await using var scope = await Scope.CreateAsync("youtube", relational: !postgres, postgres: postgres);
        var candidate = new TrackMatchSearchCandidate { CandidateSource = "youtube", ExternalId = "chosen-id",
            Title = "Everything Goes On", Artist = "Porter Robinson", DurationSeconds = 160 };
        scope.Db.ChangeTracker.Clear();
        await scope.Review.ConfirmAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id,
            scope.Entry.Id, scope.Token(candidate), default);
        var link = await scope.Db.ServicePlaylistMappings.SingleAsync();
        link.BaselineJson = new PlaylistSyncBaseline([]).Serialize();
        link.BaselineName = scope.Playlist.Name;
        link.SyncMode = present ? "bidirectional" : "from_cantaro";
        await scope.Db.SaveChangesAsync();
        var provider = new ReviewResolverProvider(scope.Db, scope.Playlist.Name, "youtube");
        var writer = new ReviewWriter(provider);
        var interrupted = new InterruptAfterWrite(writer);
        await new PlaylistSyncCoordinator(scope.Db, [provider], [interrupted], TimeProvider.System,
            NullLogger<PlaylistSyncCoordinator>.Instance).RunAsync(scope.UserId, scope.Playlist.Id, "youtube", default);
        Assert.Equal("error", link.LastSyncStatus);
        Assert.NotNull(link.PendingWriteJson);
        Assert.Equal(["chosen-id"], provider.RemoteIds);
        Assert.True(TrackArtworkUpdater.FillMissingCanonicalThumbnail(
            await scope.Db.Tracks.SingleAsync(), "https://example.invalid/recovered.jpg"));
        await scope.Db.SaveChangesAsync();
        if (!present) provider.RemoteIds = [];
        for (var pass = 0; pass < 2; pass++)
        {
            await RunDestinationSyncAsync(scope, provider, writer);
            var saved = await scope.Db.PlaylistEntries.AsNoTracking().SingleAsync();
            Assert.Equal((scope.Entry.Id, scope.Entry.TrackId, scope.Entry.TrackObservationId),
                (saved.Id, saved.TrackId, saved.TrackObservationId));
            var savedLink = await scope.Db.ServicePlaylistMappings.SingleAsync();
            Assert.Null(savedLink.PendingWriteJson);
            Assert.Equal(present ? "success" : "partial", savedLink.LastSyncStatus);
            Assert.Equal(present ? new[] { "chosen-id" } : [], provider.RemoteIds);
        }
        Assert.Single(writer.Writes);
        if (present) await AssertDestinationRemainsScopedAsync(scope);
        else Assert.True(provider.Searches >= 1);
    }

    [Fact]
    public Task ScopedDestination_NewCounterpartAcknowledgesFirstExportBeforeMetadataChanges()
        => RecoverCreatedCounterpartAsync();

    private static async Task RecoverCreatedCounterpartAsync(bool postgres = false)
    {
        await using var scope = await Scope.CreateAsync("youtube", relational: !postgres, postgres: postgres);
        scope.Db.ServicePlaylistMappings.Remove(scope.Link);
        scope.Playlist.SyncEnabled = true;
        await scope.Db.SaveChangesAsync();
        var provider = new ReviewResolverProvider(scope.Db, scope.Playlist.Name, "youtube")
        {
            SearchResults = [new TrackMatchSearchCandidate { CandidateSource = "youtube", ExternalId = "chosen-id",
                Title = "Everything Goes On", Artist = "Porter Robinson", ArtistCredits = ["Porter Robinson"], DurationSeconds = 160 }]
        };
        var writer = new ReviewWriter(provider);
        scope.Db.ChangeTracker.Clear();
        await new PlaylistSyncCoordinator(scope.Db, [provider], [writer], TimeProvider.System,
            NullLogger<PlaylistSyncCoordinator>.Instance).RunAsync(scope.UserId, scope.Playlist.Id, null, default);
        var link = await scope.Db.ServicePlaylistMappings.SingleAsync();
        var bound = Assert.Single(PlaylistSyncBaseline.Parse(link.BaselineJson)!.Items);
        Assert.Equal(scope.Entry.TrackId, bound.TrackId);
        Assert.Equal(scope.Entry.Id, bound.DestinationBinding!.EntryId);
        provider.SearchResults = [];
        Assert.True(TrackArtworkUpdater.FillMissingCanonicalThumbnail(
            await scope.Db.Tracks.SingleAsync(), "https://example.invalid/counterpart.jpg"));
        await scope.Db.SaveChangesAsync();
        for (var pass = 0; pass < 2; pass++)
        {
            await RunDestinationSyncAsync(scope, provider, writer);
            var saved = await scope.Db.PlaylistEntries.AsNoTracking().SingleAsync();
            Assert.Equal((scope.Entry.Id, scope.Entry.TrackId, scope.Entry.TrackObservationId),
                (saved.Id, saved.TrackId, saved.TrackObservationId));
            Assert.Equal(["chosen-id"], provider.RemoteIds);
            Assert.Equal("success", (await scope.Db.ServicePlaylistMappings.SingleAsync()).LastSyncStatus);
        }
        Assert.Single(writer.Writes);
        await AssertDestinationRemainsScopedAsync(scope);
    }

    [PostgresFact]
    public async Task ScopedDestination_PostgresOrderInterruptedAndCreatedReadRecovery()
    {
        foreach (var use in new[] { "cantaro", "platform" }) await ResolveScopedOrderAsync(use, postgres: true);
        foreach (var present in new[] { true, false }) await RecoverScopedWriteAsync(present, postgres: true);
        await RecoverCreatedCounterpartAsync(postgres: true);
    }

    private sealed class InterruptAfterWrite(IPlaylistWriter inner) : IPlaylistWriter
    {
        public string PlatformId => inner.PlatformId;
        public Task ValidateCreationAsync(PlatformAccountContext account, CancellationToken ct) => inner.ValidateCreationAsync(account, ct);
        public Task<string> CreatePlaylistAsync(PlatformAccountContext account, string name, CancellationToken ct) => inner.CreatePlaylistAsync(account, name, ct);
        public Task ValidateDestinationAsync(PlatformAccountContext account, string id, CancellationToken ct) => inner.ValidateDestinationAsync(account, id, ct);
        public async Task ReconcileAsync(PlatformAccountContext account, string id, IReadOnlyList<string> ids, CancellationToken ct)
        {
            await inner.ReconcileAsync(account, id, ids, ct);
            throw new PlatformApiException("provider_error", "Response lost after provider mutation", 503);
        }
    }
}
