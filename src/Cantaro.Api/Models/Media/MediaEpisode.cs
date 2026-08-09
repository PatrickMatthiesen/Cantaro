namespace Cantaro.Api.Models;

/// <summary>
/// Cantaro's canonical episode number for a media title.
/// </summary>
public class MediaEpisode
{
    public Guid Id { get; set; }

    public Guid MediaTitleId { get; set; }

    public int EpisodeNumber { get; set; }

    public string? Title { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public MediaTitle? MediaTitle { get; set; }

    public ICollection<MediaEpisodeProviderIdentity> ProviderIdentities { get; set; } = [];
}
