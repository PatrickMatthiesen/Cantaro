using Cantaro.Api.Configuration;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Xunit;

namespace Cantaro.Api.Tests;

public class TrackMatchIdentityFamilyResolverTests
{
    [Theory]
    [InlineData("Youngblood", "5 Seconds of Summer", 230, 230, 220)]
    [InlineData("Breakeven", "The Script", 256, 255, 241)]
    [InlineData("Strangers", "Sigrid", 245, 249, 234)]
    [InlineData("Darkside", "Alan Walker, Au/Ra, Tomine Harket", 240, 212, 203)]
    [InlineData("Beautiful Now", "Zedd, Jon Bellion", 253, 225, 211)]
    [InlineData("Desire", "Calvin Harris, Sam Smith", 178, 179, 151)]
    public void BuildFamilies_SelectsClosestDurationWithinEquivalentIdentity(
        string title,
        string artist,
        int observationDuration,
        int expectedDuration,
        int competingDuration)
    {
        var observation = CreateObservation(title, artist, observationDuration);
        var options = new TrackMatchingOptions();
        var candidates = new[]
        {
            CreateCandidate("closest", title, artist, expectedDuration),
            CreateCandidate("other", title, artist, competingDuration)
        }
            .Select(candidate => TrackMatchScorer.Score(observation, candidate, options))
            .OrderByDescending(candidate => candidate.Score)
            .ToArray();

        var family = Assert.Single(TrackMatchIdentityFamilyResolver.BuildFamilies(candidates));

        Assert.Equal(expectedDuration, family.Representative.Candidate.DurationSeconds);
        Assert.Equal(2, family.Members.Count);
    }

    [Fact]
    public void BuildFamilies_TreatsArtistOrderAndJoinFormattingAsEquivalent()
    {
        var observation = CreateObservation("Desire", "Calvin Harris, Sam Smith", 178);
        var options = new TrackMatchingOptions();
        var candidates = new[]
        {
            CreateCandidate("ordered", "Desire", "Calvin Harris & Sam Smith", 179),
            CreateCandidate("reversed", "Desire", "Sam Smith feat. Calvin Harris", 151)
        }
            .Select(candidate => TrackMatchScorer.Score(observation, candidate, options))
            .ToArray();

        var family = Assert.Single(TrackMatchIdentityFamilyResolver.BuildFamilies(candidates));

        Assert.Equal("ordered", family.Representative.Candidate.ExternalId);
    }

    [Fact]
    public void BuildFamilies_KeepsConflictingIsrcsAsCredibleCompetitors()
    {
        var observation = CreateObservation("Strangers", "Sigrid", 245);
        var options = new TrackMatchingOptions();
        var candidates = new[]
        {
            CreateCandidate("album", "Strangers", "Sigrid", 249, "GBCAD1801419"),
            CreateCandidate("single", "Strangers", "Sigrid", 234, "GBUM71705774")
        }.Select(candidate => TrackMatchScorer.Score(observation, candidate, options)).ToArray();

        Assert.Equal(2, TrackMatchIdentityFamilyResolver.BuildFamilies(candidates).Count);
    }

    [Fact]
    public void BuildFamilies_RecognizesCrossProviderIsrcConsensus()
    {
        var observation = CreateObservation("Strangers", "Sigrid", 245);
        var options = new TrackMatchingOptions();
        var musicBrainz = CreateCandidate("mb", "Strangers", "Sigrid", 249, "GBCAD1801419");
        var spotify = CreateCandidate("spotify", "Strangers", "Sigrid", 249, "GBCAD1801419", "spotify");
        var families = TrackMatchIdentityFamilyResolver.BuildFamilies(new[] { musicBrainz, spotify }
            .Select(candidate => TrackMatchScorer.Score(observation, candidate, options))
            .ToArray());

        var family = Assert.Single(families);
        Assert.Equal(2, family.ProviderConsensusCount);
    }

    [Fact]
    public void Decide_AutoMatchesUniqueCrossProviderIsrcConsensus()
    {
        var observation = CreateObservation("Strangers", "Sigrid", 245);
        var options = new TrackMatchingOptions();
        var ranked = new[]
        {
            CreateCandidate("mb-agreed", "Strangers", "Sigrid", 249, "GBCAD1801419"),
            CreateCandidate("spotify-agreed", "Strangers", "Sigrid", 249, "GBCAD1801419", "spotify"),
            CreateCandidate("mb-other", "Strangers", "Sigrid", 234, "GBUM71705774")
        }
            .Select(candidate => TrackMatchScorer.Score(observation, candidate, options))
            .OrderByDescending(candidate => candidate.Score)
            .ToArray();

        var decision = TrackMatchDecisionEngine.Decide(
            ranked,
            TrackMatchClusterer.BuildClusters(ranked, options.ClusterDurationToleranceSeconds),
            options.AutoMatchThreshold,
            options.AmbiguousThreshold,
            options.AutoMatchMargin);

        Assert.Equal(TrackMatchingStatuses.Matched, decision.MatchStatus);
        Assert.Equal("GBCAD1801419", decision.AcceptedCandidate?.Candidate.Isrc);
    }

    private static TrackObservation CreateObservation(string title, string artist, int duration) => new()
    {
        Id = Guid.NewGuid(),
        SourceType = "youtube",
        ExternalId = Guid.NewGuid().ToString("N"),
        Title = title,
        Artist = artist,
        DurationSeconds = duration,
        MatchStatus = TrackMatchingStatuses.Pending
    };

    private static TrackMatchSearchCandidate CreateCandidate(
        string id,
        string title,
        string artist,
        int duration,
        string? isrc = null,
        string candidateSource = "musicbrainz") => new()
    {
        CandidateSource = candidateSource,
        ExternalId = id,
        MbidRecording = id,
        Title = title,
        Artist = artist,
        ArtistCredits = TrackMetadataParser.Parse(title, artist).ArtistCredits,
        Isrc = isrc,
        DurationSeconds = duration
    };
}
