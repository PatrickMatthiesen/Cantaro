namespace Cantaro.Api.Models;

/// <summary>
/// Represents a canonical track in Cantaro's system.
/// This is the source of truth for unified music identity.
/// </summary>
public class Track
{
    public Guid? PreferredCatalogItemId { get; set; }
    public ProviderCatalogItem? PreferredCatalogItem { get; set; }
    /// <summary>
    /// Internal canonical TrackID (primary key)
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Historical metadata cleared by the cutover. Current reads use catalog facts.
    /// </summary>
    public string? CanonicalMetadata { get; set; }

    /// <summary>
    /// Queryable shared display projection assigned from verified provider facts.
    /// This is not a separate canonical identity field.
    /// </summary>
    public string? SearchTitle { get; set; }

    /// <summary>
    /// Queryable shared display projection assigned from verified provider facts.
    /// Artist identity continues to live in the structured artist-credit model.
    /// </summary>
    public string? SearchArtist { get; set; }
    
    /// <summary>
    /// MusicBrainz Recording ID (nullable - may not always be available)
    /// </summary>
    public string? MbidRecording { get; set; }
    
    /// <summary>
    /// International Standard Recording Code (nullable)
    /// </summary>
    public string? Isrc { get; set; }

    /// <summary>
    /// Compact, queryable classification of the recording/version.
    /// </summary>
    public TrackVersionFlags VersionFlags { get; set; }

    /// <summary>
    /// Optional current-state classifier evidence. Ordinary Track reads should
    /// not project this JSON unless the matching/review workflow needs it.
    /// </summary>
    public string? VersionEvidence { get; set; }
    
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<SongTrack> SongMemberships { get; set; } = [];
    
    /// <summary>
    /// Navigation property to source IDs
    /// </summary>
    public ICollection<TrackSourceId> SourceIds { get; set; } = [];

    /// <summary>
    /// Ordered artist credits. CanonicalMetadata.Artist remains as a legacy
    /// display snapshot while clients migrate to this structured relationship.
    /// </summary>
    public ICollection<TrackArtistCredit> ArtistCredits { get; set; } = [];

    public ICollection<TrackRelation> OutgoingRelations { get; set; } = [];
    public ICollection<TrackRelation> IncomingRelations { get; set; } = [];
    
    /// <summary>
    /// Navigation property to playlist entries
    /// </summary>
    public ICollection<PlaylistEntry> PlaylistEntries { get; set; } = [];
}
