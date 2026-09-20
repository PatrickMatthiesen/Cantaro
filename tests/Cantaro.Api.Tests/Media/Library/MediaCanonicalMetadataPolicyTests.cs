using Cantaro.Api.Services;
using Xunit;

namespace Cantaro.Api.Tests;

public class MediaCanonicalMetadataPolicyTests
{
    [Theory]
    [InlineData("simkl", "anilist", false)]
    [InlineData("myanimelist", "anilist", false)]
    [InlineData("simkl", "simkl", true)]
    [InlineData("anilist", "simkl", true)]
    public void ImportsPreserveAniListMetadataWhenTitlesAreLinked(string incoming, string existing, bool expected)
        => Assert.Equal(expected, MediaCanonicalMetadataPolicy.ShouldApply(incoming, [existing]));
}
