namespace Cantaro.Api.Models;

/// <summary>
/// Represents a track entry in a playlist with position
/// </summary>
public class PlaylistEntry
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// Foreign key to Playlist
    /// </summary>
    public Guid PlaylistId { get; set; }
    
    /// <summary>
    /// Foreign key to Track
    /// </summary>
    public Guid TrackId { get; set; }
    
    /// <summary>
    /// Position/order in the playlist (0-indexed)
    /// </summary>
    public int Position { get; set; }
    
    /// <summary>
    /// When this entry was added to the playlist
    /// </summary>
    public DateTimeOffset AddedAt { get; set; }
    
    /// <summary>
    /// Optional: where this track was first observed/added (e.g., "youtube", "spotify")
    /// </summary>
    public string? SourceService { get; set; }
    
    /// <summary>
    /// Navigation property to Playlist
    /// </summary>
    public Playlist? Playlist { get; set; }
    
    /// <summary>
    /// Navigation property to Track
    /// </summary>
    public Track? Track { get; set; }
}
