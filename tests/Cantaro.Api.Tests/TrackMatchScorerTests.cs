using Cantaro.Api.Configuration;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Xunit;

namespace Cantaro.Api.Tests;

public class TrackMatchScorerTests
{
    [Fact]
    public void Score_DoesNotInventLiveSemanticMismatchFromBaseTitle()
    {
        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(),
            SourceType = "youtube",
            ExternalId = "meant-to-live-video",
            Title = "Meant To Live (Jon Bellion Version)",
            Artist = "Jon Bellion",
            DurationSeconds = 205,
            MatchStatus = TrackMatchingStatuses.Pending
        };
        var candidate = new TrackMatchSearchCandidate
        {
            CandidateSource = "musicbrainz",
            ExternalId = "meant-to-live-recording",
            Title = "Meant To Live (Jon Bellion Version)",
            Artist = "Jon Bellion",
            ArtistCredits = ["Jon Bellion"],
            DurationSeconds = 205
        };

        var result = TrackMatchScorer.Score(observation, candidate, new TrackMatchingOptions());

        Assert.DoesNotContain("live", result.ObservationMetadata.VersionMarkers);
        Assert.DoesNotContain("live", result.CandidateMetadata.VersionMarkers);
        Assert.True(result.HasCompatibleSemantics);
        Assert.Equal(0m, result.SemanticAdjustment);
    }

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

    [Fact]
    public void Score_AllowsExactTitlePrimaryArtistToExpandToStronglyIdentifiedFullCredit()
    {
        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(),
            SourceType = "youtube",
            ExternalId = "so-far-away-video",
            Title = "So Far Away",
            Artist = "Seven Lions",
            DurationSeconds = 249,
            MatchStatus = TrackMatchingStatuses.Pending
        };
        var candidate = new TrackMatchSearchCandidate
        {
            CandidateSource = "spotify",
            ExternalId = "spotify-track",
            Title = "So Far Away",
            Artist = "Seven Lions, Lilly Ahlberg",
            ArtistCredits = ["Seven Lions", "Lilly Ahlberg"],
            Isrc = "CA5KR2593426",
            DurationSeconds = 248
        };

        var result = TrackMatchScorer.Score(observation, candidate, new TrackMatchingOptions());

        Assert.True(result.IsAutoMatchEligible);
        Assert.Equal(0.97m, result.Score);
    }

    [Fact]
    public void Score_DoesNotExpandCreditsWithoutAStableRecordingIdentifier()
    {
        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = "video",
            Title = "Shared Song", Artist = "Artist", DurationSeconds = 200,
            MatchStatus = TrackMatchingStatuses.Pending
        };
        var candidate = new TrackMatchSearchCandidate
        {
            CandidateSource = "musicbrainz", ExternalId = "candidate",
            Title = "Shared Song", Artist = "Artist, Guest",
            ArtistCredits = ["Artist", "Guest"], DurationSeconds = 200
        };

        Assert.False(TrackMatchScorer.Score(observation, candidate, new TrackMatchingOptions()).IsAutoMatchEligible);
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
