using System.Net;
using System.Text;
using System.Text.Json;
using Cantaro.Api.Configuration;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Google.Apis.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed partial class ProviderCatalogAcquisitionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task YouTubeReadPopulatesOwnedReferenceWithoutInventingDescription(bool viaImporter, bool referenceOnly)
    {
        await using var fixture = await Fixture.CreateAsync();
        var account = await fixture.AddYouTubeAccountAsync();
        using var scope = fixture.OpenScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.TrackObservations.Add(new TrackObservation
        {
            Id = Guid.NewGuid(), OwnerUserId = account.UserId, SourceType = "youtube", ExternalId = "video",
            Title = referenceOnly ? "" : "Old title", MatchStatus = TrackMatchingStatuses.Pending,
            RawMetadata = referenceOnly ? null : JsonSerializer.Serialize(new TrackObservationMetadata { Description = "Other Artist - Song" })
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        using var handler = new YouTubeCatalogHandler();

        await ReadYouTubeAsync(fixture, db, handler, account, viaImporter);

        db.ChangeTracker.Clear();
        var refreshed = await db.TrackObservations.SingleAsync();
        Assert.Equal("Song", refreshed.Title);
        Assert.Equal(referenceOnly ? null : "Other Artist - Song",
            TrackObservationParser.ReadMetadata(refreshed)?.Description);
        Assert.Empty(await db.TrackSourceIds.ToListAsync());
        Assert.Empty(await db.TrackArtistCredits.ToListAsync());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task YouTubeRefreshRevalidatesArtworkButRejectsChangedRecording(bool viaImporter, bool changedRecording)
    {
        await using var fixture = await Fixture.CreateAsync();
        var alice = await fixture.AddYouTubeAccountAsync();
        var bob = await fixture.AddYouTubeAccountAsync(8902, 90);
        using var handler = new YouTubeCatalogHandler { Artwork = "https://public.example/old.jpg" };
        Guid trackId;
        long originalRevision;
        using (var scope = fixture.OpenScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var acquired = await fixture.YouTubeService(db, handler).GetPublicCatalogItemAsync(alice, "video", default);
            var catalog = await db.ProviderCatalogItems.SingleAsync(item => item.Id == acquired!.Id);
            originalRevision = catalog.Revision;
            var track = new Track { Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
            db.Tracks.Add(track);
            Assert.True(await new ProviderRecordingAssociations(db).TryAssociateAsync(track, catalog, null, true, default));
            db.TrackObservations.Add(new TrackObservation
            {
                Id = Guid.NewGuid(), OwnerUserId = alice.UserId, SourceType = "youtube", ExternalId = "video",
                Title = "Song", Artist = "Artist", TrackId = track.Id, MatchStatus = TrackMatchingStatuses.Matched
            });
            await db.SaveChangesAsync();
            trackId = track.Id;
        }
        handler.Artwork = "https://public.example/new.jpg";
        if (changedRecording) handler.Title = "Artist - Song (Live)";
        using (var scope = fixture.OpenScope())
            await ReadYouTubeAsync(fixture, scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), handler, alice, viaImporter);
        using (var scope = fixture.OpenScope())
            await ReadYouTubeAsync(fixture, scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), handler, bob, viaImporter);

        using var verificationScope = fixture.OpenScope();
        var verify = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var mapping = await verify.TrackSourceIds.Include(item => item.ProviderCatalogItem).SingleAsync();
        Assert.True(mapping.ProviderCatalogItem!.Revision > originalRevision);
        Assert.Equal(!changedRecording, ProviderCatalogPolicy.IsVerified(mapping, DateTimeOffset.UtcNow));
        var bobObservation = await verify.TrackObservations.SingleAsync(item => item.OwnerUserId == bob.UserId);
        Assert.Equal(changedRecording ? (Guid?)null : trackId, bobObservation.TrackId);
        Assert.Equal(trackId, (await verify.TrackObservations.SingleAsync(item => item.OwnerUserId == alice.UserId)).TrackId);
        Assert.Single(await verify.Tracks.ToListAsync());
        if (!changedRecording)
        {
            Assert.Equal(mapping.ProviderCatalogItem.Revision, mapping.CatalogRevision);
            var current = await verify.Tracks.Include(item => item.SourceIds).ThenInclude(item => item.ProviderCatalogItem).SingleAsync();
            Assert.Equal(handler.Artwork, ProviderCatalogPolicy.SharedFacts(current, DateTimeOffset.UtcNow)!.ThumbnailUrl);
            Assert.Empty(await verify.TrackMatchQueueItems.ToListAsync());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task YouTubeSearchAdapterRetainsDescriptionForPerformerAndConflictMatching(bool conflicting)
    {
        await using var fixture = await Fixture.CreateAsync();
        var account = await fixture.AddYouTubeAccountAsync();
        using var scope = fixture.OpenScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        using var handler = new YouTubeCatalogHandler
        {
            Title = "MORTALS FUNK REMIX // Victory Royale B**ch!", Channel = "MrMoMMusic", Duration = "PT2M27S",
            Description = "LXNGVX, Warriyo - Mortals Funk Remix" + (conflicting ? "\nOther Artist - Mortals Funk Remix" : "")
        };
        var source = new TrackObservation
        {
            Id = Guid.NewGuid(), SourceType = "spotify", ExternalId = "source", Title = "Mortals Funk Remix",
            Artist = "LXNGVX, Warriyo", DurationSeconds = 146, MatchStatus = TrackMatchingStatuses.Pending
        };

        var candidate = Assert.Single(await fixture.YouTubeService(db, handler).SearchVideoCandidatesAsync(account, source, default));

        var metadata = JsonSerializer.Deserialize<TrackObservationMetadata>(candidate.RawMetadata!);
        Assert.Equal(handler.Title, metadata!.OriginalTitle);
        Assert.Equal(handler.Channel, metadata.OriginalArtist);
        Assert.Equal(handler.Channel, metadata.ChannelTitle);
        Assert.Equal(handler.Description, metadata.Description);
        var decision = TrackMatchDecisionEngine.Evaluate(source, [candidate], new TrackMatchingOptions()).Decision;
        Assert.Equal(!conflicting, decision.AcceptedCandidate is not null);
        Assert.Equal(!conflicting, PlaylistDestinationIdentityResolver.Assess(source, [candidate], new TrackMatchingOptions()).Accepted is not null);
        var catalog = (await fixture.Store.FindAsync("youtube", "video", default))!;
        Assert.DoesNotContain("Description", catalog.FactsJson, StringComparison.Ordinal);
        Assert.Empty(await fixture.ReadSourcesAsync());
    }

    private static async Task ReadYouTubeAsync(Fixture fixture, ApplicationDbContext db, YouTubeCatalogHandler handler,
        PlatformAccountContext account, bool viaImporter)
    {
        var service = fixture.YouTubeService(db, handler);
        var queue = new TrackMatchQueue(db, TimeProvider.System);
        var reconciliation = new PlaylistCanonicalReconciliationService(db);
        var provider = new YouTubePlaylistSyncProvider(db, service, queue,
            new TrackMatchingService(db, [], NullLogger<TrackMatchingService>.Instance), reconciliation,
            Options.Create(new TrackMatchingOptions()));
        if (viaImporter)
        {
            await new YouTubePlaylistSyncService(new PlaylistSyncCoordinator(db, [provider], [], TimeProvider.System,
                NullLogger<PlaylistSyncCoordinator>.Instance))
                .SyncYouTubePlaylistAsync(account, "playlist", default);
        }
        else
        {
            await provider.ReadAsync(account, "playlist", default);
        }
    }

    private sealed class YouTubeCatalogHandler : HttpMessageHandler, Google.Apis.Http.IHttpClientFactory
    {
        public string Title { get; set; } = "Artist - Song";
        public string Channel { get; set; } = "Artist";
        public string? Description { get; set; }
        public string? Artwork { get; set; }
        public string Duration { get; set; } = "PT2M";
        public ConfigurableHttpClient CreateHttpClient(CreateHttpClientArgs args) => new(new ConfigurableMessageHandler(this));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            object response;
            if (path.EndsWith("/playlists", StringComparison.Ordinal))
                response = new { items = new[] { new { id = "playlist", etag = "revision", snippet = new { title = "Playlist" },
                    contentDetails = new { itemCount = 1 } } } };
            else if (path.EndsWith("/playlistItems", StringComparison.Ordinal))
                response = new { items = new[] { new { id = "playlist-item", snippet = new { title = Title, position = 0,
                    videoOwnerChannelTitle = Channel, resourceId = new { videoId = "video" } },
                    contentDetails = new { videoId = "video" }, status = new { privacyStatus = "public" } } } };
            else if (path.EndsWith("/search", StringComparison.Ordinal))
                response = new { items = new[] { new { id = new { videoId = "video" } } } };
            else if (path.EndsWith("/videos", StringComparison.Ordinal))
                response = new { items = new[] { new { id = "video", snippet = new { title = Title, channelTitle = Channel,
                    description = Description, thumbnails = new { high = new { url = Artwork } } },
                    contentDetails = new { duration = Duration }, status = new { privacyStatus = "public" } } } };
            else throw new InvalidOperationException($"Unexpected YouTube request: {request.RequestUri}");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(JsonSerializer.Serialize(response), Encoding.UTF8, "application/json") });
        }
    }
}
