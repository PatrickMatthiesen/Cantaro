namespace Cantaro.Api.Models;

/// <summary>
/// A candidate canonical MediaTitle considered while resolving a MediaObservation.
/// </summary>
public class MediaObservationCandidate
{
    public Guid Id { get; set; }

    public Guid MediaObservationId { get; set; }

    /// <summary>
    /// Where this candidate was generated from (e.g., "provider_link_exact",
    /// "library_title_search", "provider_search").
    /// </summary>
    public required string CandidateSource { get; set; }

    /// <summary>
    /// The canonical MediaTitle this candidate points to.
    /// </summary>
    public Guid MediaTitleId { get; set; }

    /// <summary>
    /// The provider media ID for convenient display linking.
    /// </summary>
    public string? ProviderMediaId { get; set; }

    /// <summary>
    /// Provider that owns the ProviderMediaId (e.g., "anilist").
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Canonical title at time candidate was generated.
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Media kind at time candidate was generated (e.g., "anime", "manga").
    /// </summary>
    public required string MediaKind { get; set; }

    /// <summary>
    /// Matching score in [0.0, 1.0].
    /// </summary>
    public decimal Score { get; set; }

    /// <summary>
    /// Human-readable explanation of how this score was computed.
    /// </summary>
    public string? Explanation { get; set; }

    public bool IsAccepted { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public MediaObservation? MediaObservation { get; set; }

    public MediaTitle? MediaTitle { get; set; }
}
