namespace Cantaro.Api.Models;

/// <summary>
/// Composition membership for an exact recording. A mashup or medley can
/// realize more than one Song; membership does not imply substitutability.
/// </summary>
public sealed class SongTrack
{
    public Guid SongId { get; set; }
    public Guid TrackId { get; set; }

    public Song? Song { get; set; }
    public Track? Track { get; set; }
}
