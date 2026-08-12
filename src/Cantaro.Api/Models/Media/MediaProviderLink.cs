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

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public MediaTitle? MediaTitle { get; set; }

    public User? LinkedByUser { get; set; }

    public ICollection<MediaLibraryProviderBinding> LibraryBindings { get; set; } = [];
}
