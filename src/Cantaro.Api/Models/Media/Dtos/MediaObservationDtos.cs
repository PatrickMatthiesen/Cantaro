using System.ComponentModel.DataAnnotations;

namespace Cantaro.Api.Models;

// ---------------------------------------------------------------------------
// Ingestion (extension → backend)
// ---------------------------------------------------------------------------

/// <summary>
/// Payload submitted by the browser extension when it observes a media page.
/// </summary>
public class SubmitMediaObservationRequest
{
    /// <summary>
    /// Well-known identifier for the site (e.g., "anilist", "crunchyroll",
    /// "myanimelist", "unknown"). Case-insensitive.
    /// </summary>
    [Required]
    [MaxLength(64)]
    public required string SiteIdentifier { get; set; }

    /// <summary>
    /// Full URL of the observed page.
    /// </summary>
    [Required]
    [MaxLength(2048)]
    public required string ObservedUrl { get; set; }

    /// <summary>
    /// Stable site-specific media identifier when extractable (e.g., AniList
    /// media ID "154587", MAL ID "52991"). Null when not available.
    /// </summary>
    [MaxLength(256)]
    public string? SiteMediaId { get; set; }

    /// <summary>
    /// Title text extracted from the page.
    /// </summary>
    [Required]
    [MaxLength(512)]
    public required string ObservedTitle { get; set; }

    [MaxLength(512)]
    public string? SeriesTitle { get; set; }

    [MaxLength(512)]
    public string? EpisodeTitle { get; set; }

    public int? EpisodeNumber { get; set; }

    [MaxLength(256)]
    public string? SeasonTitle { get; set; }

    public int? SeasonNumber { get; set; }

    [MaxLength(256)]
    public string? ProviderSeriesId { get; set; }

    [MaxLength(256)]
    public string? ProviderSeasonId { get; set; }

    public int? ProviderSequenceNumber { get; set; }

    [MaxLength(256)]
    public string? NextEpisodeProviderId { get; set; }

    [MaxLength(2048)]
    public string? NextEpisodeUrl { get; set; }

    [MaxLength(512)]
    public string? NextEpisodeTitle { get; set; }

    public int? NextEpisodeNumber { get; set; }

    /// <summary>
    /// Episode destinations already rendered on a provider series page. The
    /// extension submits one bounded batch for the season the user is viewing.
    /// </summary>
    public List<ObservedProviderEpisodeDto> ObservedEpisodes { get; set; } = [];

    /// <summary>
    /// Opaque progress hint extracted from the page (e.g., "Episode 5").
    /// </summary>
    [MaxLength(256)]
    public string? ProgressHint { get; set; }

    public decimal? WatchProgressPercent { get; set; }

    public decimal? DurationSeconds { get; set; }

    public decimal? PositionSeconds { get; set; }

    /// <summary>
    /// When the user was on the page (extension-supplied, not server time).
    /// Defaults to server receipt time when omitted.
    /// </summary>
    public DateTimeOffset? ObservedAt { get; set; }

    /// <summary>
    /// Semver version string of the submitting extension build.
    /// </summary>
    [MaxLength(32)]
    public string? ExtensionVersion { get; set; }
}

public class ObservedProviderEpisodeDto
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

    public List<string> AvailableSubtitleLanguageCodes { get; set; } = [];

    public List<string> AvailableAudioLanguageCodes { get; set; } = [];
}

/// <summary>
/// Returned immediately after observation ingestion so the extension knows the
/// outcome without a round-trip query.
/// </summary>
public class SubmitMediaObservationResponse
{
    public required string ObservationId { get; set; }

    /// <summary>
    /// pending | matched | ambiguous | no_match | rejected
    /// </summary>
    public required string MatchStatus { get; set; }

    /// <summary>
    /// True when the observation was deduplicated to an existing row.
    /// </summary>
    public bool WasDeduplicated { get; set; }

    public string? MatchedMediaTitleId { get; set; }

    public string? MatchedTitle { get; set; }

    public int CandidateCount { get; set; }

    public bool RequiresResolution { get; set; }

    public int? ObservedProgress { get; set; }

    public int SuggestedEpisodeOffset { get; set; }

    public int? ResolvedProgress { get; set; }

    public bool ProgressUpdated { get; set; }

    public MediaObservationDto? Observation { get; set; }

    public List<MediaObservationProviderChoiceDto> ProviderChoices { get; set; } = [];

    public string? ProviderChoicesUnavailableReason { get; set; }
}

// ---------------------------------------------------------------------------
// Review / listing
// ---------------------------------------------------------------------------

public class MediaObservationCandidateDto
{
    public required string CandidateId { get; set; }
    public required string CandidateSource { get; set; }
    public required string MediaTitleId { get; set; }
    public string? Provider { get; set; }
    public string? ProviderMediaId { get; set; }
    public required string Title { get; set; }
    public required string MediaKind { get; set; }
    public decimal Score { get; set; }
    public string? Explanation { get; set; }
    public bool IsAccepted { get; set; }
}

public class MediaObservationProviderChoiceDto
{
    public required string ProviderId { get; set; }
    public required string ProviderMediaId { get; set; }
    public required string Title { get; set; }
    public string? NativeTitle { get; set; }
    public required string MediaKind { get; set; }
    public string? PosterUrl { get; set; }
    public string? BackgroundUrl { get; set; }
    public int? StartYear { get; set; }
    public int? EpisodeCount { get; set; }
    public int? ChapterCount { get; set; }
    public int? VolumeCount { get; set; }
    public required string PrimaryProgressDimension { get; set; }
    public bool IsInLibrary { get; set; }
    public string? LibraryEntryId { get; set; }
    public string? MediaTitleId { get; set; }
}

public class MediaObservationDto
{
    public required string ObservationId { get; set; }
    public required string SiteIdentifier { get; set; }
    public required string ObservedUrl { get; set; }
    public string? SiteMediaId { get; set; }
    public required string ObservedTitle { get; set; }
    public string? ProgressHint { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
    public string? ExtensionVersion { get; set; }
    public required string MatchStatus { get; set; }
    public string? MediaTitleId { get; set; }
    public string? ResolutionNotes { get; set; }
    public int? ObservedProgress { get; set; }
    public int? EpisodeOffset { get; set; }
    public int? ResolvedProgress { get; set; }
    public string? ResolvedLibraryEntryId { get; set; }
    public int MatchAttemptCount { get; set; }
    public DateTimeOffset? LastMatchAttemptedAt { get; set; }
    public string? LastMatchError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<MediaObservationCandidateDto> Candidates { get; set; } = [];
    public List<MediaObservationProviderChoiceDto> ProviderChoices { get; set; } = [];
}

public class MediaObservationSummaryDto
{
    public int TotalUnresolved { get; set; }
    public int Pending { get; set; }
    public int Ambiguous { get; set; }
    public int NoMatch { get; set; }
}

// ---------------------------------------------------------------------------
// Resolution actions
// ---------------------------------------------------------------------------

public class ResolveMediaObservationRequest
{
    public Guid? CandidateId { get; set; }

    [MaxLength(64)]
    public string? ProviderId { get; set; }

    [MaxLength(256)]
    public string? ProviderMediaId { get; set; }

    public int EpisodeOffset { get; set; }

    public bool AddToLibraryConfirmed { get; set; }
}
