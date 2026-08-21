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

        var existingBindings = await _dbContext.MediaLibraryProviderBindings
            .Include(binding => binding.MediaLibraryEntry)
            .Include(binding => binding.MediaProviderLink)
            .Include(binding => binding.ProviderListMemberships)
            .Where(binding => binding.MediaLibraryEntry!.UserId == userId
                && binding.MediaProviderLink!.Provider == importResult.ProviderId
                && binding.ProviderAccountId == account.ExternalAccountId
                && mediaIds.Contains(binding.MediaProviderLink.ExternalId))
            .ToDictionaryAsync(
                binding => binding.MediaProviderLink!.ExternalId,
                StringComparer.Ordinal,
                cancellationToken);

        var knownTitleIds = existingLinks.Values
            .Select(link => link.MediaTitleId)
            .Distinct()
            .ToList();
        var entriesByTitleId = await _dbContext.MediaLibraryEntries
            .Where(entry => entry.UserId == userId && knownTitleIds.Contains(entry.MediaTitleId))
            .ToDictionaryAsync(entry => entry.MediaTitleId, cancellationToken);

        var createdTitles = 0;
        var createdEntries = 0;
        var updatedEntries = 0;
        var createdBindings = 0;

        foreach (var item in importResult.Items)
        {
            var link = GetOrCreateProviderLink(item, importResult, existingLinks, ref createdTitles);

            if (!entriesByTitleId.TryGetValue(link.MediaTitleId, out var entry))
            {
                entry = CreateLibraryEntry(userId, link.MediaTitleId, item, importResult.ImportedAt);
                _dbContext.MediaLibraryEntries.Add(entry);
                entriesByTitleId[link.MediaTitleId] = entry;
                createdEntries++;
            }

            if (!existingBindings.TryGetValue(item.ProviderMediaId, out var binding))
            {
                binding = CreateProviderBinding(entry, link, account, item, importResult.ImportedAt);
                _dbContext.MediaLibraryProviderBindings.Add(binding);
                existingBindings[item.ProviderMediaId] = binding;
                ApplyRemoteLibraryState(entry, item, importResult.ImportedAt);
                createdBindings++;
            }
            else
            {
                binding.ConnectedServiceAccountId = account.Id;
                binding.ProviderLibraryEntryId = item.ProviderLibraryEntryId;
                if (ShouldApplyRemoteLibraryState(entry, binding, item))
                {
                    ApplyRemoteLibraryState(entry, item, importResult.ImportedAt);
                }

                binding.LastSyncedAt = importResult.ImportedAt;
                binding.LastRemoteUpdateAt = item.LastRemoteUpdateAt;
                binding.RawMetadata = item.RawMetadata;
                binding.UpdatedAt = importResult.ImportedAt;
                updatedEntries++;
            }

            ReplaceProviderListMemberships(binding, item.ProviderListNames);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Imported {Count} {Provider} media items for user {UserId}. Created titles: {CreatedTitles}, created entries: {CreatedEntries}, created bindings: {CreatedBindings}, updated entries: {UpdatedEntries}",
            importResult.Items.Count,
            importResult.ProviderId,
            userId,
            createdTitles,
            createdEntries,
            createdBindings,
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

    private MediaProviderLink GetOrCreateProviderLink(
        MediaProviderLibraryItem item,
        MediaProviderLibraryImportResult importResult,
        IDictionary<string, MediaProviderLink> existingLinks,
        ref int createdTitles)
    {
        if (existingLinks.TryGetValue(item.ProviderMediaId, out var existingLink))
        {
            if (existingLink.MediaTitle is not null)
            {
                ApplyToMediaTitle(existingLink.MediaTitle, item, importResult.ImportedAt);
            }

            existingLink.ExternalUrl = item.ExternalUrl ?? existingLink.ExternalUrl;
            existingLink.RawMetadata = item.RawMetadata ?? existingLink.RawMetadata;
            existingLink.LastVerifiedAt = importResult.ImportedAt;
            existingLink.UpdatedAt = importResult.ImportedAt;
            return existingLink;
        }

        var title = CreateMediaTitle(item, importResult.ImportedAt);
        var link = new MediaProviderLink
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
        return link;
    }

    private static MediaLibraryEntry CreateLibraryEntry(
        int userId,
        Guid mediaTitleId,
        MediaProviderLibraryItem item,
        DateTimeOffset timestamp)
    {
        return new MediaLibraryEntry
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            MediaTitleId = mediaTitleId,
            Status = item.Status,
            ProgressEpisodes = ClampEpisodeProgress(item.ProgressEpisodes, item.EpisodeCount),
            ProgressChapters = item.ProgressChapters,
            ProgressVolumes = item.ProgressVolumes,
            LastMutationSource = MediaMutationSources.ProviderImport,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
    }

    private static MediaLibraryProviderBinding CreateProviderBinding(
        MediaLibraryEntry entry,
        MediaProviderLink link,
        ConnectedServiceAccount account,
        MediaProviderLibraryItem item,
        DateTimeOffset timestamp)
    {
        return new MediaLibraryProviderBinding
        {
            Id = Guid.NewGuid(),
            MediaLibraryEntryId = entry.Id,
            MediaProviderLinkId = link.Id,
            ConnectedServiceAccountId = account.Id,
            ProviderAccountId = account.ExternalAccountId,
            ProviderLibraryEntryId = item.ProviderLibraryEntryId,
            LastSyncedAt = timestamp,
            LastRemoteUpdateAt = item.LastRemoteUpdateAt,
            RawMetadata = item.RawMetadata,
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
            MediaLibraryEntry = entry,
            MediaProviderLink = link
        };
    }

    private void ReplaceProviderListMemberships(
        MediaLibraryProviderBinding binding,
        IReadOnlyList<string> providerListNames)
    {
        var desiredNames = providerListNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        var removedMemberships = binding.ProviderListMemberships
            .Where(membership => !desiredNames.Contains(membership.Name))
            .ToList();
        _dbContext.MediaProviderListMemberships.RemoveRange(removedMemberships);

        var existingNames = binding.ProviderListMemberships
            .Except(removedMemberships)
            .Select(membership => membership.Name)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var name in desiredNames.OrderBy(name => name, StringComparer.Ordinal))
        {
            if (!existingNames.Contains(name))
            {
                binding.ProviderListMemberships.Add(new MediaProviderListMembership
                {
                    MediaLibraryProviderBindingId = binding.Id,
                    Name = name
                });
            }
        }
    }

    private static MediaTitle CreateMediaTitle(MediaProviderLibraryItem item, DateTimeOffset timestamp)
    {
        var title = new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = item.Title,
            SortTitle = item.Title,
            OriginalTitle = item.OriginalTitle ?? item.NativeTitle,
            Synonyms = [.. item.Synonyms],
            MediaKind = item.MediaKind,
            PrimaryProgressDimension = item.PrimaryProgressDimension,
            ReleaseStatusDimension = item.ReleaseStatusDimension,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
        ApplyToMediaTitle(title, item, timestamp);
        return title;
    }

    private static void ApplyToMediaTitle(MediaTitle title, MediaProviderLibraryItem item, DateTimeOffset timestamp)
    {
        title.CanonicalTitle = item.Title;
        title.SortTitle = item.Title;
        title.OriginalTitle = item.OriginalTitle ?? item.NativeTitle ?? title.OriginalTitle;
        title.Synonyms = [.. item.Synonyms];
        title.MediaKind = item.MediaKind;
        title.Synopsis = item.Synopsis ?? title.Synopsis;
        title.Format = item.Format ?? title.Format;
        title.PosterUrl = item.PosterUrl ?? title.PosterUrl;
        title.BackgroundUrl = item.BackgroundUrl ?? title.BackgroundUrl;
        title.StartYear = item.StartYear ?? title.StartYear;
        title.EpisodeCount = item.EpisodeCount ?? title.EpisodeCount;
        title.ChapterCount = item.ChapterCount ?? title.ChapterCount;
        title.VolumeCount = item.VolumeCount ?? title.VolumeCount;
        title.ReleasedCount = item.ReleasedCount ?? title.ReleasedCount;
        title.NextReleaseAt = item.NextReleaseAt;
        title.NextReleaseLabel = item.NextReleaseLabel;
        title.SupportsEpisodeProgress = item.PrimaryProgressDimension == MediaProgressDimensions.Episode;
        title.SupportsChapterProgress = item.PrimaryProgressDimension == MediaProgressDimensions.Chapter;
        title.SupportsVolumeProgress = item.PrimaryProgressDimension == MediaProgressDimensions.Volume;
        title.IsCompletionOnly = item.PrimaryProgressDimension == MediaProgressDimensions.CompletionOnly;
        title.PrimaryProgressDimension = item.PrimaryProgressDimension;
        title.ReleaseStatusDimension = item.ReleaseStatusDimension;
        title.UpdatedAt = timestamp;
    }

    private static bool ShouldApplyRemoteLibraryState(
        MediaLibraryEntry entry,
        MediaLibraryProviderBinding binding,
        MediaProviderLibraryItem item)
    {
        if (binding.LastRemoteUpdateAt is null || item.LastRemoteUpdateAt is null)
        {
            return true;
        }

        if (item.LastRemoteUpdateAt > binding.LastRemoteUpdateAt)
        {
            return true;
        }

        if (item.LastRemoteUpdateAt < binding.LastRemoteUpdateAt)
        {
            return false;
        }

        return entry.LastLocalEditAt is null || entry.LastLocalEditAt < binding.LastRemoteUpdateAt;
    }

    private static void ApplyRemoteLibraryState(
        MediaLibraryEntry entry,
        MediaProviderLibraryItem item,
        DateTimeOffset timestamp)
    {
        entry.Status = item.Status;
        entry.ProgressEpisodes = ClampEpisodeProgress(item.ProgressEpisodes, item.EpisodeCount);
        entry.ProgressChapters = item.ProgressChapters;
        entry.ProgressVolumes = item.ProgressVolumes;
        entry.LastMutationSource = MediaMutationSources.ProviderImport;
        entry.UpdatedAt = timestamp;
    }

    private static int? ClampEpisodeProgress(int? progress, int? episodeCount)
    {
        if (progress is null || episodeCount is null or <= 0)
        {
            return progress;
        }

        return Math.Clamp(progress.Value, 0, episodeCount.Value);
    }
}
