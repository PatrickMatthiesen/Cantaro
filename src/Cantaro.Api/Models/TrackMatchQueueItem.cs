namespace Cantaro.Api.Models;

/// <summary>
/// Durable scheduling state for global canonical track matching.
/// Matching outcomes and attempt diagnostics remain on <see cref="TrackObservation"/>.
/// </summary>
public sealed class TrackMatchQueueItem
{
    public Guid TrackObservationId { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public int RetryCount { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }

    public TrackObservation? TrackObservation { get; set; }
}
