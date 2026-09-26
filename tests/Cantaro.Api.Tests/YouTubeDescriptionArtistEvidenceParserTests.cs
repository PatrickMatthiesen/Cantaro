using System.Text.Json;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Xunit;

namespace Cantaro.Api.Tests;

public class YouTubeDescriptionArtistEvidenceParserTests
{
    [Fact]
    public void Extract_RecoversMortalsArtistsFromDescriptionAndKeepsUploaderSeparate()
    {
        const string title = "MORTALS FUNK REMIX // Victory Royale B**ch!";
        const string description = "LXNGVX, Warriyo - Mortals Funk Remix\n\nAvailable here: https://open.spotify.com/album/example\n\nFollow Lxngvx\nInstagram - /lxngvx\n\n🎧Previous YouTube video: • FLARE! (PHONK)";

        var evidence = YouTubeDescriptionArtistEvidenceParser.Extract(title, description);
        var credit = Assert.Single(evidence.Credits);

        Assert.False(evidence.HasConflictingAttributions);
        Assert.Equal("Mortals Funk Remix", credit.Title);
        Assert.Equal("LXNGVX, Warriyo", credit.Artist);
        Assert.Equal(1, credit.LineNumber);
        Assert.Equal("LXNGVX, Warriyo - Mortals Funk Remix", credit.EvidenceLine);
    }

    [Theory]
    [InlineData("Mortals Funk Remix by LXNGVX, Warriyo")]
    [InlineData("• LXNGVX, Warriyo – MORTALS FUNK REMIX")]
    public void Extract_RecognizesSupportedDescriptionAttributions(string line)
    {
        var evidence = YouTubeDescriptionArtistEvidenceParser.Extract(
            "MORTALS FUNK REMIX (Slowed)",
            line);

        var credit = Assert.Single(evidence.Credits);
        Assert.Equal("LXNGVX, Warriyo", credit.Artist);
    }

    [Fact]
    public void Extract_RecognizesTitleHeadingAndPerformerCredits()
    {
        var evidence = YouTubeDescriptionArtistEvidenceParser.Extract(
            "Everything Goes On - Porter Robinson (Official Music Video) | Star Guardian 2022",
            "Track: Everything Goes On\nArtist: Porter Robinson");

        var credit = Assert.Single(evidence.Credits);
        Assert.Equal("Everything Goes On", credit.Title);
        Assert.Equal("Porter Robinson", credit.Artist);
    }

    [Fact]
    public void Extract_UsesCoverArtistAndIgnoresComposerAsPerformer()
    {
        const string title = "Zoltraak | Epic Orchestral Cover - Frieren: Beyond Journey's End 葬送のフリーレン";
        const string description = "I decided to cover this iconic track! Evan Call's music inspired the cover.\n\nComposer: Evan Call\n\nCover Artist: Luke Chu";

        var evidence = YouTubeDescriptionArtistEvidenceParser.Extract(title, description);
        var credit = Assert.Single(evidence.Credits);

        Assert.Equal("Luke Chu", credit.Artist);
        Assert.Equal("Cover Artist: Luke Chu", credit.EvidenceLine);
    }

