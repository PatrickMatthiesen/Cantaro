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
    public void Score_UsesDurationForDiagnosticsWithoutBlockingExactIdentity()
    {
        var result = Score(209, 178, "Rescue Me");

        Assert.InRange(result.DurationScore, 0.01m, 0.03m);
        Assert.True(result.IsAutoMatchEligible);
        Assert.Equal(209, result.ObservationDurationSeconds);
        Assert.Equal(31, result.DurationDifferenceSeconds);
        Assert.Equal(178, result.Candidate.DurationSeconds);
    }

    [Theory]
    [InlineData(260, 200)]
    [InlineData(261, 200)]
    [InlineData(1200, 200)]
    public void Score_AcceptsExactIdentityRegardlessOfDurationDifference(
        int observationSeconds,
        int candidateSeconds)
    {
        var result = Score(observationSeconds, candidateSeconds, "Rescue Me");

        Assert.True(result.IsAutoMatchEligible);
        Assert.Equal(observationSeconds, result.ObservationDurationSeconds);
        Assert.Equal(Math.Abs(observationSeconds - candidateSeconds), result.DurationDifferenceSeconds);
        Assert.Equal(candidateSeconds, result.Candidate.DurationSeconds);
    }

    [Theory]
    [InlineData(270, 200)]
    [InlineData(271, 200)]
    [InlineData(1200, 200)]
    public void Score_AcceptsExactIdentityEvenWhenDurationExceedsConfiguredPadding(
        int observationSeconds,
        int candidateSeconds)
    {
        var result = Score(observationSeconds, candidateSeconds, "Rescue Me");

        Assert.True(result.IsAutoMatchEligible);
        Assert.Equal(observationSeconds, result.ObservationDurationSeconds);
        Assert.Equal(Math.Abs(observationSeconds - candidateSeconds), result.DurationDifferenceSeconds);
        Assert.Equal(candidateSeconds, result.Candidate.DurationSeconds);
    }

    [Fact]
    public void Score_AcceptsExactIdentityWhenDurationIsMissing()
    {
        var observation = new TrackObservation
        {
            SourceType = "youtube", ExternalId = "video", Title = "Rescue Me",
            Artist = "OneRepublic", MatchStatus = TrackMatchingStatuses.Pending
        };
        var candidate = new TrackMatchSearchCandidate
        {
            CandidateSource = "musicbrainz", ExternalId = "recording", Title = "Rescue Me",
            Artist = "OneRepublic", ArtistCredits = ["OneRepublic"]
        };

        var result = TrackMatchScorer.Score(observation, candidate, new TrackMatchingOptions());

        Assert.True(result.IsAutoMatchEligible);
        Assert.Equal(1m, result.Score);
        Assert.Null(result.ObservationDurationSeconds);
        Assert.Null(result.DurationDifferenceSeconds);
        Assert.Null(result.Candidate.DurationSeconds);
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
    public void Score_AcceptsEquivalentParentheticalAndDashSourceContext()
    {
        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = "nobody-video",
            Title = "Nobody (from Kaiju No. 8)", Artist = "OneRepublic", DurationSeconds = 154,
            MatchStatus = TrackMatchingStatuses.Pending
        };
        var candidate = new TrackMatchSearchCandidate
        {
            CandidateSource = "spotify", ExternalId = "spotify-nobody",
            Title = "Nobody - from Kaiju No. 8", Artist = "OneRepublic",
            ArtistCredits = ["OneRepublic"], Isrc = "USUM72403147", DurationSeconds = 153
        };

        var result = TrackMatchScorer.Score(observation, candidate, new TrackMatchingOptions());

        Assert.True(result.HasCompatibleSemantics);
        Assert.Equal(0.05m, result.SemanticAdjustment);
        Assert.True(result.IsAutoMatchEligible);
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
        Assert.Equal(0.965m, result.Score);
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

    [Theory]
    [InlineData("Kx5, Hayla", "Kx5, deadmau5, Kaskade, HAYLA", 241, 240, true)]
    [InlineData("Kx5, Hayla", "Kx5, deadmau5, Different Singer", 241, 240, false)]
    [InlineData("Kx5, Hayla", "Kx5, deadmau5, Kaskade, HAYLA", 180, 240, true)]
    [InlineData("K/DA, Madison Beer, (G)I-DLE, Jaira Burns", "K/DA, Madison Beer, i-dle, Jaira Burns, League of Legends", 203, 191, true)]
    [InlineData("(G)I-DLE", "Different Band", 203, 191, false)]
    [InlineData("K/DA, Madison Beer, (G)I-DLE, Jaira Burns", "K/DA, Madison Beer, Different Band, Jaira Burns", 203, 191, false)]
    public void Score_RequiresCorroboratedCreditsForCatalogExpansion(
        string artist, string candidateArtist, int duration, int candidateDuration, bool expected)
    {
        var observation = new TrackObservation
        {
            SourceType = "youtube", ExternalId = "video", Title = "Same Song",
            Artist = artist, DurationSeconds = duration, MatchStatus = TrackMatchingStatuses.Pending
        };
        var candidate = new TrackMatchSearchCandidate
        {
            CandidateSource = "spotify", ExternalId = "track", Title = "Same Song",
            Artist = candidateArtist, ArtistCredits = candidateArtist.Split(", "),
            Isrc = "USABC2400001", DurationSeconds = candidateDuration
        };
        Assert.Equal(expected, TrackMatchScorer.Score(observation, candidate, new TrackMatchingOptions()).IsAutoMatchEligible);
    }

    [Theory]
    [InlineData("Same Song (Remix)")]
    [InlineData("Same Song (Sped Up)")]
    [InlineData("Same Song (Live)")]
    public void Score_CreditExpansionDoesNotOverrideVersionMismatch(string title)
    {
        var observation = new TrackObservation
        {
            SourceType = "youtube", ExternalId = "video", Title = title,
            Artist = "Artist, Guest", DurationSeconds = 203, MatchStatus = TrackMatchingStatuses.Pending
        };
        var candidate = new TrackMatchSearchCandidate
        {
            CandidateSource = "spotify", ExternalId = "track", Title = "Same Song",
            Artist = "Artist, Guest, Additional Credit", ArtistCredits = ["Artist", "Guest", "Additional Credit"],
            Isrc = "USABC2400001", DurationSeconds = 191
        };
        Assert.False(TrackMatchScorer.Score(observation, candidate, new TrackMatchingOptions()).IsAutoMatchEligible);
    }

    [Theory]
    [InlineData("Stay Gold - from BEYBLADE X", true)]
    [InlineData("Stay Gold - from BEYBLADE X / Jax Jones Y2J! Remix", false)]
    public void Score_RecognizesJapaneseSoundtrackContextWithoutDroppingVersionDifferences(string title, bool expected)
    {
        var observation = new TrackObservation
        {
            SourceType = "youtube", ExternalId = "video",
            Title = "【Jax Jones × Ado】Stay Gold〈『BEYBLADE X』アニメMV 〉",
            Artist = "UNIVERSAL MUSIC JAPAN", DurationSeconds = 212, MatchStatus = TrackMatchingStatuses.Pending
        };
        var candidate = new TrackMatchSearchCandidate
        {
            CandidateSource = "spotify", ExternalId = "track", Title = title,
            Artist = "Jax Jones, Ado", ArtistCredits = ["Jax Jones", "Ado"],
            Isrc = "GBUM72502705", DurationSeconds = 206
        };
        var result = TrackMatchScorer.Score(observation, candidate, new TrackMatchingOptions());
        Assert.Equal(expected, result.IsAutoMatchEligible);
        if (expected) Assert.True(result.Score >= new TrackMatchingOptions().AutoMatchThreshold);
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
