using System.Text.Json;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Xunit;

namespace Cantaro.Api.Tests;

public class TrackObservationSearchHypothesesTests
{
    [Fact]
    public void ParseSearchHypotheses_RecoversReversedSongAndArtistWithoutChangingPrimary()
    {
        var observation = YouTubeObservation(
            "Everything Goes On - Porter Robinson (Official Music Video) | Star Guardian 2022",
            "Porter Robinson | Star Guardian 2022",
            "Everything Goes On");

        var hypotheses = TrackObservationParser.ParseSearchHypotheses(observation);

        Assert.Equal(TrackObservationParser.Parse(observation).SearchTitle, hypotheses[0].SearchTitle);
        var reversed = Assert.Single(hypotheses.Skip(1));
        Assert.Equal("Everything Goes On", reversed.SearchTitle);
        Assert.Equal("Porter Robinson", reversed.SearchArtist);
        Assert.Equal(["porter robinson"], reversed.ArtistCredits);
        Assert.Contains("official-video", reversed.PresentationMarkers);
        Assert.Equal("Porter Robinson | Star Guardian 2022", observation.Title);
        Assert.Equal("Everything Goes On", observation.Artist);
    }

    [Fact]
    public void ParseSearchHypotheses_RecoversSlashAttribution()
    {
        var observation = YouTubeObservation(
            "ADDICTED //// Zerb, The Chainsmokers & Ink",
            "ADDICTED //// Zerb, The Chainsmokers & Ink",
            "MrMoMMusic");

        var slash = Assert.Single(TrackObservationParser.ParseSearchHypotheses(observation).Skip(1));

        Assert.Equal("ADDICTED", slash.SearchTitle);
        Assert.Equal("Zerb, The Chainsmokers & Ink", slash.SearchArtist);
        Assert.Equal(["ink", "the chainsmokers", "zerb"], slash.ArtistCredits);
    }

    [Fact]
    public void ParseSearchHypotheses_RecoversJapaneseDecoratedAttribution()
    {
        var observation = YouTubeObservation(
            "【Jax Jones × Ado】Stay Gold〈『BEYBLADE X』アニメMV 〉",
            "【Jax Jones × Ado】Stay Gold〈『BEYBLADE X』アニメMV 〉",
            "UNIVERSAL MUSIC JAPAN");

        var decorated = Assert.Single(TrackObservationParser.ParseSearchHypotheses(observation).Skip(1));

        Assert.Equal("Stay Gold", decorated.SearchTitle);
        Assert.Equal("Jax Jones × Ado", decorated.SearchArtist);
        Assert.Equal(["ado", "jax jones"], decorated.ArtistCredits);
    }

    [Fact]
    public void ParseSearchHypotheses_PreservesVersionAndPlaybackEvidence()
    {
        var observation = YouTubeObservation(
            "Signal (Live) speed up - The Artist",
            "The Artist",
            "Signal");

        var reversed = Assert.Single(TrackObservationParser.ParseSearchHypotheses(observation).Skip(1));

        Assert.Contains("live", reversed.VersionMarkers);
        Assert.Contains("speed up", reversed.PlaybackModifiers);
    }

    [Fact]
    public void ParseSearchHypotheses_OnlyAddsVariantsForYouTube()
    {
        var observation = YouTubeObservation("Song - Artist", "Song", "Artist");
        observation.SourceType = "spotify";

        Assert.Single(TrackObservationParser.ParseSearchHypotheses(observation));
    }

    private static TrackObservation YouTubeObservation(string originalTitle, string title, string artist) => new()
    {
        Id = Guid.NewGuid(),
        SourceType = "youtube",
        ExternalId = Guid.NewGuid().ToString("N"),
        Title = title,
        Artist = artist,
        RawMetadata = JsonSerializer.Serialize(new TrackObservationMetadata
        {
            OriginalTitle = originalTitle,
            OriginalArtist = artist
        }),
        MatchStatus = TrackMatchingStatuses.Pending
    };
}
