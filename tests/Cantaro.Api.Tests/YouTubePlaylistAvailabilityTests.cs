using Cantaro.Api.Services;
using Google.Apis.YouTube.v3.Data;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class YouTubePlaylistAvailabilityTests
{
    [Theory]
    [InlineData(false, null, "public", true)]
    [InlineData(true, "private", "public", true)]
    [InlineData(true, "public", "private", true)]
    [InlineData(true, "public", "public", false)]
    public void IsPlaylistItemUnavailable_OnlyCountsItemsCantaroCannotRead(
        bool videoMetadataFound, string? videoPrivacy, string? itemPrivacy, bool expected)
    {
        Assert.Equal(expected, YouTubeService.IsPlaylistItemUnavailable(
            videoMetadataFound, videoPrivacy, itemPrivacy));
    }

    [Fact]
    public void UnavailableVideoCanStillBePartOfAStructurallyCompleteSnapshot()
    {
        Assert.True(YouTubeService.IsPlaylistItemUnavailable(false, null, "public"));
        Assert.True(YouTubeService.IsPlaylistItemStructurallyReadable(7, 7, "playlist-item-7", "Unavailable video"));
    }

    [Fact]
    public void PlaylistItemWithBrokenPositionRemainsStructurallyIncomplete()
    {
        Assert.False(YouTubeService.IsPlaylistItemStructurallyReadable(8, 7, "playlist-item-8", "Song"));
    }

    [Fact]
    public void MapUnavailableItemPreservesKnownLocalIdentityAndMarksRemoteItemUnavailable()
    {
        var trackId = Guid.NewGuid();
        var observationId = Guid.NewGuid();
        var item = new YouTubeService.SyncReadItem("video-1", "playlist-item-1", "Song", "Artist",
            null, null, 0, false);

        var mapped = YouTubePlaylistSyncProvider.MapUnavailableItem(item, trackId, observationId);

        Assert.Equal(trackId, mapped.TrackId);
        Assert.Equal(observationId, mapped.ObservationId);
        Assert.False(mapped.IsAvailable);
        Assert.Equal("video-1", mapped.ExternalId);
    }

    [Fact]
    public void MapUnknownUnavailableItemDoesNotInventLocalIdentity()
    {
        var item = new YouTubeService.SyncReadItem("!unavailable:playlist-item-1", "playlist-item-1",
            "Unavailable video", null, null, null, 0, false);

        var mapped = YouTubePlaylistSyncProvider.MapUnavailableItem(item, null, null);

        Assert.Null(mapped.TrackId);
        Assert.Null(mapped.ObservationId);
        Assert.False(mapped.IsAvailable);
    }

    [Fact]
    public void MapAvailablePlaylistItem_ReturnsOrdinaryVideo()
    {
        var result = YouTubeService.MapAvailablePlaylistItem(
            MakeItem("video-1", position: 4),
            new Dictionary<string, YouTubeService.AvailableVideoMetadata>
            {
                ["video-1"] = new(213, "Song credits from the video description")
            });

        Assert.Null(result.SkipReason);
        Assert.NotNull(result.Item);
        Assert.Equal("video-1", result.Item.VideoId);
        Assert.Equal("Ordinary song", result.Item.Title);
        Assert.Equal(4, result.Item.Position);
        Assert.Equal(213, result.Item.DurationSeconds);
        Assert.Equal("Song credits from the video description", result.Item.Description);
    }

    [Fact]
    public void MapAvailablePlaylistItem_SkipsVideoMissingFromVideoLookup()
    {
        var result = YouTubeService.MapAvailablePlaylistItem(
            MakeItem("deleted-video", position: 2, title: "Deleted video"),
            new Dictionary<string, YouTubeService.AvailableVideoMetadata>());

        Assert.Null(result.Item);
        Assert.Equal("provider_unavailable", result.SkipReason);
        Assert.Equal(2, result.ProviderPosition);
    }

    [Fact]
    public void MapAvailablePlaylistItem_SkipsPrivatePlaylistItemEvenWhenVideoWasReturned()
    {
        var result = YouTubeService.MapAvailablePlaylistItem(
            MakeItem("private-video", position: 1, privacyStatus: "private"),
            new Dictionary<string, YouTubeService.AvailableVideoMetadata>
            {
                ["private-video"] = new(180, "")
            });

        Assert.Null(result.Item);
        Assert.Equal("private", result.SkipReason);
    }

    [Theory]
    [InlineData(true, "missing_snippet")]
    [InlineData(false, "missing_video_id")]
    public void MapAvailablePlaylistItem_SkipsMalformedRows(bool omitSnippet, string expectedReason)
    {
        var item = MakeItem("video-1", position: 0);
        if (omitSnippet)
        {
            item.Snippet = null;
        }
        else
        {
            item.ContentDetails = null;
        }

        var result = YouTubeService.MapAvailablePlaylistItem(
            item,
            new Dictionary<string, YouTubeService.AvailableVideoMetadata>
            {
                ["video-1"] = new(180, null)
            });

        Assert.Null(result.Item);
        Assert.Equal(expectedReason, result.SkipReason);
    }

    [Fact]
    public void GetPreservedDescription_KeepsPreviouslyStoredDescriptionWhenCurrentReadOmitsIt()
    {
        const string rawMetadata = """{"Description":"Previously captured song credits"}""";

        var description = YouTubePlaylistSyncProvider.GetPreservedDescription(null, rawMetadata);

        Assert.Equal("Previously captured song credits", description);
    }

    [Fact]
    public void GetPreservedDescription_PrefersDescriptionFromCurrentRead()
    {
        var description = YouTubePlaylistSyncProvider.GetPreservedDescription(
            "Current song credits", """{"Description":"Old description"}""");

        Assert.Equal("Current song credits", description);
    }

    [Fact]
    public void LegacyImport_PreservesDescriptionWhenRefreshedReadOmitsIt()
    {
        const string previousRawMetadata = """{"Description":"Previously captured song credits"}""";
        const string currentRawMetadata = """{"OriginalTitle":"Video title"}""";

        var preserved = YouTubePlaylistSyncService.PreserveExistingDescription(
            currentRawMetadata, previousRawMetadata);

        Assert.Contains("Previously captured song credits", preserved);
    }

    [Fact]
    public void CompactPlaylistPositions_RemovesUnavailableGapsDeterministically()
    {
        var items = new[]
        {
            MakeDto("third", 7),
            MakeDto("first", 1),
            MakeDto("second", 4)
        };

        var result = YouTubeService.CompactPlaylistPositions(items);

        Assert.Collection(
            result,
            item => { Assert.Equal("first", item.VideoId); Assert.Equal(0, item.Position); },
            item => { Assert.Equal("second", item.VideoId); Assert.Equal(1, item.Position); },
            item => { Assert.Equal("third", item.VideoId); Assert.Equal(2, item.Position); });
    }

    private static PlaylistItem MakeItem(
        string videoId,
        long position,
        string title = "Ordinary song",
        string privacyStatus = "public") => new()
    {
        ContentDetails = new PlaylistItemContentDetails { VideoId = videoId },
        Snippet = new PlaylistItemSnippet
        {
            Position = position,
            Title = title,
            VideoOwnerChannelTitle = "Artist"
        },
        Status = new PlaylistItemStatus { PrivacyStatus = privacyStatus }
    };

    private static YouTubePlaylistItemDto MakeDto(string videoId, int position) => new()
    {
        VideoId = videoId,
        Title = videoId,
        Position = position
    };
}
