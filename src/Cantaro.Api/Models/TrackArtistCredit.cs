namespace Cantaro.Api.Models;

public enum TrackArtistRole
{
    Primary,
    Featured,
    Composer,
    Remixer
}

/// <summary>
/// An ordered, role-aware artist credit on a canonical track. CreditedName is
/// retained separately from the canonical artist name so provider spelling and
/// collaboration formatting can be reproduced faithfully.
/// </summary>
public sealed class TrackArtistCredit
{
    public Guid Id { get; set; }
    public Guid TrackId { get; set; }
    public Guid ArtistId { get; set; }
    public TrackArtistRole Role { get; set; }
    public int Position { get; set; }
    public required string CreditedName { get; set; }

    public Track? Track { get; set; }
    public Artist? Artist { get; set; }
}
