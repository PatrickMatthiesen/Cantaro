using System.Text.Json;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Xunit;

namespace Cantaro.Api.Tests;

public class TrackMatchCreditEvidenceTests
{
    [Theory]
    [InlineData("spotify")]
    [InlineData("musicbrainz")]
    public void ExplicitFeaturedCreditCanBeOmittedWhenPrimaryArtistAndTitleMatch(string provider)
    {
        var source = Source("Exyl - Together Forever (ft. Rythm)", "Exyl, Rythm", 195);
        var candidate = Candidate("Together Forever", "Exyl", 175, provider);
        AssertAcceptedByBothPaths(source, candidate);
    }

    [Theory]
    [InlineData("Exyl, Rythm - Together Forever", "Exyl, Rythm", "Together Forever", "Exyl")]
    [InlineData("Exyl - Together Forever (ft. Rythm)", "Exyl, Rythm", "Together Forever", "Rythm")]
    [InlineData("Exyl - Together Forever (ft. Rythm)", "Exyl, Rythm", "Together Forever", "Other Artist")]
    [InlineData("Exyl - Together Forever (ft. Rythm)", "Exyl, Rythm", "Together Forever (Cover)", "Exyl")]
    [InlineData("Exyl - Together Forever (ft. Rythm)", "Exyl, Rythm", "Together Forever (Sped Up)", "Exyl")]
    [InlineData("Exyl - Together Forever (ft. Rythm)", "Exyl, Rythm", "Together Forever Again", "Exyl")]
    public void FeaturedCreditToleranceDoesNotDiscardPrimaryCreditsOrVersionEvidence(
        string title, string artist, string candidateTitle, string candidateArtist)
    {
        AssertRejectedByBothPaths(Source(title, artist, 195), Candidate(candidateTitle, candidateArtist, 195));
    }

    [Fact]
    public void NamedMashupMatchesTheSameCreatorsCatalogEdit()
    {
        var source = Kronus();
        AssertAcceptedByBothPaths(source, Candidate("Story of My Life - Clarity Edit", "Kronus", 175));
    }

    [Fact]
    public void FeaturedCreditToleranceStillRequiresCatalogRecordingEvidence()
    {
        var candidate = new TrackMatchSearchCandidate
        {
            CandidateSource = "spotify", ExternalId = "no-identifier", Title = "Together Forever",
            Artist = "Exyl", ArtistCredits = ["Exyl"], DurationSeconds = 195
        };
        AssertRejectedByBothPaths(Source("Exyl - Together Forever (ft. Rythm)", "Exyl, Rythm", 195), candidate);
    }

    [Theory]
    [InlineData("Story of My Life", "Kronus")]
    [InlineData("Story of My Life - Other Song Edit", "Kronus")]
    [InlineData("Story of My Life - Clarity Edit", "Another Creator")]
    [InlineData("Story of My Life - Clarity Edit (Sped Up)", "Kronus")]
    [InlineData("Story of My Life - Clarity Edit (Live)", "Kronus")]
    public void MashupComparisonRequiresBothWorksCreatorAndCompatibleVersion(string title, string artist)
    {
        AssertRejectedByBothPaths(Kronus(), Candidate(title, artist, 177));
    }

    private static TrackObservation Kronus()
    {
        var source = Source("Story of My Life x Clarity (Kronus Mashup)", "Kronus", 177);
        source.RawMetadata = JsonSerializer.Serialize(new TrackObservationMetadata
        {
            OriginalTitle = source.Title, OriginalArtist = "Kronus", ChannelTitle = "Kronus",
            Description = "Kronus, Zedd - Story of My Life x Clarity (Kronus Mashup)"
        });
        return source;
    }

    private static void AssertAcceptedByBothPaths(TrackObservation source, TrackMatchSearchCandidate candidate)
    {
        Assert.NotNull(TrackMatchDecisionEngine.Evaluate(source, [candidate], new()).Decision.AcceptedCandidate);
        Assert.NotNull(PlaylistDestinationIdentityResolver.Assess(source, [candidate], new()).Accepted);
    }

    private static void AssertRejectedByBothPaths(TrackObservation source, TrackMatchSearchCandidate candidate)
    {
        Assert.Null(TrackMatchDecisionEngine.Evaluate(source, [candidate], new()).Decision.AcceptedCandidate);
        Assert.Null(PlaylistDestinationIdentityResolver.Assess(source, [candidate], new()).Accepted);
    }

    private static TrackObservation Source(string title, string artist, int duration) => new()
    {
        SourceType = "youtube", ExternalId = "video", Title = title, Artist = artist,
        DurationSeconds = duration, MatchStatus = TrackMatchingStatuses.Pending
    };

    private static TrackMatchSearchCandidate Candidate(string title, string artist, int duration, string provider = "spotify") => new()
    {
        CandidateSource = provider, ExternalId = "catalog-track", Title = title, Artist = artist,
        ArtistCredits = artist.Split(", "), DurationSeconds = duration, Isrc = "TEST12345678"
    };
}
