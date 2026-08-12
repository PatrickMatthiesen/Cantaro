namespace Cantaro.Api.Models;

/// <summary>
/// A provider's identity and safe destination path for a canonical episode.
/// </summary>
public class MediaEpisodeProviderIdentity
{
    public Guid Id { get; set; }

    public Guid MediaEpisodeId { get; set; }

    public required string Provider { get; set; }

    public string? ProviderSeriesId { get; set; }

    public string? ProviderSeasonId { get; set; }

    public required string ProviderEpisodeId { get; set; }

    public int? ProviderSeasonNumber { get; set; }

    public int? ProviderEpisodeNumber { get; set; }

    public int? ProviderSequenceNumber { get; set; }

    /// <summary>
    /// Provider-relative path. A destination URL is only reconstructed through
    /// the provider allowlist; arbitrary observed URLs are never served back.
    /// </summary>
    public required string ProviderUrlPath { get; set; }

    public int SeenCount { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>
    /// True only when the canonical episode assignment was established by an
    /// explicit user resolution or an already-authoritative season mapping.
    /// </summary>
    public bool IsTrusted { get; set; }

    /// <summary>
    /// Set when the same provider episode identity is observed against a
    /// different canonical episode. Conflicted identities are not resolved.
    /// </summary>
    public bool HasConflict { get; set; }

    public MediaEpisode? MediaEpisode { get; set; }
}
