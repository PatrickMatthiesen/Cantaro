namespace Cantaro.Api.Models;

/// <summary>
/// A provider's logical episode content. One content can have several playable
/// variants, for example one Crunchyroll watch identity per audio locale.
/// </summary>
public class MediaEpisodeProviderContent
{
    public Guid Id { get; set; }

    public Guid MediaEpisodeId { get; set; }

    public required string Provider { get; set; }

    /// <summary>
    /// Stable provider content identity when the provider exposes one. This is
    /// null for opaque identities that Cantaro can only group canonically.
    /// </summary>
    public string? ProviderContentKey { get; set; }

    public string? ProviderSeriesId { get; set; }

    public string? ProviderSeasonId { get; set; }

    public int? ProviderSeasonNumber { get; set; }

    public int? ProviderEpisodeNumber { get; set; }

    public int? ProviderSequenceNumber { get; set; }

    public MediaEpisode? MediaEpisode { get; set; }

    public ICollection<MediaEpisodeProviderIdentity> Variants { get; set; } = [];
}
