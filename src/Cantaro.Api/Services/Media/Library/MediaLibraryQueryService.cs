using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.Json;

namespace Cantaro.Api.Services;

public class MediaLibraryQueryOptions
{
    public string? Query { get; set; }
    public string? Status { get; set; }
    public string? MediaKind { get; set; }
    public string? Provider { get; set; }
    public string? ProviderListName { get; set; }

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
            .Include(e => e.ProviderListMemberships)
            .Where(e => e.UserId == userId);

        if (!string.IsNullOrWhiteSpace(options.Query))
        {
            var searchTerm = options.Query.Trim().ToLowerInvariant();
            query = query.Where(e =>
                (e.MediaTitle != null
                    && ((e.MediaTitle.CanonicalTitle != null && e.MediaTitle.CanonicalTitle.ToLower().Contains(searchTerm))
                        || (e.MediaTitle.OriginalTitle != null && e.MediaTitle.OriginalTitle.ToLower().Contains(searchTerm))
                        || (e.MediaTitle.SortTitle != null && e.MediaTitle.SortTitle.ToLower().Contains(searchTerm))))
                || e.ProviderMediaId.ToLower().Contains(searchTerm));
        }

        if (!string.IsNullOrWhiteSpace(options.Status))
        {
            query = query.Where(e => e.Status == options.Status);
        }

        if (!string.IsNullOrWhiteSpace(options.MediaKind))
        {
            query = query.Where(e => e.MediaTitle != null && e.MediaTitle.MediaKind == options.MediaKind);
        }

        if (!string.IsNullOrWhiteSpace(options.Provider))
        {
            query = query.Where(e => e.Provider == options.Provider);
        }

