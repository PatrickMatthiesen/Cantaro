namespace Cantaro.Api.Models;

/// <summary>
/// Represents the Cantaro-owned state for one media title in one user's library.
/// Provider/account synchronization belongs to <see cref="MediaLibraryProviderBinding"/>.
/// </summary>
public class MediaLibraryEntry
{
    public Guid Id { get; set; }

    public int UserId { get; set; }

    public Guid MediaTitleId { get; set; }

    public required string Status { get; set; }

    /// <summary>
    /// Cantaro's canonical personal score in the inclusive range 1..100.
    /// Null means that the user has not rated this title.
    /// </summary>
    public decimal? Score { get; set; }

    public int? ProgressEpisodes { get; set; }

    public int? ProgressChapters { get; set; }

    public int? ProgressVolumes { get; set; }

    public DateTimeOffset? LastLocalEditAt { get; set; }

    public string? LastMutationSource { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public User? User { get; set; }

    public MediaTitle? MediaTitle { get; set; }

    public ICollection<MediaLibraryProviderBinding> ProviderBindings { get; set; } = [];
}
