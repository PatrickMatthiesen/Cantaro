namespace Cantaro.Api.Models;

/// <summary>
/// Represents a unified playlist in Cantaro's system
/// </summary>
public class Playlist
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// Foreign key to User (int type matching existing User model)
    /// </summary>
    public int UserId { get; set; }
    
    /// <summary>
    /// Playlist name
    /// </summary>
    public required string Name { get; set; }
    
    /// <summary>
    /// Playlist description (nullable)
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Additional metadata stored as JSON (tags, visibility, custom fields)
    /// </summary>
    public string? Metadata { get; set; }

    /// <summary>
    /// Provider whose personal playlist data created this local import. Null means Cantaro-owned.
    /// </summary>
    public string? ImportedFromService { get; set; }
    
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    
    /// <summary>
    /// Navigation property to User
    /// </summary>
    public User? User { get; set; }
    
    /// <summary>
    /// Navigation property to playlist entries
    /// </summary>
    public ICollection<PlaylistEntry> Entries { get; set; } = [];
    
    /// <summary>
    /// Navigation property to service playlist mappings
    /// </summary>
    public ICollection<ServicePlaylistMapping> ServiceMappings { get; set; } = [];
}
