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

public class MediaCatalogAddRequestDto
{
    public string Status { get; set; } = MediaLibraryStatuses.Planned;
}

public class MediaCatalogAddResultDto
{
    public required Guid LibraryEntryId { get; set; }
    public required Guid MediaTitleId { get; set; }
    public required string Status { get; set; }
}

public class MediaCatalogLibraryStateDto
{
    public bool IsInLibrary { get; set; }
    public Guid? LibraryEntryId { get; set; }
    public Guid? MediaTitleId { get; set; }
    public string? NormalizedStatus { get; set; }
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
    public MediaCatalogLibraryStateDto? LibraryState { get; set; }
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
