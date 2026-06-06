namespace Cantaro.Api.Models;

/// <summary>
/// Reusable episode offset for site observations resolved to a canonical title.
/// </summary>
public class MediaObservationEpisodeOffset
{
    public Guid Id { get; set; }

    public int UserId { get; set; }

    public required string SiteIdentifier { get; set; }

    public Guid MediaTitleId { get; set; }

    public int EpisodeOffset { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public User? User { get; set; }

    public MediaTitle? MediaTitle { get; set; }
}
