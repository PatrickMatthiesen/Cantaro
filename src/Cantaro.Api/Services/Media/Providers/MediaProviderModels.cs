namespace Cantaro.Api.Services;

public class MediaCatalogSearchRequest
{
    public required string Query { get; set; }

    public IReadOnlyCollection<string> MediaKinds { get; set; } = [];

    public int Limit { get; set; } = 25;
}

public class MediaProviderLibraryImportResult
{
    public required string ProviderId { get; set; }

    public DateTimeOffset ImportedAt { get; set; }

    public IReadOnlyList<MediaProviderLibraryItem> Items { get; set; } = [];
}

public class MediaProviderLibraryItem
{
    public required string ProviderMediaId { get; set; }

    public string? ProviderLibraryEntryId { get; set; }

    public required string Title { get; set; }

    public string? NativeTitle { get; set; }

    public string? OriginalTitle { get; set; }

    public required string MediaKind { get; set; }

    public string? Synopsis { get; set; }

    public string? ExternalUrl { get; set; }

    public int? StartYear { get; set; }

    public int? EpisodeCount { get; set; }

    public int? ChapterCount { get; set; }

    public int? VolumeCount { get; set; }

    public required string Status { get; set; }

    public IReadOnlyList<string> ProviderListNames { get; set; } = [];

    public int? ProgressEpisodes { get; set; }

    public int? ProgressChapters { get; set; }

    public int? ProgressVolumes { get; set; }

    public required string PrimaryProgressDimension { get; set; }

    public required string ReleaseStatusDimension { get; set; }

    public DateTimeOffset? LastRemoteUpdateAt { get; set; }

    public string? RawMetadata { get; set; }
}

public class MediaProviderSearchResult
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

    public string? RawMetadata { get; set; }
}

public class MediaProviderTitleDetails
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

    public IReadOnlyList<MediaProviderAvailabilityLink> AvailabilityLinks { get; set; } = [];

    public IReadOnlyList<MediaProviderCharacterCredit> Characters { get; set; } = [];

    public string? RawMetadata { get; set; }
}

public class MediaProviderCharacterCredit
{
    public required string CharacterId { get; set; }
    public required string Name { get; set; }
    public string? ImageUrl { get; set; }
    public required string Role { get; set; }
    public string? ProviderUrl { get; set; }
    public int Order { get; set; }
}

public class MediaProviderAvailabilityLink
{
    public required string ServiceId { get; set; }

    public required string DisplayName { get; set; }

    public string? Url { get; set; }

    public required string AvailabilityKind { get; set; }

    public string? Notes { get; set; }

    public string? IconUrl { get; set; }
}

public class MediaProgressUpdateRequest
{
    public required string ProviderMediaId { get; set; }

    public int? ProgressEpisodes { get; set; }

    public int? ProgressChapters { get; set; }

    public int? ProgressVolumes { get; set; }

    public DateTimeOffset? LastKnownRemoteUpdateAt { get; set; }
}

public class MediaStatusUpdateRequest
{
    public required string ProviderMediaId { get; set; }

    public required string Status { get; set; }

    public DateTimeOffset? LastKnownRemoteUpdateAt { get; set; }
}

public class MediaProviderMutationResult
{
    public required string ProviderId { get; set; }

    public required string ProviderMediaId { get; set; }

    public DateTimeOffset AppliedAt { get; set; }

    public DateTimeOffset? LastRemoteUpdateAt { get; set; }

    public string? RawMetadata { get; set; }
}

public class MediaReleaseMetadata
{
    public required string ProviderId { get; set; }

    public required string ProviderMediaId { get; set; }

    public required string ReleaseStatusDimension { get; set; }

    public int? ReleasedCount { get; set; }

    public int? TotalKnownCount { get; set; }

    public DateTimeOffset? NextReleaseAt { get; set; }

    public string? NextReleaseLabel { get; set; }

    public string? RawMetadata { get; set; }
}

public class MediaLibraryImportPersistenceResult
{
    public required string ProviderId { get; set; }

    public int ImportedCount { get; set; }

    public int CreatedTitles { get; set; }

    public int CreatedEntries { get; set; }

    public int UpdatedEntries { get; set; }

    public DateTimeOffset ImportedAt { get; set; }
}
