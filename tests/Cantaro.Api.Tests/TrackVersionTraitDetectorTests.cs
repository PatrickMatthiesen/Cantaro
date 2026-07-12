using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class TrackVersionTraitDetectorTests
{
    private readonly TitleMarkerTrackVersionTraitDetector _detector = new();

    [Fact]
    public void Detect_PreservesMultipleSimultaneousMusicalTraits()
    {
        var proposals = _detector.Detect("Song (Live Acoustic)");

        Assert.Collection(
            proposals,
            proposal => Assert.Equal(TrackVersionTraitKeys.Acoustic, proposal.TraitKey),
            proposal => Assert.Equal(TrackVersionTraitKeys.Live, proposal.TraitKey));
    }

    [Theory]
    [InlineData("Song (Live Video)")]
    [InlineData("Song (Cover Art Audio)")]
    [InlineData("Song (Live-Video)")]
    [InlineData("Song (Cover-Art-Audio)")]
    [InlineData("Song | Live Video")]
    public void Detect_DoesNotTurnProviderPresentationIntoMusicalTrait(string title)
    {
        Assert.Empty(_detector.Detect(title));
    }

    [Theory]
    [InlineData("Song (Live) [Live Video]", TrackVersionTraitKeys.Live)]
    [InlineData("Song (Cover) [Cover Art Audio]", TrackVersionTraitKeys.Cover)]
    [InlineData("Song (Live at Wembley) [Official Video]", TrackVersionTraitKeys.Live)]
    [InlineData("Artist - Song - Live [Official Video]", TrackVersionTraitKeys.Live)]
    [InlineData("Song - Remastered (Official Video)", TrackVersionTraitKeys.Remaster)]
    public void Detect_PreservesSeparateMusicalEvidenceAlongsidePresentation(
        string title,
        string expectedTrait)
    {
        Assert.Contains(
            _detector.Detect(title),
            proposal => proposal.TraitKey == expectedTrait);
    }

    [Theory]
    [InlineData("Live Forever")]
    [InlineData("I Live for You")]
    [InlineData("Cover Me")]
    [InlineData("Demo")]
    [InlineData("Edit")]
    [InlineData("Artist - Live Forever")]
    [InlineData("Song (Live Forever)")]
    [InlineData("Song (Cover Me)")]
    [InlineData("Song (The Live Sessions)")]
    [InlineData("Song (Demo Track)")]
    [InlineData("Song (Original Soundtrack)")]
    public void Detect_DoesNotTreatOrdinaryTitleWordsAsAnnotations(string title)
    {
        Assert.Empty(_detector.Detect(title));
    }

    [Theory]
    [InlineData("Song (Original)")]
    [InlineData("Song (Original Version)")]
    [InlineData("Song (Original Mix)")]
    public void Detect_RequiresAffirmativeOriginalAnnotation(string title)
    {
        var proposal = Assert.Single(_detector.Detect(title));

        Assert.Equal(TrackVersionTraitKeys.Original, proposal.TraitKey);
    }

    [Fact]
    public void Detect_AllowsOriginalAlongsideOtherExplicitTraits()
    {
        var proposals = _detector.Detect("Song (Original Acoustic Version)");

        Assert.Contains(proposals, proposal => proposal.TraitKey == TrackVersionTraitKeys.Original);
        Assert.Contains(proposals, proposal => proposal.TraitKey == TrackVersionTraitKeys.Acoustic);
    }

    [Fact]
    public void Detect_DoesNotInferOriginalFromMissingMarkers()
    {
        Assert.Empty(_detector.Detect("Song"));
    }

    [Theory]
    [InlineData("Song (Orchestral)", TrackVersionTraitKeys.Orchestral)]
    [InlineData("Song (A Cappella)", TrackVersionTraitKeys.ACappella)]
    [InlineData("Song (A-Cappella)", TrackVersionTraitKeys.ACappella)]
    [InlineData("Song (A Capella)", TrackVersionTraitKeys.ACappella)]
    [InlineData("Song (Acapella)", TrackVersionTraitKeys.ACappella)]
    [InlineData("Song (VIP Mix)", TrackVersionTraitKeys.Remix)]
    [InlineData("Song (Radio Version)", TrackVersionTraitKeys.Edit)]
    [InlineData("Song - Remastered", TrackVersionTraitKeys.Remaster)]
    [InlineData("Artist - Song - Remastered", TrackVersionTraitKeys.Remaster)]
    public void Detect_NormalizesExplicitContextIntoControlledProposals(
        string title,
        string expectedTrait)
    {
        var proposal = Assert.Single(_detector.Detect(title));

        Assert.Equal(expectedTrait, proposal.TraitKey);
        Assert.InRange(proposal.Confidence, 0m, 1m);
        Assert.Equal(TitleMarkerTrackVersionTraitDetector.EvidenceMethod, proposal.EvidenceMethod);
        Assert.Equal(TitleMarkerTrackVersionTraitDetector.MethodVersion, proposal.MethodVersion);
        Assert.NotEmpty(proposal.EvidenceMarkers);
    }

    [Fact]
    public void Detect_DoesNotChangeSharedMatchingParserSemantics()
    {
        var parsed = TrackMetadataParser.Parse("Song (Orchestral)", "Artist");

        Assert.DoesNotContain("orchestral", parsed.VersionMarkers);
        Assert.Contains(
            _detector.Detect("Song (Orchestral)"),
            proposal => proposal.TraitKey == TrackVersionTraitKeys.Orchestral);
    }
}
