namespace Cantaro.Api.Models;

/// <summary>
/// Represents a media title observation submitted by the browser extension.
/// User-scoped: each user's observations are independent and never shared.
/// </summary>
public class MediaObservation
{
    public Guid Id { get; set; }

    /// <summary>
    /// The user who owns this observation.
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// Identifier for the site where the observation was made (e.g., "anilist",
    /// "crunchyroll", "myanimelist", "unknown").
    /// </summary>
    public required string SiteIdentifier { get; set; }

    /// <summary>
    /// Full URL that was observed.
    /// </summary>
    public required string ObservedUrl { get; set; }

    /// <summary>
    /// Stable site-specific media identifier when extractable from the URL or
    /// page (e.g., AniList media ID, MAL ID). Null when not available.
    /// </summary>
    public string? SiteMediaId { get; set; }

    /// <summary>
    /// Title text extracted from the page at observation time.
    /// </summary>
    public required string ObservedTitle { get; set; }

    /// <summary>
    /// Opaque progress hint extracted from the page (e.g., "Episode 5",
    /// "Chapter 12"). Stored as-is; the backend does not parse this for MVP.
    /// </summary>
    public string? ProgressHint { get; set; }

    /// <summary>
    /// When the user was observed on the page (sent by the extension, not the
    /// server receipt time).
    /// </summary>
    public DateTimeOffset ObservedAt { get; set; }

    /// <summary>
    /// Version string of the extension that submitted the observation.
    /// </summary>
    public string? ExtensionVersion { get; set; }

    /// <summary>
    /// Raw JSON payload from the extension for replay and debugging.
    /// </summary>
    public string? RawPayload { get; set; }

    /// <summary>
    /// Matching/resolution lifecycle: pending, matched, ambiguous, no_match, rejected.
    /// </summary>
    public required string MatchStatus { get; set; }

    /// <summary>
    /// Canonical MediaTitle resolved for this observation, when matched.
    /// </summary>
    public Guid? MediaTitleId { get; set; }

    /// <summary>
    /// The candidate accepted by the user or automatic matcher.
    /// </summary>
    public Guid? AcceptedCandidateId { get; set; }

    /// <summary>
    /// Human-readable notes about the current resolution state.
    /// </summary>
    public string? ResolutionNotes { get; set; }

    public int MatchAttemptCount { get; set; }

    public DateTimeOffset? LastMatchAttemptedAt { get; set; }

    public string? LastMatchError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public User? User { get; set; }

    public MediaTitle? MediaTitle { get; set; }

    public ICollection<MediaObservationCandidate> Candidates { get; set; } = [];
}
