namespace Cantaro.Api.Models;

/// <summary>A stable artist identity and the metadata supplied by its provider.</summary>
public sealed class ArtistProviderLink
{
    public Guid Id { get; set; }
    public Guid ArtistId { get; set; }
    public Artist? Artist { get; set; }
    public required string Provider { get; set; }
    public required string ExternalId { get; set; }
    public string? ProfileUrl { get; set; }
    public string? ArtworkUrl { get; set; }
    public string? RawMetadata { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
