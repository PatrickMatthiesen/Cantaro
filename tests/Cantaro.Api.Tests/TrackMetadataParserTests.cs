using Cantaro.Api.Services;
using Cantaro.Api.Models;
using System.Text.Json;
using Xunit;

namespace Cantaro.Api.Tests;

public class TrackMetadataParserTests
{
    [Theory]
    [InlineData("Kx5 - Escape (feat. Hayla) [Official Lyric Video]", "Kx5", "Escape (feat. Hayla)", "Kx5, Hayla", "Escape", "Kx5, Hayla")]
    [InlineData("Diamond Eyes - Flutter | Future Bass | NCS - Copyright Free Music", "NoCopyrightSounds", "Flutter", "Diamond Eyes", "Flutter", "Diamond Eyes")]
    [InlineData("NURKO feat. Valerie Broussard - The Longest Night", "Proximity", "The Longest Night", "NURKO feat. Valerie Broussard", "The Longest Night", "NURKO feat. Valerie Broussard")]
    [InlineData("Superfruit - Guy.exe speed up", "Iztuwa", "Guy.exe", "Superfruit", "Guy.exe", "Superfruit")]
    [InlineData("Riptide | South Arcade - Topic", "South Arcade - Topic", "Riptide", "South Arcade", "Riptide", "South Arcade")]
    [InlineData("Maybe IDK (Audio)", "Jon Bellion", "Maybe IDK", "Jon Bellion", "Maybe IDK", "Jon Bellion")]
    public void Parse_UsesRepresentativeQueueExamples(
        string rawTitle,
        string rawArtist,
        string expectedTitle,
        string expectedArtist,
        string expectedSearchTitle,
        string expectedSearchArtist)
    {
        var parsed = TrackMetadataParser.Parse(rawTitle, rawArtist);

        Assert.Equal(expectedTitle, parsed.DisplayTitle);
        Assert.Equal(expectedArtist, parsed.DisplayArtist);
        Assert.Equal(expectedSearchTitle, parsed.SearchTitle);
        Assert.Equal(expectedSearchArtist, parsed.SearchArtist);
    }

    [Fact]
    public void Parse_ExtractsVersionMarkersFromBracketedTitleContext()
    {
        var parsed = TrackMetadataParser.Parse("Crop Circles (Acoustic Vertical Video)", "Jon Bellion");

        Assert.Equal("Crop Circles", parsed.DisplayTitle);
        Assert.Contains("acoustic", parsed.VersionMarkers);
        Assert.Empty(parsed.PlaybackModifiers);
    }

    [Fact]
    public void Parse_PreservesParentheticalSourceAsRecordingContext()
    {
        var parsed = TrackMetadataParser.Parse(
            "Rescue Me (from One Night in Malibu)",
            "OneRepublic");

        Assert.Contains("source-context", parsed.VersionMarkers);
    }

    [Fact]
    public void Parse_TreatsTrailingDashVersionAsContextWhenArtistIsSupplied()
    {
        var parsed = TrackMetadataParser.Parse("Signal - Live", "The Artist");

        Assert.Equal("Signal", parsed.DisplayTitle);
        Assert.Equal("Signal", parsed.SearchTitle);
        Assert.Equal("The Artist", parsed.DisplayArtist);
        Assert.Contains("live", parsed.VersionMarkers);
    }

    [Theory]
    [InlineData("Meant To Live (Jon Bellion Version)")]
    [InlineData("How Do I Live")]
    [InlineData("Live Forever")]
    public void Parse_DoesNotTreatLiveInBaseTitleAsVersionMarker(string title)
    {
        var parsed = TrackMetadataParser.Parse(title, "The Artist");

        Assert.DoesNotContain("live", parsed.VersionMarkers);
    }

    [Theory]
    [InlineData("Signal (Live)", "Signal (Live)")]
    [InlineData("Signal (Live at Wembley)", "Signal (Live at Wembley)")]
    [InlineData("Signal - Live", "Signal")]
    [InlineData("Signal - Live Version", "Signal")]
    public void Parse_RecognizesLiveOnlyInVersionContext(string title, string expectedSearchTitle)
    {
        var parsed = TrackMetadataParser.Parse(title, "The Artist");

        Assert.Equal(expectedSearchTitle, parsed.SearchTitle);
        Assert.Contains("live", parsed.VersionMarkers);
    }

    [Fact]
    public void Parse_ExtractsPlaybackModifiersEvenWhenTheyAreRemovedFromSearchTitle()
    {
        var parsed = TrackMetadataParser.Parse("Nightcore | Crop Circles", "Jon Bellion");

        Assert.Equal("Crop Circles", parsed.DisplayTitle);
        Assert.Equal("Crop Circles", parsed.SearchTitle);
        Assert.Contains("nightcore", parsed.PlaybackModifiers);
    }

