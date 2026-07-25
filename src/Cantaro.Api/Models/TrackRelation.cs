namespace Cantaro.Api.Models;

public enum TrackRelationType : short
{
    RemixOf,
    EditOf,
    RemasterOf,
    ReRecordingOf,
    DerivedFrom,
    Samples
}

/// <summary>
/// Known lineage between exact recordings. FromTrack is the derived recording
/// and ToTrack is its known source.
/// </summary>
public sealed class TrackRelation
{
    public Guid FromTrackId { get; set; }
    public Guid ToTrackId { get; set; }
    public TrackRelationType RelationType { get; set; }

    public Track? FromTrack { get; set; }
    public Track? ToTrack { get; set; }
}
