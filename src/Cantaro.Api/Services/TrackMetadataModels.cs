namespace Cantaro.Api.Services;

/// <summary>
/// Canonical metadata snapshot stored on resolved tracks.
/// </summary>
public class TrackCanonicalMetadata
{
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public int? DurationSeconds { get; set; }
}

/// <summary>
/// Raw snapshot stored on imported observations.
/// </summary>
public class TrackObservationMetadata
{
    public string? SourceType { get; set; }
    public string? ExternalId { get; set; }
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public string? OriginalTitle { get; set; }
    public string? OriginalArtist { get; set; }
    public string? ChannelTitle { get; set; }
    public string? SearchTitle { get; set; }
    public string? SearchArtist { get; set; }
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public int? DurationSeconds { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}
