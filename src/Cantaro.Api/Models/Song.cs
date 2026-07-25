namespace Cantaro.Api.Models;

/// <summary>
/// Cantaro's identity for an underlying musical work. Tracks are the distinct
/// recorded or performed versions that belong to the Song.
/// </summary>
public sealed class Song
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<SongTrack> TrackMemberships { get; set; } = [];
    public ICollection<SongCredit> Credits { get; set; } = [];
}
