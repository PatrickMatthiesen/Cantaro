namespace Cantaro.Api.Models;

/// <summary>
/// Maps a canonical Track to external service identifiers
/// (YouTube video ID, Spotify track ID, MusicBrainz ID, etc.)
/// </summary>
public class TrackSourceId
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// Foreign key to Track
    /// </summary>
    public Guid TrackId { get; set; }
    
    /// <summary>
    /// Source type (e.g., "youtube", "spotify", "musicbrainz")
    /// </summary>
    public required string SourceType { get; set; }
    
    /// <summary>
    /// External identifier from the source (e.g., YouTube video ID, Spotify track ID)
    /// </summary>
    public required string ExternalId { get; set; }
    
    /// <summary>
    /// Confidence or quality of the mapping (nullable decimal for metadata matching quality)
    /// </summary>
    public decimal? Confidence { get; set; }
    
    /// <summary>
    /// Additional context stored as JSON (nullable)
    /// </summary>
    public string? OriginMetadata { get; set; }
    
    /// <summary>
    /// When this mapping was last verified (nullable)
    /// </summary>
    public DateTimeOffset? LastVerifiedAt { get; set; }

    /// <summary>
    /// Provider presentation of this exact recording, independent of uploader
    /// authority.
    /// </summary>
    public TrackSourcePresentationKind PresentationKind { get; set; }

    /// <summary>
    /// Confidence in PresentationKind on a compact 0-100 scale.
    /// </summary>
    public byte? PresentationConfidence { get; set; }

    /// <summary>
    /// Whether the provider item is official. Null means unknown.
    /// </summary>
    public bool? IsOfficial { get; set; }
    
    /// <summary>
    /// Navigation property to Track
    /// </summary>
    public Track? Track { get; set; }
}
