using System.Text.Json;
using Cantaro.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services.Lyrics;

public sealed class LyricsService(ApplicationDbContext dbContext, ILyricsProvider provider)
{
    public async Task<LyricsResult?> GetLyricsAsync(int userId, Guid trackId, CancellationToken cancellationToken)
    {
        _ = userId;
        var track = await dbContext.Tracks
            .AsNoTracking()
            .Where(item => item.Id == trackId)
            .SingleOrDefaultAsync(cancellationToken);
        if (track is null)
        {
            return null;
        }

        TrackCanonicalMetadata? metadata = null;
        if (!string.IsNullOrWhiteSpace(track.CanonicalMetadata))
        {
            try
            {
                metadata = JsonSerializer.Deserialize<TrackCanonicalMetadata>(track.CanonicalMetadata);
            }
            catch (JsonException)
            {
                // Invalid legacy metadata is reported as unavailable below.
            }
        }

        if (string.IsNullOrWhiteSpace(metadata?.Title) || string.IsNullOrWhiteSpace(metadata.Artist))
        {
            return new LyricsResult
            {
                State = LyricsStates.Unavailable,
                MatchStatus = LyricsMatchStatuses.Unavailable,
                Provider = "lrclib",
                Attribution = "Lyrics provided by LRCLIB (https://lrclib.net)",
                Explanation = "The canonical track does not have enough metadata to look up lyrics."
            };
        }

        return await provider.GetLyricsAsync(new LyricsLookup(
            track.Id,
            metadata.Title.Trim(),
            metadata.Artist.Trim(),
            metadata.Albums.FirstOrDefault(album => !string.IsNullOrWhiteSpace(album))?.Trim(),
            metadata.DurationSeconds,
            track.MbidRecording,
            track.Isrc), cancellationToken);
    }
}