    [Fact]
    public void ParseSearchHypotheses_UsesCoverArtistWithoutTreatingFranchiseAsArtist()
    {
        const string title = "Zoltraak | Epic Orchestral Cover - Frieren: Beyond Journey's End 葬送のフリーレン";
        const string description = "Composer: Evan Call\n\nCover Artist: Luke Chu";
        var observation = YouTubeObservation(title, title, "Luke Chu", description);

        var hypotheses = TrackObservationParser.ParseSearchHypotheses(observation);
        var coverArtist = Assert.Single(hypotheses, hypothesis => hypothesis.IsDescriptionArtistEvidence);

        Assert.Equal("Zoltraak - Epic Orchestral Cover", coverArtist.SearchTitle);
        Assert.Equal("Luke Chu", coverArtist.SearchArtist);
        Assert.Equal(["luke chu"], coverArtist.ArtistCredits);
        Assert.DoesNotContain(hypotheses, hypothesis => hypothesis.SearchArtist?.Contains("Frieren", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(hypotheses, hypothesis => hypothesis.SearchArtist?.Contains("Evan Call", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(hypotheses, hypothesis => hypothesis.HasConflictingArtistEvidence);
    }

    [Fact]
    public void ParseSearchHypotheses_AddsNamedMashupCatalogQueryButKeepsMashupSemantics()
    {
        const string title = "Story of My Life x Clarity (Kronus Mashup)";
        var observation = YouTubeObservation(title, title, "Kronus", null);

        var mashup = Assert.Single(TrackObservationParser.ParseSearchHypotheses(observation),
            hypothesis => hypothesis.SearchTitle == "Story of My Life - Clarity Edit");

        Assert.Equal("Kronus", mashup.SearchArtist);
        Assert.Contains("mashup", mashup.VersionMarkers);
        Assert.DoesNotContain("edit", mashup.VersionMarkers);
    }

    [Fact]
    public void Parse_SpotifyObservationKeepsStructuredDashTitleAndArtist()
    {
        var observation = YouTubeObservation("Zoltraak - Epic Orchestral Cover", "Zoltraak - Epic Orchestral Cover", "Luke Chu", null);
        observation.SourceType = "spotify";

        var parsed = TrackObservationParser.Parse(observation);

        Assert.Equal("Zoltraak - Epic Orchestral Cover", parsed.SearchTitle);
        Assert.Equal("Luke Chu", parsed.SearchArtist);
        Assert.Equal(["luke chu"], parsed.ArtistCredits);
    }

    [Theory]
    [InlineData("Lyrics:\nLXNGVX, Warriyo - Mortals Funk Remix")]
    [InlineData("Artwork: Mortals Funk Remix - Mizah")]
    [InlineData("Previous YouTube video:\nWrong Artist - Mortals Funk Remix")]
    [InlineData("Check out Wrong Artist - Mortals Funk Remix")]
    [InlineData("Official Audio - Mortals Funk Remix")]
    [InlineData("Original Mix - Mortals Funk Remix")]
    public void Extract_IgnoresNonPerformerAndPromotionalSections(string description)
    {
        var evidence = YouTubeDescriptionArtistEvidenceParser.Extract(
            "MORTALS FUNK REMIX // Victory Royale B**ch!",
            description);

        Assert.Empty(evidence.Credits);
    }

    [Fact]
    public void Extract_FlagsDistinctArtistFamiliesAsConflictingEvidence()
    {
        var evidence = YouTubeDescriptionArtistEvidenceParser.Extract(
            "MORTALS FUNK REMIX // Victory Royale B**ch!",
            "LXNGVX, Warriyo - Mortals Funk Remix\nOther Artist - Mortals Funk Remix");

        Assert.Equal(2, evidence.Credits.Count);
        Assert.True(evidence.HasConflictingAttributions);
    }

    [Fact]
    public void ParseSearchHypotheses_PrioritizesDescriptionCreditsAndPreservesPlaybackMarkers()
    {
        var observation = YouTubeObservation(
            "MORTALS FUNK REMIX // Victory Royale B**ch! (Slowed)",
            "MORTALS FUNK REMIX",
            "MrMoMMusic",
            "LXNGVX, Warriyo - Mortals Funk Remix");

        var hypotheses = TrackObservationParser.ParseSearchHypotheses(observation);
        var descriptionHypothesis = hypotheses[0];

        Assert.True(descriptionHypothesis.IsDescriptionArtistEvidence);
        Assert.Equal("Mortals Funk Remix", descriptionHypothesis.SearchTitle);
        Assert.Equal("LXNGVX, Warriyo", descriptionHypothesis.SearchArtist);
        Assert.Contains("slowed", descriptionHypothesis.PlaybackModifiers);
        Assert.Equal("LXNGVX, Warriyo - Mortals Funk Remix", descriptionHypothesis.ArtistEvidenceLine);
        Assert.Contains(hypotheses, hypothesis => hypothesis.SearchArtist == "MrMoMMusic");
    }

    [Fact]
    public void ParseSearchHypotheses_FlagsDescriptionConflictWithArtistNamedInTitleOnAllHypotheses()
    {
        var observation = YouTubeObservation(
            "Explicit Artist - Mortals Funk Remix",
            "Mortals Funk Remix",
            "Uploader",
            "Other Artist - Mortals Funk Remix");

        var hypotheses = TrackObservationParser.ParseSearchHypotheses(observation);

        Assert.NotEmpty(hypotheses);
        Assert.All(hypotheses, hypothesis => Assert.True(hypothesis.HasConflictingArtistEvidence));
    }

    [Fact]
    public void ParseSearchHypotheses_FlagsConflictingDescriptionCreditsOnEveryHypothesis()
    {
        var observation = YouTubeObservation(
            "Mortals Funk Remix",
            "Mortals Funk Remix",
            "Uploader",
            "LXNGVX, Warriyo - Mortals Funk Remix\nOther Artist - Mortals Funk Remix");

        var hypotheses = TrackObservationParser.ParseSearchHypotheses(observation);

        Assert.Equal(2, hypotheses.Count(hypothesis => hypothesis.IsDescriptionArtistEvidence));
        Assert.All(hypotheses, hypothesis => Assert.True(hypothesis.HasConflictingArtistEvidence));
    }

    [Fact]
    public void ParseSearchHypotheses_DoesNotTreatUploaderAsExplicitTitleArtist()
    {
        var observation = YouTubeObservation(
            "MORTALS FUNK REMIX // Victory Royale B**ch!",
            "MORTALS FUNK REMIX",
            "MrMoMMusic",
            "LXNGVX, Warriyo - Mortals Funk Remix");

        var hypotheses = TrackObservationParser.ParseSearchHypotheses(observation);

        Assert.DoesNotContain(hypotheses, hypothesis => hypothesis.HasConflictingArtistEvidence);
    }

    private static TrackObservation YouTubeObservation(
        string originalTitle,
        string title,
        string artist,
        string? description) => new()
    {
        Id = Guid.NewGuid(),
        SourceType = "youtube",
        ExternalId = Guid.NewGuid().ToString("N"),
        Title = title,
        Artist = artist,
        RawMetadata = JsonSerializer.Serialize(new TrackObservationMetadata
        {
            OriginalTitle = originalTitle,
            OriginalArtist = artist,
            ChannelTitle = artist,
            SearchTitle = title,
            SearchArtist = artist,
            Description = description
        }),
        MatchStatus = TrackMatchingStatuses.Pending
    };
}
