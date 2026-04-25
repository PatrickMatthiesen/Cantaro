using Cantaro.Api.Services;
using Xunit;

namespace Cantaro.Api.Tests;

public class TrackMetadataParserTests
{
    [Theory]
    [InlineData("Kx5 - Escape (feat. Hayla) [Official Lyric Video]", "Kx5", "Escape (feat. Hayla)", "Kx5", "Escape", "Kx5")]
    [InlineData("Diamond Eyes - Flutter | Future Bass | NCS - Copyright Free Music", "NoCopyrightSounds", "Flutter", "Diamond Eyes", "Flutter", "Diamond Eyes")]
    [InlineData("NURKO feat. Valerie Broussard - The Longest Night", "Proximity", "The Longest Night", "NURKO feat. Valerie Broussard", "The Longest Night", "NURKO")]
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
}