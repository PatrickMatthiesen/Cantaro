namespace Cantaro.Api.Models;

/// <summary>
/// Represents a user-owned media library row synchronized with a provider.
/// </summary>
public class MediaLibraryEntry
{
    public Guid Id { get; set; }

    public int UserId { get; set; }

    public Guid MediaTitleId { get; set; }

    /// <summary>
    /// Optional while disconnected library state is retained after account
    /// removal.
    /// </summary>
    public int? ConnectedServiceAccountId { get; set; }

    public required string Provider { get; set; }

    /// <summary>
    /// Snapshot of the upstream account identifier so rows remain attributable
    /// after disconnect.
    /// </summary>
    public required string ProviderAccountId { get; set; }

    public required string ProviderMediaId { get; set; }

    public string? ProviderLibraryEntryId { get; set; }

    public required string NormalizedStatus { get; set; }

    public string? RawStatus { get; set; }

    public string? RawListName { get; set; }

    public int? ProgressEpisodes { get; set; }

    public int? ProgressChapters { get; set; }

    public int? ProgressVolumes { get; set; }

    public DateTimeOffset? LastSyncedAt { get; set; }

    public DateTimeOffset? LastRemoteUpdateAt { get; set; }

    public DateTimeOffset? LastLocalEditAt { get; set; }

    public string? LastMutationSource { get; set; }

    public string? RawMetadata { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// When true the backend will automatically advance this entry's progress
    /// counter whenever a matched, linked observation carries a numeric progress
    /// hint that exceeds the current value.  Disabled by default; the user must
    /// explicitly opt in per entry.
    /// </summary>
    public bool AutoProgressFromObservations { get; set; }

    public User? User { get; set; }

    public MediaTitle? MediaTitle { get; set; }

    public ConnectedServiceAccount? ConnectedServiceAccount { get; set; }

    public ICollection<MediaProviderOperation> ProviderOperations { get; set; } = [];
}
