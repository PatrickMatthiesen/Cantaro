using Cantaro.Api.Services;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class JapaneseTrackMetadataParserTests
{
    [Fact]
    public void Parse_StripsMatchingJapaneseArtistTag()
    {
        var parsed = TrackMetadataParser.Parse("【Ado】レディメイド", "Ado");

        Assert.Equal("レディメイド", parsed.SearchTitle);
        Assert.Equal("Ado", parsed.SearchArtist);
        Assert.True(parsed.ParsedArtistFromTitle);
    }

    [Fact]
    public void Parse_ExtractsJapaneseQuotedTitleFromOfficialChannelPromotion()
    {
        var parsed = TrackMetadataParser.Parse(
            "Aimer「星の消えた夜に -rit. ver.-」MUSIC VIDEO(new album『星の消えた夜に』now on sale）",
            "Aimer Official YouTube Channel");

        Assert.Equal("星の消えた夜に -rit. ver.", parsed.SearchTitle);
        Assert.Equal("Aimer", parsed.SearchArtist);
        Assert.True(parsed.ParsedArtistFromTitle);
    }

    [Fact]
    public void Parse_DoesNotStripUnrelatedJapaneseArtistTag()
    {
        var parsed = TrackMetadataParser.Parse("【Other Artist】レディメイド", "Ado");

        Assert.Equal("【Other Artist】レディメイド", parsed.SearchTitle);
        Assert.False(parsed.ParsedArtistFromTitle);
    }
}
