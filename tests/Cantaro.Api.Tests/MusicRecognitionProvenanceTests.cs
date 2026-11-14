using System.Security.Claims;
using System.Net;
using System.Text;
using System.Text.Json;
using Cantaro.Api.Controllers;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Google.Apis.Http;
using Google.Apis.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class MusicRecognitionProvenanceTests
{
    private static async Task<(ApplicationDbContext Db, SqliteConnection Connection)> CreateDbAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        return (db, connection);
    }

    [Fact]
    public async Task Recognize_UsesVerifiedPublicMappingForAnotherLibraryUser()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var now = DateTimeOffset.UtcNow;
        var requester = TestUserFactory.Create(735, "recognition-requester@example.com");
        var playlist = new Playlist { Id = Guid.NewGuid(), UserId = requester.Id, Name = "Saved" };
        var item = new ProviderCatalogItem
        {
            Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = "publicVid01",
            FactsJson = JsonSerializer.Serialize(new ProviderCatalogFacts
            {
                Title = "Public song", Artist = "Public artist", ThumbnailUrl = "https://img.example/public.jpg",
                ProviderUrl = "https://youtube.example/watch?v=publicVideo1", DurationSeconds = 187
            }),
            VisibilityState = CatalogVisibility.Public, VerificationMethod = CatalogVerificationMethods.YouTubePublicVideo,
            VerifiedAt = now, RevalidateAfter = now.AddDays(1), Revision = 1
        };
        var track = new Track { Id = Guid.NewGuid(), };
        track.ProviderItems.Add(TrackMatchFixtures.Associate(item, track.Id, RecordingAssociationState.Verified));
        db.AddRange(requester, playlist, track);
        db.PlaylistEntries.Add(new PlaylistEntry
        { OwnerUserId = requester.Id,
            Id = Guid.NewGuid(), PlaylistId = playlist.Id, TrackId = track.Id, Position = 0
        });
        await db.SaveChangesAsync();

        var response = await RecognizeAsync(db, requester, item.ExternalId);

        Assert.Equal("matched", response.Status);
        Assert.Equal(track.Id, response.TrackId);
        Assert.True(response.InUserLibrary);
        var song = Assert.IsType<MusicLibrarySongDto>(response.Song);
        Assert.Equal("Public song", song.Title);
        Assert.Equal("https://img.example/public.jpg", song.ThumbnailUrl);
        Assert.Contains(song.SourceIdentities, identity => identity.Source == "youtube" && identity.ExternalId == item.ExternalId);
        Assert.Contains(song.PlatformLinks, link => link.ExternalId == item.ExternalId);
    }

    [Fact]
    public async Task Recognize_DoesNotMatchAnotherUsersPrivateObservation()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var requester = TestUserFactory.Create(735, "recognition-requester@example.com");
        var owner = TestUserFactory.Create(736, "recognition-owner@example.com");
        var requesterPlaylist = new Playlist { Id = Guid.NewGuid(), UserId = requester.Id, Name = "Saved" };
        var ownerPlaylist = new Playlist { Id = Guid.NewGuid(), UserId = owner.Id, Name = "Private" };
        var track = new Track { Id = Guid.NewGuid() };
        var privateObservation = new TrackObservation
        {
            InputKind = ObservationInputKind.Private,
            Id = Guid.NewGuid(), OwnerUserId = owner.Id, TrackId = track.Id, SourceType = "youtube",
            ExternalId = "privateVid1", Title = "Private video", ThumbnailUrl = "https://img.example/private.jpg",
            MatchStatus = TrackMatchingStatuses.Matched
        };
        track.ProviderItems.Add(TrackMatchFixtures.Associate(new ProviderCatalogItem { Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = privateObservation.ExternalId, FactsJson = "{}" }, track.Id, RecordingAssociationState.Unknown));
        db.AddRange(requester, owner, requesterPlaylist, ownerPlaylist, track, privateObservation);
        db.PlaylistEntries.AddRange(
            new PlaylistEntry { OwnerUserId = requester.Id, Id = Guid.NewGuid(), PlaylistId = requesterPlaylist.Id, TrackId = track.Id, Position = 0 },
            new PlaylistEntry { OwnerUserId = owner.Id, Id = Guid.NewGuid(), PlaylistId = ownerPlaylist.Id, TrackId = track.Id,
                TrackObservationId = privateObservation.Id, Position = 0 });
        await db.SaveChangesAsync();

        var requesterResponse = await RecognizeAsync(db, requester, privateObservation.ExternalId);

        Assert.Equal("pending", requesterResponse.Status);
        Assert.Null(requesterResponse.TrackId);
        Assert.False(requesterResponse.InUserLibrary);
        Assert.Null(requesterResponse.Song);
        Assert.False(await db.TrackObservations.AnyAsync(observation => observation.OwnerUserId == requester.Id));
        Assert.Equal(track.Id, await db.TrackObservations.Where(observation => observation.Id == privateObservation.Id)
            .Select(observation => observation.TrackId).SingleAsync());

        var ownerResponse = await RecognizeAsync(db, owner, privateObservation.ExternalId);
        Assert.Equal("matched", ownerResponse.Status);
        Assert.Equal(track.Id, ownerResponse.TrackId);
        var ownerSong = Assert.IsType<MusicLibrarySongDto>(ownerResponse.Song);
        Assert.Equal(privateObservation.ThumbnailUrl, ownerSong.ThumbnailUrl);
        Assert.Contains(ownerSong.SourceIdentities, identity => identity.Source == "youtube"
            && identity.ExternalId == privateObservation.ExternalId);
        Assert.Contains(ownerSong.PlatformLinks, link => link.ExternalId == privateObservation.ExternalId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recognize_FailedProviderReadCannotTurnPageTitleIntoAnOwnedSnapshot(bool existingReference)
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var user = TestUserFactory.Create(735, "recognition-requester@example.com");
        db.Users.Add(user);
        if (existingReference)
            db.TrackObservations.Add(new TrackObservation
            {
            InputKind = ObservationInputKind.Reference,
                Id = Guid.NewGuid(), OwnerUserId = user.Id, SourceType = "youtube", ExternalId = "sourceVid01",
                Title = null, MatchStatus = TrackMatchingStatuses.Pending
            });
        await db.SaveChangesAsync();

        var response = await RecognizeAsync(db, user, "sourceVid01", "Unverified page title");

        Assert.Equal("pending", response.Status);
        Assert.Null(response.Title);
        Assert.Null(response.Artist);
        Assert.Null(response.TrackId);
        Assert.Empty(await db.TrackMatchQueueItems.ToListAsync());
        var observations = await db.TrackObservations.ToListAsync();
        Assert.Equal(existingReference ? 1 : 0, observations.Count);
        Assert.All(observations, reference =>
        {
            Assert.Null(reference.Title);
            Assert.Null(reference.PrivateInputJson);
            Assert.False(ProviderCatalogPolicy.HasOwnedInput(reference, user.Id));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recognize_SuccessfulProviderReadPersistsSnapshotAndSchedulesMatching(bool existingReference)
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var user = TestUserFactory.Create(735, "recognition-requester@example.com");
        var referenceId = Guid.NewGuid();
        db.AddRange(user, new ConnectedServiceAccount
        {
            Id = 739, UserId = user.Id, Service = "youtube", ExternalAccountId = "channel",
            ConnectionState = "connected"
        });
        if (existingReference)
            db.TrackObservations.Add(new TrackObservation
            {
            InputKind = ObservationInputKind.Reference,
                Id = referenceId, OwnerUserId = user.Id, SourceType = "youtube", ExternalId = "sourceVid01",
                Title = null, MatchStatus = TrackMatchingStatuses.Pending
            });
        await db.SaveChangesAsync();
        using var handler = new RecognitionVideoHandler();
        var youtube = new YouTubeService(new ConfigurationBuilder().Build(), db,
            new TokenEncryptionService(new EphemeralDataProtectionProvider()), NullLogger<YouTubeService>.Instance,
            (_, _) => Task.FromResult(new Google.Apis.YouTube.v3.YouTubeService(new BaseClientService.Initializer
            { ApplicationName = "Recognition snapshot tests", HttpClientFactory = handler })));

        var response = await RecognizeAsync(db, user, "sourceVid01", youtubeOverride: youtube);

        Assert.Equal("pending", response.Status);
        Assert.Equal("Refreshed song", response.Title);
        await using var verify = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        var observation = await verify.TrackObservations.SingleAsync();
        if (existingReference) Assert.Equal(referenceId, observation.Id);
        Assert.True(ProviderCatalogPolicy.HasOwnedInput(observation, user.Id));
        Assert.NotNull(observation.PrivateInputJson);
        Assert.Equal(observation.Id, (await verify.TrackMatchQueueItems.SingleAsync()).TrackObservationId);
        var claimed = await new TrackMatchQueue(verify, TimeProvider.System).TryClaimNextAsync(default);
        Assert.Equal(observation.Id, Assert.IsType<ClaimedTrackMatch>(claimed).ObservationId);
    }

    [Fact]
    public async Task Recognize_DoesNotServeAnAutomaticResultWhosePublicFactsExpired()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var user = TestUserFactory.Create(735, "expired-recognition@example.test");
        var track = new Track { Id = Guid.NewGuid() };
        var item = new ProviderCatalogItem
        {
            Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = "expiredVid1",
            FactsJson = JsonSerializer.Serialize(new ProviderCatalogFacts { Title = "Expired public title" }),
            Revision = 1, VisibilityState = CatalogVisibility.Public,
            VerificationMethod = CatalogVerificationMethods.YouTubePublicVideo,
            VerifiedAt = DateTimeOffset.UtcNow.AddDays(-2), RevalidateAfter = DateTimeOffset.UtcNow.AddDays(-1)
        };
        TrackMatchFixtures.Associate(item, track.Id, RecordingAssociationState.Verified);
        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(), OwnerUserId = user.Id, SourceType = item.SourceType, ExternalId = item.ExternalId,
            TrackId = track.Id, MatchStatus = TrackMatchingStatuses.Matched
        };
        ObservationInput.SetPublic(observation, item);
        db.AddRange(user, track, item, observation);
        await db.SaveChangesAsync();
        var response = await RecognizeAsync(db, user, item.ExternalId);
        Assert.Equal("pending", response.Status);
        Assert.Null(response.TrackId);
        Assert.Null(response.Title);
    }

    [Theory]
    [InlineData("candidate", "fresh")]
    [InlineData("version", "fresh")]
    [InlineData("candidate", "expired")]
    [InlineData("version", "expired")]
    [InlineData("candidate", "other-owner")]
    [InlineData("version", "other-owner")]
    [InlineData("create", "fresh")]
    [InlineData("create", "expired")]
    [InlineData("create", "other-owner")]
    public async Task Recognize_StandaloneReviewedPublicResultUsesOnlyFreshOwnedInput(string resolution, string access)
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var owner = TestUserFactory.Create(735, "reviewed-recognition@example.test");
        var other = TestUserFactory.Create(736, "other-recognition@example.test");
        var now = DateTimeOffset.UtcNow;
        var track = new Track { Id = Guid.NewGuid(), VersionFlags = resolution == "version" ? TrackVersionFlags.Live : TrackVersionFlags.None };
        var item = new ProviderCatalogItem
        {
            Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = "reviewedVid", Revision = 1,
            FactsJson = JsonSerializer.Serialize(new ProviderCatalogFacts { Title = "Reviewed source title", Artist = "Reviewed source artist" }),
            VisibilityState = CatalogVisibility.Public, VerificationMethod = CatalogVerificationMethods.YouTubePublicVideo,
            VerifiedAt = now.AddDays(-2), RevalidateAfter = access == "expired" ? now.AddDays(-1) : now.AddHours(1)
        };
        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(), OwnerUserId = owner.Id, SourceType = item.SourceType, ExternalId = item.ExternalId,
            TrackId = track.Id, MatchStatus = TrackMatchingStatuses.Matched,
            ResolutionNotes = resolution == "version" ? "Created a live version Track after explicit review." : resolution == "create" ? "Created canonical track from observation during manual review." : "Resolved by manual candidate selection."
        };
        ObservationInput.SetPublic(observation, item);
        var candidate = new TrackResolutionCandidate
        {
            Id = Guid.NewGuid(), TrackObservationId = observation.Id, CandidateSource = item.SourceType,
            ExternalId = item.ExternalId, ProviderCatalogItemId = item.Id, ProviderCatalogRevision = item.Revision,
            IsAccepted = true, Score = 1
        };
        db.AddRange(owner, other, track, item, observation, candidate);
        await db.SaveChangesAsync();
        observation.AcceptedCandidateId = resolution == "create" ? null : candidate.Id;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var response = await RecognizeAsync(db, access == "other-owner" ? other : owner, item.ExternalId);

        Assert.False(response.InUserLibrary);
        Assert.Null(response.Song);
        if (access == "fresh")
        {
            Assert.Equal("matched", response.Status);
            Assert.Equal(track.Id, response.TrackId);
            Assert.Equal("Reviewed source title", response.Title);
            Assert.Equal("Reviewed source artist", response.Artist);
        }
        else
        {
            Assert.Equal("pending", response.Status);
            Assert.Null(response.TrackId); Assert.Null(response.Title); Assert.Null(response.Artist);
        }
        Assert.Empty(await db.PlaylistEntries.ToArrayAsync());
        Assert.Null((await db.ProviderCatalogItems.SingleAsync()).TrackId);
        Assert.Null((await db.TrackObservations.SingleAsync()).Title);
    }

    [Theory]
    [InlineData("current")]
    [InlineData("candidate-expired")]
    [InlineData("source-changed")]
    [InlineData("other-owner")]
    public async Task Recognize_ReusesOnlySupportedOwnerCrossProviderMatches(string state)
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var owner = TestUserFactory.Create(735, "cross-owner@example.test");
        var other = TestUserFactory.Create(736, "cross-other@example.test");
        var now = DateTimeOffset.UtcNow;
        var track = new Track { Id = Guid.NewGuid() };
        var source = new ProviderCatalogItem
        {
            Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = "reviewedVid", Revision = 1,
            FactsJson = JsonSerializer.Serialize(new ProviderCatalogFacts
            { Title = state == "source-changed" ? "Unrelated Piece" : "Song",
                Artist = state == "source-changed" ? "Other Performer" : "Artist", DurationSeconds = 180 }),
            VisibilityState = CatalogVisibility.Public, VerificationMethod = CatalogVerificationMethods.YouTubePublicVideo,
            VerifiedAt = now.AddMinutes(-1), RevalidateAfter = now.AddHours(1)
        };
        var catalog = new ProviderCatalogItem
        {
            Id = Guid.NewGuid(), SourceType = "spotify", ExternalId = "matched-recording", Revision = 1,
            FactsJson = JsonSerializer.Serialize(new ProviderCatalogFacts { Title = "Song", Artist = "Artist", DurationSeconds = 180 }),
            VisibilityState = CatalogVisibility.Public, VerificationMethod = CatalogVerificationMethods.SpotifyApplication,
            VerifiedAt = now.AddDays(-2), RevalidateAfter = state == "candidate-expired" ? now.AddDays(-1) : now.AddHours(1)
        };
        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(), OwnerUserId = owner.Id, SourceType = source.SourceType, ExternalId = source.ExternalId,
            TrackId = track.Id, MatchStatus = TrackMatchingStatuses.Matched, ResolutionNotes = "Automatically resolved from stored provider evidence."
        };
        ObservationInput.SetPublic(observation, source);
        var candidate = new TrackResolutionCandidate
        {
            Id = Guid.NewGuid(), TrackObservationId = observation.Id, CandidateSource = catalog.SourceType,
            ExternalId = catalog.ExternalId, ProviderCatalogItemId = catalog.Id, ProviderCatalogRevision = catalog.Revision,
            IsAccepted = true, Score = 1
        };
        db.AddRange(owner, other, track, source, CatalogFixture.Associate(catalog, track.Id), observation, candidate);
        await db.SaveChangesAsync();
        observation.AcceptedCandidateId = candidate.Id;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var response = await RecognizeAsync(db, state == "other-owner" ? other : owner, source.ExternalId);

        Assert.Equal(state == "current" ? "matched" : "pending", response.Status);
        Assert.Equal(state == "current" ? track.Id : (Guid?)null, response.TrackId);
        Assert.Null((await db.ProviderCatalogItems.SingleAsync(row => row.SourceType == "youtube")).TrackId);
    }

    private sealed class RecognitionVideoHandler : HttpMessageHandler, Google.Apis.Http.IHttpClientFactory
    {
        public ConfigurableHttpClient CreateHttpClient(CreateHttpClientArgs args) => new(new ConfigurableMessageHandler(this));
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.EndsWith("/videos", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"items":[{"id":"sourceVid01","snippet":{"title":"Artist - Refreshed song","channelTitle":"Artist","categoryId":"10"},"contentDetails":{"duration":"PT3M"}}]}
                    """, Encoding.UTF8, "application/json")
            });
        }
    }

    private static async Task<RecognizeYouTubeResponse> RecognizeAsync(
        ApplicationDbContext db, User user, string videoId, string? pageTitle = null, YouTubeService? youtubeOverride = null)
    {
        using var manager = new UserManager<User>(
            new UserStore<User, IdentityRole<int>, ApplicationDbContext, int>(db),
            Options.Create(new IdentityOptions()), new PasswordHasher<User>(), [], [],
            new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), null!, NullLogger<UserManager<User>>.Instance);
        var youtube = youtubeOverride ?? new YouTubeService(new ConfigurationBuilder().Build(), db,
            new TokenEncryptionService(new EphemeralDataProtectionProvider()), NullLogger<YouTubeService>.Instance);
        var controller = new MusicRecognitionController(db, youtube, new TrackMatchQueue(db, TimeProvider.System),
            new MusicLibraryQueryService(db), manager)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())], "test"))
                }
            }
        };
        var result = await controller.Recognize(new RecognizeYouTubeRequest(videoId, "youtube_music", pageTitle), default);
        return Assert.IsType<RecognizeYouTubeResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
    }
}
