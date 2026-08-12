namespace Cantaro.Api.Models;

public class MediaLibraryPageDto
{
    public required IReadOnlyList<MediaLibraryListItemDto> Items { get; set; }
    public required IReadOnlyList<string> AvailableProviderListNames { get; set; }
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}

public class MediaLibraryListItemDto
{
    public Guid Id { get; set; }
    public Guid MediaTitleId { get; set; }
    public required string CanonicalTitle { get; set; }
    public string? OriginalTitle { get; set; }
    public string? PosterUrl { get; set; }
    public required string MediaKind { get; set; }
    public required string Status { get; set; }
    public int? ProgressEpisodes { get; set; }
    public int? ProgressChapters { get; set; }
    public int? ProgressVolumes { get; set; }
    public int? EpisodeCount { get; set; }
    public int? ChapterCount { get; set; }
    public int? VolumeCount { get; set; }
    public int? ReleasedCount { get; set; }
    public int? AvailableReleasedCount { get; set; }
    public required string PrimaryProgressDimension { get; set; }
    public required string Provider { get; set; }
    public required string ProviderMediaId { get; set; }
    public required IReadOnlyList<string> ProviderListNames { get; set; }
    public bool IsConnected { get; set; }
    public DateTimeOffset? NextReleaseAt { get; set; }
    public string? NextReleaseLabel { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class MediaTitleDetailDto
{
    public Guid Id { get; set; }
    public required string CanonicalTitle { get; set; }
    public string? OriginalTitle { get; set; }
    public required string MediaKind { get; set; }
    public string? Format { get; set; }
    public string? Synopsis { get; set; }
    public string? PosterUrl { get; set; }
    public string? BackgroundUrl { get; set; }
    public int? StartYear { get; set; }
    public int? EpisodeCount { get; set; }
    public int? ChapterCount { get; set; }
    public int? VolumeCount { get; set; }
    public int? ReleasedCount { get; set; }
    public DateTimeOffset? NextReleaseAt { get; set; }
    public string? NextReleaseLabel { get; set; }
    public required string PrimaryProgressDimension { get; set; }
    public required string ReleaseStatusDimension { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public required IReadOnlyList<MediaProviderLinkSummaryDto> ProviderLinks { get; set; }
}

public class MediaViewerStateDto
{
    public Guid Id { get; set; }
    public Guid MediaTitleId { get; set; }
    public required string Status { get; set; }
    public int? ProgressEpisodes { get; set; }
    public int? ProgressChapters { get; set; }
    public int? ProgressVolumes { get; set; }
    public DateTimeOffset? LastLocalEditAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public required IReadOnlyList<MediaViewerProviderBindingDto> ProviderBindings { get; set; }
}

public class MediaViewerStateCreateDto
{
    public string Status { get; set; } = MediaLibraryStatuses.Planned;
}

public class MediaViewerProviderBindingDto
{
    public Guid Id { get; set; }
    public required string Provider { get; set; }
    public required string ProviderMediaId { get; set; }
    public string? ProviderLibraryEntryId { get; set; }
    public required IReadOnlyList<string> ProviderListNames { get; set; }
    public bool IsConnected { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
    public DateTimeOffset? LastRemoteUpdateAt { get; set; }
}

public class MediaProviderLinkSummaryDto
{
    public Guid Id { get; set; }
    public required string Provider { get; set; }
    public required string ExternalId { get; set; }
    public string? ExternalUrl { get; set; }
    public required string LinkSource { get; set; }
    public DateTimeOffset? LastVerifiedAt { get; set; }
}

public class MediaLinkRequestDto
{
    public required string ProviderId { get; set; }
    public required string ProviderMediaId { get; set; }

    /// <summary>
    /// Confirms replacement of this title's existing identity for the same provider.
    /// This never permits taking an identity from another canonical title.
    /// </summary>
    public bool ConfirmReplacement { get; set; }
}

public class MediaLinkConflictDto
{
    public required string Error { get; set; }
    public required string Code { get; set; }
    public Guid? ConflictingMediaTitleId { get; set; }
    public string? ConflictingCanonicalTitle { get; set; }
    public string? CurrentProviderMediaId { get; set; }
}

public class MediaContinueWatchingDto
{
    /// <summary>
    /// direct | series_fallback | completed | unavailable | conflict
    /// </summary>
    public required string Outcome { get; set; }

    public int? EpisodeNumber { get; set; }

    public string? Provider { get; set; }

    public string? Url { get; set; }
}

public class MediaEpisodeCatalogDto
{
    public required IReadOnlyList<MediaStreamingDestinationDto> SeriesDestinations { get; set; }
    public required IReadOnlyList<MediaEpisodeDestinationDto> Episodes { get; set; }
    public required MediaReleaseAvailabilityDto ReleaseAvailability { get; set; }
}

public class MediaReleaseAvailabilityDto
{
    public int? MaxReleasedEpisodes { get; set; }
    public required IReadOnlyList<MediaReleaseLanguageAvailabilityDto> Languages { get; set; }
}

public class MediaReleaseLanguageAvailabilityDto
{
    public required string LanguageCode { get; set; }
    public int? SubReleasedEpisodes { get; set; }
    public int? DubReleasedEpisodes { get; set; }
}

public class MediaEpisodeDestinationDto
{
    public int EpisodeNumber { get; set; }
    public string? Title { get; set; }
    public required IReadOnlyList<string> AvailableSubtitleLanguageCodes { get; set; }
    public required IReadOnlyList<string> AvailableAudioLanguageCodes { get; set; }
    public required IReadOnlyList<MediaStreamingDestinationDto> Destinations { get; set; }
    public int SeenCount { get; set; }
    public bool HasConflict { get; set; }
}

public class MediaStreamingDestinationDto
{
    public required string ServiceId { get; set; }
    public required string Url { get; set; }
    public int SeenCount { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}
