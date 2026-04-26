namespace Cantaro.Api.Models;

public class MediaLibraryPageDto
{
    public required IReadOnlyList<MediaLibraryListItemDto> Items { get; set; }
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
    public required string MediaKind { get; set; }
    public required string NormalizedStatus { get; set; }
    public int? ProgressEpisodes { get; set; }
    public int? ProgressChapters { get; set; }
    public int? ProgressVolumes { get; set; }
    public int? EpisodeCount { get; set; }
    public int? ChapterCount { get; set; }
    public int? VolumeCount { get; set; }
    public required string PrimaryProgressDimension { get; set; }
    public required string Provider { get; set; }
    public required string ProviderMediaId { get; set; }
    public bool IsConnected { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class MediaLibraryEntryDetailDto
{
    public Guid Id { get; set; }
    public required MediaLibraryTitleDto Title { get; set; }
    public required string Provider { get; set; }
    public required string ProviderMediaId { get; set; }
    public string? ProviderLibraryEntryId { get; set; }
    public required string NormalizedStatus { get; set; }
    public string? RawStatus { get; set; }
    public string? RawListName { get; set; }
    public int? ProgressEpisodes { get; set; }
    public int? ProgressChapters { get; set; }
    public int? ProgressVolumes { get; set; }
    public bool IsConnected { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
    public DateTimeOffset? LastRemoteUpdateAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public bool AutoProgressFromObservations { get; set; }
    public required IReadOnlyList<MediaProviderLinkSummaryDto> ProviderLinks { get; set; }
}

public class MediaLibraryTitleDto
{
    public Guid Id { get; set; }
    public required string CanonicalTitle { get; set; }
    public string? OriginalTitle { get; set; }
    public required string MediaKind { get; set; }
    public string? Synopsis { get; set; }
    public int? StartYear { get; set; }
    public int? EpisodeCount { get; set; }
    public int? ChapterCount { get; set; }
    public int? VolumeCount { get; set; }
    public required string PrimaryProgressDimension { get; set; }
    public required string ReleaseStatusDimension { get; set; }
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
    /// When true, allows stealing a provider link that is already associated with a
    /// different canonical title. Without this flag a 409 Conflict is returned.
    /// </summary>
    public bool ForceRelink { get; set; }
}

public class MediaLinkConflictDto
{
    public required string Error { get; set; }
    public Guid ConflictingMediaTitleId { get; set; }
    public required string ConflictingCanonicalTitle { get; set; }
}

public class MediaAutoProgressRequest
{
    /// <summary>
    /// When true, the backend will automatically advance progress counters
    /// for this entry from matched observations (subject to monotonic and
    /// sync-metadata guards).
    /// </summary>
    public bool Enabled { get; set; }
}
