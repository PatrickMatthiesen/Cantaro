using Cantaro.Api.Configuration;
using Cantaro.Api.Services;
using Xunit;

namespace Cantaro.Api.Tests;

public class TrackDurationSimilarityTests
{
    private static readonly TrackMatchingOptions Options = new();

    [Theory]
    [InlineData(187, 174, 0.50)]
    [InlineData(187, 182, 0.90)]
    [InlineData(187, 167, 0.19)]
    public void Calculate_IsForgivingWhenObservationIsLonger(
        int observationSeconds,
        int candidateSeconds,
        decimal expected)
    {
        var score = TrackDurationSimilarity.Calculate(observationSeconds, candidateSeconds, Options);

        Assert.InRange(score, expected - 0.01m, expected + 0.01m);
    }

    [Theory]
    [InlineData(266, 270, 0.96)]
    [InlineData(182, 187, 0.90)]
    [InlineData(179, 187, 0.50)]
    [InlineData(177, 187, 0.18)]
    public void Calculate_IsForgivingOfSmallDifferencesWhenObservationIsShorter(
        int observationSeconds,
        int candidateSeconds,
        decimal expected)
    {
        var score = TrackDurationSimilarity.Calculate(observationSeconds, candidateSeconds, Options);

        Assert.InRange(score, expected - 0.01m, expected + 0.01m);
    }

    [Theory]
    [InlineData(187, 187)]
    [InlineData(187, 185)]
    [InlineData(185, 187)]
    public void Calculate_TreatsRoundingToleranceAsExact(int observationSeconds, int candidateSeconds)
    {
        Assert.Equal(1m, TrackDurationSimilarity.Calculate(observationSeconds, candidateSeconds, Options));
    }

    [Theory]
    [InlineData(209, 178, 0.93)]
    [InlineData(209, 160, 0.62)]
    public void CalculateWithObservationPadding_AllowsForNonMusicVideoSegments(
        int observationSeconds,
        int candidateSeconds,
        decimal expected)
    {
        var score = TrackDurationSimilarity.CalculateWithObservationPadding(
            observationSeconds,
            candidateSeconds,
            Options);

        Assert.InRange(score, expected - 0.01m, expected + 0.01m);
    }
}
