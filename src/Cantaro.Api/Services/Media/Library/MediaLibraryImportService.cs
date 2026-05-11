using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

public class MediaLibraryImportService(
    ApplicationDbContext dbContext,
    ILogger<MediaLibraryImportService> logger)
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly ILogger<MediaLibraryImportService> _logger = logger;

    public async Task<MediaLibraryImportPersistenceResult> ImportAsync(
        int userId,
        ConnectedServiceAccount account,
        MediaProviderLibraryImportResult importResult,
        CancellationToken cancellationToken)
    {
        var mediaIds = importResult.Items
            .Select(item => item.ProviderMediaId)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var existingLinks = await _dbContext.MediaProviderLinks
            .Include(link => link.MediaTitle)
            .Where(link => link.Provider == importResult.ProviderId && mediaIds.Contains(link.ExternalId))
            .ToDictionaryAsync(link => link.ExternalId, StringComparer.Ordinal, cancellationToken);

        var existingEntries = await _dbContext.MediaLibraryEntries
            .Where(entry => entry.UserId == userId
                && entry.Provider == importResult.ProviderId
                && entry.ProviderAccountId == account.ExternalAccountId
                && mediaIds.Contains(entry.ProviderMediaId))
            .ToDictionaryAsync(entry => entry.ProviderMediaId, StringComparer.Ordinal, cancellationToken);

        var createdTitles = 0;
        var createdEntries = 0;
        var updatedEntries = 0;

        foreach (var item in importResult.Items)
        {
            if (!existingLinks.TryGetValue(item.ProviderMediaId, out var link))
            {
                var title = CreateMediaTitle(item, importResult.ImportedAt);
                link = new MediaProviderLink
                {
                    Id = Guid.NewGuid(),
                    MediaTitleId = title.Id,
                    Provider = importResult.ProviderId,
                    ExternalId = item.ProviderMediaId,
                    ExternalUrl = item.ExternalUrl,
                    LinkSource = MediaMappingSources.Imported,
                    RawMetadata = item.RawMetadata,
                    LastVerifiedAt = importResult.ImportedAt,
                    CreatedAt = importResult.ImportedAt,
                    UpdatedAt = importResult.ImportedAt,
                    MediaTitle = title
                };

                _dbContext.MediaTitles.Add(title);
                _dbContext.MediaProviderLinks.Add(link);
                existingLinks[item.ProviderMediaId] = link;
                createdTitles++;
            }
            else if (link.MediaTitle is not null)
            {
                ApplyToMediaTitle(link.MediaTitle, item, importResult.ImportedAt);
                link.ExternalUrl = item.ExternalUrl ?? link.ExternalUrl;
                link.RawMetadata = item.RawMetadata ?? link.RawMetadata;
                link.LastVerifiedAt = importResult.ImportedAt;
                link.UpdatedAt = importResult.ImportedAt;
            }

            if (!existingEntries.TryGetValue(item.ProviderMediaId, out var entry))
            {
                entry = new MediaLibraryEntry
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    MediaTitleId = link.MediaTitleId,
                    ConnectedServiceAccountId = account.Id,
                    Provider = importResult.ProviderId,
                    ProviderAccountId = account.ExternalAccountId,
                    ProviderMediaId = item.ProviderMediaId,
                    ProviderLibraryEntryId = item.ProviderLibraryEntryId,
                    NormalizedStatus = item.NormalizedStatus,
                    RawStatus = item.RawStatus,
                    RawListName = item.RawListName,
                    ProgressEpisodes = item.ProgressEpisodes,
                    ProgressChapters = item.ProgressChapters,
                    ProgressVolumes = item.ProgressVolumes,
                    LastSyncedAt = importResult.ImportedAt,
                    LastRemoteUpdateAt = item.LastRemoteUpdateAt,
                    LastMutationSource = MediaMutationSources.ProviderImport,
                    RawMetadata = item.RawMetadata,
                    CreatedAt = importResult.ImportedAt,
                    UpdatedAt = importResult.ImportedAt
                };

                _dbContext.MediaLibraryEntries.Add(entry);
                existingEntries[item.ProviderMediaId] = entry;
                createdEntries++;
            }
            else
            {
                entry.MediaTitleId = link.MediaTitleId;
                entry.ConnectedServiceAccountId = account.Id;
                entry.ProviderLibraryEntryId = item.ProviderLibraryEntryId;
                entry.NormalizedStatus = item.NormalizedStatus;
                entry.RawStatus = item.RawStatus;
                entry.RawListName = item.RawListName;
                entry.ProgressEpisodes = item.ProgressEpisodes;
                entry.ProgressChapters = item.ProgressChapters;
                entry.ProgressVolumes = item.ProgressVolumes;
                entry.LastSyncedAt = importResult.ImportedAt;
                entry.LastRemoteUpdateAt = item.LastRemoteUpdateAt;
                entry.LastMutationSource = MediaMutationSources.ProviderImport;
                entry.RawMetadata = item.RawMetadata;
                entry.UpdatedAt = importResult.ImportedAt;
                updatedEntries++;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Imported {Count} {Provider} media items for user {UserId}. Created titles: {CreatedTitles}, created entries: {CreatedEntries}, updated entries: {UpdatedEntries}",
            importResult.Items.Count,
            importResult.ProviderId,
            userId,
            createdTitles,
            createdEntries,
            updatedEntries);

        return new MediaLibraryImportPersistenceResult
        {
            ProviderId = importResult.ProviderId,
            ImportedCount = importResult.Items.Count,
            CreatedTitles = createdTitles,
            CreatedEntries = createdEntries,
            UpdatedEntries = updatedEntries,
            ImportedAt = importResult.ImportedAt
        };
    }

    private static MediaTitle CreateMediaTitle(MediaProviderLibraryItem item, DateTimeOffset timestamp)
    {
        return new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = item.Title,
            SortTitle = item.Title,
            OriginalTitle = item.OriginalTitle ?? item.NativeTitle,
            MediaKind = item.MediaKind,
            Synopsis = item.Synopsis,
            StartYear = item.StartYear,
            EpisodeCount = item.EpisodeCount,
            ChapterCount = item.ChapterCount,
            VolumeCount = item.VolumeCount,
            SupportsEpisodeProgress = item.PrimaryProgressDimension == MediaProgressDimensions.Episode,
            SupportsChapterProgress = item.PrimaryProgressDimension == MediaProgressDimensions.Chapter,
            SupportsVolumeProgress = item.PrimaryProgressDimension == MediaProgressDimensions.Volume,
            IsCompletionOnly = item.PrimaryProgressDimension == MediaProgressDimensions.CompletionOnly,
            PrimaryProgressDimension = item.PrimaryProgressDimension,
            ReleaseStatusDimension = item.ReleaseStatusDimension,
            CanonicalMetadata = item.RawMetadata,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
    }

    private static void ApplyToMediaTitle(MediaTitle title, MediaProviderLibraryItem item, DateTimeOffset timestamp)
    {
        title.CanonicalTitle = item.Title;
        title.SortTitle = item.Title;
        title.OriginalTitle = item.OriginalTitle ?? item.NativeTitle ?? title.OriginalTitle;
        title.MediaKind = item.MediaKind;
        title.Synopsis = item.Synopsis ?? title.Synopsis;
        title.StartYear = item.StartYear ?? title.StartYear;
        title.EpisodeCount = item.EpisodeCount ?? title.EpisodeCount;
        title.ChapterCount = item.ChapterCount ?? title.ChapterCount;
        title.VolumeCount = item.VolumeCount ?? title.VolumeCount;
        title.SupportsEpisodeProgress = item.PrimaryProgressDimension == MediaProgressDimensions.Episode;
        title.SupportsChapterProgress = item.PrimaryProgressDimension == MediaProgressDimensions.Chapter;
        title.SupportsVolumeProgress = item.PrimaryProgressDimension == MediaProgressDimensions.Volume;
        title.IsCompletionOnly = item.PrimaryProgressDimension == MediaProgressDimensions.CompletionOnly;
        title.PrimaryProgressDimension = item.PrimaryProgressDimension;
        title.ReleaseStatusDimension = item.ReleaseStatusDimension;
        title.CanonicalMetadata = item.RawMetadata ?? title.CanonicalMetadata;
        title.UpdatedAt = timestamp;
    }
}
