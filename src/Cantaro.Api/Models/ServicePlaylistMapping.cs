namespace Cantaro.Api.Models;

/// <summary>
/// Maps a Cantaro Playlist to an external service playlist (e.g., YouTube, Spotify)
/// </summary>
public class ServicePlaylistMapping
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// Foreign key to Playlist
    /// </summary>
    public Guid PlaylistId { get; set; }

    /// <summary>
    /// Connected account that supplied this provider-derived mapping.
    /// </summary>
    public int ConnectedServiceAccountId { get; set; }

    public int UserId { get; set; }
    public string ExternalAccountId { get; set; } = "";
    public string State { get; set; } = "active";
    public string? BaselineJson { get; set; }
    public string? PendingWriteJson { get; set; }
    public string? MatchingProgressJson { get; set; }
    public int MatchingProcessedCount { get; set; }
    public int MatchingTotalCount { get; set; }
    public string? BaselineName { get; set; }
    public string? PendingName { get; set; }
    public string? RejectedName { get; set; }
    public string? DesiredName { get; set; }
    public int UnresolvedCount { get; set; }
    public long CanonicalRevision { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
    public string? InitialMode { get; set; }
    
    /// <summary>
    /// Service name (e.g., "youtube", "spotify")
    /// </summary>
    public required string Service { get; set; }
    
    /// <summary>
    /// External playlist ID from the service
    /// </summary>
    public required string ServicePlaylistId { get; set; }
    
    /// <summary>
    /// Sync mode: "from_cantaro", "bidirectional", "import_only"
    /// </summary>
    public required string SyncMode { get; set; }
    
    /// <summary>
    /// When the playlist was last synced (nullable)
    /// </summary>
    public DateTimeOffset? LastSyncedAt { get; set; }
    
    /// <summary>
    /// Last sync status (nullable): "success", "partial_failure", "error"
    /// </summary>
    public string? LastSyncStatus { get; set; }
    
    /// <summary>
    /// Navigation property to Playlist
    /// </summary>
    public Playlist? Playlist { get; set; }

    public ConnectedServiceAccount? ConnectedServiceAccount { get; set; }
}
