namespace Cantaro.Api.Models;

public class MediaProviderAccountStatusDto
{
    public required string ProviderId { get; set; }
    public bool IsConnected { get; set; }
    public string? DisplayName { get; set; }
    public string? ExternalAccountId { get; set; }
    public DateTime? ConnectedAt { get; set; }
}

public class MediaImportDto
{
    public required string ProviderId { get; set; }
    public int ImportedCount { get; set; }
    public int CreatedTitles { get; set; }
    public int CreatedEntries { get; set; }
    public int UpdatedEntries { get; set; }
    public DateTimeOffset ImportedAt { get; set; }
}

public class MediaImportRequestDto
{
    public required string ProviderId { get; set; }
    public required Guid ImportId { get; set; }
    public required string Status { get; set; }
}

public class MediaLibraryImportEventDto
{
    public required string ProviderId { get; set; }
    public required Guid ImportId { get; set; }
    public required string Status { get; set; }
    public int ImportedCount { get; set; }
    public int CreatedTitles { get; set; }
    public int CreatedEntries { get; set; }
    public int UpdatedEntries { get; set; }
    public bool LibraryChanged { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

public class MediaProviderInitialSyncTitleDto
{
    public Guid MediaTitleId { get; set; }
    public required string Title { get; set; }
    public required string MediaKind { get; set; }
}

public class MediaProviderInitialSyncPreviewDto
{
    public required string ProviderId { get; set; }
    public required string Status { get; set; }
    public string? Fingerprint { get; set; }
    public IReadOnlyList<string> RefreshedProviderIds { get; set; } = [];
    public int WillAdd { get; set; }
    public int WillUpdate { get; set; }
    public int AlreadyAligned { get; set; }
    public int ProviderOnly { get; set; }
    public int NeedsMatching { get; set; }
    public int PendingOperations { get; set; }
    public string? Message { get; set; }
    public IReadOnlyList<MediaProviderInitialSyncTitleDto> UnresolvedTitles { get; set; } = [];
    public DateTimeOffset GeneratedAt { get; set; }
}

public class MediaProviderInitialSyncApplyDto
{
    public required string Fingerprint { get; set; }
}

public class MediaProviderInitialSyncResultDto
{
    public required string ProviderId { get; set; }
    public required string Status { get; set; }
    public int Added { get; set; }
    public int Updated { get; set; }
    public int AlreadyAligned { get; set; }
    public int ProviderOnly { get; set; }
    public int NeedsMatching { get; set; }
    public int QueuedOperations { get; set; }
    public Guid? BatchId { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
}

public class MediaProviderInitialSyncProgressDto
{
    public required string ProviderId { get; set; }
    public required string Status { get; set; }
    public int PendingOperations { get; set; }
    public int FailedOperations { get; set; }
    public DateTimeOffset CheckedAt { get; set; }
}

public class MediaProgressUpdateDto
{
    public int? ProgressEpisodes { get; set; }
    public int? ProgressChapters { get; set; }
    public int? ProgressVolumes { get; set; }
}

public class MediaStatusUpdateDto
{
    public required string Status { get; set; }
}

public class MediaScoreUpdateDto
{
    public required decimal? Score { get; set; }
}

public class MediaCatalogLibraryStateDto
{
    public bool IsInLibrary { get; set; }
    public Guid? ViewerStateId { get; set; }
    public Guid? MediaTitleId { get; set; }
    public string? Status { get; set; }
    public decimal? Score { get; set; }
    public int? ProgressEpisodes { get; set; }
    public int? ProgressChapters { get; set; }
    public int? ProgressVolumes { get; set; }
}

public class MediaProviderSearchResultDto
{
    public required string ProviderId { get; set; }
    public required string ProviderMediaId { get; set; }
    public required string Title { get; set; }
    public string? NativeTitle { get; set; }
    public required string MediaKind { get; set; }
    public string? Synopsis { get; set; }
    public string? PosterUrl { get; set; }
    public string? BackgroundUrl { get; set; }
    public int? StartYear { get; set; }
    public int? EpisodeCount { get; set; }
    public int? ChapterCount { get; set; }
    public int? VolumeCount { get; set; }
    public required string PrimaryProgressDimension { get; set; }
    public required string ReleaseStatusDimension { get; set; }
    public MediaCatalogLibraryStateDto? LibraryState { get; set; }
}

public class MediaProviderTitleDetailsDto
{
    /// <summary>
    /// Stable Cantaro identity resolved (or created) for this provider title.
    /// </summary>
    public Guid MediaTitleId { get; set; }
    public required string ProviderId { get; set; }
    public required string ProviderMediaId { get; set; }
    public required string Title { get; set; }
    public string? NativeTitle { get; set; }
    public required string MediaKind { get; set; }
    public string? Synopsis { get; set; }
    public string? PosterUrl { get; set; }
    public string? BackgroundUrl { get; set; }
    public int? StartYear { get; set; }
    public int? EpisodeCount { get; set; }
    public int? ChapterCount { get; set; }
    public int? VolumeCount { get; set; }
    public required string PrimaryProgressDimension { get; set; }
    public required string ReleaseStatusDimension { get; set; }
    public IReadOnlyList<MediaProviderAvailabilityLinkDto> AvailabilityLinks { get; set; } = [];
    /// <summary>
    /// fresh when returned by the provider, stale when served from the last
    /// successful snapshot after a provider failure.
    /// </summary>
    public required string AvailabilityStatus { get; set; }
    public DateTimeOffset? AvailabilityLastVerifiedAt { get; set; }
    public IReadOnlyList<MediaProviderCharacterCreditDto> Characters { get; set; } = [];
    public MediaCatalogLibraryStateDto? LibraryState { get; set; }
}

public class MediaProviderCharacterCreditDto
{
    public required string CharacterId { get; set; }
    public required string Name { get; set; }
    public string? ImageUrl { get; set; }
    public required string Role { get; set; }
    public string? ProviderUrl { get; set; }
    public int Order { get; set; }
}

public class MediaProviderAvailabilityLinkDto
{
    public required string ServiceId { get; set; }
    public required string DisplayName { get; set; }
    public string? Url { get; set; }
    public required string AvailabilityKind { get; set; }
    public string? Notes { get; set; }
    public string? IconUrl { get; set; }
}

public class MediaReleaseMetadataDto
{
    public required string ProviderId { get; set; }
    public required string ProviderMediaId { get; set; }
    public required string ReleaseStatusDimension { get; set; }
    public int? ReleasedCount { get; set; }
    public int? TotalKnownCount { get; set; }
    public DateTimeOffset? NextReleaseAt { get; set; }
    public string? NextReleaseLabel { get; set; }
}
