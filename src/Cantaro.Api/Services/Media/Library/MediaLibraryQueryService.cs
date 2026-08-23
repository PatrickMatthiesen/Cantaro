using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

public class MediaLibraryQueryOptions
{
    public string? Query { get; set; }
    public string? Status { get; set; }
    public string? MediaKind { get; set; }
    public string? Provider { get; set; }
    public string? ProviderListName { get; set; }
    public string SortBy { get; set; } = "updatedAt";
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
            .Include(entry => entry.MediaTitle)
            .Include(entry => entry.ProviderBindings)
                .ThenInclude(binding => binding.MediaProviderLink)
            .Include(entry => entry.ProviderBindings)
                .ThenInclude(binding => binding.ProviderListMemberships)
            .Where(entry => entry.UserId == userId);

        if (!string.IsNullOrWhiteSpace(options.Query))
        {
            var searchTerm = options.Query.Trim().ToLowerInvariant();
            query = query.Where(entry =>
                (entry.MediaTitle != null
                    && (entry.MediaTitle.CanonicalTitle.ToLower().Contains(searchTerm)
                        || (entry.MediaTitle.OriginalTitle != null && entry.MediaTitle.OriginalTitle.ToLower().Contains(searchTerm))
                        || (entry.MediaTitle.SortTitle != null && entry.MediaTitle.SortTitle.ToLower().Contains(searchTerm))))
                || entry.ProviderBindings.Any(binding =>
                    binding.MediaProviderLink != null
                    && binding.MediaProviderLink.ExternalId.ToLower().Contains(searchTerm)));
        }

        if (!string.IsNullOrWhiteSpace(options.Status))
        {
            query = query.Where(entry => entry.Status == options.Status);
        }

        if (!string.IsNullOrWhiteSpace(options.MediaKind))
        {
            query = query.Where(entry => entry.MediaTitle != null && entry.MediaTitle.MediaKind == options.MediaKind);
        }

        if (!string.IsNullOrWhiteSpace(options.Provider))
        {
            query = query.Where(entry => entry.ProviderBindings.Any(binding =>
                binding.MediaProviderLink != null && binding.MediaProviderLink.Provider == options.Provider));
        }

