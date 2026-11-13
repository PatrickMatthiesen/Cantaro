namespace Cantaro.Api.Models;

public enum ObservationInputKind { Reference, Public, Private }

/// <summary>
/// Represents an imported source item that has not necessarily been resolved to a canonical Track yet.
/// </summary>
public class TrackObservation
{
    public int OwnerUserId { get; set; }
    public Guid? ProviderCatalogItemId { get; set; }
    public ProviderCatalogItem? ProviderCatalogItem { get; set; }
    public ObservationInputKind InputKind { get; set; }
    public Guid Id { get; set; }

    /// <summary>
    /// Source type (e.g. "youtube", "spotify").
    /// </summary>
    public required string SourceType { get; set; }

    /// <summary>
    /// Source-specific identifier (e.g. YouTube video ID).
    /// </summary>
    public required string ExternalId { get; set; }

    /// <summary>
    /// Allowlisted owner-only matching input. Shared provider metadata lives on the catalog item.
    /// </summary>
    public string? PrivateInputJson { get; set; }

    /// <summary>
    /// Human-readable title captured from the source.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Human-readable artist/channel captured from the source.
    /// </summary>
    public string? Artist { get; set; }

    /// <summary>
    /// Source thumbnail, if any.
    /// </summary>
    public string? ThumbnailUrl { get; set; }

    /// <summary>
    /// Duration in whole seconds when known.
    /// </summary>
    public int? DurationSeconds { get; set; }

    /// <summary>
    /// Matching lifecycle state: pending, matched, ambiguous, or no_match.
    /// </summary>
    public required string MatchStatus { get; set; }

    /// <summary>
    /// Canonical Track created or linked after resolution succeeds.
    /// </summary>
    public Guid? TrackId { get; set; }

    /// <summary>
    /// How many matching attempts have been made.
    /// </summary>
    public int MatchAttemptCount { get; set; }

    /// <summary>
    /// When the observation was last evaluated by the matcher.
    /// </summary>
    public DateTimeOffset? LastMatchAttemptedAt { get; set; }

    /// <summary>
    /// Last matching error, if any.
    /// </summary>
    public string? LastMatchError { get; set; }

    /// <summary>
    /// Why the current resolution state was chosen.
    /// </summary>
    public string? ResolutionNotes { get; set; }

    /// <summary>
    /// Candidate accepted during review, if any.
    /// </summary>
    public Guid? AcceptedCandidateId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Track? Track { get; set; }
    public ICollection<TrackResolutionCandidate> Candidates { get; set; } = [];
    public ICollection<PlaylistEntry> PlaylistEntries { get; set; } = [];
    public TrackMatchQueueItem? MatchQueueItem { get; set; }
}
