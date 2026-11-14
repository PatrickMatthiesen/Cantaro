using System.Net;
using System.Text;
using System.Text.Json;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed partial class PlaylistUnmatchedReviewTests
{
    [Theory]
    [InlineData("spotify", false)]
    [InlineData("spotify", true)]
    [InlineData("youtube", false)]
    [InlineData("youtube", true)]
    public Task CanonicalReview_UsesOwnedInput(string service, bool anchor)
        => CanonicalReviewInputAsync(service, anchor, false);

    [PostgresTheory]
    [InlineData("spotify", false)]
    [InlineData("spotify", true)]
    [InlineData("youtube", false)]
    [InlineData("youtube", true)]
    public Task CanonicalReview_UsesOwnedInput_Postgres(string service, bool anchor)
        => CanonicalReviewInputAsync(service, anchor, true);

    private static async Task CanonicalReviewInputAsync(string service, bool anchor, bool postgres)
    {
        await using var scope = await Scope.CreateAsync(service, relational: !postgres, postgres: postgres);
        var (entry, candidate) = await AddCanonicalReviewEntryAsync(scope, anchor);
        using var handler = new CanonicalInputHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.spotify.com") };
        var review = service == "spotify" ? await scope.SearchReviewAsync(http) : scope.CanonicalYouTubeReview(handler);
        var before = await scope.Db.PlaylistEntries.AsNoTracking().OrderBy(e => e.Id)
            .Select(e => new { e.Id, e.TrackId, e.TrackObservationId }).ToArrayAsync();
        var listed = Assert.Single(await review.ListAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, default));
        Assert.Equal("Everything Goes On", listed.Title);
        Assert.Equal("Porter Robinson", listed.Artist);
        Assert.Equal(160, listed.DurationSeconds);
        var search = await review.SearchAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, entry.Id, null, default);
        Assert.Equal(listed, search.Entry);
        Assert.Equal("Everything Goes On Porter Robinson", search.SuggestedQuery);
        Assert.Contains(handler.Queries, q => q.Contains("Everything Goes On") && q.Contains("Porter Robinson"));
        Assert.DoesNotContain(handler.Queries, q => q.Contains("Unknown track"));
        Assert.Null(Assert.Single(search.Candidates).Reason);
        Assert.Equal("A matching recording was found.", search.Reason);
        Assert.Equal(before, await scope.Db.PlaylistEntries.AsNoTracking().OrderBy(e => e.Id)
            .Select(e => new { e.Id, e.TrackId, e.TrackObservationId }).ToArrayAsync());
        Assert.False(await scope.Db.ProviderCatalogItems.AnyAsync(i => i.TrackId == entry.TrackId));
        Assert.Null((await scope.Db.PlaylistEntries.AsNoTracking().SingleAsync(e => e.Id == entry.Id)).TrackObservationId);

        // This choice is reachable through real search. Changing its supporting input must expire it.
        if (candidate is not null) candidate.ProviderCatalogRevision++;
        else scope.Entry.TrackObservation!.Title = "Different song";
        await scope.Db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<PlatformApiException>(() => review.ConfirmAsync(scope.UserId,
            scope.Playlist.Id, scope.Link.Id, entry.Id, Assert.Single(search.Candidates).CandidateToken, default));
        Assert.Equal("stale_match_choice", error.Code);
        Assert.Empty(PlaylistMatchingProgress.Read((await scope.Db.ServicePlaylistMappings.SingleAsync()).MatchingProgressJson));
        var renewed = await review.SearchAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, entry.Id, "custom query", default);
        Assert.Equal("custom query", handler.Queries.Last());
        await review.ConfirmAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, entry.Id,
            Assert.Single(renewed.Candidates).CandidateToken, default);
        Assert.Empty(await review.ListAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, default));
        Assert.False(await scope.Db.ProviderCatalogItems.AnyAsync(i => i.TrackId == entry.TrackId));
    }

    [Theory]
    [InlineData("spotify")]
    [InlineData("youtube")]
    public async Task CanonicalReview_PrefersCurrentSharedFactsAndCanConfirm(string service)
    {
        await using var scope = await Scope.CreateAsync(service, relational: true);
        var (entry, _) = await AddCanonicalReviewEntryAsync(scope, anchor: true);
        var track = await scope.Db.Tracks.SingleAsync(t => t.Id == entry.TrackId);
        CatalogFixture.SetFacts(track, "{\"Title\":\"Shelter\",\"Artist\":\"Porter Robinson\",\"DurationSeconds\":180}", "musicbrainz");
        await CatalogFixture.AddNewItemsAsync(scope.Db);
        await scope.Db.SaveChangesAsync();
        using var handler = new CanonicalInputHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.spotify.com") };
        var review = service == "spotify" ? await scope.SearchReviewAsync(http) : scope.CanonicalYouTubeReview(handler);
        var listed = Assert.Single(await review.ListAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, default));
        Assert.Equal("Shelter", listed.Title);
        Assert.Equal(180, listed.DurationSeconds);
        var search = await review.SearchAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, entry.Id, null, default);
        Assert.Equal("Shelter Porter Robinson", search.SuggestedQuery);
        Assert.Contains(handler.Queries, q => q.Contains("Shelter"));
        await review.ConfirmAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, entry.Id,
            Assert.Single(search.Candidates).CandidateToken, default);
        Assert.Empty(await review.ListAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, default));
        Assert.False(await scope.Db.ProviderCatalogItems.AnyAsync(i => i.TrackId == entry.TrackId && i.SourceType == service));
        Assert.Null(entry.TrackObservationId);
    }

    [Theory]
    [InlineData("other-owner", false)]
    [InlineData("expired", false)]
    [InlineData("withdrawn", false)]
    [InlineData("revision", true)]
    public Task CanonicalReview_ExcludesUnavailableInput(string change, bool anchor)
        => CanonicalReviewExclusionAsync(change, anchor, false);

    [PostgresTheory]
    [InlineData("other-owner", false)]
    [InlineData("expired", false)]
    [InlineData("withdrawn", false)]
    [InlineData("revision", true)]
    public Task CanonicalReview_ExcludesUnavailableInput_Postgres(string change, bool anchor)
        => CanonicalReviewExclusionAsync(change, anchor, true);

    private static async Task CanonicalReviewExclusionAsync(string change, bool anchor, bool postgres)
    {
        await using var scope = await Scope.CreateAsync("spotify", relational: !postgres, postgres: postgres);
        var (entry, candidate) = await AddCanonicalReviewEntryAsync(scope, anchor);
        if (change == "revision") candidate!.ProviderCatalogRevision++;
        else if (change == "other-owner")
        {
            scope.Db.Users.Add(TestUserFactory.Create(scope.UserId + 1, "other-review@example.com"));
            var other = new Playlist { Id = Guid.NewGuid(), UserId = scope.UserId + 1, Name = "Other owner" };
            scope.Db.Add(other);
            scope.Entry.Playlist = other;
            scope.Entry.PlaylistId = other.Id;
            scope.Entry.OwnerUserId = scope.UserId + 1;
            var foreign = new TrackObservation { Id = Guid.NewGuid(), OwnerUserId = other.UserId,
                InputKind = ObservationInputKind.Private, SourceType = "youtube", ExternalId = "foreign-id",
                Title = "Foreign title", Artist = "Foreign artist", MatchStatus = TrackMatchingStatuses.Pending };
            scope.Db.Add(foreign);
            scope.Entry.TrackObservation = foreign;
            scope.Entry.TrackObservationId = foreign.Id;
        }
        else
        {
            var item = new ProviderCatalogItem { FactsJson = "{}", Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = "source-video" };
            scope.VerifyMapping(item);
            item.AssociationState = RecordingAssociationState.Unknown;
            ObservationInput.SetPublic(scope.Entry.TrackObservation!, item);
            if (change == "expired") item.RevalidateAfter = DateTimeOffset.UtcNow.AddMinutes(-1);
            else item.VisibilityState = CatalogVisibility.Withdrawn;
            scope.Db.Add(item);
        }
        await scope.Db.SaveChangesAsync();
        var listed = Assert.Single(await scope.Review.ListAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, default));
        Assert.Equal("Unknown track", listed.Title);
        Assert.Equal("", listed.Artist);
        Assert.Null(listed.SourceUrl);
        Assert.Null((await scope.Db.PlaylistEntries.AsNoTracking().SingleAsync(e => e.Id == entry.Id)).TrackObservationId);
    }

    private static async Task<(PlaylistEntry Entry, TrackResolutionCandidate? Candidate)> AddCanonicalReviewEntryAsync(Scope scope, bool anchor)
    {
        var trackId = scope.Entry.TrackId!.Value;
        var supporting = new Playlist { Id = Guid.NewGuid(), UserId = scope.UserId, Name = "Supporting" };
        scope.Db.Add(supporting);
        scope.Entry.Playlist = supporting;
        scope.Entry.PlaylistId = supporting.Id;
        scope.Entry.TrackObservation!.PrivateInputJson = "{}";
        // Processing results do not associate metadata with the original recording.
        var result = new Track { Id = Guid.NewGuid() };
        scope.Db.Add(result);
        scope.Entry.TrackObservation.TrackId = result.Id;
        scope.Entry.TrackObservation.Track = result;
        TrackResolutionCandidate? candidate = null;
        if (anchor)
        {
            scope.Entry.TrackId = result.Id;
            scope.Entry.Track = result;
            var item = new ProviderCatalogItem { FactsJson = "{}", Id = Guid.NewGuid(), SourceType = "spotify", ExternalId = "anchor-private-id" };
            scope.VerifyMapping(item);
            item.AssociationState = RecordingAssociationState.Unknown;
            scope.Db.Add(item);
            candidate = new TrackResolutionCandidate { Id = Guid.NewGuid(), TrackObservationId = scope.Entry.TrackObservationId!.Value,
                AnchorTrackId = trackId, IsAccepted = true, CandidateSource = item.SourceType, ExternalId = item.ExternalId,
                ProviderCatalogItem = item, ProviderCatalogItemId = item.Id, ProviderCatalogRevision = item.Revision };
            scope.Entry.TrackObservation.AcceptedCandidateId = candidate.Id;
            scope.Db.Add(candidate);
        }
        var badInput = new TrackObservation { Id = Guid.NewGuid(), OwnerUserId = scope.UserId,
            SourceType = "youtube", ExternalId = "unreadable-input", InputKind = ObservationInputKind.Private,
            Title = "Wrong title", Artist = "Wrong artist", MatchStatus = TrackMatchingStatuses.Pending };
        var badFacts = new ProviderCatalogItem { Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = badInput.ExternalId, FactsJson = "{}" };
        CatalogFixture.Verify(badFacts, new ProviderCatalogFacts { Title = " " });
        badFacts.AssociationState = RecordingAssociationState.Unknown;
        ObservationInput.SetPublic(badInput, badFacts);
        scope.Db.Add(badFacts);
        scope.Db.Add(new PlaylistEntry { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            OwnerUserId = scope.UserId, Playlist = new Playlist { Id = Guid.NewGuid(), UserId = scope.UserId, Name = "Unusable" }, TrackId = trackId,
            TrackObservation = badInput, TrackObservationId = badInput.Id });
        await scope.Db.SaveChangesAsync();
        Assert.True(await new MusicLibraryQueryService(scope.Db).AddCanonicalSongToPlaylistAsync(trackId, scope.Playlist.Id, scope.UserId, default));
        return (await scope.Db.PlaylistEntries.SingleAsync(e => e.PlaylistId == scope.Playlist.Id), candidate);
    }

    private sealed class CanonicalInputHandler : HttpMessageHandler, Google.Apis.Http.IHttpClientFactory
    {
        public List<string> Queries { get; } = [];
        public Google.Apis.Http.ConfigurableHttpClient CreateHttpClient(Google.Apis.Http.CreateHttpClientArgs args)
            => new(new Google.Apis.Http.ConfigurableMessageHandler(this));
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            var spotify = uri.Host.Contains("spotify");
            object response;
            if (uri.AbsolutePath.EndsWith("/me")) response = new { id = "account" };
            else if (uri.AbsolutePath.EndsWith("/token")) response = new { access_token = "catalog-token", expires_in = 3600 };
            else if (uri.AbsolutePath.EndsWith("/search"))
            {
                Queries.Add(Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query)["q"].ToString());
                response = spotify ? (object)new { tracks = new { items = new[] { new { type = "track", id = "chosen-id",
                    name = "Everything Goes On", duration_ms = 160000, artists = new[] { new { name = "Porter Robinson" } } } } } }
                    : new { items = new[] { new { id = new { videoId = "chosen-id" } } } };
            }
            else
            {
                Assert.EndsWith("/videos", uri.AbsolutePath);
                response = new { items = new[] { new { id = "chosen-id", snippet = new { title = "Everything Goes On",
                    channelTitle = "Porter Robinson", description = "" },
                    contentDetails = new { duration = "PT2M40S" }, status = new { privacyStatus = "public" } } } };
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(JsonSerializer.Serialize(response), Encoding.UTF8, "application/json") });
        }
    }

    private sealed partial class Scope
    {
        public PlaylistUnmatchedReviewService CanonicalYouTubeReview(CanonicalInputHandler handler)
        {
            var youtube = new YouTubeService(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), Db,
                new TokenEncryptionService(_protector), Microsoft.Extensions.Logging.Abstractions.NullLogger<YouTubeService>.Instance,
                (_, _) => Task.FromResult(new Google.Apis.YouTube.v3.YouTubeService(new Google.Apis.Services.BaseClientService.Initializer
                { ApplicationName = "Cantaro tests", HttpClientFactory = handler })));
            return new(Db, null!, null!, [], youtube,
                new TrackMatchingService(Db, [], Microsoft.Extensions.Logging.Abstractions.NullLogger<TrackMatchingService>.Instance),
                new PlaylistCanonicalReconciliationService(Db), Microsoft.Extensions.Options.Options.Create(new Cantaro.Api.Configuration.TrackMatchingOptions()),
                _protector, new PlaylistSyncCoordinator(Db, [], [], TimeProvider.System,
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<PlaylistSyncCoordinator>.Instance));
        }
    }
}
