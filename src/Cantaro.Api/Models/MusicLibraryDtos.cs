namespace Cantaro.Api.Models;

public sealed class MusicLibraryResponse
{
    public required MusicLibrarySummaryDto Summary { get; set; }
    public required List<MusicLibrarySongDto> Songs { get; set; }
    public required List<MusicLibraryPlaylistDto> Playlists { get; set; }
}

public sealed class MusicLibrarySummaryDto
{
    public int SongCount { get; set; }
    public int PlaylistCount { get; set; }
}

public sealed class MusicLibrarySongDto
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public string? Artist { get; set; }
    public string? ThumbnailUrl { get; set; }
    public int? DurationSeconds { get; set; }
    public string? MatchStatus { get; set; }
    public required List<string> SourcePlatforms { get; set; }
    public required List<MusicLibrarySongPlaylistDto> Playlists { get; set; }
}

public sealed class MusicLibrarySongPlaylistDto
{
    public required string PlaylistId { get; set; }
    public required string PlaylistName { get; set; }
    public int Position { get; set; }
}

public sealed class MusicLibraryPlaylistDto
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public int EntryCount { get; set; }
    public required List<MusicLibraryPlaylistServiceDto> Services { get; set; }
}

public sealed class MusicLibraryPlaylistServiceDto
{
    public required string Service { get; set; }
    public required string ServicePlaylistId { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
    public string? LastSyncStatus { get; set; }
}