        var availableProviderListNames = await query
            .SelectMany(e => e.ProviderListMemberships)
            .Select(membership => membership.Name)
            .Distinct()
            .OrderBy(name => name)
            .ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(options.ProviderListName))
        {
            query = query.Where(e => e.ProviderListMemberships.Any(membership => membership.Name == options.ProviderListName));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var page = Math.Max(1, options.Page);
        var pageSize = Math.Clamp(options.PageSize, 1, 100);
        var skip = (page - 1) * pageSize;

        List<MediaLibraryEntry> entries;

        if (string.Equals(_dbContext.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal))
        {
            // SQLite still struggles with some of this query's DateTimeOffset ordering, so keep the
            // fallback local to test/dev storage while PostgreSQL handles paging server-side.
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
        var items = entries
            .Select(entry => MapListItem(
                entry,
                availableCounts.GetValueOrDefault(entry.MediaTitleId)))
            .ToList();

        return new MediaLibraryPageDto
        {
            Items = items,
            AvailableProviderListNames = availableProviderListNames,
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
            .Include(e => e.ProviderListMemberships)
            .FirstOrDefaultAsync(e => e.Id == libraryEntryId && e.UserId == userId, cancellationToken);

        return entry is null ? null : MapDetail(entry);
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
                ? entries.OrderByDescending(e => e.MediaTitle?.CanonicalTitle ?? string.Empty)
                : entries.OrderBy(e => e.MediaTitle?.CanonicalTitle ?? string.Empty),
            "status" => descending
                ? entries.OrderByDescending(e => e.Status).ThenByDescending(e => e.UpdatedAt)
                : entries.OrderBy(e => e.Status).ThenByDescending(e => e.UpdatedAt),
            "progress" => descending
                ? entries.OrderByDescending(e => e.ProgressEpisodes ?? e.ProgressChapters ?? e.ProgressVolumes)
                : entries.OrderBy(e => e.ProgressEpisodes ?? e.ProgressChapters ?? e.ProgressVolumes),
            _ => descending
                ? entries.OrderByDescending(e => e.UpdatedAt)
                : entries.OrderBy(e => e.UpdatedAt)
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
                ? entries.OrderByDescending(e => e.MediaTitle!.CanonicalTitle).ThenByDescending(e => e.Id)
                : entries.OrderBy(e => e.MediaTitle!.CanonicalTitle).ThenBy(e => e.Id),
            "status" => descending
                ? entries.OrderByDescending(e => e.Status).ThenByDescending(e => e.UpdatedAt)
                : entries.OrderBy(e => e.Status).ThenByDescending(e => e.UpdatedAt),
            "progress" => descending
                ? entries.OrderByDescending(e => e.ProgressEpisodes ?? e.ProgressChapters ?? e.ProgressVolumes ?? -1)
                    .ThenByDescending(e => e.UpdatedAt)
                : entries.OrderBy(e => e.ProgressEpisodes ?? e.ProgressChapters ?? e.ProgressVolumes ?? -1)
                    .ThenByDescending(e => e.UpdatedAt),
            _ => descending
                ? entries.OrderByDescending(e => e.UpdatedAt).ThenByDescending(e => e.Id)
                : entries.OrderBy(e => e.UpdatedAt).ThenBy(e => e.Id)
        };
    }

    private static MediaLibraryListItemDto MapListItem(MediaLibraryEntry entry, int? availableReleasedCount)
    {
        var artwork = MediaArtworkMetadata.FromCanonicalMetadata(entry.MediaTitle?.CanonicalMetadata);
        var releaseMetadata = ReadNextReleaseMetadata(entry.RawMetadata);

        return new MediaLibraryListItemDto
        {
            Id = entry.Id,
            MediaTitleId = entry.MediaTitleId,
            CanonicalTitle = entry.MediaTitle?.CanonicalTitle ?? string.Empty,
            OriginalTitle = entry.MediaTitle?.OriginalTitle,
            PosterUrl = artwork.LargePosterUrl,
            MediaKind = entry.MediaTitle?.MediaKind ?? string.Empty,
            Status = entry.Status,
            ProgressEpisodes = entry.ProgressEpisodes,
            ProgressChapters = entry.ProgressChapters,
            ProgressVolumes = entry.ProgressVolumes,
            EpisodeCount = entry.MediaTitle?.EpisodeCount,
            ChapterCount = entry.MediaTitle?.ChapterCount,
            VolumeCount = entry.MediaTitle?.VolumeCount,
            ReleasedCount = releaseMetadata.ReleasedCount,
            AvailableReleasedCount = availableReleasedCount,
            PrimaryProgressDimension = entry.MediaTitle?.PrimaryProgressDimension ?? string.Empty,
            Provider = entry.Provider,
            ProviderMediaId = entry.ProviderMediaId,
            ProviderListNames = entry.ProviderListMemberships
                .Select(membership => membership.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList(),
            IsConnected = entry.ConnectedServiceAccountId is not null,
            NextReleaseAt = releaseMetadata.NextReleaseAt,
            NextReleaseLabel = releaseMetadata.NextReleaseLabel,
            LastSyncedAt = entry.LastSyncedAt,
            UpdatedAt = entry.UpdatedAt
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

    private static MediaLibraryEntryDetailDto MapDetail(MediaLibraryEntry entry)
    {
        var title = entry.MediaTitle!;
        var artwork = MediaArtworkMetadata.FromCanonicalMetadata(title.CanonicalMetadata);
        var releaseMetadata = ReadNextReleaseMetadata(entry.RawMetadata);

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
                PosterUrl = artwork.LargePosterUrl,
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
            Status = entry.Status,
            ProviderListNames = entry.ProviderListMemberships
                .Select(membership => membership.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList(),
            ProgressEpisodes = entry.ProgressEpisodes,
            ProgressChapters = entry.ProgressChapters,
            ProgressVolumes = entry.ProgressVolumes,
            IsConnected = entry.ConnectedServiceAccountId is not null,
            NextReleaseAt = releaseMetadata.NextReleaseAt,
            NextReleaseLabel = releaseMetadata.NextReleaseLabel,
            LastSyncedAt = entry.LastSyncedAt,
            LastRemoteUpdateAt = entry.LastRemoteUpdateAt,
            UpdatedAt = entry.UpdatedAt,
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

    private static (int? ReleasedCount, DateTimeOffset? NextReleaseAt, string? NextReleaseLabel) ReadNextReleaseMetadata(string? rawMetadata)
    {
        if (string.IsNullOrWhiteSpace(rawMetadata))
        {
            return default;
        }

        try
        {
            using var document = JsonDocument.Parse(rawMetadata);
            var root = document.RootElement;
            if (root.TryGetProperty("media", out var media)
                && media.ValueKind == JsonValueKind.Object)
            {
                root = media;
            }
            var releasedCount = TryReadInt32(root, "releasedCount");

            if (root.TryGetProperty("nextAiringEpisode", out var nextAiringEpisode)
                && nextAiringEpisode.ValueKind == JsonValueKind.Object)
            {
                DateTimeOffset? airingAt = null;
                if (nextAiringEpisode.TryGetProperty("airingAt", out var airingAtElement)
                    && TryReadUnixTimeSeconds(airingAtElement, out var parsedAiringAt))
                {
                    airingAt = parsedAiringAt;
                }

                string? label = null;
                if (nextAiringEpisode.TryGetProperty("episode", out var episodeElement)
                    && episodeElement.TryGetInt32(out var episode))
                {
                    label = $"Ep {episode}";
                    releasedCount = Math.Max(0, episode - 1);
                }

                return (releasedCount, airingAt, label);
            }

            if (root.TryGetProperty("releasedCount", out _)
                || root.TryGetProperty("nextReleaseAt", out _)
                || root.TryGetProperty("nextReleaseLabel", out _))
            {
                _ = TryReadDateTimeOffset(root, "nextReleaseAt", out var nextReleaseAt);
                return (releasedCount, nextReleaseAt, TryReadString(root, "nextReleaseLabel"));
            }

            if (string.Equals(TryReadString(root, "status"), "FINISHED", StringComparison.OrdinalIgnoreCase))
            {
                releasedCount = TryReadInt32(root, "episodes") ?? TryReadInt32(root, "chapters");
            }

            return (releasedCount, null, null);
        }
        catch (JsonException)
        {
        }

        return default;
    }

    private static bool TryReadDateTimeOffset(JsonElement root, string propertyName, out DateTimeOffset? value)
    {
        value = null;

        if (!root.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(property.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            value = parsed;
            return true;
        }

        if (TryReadUnixTimeSeconds(property, out var unixParsed))
        {
            value = unixParsed;
            return true;
        }

        return false;
    }

    private static bool TryReadUnixTimeSeconds(JsonElement element, out DateTimeOffset value)
    {
        value = default;

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var unixSeconds))
        {
            value = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
            return true;
        }

        return false;
    }

    private static string? TryReadString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static int? TryReadInt32(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out var value)
            ? value
            : null;
    }
}

file sealed class MediaArtworkMetadata
{
    public string? MediumPosterUrl { get; private init; }

    public string? LargePosterUrl { get; private init; }

    public static MediaArtworkMetadata FromCanonicalMetadata(string? canonicalMetadata)
    {
        if (string.IsNullOrWhiteSpace(canonicalMetadata))
        {
            return new MediaArtworkMetadata();
        }

        try
        {
            using var document = JsonDocument.Parse(canonicalMetadata);
            var root = document.RootElement;
            if (root.TryGetProperty("media", out var media)
                && media.ValueKind == JsonValueKind.Object)
            {
                root = media;
            }

            if (!root.TryGetProperty("coverImage", out var coverImage)
                || coverImage.ValueKind != JsonValueKind.Object)
            {
                return new MediaArtworkMetadata();
            }

            var mediumPosterUrl = TryGetString(coverImage, "medium");
            var largePosterUrl = TryGetString(coverImage, "large");
            var extraLargePosterUrl = TryGetString(coverImage, "extraLarge");

            return new MediaArtworkMetadata
            {
                MediumPosterUrl = largePosterUrl ?? mediumPosterUrl,
                LargePosterUrl = extraLargePosterUrl
                    ?? largePosterUrl
                    ?? mediumPosterUrl
            };
        }
        catch (JsonException)
        {
            return new MediaArtworkMetadata();
        }
    }

    private static string? TryGetString(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }
}
