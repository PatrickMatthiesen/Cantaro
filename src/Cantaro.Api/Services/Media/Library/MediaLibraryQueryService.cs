using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

public class MediaLibraryQueryOptions
{
    public string? Status { get; set; }
    public string? MediaKind { get; set; }
    public string? Provider { get; set; }

    /// <summary>
    /// Valid values: title, updatedAt, status, progress. Defaults to updatedAt.
    /// </summary>
    public string SortBy { get; set; } = "updatedAt";

    /// <summary>
    /// Valid values: asc, desc. Defaults to desc.
    /// </summary>
    public string SortDir { get; set; } = "desc";

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public class MediaLibraryQueryService(ApplicationDbContext dbContext)
{
    private readonly ApplicationDbContext _dbContext = dbContext;

    public async Task<MediaLibraryPageDto> GetLibraryAsync(
        int userId,
        MediaLibraryQueryOptions options,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.MediaLibraryEntries
            .Include(e => e.MediaTitle)
            .Where(e => e.UserId == userId);

        if (!string.IsNullOrWhiteSpace(options.Status))
        {
            query = query.Where(e => e.NormalizedStatus == options.Status);
        }

        if (!string.IsNullOrWhiteSpace(options.MediaKind))
        {
            query = query.Where(e => e.MediaTitle != null && e.MediaTitle.MediaKind == options.MediaKind);
        }

        if (!string.IsNullOrWhiteSpace(options.Provider))
        {
            query = query.Where(e => e.Provider == options.Provider);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var page = Math.Max(1, options.Page);
        var pageSize = Math.Clamp(options.PageSize, 1, 100);
        var skip = (page - 1) * pageSize;

        // EF Core's SQLite provider cannot translate DateTimeOffset expressions in ORDER BY.
        // Sorting is applied in memory after the filtered set is fetched from the DB.
        // This is acceptable because the query is already scoped to a single user's library.
        var allFiltered = await query.ToListAsync(cancellationToken);
        var entries = ApplySort(allFiltered, options.SortBy, options.SortDir)
            .Skip(skip)
            .Take(pageSize)
            .ToList();

        var items = entries.Select(MapListItem).ToList();

        return new MediaLibraryPageDto
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling((double)totalCount / pageSize)
        };
    }

    public async Task<MediaLibraryEntryDetailDto?> GetLibraryEntryDetailAsync(
        int userId,
        Guid libraryEntryId,
        CancellationToken cancellationToken)
    {
        var entry = await _dbContext.MediaLibraryEntries
            .Include(e => e.MediaTitle)
                .ThenInclude(t => t!.ProviderLinks)
            .FirstOrDefaultAsync(e => e.Id == libraryEntryId && e.UserId == userId, cancellationToken);

        return entry is null ? null : MapDetail(entry);
    }

    private static IEnumerable<MediaLibraryEntry> ApplySort(
        IEnumerable<MediaLibraryEntry> entries,
        string sortBy,
        string sortDir)
    {
        var descending = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);

        return sortBy.ToLowerInvariant() switch
        {
            "title" => descending
                ? entries.OrderByDescending(e => e.MediaTitle?.CanonicalTitle ?? string.Empty)
                : entries.OrderBy(e => e.MediaTitle?.CanonicalTitle ?? string.Empty),
            "status" => descending
                ? entries.OrderByDescending(e => e.NormalizedStatus).ThenByDescending(e => e.UpdatedAt)
                : entries.OrderBy(e => e.NormalizedStatus).ThenByDescending(e => e.UpdatedAt),
            "progress" => descending
                ? entries.OrderByDescending(e => e.ProgressEpisodes ?? e.ProgressChapters ?? e.ProgressVolumes)
                : entries.OrderBy(e => e.ProgressEpisodes ?? e.ProgressChapters ?? e.ProgressVolumes),
            _ => descending
                ? entries.OrderByDescending(e => e.UpdatedAt)
                : entries.OrderBy(e => e.UpdatedAt)
        };
    }

    private static MediaLibraryListItemDto MapListItem(MediaLibraryEntry entry)
    {
        return new MediaLibraryListItemDto
        {
            Id = entry.Id,
            MediaTitleId = entry.MediaTitleId,
            CanonicalTitle = entry.MediaTitle?.CanonicalTitle ?? string.Empty,
            OriginalTitle = entry.MediaTitle?.OriginalTitle,
            MediaKind = entry.MediaTitle?.MediaKind ?? string.Empty,
            NormalizedStatus = entry.NormalizedStatus,
            ProgressEpisodes = entry.ProgressEpisodes,
            ProgressChapters = entry.ProgressChapters,
            ProgressVolumes = entry.ProgressVolumes,
            EpisodeCount = entry.MediaTitle?.EpisodeCount,
            ChapterCount = entry.MediaTitle?.ChapterCount,
            VolumeCount = entry.MediaTitle?.VolumeCount,
            PrimaryProgressDimension = entry.MediaTitle?.PrimaryProgressDimension ?? string.Empty,
            Provider = entry.Provider,
            ProviderMediaId = entry.ProviderMediaId,
            IsConnected = entry.ConnectedServiceAccountId is not null,
            LastSyncedAt = entry.LastSyncedAt,
            UpdatedAt = entry.UpdatedAt
        };
    }

    private static MediaLibraryEntryDetailDto MapDetail(MediaLibraryEntry entry)
    {
        var title = entry.MediaTitle!;
        return new MediaLibraryEntryDetailDto
        {
            Id = entry.Id,
            Title = new MediaLibraryTitleDto
            {
                Id = title.Id,
                CanonicalTitle = title.CanonicalTitle,
                OriginalTitle = title.OriginalTitle,
                MediaKind = title.MediaKind,
                Synopsis = title.Synopsis,
                StartYear = title.StartYear,
                EpisodeCount = title.EpisodeCount,
                ChapterCount = title.ChapterCount,
                VolumeCount = title.VolumeCount,
                PrimaryProgressDimension = title.PrimaryProgressDimension,
                ReleaseStatusDimension = title.ReleaseStatusDimension
            },
            Provider = entry.Provider,
            ProviderMediaId = entry.ProviderMediaId,
            ProviderLibraryEntryId = entry.ProviderLibraryEntryId,
            NormalizedStatus = entry.NormalizedStatus,
            RawStatus = entry.RawStatus,
            RawListName = entry.RawListName,
            ProgressEpisodes = entry.ProgressEpisodes,
            ProgressChapters = entry.ProgressChapters,
            ProgressVolumes = entry.ProgressVolumes,
            IsConnected = entry.ConnectedServiceAccountId is not null,
            LastSyncedAt = entry.LastSyncedAt,
            LastRemoteUpdateAt = entry.LastRemoteUpdateAt,
            UpdatedAt = entry.UpdatedAt,
            AutoProgressFromObservations = entry.AutoProgressFromObservations,
            ProviderLinks = title.ProviderLinks
                .Select(l => new MediaProviderLinkSummaryDto
                {
                    Id = l.Id,
                    Provider = l.Provider,
                    ExternalId = l.ExternalId,
                    ExternalUrl = l.ExternalUrl,
                    LinkSource = l.LinkSource,
                    LastVerifiedAt = l.LastVerifiedAt
                })
                .ToList()
        };
    }
}
