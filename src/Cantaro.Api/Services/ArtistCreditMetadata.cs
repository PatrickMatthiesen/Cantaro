using Cantaro.Api.Models;

namespace Cantaro.Api.Services;

/// <summary>One ordered credit from a provider, before canonical reconciliation.</summary>
public sealed record ArtistCreditMetadata
{
    public required string Name { get; init; }
    public string? CanonicalName { get; init; }
    public string? SortName { get; init; }
    public string? MusicBrainzArtistId { get; init; }
    public string? Provider { get; init; }
    public string? ExternalId { get; init; }
    public string? ProfileUrl { get; init; }
    public string? ArtworkUrl { get; init; }
    public string? RawMetadata { get; init; }
    public string? JoinPhrase { get; init; }
    public TrackArtistRole? Role { get; init; }
}
