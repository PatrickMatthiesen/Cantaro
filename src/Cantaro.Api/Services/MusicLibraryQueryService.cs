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
                    .ThenInclude(track => track!.ArtistCredits)
                        .ThenInclude(credit => credit.Artist)
            .Include(playlist => playlist.Entries)
                .ThenInclude(entry => entry.TrackObservation)
            .Where(playlist => playlist.UserId == userId)
            .OrderBy(playlist => playlist.Name)
            .ToListAsync(cancellationToken);

        var playlistDtos = playlists.Select(MapPlaylist).ToList();
        var songDtos = playlists
            .SelectMany(playlist => playlist.Entries
                .Where(IsMusicEntry)
                .Select(entry => new MusicLibraryEntryContext(playlist, entry)))
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
            EntryCount = playlist.Entries.Count(IsMusicEntry),
            AllowDuplicateTracks = playlist.AllowDuplicateTracks,
            Services = playlist.ServiceMappings
                .Where(mapping => mapping.State != "unlinked" && !mapping.ServicePlaylistId.StartsWith("pending:", StringComparison.Ordinal))
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

    private static bool IsMusicEntry(PlaylistEntry entry) =>
        entry.TrackObservation?.MatchStatus != TrackMatchingStatuses.NotMusic;

    private static MusicLibrarySongDto MapSong(IGrouping<string, MusicLibraryEntryContext> group)
    {
        var entries = OrderOwnedEntries(group.Select(item => item.Entry)).ToList();
        var representative = entries[0];
        var metadata = BuildSongMetadata(representative.Track, entries);
        var identities = DistinctIdentities(GetSourceIdentities(representative.Track, group.Select(item => item.Entry)));

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
            SourcePlatforms = identities.Select(identity => identity.Source).Distinct().Order().ToList(),
            SourceIdentities = identities,
            PlatformLinks = BuildPlatformLinks(identities),
            VersionLabels = representative.Track is null ? [] : GetVersionLabels(representative.Track.VersionFlags),
            Playlists = group
                .OrderBy(item => item.Playlist.Name)
                .ThenBy(item => item.Entry.Position)
                .Select(item => new MusicLibrarySongPlaylistDto
                {
                    EntryId = item.Entry.Id.ToString(),
                    PlaylistId = item.Playlist.Id.ToString(),
                    PlaylistName = item.Playlist.Name,
                    Position = item.Entry.Position
                })
                .ToList()
        };
    }

    public async Task<MusicLibrarySongDto?> GetCanonicalSongAsync(Guid trackId, int userId, CancellationToken cancellationToken)
    {
        var accessibleTrackIds = CanonicalTrackAccess.GetAccessibleTrackIds(_dbContext, userId);
        var track = await _dbContext.Tracks.AsNoTracking()
            .Include(x => x.ArtistCredits).ThenInclude(x => x.Artist)
            .FirstOrDefaultAsync(x => x.Id == trackId && accessibleTrackIds.Contains(x.Id), cancellationToken);
        if (track is null) return null;
        var metadata = ParseJson<TrackCanonicalMetadata>(track.CanonicalMetadata);
        var entries = await _dbContext.PlaylistEntries.AsNoTracking()
            .Include(entry => entry.TrackObservation)
            .Include(entry => entry.Playlist)
            .Where(entry => entry.TrackId == trackId && entry.Playlist != null && entry.Playlist.UserId == userId)
            .OrderBy(entry => entry.Position)
            .ThenBy(entry => entry.Playlist!.Name)
            .ThenBy(entry => entry.PlaylistId)
            .ThenBy(entry => entry.Id)
            .ToListAsync(cancellationToken);
        var displayMetadata = BuildSongMetadata(track, entries);
        var memberships = entries
            .Select(x => new MusicLibrarySongPlaylistDto { EntryId = x.Id.ToString(), PlaylistId = x.PlaylistId.ToString(), PlaylistName = x.Playlist!.Name, Position = x.Position })
            .OrderBy(x => x.PlaylistName).ToList();
        var identities = DistinctIdentities(GetSourceIdentities(track, entries));
        return new MusicLibrarySongDto
        {
            Id = $"track:{track.Id}", Title = FirstNonEmpty(metadata?.Title, track.SearchTitle, "Unknown song")!,
            Artist = FirstNonEmpty(metadata?.Artist, BuildArtistDisplay(track), track.SearchArtist),
            ArtistCredits = BuildArtistCredits(track),
            Albums = metadata?.Albums ?? [],
            ThumbnailUrl = displayMetadata.ThumbnailUrl,
            DurationSeconds = displayMetadata.DurationSeconds,
            MatchStatus = null, SourcePlatforms = identities.Select(x => x.Source).Distinct().Order().ToList(),
            SourceIdentities = identities, PlatformLinks = BuildPlatformLinks(identities), Playlists = memberships,
            VersionLabels = GetVersionLabels(track.VersionFlags),
            RelatedRecordings = await GetRelatedRecordingsAsync(trackId, accessibleTrackIds, userId, cancellationToken)
        };
    }

    private async Task<List<MusicLibraryRelatedRecordingDto>> GetRelatedRecordingsAsync(
        Guid trackId, IQueryable<Guid> accessibleTrackIds, int userId, CancellationToken cancellationToken)
    {
        var compositionIds = await _dbContext.SongTracks.AsNoTracking()
            .Where(member => member.TrackId == trackId)
            .Select(member => member.SongId)
            .ToListAsync(cancellationToken);
        var sameCompositionIds = await _dbContext.SongTracks.AsNoTracking()
            .Where(member => member.TrackId != trackId && compositionIds.Contains(member.SongId))
            .Select(member => member.TrackId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var relations = await _dbContext.TrackRelations.AsNoTracking()
            .Where(relation => relation.FromTrackId == trackId || relation.ToTrackId == trackId)
            .ToListAsync(cancellationToken);
        var relatedIds = sameCompositionIds
            .Concat(relations.Select(relation => relation.FromTrackId == trackId ? relation.ToTrackId : relation.FromTrackId))
            .Distinct().ToList();
        if (relatedIds.Count == 0) return [];

        var ownedEntries = await _dbContext.PlaylistEntries.AsNoTracking()
            .Include(entry => entry.TrackObservation)
            .Include(entry => entry.Playlist)
            .Where(entry => entry.TrackId != null && relatedIds.Contains(entry.TrackId.Value)
                && entry.Playlist != null && entry.Playlist.UserId == userId)
            .OrderBy(entry => entry.Position)
            .ThenBy(entry => entry.Playlist!.Name)
            .ThenBy(entry => entry.PlaylistId)
            .ThenBy(entry => entry.Id)
            .ToListAsync(cancellationToken);
        var entriesByTrack = ownedEntries.ToLookup(entry => entry.TrackId!.Value);
        var tracks = await _dbContext.Tracks.AsNoTracking()
            .Where(candidate => relatedIds.Contains(candidate.Id) && accessibleTrackIds.Contains(candidate.Id))
            .Include(candidate => candidate.ArtistCredits).ThenInclude(credit => credit.Artist)
            .ToListAsync(cancellationToken);

        return tracks.Select(candidate =>
        {
            var metadata = ParseJson<TrackCanonicalMetadata>(candidate.CanonicalMetadata);
            var entries = entriesByTrack[candidate.Id];
            var displayMetadata = BuildSongMetadata(candidate, entries);
            var labels = relations
                .Where(relation => relation.FromTrackId == candidate.Id || relation.ToTrackId == candidate.Id)
                .Select(relation => DescribeRelation(relation, trackId))
                .Distinct().ToList();
            if (labels.Count == 0 && sameCompositionIds.Contains(candidate.Id)) labels.Add("Another recording of this composition");
            return new MusicLibraryRelatedRecordingDto
            {
                Id = $"track:{candidate.Id}",
                Title = FirstNonEmpty(metadata?.Title, candidate.SearchTitle, "Unknown song")!,
                Artist = FirstNonEmpty(metadata?.Artist, BuildArtistDisplay(candidate), candidate.SearchArtist),
                ThumbnailUrl = displayMetadata.ThumbnailUrl,
                DurationSeconds = displayMetadata.DurationSeconds,
                RelationshipLabels = labels,
                VersionLabels = GetVersionLabels(candidate.VersionFlags),
                InLibrary = entries.Any()
            };
        }).OrderBy(candidate => candidate.Title).ThenBy(candidate => candidate.Id).ToList();
    }

    private static string DescribeRelation(TrackRelation relation, Guid currentTrackId)
    {
        var kind = relation.RelationType switch
        {
            TrackRelationType.RemixOf => "remix",
            TrackRelationType.EditOf => "edit",
            TrackRelationType.RemasterOf => "remaster",
            TrackRelationType.ReRecordingOf => "re-recording",
            TrackRelationType.DerivedFrom => "derived recording",
            TrackRelationType.Samples => "sample",
            _ => "related recording"
        };
        return relation.FromTrackId == currentTrackId
            ? $"Source of this {kind}"
            : $"{char.ToUpperInvariant(kind[0])}{kind[1..]} of this recording";
    }

    private static List<string> GetVersionLabels(TrackVersionFlags flags) =>
        Enum.GetValues<TrackVersionFlags>()
            .Where(flag => flag != TrackVersionFlags.None && flags.HasFlag(flag))
            .Select(flag => flag switch
            {
                TrackVersionFlags.ACappella => "A cappella",
                TrackVersionFlags.RadioEdit => "Radio edit",
                TrackVersionFlags.ReRecorded => "Re-recorded",
                TrackVersionFlags.AlternateTake => "Alternate take",
                TrackVersionFlags.SpedUp => "Sped up",
                _ => flag.ToString()
            })
            .ToList();

    public async Task<bool> AddCanonicalSongToPlaylistAsync(Guid trackId, Guid playlistId, int userId, CancellationToken cancellationToken)
    {
        var playlist = await _dbContext.Playlists.Include(x => x.Entries)
            .FirstOrDefaultAsync(x => x.Id == playlistId && x.UserId == userId, cancellationToken);
        if (playlist is null || !await _dbContext.Tracks.AnyAsync(x => x.Id == trackId, cancellationToken)) return false;
        if (!playlist.AllowDuplicateTracks && playlist.Entries.Any(x => x.TrackId == trackId)) return true;
        _dbContext.PlaylistEntries.Add(new PlaylistEntry
        {
            Id = Guid.NewGuid(), PlaylistId = playlistId, TrackId = trackId,
            Position = playlist.Entries.Count == 0 ? 0 : playlist.Entries.Max(x => x.Position) + 1,
            AddedAt = DateTimeOffset.UtcNow, SourceService = "cantaro"
        });
        var now = DateTimeOffset.UtcNow;
        playlist.UpdatedAt = now;
        playlist.SyncRevision++;
        if (playlist.SyncEnabled) playlist.NextSyncAt ??= now.AddDays(1);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RemoveCanonicalSongFromPlaylistAsync(Guid trackId, Guid playlistId, int userId,
        CancellationToken cancellationToken, Guid? entryId = null)
    {
        var playlist = await _dbContext.Playlists.Include(x => x.Entries)
            .FirstOrDefaultAsync(x => x.Id == playlistId && x.UserId == userId, cancellationToken);
        if (playlist is null) return false;
        var entries = playlist.Entries.Where(x => x.TrackId == trackId
            && (entryId is null || x.Id == entryId.Value)).ToList();
        if (entries.Count == 0) return entryId is null;
        _dbContext.PlaylistEntries.RemoveRange(entries);
        var remaining = playlist.Entries.Except(entries).OrderBy(x => x.Position).ToList();
        for (var index = 0; index < remaining.Count; index++) remaining[index].Position = index;
        var now = DateTimeOffset.UtcNow;
        playlist.UpdatedAt = now;
        playlist.SyncRevision++;
        if (playlist.SyncEnabled) playlist.NextSyncAt ??= now.AddDays(1);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    // Both callers supply only entries from the requester's playlists. Global source mappings
    // support matching and sync, but only attached observations authorize provider identities here.
    private static IEnumerable<MusicLibrarySongSourceIdentityDto> GetSourceIdentities(
        Track? track, IEnumerable<PlaylistEntry> ownedEntries)
    {
        if (!string.IsNullOrWhiteSpace(track?.Isrc))
        {
            yield return Identity("isrc", track.Isrc);
        }

        if (!string.IsNullOrWhiteSpace(track?.MbidRecording))
        {
            yield return Identity("musicbrainz", track.MbidRecording);
        }

        foreach (var entry in ownedEntries)
        {
            if (entry.TrackObservation is { SourceType: { } source, ExternalId: { } externalId }
                && !string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(externalId))
            {
                yield return Identity(source, externalId);
            }
        }
    }

    private static MusicLibrarySongSourceIdentityDto Identity(string source, string externalId) => new()
    {
        Source = source.Trim().ToLowerInvariant(),
        ExternalId = externalId.Trim()
    };

    private static List<MusicLibrarySongSourceIdentityDto> DistinctIdentities(IEnumerable<MusicLibrarySongSourceIdentityDto> identities) =>
        identities.DistinctBy(identity => $"{identity.Source}\0{identity.ExternalId}", StringComparer.OrdinalIgnoreCase)
            .OrderBy(identity => identity.Source).ThenBy(identity => identity.ExternalId).ToList();

    private static List<MusicLibrarySongPlatformLinkDto> BuildPlatformLinks(
        IEnumerable<MusicLibrarySongSourceIdentityDto> identities)
    {
        var links = identities.SelectMany(identity => identity.Source switch
            {
                "musicbrainz" => new[] { Link("musicbrainz", "View on MusicBrainz", $"https://musicbrainz.org/recording/{Uri.EscapeDataString(identity.ExternalId)}", identity) },
                "youtube" =>
                new[]
                {
                    Link("youtube", "Watch on YouTube", $"https://www.youtube.com/watch?v={Uri.EscapeDataString(identity.ExternalId)}", identity),
                    Link("youtube-music", "Open in YouTube Music", $"https://music.youtube.com/watch?v={Uri.EscapeDataString(identity.ExternalId)}", identity)
                },
                "spotify" => new[]
                {
                    Link("spotify", "Listen on Spotify", $"https://open.spotify.com/track/{Uri.EscapeDataString(identity.ExternalId)}", identity)
                },
                _ => Array.Empty<MusicLibrarySongPlatformLinkDto>()
            })
            .DistinctBy(link => link.Url, StringComparer.OrdinalIgnoreCase)
            .OrderBy(link => link.Platform)
            .ToList();

        return links;
    }

    private static MusicLibrarySongPlatformLinkDto Link(string platform, string label, string url, MusicLibrarySongSourceIdentityDto identity) => new()
    {
        Platform = platform,
        Label = label,
        Url = url,
        Source = identity.Source,
        ExternalId = identity.ExternalId
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

    private static IOrderedEnumerable<PlaylistEntry> OrderOwnedEntries(IEnumerable<PlaylistEntry> entries) =>
        entries.OrderBy(entry => entry.Position)
            .ThenBy(entry => entry.Playlist?.Name, StringComparer.Ordinal)
            .ThenBy(entry => entry.PlaylistId)
            .ThenBy(entry => entry.Id);

    // Every caller supplies only entries from the requester's playlists. Canonical artwork
    // has no public provenance, including legacy URLs copied there by matching. An attached
    // owned observation must contain that exact URL before canonical precedence applies.
    private static SongMetadata BuildSongMetadata(Track? track, IEnumerable<PlaylistEntry> ownedEntries)
    {
        var canonicalMetadata = ParseJson<TrackCanonicalMetadata>(track?.CanonicalMetadata);
        var observations = OrderOwnedEntries(ownedEntries)
            .Where(entry => entry.TrackObservation is not null)
            .Select(entry => (Observation: entry.TrackObservation!,
                Metadata: ParseJson<TrackObservationMetadata>(entry.TrackObservation!.RawMetadata)))
            .ToList();
        var observation = observations.FirstOrDefault().Observation;
        var observationMetadata = observations.FirstOrDefault().Metadata;
        var canonicalArtwork = FirstNonEmpty(canonicalMetadata?.ThumbnailUrl);
        var thumbnailUrl = canonicalArtwork is not null && observations.Any(item =>
            string.Equals(canonicalArtwork, FirstNonEmpty(item.Observation.ThumbnailUrl), StringComparison.Ordinal)
            || string.Equals(canonicalArtwork, FirstNonEmpty(item.Metadata?.ThumbnailUrl), StringComparison.Ordinal))
            ? canonicalArtwork
            : observations.Select(item => FirstNonEmpty(item.Observation.ThumbnailUrl, item.Metadata?.ThumbnailUrl))
                .FirstOrDefault(url => url is not null);
        var durationSeconds = canonicalMetadata?.DurationSeconds
            ?? observations.Select(item => item.Observation.DurationSeconds ?? item.Metadata?.DurationSeconds)
                .FirstOrDefault(duration => duration is not null);

        var title = FirstNonEmpty(
            canonicalMetadata?.Title,
            observation is null ? null : TrackObservationDisplayFormatter.GetQueueTitle(observation, observationMetadata),
            observation?.Title,
            "Unknown song")!;
        var artist = FirstNonEmpty(
            canonicalMetadata?.Artist,
            BuildArtistDisplay(track),
            observation is null ? null : TrackObservationDisplayFormatter.GetQueueArtist(observation, observationMetadata));

        return new SongMetadata(
            title,
            artist,
            (canonicalMetadata?.Albums ?? [])
                .Where(album => !string.IsNullOrWhiteSpace(album))
                .Select(album => album.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            thumbnailUrl,
            durationSeconds,
            observation?.MatchStatus ?? (track is not null ? TrackMatchingStatuses.Matched : null));
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
