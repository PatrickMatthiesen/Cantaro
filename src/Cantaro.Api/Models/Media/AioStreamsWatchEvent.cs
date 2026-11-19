namespace Cantaro.Api.Models;

internal sealed class AioStreamsWatchEvent
{
    public long Id { get; set; }
    public Guid MediaLibraryEntryId { get; set; }
    public Guid MediaEpisodeId { get; set; }
    public required string EventId { get; set; }
    public long At { get; set; }
    public long? PositionMs { get; set; }
    public long? DurationMs { get; set; }
    public bool Played { get; set; }
}
