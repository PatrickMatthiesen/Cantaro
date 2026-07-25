namespace Cantaro.Api.Models;

/// <summary>
/// A canonical music artist. Names are descriptive metadata; stable provider IDs
/// are the only identities that may be used to reconcile separate rows.
/// </summary>
public sealed class Artist
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? SortName { get; set; }
    public string? MusicBrainzArtistId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<TrackArtistCredit> TrackCredits { get; set; } = [];
    public ICollection<SongCredit> SongCredits { get; set; } = [];
}
