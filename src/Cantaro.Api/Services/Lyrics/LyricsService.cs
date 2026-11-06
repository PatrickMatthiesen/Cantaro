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
        var accessibleTrackIds = CanonicalTrackAccess.GetAccessibleTrackIds(dbContext, userId);
        var track = await dbContext.Tracks
            .AsNoTracking()
            .Include(item => item.SourceIds).ThenInclude(item => item.ProviderCatalogItem)
            .Where(item => item.Id == trackId && accessibleTrackIds.Contains(item.Id))
            .SingleOrDefaultAsync(cancellationToken);
        if (track is null)
        {
            return null;
        }

        return await GetTrackLyricsAsync(track, userId, cancellationToken);
    }

    private async Task<LyricsResult?> GetObservationLyricsAsync(int userId, Guid observationId, CancellationToken cancellationToken)
    {
        var observation = await dbContext.TrackObservations
            .AsNoTracking()
            .Where(item => item.Id == observationId
                && item.PlaylistEntries.Any(entry => entry.Playlist != null && entry.Playlist.UserId == userId))
            .SingleOrDefaultAsync(cancellationToken);
        if (observation is null)
        {
            return null;
        }

        if (observation.TrackId is { } trackId)
        {
            var track = await dbContext.Tracks.AsNoTracking()
                .Include(item => item.SourceIds).ThenInclude(item => item.ProviderCatalogItem)
                .SingleOrDefaultAsync(item => item.Id == trackId, cancellationToken);
            var facts = ProviderCatalogPolicy.SharedFacts(track, DateTimeOffset.UtcNow);
            if (facts is not null)
                return await GetPublicLyricsAsync(track!.Id, facts, cancellationToken);
        }

        if (!ProviderCatalogPolicy.HasOwnedSnapshot(observation, userId))
            return null;

        return await GetObservationLyricsAsync(observation, cancellationToken);
    }

    private async Task<LyricsResult> GetObservationLyricsAsync(TrackObservation observation,
        CancellationToken cancellationToken)
    {
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
            sourceMetadata?.Isrc), cancellationToken);
    }

    private async Task<LyricsResult> GetTrackLyricsAsync(Track track, int userId, CancellationToken cancellationToken)
    {
        var facts = ProviderCatalogPolicy.SharedFacts(track, DateTimeOffset.UtcNow);
        if (facts is not null)
            return await GetPublicLyricsAsync(track.Id, facts, cancellationToken);

        var observation = await dbContext.PlaylistEntries.AsNoTracking()
            .Where(entry => entry.TrackId == track.Id && entry.Playlist != null && entry.Playlist.UserId == userId
                && entry.TrackObservation != null && entry.TrackObservation.OwnerUserId == userId
                && entry.TrackObservation.Title.Trim() != "")
            .OrderBy(entry => entry.Position)
            .Select(entry => entry.TrackObservation)
            .FirstOrDefaultAsync(cancellationToken);
        return observation is null
            ? Unavailable("The track has no current public facts or owned observation for a lyrics lookup.")
            : await GetObservationLyricsAsync(observation, cancellationToken);
    }

    private Task<LyricsResult> GetPublicLyricsAsync(Guid trackId, ProviderCatalogFacts facts,
        CancellationToken cancellationToken)
    {
        var artist = facts.Artist ?? (facts.ArtistCredits.Count > 0
            ? string.Join(", ", facts.ArtistCredits.Select(credit => credit.Name)) : null);
        if (string.IsNullOrWhiteSpace(facts.Title) || string.IsNullOrWhiteSpace(artist))
            return Task.FromResult(Unavailable("The public track facts do not have enough metadata to look up lyrics."));

        return provider.GetLyricsAsync(new LyricsLookup(trackId, facts.Title.Trim(), artist.Trim(),
            facts.Albums.FirstOrDefault(album => !string.IsNullOrWhiteSpace(album))?.Trim(),
            facts.DurationSeconds, facts.MusicBrainzRecordingId, facts.Isrc), cancellationToken);
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
