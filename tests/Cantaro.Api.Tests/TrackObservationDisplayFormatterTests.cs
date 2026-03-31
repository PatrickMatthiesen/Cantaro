using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Xunit;

namespace Cantaro.Api.Tests;

public class TrackObservationDisplayFormatterTests
{
    [Fact]
    public void GetQueueTitle_StripsArtistPrefixAndKeepsAcousticVideoContext()
    {
        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(),
            SourceType = "youtube",
            ExternalId = "VmFu_j6iWKM",
            Title = "Crop Circles",
            Artist = "Jon Bellion",
            MatchStatus = TrackMatchingStatuses.Ambiguous,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
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
        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(),
            SourceType = "youtube",
            ExternalId = "J9Zjgb03FMQ",
            Title = "Good Things Fall Apart",
            Artist = "ILLENIUM, Jon Bellion",
            MatchStatus = TrackMatchingStatuses.Ambiguous,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
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
        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(),
            SourceType = "youtube",
            ExternalId = "eo-6ra0Y5qw",
            Title = "1990 (Demo)",
            Artist = "Jon Bellion",
            MatchStatus = TrackMatchingStatuses.NoMatch,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
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
        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(),
            SourceType = "youtube",
            ExternalId = "video-plain",
            Title = "Crop Circles",
            Artist = "Jon Bellion",
            MatchStatus = TrackMatchingStatuses.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var title = TrackObservationDisplayFormatter.GetQueueTitle(observation, metadata: null);

        Assert.Equal("Crop Circles", title);
    }
}