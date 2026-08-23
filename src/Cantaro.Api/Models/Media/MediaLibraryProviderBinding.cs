namespace Cantaro.Api.Models;

/// <summary>
/// Connects a Cantaro-owned user media entry to one provider catalog identity
/// and one provider account. Remote metadata and synchronization timestamps are
/// deliberately kept here instead of on either the global title or user state.
/// </summary>
public class MediaLibraryProviderBinding
{
    public Guid Id { get; set; }

    public Guid MediaLibraryEntryId { get; set; }

    public Guid MediaProviderLinkId { get; set; }

    public int? ConnectedServiceAccountId { get; set; }

    /// <summary>
    /// Retained provider account identity, including after disconnection.
    /// </summary>
    public required string ProviderAccountId { get; set; }

    public string? ProviderLibraryEntryId { get; set; }

    public DateTimeOffset? LastSyncedAt { get; set; }

    public DateTimeOffset? LastRemoteUpdateAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public MediaLibraryEntry? MediaLibraryEntry { get; set; }

    public MediaProviderLink? MediaProviderLink { get; set; }

    public ConnectedServiceAccount? ConnectedServiceAccount { get; set; }

    public ICollection<MediaProviderOperation> ProviderOperations { get; set; } = [];

    public ICollection<MediaProviderListMembership> ProviderListMemberships { get; set; } = [];
}
