namespace Cantaro.Api.Models;

/// <summary>
/// Operation payload for an automatic observation-driven progress update.
/// Stored in <see cref="MediaProviderOperation.PayloadJson"/> so provenance
/// travels with the queued operation and survives retries.
/// </summary>
public class AutoProgressUpdatePayload
{
    /// <summary>Provider-specific ID of the media entry being updated.</summary>
    public required string ProviderMediaId { get; set; }

    public int? ProgressEpisodes { get; set; }

    public int? ProgressChapters { get; set; }

    public int? ProgressVolumes { get; set; }

    /// <summary>
    /// Snapshot of the entry's <c>LastRemoteUpdateAt</c> at the time the
    /// operation was enqueued.  If the provider reports a newer value before
    /// we execute, the operation processor will skip the write to avoid
    /// clobbering remote state.
    /// </summary>
    public DateTimeOffset? LastKnownRemoteUpdateAt { get; set; }

    // ── Provenance ──────────────────────────────────────────────────────────

    /// <summary>The <see cref="MediaObservation.Id"/> that triggered this update.</summary>
    public string? TriggeredByObservationId { get; set; }

    /// <summary>Site identifier of the originating observation (e.g., "crunchyroll").</summary>
    public string? ObservedSiteIdentifier { get; set; }

    /// <summary>Raw progress hint string from the originating observation.</summary>
    public string? ObservedProgressHint { get; set; }

    /// <summary>Match confidence score of the observation → MediaTitle pairing.</summary>
    public decimal? ObservationMatchScore { get; set; }

    /// <summary>UTC timestamp when the auto-progress rule was triggered.</summary>
    public DateTimeOffset TriggeredAt { get; set; }
}
