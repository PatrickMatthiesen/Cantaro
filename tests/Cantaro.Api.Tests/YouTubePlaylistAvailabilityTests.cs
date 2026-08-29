using Cantaro.Api.Services;
using Google.Apis.YouTube.v3.Data;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class YouTubePlaylistAvailabilityTests
{
    [Fact]
    public void MapAvailablePlaylistItem_ReturnsOrdinaryVideo()
    {
        var result = YouTubeService.MapAvailablePlaylistItem(
            MakeItem("video-1", position: 4),
            new Dictionary<string, int?> { ["video-1"] = 213 });

        Assert.Null(result.SkipReason);
        Assert.NotNull(result.Item);
        Assert.Equal("video-1", result.Item.VideoId);
        Assert.Equal("Ordinary song", result.Item.Title);
        Assert.Equal(4, result.Item.Position);
        Assert.Equal(213, result.Item.DurationSeconds);
    }

    [Fact]
    public void MapAvailablePlaylistItem_SkipsVideoMissingFromVideoLookup()
    {
        var result = YouTubeService.MapAvailablePlaylistItem(
            MakeItem("deleted-video", position: 2, title: "Deleted video"),
            new Dictionary<string, int?>());

        Assert.Null(result.Item);
        Assert.Equal("provider_unavailable", result.SkipReason);
        Assert.Equal(2, result.ProviderPosition);
    }

    [Fact]
    public void MapAvailablePlaylistItem_SkipsPrivatePlaylistItemEvenWhenVideoWasReturned()
    {
        var result = YouTubeService.MapAvailablePlaylistItem(
            MakeItem("private-video", position: 1, privacyStatus: "private"),
            new Dictionary<string, int?> { ["private-video"] = 180 });

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
            new Dictionary<string, int?> { ["video-1"] = 180 });

        Assert.Null(result.Item);
        Assert.Equal(expectedReason, result.SkipReason);
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
