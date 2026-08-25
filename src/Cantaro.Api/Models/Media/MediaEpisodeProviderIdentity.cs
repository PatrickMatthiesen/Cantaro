namespace Cantaro.Api.Models;

using System.ComponentModel.DataAnnotations.Schema;

/// <summary>
/// A provider's playable identity and safe destination path for logical episode content.
/// </summary>
public class MediaEpisodeProviderIdentity
{
    public Guid Id { get; set; }

    public Guid MediaEpisodeProviderContentId { get; set; }

    public required string Provider { get; set; }

    public required string ProviderEpisodeId { get; set; }

    /// <summary>BCP-47 audio locale for this playable variant, when known.</summary>
    public string? AudioLocale { get; set; }

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

    public MediaEpisodeProviderContent? Content { get; set; }

    // Compatibility accessors keep callers focused on the canonical episode
    // while persistence remains normalized through Content.
    [NotMapped]
    public Guid MediaEpisodeId
    {
        get => Content?.MediaEpisodeId ?? Guid.Empty;
        set => EnsureContent().MediaEpisodeId = value;
    }

    [NotMapped]
    public MediaEpisode? MediaEpisode
    {
        get => Content?.MediaEpisode;
        set
        {
            var content = EnsureContent();
            content.MediaEpisode = value;
            if (value is not null) content.MediaEpisodeId = value.Id;
        }
    }

    [NotMapped]
    public string? ProviderSeriesId { get => Content?.ProviderSeriesId; set => EnsureContent().ProviderSeriesId = value; }

    [NotMapped]
    public string? ProviderSeasonId { get => Content?.ProviderSeasonId; set => EnsureContent().ProviderSeasonId = value; }

    [NotMapped]
    public int? ProviderSeasonNumber { get => Content?.ProviderSeasonNumber; set => EnsureContent().ProviderSeasonNumber = value; }

    [NotMapped]
    public int? ProviderEpisodeNumber { get => Content?.ProviderEpisodeNumber; set => EnsureContent().ProviderEpisodeNumber = value; }

    [NotMapped]
    public int? ProviderSequenceNumber { get => Content?.ProviderSequenceNumber; set => EnsureContent().ProviderSequenceNumber = value; }

    private MediaEpisodeProviderContent EnsureContent()
    {
        if (Content is not null) return Content;
        Content = new MediaEpisodeProviderContent
        {
            Id = Guid.NewGuid(),
            Provider = Provider
        };
        MediaEpisodeProviderContentId = Content.Id;
        return Content;
    }
}
