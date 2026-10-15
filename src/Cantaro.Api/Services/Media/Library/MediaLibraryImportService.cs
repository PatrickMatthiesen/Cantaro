using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Cantaro.Api.Services;

public class MediaLibraryImportService(
    ApplicationDbContext dbContext,
    MediaLibraryEventHub eventHub,
    MediaCatalogCanonicalizationService catalogCanonicalizationService,
    ILogger<MediaLibraryImportService> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly DbContextOptions<ApplicationDbContext> _options =
        (DbContextOptions<ApplicationDbContext>)dbContext.GetService<IDbContextOptions>();
    private readonly MediaLibraryEventHub _eventHub = eventHub;
    private readonly MediaCatalogCanonicalizationService _catalogCanonicalizationService = catalogCanonicalizationService;
    private readonly ILogger<MediaLibraryImportService> _logger = logger;

    public async Task<MediaLibraryImportPersistenceResult> ImportAsync(
        int userId,
        ConnectedServiceAccount account,
        MediaProviderLibraryImportResult importResult,
        CancellationToken cancellationToken,
        bool fanOutChanges = true)
    {
        // Import owns its atomic write. A fresh context on every attempt keeps
        // failed inserts out of the caller's tracker and preserves its pending work.
        for (var attempt = 0; ; attempt++)
        {
            await using var db = new ApplicationDbContext(_options);
            try
            {
                return await ImportCoreAsync(db, userId, account, importResult, cancellationToken, fanOutChanges);
            }
            catch (DbUpdateException exception) when (attempt < 2
                && MediaCatalogCanonicalizationService.IsIdentityRace(exception))
            {
                _logger.LogDebug(exception, "Retrying media library import after a catalog identity race, attempt {Attempt}.", attempt + 1);
            }
        }
    }

    private async Task<MediaLibraryImportPersistenceResult> ImportCoreAsync(
        ApplicationDbContext db,
        int userId,
        ConnectedServiceAccount account,
        MediaProviderLibraryImportResult importResult,
        CancellationToken cancellationToken,
        bool fanOutChanges)
    {
        var mediaIds = importResult.Items
            .Select(item => item.ProviderMediaId)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var candidates = importResult.Items.Select(item => new MediaCatalogCandidate(
            new MediaProviderTitleDetails
            {
                ProviderId = importResult.ProviderId,
                ProviderMediaId = item.ProviderMediaId,
                Title = item.Title,
                ExternalUrl = item.ExternalUrl,
                NativeTitle = item.OriginalTitle ?? item.NativeTitle,
                Synonyms = item.Synonyms,
                MediaKind = item.MediaKind,
                Synopsis = item.Synopsis,
                Format = item.Format,
                PosterUrl = item.PosterUrl,
                BackgroundUrl = item.BackgroundUrl,
                CrossReferences = item.CrossReferences,
                StartYear = item.StartYear,
                EpisodeCount = item.EpisodeCount,
                ChapterCount = item.ChapterCount,
                VolumeCount = item.VolumeCount,
                ReleasedCount = item.ReleasedCount,
                TotalKnownCount = item.TotalKnownCount,
                NextReleaseAt = item.NextReleaseAt,
                NextReleaseLabel = item.NextReleaseLabel,
                PrimaryProgressDimension = item.PrimaryProgressDimension,
                ReleaseStatusDimension = item.ReleaseStatusDimension,
                AvailabilityRefreshSucceeded = false
            }))
            .ToList();
        var catalogResolution = await _catalogCanonicalizationService.ResolveAsync(
            db, candidates, importResult.ImportedAt, cancellationToken);
        var resolvedLinks = catalogResolution.Links;

        var existingBindings = await db.MediaLibraryProviderBindings
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

        var knownTitleIds = resolvedLinks.Values
            .Select(link => link.MediaTitleId)
            .Distinct()
            .ToList();
        var entriesByTitleId = await db.MediaLibraryEntries
            .Include(entry => entry.ProviderBindings)
                .ThenInclude(binding => binding.MediaProviderLink)
            .Where(entry => entry.UserId == userId && knownTitleIds.Contains(entry.MediaTitleId))
            .ToDictionaryAsync(entry => entry.MediaTitleId, cancellationToken);

        var createdTitles = catalogResolution.CreatedTitles;
        var createdEntries = 0;
        var updatedEntries = 0;
        var createdBindings = 0;
        var fanOutOperations = new List<MediaProviderOperation>();

        foreach (var item in importResult.Items)
        {
            var link = resolvedLinks[MediaCatalogIdentity.Create(importResult.ProviderId, item.ProviderMediaId)];

            entriesByTitleId.TryGetValue(link.MediaTitleId, out var entry);
            var entryWasCreated = entry is null;
            if (entry is null)
            {
                entry = CreateLibraryEntry(userId, link.MediaTitleId, item, importResult.ImportedAt);
                db.MediaLibraryEntries.Add(entry);
                entriesByTitleId[link.MediaTitleId] = entry;
                createdEntries++;
            }

            var previousState = CaptureState(entry);
            existingBindings.TryGetValue(item.ProviderMediaId, out var binding);
            var sourceBindingExisted = binding is not null;
            var previousSourceRemoteUpdateAt = binding?.LastRemoteUpdateAt;
            if (binding is null)
            {
                binding = CreateProviderBinding(entry, link, account, item, importResult.ImportedAt);
                db.MediaLibraryProviderBindings.Add(binding);
                existingBindings[item.ProviderMediaId] = binding;
                createdBindings++;
            }
            else
            {
                binding.ConnectedServiceAccountId = account.Id;
                binding.ProviderLibraryEntryId = item.ProviderLibraryEntryId;
                updatedEntries++;
            }

            // A newly connected provider contributes its binding and metadata, but
            // cannot replace an existing viewer history during its first import.
            var shouldApplyRemoteState = entryWasCreated || (sourceBindingExisted
                && ShouldApplyRemoteLibraryState(
                    entry,
                    binding,
                    item,
                    previousSourceRemoteUpdateAt));
            var preserveNormalizedStatus = shouldApplyRemoteState
                && ShouldPreserveNormalizedStatus(entry, binding, item);
            if (shouldApplyRemoteState && sourceBindingExisted && !preserveNormalizedStatus
                && (binding.LastRequestedStatus is not null || binding.LastAppliedStatus is not null))
            {
                binding.LastRequestedStatus = null;
                binding.LastAppliedStatus = null;
            }
            if (shouldApplyRemoteState)
            {
                ApplyRemoteLibraryState(entry, item, importResult.ImportedAt, preserveNormalizedStatus);
                if (!entryWasCreated && fanOutChanges)
                {
                    fanOutOperations.AddRange(CreateFanOutOperations(
                        entry,
                        binding,
                        previousState,
                        importResult.ImportedAt,
                        allowProgress: !item.HasNonContiguousProgress));
                }
            }

            binding.LastSyncedAt = importResult.ImportedAt;
            binding.LastRemoteUpdateAt = item.LastRemoteUpdateAt;
            binding.UpdatedAt = importResult.ImportedAt;

            ReplaceProviderListMemberships(db, binding, item.ProviderListNames);
        }

        db.MediaProviderOperations.AddRange(fanOutOperations);

        await db.SaveChangesAsync(cancellationToken);
        _eventHub.Publish(userId);

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

    private static void ReplaceProviderListMemberships(
        ApplicationDbContext db,
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
        db.MediaProviderListMemberships.RemoveRange(removedMemberships);

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

    private static bool ShouldPreserveNormalizedStatus(
        MediaLibraryEntry entry,
        MediaLibraryProviderBinding binding,
        MediaProviderLibraryItem item)
        => binding.LastRequestedStatus is not null
            && binding.LastAppliedStatus is not null
            && string.Equals(entry.Status, binding.LastRequestedStatus, StringComparison.Ordinal)
            && string.Equals(item.Status, binding.LastAppliedStatus, StringComparison.Ordinal);

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
        DateTimeOffset timestamp,
        bool allowProgress)
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
                    MediaProviderOperationTypes.ImportFanOutStatus,
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
                    MediaProviderOperationTypes.ImportFanOutScore,
                    new MediaScoreUpdateRequest
                    {
                        ProviderMediaId = target.MediaProviderLink!.ExternalId,
                        Score = entry.Score,
                        LastKnownRemoteUpdateAt = target.LastRemoteUpdateAt
                    },
                    timestamp));
            }

            if (allowProgress && (previousState.ProgressEpisodes != entry.ProgressEpisodes
                || previousState.ProgressChapters != entry.ProgressChapters
                || previousState.ProgressVolumes != entry.ProgressVolumes))
            {
                operations.Add(CreateOperation(
                    target,
                    MediaProviderOperationTypes.ImportFanOutProgress,
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
        DateTimeOffset timestamp,
        bool preserveNormalizedStatus)
    {
        if (!preserveNormalizedStatus)
        {
            entry.Status = item.Status;
        }
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
