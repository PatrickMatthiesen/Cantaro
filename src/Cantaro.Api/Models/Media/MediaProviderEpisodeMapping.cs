namespace Cantaro.Api.Models;

public sealed class MediaProviderEpisodeMapping
{
    public required string Provider { get; set; }
    public required string ProviderEpisodeId { get; set; }
    public Guid MediaTitleId { get; set; }
    public int? CanonicalEpisodeNumber { get; set; }
    public required string MappingSource { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? LastObservedAt { get; set; }
    public MediaTitle? MediaTitle { get; set; }
}
