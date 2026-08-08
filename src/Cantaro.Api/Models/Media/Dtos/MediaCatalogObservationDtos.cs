using System.ComponentModel.DataAnnotations;

namespace Cantaro.Api.Models;

public static class MediaCatalogObservationLimits
{
    // Some providers expose a long-running show as one season (for example,
    // Boruto has 293 episodes). This is only an ingestion safety bound; once
    // matched, the canonical title's trusted episode count limits recording.
    public const int MaximumEpisodesPerObservation = 2000;
}

/// <summary>
/// A bounded set of episode destinations rendered on a provider's series or
/// season page. This is catalog evidence, not watch-progress evidence.
/// </summary>
public class SubmitMediaCatalogObservationRequest
{
    [Required]
    [MaxLength(64)]
    public required string Provider { get; set; }

    [Required]
    [MaxLength(2048)]
    public required string SeriesUrl { get; set; }

    [Required]
    [MaxLength(256)]
    public required string ProviderSeriesId { get; set; }

    [Required]
    [MaxLength(512)]
    public required string SeriesTitle { get; set; }

    [MaxLength(256)]
    public string? ProviderSeasonId { get; set; }

    [MaxLength(256)]
    public string? SeasonTitle { get; set; }

    [Range(1, int.MaxValue)]
    public int? SeasonNumber { get; set; }

    [Required]
    [MinLength(1)]
    [MaxLength(MediaCatalogObservationLimits.MaximumEpisodesPerObservation)]
    public List<MediaCatalogEpisodeObservationDto> Episodes { get; set; } = [];

    public DateTimeOffset? ObservedAt { get; set; }

    [MaxLength(32)]
    public string? ExtensionVersion { get; set; }
}

public class MediaCatalogEpisodeObservationDto
{
    [Required]
    [MaxLength(256)]
    public required string ProviderEpisodeId { get; set; }

    [Required]
    [MaxLength(2048)]
    public required string ProviderUrl { get; set; }

    [Range(1, int.MaxValue)]
    public int EpisodeNumber { get; set; }

    [MaxLength(512)]
    public string? EpisodeTitle { get; set; }
}

public static class MediaCatalogObservationStatuses
{
    public const string Accepted = "accepted";
    public const string PendingMatch = "pending_match";
    public const string Deduplicated = "deduplicated";
    public const string Rejected = "rejected";
}

public class SubmitMediaCatalogObservationResponse
{
    /// <summary>
    /// accepted | pending_match | deduplicated | rejected
    /// </summary>
    public required string Status { get; set; }

    public string? ObservationId { get; set; }

    public string? MatchStatus { get; set; }

    public string? MatchedMediaTitleId { get; set; }

    public int ObservedEpisodeCount { get; set; }

    public int RecordedEpisodeCount { get; set; }

    public int RejectedEpisodeCount { get; set; }

    public string? Error { get; set; }
}
