namespace Cantaro.Api.Models;

/// <summary>
/// Membership in a provider-defined custom list. Lifecycle groups are modeled
/// by <see cref="MediaLibraryEntry.Status"/> and must not be stored here.
/// </summary>
public class MediaProviderListMembership
{
    public Guid MediaLibraryEntryId { get; set; }

    public required string Name { get; set; }

    public MediaLibraryEntry? MediaLibraryEntry { get; set; }
}
