namespace Cantaro.Api.Models;

/// <summary>Unresolved import evidence retained for later identity review.</summary>
public sealed class ArtistReconciliationConflict
{
    public Guid Id { get; set; }
    public Guid TrackId { get; set; }
    public Track? Track { get; set; }
    public required string EvidenceKey { get; set; }
    public required string Evidence { get; set; }
    public required string Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
