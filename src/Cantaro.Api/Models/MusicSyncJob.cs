namespace Cantaro.Api.Models;

public static class MusicSyncJobStatuses
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";

    public static bool IsTerminal(string status) => status is Completed or Failed;
}

public sealed class MusicSyncJob
{
    public Guid Id { get; set; }
    public int UserId { get; set; }
    public required string Service { get; set; }
    public required string PlaylistsJson { get; set; }
    public required string Status { get; set; }
    public int PlaylistCount { get; set; }
    public int SongCount { get; set; }
    public int EstimatedNewSongCount { get; set; }
    public int ProcessedPlaylistCount { get; set; }
    public int ProcessedSongCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public string? CurrentPlaylistName { get; set; }
    public string? CurrentSongName { get; set; }
    public string? ResultsJson { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public User? User { get; set; }
}

public sealed record MusicSyncJobPlaylist(string Id, string Name, int SongCount);

