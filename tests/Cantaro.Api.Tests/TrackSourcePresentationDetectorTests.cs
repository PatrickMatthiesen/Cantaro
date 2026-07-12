using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class TrackSourcePresentationDetectorTests
{
    private readonly TrackSourcePresentationDetector _detector = new();

    [Fact]
    public void Detect_ClassifiesSpotifyAsAudioWithoutInferringAuthority()
    {
        var result = _detector.Detect(new("spotify", "track-1", "Song", "spotify-snapshot-1"));

        Assert.Equal(TrackSourcePresentationKinds.Audio, result.PresentationKind?.Value);
        Assert.Equal(1m, result.PresentationKind?.Confidence);
        Assert.Equal(TrackSourcePresentationDetector.ProviderMediaTypeEvidenceMethod, result.PresentationKind?.EvidenceMethod);
        Assert.Null(result.UploaderAuthority);
    }

    [Theory]
    [InlineData("Song (Official Music Video)", TrackSourcePresentationKinds.MusicVideo)]
    [InlineData("Song [Lyric Video]", TrackSourcePresentationKinds.LyricVideo)]
    [InlineData("Song - Cover Art Audio", TrackSourcePresentationKinds.CoverArtAudio)]
    [InlineData("Song | Audio Visualizer", TrackSourcePresentationKinds.Visualizer)]
    [InlineData("Song (Official Live Video)", TrackSourcePresentationKinds.LiveVideo)]
    [InlineData("Song (Official Audio)", TrackSourcePresentationKinds.Audio)]
    public void Detect_ClassifiesExplicitYouTubePresentationMarkers(string title, string expectedKind)
    {
        var result = _detector.Detect(new("YouTube", "video-1", title, "youtube-snapshot-1"));

        Assert.Equal(expectedKind, result.PresentationKind?.Value);
        Assert.Equal("youtube", result.PresentationKind?.EvidenceSource);
        Assert.Equal("youtube-snapshot-1", result.PresentationKind?.EvidenceIdentity);
        Assert.Equal(TrackSourcePresentationDetector.TitleMarkerEvidenceMethod, result.PresentationKind?.EvidenceMethod);
        Assert.Equal(TrackSourcePresentationDetector.MethodVersion, result.PresentationKind?.MethodVersion);
    }

    [Theory]
    [InlineData("Live Forever")]
    [InlineData("Cover Me")]
    [InlineData("Visualizer")]
    [InlineData("Video")]
    [InlineData("Video Games")]
    [InlineData("Music Video Girl")]
    [InlineData("The Live Video Sessions")]
    [InlineData("My Cover Art Audio Project")]
    [InlineData("Song (The Live Video Sessions)")]
    [InlineData("Song (Live)")]
    [InlineData("Song (Acoustic Version)")]
    [InlineData("Song")]
    public void Detect_DoesNotTreatMusicalOrOrdinaryTitleTextAsPresentation(string title)
    {
        var result = _detector.Detect(new("youtube", "video-1", title, "youtube-snapshot-1"));

        Assert.Null(result.PresentationKind);
        Assert.Null(result.UploaderAuthority);
    }

    [Fact]
    public void Detect_DoesNotInferOfficialAuthorityFromTitle()
    {
        var result = _detector.Detect(new(
            "youtube",
            "video-1",
            "Song (Official Music Video)",
            "youtube-snapshot-1"));

        Assert.Equal(TrackSourcePresentationKinds.MusicVideo, result.PresentationKind?.Value);
        Assert.Null(result.UploaderAuthority);
    }

    [Theory]
    [InlineData(TrackSourceVerifiedUploaderAuthority.Official, TrackSourceUploaderAuthorities.Official)]
    [InlineData(TrackSourceVerifiedUploaderAuthority.User, TrackSourceUploaderAuthorities.User)]
    public void Detect_UsesOnlyAffirmativeProviderAuthoritySignals(
        TrackSourceVerifiedUploaderAuthority authority,
        string expectedAuthority)
    {
        var result = _detector.Detect(new(
            "youtube",
            "video-1",
            null,
            "youtube-snapshot-1",
            new(authority, "verified-owner-signal-1")));

        Assert.Equal(expectedAuthority, result.UploaderAuthority?.Value);
        Assert.Equal(1m, result.UploaderAuthority?.Confidence);
        Assert.Equal(TrackSourcePresentationDetector.UploaderSignalEvidenceMethod, result.UploaderAuthority?.EvidenceMethod);
        Assert.Equal("verified-owner-signal-1", result.UploaderAuthority?.EvidenceIdentity);
        Assert.Null(result.PresentationKind);
    }

    [Fact]
    public void Detect_UnknownProviderProducesNoProposals()
    {
        var result = _detector.Detect(new(
            "other",
            "item-1",
            "Song (Official Music Video)",
            "other-snapshot-1",
            new(TrackSourceVerifiedUploaderAuthority.Official, "owner-signal-1")));

        Assert.Null(result.PresentationKind);
        Assert.Null(result.UploaderAuthority);
    }

    [Fact]
    public void Detect_DoesNotWriteMusicalTraitEvidence()
    {
        var presentation = _detector.Detect(new(
            "youtube",
            "video-1",
            "Song (Live Video)",
            "youtube-snapshot-1"));
        var musicalTraits = new TitleMarkerTrackVersionTraitDetector().Detect("Song (Live Video)");

        Assert.Equal(TrackSourcePresentationKinds.LiveVideo, presentation.PresentationKind?.Value);
        Assert.Empty(musicalTraits);
    }

    [Fact]
    public void Detect_AcceptsStandaloneVisualizerOnlyAsContextualAnnotation()
    {
        var contextual = _detector.Detect(new(
            "youtube",
            "video-1",
            "Song [Visualizer]",
            "youtube-snapshot-1"));
        var bareTitle = _detector.Detect(new(
            "youtube",
            "video-2",
            "Visualizer",
            "youtube-snapshot-2"));

        Assert.Equal(TrackSourcePresentationKinds.Visualizer, contextual.PresentationKind?.Value);
        Assert.Null(bareTitle.PresentationKind);
    }

    [Fact]
    public void Detect_ReportsConflictingKindsInsteadOfUsingTitleOrder()
    {
        var result = _detector.Detect(new(
            "youtube",
            "video-1",
            "Song [Official Audio] [Official Music Video]",
            "youtube-snapshot-1"));

        Assert.Null(result.PresentationKind);
        Assert.Equal(
            [TrackSourcePresentationKinds.Audio, TrackSourcePresentationKinds.MusicVideo],
            result.ConflictingPresentationKinds);
    }

    [Fact]
    public void Detect_RequiresBoundedEvidenceIdentity()
    {
        Assert.Throws<ArgumentException>(() => _detector.Detect(new(
            "youtube",
            "video-1",
            "Song [Official Audio]",
            " ")));
        Assert.Throws<ArgumentOutOfRangeException>(() => _detector.Detect(new(
            "youtube",
            "video-1",
            null,
            "snapshot-1",
            new(TrackSourceVerifiedUploaderAuthority.Official, new string('x', 257)))));
    }
}
