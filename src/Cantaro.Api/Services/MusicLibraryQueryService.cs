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
                .ThenInclude(entry => entry.Track)
                    .ThenInclude(track => track!.ArtistCredits)
                        .ThenInclude(credit => credit.Artist)
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
        var identities = group
            .SelectMany(item => GetSourceIdentities(item.Entry))
            .DistinctBy(identity => $"{identity.Source}\0{identity.ExternalId}", StringComparer.OrdinalIgnoreCase)
            .OrderBy(identity => identity.Source)
            .ThenBy(identity => identity.ExternalId)
            .ToList();

        return new MusicLibrarySongDto
        {
            Id = group.Key,
            Title = metadata.Title,
            Artist = metadata.Artist,
            ArtistCredits = BuildArtistCredits(representative.Track),
            Albums = metadata.Albums,
            ThumbnailUrl = metadata.ThumbnailUrl,
            DurationSeconds = metadata.DurationSeconds,
            MatchStatus = metadata.MatchStatus == TrackMatchingStatuses.Matched ? null : metadata.MatchStatus,
            SourcePlatforms = group
                .SelectMany(item => GetSourcePlatforms(item.Entry))
                .Select(source => source.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(source => source)
                .ToList(),
            SourceIdentities = identities,
            PlatformLinks = BuildPlatformLinks(identities),
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

    public async Task<MusicLibrarySongDto?> GetCanonicalSongAsync(Guid trackId, int userId, CancellationToken cancellationToken)
    {
        var track = await _dbContext.Tracks.AsNoTracking()
            .Include(x => x.SourceIds)
            .Include(x => x.ArtistCredits).ThenInclude(x => x.Artist)
            .FirstOrDefaultAsync(
                x => x.Id == trackId
                    && x.PlaylistEntries.Any(entry =>
                        entry.Playlist != null && entry.Playlist.UserId == userId),
                cancellationToken);
        if (track is null) return null;
        var metadata = ParseJson<TrackCanonicalMetadata>(track.CanonicalMetadata);
        var memberships = await _dbContext.PlaylistEntries.AsNoTracking()
            .Where(x => x.TrackId == trackId && x.Playlist != null && x.Playlist.UserId == userId)
            .Select(x => new MusicLibrarySongPlaylistDto { PlaylistId = x.PlaylistId.ToString(), PlaylistName = x.Playlist!.Name, Position = x.Position })
            .OrderBy(x => x.PlaylistName).ToListAsync(cancellationToken);
        var identities = track.SourceIds.Select(x => Identity(x.SourceType, x.ExternalId)).ToList();
        if (!string.IsNullOrWhiteSpace(track.Isrc)) identities.Add(Identity("isrc", track.Isrc));
        return new MusicLibrarySongDto
        {
            Id = $"track:{track.Id}", Title = metadata?.Title ?? "Unknown song",
            Artist = FirstNonEmpty(metadata?.Artist, BuildArtistDisplay(track)),
            ArtistCredits = BuildArtistCredits(track),
            Albums = metadata?.Albums ?? [], ThumbnailUrl = metadata?.ThumbnailUrl, DurationSeconds = metadata?.DurationSeconds,
            MatchStatus = null, SourcePlatforms = identities.Select(x => x.Source).Distinct().Order().ToList(),
            SourceIdentities = identities, PlatformLinks = BuildPlatformLinks(identities), Playlists = memberships
        };
    }

    public async Task<bool> AddCanonicalSongToPlaylistAsync(Guid trackId, Guid playlistId, int userId, CancellationToken cancellationToken)
    {
        var playlist = await _dbContext.Playlists.Include(x => x.Entries)
            .FirstOrDefaultAsync(x => x.Id == playlistId && x.UserId == userId, cancellationToken);
        if (playlist is null || !await _dbContext.Tracks.AnyAsync(x => x.Id == trackId, cancellationToken)) return false;
        if (playlist.Entries.Any(x => x.TrackId == trackId)) return true;
        _dbContext.PlaylistEntries.Add(new PlaylistEntry
        {
            Id = Guid.NewGuid(), PlaylistId = playlistId, TrackId = trackId,
            Position = playlist.Entries.Count == 0 ? 0 : playlist.Entries.Max(x => x.Position) + 1,
            AddedAt = DateTimeOffset.UtcNow, SourceService = "cantaro"
        });
        playlist.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RemoveCanonicalSongFromPlaylistAsync(Guid trackId, Guid playlistId, int userId, CancellationToken cancellationToken)
    {
        var playlist = await _dbContext.Playlists.Include(x => x.Entries)
            .FirstOrDefaultAsync(x => x.Id == playlistId && x.UserId == userId, cancellationToken);
        if (playlist is null) return false;
        var entries = playlist.Entries.Where(x => x.TrackId == trackId).ToList();
        if (entries.Count == 0) return true;
        _dbContext.PlaylistEntries.RemoveRange(entries);
        var remaining = playlist.Entries.Except(entries).OrderBy(x => x.Position).ToList();
        for (var index = 0; index < remaining.Count; index++) remaining[index].Position = index;
        playlist.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static IEnumerable<MusicLibrarySongSourceIdentityDto> GetSourceIdentities(PlaylistEntry entry)
    {
        if (entry.Track is { Isrc: { } isrc } && !string.IsNullOrWhiteSpace(isrc))
        {
            yield return Identity("isrc", isrc);
        }

        foreach (var sourceId in entry.Track?.SourceIds ?? [])
        {
            if (!string.IsNullOrWhiteSpace(sourceId.SourceType) && !string.IsNullOrWhiteSpace(sourceId.ExternalId))
            {
                yield return Identity(sourceId.SourceType, sourceId.ExternalId);
            }
        }

        if (entry.TrackObservation is { SourceType: { } source, ExternalId: { } externalId }
            && !string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(externalId))
        {
            yield return Identity(source, externalId);
        }
    }

    private static MusicLibrarySongSourceIdentityDto Identity(string source, string externalId) => new()
    {
        Source = source.Trim().ToLowerInvariant(),
        ExternalId = externalId.Trim()
    };

    private static List<MusicLibrarySongPlatformLinkDto> BuildPlatformLinks(
        IEnumerable<MusicLibrarySongSourceIdentityDto> identities)
    {
        var links = identities.SelectMany(identity => identity.Source switch
            {
                "musicbrainz" => new[] { Link("musicbrainz", "View on MusicBrainz", $"https://musicbrainz.org/recording/{Uri.EscapeDataString(identity.ExternalId)}") },
                "youtube" =>
                new[]
                {
                    Link("youtube", "Watch on YouTube", $"https://www.youtube.com/watch?v={Uri.EscapeDataString(identity.ExternalId)}"),
                    Link("youtube-music", "Open in YouTube Music", $"https://music.youtube.com/watch?v={Uri.EscapeDataString(identity.ExternalId)}")
                },
                "spotify" => new[]
                {
                    Link("spotify", "Listen on Spotify", $"https://open.spotify.com/track/{Uri.EscapeDataString(identity.ExternalId)}")
                },
                _ => Array.Empty<MusicLibrarySongPlatformLinkDto>()
            })
            .DistinctBy(link => link.Url, StringComparer.OrdinalIgnoreCase)
            .OrderBy(link => link.Platform)
            .ToList();

        return links;
    }

    private static MusicLibrarySongPlatformLinkDto Link(string platform, string label, string url) => new()
    {
        Platform = platform,
        Label = label,
        Url = url
    };

    private static List<MusicLibrarySongArtistCreditDto> BuildArtistCredits(Track? track) =>
        track?.ArtistCredits
            .OrderBy(credit => credit.Position)
            .Select(credit => new MusicLibrarySongArtistCreditDto
            {
                ArtistId = credit.ArtistId.ToString(),
                Name = credit.Artist?.Name ?? credit.CreditedName,
                CreditedName = credit.CreditedName,
                Role = credit.Role.ToString().ToLowerInvariant(),
                Position = credit.Position,
                MusicBrainzArtistId = credit.Artist?.MusicBrainzArtistId
            })
            .ToList() ?? [];

    private static string? BuildArtistDisplay(Track? track)
    {
        var creditedNames = track?.ArtistCredits
            .OrderBy(credit => credit.Position)
            .Where(credit => credit.Role is TrackArtistRole.Primary or TrackArtistRole.Featured)
            .Select(credit => credit.CreditedName.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList() ?? [];
        return creditedNames.Count == 0 ? null : string.Join(" ", creditedNames);
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
            BuildArtistDisplay(entry.Track),
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
