using Cantaro.Api.Services;
using Xunit;

namespace Cantaro.Api.Tests;

public class TrackTextNormalizerTests
{
    [Theory]
    [InlineData("Boohoo", "BOO HOO")]
    [InlineData("Starfall", "Star Fall")]
    public void CalculateSimilarity_TreatsInternalSpacingAsEquivalent(string left, string right)
    {
        Assert.Equal(1m, TrackTextNormalizer.CalculateSimilarity(left, right));
        Assert.True(TrackTextNormalizer.AreEquivalentTitles(left, right));
    }

    [Fact]
    public void CalculateSimilarity_DoesNotCollapseDifferentMultiwordTitles()
    {
        Assert.NotEqual(1m, TrackTextNormalizer.CalculateSimilarity("A New Day", "An Ewday"));
    }
}
