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
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task KnownCanonicalDestinationChoice_RemainsScoped(bool canonicalOnly, bool customQuery)
        => AssertDestinationChoiceScopedAsync(canonicalOnly, customQuery, postgres: false);

    [PostgresFact]
    public Task KnownCanonicalDestinationChoice_PostgresRemainsScoped()
        => AssertDestinationChoiceScopedAsync(canonicalOnly: true, customQuery: true, postgres: true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KnownCanonicalDestinationChoice_RejectsConflictingSearchedVideo(bool alreadyConfirmed)
    {
        await using var scope = await Scope.CreateAsync("youtube", relational: true);
        var review = scope.YouTubeSearchReview(new DestinationVideoHandler());
        var searched = await review.SearchAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id,
            scope.Entry.Id, "Porter Robinson Shelter", default);
        var choice = Assert.Single(searched.Candidates);
        if (alreadyConfirmed)
        {
            await review.ConfirmAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id,
                scope.Entry.Id, choice.CandidateToken, default);
            var playlist = new Playlist { Id = Guid.NewGuid(), UserId = scope.UserId, Name = "Owned alternative" };
            var observation = new TrackObservation { Id = Guid.NewGuid(), TrackId = scope.Entry.TrackId,
                SourceType = "youtube", ExternalId = "alternative-a", Title = "Everything Goes On",
                MatchStatus = TrackMatchingStatuses.Matched };
            scope.Db.PlaylistEntries.Add(new PlaylistEntry { Id = Guid.NewGuid(), Playlist = playlist,
                PlaylistId = playlist.Id, TrackId = scope.Entry.TrackId, TrackObservation = observation,
                TrackObservationId = observation.Id });
        }
        var other = new Track { Id = Guid.NewGuid(), SearchTitle = "Shelter" };
        var mapping = new TrackSourceId { Id = Guid.NewGuid(), TrackId = other.Id,
            SourceType = "youtube", ExternalId = choice.ExternalId };
        scope.Db.AddRange(other, mapping);
        await scope.Db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<PlatformApiException>(() => review.ConfirmAsync(scope.UserId,
            scope.Playlist.Id, scope.Link.Id, scope.Entry.Id, choice.CandidateToken, default));

        Assert.Equal("recording_already_linked", error.Code);
        Assert.Equal(other.Id, (await scope.Db.TrackSourceIds.SingleAsync()).TrackId);
        Assert.Equal(alreadyConfirmed, !string.IsNullOrEmpty((await scope.Db.ServicePlaylistMappings.SingleAsync()).MatchingProgressJson));
        Assert.Equal(scope.Entry.TrackId, scope.Entry.TrackObservation!.TrackId);
    }

    private static async Task AssertDestinationChoiceScopedAsync(bool canonicalOnly, bool customQuery, bool postgres)
    {
        await using var scope = await Scope.CreateAsync("youtube", relational: !postgres, postgres: postgres);
        var trackId = scope.Entry.TrackId!.Value;
        var source = scope.Entry.TrackObservation!;
        var sourceId = source.Id;
        scope.Db.TrackSourceIds.AddRange(
            new TrackSourceId { Id = Guid.NewGuid(), TrackId = trackId,
                SourceType = source.SourceType, ExternalId = source.ExternalId, OriginMetadata = "original-source" },
            new TrackSourceId { Id = Guid.NewGuid(), TrackId = trackId,
                SourceType = "youtube", ExternalId = "canonical-video-a", OriginMetadata = "original-canonical" });
        if (canonicalOnly)
        {
            scope.Entry.TrackObservationId = null;
            scope.Entry.TrackObservation = null;
        }
        var otherUser = scope.UserId + 1;
        var otherPlaylist = new Playlist { Id = Guid.NewGuid(), UserId = otherUser, Name = "Other owner" };
        var otherAccount = new ConnectedServiceAccount { Id = 52, UserId = otherUser,
            Service = "youtube", ExternalAccountId = "other-account", ConnectionState = "connected" };
        var otherLink = new ServicePlaylistMapping { Id = Guid.NewGuid(), PlaylistId = otherPlaylist.Id,
            UserId = otherUser, Service = "youtube", ConnectedServiceAccountId = otherAccount.Id,
            ExternalAccountId = otherAccount.ExternalAccountId, ServicePlaylistId = "other-playlist",
            State = "active", SyncMode = "bidirectional" };
        var otherEntry = new PlaylistEntry { Id = Guid.NewGuid(), PlaylistId = otherPlaylist.Id, TrackId = trackId };
        scope.Db.AddRange(TestUserFactory.Create(otherUser, "scope-other@example.com"),
            otherPlaylist, otherAccount, otherLink, otherEntry);
        await scope.Db.SaveChangesAsync();
        var originals = await scope.Db.TrackSourceIds.AsNoTracking().OrderBy(item => item.ExternalId)
            .Select(item => new { item.Id, item.TrackId, item.SourceType, item.ExternalId, item.OriginMetadata }).ToArrayAsync();
        var handler = new DestinationVideoHandler();
        var review = scope.YouTubeSearchReview(handler);
        var searched = await review.SearchAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id,
            scope.Entry.Id, customQuery ? "Porter Robinson Shelter" : null, default);
        Assert.Equal(1, handler.Searches);
        var choice = Assert.Single(searched.Candidates);
        Assert.Equal("public-video-b", choice.ExternalId);
        await review.ConfirmAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id,
            scope.Entry.Id, choice.CandidateToken, default);
        await review.ConfirmAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id,
            scope.Entry.Id, choice.CandidateToken, default);

        Assert.False(await scope.Db.TrackSourceIds.AnyAsync(item => item.SourceType == "youtube"
            && item.ExternalId == "public-video-b"));
        Assert.Equal(originals, await scope.Db.TrackSourceIds.AsNoTracking().OrderBy(item => item.ExternalId)
            .Select(item => new { item.Id, item.TrackId, item.SourceType, item.ExternalId, item.OriginMetadata }).ToArrayAsync());
        Assert.Equal(trackId, (await scope.Db.TrackObservations.SingleAsync(item => item.Id == sourceId)).TrackId);
        var saved = await scope.Db.ServicePlaylistMappings.SingleAsync(item => item.Id == scope.Link.Id);
        var proof = Assert.Single(PlaylistMatchingProgress.Read(saved.MatchingProgressJson)).Value;
        Assert.True(proof.ManualConfirmation);
        Assert.Equal("public-video-b", proof.ExternalId);

        // Copying a durable proof or replaying a signed choice cannot authorize another owner.
        otherLink.MatchingProgressJson = PlaylistMatchingProgress.Serialize(new() { [otherEntry.Id] = proof });
        await scope.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<PlatformApiException>(() => review.ConfirmAsync(otherUser, otherPlaylist.Id,
            otherLink.Id, otherEntry.Id, choice.CandidateToken, default));
        var otherProvider = new ReviewResolverProvider(scope.Db, otherPlaylist.Name, "youtube");
        Assert.Null(await otherProvider.ResolveAsync(new(otherUser, 52, "other-account"), otherEntry, default));
        Assert.Single(await review.ListAsync(otherUser, otherPlaylist.Id, otherLink.Id, default));
        Assert.Null(await otherProvider.ResolveAsync(new(scope.UserId, 52, "other-account"), scope.Entry, default));

        // Later imports of B use ordinary identity resolution, not the confirming user's proof.
        var imported = new TrackObservation { Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = choice.ExternalId,
            Title = "Shelter", Artist = "Porter Robinson", DurationSeconds = 160,
            MatchStatus = TrackMatchingStatuses.Pending };
        scope.Db.PlaylistEntries.Add(new PlaylistEntry { Id = Guid.NewGuid(), PlaylistId = otherPlaylist.Id,
            TrackObservation = imported, TrackObservationId = imported.Id, Position = 1 });
        await scope.Db.SaveChangesAsync();
        Assert.Null(await new TrackIdentityResolver(scope.Db).ResolveExistingAsync(
            new("youtube", choice.ExternalId, imported.Title, imported.Artist, 160), default));
        var matching = new TrackMatchingService(scope.Db, [], NullLogger<TrackMatchingService>.Instance);
        Assert.Null((await matching.ProcessObservationAsync(imported.Id, default)).TrackId);

        var provider = new ReviewResolverProvider(scope.Db, scope.Playlist.Name, "youtube");
        var writer = new ReviewWriter(provider);
        for (var pass = 0; pass < 2; pass++)
        {
            scope.Db.ChangeTracker.Clear();
            var coordinator = new PlaylistSyncCoordinator(scope.Db, [provider], [writer], TimeProvider.System,
                NullLogger<PlaylistSyncCoordinator>.Instance);
            var link = await scope.Db.ServicePlaylistMappings.SingleAsync(item => item.Id == scope.Link.Id);
            link.BaselineName ??= scope.Playlist.Name;
            link.BaselineJson ??= new PlaylistSyncBaseline([]).Serialize();
            await scope.Db.SaveChangesAsync();
            await coordinator.RunAsync(scope.UserId, scope.Playlist.Id, "youtube", default);
            Assert.Equal("success", link.LastSyncStatus);
            Assert.Equal(trackId, Assert.Single(PlaylistSyncBaseline.Parse(link.BaselineJson)!.Items).TrackId);
            Assert.Equal(proof, Assert.Single(PlaylistMatchingProgress.Read(link.MatchingProgressJson)).Value);
            Assert.Equal(scope.Entry.Id, (await scope.Db.PlaylistEntries.SingleAsync(item => item.PlaylistId == scope.Playlist.Id)).Id);
            Assert.False(await scope.Db.TrackSourceIds.AnyAsync(item => item.SourceType == "youtube"
                && item.ExternalId == "public-video-b"));
        }
        Assert.Equal(["public-video-b"], Assert.Single(writer.Writes));
        Assert.Equal(0, provider.Searches);
        await review.ConfirmAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id,
            scope.Entry.Id, choice.CandidateToken, default);
        Assert.Equal(originals, await scope.Db.TrackSourceIds.AsNoTracking().OrderBy(item => item.ExternalId)
            .Select(item => new { item.Id, item.TrackId, item.SourceType, item.ExternalId, item.OriginMetadata }).ToArrayAsync());
        Assert.Null((await scope.Db.TrackObservations.SingleAsync(item => item.Id == imported.Id)).TrackId);
    }

    private sealed class DestinationVideoHandler : HttpMessageHandler, Google.Apis.Http.IHttpClientFactory
    {
        public int Searches { get; private set; }
        public ConfigurableHttpClient CreateHttpClient(CreateHttpClientArgs args)
            => new(new ConfigurableMessageHandler(this));
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            object response;
            if (request.RequestUri!.AbsolutePath.EndsWith("/search"))
            {
                Searches++;
                response = new { items = new[] { new { id = new { videoId = "public-video-b" } } } };
            }
            else
            {
                Assert.EndsWith("/videos", request.RequestUri.AbsolutePath);
                response = new { items = new[] { new { id = "public-video-b",
                    snippet = new { title = "Porter Robinson - Shelter", channelTitle = "Porter Robinson", description = "Public recording" },
                    contentDetails = new { duration = "PT2M40S" }, status = new { privacyStatus = "public" } } } };
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(JsonSerializer.Serialize(response), Encoding.UTF8, "application/json") });
        }
    }

    private sealed partial class Scope
    {
        public PlaylistUnmatchedReviewService YouTubeSearchReview(DestinationVideoHandler handler)
        {
            var youtube = new YouTubeService(new ConfigurationBuilder().Build(), Db, new TokenEncryptionService(_protector),
                NullLogger<YouTubeService>.Instance, (_, _) => Task.FromResult(new Google.Apis.YouTube.v3.YouTubeService(
                    new BaseClientService.Initializer { ApplicationName = "Cantaro tests", HttpClientFactory = handler })));
            return new(Db, null!, null!, [], youtube,
                new TrackMatchingService(Db, [], NullLogger<TrackMatchingService>.Instance),
                new PlaylistCanonicalReconciliationService(Db), Options.Create(new TrackMatchingOptions()), _protector,
                new PlaylistSyncCoordinator(Db, [], [], TimeProvider.System, NullLogger<PlaylistSyncCoordinator>.Instance));
        }
    }
}
