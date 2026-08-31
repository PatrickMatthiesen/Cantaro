using Cantaro.Api.Services;
using Xunit;

namespace Cantaro.Api.Tests;

public class TrackMatchClustererTests
{
    [Fact]
    public void BuildClusters_AttachesMissingDurationToKnownIdentityWithoutBridgingKnownDurations()
    {
        var longer = Candidate("longer", "Rescue Me", 178, 0.95m);
        var missing = Candidate("missing", "Rescue Me", null, 0.95m);
        var shorter = Candidate("shorter", "Rescue Me", 160, 0.80m);

        var clusters = TrackMatchClusterer.BuildClusters([missing, longer, shorter], 8);

        Assert.Equal(2, clusters.Count);
        Assert.Equal("longer", clusters[0].Representative.Candidate.ExternalId);
        Assert.Contains(clusters[0].Members, member => member.Candidate.ExternalId == "missing");
        Assert.Equal("normalized-title-artist-missing-duration", clusters[0].ClusterReason);
        Assert.Equal("shorter", clusters[1].Representative.Candidate.ExternalId);
    }

    [Fact]
    public void BuildClusters_DoesNotAttachMissingDurationAcrossVersionSemantics()
    {
        var plain = Candidate("plain", "Rescue Me", 160, 0.95m);
        var missingVersion = Candidate(
            "version",
            "Rescue Me (from One Night in Malibu)",
            null,
            0.70m);

        var clusters = TrackMatchClusterer.BuildClusters([plain, missingVersion], 8);

        Assert.Equal(2, clusters.Count);
    }

    [Fact]
    public void BuildClusters_DoesNotMergeCandidatesWhenBothDurationsAreMissing()
    {
        var first = Candidate("first", "Rescue Me", null, 0.85m);
        var second = Candidate("second", "Rescue Me", null, 0.85m);

        var clusters = TrackMatchClusterer.BuildClusters([first, second], 8);

        Assert.Equal(2, clusters.Count);
    }

    [Fact]
    public void BuildClusters_MergesMatchingJapaneseTitlesButKeepsDistinctTitlesSeparate()
    {
        var first = Candidate("first", "残響散歌", 181, 0.95m, "Aimer");
        var same = Candidate("same", "残響散歌", 183, 0.90m, "Aimer");
        var distinct = Candidate("distinct", "カタオモイ", 182, 0.85m, "Aimer");

        var clusters = TrackMatchClusterer.BuildClusters([first, same, distinct], 8);

        Assert.Equal(2, clusters.Count);
        Assert.Equal(2, clusters[0].Members.Count);
        Assert.Single(clusters[1].Members);
    }

    private static TrackMatchScoredCandidate Candidate(
        string externalId,
        string title,
        int? durationSeconds,
        decimal score,
        string artist = "OneRepublic")
    {
        var observationMetadata = TrackMetadataParser.Parse(title, artist);
        var candidateMetadata = TrackMetadataParser.Parse(title, artist);
        return new TrackMatchScoredCandidate
        {
            Candidate = new TrackMatchSearchCandidate
            {
                CandidateSource = "musicbrainz",
                ExternalId = externalId,
                Title = title,
                Artist = artist,
                ArtistCredits = [artist],
                DurationSeconds = durationSeconds
            },
            ObservationTitle = "Rescue Me",
            ObservationArtist = artist,
            ObservationMetadata = observationMetadata,
            CandidateMetadata = candidateMetadata,
            TitleSimilarity = 1m,
            ArtistSimilarity = 1m,
            DurationScore = 0m,
            SemanticAdjustment = 0m,
            SemanticExplanation = string.Empty,
            Score = score,
            IsAutoMatchEligible = false,
            AutoMatchEligibilityReason = string.Empty
        };
    }
}
