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
        var exponent = signedDifference >= 0
            ? scaledDifference * scaledDifference
            : Math.Pow(scaledDifference, 4d);
        var similarity = Math.Pow(0.5d, exponent);
        return Math.Round((decimal)similarity, 3, MidpointRounding.AwayFromZero);
    }
}
