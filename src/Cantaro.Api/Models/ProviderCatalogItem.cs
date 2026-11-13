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

    public Guid? TrackId { get; set; }
    public Track? Track { get; set; }
    public RecordingAssociationState AssociationState { get; set; }
    public string? AssociationMethod { get; set; }
    public DateTimeOffset? AssociationVerifiedAt { get; set; }
    public long? AssociationFactsRevision { get; set; }
    public long AssociationRevision { get; set; }
    public string? AssociationEvidenceJson { get; set; }
    public TrackSourcePresentationKind PresentationKind { get; set; }
    public byte? PresentationConfidence { get; set; }
    public bool? IsOfficial { get; set; }
}
