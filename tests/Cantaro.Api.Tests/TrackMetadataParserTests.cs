using Cantaro.Api.Services;
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
    public void Parse_ExtractsPlaybackModifiersEvenWhenTheyAreRemovedFromSearchTitle()
    {
        var parsed = TrackMetadataParser.Parse("Nightcore | Crop Circles", "Jon Bellion");

        Assert.Equal("Crop Circles", parsed.DisplayTitle);
        Assert.Equal("Crop Circles", parsed.SearchTitle);
        Assert.Contains("nightcore", parsed.PlaybackModifiers);
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