    [Fact]
    public void ParseObservation_UsesOriginalProviderTitleForVersionSemantics()
    {
        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(),
            SourceType = "youtube",
            ExternalId = "CH_oVqS6iss",
            Title = "Guy.exe",
            Artist = "Superfruit",
            RawMetadata = JsonSerializer.Serialize(new TrackObservationMetadata
            {
                OriginalTitle = "Superfruit - Guy.exe speed up",
                Artist = "Superfruit",
                SearchArtist = "Superfruit"
            }),
            MatchStatus = TrackMatchingStatuses.Ambiguous,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var parsed = TrackObservationParser.Parse(observation);

        Assert.Equal("Guy.exe", parsed.DisplayTitle);
        Assert.Equal("Superfruit", parsed.DisplayArtist);
        Assert.Contains("speed up", parsed.PlaybackModifiers);
    }

    [Fact]
    public void Parse_RemovesNoiseMarkersWithoutInventingVersionMarkers()
    {
        var parsed = TrackMetadataParser.Parse("Crop Circles (Official Video)", "Jon Bellion");

        Assert.Equal("Crop Circles", parsed.DisplayTitle);
        Assert.Empty(parsed.VersionMarkers);
        Assert.Empty(parsed.PlaybackModifiers);
    }

    [Fact]
    public void Parse_RetainsOfficialVideoAndFeaturedCreditsAsEvidence()
    {
        var parsed = TrackMetadataParser.Parse("1-800-273-8255 ft. Alessia Cara, Khalid (Official Video)", "Logic");

        Assert.Contains("official-video", parsed.PresentationMarkers);
        Assert.Equal(["alessia cara", "khalid", "logic"], parsed.ArtistCredits);
    }

    [Fact]
    public void Parse_AppendsParentheticalFeaturedArtistToDisplayAndSearchArtist()
    {
        var parsed = TrackMetadataParser.Parse("Love Me The Same (ft. GLNNA)", "Vaance, Deerock, Wyle");

        Assert.Equal("Vaance, Deerock, Wyle, GLNNA", parsed.DisplayArtist);
        Assert.Equal("Vaance, Deerock, Wyle, GLNNA", parsed.SearchArtist);
        Assert.Contains("glnna", parsed.ArtistCredits);
    }

    [Fact]
    public void Parse_SplitsSpacedXCollaboratorsIntoArtistCredits()
    {
        var parsed = TrackMetadataParser.Parse("Lucid Eyes (ft. Jay Mason)", "Rival x Sabai");

        Assert.Equal("Rival x Sabai, Jay Mason", parsed.SearchArtist);
        Assert.Equal(["jay mason", "rival", "sabai"], parsed.ArtistCredits);
    }

    [Theory]
    [InlineData("Kx5")]
    [InlineData("X Ambassadors")]
    [InlineData("The xx")]
    public void Parse_DoesNotSplitXInsideArtistNames(string artist)
    {
        var parsed = TrackMetadataParser.Parse("Song", artist);

        Assert.Single(parsed.ArtistCredits);
        Assert.Equal(TrackTextNormalizer.Normalize(artist), parsed.ArtistCredits[0]);
    }

    [Fact]
    public void Parse_StripsTrailingOstContextIdempotently()
    {
        var first = TrackMetadataParser.Parse(
            "Horizon Forbidden West - No Footfalls to Follow - OST",
            "HeXenkingTV");
        var second = TrackMetadataParser.Parse(first.DisplayTitle, first.DisplayArtist);

        Assert.Equal("No Footfalls to Follow", first.DisplayTitle);
        Assert.Equal("Horizon Forbidden West", first.DisplayArtist);
        Assert.Equal(first.DisplayTitle, second.DisplayTitle);
        Assert.Equal(first.DisplayArtist, second.DisplayArtist);
    }

    [Theory]
    [InlineData("Song - OST", "Song")]
    [InlineData("Song [OST]", "Song")]
    [InlineData("Song - Original Soundtrack", "Song")]
    [InlineData("Song | Video Game Soundtrack", "Song")]
    public void Parse_StripsExactTrailingSoundtrackContext(string rawTitle, string expectedTitle)
    {
        var parsed = TrackMetadataParser.Parse(rawTitle, "Artist");

        Assert.Equal(expectedTitle, parsed.DisplayTitle);
        Assert.Equal(expectedTitle, parsed.SearchTitle);
    }

    [Theory]
    [InlineData("OST - After Hours")]
    [InlineData("Dabin - Soundtrack to the End")]
    [InlineData("Ost")]
    public void Parse_PreservesLegitimateOstAndSoundtrackText(string rawTitle)
    {
        var parsed = TrackMetadataParser.Parse(rawTitle, "Channel");

        Assert.NotEqual(string.Empty, parsed.DisplayTitle);
        Assert.Contains(
            rawTitle.Contains(" - ", StringComparison.Ordinal) ? rawTitle.Split(" - ")[1] : rawTitle,
            parsed.DisplayTitle,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Song (radio version)", "radio-edit")]
    [InlineData("Song (remastered)", "remaster")]
    [InlineData("Song (VIP mix)", "vip-mix")]
    [InlineData("Song (extended mix)", "extended-mix")]
    [InlineData("Song (cover)", "cover")]
    public void Parse_CanonicalizesRecordingVersionFamilies(string title, string expectedMarker)
    {
        var parsed = TrackMetadataParser.Parse(title, "Artist");

        Assert.Contains(expectedMarker, parsed.VersionMarkers);
    }
}
