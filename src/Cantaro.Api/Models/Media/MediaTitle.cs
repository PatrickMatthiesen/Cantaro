namespace Cantaro.Api.Models;

/// <summary>
/// Represents a canonical media title in Cantaro's media bounded context.
/// </summary>
public class MediaTitle
{
    public Guid Id { get; set; }

    /// <summary>
    /// Cantaro-owned canonical title used throughout the product.
    /// </summary>
    public required string CanonicalTitle { get; set; }

    public string? SortTitle { get; set; }

    public string? OriginalTitle { get; set; }

    /// <summary>
    /// Broad media kind such as anime, manga, movie, or series.
    /// </summary>
    public required string MediaKind { get; set; }

    public string? Synopsis { get; set; }

    public int? StartYear { get; set; }

    public int? EpisodeCount { get; set; }

    public int? ChapterCount { get; set; }

    public int? VolumeCount { get; set; }

    public bool SupportsEpisodeProgress { get; set; }

    public bool SupportsChapterProgress { get; set; }

    public bool SupportsVolumeProgress { get; set; }

    /// <summary>
    /// True when the title should be treated as completion-only rather than
    /// exposing granular progress fields.
    /// </summary>
    public bool IsCompletionOnly { get; set; }

    /// <summary>
    /// Primary progress dimension used for user progress semantics.
    /// </summary>
    public required string PrimaryProgressDimension { get; set; }

    /// <summary>
    /// Dimension used for release-state semantics.
    /// </summary>
    public required string ReleaseStatusDimension { get; set; }

    /// <summary>
    /// Additional canonical metadata captured as JSON for future expansion.
    /// </summary>
    public string? CanonicalMetadata { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<MediaProviderLink> ProviderLinks { get; set; } = [];

    public ICollection<MediaEpisode> Episodes { get; set; } = [];

    public ICollection<MediaLibraryEntry> LibraryEntries { get; set; } = [];
}
