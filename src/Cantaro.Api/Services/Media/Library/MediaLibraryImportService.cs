using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

public class MediaLibraryImportService(
    ApplicationDbContext dbContext,
    ILogger<MediaLibraryImportService> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly ILogger<MediaLibraryImportService> _logger = logger;

    public async Task<MediaLibraryImportPersistenceResult> ImportAsync(
        int userId,
        ConnectedServiceAccount account,
        MediaProviderLibraryImportResult importResult,
        CancellationToken cancellationToken,
        bool fanOutChanges = true)
    {
        var mediaIds = importResult.Items
            .Select(item => item.ProviderMediaId)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var existingLinks = await _dbContext.MediaProviderLinks
            .Include(link => link.MediaTitle)
            .Where(link => link.Provider == importResult.ProviderId && mediaIds.Contains(link.ExternalId))
            .ToDictionaryAsync(link => link.ExternalId, StringComparer.Ordinal, cancellationToken);

        var crossReferenceProviders = importResult.Items
            .SelectMany(item => item.CrossReferences)
            .Select(reference => NormalizeProviderId(reference.ProviderId))
            .Where(provider => provider.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var crossReferenceMediaIds = importResult.Items
            .SelectMany(item => item.CrossReferences)
            .Select(reference => reference.ProviderMediaId.Trim())
            .Where(mediaId => mediaId.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var crossReferenceLinks = crossReferenceProviders.Count == 0 || crossReferenceMediaIds.Count == 0
            ? new Dictionary<string, MediaProviderLink>(StringComparer.Ordinal)
            : (await _dbContext.MediaProviderLinks
                .Include(link => link.MediaTitle)
                .Where(link => crossReferenceProviders.Contains(link.Provider)
                    && crossReferenceMediaIds.Contains(link.ExternalId))
                .ToListAsync(cancellationToken))
                .ToDictionary(
                    link => BuildCrossReferenceKey(link.Provider, link.ExternalId),
                    StringComparer.Ordinal);

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
            .Concat(crossReferenceLinks.Values)
            .Select(link => link.MediaTitleId)
            .Distinct()
            .ToList();
        var titleProviderLinks = knownTitleIds.Count == 0
            ? new Dictionary<string, MediaProviderLink>(StringComparer.Ordinal)
            : (await _dbContext.MediaProviderLinks
                .Include(link => link.MediaTitle)
                .Where(link => knownTitleIds.Contains(link.MediaTitleId))
                .ToListAsync(cancellationToken))
                .ToDictionary(
                    link => BuildTitleProviderKey(link.MediaTitleId, link.Provider),
                    StringComparer.Ordinal);
        var entriesByTitleId = await _dbContext.MediaLibraryEntries
            .Include(entry => entry.ProviderBindings)
                .ThenInclude(binding => binding.MediaProviderLink)
            .Where(entry => entry.UserId == userId && knownTitleIds.Contains(entry.MediaTitleId))
            .ToDictionaryAsync(entry => entry.MediaTitleId, cancellationToken);

        var createdTitles = 0;
        var createdEntries = 0;
        var updatedEntries = 0;
        var createdBindings = 0;
        var fanOutOperations = new List<MediaProviderOperation>();

        foreach (var item in importResult.Items)
        {
            var link = GetOrCreateProviderLink(
                item,
                importResult,
                existingLinks,
                crossReferenceLinks,
                titleProviderLinks,
                ref createdTitles);
            PersistCrossReferences(
                link,
                item.CrossReferences,
                importResult.ImportedAt,
                crossReferenceLinks,
                titleProviderLinks);

            if (!entriesByTitleId.TryGetValue(link.MediaTitleId, out var entry))
            {
                entry = CreateLibraryEntry(userId, link.MediaTitleId, item, importResult.ImportedAt);
                _dbContext.MediaLibraryEntries.Add(entry);
                entriesByTitleId[link.MediaTitleId] = entry;
                createdEntries++;
            }

            var entryWasCreated = entry.ProviderBindings.Count == 0;
            var previousState = CaptureState(entry);
            existingBindings.TryGetValue(item.ProviderMediaId, out var binding);
            var previousSourceRemoteUpdateAt = binding?.LastRemoteUpdateAt;
            if (binding is null)
            {
                binding = CreateProviderBinding(entry, link, account, item, importResult.ImportedAt);
                _dbContext.MediaLibraryProviderBindings.Add(binding);
                existingBindings[item.ProviderMediaId] = binding;
                createdBindings++;
            }
            else
            {
                binding.ConnectedServiceAccountId = account.Id;
                binding.ProviderLibraryEntryId = item.ProviderLibraryEntryId;
                updatedEntries++;
            }

            var shouldApplyRemoteState = entryWasCreated || ShouldApplyRemoteLibraryState(
                entry,
                binding,
                item,
                previousSourceRemoteUpdateAt);
            if (shouldApplyRemoteState)
            {
                ApplyRemoteLibraryState(entry, item, importResult.ImportedAt);
                if (!entryWasCreated && fanOutChanges)
                {
                    fanOutOperations.AddRange(CreateFanOutOperations(
                        entry,
                        binding,
                        previousState,
                        importResult.ImportedAt));
                }
            }

            binding.LastSyncedAt = importResult.ImportedAt;
            binding.LastRemoteUpdateAt = item.LastRemoteUpdateAt;
            binding.UpdatedAt = importResult.ImportedAt;

            ReplaceProviderListMemberships(binding, item.ProviderListNames);
        }

        _dbContext.MediaProviderOperations.AddRange(fanOutOperations);

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

    private void PersistCrossReferences(
        MediaProviderLink primaryLink,
        IReadOnlyList<MediaProviderCrossReference> crossReferences,
        DateTimeOffset timestamp,
        IDictionary<string, MediaProviderLink> existingCrossReferences,
        IDictionary<string, MediaProviderLink> titleProviderLinks)
    {
        foreach (var reference in crossReferences)
        {
            var providerId = NormalizeProviderId(reference.ProviderId);
            var providerMediaId = reference.ProviderMediaId.Trim();
            if (providerId.Length == 0 || providerMediaId.Length == 0
                || providerId == primaryLink.Provider && providerMediaId == primaryLink.ExternalId)
            {
                continue;
            }

            var key = BuildCrossReferenceKey(providerId, providerMediaId);
            if (existingCrossReferences.TryGetValue(key, out var existing))
            {
                if (existing.MediaTitleId != primaryLink.MediaTitleId)
                {
                    _logger.LogWarning(
                        "Provider cross-reference {Provider}/{ProviderMediaId} already belongs to MediaTitle {ExistingTitleId}; refusing to move it to {RequestedTitleId}.",
                        providerId,
                        providerMediaId,
                        existing.MediaTitleId,
                        primaryLink.MediaTitleId);
                    continue;
                }

                existing.ExternalUrl = reference.ExternalUrl ?? existing.ExternalUrl;
                existing.LastVerifiedAt = timestamp;
                existing.UpdatedAt = timestamp;
                continue;
            }

            var titleProviderKey = BuildTitleProviderKey(primaryLink.MediaTitleId, providerId);
            if (titleProviderLinks.TryGetValue(titleProviderKey, out var conflictingProviderLink))
            {
                _logger.LogWarning(
                    "MediaTitle {MediaTitleId} already has provider link {Provider}/{ExistingProviderMediaId}; refusing cross-reference {RequestedProviderMediaId}.",
                    primaryLink.MediaTitleId,
                    providerId,
                    conflictingProviderLink.ExternalId,
                    providerMediaId);
                continue;
            }

            var link = new MediaProviderLink
            {
                Id = Guid.NewGuid(),
                MediaTitleId = primaryLink.MediaTitleId,
                Provider = providerId,
                ExternalId = providerMediaId,
                ExternalUrl = reference.ExternalUrl,
                Confidence = 1m,
                LinkSource = MediaMappingSources.Imported,
                LastVerifiedAt = timestamp,
                CreatedAt = timestamp,
                UpdatedAt = timestamp,
                MediaTitle = primaryLink.MediaTitle
            };
            _dbContext.MediaProviderLinks.Add(link);
            existingCrossReferences[key] = link;
            titleProviderLinks[titleProviderKey] = link;
        }
    }

    private static string NormalizeProviderId(string providerId)
        => providerId.Trim().ToLowerInvariant();

    private static string BuildCrossReferenceKey(string providerId, string providerMediaId)
        => $"{NormalizeProviderId(providerId)}\u001f{providerMediaId.Trim()}";

    private static string BuildTitleProviderKey(Guid mediaTitleId, string providerId)
        => $"{mediaTitleId:N}\u001f{NormalizeProviderId(providerId)}";

    private MediaProviderLink GetOrCreateProviderLink(
        MediaProviderLibraryItem item,
        MediaProviderLibraryImportResult importResult,
        IDictionary<string, MediaProviderLink> existingLinks,
        IReadOnlyDictionary<string, MediaProviderLink> existingCrossReferences,
        IDictionary<string, MediaProviderLink> titleProviderLinks,
        ref int createdTitles)
    {
        if (existingLinks.TryGetValue(item.ProviderMediaId, out var existingLink))
        {
            if (existingLink.MediaTitle is not null)
            {
                ApplyToMediaTitle(existingLink.MediaTitle, item, importResult.ImportedAt);
            }

            existingLink.ExternalUrl = item.ExternalUrl ?? existingLink.ExternalUrl;
            existingLink.LastVerifiedAt = importResult.ImportedAt;
            existingLink.UpdatedAt = importResult.ImportedAt;
            return existingLink;
        }

        var crossReferenceAnchors = item.CrossReferences
            .Select(reference => BuildCrossReferenceKey(reference.ProviderId, reference.ProviderMediaId))
            .Where(existingCrossReferences.ContainsKey)
            .Select(key => existingCrossReferences[key])
            .GroupBy(link => link.MediaTitleId)
            .Select(group => group.First())
            .Where(anchor =>
            {
                var titleProviderKey = BuildTitleProviderKey(anchor.MediaTitleId, importResult.ProviderId);
                if (!titleProviderLinks.TryGetValue(titleProviderKey, out var providerLink)
                    || providerLink.ExternalId == item.ProviderMediaId)
                {
                    return true;
                }

                _logger.LogWarning(
                    "Cross-reference anchor MediaTitle {MediaTitleId} already has provider link {Provider}/{ExistingProviderMediaId}; refusing requested {RequestedProviderMediaId}.",
                    anchor.MediaTitleId,
                    importResult.ProviderId,
                    providerLink.ExternalId,
                    item.ProviderMediaId);
                return false;
            })
            .ToList();
        MediaTitle title;
        if (crossReferenceAnchors.Count == 1
            && crossReferenceAnchors[0].MediaTitle is { } anchoredTitle)
        {
            title = anchoredTitle;
            ApplyToMediaTitle(title, item, importResult.ImportedAt);
        }
        else
        {
            if (crossReferenceAnchors.Count > 1)
            {
                _logger.LogWarning(
                    "Provider item {Provider}/{ProviderMediaId} has cross-references attached to multiple MediaTitles; creating a separate title instead of guessing.",
                    importResult.ProviderId,
                    item.ProviderMediaId);
            }

            title = CreateMediaTitle(item, importResult.ImportedAt);
            _dbContext.MediaTitles.Add(title);
            createdTitles++;
        }

        var link = new MediaProviderLink
        {
            Id = Guid.NewGuid(),
            MediaTitleId = title.Id,
            Provider = importResult.ProviderId,
            ExternalId = item.ProviderMediaId,
            ExternalUrl = item.ExternalUrl,
            LinkSource = MediaMappingSources.Imported,
            LastVerifiedAt = importResult.ImportedAt,
            CreatedAt = importResult.ImportedAt,
            UpdatedAt = importResult.ImportedAt,
            MediaTitle = title
        };

        _dbContext.MediaProviderLinks.Add(link);
        existingLinks[item.ProviderMediaId] = link;
        titleProviderLinks[BuildTitleProviderKey(title.Id, importResult.ProviderId)] = link;
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
            Score = NormalizeScore(item.Score),
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
        title.TotalKnownCount = item.TotalKnownCount ?? title.TotalKnownCount;
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
        MediaProviderLibraryItem item,
        DateTimeOffset? previousSourceRemoteUpdateAt)
    {
        var latestKnownUpdateAt = entry.ProviderBindings
            .Where(candidate => candidate.Id != binding.Id)
            .Select(candidate => candidate.LastRemoteUpdateAt)
            .Append(entry.LastLocalEditAt)
            .Append(previousSourceRemoteUpdateAt)
            .Where(timestamp => timestamp.HasValue)
            .Select(timestamp => timestamp!.Value)
            .DefaultIfEmpty()
            .Max();

        if (item.LastRemoteUpdateAt is null)
        {
            return latestKnownUpdateAt == default;
        }

        return latestKnownUpdateAt == default || item.LastRemoteUpdateAt > latestKnownUpdateAt;
    }

    private static MediaLibraryEntryState CaptureState(MediaLibraryEntry entry)
        => new(
            entry.Status,
            entry.Score,
            entry.ProgressEpisodes,
            entry.ProgressChapters,
            entry.ProgressVolumes);

    private static IReadOnlyList<MediaProviderOperation> CreateFanOutOperations(
        MediaLibraryEntry entry,
        MediaLibraryProviderBinding sourceBinding,
        MediaLibraryEntryState previousState,
        DateTimeOffset timestamp)
    {
        var operations = new List<MediaProviderOperation>();
        foreach (var target in entry.ProviderBindings.Where(binding =>
                     binding.Id != sourceBinding.Id
                     && binding.ConnectedServiceAccountId.HasValue
                     && binding.MediaProviderLink is not null))
        {
            if (!string.Equals(previousState.Status, entry.Status, StringComparison.Ordinal)
                && entry.Status != MediaLibraryStatuses.Unknown)
            {
                operations.Add(CreateOperation(
                    target,
                    MediaProviderOperationTypes.UpdateStatus,
                    new MediaStatusUpdateRequest
                    {
                        ProviderMediaId = target.MediaProviderLink!.ExternalId,
                        Status = entry.Status,
                        LastKnownRemoteUpdateAt = target.LastRemoteUpdateAt
                    },
                    timestamp));
            }

            if (previousState.Score != entry.Score)
            {
                operations.Add(CreateOperation(
                    target,
                    MediaProviderOperationTypes.UpdateScore,
                    new MediaScoreUpdateRequest
                    {
                        ProviderMediaId = target.MediaProviderLink!.ExternalId,
                        Score = entry.Score,
                        LastKnownRemoteUpdateAt = target.LastRemoteUpdateAt
                    },
                    timestamp));
            }

            if (previousState.ProgressEpisodes != entry.ProgressEpisodes
                || previousState.ProgressChapters != entry.ProgressChapters
                || previousState.ProgressVolumes != entry.ProgressVolumes)
            {
                operations.Add(CreateOperation(
                    target,
                    MediaProviderOperationTypes.UpdateProgress,
                    new MediaProgressUpdateRequest
                    {
                        ProviderMediaId = target.MediaProviderLink!.ExternalId,
                        ProgressEpisodes = entry.ProgressEpisodes,
                        ProgressChapters = entry.ProgressChapters,
                        ProgressVolumes = entry.ProgressVolumes,
                        LastKnownRemoteUpdateAt = target.LastRemoteUpdateAt
                    },
                    timestamp));
            }
        }

        return operations;
    }

    private static MediaProviderOperation CreateOperation<TRequest>(
        MediaLibraryProviderBinding binding,
        string operationType,
        TRequest request,
        DateTimeOffset timestamp)
    {
        return new MediaProviderOperation
        {
            Id = Guid.NewGuid(),
            MediaLibraryProviderBindingId = binding.Id,
            OperationType = operationType,
            PayloadJson = JsonSerializer.Serialize(request, SerializerOptions),
            Status = MediaProviderOperationStatuses.Pending,
            AttemptCount = 0,
            NextAttemptAt = timestamp,
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
            MediaLibraryProviderBinding = binding
        };
    }

    private sealed record MediaLibraryEntryState(
        string Status,
        decimal? Score,
        int? ProgressEpisodes,
        int? ProgressChapters,
        int? ProgressVolumes);

    private static void ApplyRemoteLibraryState(
        MediaLibraryEntry entry,
        MediaProviderLibraryItem item,
        DateTimeOffset timestamp)
    {
        entry.Status = item.Status;
        entry.Score = NormalizeScore(item.Score);
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

    private static decimal? NormalizeScore(decimal? score)
        => score is >= 1m and <= 100m ? score : null;
}
