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
    /// Current provider presentation classification. This is independent from
    /// the Track's musical-version traits.
    /// </summary>
    public string? PresentationKind { get; set; }
    public decimal? PresentationKindConfidence { get; set; }
    public string? PresentationKindEvidenceSource { get; set; }
    public string? PresentationKindEvidenceIdentity { get; set; }
    public string? PresentationKindEvidenceMethod { get; set; }
    public string? PresentationKindMethodVersion { get; set; }
    public DateTimeOffset? PresentationKindClassifiedAt { get; set; }
    public Guid? PresentationKindRevision { get; set; }

    /// <summary>
    /// Current uploader-authority classification. Official/user is orthogonal
    /// to presentation kind, allowing values such as an official lyric video.
    /// </summary>
    public string? UploaderAuthority { get; set; }
    public decimal? UploaderAuthorityConfidence { get; set; }
    public string? UploaderAuthorityEvidenceSource { get; set; }
    public string? UploaderAuthorityEvidenceIdentity { get; set; }
    public string? UploaderAuthorityEvidenceMethod { get; set; }
    public string? UploaderAuthorityMethodVersion { get; set; }
    public DateTimeOffset? UploaderAuthorityClassifiedAt { get; set; }
    public Guid? UploaderAuthorityRevision { get; set; }
    
    /// <summary>
    /// Navigation property to Track
    /// </summary>
    public Track? Track { get; set; }
}
