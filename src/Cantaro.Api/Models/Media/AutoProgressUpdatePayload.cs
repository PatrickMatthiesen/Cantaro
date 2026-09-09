namespace Cantaro.Api.Models;

/// <summary>
/// Operation payload for an automatic observation-driven progress update.
/// Stored in <see cref="MediaProviderOperation.PayloadJson"/> so the provider
/// target and concurrency snapshot survive retries.
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

}