        var availableProviderListNames = await query
            .SelectMany(entry => entry.ProviderBindings)
            .SelectMany(binding => binding.ProviderListMemberships)
            .Select(membership => membership.Name)
            .Distinct()
            .OrderBy(name => name)
            .ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(options.ProviderListName))
        {
            query = query.Where(entry => entry.ProviderBindings.Any(binding =>
                binding.ProviderListMemberships.Any(membership => membership.Name == options.ProviderListName)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var page = Math.Max(1, options.Page);
        var pageSize = Math.Clamp(options.PageSize, 1, 100);
        var skip = (page - 1) * pageSize;

        List<MediaLibraryEntry> entries;
        if (string.Equals(_dbContext.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal))
        {
            var allFiltered = await query.ToListAsync(cancellationToken);
            entries = ApplyEnumerableSort(allFiltered, options.SortBy, options.SortDir)
                .Skip(skip)
                .Take(pageSize)
                .ToList();
        }
        else
        {
            entries = await ApplyQueryableSort(query, options.SortBy, options.SortDir)
                .Skip(skip)
                .Take(pageSize)
                .ToListAsync(cancellationToken);
        }

        var preferredTrack = await _dbContext.UserSettings
            .AsNoTracking()
            .Where(settings => settings.UserId == userId)
            .Select(settings => settings.PreferredMediaReleaseTrack)
            .SingleOrDefaultAsync(cancellationToken) ?? MediaReleaseTrackPreferences.Default;
        if (!MediaReleaseTrackPreferences.TryNormalize(preferredTrack, out preferredTrack))
        {
            preferredTrack = MediaReleaseTrackPreferences.Default;
        }

        var titleIds = entries.Select(entry => entry.MediaTitleId).Distinct().ToArray();
        var episodeAvailability = await _dbContext.MediaEpisodes
            .AsNoTracking()
            .Where(episode => titleIds.Contains(episode.MediaTitleId))
            .Select(episode => new EpisodeAvailabilityProjection(
                episode.MediaTitleId,
                episode.EpisodeNumber,
                episode.AvailableSubtitleLanguageCodes,
                episode.AvailableAudioLanguageCodes))
            .ToListAsync(cancellationToken);
        var availableCounts = ResolveAvailableReleasedCounts(episodeAvailability, preferredTrack);

        return new MediaLibraryPageDto
        {
            Items = entries
                .Select(entry => MapListItem(entry, availableCounts.GetValueOrDefault(entry.MediaTitleId)))
                .ToList(),
            AvailableProviderListNames = availableProviderListNames,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling((double)totalCount / pageSize)
        };
    }

    public async Task<MediaTitleDetailDto?> GetMediaTitleAsync(
        Guid mediaTitleId,
        CancellationToken cancellationToken)
    {
        var title = await _dbContext.MediaTitles
            .AsNoTracking()
            .Include(item => item.ProviderLinks)
            .FirstOrDefaultAsync(item => item.Id == mediaTitleId, cancellationToken);
        return title is null ? null : MapTitle(title);
    }

    public async Task<MediaViewerStateDto?> GetViewerStateAsync(
        int userId,
        Guid mediaTitleId,
        CancellationToken cancellationToken)
    {
        var entry = await _dbContext.MediaLibraryEntries
            .AsNoTracking()
            .Include(item => item.ProviderBindings)
                .ThenInclude(binding => binding.MediaProviderLink)
            .Include(item => item.ProviderBindings)
                .ThenInclude(binding => binding.ProviderListMemberships)
            .FirstOrDefaultAsync(
                item => item.UserId == userId && item.MediaTitleId == mediaTitleId,
                cancellationToken);
        return entry is null ? null : MapViewerState(entry);
    }

    private static IEnumerable<MediaLibraryEntry> ApplyEnumerableSort(
        IEnumerable<MediaLibraryEntry> entries,
        string sortBy,
        string sortDir)
    {
        var descending = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
        return sortBy.ToLowerInvariant() switch
        {
            "title" => descending
                ? entries.OrderByDescending(entry => entry.MediaTitle?.CanonicalTitle ?? string.Empty)
                : entries.OrderBy(entry => entry.MediaTitle?.CanonicalTitle ?? string.Empty),
            "status" => descending
                ? entries.OrderByDescending(entry => entry.Status).ThenByDescending(entry => entry.UpdatedAt)
                : entries.OrderBy(entry => entry.Status).ThenByDescending(entry => entry.UpdatedAt),
            "progress" => descending
                ? entries.OrderByDescending(entry => entry.ProgressEpisodes ?? entry.ProgressChapters ?? entry.ProgressVolumes)
                : entries.OrderBy(entry => entry.ProgressEpisodes ?? entry.ProgressChapters ?? entry.ProgressVolumes),
            _ => descending
                ? entries.OrderByDescending(entry => entry.UpdatedAt)
                : entries.OrderBy(entry => entry.UpdatedAt)
        };
    }

    private static IQueryable<MediaLibraryEntry> ApplyQueryableSort(
        IQueryable<MediaLibraryEntry> entries,
        string sortBy,
        string sortDir)
    {
        var descending = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
        return sortBy.ToLowerInvariant() switch
        {
            "title" => descending
                ? entries.OrderByDescending(entry => entry.MediaTitle!.CanonicalTitle).ThenByDescending(entry => entry.Id)
                : entries.OrderBy(entry => entry.MediaTitle!.CanonicalTitle).ThenBy(entry => entry.Id),
            "status" => descending
                ? entries.OrderByDescending(entry => entry.Status).ThenByDescending(entry => entry.UpdatedAt)
                : entries.OrderBy(entry => entry.Status).ThenByDescending(entry => entry.UpdatedAt),
            "progress" => descending
                ? entries.OrderByDescending(entry => entry.ProgressEpisodes ?? entry.ProgressChapters ?? entry.ProgressVolumes ?? -1)
                    .ThenByDescending(entry => entry.UpdatedAt)
                : entries.OrderBy(entry => entry.ProgressEpisodes ?? entry.ProgressChapters ?? entry.ProgressVolumes ?? -1)
                    .ThenByDescending(entry => entry.UpdatedAt),
            _ => descending
                ? entries.OrderByDescending(entry => entry.UpdatedAt).ThenByDescending(entry => entry.Id)
                : entries.OrderBy(entry => entry.UpdatedAt).ThenBy(entry => entry.Id)
        };
    }

    private static MediaLibraryListItemDto MapListItem(MediaLibraryEntry entry, int? availableReleasedCount)
    {
        var title = entry.MediaTitle!;
        var primaryBinding = entry.ProviderBindings
            .OrderByDescending(binding => binding.ConnectedServiceAccountId is not null)
            .ThenByDescending(binding => binding.UpdatedAt)
            .FirstOrDefault();

        return new MediaLibraryListItemDto
        {
            Id = entry.Id,
            MediaTitleId = entry.MediaTitleId,
            CanonicalTitle = title.CanonicalTitle,
            OriginalTitle = title.OriginalTitle,
            PosterUrl = title.PosterUrl,
            MediaKind = title.MediaKind,
            Status = entry.Status,
            Score = entry.Score,
            ProgressEpisodes = entry.ProgressEpisodes,
            ProgressChapters = entry.ProgressChapters,
            ProgressVolumes = entry.ProgressVolumes,
            EpisodeCount = title.EpisodeCount,
            ChapterCount = title.ChapterCount,
            VolumeCount = title.VolumeCount,
            ReleasedCount = title.ReleasedCount,
            TotalKnownCount = title.TotalKnownCount,
            AvailableReleasedCount = availableReleasedCount,
            PrimaryProgressDimension = title.PrimaryProgressDimension,
            Provider = primaryBinding?.MediaProviderLink?.Provider ?? string.Empty,
            ProviderMediaId = primaryBinding?.MediaProviderLink?.ExternalId ?? string.Empty,
            ProviderListNames = entry.ProviderBindings
                .SelectMany(binding => binding.ProviderListMemberships)
                .Select(membership => membership.Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList(),
            IsConnected = entry.ProviderBindings.Any(binding => binding.ConnectedServiceAccountId is not null),
            NextReleaseAt = title.NextReleaseAt,
            NextReleaseLabel = title.NextReleaseLabel,
            LastSyncedAt = entry.ProviderBindings.Max(binding => binding.LastSyncedAt),
            UpdatedAt = entry.UpdatedAt
        };
    }

    private static MediaTitleDetailDto MapTitle(MediaTitle title)
    {
        return new MediaTitleDetailDto
        {
            Id = title.Id,
            CanonicalTitle = title.CanonicalTitle,
            OriginalTitle = title.OriginalTitle,
            Synonyms = title.Synonyms,
            MediaKind = title.MediaKind,
            Format = title.Format,
            Synopsis = title.Synopsis,
            PosterUrl = title.PosterUrl,
            BackgroundUrl = title.BackgroundUrl,
            StartYear = title.StartYear,
            EpisodeCount = title.EpisodeCount,
            ChapterCount = title.ChapterCount,
            VolumeCount = title.VolumeCount,
            ReleasedCount = title.ReleasedCount,
            TotalKnownCount = title.TotalKnownCount,
            NextReleaseAt = title.NextReleaseAt,
            NextReleaseLabel = title.NextReleaseLabel,
            PrimaryProgressDimension = title.PrimaryProgressDimension,
            ReleaseStatusDimension = title.ReleaseStatusDimension,
            UpdatedAt = title.UpdatedAt,
            ProviderLinks = title.ProviderLinks
                .OrderBy(link => link.Provider, StringComparer.Ordinal)
                .Select(link => new MediaProviderLinkSummaryDto
                {
                    Id = link.Id,
                    Provider = link.Provider,
                    ExternalId = link.ExternalId,
                    ExternalUrl = link.ExternalUrl,
                    LinkSource = link.LinkSource,
                    LastVerifiedAt = link.LastVerifiedAt,
                    AvailabilityLinks = MediaProviderAvailabilitySnapshotCodec.Deserialize(link.AvailabilitySnapshot)
                        .Select(MapAvailabilityLink)
                        .ToList(),
                    AvailabilityLastVerifiedAt = link.AvailabilityLastVerifiedAt
                })
                .ToList()
        };
    }

    private static MediaProviderAvailabilityLinkDto MapAvailabilityLink(MediaProviderAvailabilityLink link)
        => new()
        {
            ServiceId = link.ServiceId,
            DisplayName = link.DisplayName,
            Url = link.Url,
            AvailabilityKind = link.AvailabilityKind,
            Notes = link.Notes,
            IconUrl = link.IconUrl
        };

    private static MediaViewerStateDto MapViewerState(MediaLibraryEntry entry)
    {
        return new MediaViewerStateDto
        {
            Id = entry.Id,
            MediaTitleId = entry.MediaTitleId,
            Status = entry.Status,
            Score = entry.Score,
            ProgressEpisodes = entry.ProgressEpisodes,
            ProgressChapters = entry.ProgressChapters,
            ProgressVolumes = entry.ProgressVolumes,
            LastLocalEditAt = entry.LastLocalEditAt,
            UpdatedAt = entry.UpdatedAt,
            ProviderBindings = entry.ProviderBindings
                .Where(binding => binding.MediaProviderLink is not null)
                .OrderBy(binding => binding.MediaProviderLink!.Provider, StringComparer.Ordinal)
                .Select(binding => new MediaViewerProviderBindingDto
                {
                    Id = binding.Id,
                    Provider = binding.MediaProviderLink!.Provider,
                    ProviderMediaId = binding.MediaProviderLink.ExternalId,
                    ProviderLibraryEntryId = binding.ProviderLibraryEntryId,
                    ProviderListNames = binding.ProviderListMemberships
                        .Select(membership => membership.Name)
                        .OrderBy(name => name, StringComparer.Ordinal)
                        .ToList(),
                    IsConnected = binding.ConnectedServiceAccountId is not null,
                    LastSyncedAt = binding.LastSyncedAt,
                    LastRemoteUpdateAt = binding.LastRemoteUpdateAt
                })
                .ToList()
        };
    }

    private static Dictionary<Guid, int?> ResolveAvailableReleasedCounts(
        IEnumerable<EpisodeAvailabilityProjection> episodes,
        string preferredTrack)
    {
        var presentationSeparator = preferredTrack.IndexOf(':');
        var presentation = preferredTrack[..presentationSeparator];
        var languageCode = preferredTrack[(presentationSeparator + 1)..];
        return episodes
            .GroupBy(episode => episode.MediaTitleId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var preferred = group
                        .Where(episode => (presentation == "dub" ? episode.AudioLanguageCodes : episode.SubtitleLanguageCodes)
                            .Contains(languageCode, StringComparer.OrdinalIgnoreCase))
                        .Select(episode => (int?)episode.EpisodeNumber)
                        .Max();
                    if (preferred is not null || presentation != "dub")
                    {
                        return preferred;
                    }

                    return group
                        .Where(episode => episode.SubtitleLanguageCodes.Contains(languageCode, StringComparer.OrdinalIgnoreCase))
                        .Select(episode => (int?)episode.EpisodeNumber)
                        .Max();
                });
    }

    private sealed record EpisodeAvailabilityProjection(
        Guid MediaTitleId,
        int EpisodeNumber,
        string[] SubtitleLanguageCodes,
        string[] AudioLanguageCodes);
}
