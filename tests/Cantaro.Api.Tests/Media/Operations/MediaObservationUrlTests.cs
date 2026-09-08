using Cantaro.Api.Services;
using Xunit;

namespace Cantaro.Api.Tests;

public class MediaObservationUrlTests
{
    [Theory]
    [InlineData("https://user:secret@www.crunchyroll.com/watch/EP1?token=secret#secret", "https://www.crunchyroll.com/watch/EP1")]
    [InlineData("https://www.crunchyroll.com/watch/EP1#secret?token=secret", "https://www.crunchyroll.com/watch/EP1")]
    [InlineData(" /watch/EP1?token=secret#secret ", "/watch/EP1")]
    [InlineData("not a valid URL?token=secret", "not a valid URL")]
    public void RemovesPrivateUrlComponentsBeforePersistence(string input, string expected)
    {
        Assert.Equal(expected, MediaDestinationUrlPolicy.NormalizeObservedUrl(input));
    }
}
