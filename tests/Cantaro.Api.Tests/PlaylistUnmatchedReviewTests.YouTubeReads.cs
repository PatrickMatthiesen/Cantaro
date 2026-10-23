using System.Net;
using System.Text;
using System.Text.Json;
using Cantaro.Api.Configuration;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Google.Apis.Http;
using Google.Apis.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed partial class PlaylistUnmatchedReviewTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProductionYouTubeRead_ScopedExportsDoNotCreateMatchingWorkButNewContentDoes(bool addNew)
    {
        await using var scope = await Scope.CreateAsync("youtube", relational: true);
        scope.Db.ChangeTracker.Clear();
        await scope.Review.ConfirmAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, scope.Entry.Id,
            scope.Token(new TrackMatchSearchCandidate { CandidateSource = "youtube", ExternalId = "chosen-id",
                Title = "Everything Goes On", Artist = "Porter Robinson", DurationSeconds = 160 }), default);
        var link = await scope.Db.ServicePlaylistMappings.SingleAsync();
        link.BaselineJson = new PlaylistSyncBaseline([]).Serialize();
        link.BaselineName = scope.Playlist.Name;
        await scope.Db.SaveChangesAsync();
        using var handler = new ProductionYouTubeReadHandler(scope.Playlist.Name);
        var youtube = new YouTubeService(new ConfigurationBuilder().Build(), scope.Db,
            new TokenEncryptionService(new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider()),
            NullLogger<YouTubeService>.Instance, (_, _) => Task.FromResult(new Google.Apis.YouTube.v3.YouTubeService(
                new BaseClientService.Initializer { ApplicationName = "Cantaro read tests", HttpClientFactory = handler })));
        var provider = new YouTubePlaylistSyncProvider(scope.Db, youtube, new TrackMatchQueue(scope.Db, TimeProvider.System),
            new TrackMatchingService(scope.Db, [], NullLogger<TrackMatchingService>.Instance),
            new PlaylistCanonicalReconciliationService(scope.Db), Options.Create(new TrackMatchingOptions()));
        var writer = new ProductionYouTubeReadWriter(handler);
        async Task SyncAsync()
        {
            scope.Db.ChangeTracker.Clear();
            await new PlaylistSyncCoordinator(scope.Db, [provider], [writer], TimeProvider.System,
                NullLogger<PlaylistSyncCoordinator>.Instance).RunAsync(scope.UserId, scope.Playlist.Id, "youtube", default);
        }
        await SyncAsync();
        TrackArtworkUpdater.FillMissingCanonicalThumbnail(await scope.Db.Tracks.SingleAsync(), "https://example.invalid/local-art.jpg");
        await scope.Db.SaveChangesAsync();
        handler.RemoteIds = addNew ? ["chosen-id", "fresh-id"] : ["chosen-id", "chosen-id"];
        for (var pass = 0; pass < 2; pass++) await SyncAsync();
        var original = await scope.Db.PlaylistEntries.AsNoTracking().SingleAsync(item => item.Id == scope.Entry.Id);
        Assert.Equal((scope.Entry.TrackId, scope.Entry.TrackObservationId), (original.TrackId, original.TrackObservationId));
        Assert.False(await scope.Db.TrackSourceIds.AnyAsync(item => item.SourceType == "youtube" && item.ExternalId == "chosen-id"));
        Assert.False(await scope.Db.TrackObservations.AnyAsync(item => item.SourceType == "youtube" && item.ExternalId == "chosen-id"));
        Assert.Empty(await scope.Db.TrackArtistCredits.ToArrayAsync());
        var synced = await scope.Db.ServicePlaylistMappings.SingleAsync();
        Assert.True(synced.LastSyncStatus == "success", synced.LastError);
        Assert.Equal(addNew ? new[] { "chosen-id", "fresh-id" } : ["chosen-id"], handler.RemoteIds);
        if (addNew)
        {
            var fresh = await scope.Db.TrackObservations.SingleAsync(item => item.ExternalId == "fresh-id");
            Assert.Null(fresh.TrackId);
            Assert.Equal(TrackMatchingStatuses.Pending, fresh.MatchStatus);
            Assert.Equal(fresh.Id, (await scope.Db.TrackMatchQueueItems.SingleAsync()).TrackObservationId);
            Assert.True(await scope.Db.PlaylistEntries.AnyAsync(item => item.TrackObservationId == fresh.Id));
        }
        else Assert.Empty(await scope.Db.TrackMatchQueueItems.ToArrayAsync());
    }

    private sealed class ProductionYouTubeReadHandler(string name) : HttpMessageHandler, Google.Apis.Http.IHttpClientFactory
    {
        public List<string> RemoteIds { get; set; } = [];
        public Action? OnItemsRead { get; set; }
        public int ReportedItemCountDelta { get; set; }
        public ConfigurableHttpClient CreateHttpClient(CreateHttpClientArgs args) => new(new ConfigurableMessageHandler(this));
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            object response;
            if (path.EndsWith("/playlists", StringComparison.Ordinal))
                response = new { items = new[] { new { id = "playlist", etag = "snapshot-" + string.Join(',', RemoteIds),
                    snippet = new { title = name }, contentDetails = new { itemCount = RemoteIds.Count + ReportedItemCountDelta } } } };
            else if (path.EndsWith("/playlistItems", StringComparison.Ordinal))
            {
                OnItemsRead?.Invoke();
                response = new { items = RemoteIds.Select((id, index) => new { id = "item-" + index,
                    snippet = new { title = id == "chosen-id" ? "Porter Robinson - Everything Goes On" : "New artist - New song",
                        position = index, videoOwnerChannelTitle = "Provider artist", resourceId = new { videoId = id } },
                    contentDetails = new { videoId = id }, status = new { privacyStatus = "public" } }) };
            }
            else if (path.EndsWith("/videos", StringComparison.Ordinal))
                response = new { items = RemoteIds.Distinct().Select(id => new { id,
                    snippet = new { title = "Provider song", channelTitle = "Provider artist", description = "Provider-only details" },
                    contentDetails = new { duration = "PT2M40S" }, status = new { privacyStatus = "public" } }) };
            else throw new InvalidOperationException($"Unexpected request {request.Method} {request.RequestUri}");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(JsonSerializer.Serialize(response), Encoding.UTF8, "application/json") });
        }
    }

    private sealed class ProductionYouTubeReadWriter(ProductionYouTubeReadHandler handler) : IPlaylistWriter
    {
        public string PlatformId => "youtube";
        public Task ValidateCreationAsync(PlatformAccountContext account, CancellationToken ct) => Task.CompletedTask;
        public Task<string> CreatePlaylistAsync(PlatformAccountContext account, string name, CancellationToken ct) => Task.FromResult("playlist");
        public Task ValidateDestinationAsync(PlatformAccountContext account, string id, CancellationToken ct) => Task.CompletedTask;
        public Task ReconcileAsync(PlatformAccountContext account, string id, IReadOnlyList<string> ids, CancellationToken ct)
        { handler.RemoteIds = ids.ToList(); return Task.CompletedTask; }
    }
}
