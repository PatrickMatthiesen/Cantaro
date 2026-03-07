namespace Cantaro.Api.Models;

/// <summary>
/// A candidate canonical identity considered while resolving a TrackObservation.
/// </summary>
public class TrackResolutionCandidate
{
    public Guid Id { get; set; }
    public Guid TrackObservationId { get; set; }

    /// <summary>
    /// Candidate provider/source (e.g. musicbrainz, manual).
    /// </summary>
    public required string CandidateSource { get; set; }

    /// <summary>
    /// Provider-specific candidate ID.
    /// </summary>
    public required string ExternalId { get; set; }

    public required string Title { get; set; }
    public string? Artist { get; set; }
    public string? MbidRecording { get; set; }
    public string? Isrc { get; set; }
    public int? DurationSeconds { get; set; }

    /// <summary>
    /// Matching score from 0.0 to 1.0.
    /// </summary>
    public decimal Score { get; set; }

    /// <summary>
    /// Human-readable explanation shown in the review UI.
    /// </summary>
    public string? Explanation { get; set; }

    /// <summary>
    /// Raw provider payload for debugging or replay.
    /// </summary>
    public string? RawMetadata { get; set; }

    public bool IsAccepted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public TrackObservation? TrackObservation { get; set; }
}
