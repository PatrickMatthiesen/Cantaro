using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Xunit;

namespace Cantaro.Api.Tests;

public class TrackObservationDisplayFormatterTests
{
    [Fact]
    public void GetQueueTitle_StripsSoundtrackContextAfterOriginalArtistPrefix()
    {
        var observation = new ObservationInput
        {
            SourceType = "youtube",
            ExternalId = "soundtrack-video",
            Title = "No Footfalls to Follow - OST",
            Artist = "Horizon Forbidden West",
        };
        var metadata = new TrackObservationMetadata
        {
            OriginalTitle = "Horizon Forbidden West - No Footfalls to Follow - OST",
            Artist = "Horizon Forbidden West",
            SearchArtist = "Horizon Forbidden West"
        };

        var title = TrackObservationDisplayFormatter.GetQueueTitle(observation, metadata);

        Assert.Equal("No Footfalls to Follow", title);
    }

    [Fact]
    public void GetQueueArtist_PrefersParsedFeaturedArtistsFromTitle()
    {
        var observation = new ObservationInput
        {
            SourceType = "youtube",
            ExternalId = "featured-artist",
            Title = "Love Me The Same (ft. GLNNA)",
            Artist = "Vaance, Deerock, Wyle",
        };
        var metadata = new TrackObservationMetadata
        {
            Artist = observation.Artist,
            SearchArtist = "Vaance, Deerock, Wyle, GLNNA"
        };

        var artist = TrackObservationDisplayFormatter.GetQueueArtist(observation, metadata);

        Assert.Equal("Vaance, Deerock, Wyle, GLNNA", artist);
    }

    [Fact]
    public void GetQueueTitle_StripsArtistPrefixAndKeepsAcousticVideoContext()
    {
        var observation = new ObservationInput
        {
            SourceType = "youtube",
            ExternalId = "VmFu_j6iWKM",
            Title = "Crop Circles",
            Artist = "Jon Bellion",
        };
        var metadata = new TrackObservationMetadata
        {
            OriginalTitle = "Jon Bellion - Crop Circles (Acoustic Vertical Video)",
            Artist = "Jon Bellion",
            SearchArtist = "Jon Bellion"
        };

        var title = TrackObservationDisplayFormatter.GetQueueTitle(observation, metadata);
        var artist = TrackObservationDisplayFormatter.GetQueueArtist(observation, metadata);

        Assert.Equal("Crop Circles (Acoustic Vertical Video)", title);
        Assert.Equal("Jon Bellion", artist);
    }

    [Fact]
    public void GetQueueTitle_StripsCollaborationArtistPrefixFromOriginalTitle()
    {
        var observation = new ObservationInput
        {
            SourceType = "youtube",
            ExternalId = "J9Zjgb03FMQ",
            Title = "Good Things Fall Apart",
            Artist = "ILLENIUM, Jon Bellion",
        };
        var metadata = new TrackObservationMetadata
        {
            OriginalTitle = "ILLENIUM, Jon Bellion - Good Things Fall Apart (Official Lyric Video)",
            Artist = "ILLENIUM, Jon Bellion",
            SearchArtist = "ILLENIUM, Jon Bellion"
        };

        var title = TrackObservationDisplayFormatter.GetQueueTitle(observation, metadata);

        Assert.Equal("Good Things Fall Apart (Official Lyric Video)", title);
    }

    [Fact]
    public void GetQueueTitle_UsesOriginalTitleWhenItContainsTheMeaningfulSuffix()
    {
        var observation = new ObservationInput
        {
            SourceType = "youtube",
            ExternalId = "eo-6ra0Y5qw",
            Title = "1990 (Demo)",
            Artist = "Jon Bellion",
        };
        var metadata = new TrackObservationMetadata
        {
            OriginalTitle = "Jon Bellion - 1990 (Demo)",
            Artist = "Jon Bellion",
            SearchArtist = "Jon Bellion"
        };

        var title = TrackObservationDisplayFormatter.GetQueueTitle(observation, metadata);

        Assert.Equal("1990 (Demo)", title);
    }

    [Fact]
    public void GetQueueTitle_FallsBackToObservationTitleWhenNoOriginalTitleExists()
    {
        var observation = new ObservationInput
        {
            SourceType = "youtube",
            ExternalId = "video-plain",
            Title = "Crop Circles",
            Artist = "Jon Bellion",
        };

        var title = TrackObservationDisplayFormatter.GetQueueTitle(observation, metadata: null);

        Assert.Equal("Crop Circles", title);
    }
}
