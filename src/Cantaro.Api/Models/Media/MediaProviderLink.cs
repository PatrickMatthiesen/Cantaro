namespace Cantaro.Api.Models;

/// <summary>
/// Maps a canonical media title to an external provider catalog identity.
/// </summary>
public class MediaProviderLink
{
    public Guid Id { get; set; }

    public Guid MediaTitleId { get; set; }

    public required string Provider { get; set; }

    public required string ExternalId { get; set; }

    public string? ExternalUrl { get; set; }

    public decimal? Confidence { get; set; }

    public string? RawMetadata { get; set; }

    /// <summary>
    /// Tracks whether the mapping was imported, automatic, or user-confirmed.
    /// </summary>
    public required string LinkSource { get; set; }

    public int? LinkedByUserId { get; set; }

    public DateTimeOffset? LastVerifiedAt { get; set; }

    /// <summary>
    /// Last time this provider identity's outgoing relation collection was read.
    /// This is distinct from identity verification so an empty relation graph can
    /// still be cached without inventing an edge row.
    /// </summary>
    public DateTimeOffset? RelationsLastVerifiedAt { get; set; }

    /// <summary>
    /// Identifies the provider graph snapshot that verified this title's
    /// outgoing relations. Continuity is complete only when every source in
    /// the component was verified by the same snapshot.
    /// </summary>
    public Guid? RelationsSnapshotId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public MediaTitle? MediaTitle { get; set; }

    public User? LinkedByUser { get; set; }

    public ICollection<MediaLibraryProviderBinding> LibraryBindings { get; set; } = [];
}
