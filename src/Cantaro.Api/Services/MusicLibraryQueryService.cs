using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

public class MusicLibraryQueryService(ApplicationDbContext dbContext)
{
    private readonly ApplicationDbContext _dbContext = dbContext;

    public async Task<MusicLibraryResponse> GetLibraryAsync(int userId, CancellationToken cancellationToken)
    {
        var playlists = await _dbContext.Playlists
            .AsNoTracking()
            .Include(playlist => playlist.ServiceMappings)
            .Include(playlist => playlist.Entries)
                .ThenInclude(entry => entry.Track)
                    .ThenInclude(track => track!.SourceIds)
            .Include(playlist => playlist.Entries)
                .ThenInclude(entry => entry.TrackObservation)
            .Where(playlist => playlist.UserId == userId)
            .OrderBy(playlist => playlist.Name)
            .ToListAsync(cancellationToken);

        var playlistDtos = playlists.Select(MapPlaylist).ToList();
        var songDtos = playlists
            .SelectMany(playlist => playlist.Entries.Select(entry => new MusicLibraryEntryContext(playlist, entry)))
            .GroupBy(item => GetSongStableId(item.Entry))
            .Select(MapSong)
            .OrderBy(song => song.Title)
            .ThenBy(song => song.Artist)
            .ToList();

        return new MusicLibraryResponse
        {
            Summary = new MusicLibrarySummaryDto
            {
                SongCount = songDtos.Count,
                PlaylistCount = playlistDtos.Count
            },
            Songs = songDtos,
            Playlists = playlistDtos
        };
    }

    private static MusicLibraryPlaylistDto MapPlaylist(Playlist playlist)
    {
        return new MusicLibraryPlaylistDto
        {
            Id = playlist.Id.ToString(),
            Name = playlist.Name,
            Description = playlist.Description,
            EntryCount = playlist.Entries.Count,
            Services = playlist.ServiceMappings
                .OrderBy(mapping => mapping.Service)
                .Select(mapping => new MusicLibraryPlaylistServiceDto
                {
                    Service = mapping.Service,
                    ServicePlaylistId = mapping.ServicePlaylistId,
                    LastSyncedAt = mapping.LastSyncedAt,
                    LastSyncStatus = mapping.LastSyncStatus
                })
                .ToList()
        };
    }

    private static MusicLibrarySongDto MapSong(IGrouping<string, MusicLibraryEntryContext> group)
    {
        var representative = group
            .OrderBy(item => item.Entry.Position)
            .First()
            .Entry;
        var metadata = BuildSongMetadata(representative);

        return new MusicLibrarySongDto
        {
            Id = group.Key,
            Title = metadata.Title,
            Artist = metadata.Artist,
            Albums = metadata.Albums,
            ThumbnailUrl = metadata.ThumbnailUrl,
            DurationSeconds = metadata.DurationSeconds,
            MatchStatus = metadata.MatchStatus == TrackMatchingStatuses.Matched ? null : metadata.MatchStatus,
            SourcePlatforms = group
                .SelectMany(item => GetSourcePlatforms(item.Entry))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(source => source)
                .ToList(),
            Playlists = group
                .OrderBy(item => item.Playlist.Name)
                .ThenBy(item => item.Entry.Position)
                .Select(item => new MusicLibrarySongPlaylistDto
                {
                    PlaylistId = item.Playlist.Id.ToString(),
                    PlaylistName = item.Playlist.Name,
                    Position = item.Entry.Position
                })
                .ToList()
        };
    }

    private static SongMetadata BuildSongMetadata(PlaylistEntry entry)
    {
        var canonicalMetadata = ParseJson<TrackCanonicalMetadata>(entry.Track?.CanonicalMetadata);
        var observationMetadata = ParseJson<TrackObservationMetadata>(entry.TrackObservation?.RawMetadata);
        var observation = entry.TrackObservation;

        var title = FirstNonEmpty(
            canonicalMetadata?.Title,
            observation is null ? null : TrackObservationDisplayFormatter.GetQueueTitle(observation, observationMetadata),
            observation?.Title,
            "Unknown song")!;
        var artist = FirstNonEmpty(
            canonicalMetadata?.Artist,
            observation is null ? null : TrackObservationDisplayFormatter.GetQueueArtist(observation, observationMetadata));

        return new SongMetadata(
            title,
            artist,
            (canonicalMetadata?.Albums ?? [])
                .Where(album => !string.IsNullOrWhiteSpace(album))
                .Select(album => album.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            FirstNonEmpty(canonicalMetadata?.ThumbnailUrl, observation?.ThumbnailUrl, observationMetadata?.ThumbnailUrl),
            canonicalMetadata?.DurationSeconds ?? observation?.DurationSeconds ?? observationMetadata?.DurationSeconds,
            observation?.MatchStatus ?? (entry.TrackId is not null ? TrackMatchingStatuses.Matched : null));
    }

    private static string GetSongStableId(PlaylistEntry entry)
    {
        if (entry.TrackId is Guid trackId)
        {
            return $"track:{trackId}";
        }

        if (entry.TrackObservationId is Guid observationId)
        {
            return $"observation:{observationId}";
        }

        return $"entry:{entry.Id}";
    }

    private static IEnumerable<string> GetSourcePlatforms(PlaylistEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.SourceService))
        {
            yield return entry.SourceService;
        }

        if (!string.IsNullOrWhiteSpace(entry.TrackObservation?.SourceType))
        {
            yield return entry.TrackObservation.SourceType;
        }

        foreach (var sourceId in entry.Track?.SourceIds ?? [])
        {
            if (!string.IsNullOrWhiteSpace(sourceId.SourceType))
            {
                yield return sourceId.SourceType;
            }
        }
    }

    private static T? ParseJson<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    private sealed record MusicLibraryEntryContext(Playlist Playlist, PlaylistEntry Entry);
    private sealed record SongMetadata(
        string Title,
        string? Artist,
        List<string> Albums,
        string? ThumbnailUrl,
        int? DurationSeconds,
        string? MatchStatus);
}
