using Cantaro.Api.Controllers;
using Cantaro.Api.Services;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class MusicRecognitionClassificationTests
{
    [Fact]
    public void YouTubeMusic_IsMusicWithoutProviderMetadata() =>
        Assert.Equal("music", MusicRecognitionController.Classify("youtube_music", null));

    [Fact]
    public void RegularYouTube_WithoutMetadata_IsUncertain() =>
        Assert.Equal("uncertain", MusicRecognitionController.Classify("youtube", null));

    [Fact]
    public void RegularYouTube_MusicCategory_IsMusic() =>
        Assert.Equal("music", MusicRecognitionController.Classify("youtube", new YouTubeVideoMetadataDto
        {
            VideoId = "abcdefghijk", Title = "Song", CategoryId = "10"
        }));

    [Fact]
    public void RegularYouTube_NonMusicCategory_IsIgnored() =>
        Assert.Equal("not_music", MusicRecognitionController.Classify("youtube", new YouTubeVideoMetadataDto
        {
            VideoId = "abcdefghijk", Title = "Video", CategoryId = "22"
        }));
}
