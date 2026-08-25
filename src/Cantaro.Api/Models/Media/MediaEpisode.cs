namespace Cantaro.Api.Models;

using System.ComponentModel.DataAnnotations.Schema;

/// <summary>
/// Cantaro's canonical episode number for a media title.
/// </summary>
public class MediaEpisode
{
    public Guid Id { get; set; }

    public Guid MediaTitleId { get; set; }

    public int EpisodeNumber { get; set; }

    public string? Title { get; set; }

    /// <summary>
    /// BCP-47 language codes for known subtitle tracks on this episode.
    /// </summary>
    public string[] AvailableSubtitleLanguageCodes { get; set; } = [];

    /// <summary>
    /// BCP-47 language codes for known audio tracks on this episode.
    /// </summary>
    public string[] AvailableAudioLanguageCodes { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public MediaTitle? MediaTitle { get; set; }

    public ICollection<MediaEpisodeProviderContent> ProviderContents { get; set; } = [];

    [NotMapped]
    public ICollection<MediaEpisodeProviderIdentity> ProviderIdentities =>
        ProviderContents.SelectMany(content => content.Variants).ToList();
}
