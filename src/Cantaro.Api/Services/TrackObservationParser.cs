using System.Text.Json;
using Cantaro.Api.Models;

namespace Cantaro.Api.Services;

public static class TrackObservationParser
{
    public static ParsedTrackMetadata Parse(TrackObservation observation)
    {
        var metadata = ReadMetadata(observation);
        return Parse(observation, metadata);
    }

    public static ParsedTrackMetadata Parse(
        TrackObservation observation,
        TrackObservationMetadata? metadata)
    {
        var effectiveTitle = TrackObservationDisplayFormatter.GetQueueTitle(
            observation,
            metadata);
        var effectiveArtist = TrackObservationDisplayFormatter.GetQueueArtist(
            observation,
            metadata);
        return TrackMetadataParser.Parse(effectiveTitle, effectiveArtist);
    }

    public static TrackObservationMetadata? ReadMetadata(TrackObservation observation)
    {
        if (string.IsNullOrWhiteSpace(observation.RawMetadata))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TrackObservationMetadata>(
                observation.RawMetadata);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
