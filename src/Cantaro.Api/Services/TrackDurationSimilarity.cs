using Cantaro.Api.Configuration;

namespace Cantaro.Api.Services;

public static class TrackDurationSimilarity
{
    public static decimal Calculate(int observationSeconds, int candidateSeconds, TrackMatchingOptions options)
    {
        var signedDifference = observationSeconds - candidateSeconds;
        var absoluteDifference = Math.Abs(signedDifference);
        if (absoluteDifference <= options.DurationExactToleranceSeconds)
        {
            return 1m;
        }

        var halfLifeSeconds = signedDifference >= 0
            ? options.DurationLongerHalfLifeSeconds
            : options.DurationShorterHalfLifeSeconds;
        var scaledDifference = absoluteDifference / halfLifeSeconds;
        var similarity = Math.Pow(0.5d, scaledDifference * scaledDifference);
        return Math.Round((decimal)similarity, 3, MidpointRounding.AwayFromZero);
    }
}
