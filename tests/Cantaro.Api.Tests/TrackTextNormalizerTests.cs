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

    [Theory]
    [InlineData("唱", "唱")]
    [InlineData("残響散歌", "残響散歌")]
    [InlineData("Ado 唱", "ADO 唱")]
    public void CalculateSimilarity_PreservesUnicodeLetters(string left, string right)
    {
        Assert.Equal(1m, TrackTextNormalizer.CalculateSimilarity(left, right));
        Assert.True(TrackTextNormalizer.AreEquivalentTitles(left, right));
    }

    [Theory]
    [InlineData("唱", "新時代")]
    [InlineData("残響散歌", "Ref:rain")]
    [InlineData("Ado 唱", "Ado 新時代")]
    public void CalculateSimilarity_DistinguishesUnicodeTitles(string left, string right)
    {
        Assert.NotEqual(1m, TrackTextNormalizer.CalculateSimilarity(left, right));
        Assert.False(TrackTextNormalizer.AreEquivalentTitles(left, right));
    }
}
