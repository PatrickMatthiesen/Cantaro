using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services.Lyrics;

public sealed class LyricsService(ApplicationDbContext dbContext, ILyricsProvider provider)
{
    public Task<LyricsResult?> GetLyricsAsync(int userId, string songId, CancellationToken cancellationToken)
    {
        if (songId.StartsWith("observation:", StringComparison.Ordinal))
        {
            return Guid.TryParse(songId["observation:".Length..], out var observationId)
                ? GetObservationLyricsAsync(userId, observationId, cancellationToken)
                : Task.FromResult<LyricsResult?>(null);
        }

        var rawId = songId.StartsWith("track:", StringComparison.Ordinal)
            ? songId["track:".Length..]
            : songId;
        return Guid.TryParse(rawId, out var trackId)
            ? GetLyricsAsync(userId, trackId, cancellationToken)
            : Task.FromResult<LyricsResult?>(null);
    }

    public async Task<LyricsResult?> GetLyricsAsync(int userId, Guid trackId, CancellationToken cancellationToken)
    {
        var track = await dbContext.Tracks
            .AsNoTracking()
            .Where(item => item.Id == trackId
                && item.PlaylistEntries.Any(entry => entry.Playlist != null && entry.Playlist.UserId == userId))
            .SingleOrDefaultAsync(cancellationToken);
        if (track is null)
        {
            return null;
        }

        return await GetTrackLyricsAsync(track, cancellationToken);
    }

    private async Task<LyricsResult?> GetObservationLyricsAsync(int userId, Guid observationId, CancellationToken cancellationToken)
    {
        var observation = await dbContext.TrackObservations
            .AsNoTracking()
            .Include(item => item.Track)
            .Where(item => item.Id == observationId
                && item.PlaylistEntries.Any(entry => entry.Playlist != null && entry.Playlist.UserId == userId))
            .SingleOrDefaultAsync(cancellationToken);
        if (observation is null)
        {
            return null;
        }

        if (observation.Track is { } track)
        {
            return await GetTrackLyricsAsync(track, cancellationToken);
        }

        var metadata = TrackObservationParser.Parse(observation);
        if (string.IsNullOrWhiteSpace(metadata.SearchTitle) || string.IsNullOrWhiteSpace(metadata.SearchArtist))
        {
            return Unavailable("The observed song does not have enough metadata to look up lyrics.");
        }

        var sourceMetadata = TrackObservationParser.ReadMetadata(observation);
        return await provider.GetLyricsAsync(new LyricsLookup(
            observation.Id,
            metadata.SearchTitle.Trim(),
            metadata.SearchArtist.Trim(),
            sourceMetadata?.Album?.Trim(),
            observation.DurationSeconds ?? sourceMetadata?.DurationSeconds,
            null,
            null), cancellationToken);
    }

    private async Task<LyricsResult> GetTrackLyricsAsync(Track track, CancellationToken cancellationToken)
    {
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
            return Unavailable("The canonical track does not have enough metadata to look up lyrics.");
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

    private static LyricsResult Unavailable(string explanation) => new()
    {
        State = LyricsStates.Unavailable,
        MatchStatus = LyricsMatchStatuses.Unavailable,
        Provider = "lrclib",
        Attribution = "Lyrics provided by LRCLIB (https://lrclib.net)",
        Explanation = explanation
    };
}
