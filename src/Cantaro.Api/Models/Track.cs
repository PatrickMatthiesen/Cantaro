namespace Cantaro.Api.Models;

/// <summary>
/// Represents a canonical track in Cantaro's system.
/// This is the source of truth for unified music identity.
/// </summary>
public class Track
{
    /// <summary>
    /// Internal canonical TrackID (primary key)
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The underlying Song this musical version belongs to. Nullable while
    /// existing writers transition to creating Songs alongside Tracks.
    /// </summary>
    public Guid? SongId { get; set; }
    
    /// <summary>
    /// Canonical metadata stored as JSON (artist, title, duration, etc.)
    /// </summary>
    public string? CanonicalMetadata { get; set; }
    
    /// <summary>
    /// MusicBrainz Recording ID (nullable - may not always be available)
    /// </summary>
    public string? MbidRecording { get; set; }
    
    /// <summary>
    /// International Standard Recording Code (nullable)
    /// </summary>
    public string? Isrc { get; set; }
    
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Song? Song { get; set; }
    
    /// <summary>
    /// Navigation property to source IDs
    /// </summary>
    public ICollection<TrackSourceId> SourceIds { get; set; } = [];

    /// <summary>
    /// Ordered artist credits. CanonicalMetadata.Artist remains as a legacy
    /// display snapshot while clients migrate to this structured relationship.
    /// </summary>
    public ICollection<TrackArtistCredit> ArtistCredits { get; set; } = [];

    /// <summary>
    /// Auditable musical-version trait assertions. Provider presentation
    /// metadata belongs to SourceIds, not this collection.
    /// </summary>
    public ICollection<TrackVersionTrait> VersionTraits { get; set; } = [];
    
    /// <summary>
    /// Navigation property to playlist entries
    /// </summary>
    public ICollection<PlaylistEntry> PlaylistEntries { get; set; } = [];
}
