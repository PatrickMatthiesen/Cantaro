namespace Cantaro.Api.Models;

public enum CatalogVisibility { Unknown, Public, Withdrawn }
public enum RecordingAssociationState { Unknown, Verified, Conflicted }

/// <summary>Independently acquired provider facts, without a required recording association.</summary>
public sealed class ProviderCatalogItem
{
    public Guid Id { get; set; }
    public required string SourceType { get; set; }
    public required string ExternalId { get; set; }
    public required string FactsJson { get; set; }
    public string? NormalizedTitle { get; set; }
    public string? NormalizedArtist { get; set; }
    public string? NormalizedIsrc { get; set; }
    public string? NormalizedMusicBrainzRecordingId { get; set; }
    public string? VerificationMethod { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public DateTimeOffset? RevalidateAfter { get; set; }
    public CatalogVisibility VisibilityState { get; set; }
    public long Revision { get; set; }
}
