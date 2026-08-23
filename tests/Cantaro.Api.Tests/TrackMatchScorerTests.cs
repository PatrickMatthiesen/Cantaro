using Cantaro.Api.Configuration;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Xunit;

namespace Cantaro.Api.Tests;

public class TrackMatchScorerTests
{
    [Fact]
    public void Score_UsesBoundedYouTubePaddingForExactIdentity()
    {
        var result = Score(209, 178, "Rescue Me");

        Assert.InRange(result.DurationScore, 0.92m, 0.94m);
        Assert.True(result.IsAutoMatchEligible);
    }

    [Theory]
    [InlineData(260, 200, true)]
    [InlineData(261, 200, false)]
    public void Score_BoundsUnmarkedYouTubePaddingByDifference(
        int observationSeconds,
        int candidateSeconds,
        bool expectedEligibility)
    {
        var result = Score(observationSeconds, candidateSeconds, "Rescue Me");

        Assert.Equal(expectedEligibility, result.IsAutoMatchEligible);
    }

    [Theory]
    [InlineData(270, 200, true)]
    [InlineData(271, 200, false)]
    public void Score_BoundsUnmarkedYouTubePaddingByRatio(
        int observationSeconds,
        int candidateSeconds,
        bool expectedEligibility)
    {
        var result = Score(
            observationSeconds,
            candidateSeconds,
            "Rescue Me",
            new TrackMatchingOptions { YouTubeUnmarkedPaddingMaxSeconds = 100 });

        Assert.Equal(expectedEligibility, result.IsAutoMatchEligible);
    }

    [Fact]
    public void Score_DoesNotApplySourcePaddingAcrossVersionContext()
    {
        var result = Score(209, 178, "Rescue Me (from One Night in Malibu)");

        Assert.InRange(result.DurationScore, 0.01m, 0.03m);
        Assert.Equal(-0.15m, result.SemanticAdjustment);
        Assert.False(result.IsAutoMatchEligible);
    }

    private static TrackMatchScoredCandidate Score(
        int observationSeconds,
        int candidateSeconds,
        string candidateTitle,
        TrackMatchingOptions? options = null)
    {
        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(),
            SourceType = "youtube",
            ExternalId = "video",
            Title = "Rescue Me",
            Artist = "OneRepublic",
            DurationSeconds = observationSeconds,
            MatchStatus = TrackMatchingStatuses.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var candidate = new TrackMatchSearchCandidate
        {
            CandidateSource = "musicbrainz",
            ExternalId = "recording",
            Title = candidateTitle,
            Artist = "OneRepublic",
            ArtistCredits = ["OneRepublic"],
            DurationSeconds = candidateSeconds
        };

        return TrackMatchScorer.Score(observation, candidate, options ?? new TrackMatchingOptions());
    }
}
