using System.Security.Claims;
using System.Text.Json;
using Cantaro.Api.Controllers;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class MusicRecognitionProvenanceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recognize_MatchedSongOnlyReturnsRequesterOwnedObservationIdentities(bool useGlobalLookup)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var requester = TestUserFactory.Create(735, "recognition-requester@example.com");
        var other = TestUserFactory.Create(736, "recognition-other@example.com");
        var playlist = new Playlist { Id = Guid.NewGuid(), UserId = requester.Id, Name = "Saved" };
        var privatePlaylist = new Playlist { Id = Guid.NewGuid(), UserId = other.Id, Name = "Private" };
        var track = new Track
        {
            Id = Guid.NewGuid(), Isrc = "PUBLICISRC",
            CanonicalMetadata = JsonSerializer.Serialize(new TrackCanonicalMetadata
            {
                ThumbnailUrl = "https://i.ytimg.com/vi/privateVid1/mqdefault.jpg"
            })
        };
        var privateObservation = new TrackObservation
        {
            Id = Guid.NewGuid(), TrackId = track.Id, SourceType = "youtube", ExternalId = "privateVid1",
            Title = "Private video", ThumbnailUrl = "https://i.ytimg.com/vi/privateVid1/mqdefault.jpg", MatchStatus = TrackMatchingStatuses.Matched
        };
        var ownedObservation = new TrackObservation
        {
            Id = Guid.NewGuid(), TrackId = track.Id, SourceType = "youtube", ExternalId = "ownedVideo1",
            Title = "Owned video", ThumbnailUrl = "https://i.ytimg.com/vi/ownedVideo1/mqdefault.jpg", MatchStatus = TrackMatchingStatuses.Matched
        };
        if (useGlobalLookup)
            track.SourceIds.Add(new TrackSourceId
            {
                Id = Guid.NewGuid(), TrackId = track.Id, SourceType = "youtube", ExternalId = privateObservation.ExternalId
            });
        track.SourceIds.Add(new TrackSourceId
        {
            Id = Guid.NewGuid(), TrackId = track.Id, SourceType = "spotify", ExternalId = "other-users-spotify-id"
        });
        db.AddRange(requester, other, playlist, privatePlaylist, track, privateObservation, ownedObservation);
        db.PlaylistEntries.Add(new PlaylistEntry
        {
            Id = Guid.NewGuid(), PlaylistId = privatePlaylist.Id, TrackId = track.Id,
            TrackObservationId = privateObservation.Id, Position = 0
        });
        await db.SaveChangesAsync();
        var library = new MusicLibraryQueryService(db);
        Assert.True(await library.AddCanonicalSongToPlaylistAsync(track.Id, playlist.Id, requester.Id, default));
        db.ChangeTracker.Clear();
        using var manager = new UserManager<User>(
            new UserStore<User, IdentityRole<int>, ApplicationDbContext, int>(db),
            Options.Create(new IdentityOptions()), new PasswordHasher<User>(), [], [],
            new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), null!, NullLogger<UserManager<User>>.Instance);
        // Both matched lookup branches return before needing provider metadata or queueing.
        var controller = new MusicRecognitionController(db, null!, null!, library, manager)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, requester.Id.ToString())], "test"))
                }
            }
        };
        var request = new RecognizeYouTubeRequest(privateObservation.ExternalId, "youtube", null);

        var canonicalOnly = await RecognizeSongAsync();
        Assert.Null(canonicalOnly.ThumbnailUrl);
        Assert.Equal(new[] { ("isrc", track.Isrc!) },
            canonicalOnly.SourceIdentities.Select(identity => (identity.Source, identity.ExternalId)));
        Assert.Equal(["isrc"], canonicalOnly.SourcePlatforms);
        Assert.Empty(canonicalOnly.PlatformLinks);

        var ownedEntry = new PlaylistEntry
        {
            Id = Guid.NewGuid(), PlaylistId = playlist.Id, TrackId = track.Id,
            TrackObservationId = ownedObservation.Id, Position = 1
        };
        db.PlaylistEntries.Add(ownedEntry);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var withObservation = await RecognizeSongAsync();
        Assert.Equal(ownedObservation.ThumbnailUrl, withObservation.ThumbnailUrl);
        Assert.Equal(new[] { ("isrc", track.Isrc!), ("youtube", ownedObservation.ExternalId) },
            withObservation.SourceIdentities.Select(identity => (identity.Source, identity.ExternalId)));
        Assert.Equal(["isrc", "youtube"], withObservation.SourcePlatforms);
        Assert.Equal(2, withObservation.PlatformLinks.Count);
        Assert.All(withObservation.PlatformLinks, link => Assert.Equal(ownedObservation.ExternalId, link.ExternalId));

        Assert.True(await library.RemoveCanonicalSongFromPlaylistAsync(track.Id, playlist.Id, requester.Id, default, ownedEntry.Id));
        db.ChangeTracker.Clear();
        var afterRemove = await RecognizeSongAsync();
        Assert.Null(afterRemove.ThumbnailUrl);
        Assert.Equal(new[] { ("isrc", track.Isrc!) },
            afterRemove.SourceIdentities.Select(identity => (identity.Source, identity.ExternalId)));
        Assert.Empty(afterRemove.PlatformLinks);
        Assert.Equal(["isrc"], afterRemove.SourcePlatforms);
        Assert.Equal(useGlobalLookup ? 2 : 1, await db.TrackSourceIds.CountAsync());

        async Task<MusicLibrarySongDto> RecognizeSongAsync()
        {
            var result = await controller.Recognize(request, default);
            var response = Assert.IsType<RecognizeYouTubeResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
            Assert.Equal("matched", response.Status);
            Assert.Equal(track.Id, response.TrackId);
            Assert.True(response.InUserLibrary);
            return Assert.IsType<MusicLibrarySongDto>(response.Song);
        }
    }
}
