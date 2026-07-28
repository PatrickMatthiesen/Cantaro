using System.Text.Json;
using Cantaro.Api.Models;

namespace Cantaro.Api.Services;

public static class TrackArtworkUpdater
{
    public static bool FillMissingCanonicalThumbnail(
        Track track,
        string? thumbnailUrl)
    {
        if (string.IsNullOrWhiteSpace(thumbnailUrl))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(track.CanonicalMetadata))
        {
            track.CanonicalMetadata = JsonSerializer.Serialize(
                new TrackCanonicalMetadata { ThumbnailUrl = thumbnailUrl });
            track.UpdatedAt = DateTimeOffset.UtcNow;
            return true;
        }

        try
        {
            var metadata = JsonSerializer.Deserialize<TrackCanonicalMetadata>(
                track.CanonicalMetadata);
            if (metadata == null || !string.IsNullOrWhiteSpace(metadata.ThumbnailUrl))
            {
                return false;
            }

            metadata.ThumbnailUrl = thumbnailUrl;
            track.CanonicalMetadata = JsonSerializer.Serialize(metadata);
            track.UpdatedAt = DateTimeOffset.UtcNow;
            return true;
        }
        catch (JsonException)
        {
            // Preserve malformed legacy metadata instead of replacing unrelated
            // canonical fields while attempting an artwork-only enrichment.
            return false;
        }
    }
}
