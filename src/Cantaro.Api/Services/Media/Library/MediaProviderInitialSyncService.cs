using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

public sealed class MediaProviderInitialSyncChangedException : Exception
{
    public MediaProviderInitialSyncChangedException()
        : base("The Cantaro or provider library changed after the preview was created.")
    {
    }
}

public sealed class MediaProviderInitialSyncNotReadyException(string message) : Exception(message);

public sealed class MediaProviderInitialSyncService(
    ApplicationDbContext dbContext,
    IMediaProviderRegistry mediaProviderRegistry,
    MediaLibraryImportService importService,
    ILogger<MediaProviderInitialSyncService> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] ActiveOperationStatuses =
    [
        MediaProviderOperationStatuses.Pending,
        MediaProviderOperationStatuses.Processing,
        MediaProviderOperationStatuses.Retrying
    ];

    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly IMediaProviderRegistry _mediaProviderRegistry = mediaProviderRegistry;
    private readonly MediaLibraryImportService _importService = importService;
    private readonly ILogger<MediaProviderInitialSyncService> _logger = logger;

    public async Task<MediaProviderInitialSyncPreviewDto> PreviewAsync(
        int userId,
        string providerId,
        CancellationToken cancellationToken)
    {
        var normalizedProviderId = NormalizeProviderId(providerId);
        var targetProvider = _mediaProviderRegistry.GetRequired(normalizedProviderId);
        _ = await RequireAccountAsync(targetProvider, userId, cancellationToken);

        var operationState = await ReadOperationStateAsync(userId, cancellationToken);
        if (operationState.Failed > 0)
        {
            return StatusPreview(
                normalizedProviderId,
                "blocked",
                operationState,
                "An earlier provider update needs attention before Cantaro can prepare this sync.");
        }

        if (operationState.Active > 0)
        {
            return StatusPreview(
                normalizedProviderId,
                "settling",
                operationState,
                "Cantaro is finishing existing provider updates before comparing the new provider.");
        }

        var refreshedProviderIds = new List<string>();
        var existingSnapshots = new List<(ConnectedServiceAccount Account, MediaProviderLibraryImportResult Snapshot)>();
        foreach (var existingProviderId in _mediaProviderRegistry.GetSupportedProviderIds()
                     .Select(NormalizeProviderId)
                     .Where(id => id != normalizedProviderId)
                     .OrderBy(id => id, StringComparer.Ordinal))
        {
            var existingProvider = _mediaProviderRegistry.GetRequired(existingProviderId);
            var account = await existingProvider.GetConnectedAccountAsync(userId, cancellationToken);
            if (account is null)
            {
                continue;
            }

            try
            {
                var snapshot = await existingProvider.ImportLibraryAsync(userId, cancellationToken);
                await _importService.ImportAsync(
                    userId,
                    account,
                    snapshot,
                    cancellationToken,
                    fanOutChanges: false);
                existingSnapshots.Add((account, snapshot));
                refreshedProviderIds.Add(existingProviderId);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    exception,
                    "Could not refresh existing media provider {ProviderId} before initial sync to {TargetProviderId} for user {UserId}.",
                    existingProviderId,
                    normalizedProviderId,
                    userId);
                return new MediaProviderInitialSyncPreviewDto
                {
                    ProviderId = normalizedProviderId,
                    Status = "blocked",
                    RefreshedProviderIds = refreshedProviderIds,
                    Message = $"Cantaro could not refresh {existingProviderId}. Retry when that provider is available.",
                    GeneratedAt = DateTimeOffset.UtcNow
                };
            }
        }

        var existingOperations = 0;
        foreach (var (account, snapshot) in existingSnapshots)
        {
            var plan = await BuildPlanAsync(userId, snapshot, cancellationToken);
            existingOperations += await QueuePlanAsync(account, plan, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (existingOperations > 0)
        {
            return new MediaProviderInitialSyncPreviewDto
            {
                ProviderId = normalizedProviderId,
                Status = "settling",
                RefreshedProviderIds = refreshedProviderIds,
                PendingOperations = existingOperations,
                Message = "Cantaro found newer changes and is finishing them across your existing providers.",
                GeneratedAt = DateTimeOffset.UtcNow
            };
        }

        var targetSnapshot = await targetProvider.ImportLibraryAsync(userId, cancellationToken);
        var targetPlan = await BuildPlanAsync(userId, targetSnapshot, cancellationToken);
        return MapPreview(targetPlan, refreshedProviderIds);
    }

    public async Task<MediaProviderInitialSyncResultDto> ApplyAsync(
        int userId,
        string providerId,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        var normalizedProviderId = NormalizeProviderId(providerId);
        var operationState = await ReadOperationStateAsync(userId, cancellationToken);
        if (operationState.Failed > 0 || operationState.Active > 0)
        {
            throw new MediaProviderInitialSyncNotReadyException(
                "Cantaro is still finishing provider updates. Create a fresh preview before applying this sync.");
        }

        var provider = _mediaProviderRegistry.GetRequired(normalizedProviderId);
        var account = await RequireAccountAsync(provider, userId, cancellationToken);
        var snapshot = await provider.ImportLibraryAsync(userId, cancellationToken);
        var plan = await BuildPlanAsync(userId, snapshot, cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(plan.Fingerprint),
                Encoding.UTF8.GetBytes(fingerprint)))
        {
            throw new MediaProviderInitialSyncChangedException();
        }

        var queuedOperations = await QueuePlanAsync(account, plan, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new MediaProviderInitialSyncResultDto
        {
            ProviderId = normalizedProviderId,
            Status = queuedOperations > 0 ? "queued" : "completed",
            Added = plan.AddCount,
            Updated = plan.UpdateCount,
            AlreadyAligned = plan.AlignedCount,
            ProviderOnly = plan.ProviderOnlyItems.Count,
            NeedsMatching = plan.UnresolvedEntries.Count,
            QueuedOperations = queuedOperations,
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private async Task<InitialSyncPlan> BuildPlanAsync(
        int userId,
        MediaProviderLibraryImportResult snapshot,
        CancellationToken cancellationToken)
    {
        var providerId = NormalizeProviderId(snapshot.ProviderId);
        var entries = await _dbContext.MediaLibraryEntries
            .Include(entry => entry.MediaTitle)
                .ThenInclude(title => title!.ProviderLinks)
            .Include(entry => entry.ProviderBindings)
                .ThenInclude(binding => binding.MediaProviderLink)
            .Include(entry => entry.ProviderBindings)
                .ThenInclude(binding => binding.ProviderListMemberships)
            .Where(entry => entry.UserId == userId)
            .OrderBy(entry => entry.Id)
            .ToListAsync(cancellationToken);

        var entryByProviderIdentity = entries
            .SelectMany(entry => entry.MediaTitle!.ProviderLinks.Select(link => new { entry, link }))
            .GroupBy(value => ProviderIdentityKey(value.link.Provider, value.link.ExternalId), StringComparer.Ordinal)
            .Where(group => group.Select(value => value.entry.Id).Distinct().Count() == 1)
            .ToDictionary(group => group.Key, group => group.First().entry, StringComparer.Ordinal);

        var matches = new List<InitialSyncMatch>();
        var providerOnlyItems = new List<MediaProviderLibraryItem>();
        var matchedEntryIds = new HashSet<Guid>();
        foreach (var item in snapshot.Items
                     .GroupBy(item => item.ProviderMediaId, StringComparer.Ordinal)
                     .Select(group => group.First())
                     .OrderBy(item => item.ProviderMediaId, StringComparer.Ordinal))
        {
            var resolvedEntries = new List<MediaLibraryEntry>();
            if (entryByProviderIdentity.TryGetValue(
                    ProviderIdentityKey(providerId, item.ProviderMediaId),
                    out var exactEntry))
            {
                resolvedEntries.Add(exactEntry);
            }
            else
            {
                resolvedEntries.AddRange(item.CrossReferences
                    .Select(reference => ProviderIdentityKey(reference.ProviderId, reference.ProviderMediaId))
                    .Where(entryByProviderIdentity.ContainsKey)
                    .Select(key => entryByProviderIdentity[key])
                    .DistinctBy(entry => entry.Id));
            }

            if (resolvedEntries.Count != 1 || !matchedEntryIds.Add(resolvedEntries[0].Id))
            {
                providerOnlyItems.Add(item);
                continue;
            }

            matches.Add(CreateMatch(resolvedEntries[0], providerId, item));
        }

        var unresolvedEntries = new List<MediaLibraryEntry>();
        foreach (var entry in entries.Where(entry => !matchedEntryIds.Contains(entry.Id)))
        {
            var targetLink = entry.MediaTitle!.ProviderLinks.FirstOrDefault(
                link => string.Equals(link.Provider, providerId, StringComparison.OrdinalIgnoreCase));
            if (targetLink is null)
            {
                unresolvedEntries.Add(entry);
                continue;
            }

            matches.Add(CreateMatch(entry, providerId, remoteItem: null));
        }

        return new InitialSyncPlan
        {
            ProviderId = providerId,
            Matches = matches,
            ProviderOnlyItems = providerOnlyItems,
            UnresolvedEntries = unresolvedEntries,
            Fingerprint = CreateFingerprint(entries, snapshot)
        };
    }

    private async Task<int> QueuePlanAsync(
        ConnectedServiceAccount account,
        InitialSyncPlan plan,
        CancellationToken cancellationToken)
    {
        var activeBindingIds = await _dbContext.MediaProviderOperations
            .Where(operation => ActiveOperationStatuses.Contains(operation.Status))
            .Select(operation => operation.MediaLibraryProviderBindingId)
            .ToHashSetAsync(cancellationToken);
        var timestamp = DateTimeOffset.UtcNow;
        var queued = 0;

        foreach (var match in plan.Matches)
        {
            var link = EnsureProviderLink(match, plan.ProviderId, timestamp);
            var binding = EnsureProviderBinding(match, link, account, timestamp);
            ReplaceProviderListMemberships(binding, match.RemoteItem?.ProviderListNames ?? []);

            if (!match.RequiresWrite)
            {
                binding.LastSyncedAt = timestamp;
                binding.LastRemoteUpdateAt = match.RemoteItem?.LastRemoteUpdateAt;
                binding.UpdatedAt = timestamp;
                continue;
            }

            if (activeBindingIds.Contains(binding.Id))
            {
                continue;
            }

            _dbContext.MediaProviderOperations.Add(new MediaProviderOperation
            {
                Id = Guid.NewGuid(),
                MediaLibraryProviderBindingId = binding.Id,
                OperationType = MediaProviderOperationTypes.SyncLibraryState,
                PayloadJson = JsonSerializer.Serialize(match.Request, SerializerOptions),
                Status = MediaProviderOperationStatuses.Pending,
                AttemptCount = 0,
                NextAttemptAt = timestamp,
                CreatedAt = timestamp,
                UpdatedAt = timestamp,
                MediaLibraryProviderBinding = binding
            });
            activeBindingIds.Add(binding.Id);
            queued++;
        }

        return queued;
    }

    private MediaProviderLink EnsureProviderLink(
        InitialSyncMatch match,
        string providerId,
        DateTimeOffset timestamp)
    {
        var existing = match.Entry.MediaTitle!.ProviderLinks.FirstOrDefault(
            link => string.Equals(link.Provider, providerId, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (match.RemoteItem is not null)
            {
                existing.ExternalUrl = match.RemoteItem.ExternalUrl ?? existing.ExternalUrl;
                existing.LastVerifiedAt = timestamp;
                existing.UpdatedAt = timestamp;
            }
            return existing;
        }

        var remoteItem = match.RemoteItem
            ?? throw new InvalidOperationException("A provider link cannot be created without an exact remote identity.");
        var link = new MediaProviderLink
        {
            Id = Guid.NewGuid(),
            MediaTitleId = match.Entry.MediaTitleId,
            Provider = providerId,
            ExternalId = remoteItem.ProviderMediaId,
            ExternalUrl = remoteItem.ExternalUrl,
            Confidence = 1m,
            LinkSource = MediaMappingSources.Imported,
            LastVerifiedAt = timestamp,
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
            MediaTitle = match.Entry.MediaTitle
        };
        match.Entry.MediaTitle.ProviderLinks.Add(link);
        _dbContext.MediaProviderLinks.Add(link);
        return link;
    }

    private MediaLibraryProviderBinding EnsureProviderBinding(
        InitialSyncMatch match,
        MediaProviderLink link,
        ConnectedServiceAccount account,
        DateTimeOffset timestamp)
    {
        var binding = match.Entry.ProviderBindings.FirstOrDefault(candidate =>
            candidate.MediaProviderLinkId == link.Id
            && string.Equals(candidate.ProviderAccountId, account.ExternalAccountId, StringComparison.Ordinal));
        if (binding is null)
        {
            binding = new MediaLibraryProviderBinding
            {
                Id = Guid.NewGuid(),
                MediaLibraryEntryId = match.Entry.Id,
                MediaProviderLinkId = link.Id,
                ConnectedServiceAccountId = account.Id,
                ProviderAccountId = account.ExternalAccountId,
                ProviderLibraryEntryId = match.RemoteItem?.ProviderLibraryEntryId,
                LastRemoteUpdateAt = match.RemoteItem?.LastRemoteUpdateAt,
                CreatedAt = timestamp,
                UpdatedAt = timestamp,
                MediaLibraryEntry = match.Entry,
                MediaProviderLink = link,
                ConnectedServiceAccount = account
            };
            match.Entry.ProviderBindings.Add(binding);
            _dbContext.MediaLibraryProviderBindings.Add(binding);
        }
        else
        {
            binding.ConnectedServiceAccountId = account.Id;
            binding.ConnectedServiceAccount = account;
            binding.ProviderLibraryEntryId = match.RemoteItem?.ProviderLibraryEntryId
                ?? binding.ProviderLibraryEntryId;
            binding.LastRemoteUpdateAt = match.RemoteItem?.LastRemoteUpdateAt
                ?? binding.LastRemoteUpdateAt;
            binding.UpdatedAt = timestamp;
        }

        return binding;
    }

    private void ReplaceProviderListMemberships(
        MediaLibraryProviderBinding binding,
        IReadOnlyList<string> providerListNames)
    {
        var desired = providerListNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        var removed = binding.ProviderListMemberships
            .Where(membership => !desired.Contains(membership.Name))
            .ToList();
        _dbContext.MediaProviderListMemberships.RemoveRange(removed);

        var existing = binding.ProviderListMemberships
            .Except(removed)
            .Select(membership => membership.Name)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var name in desired.Where(name => !existing.Contains(name)))
        {
            binding.ProviderListMemberships.Add(new MediaProviderListMembership
            {
                MediaLibraryProviderBindingId = binding.Id,
                Name = name
            });
        }
    }

    private static InitialSyncMatch CreateMatch(
        MediaLibraryEntry entry,
        string providerId,
        MediaProviderLibraryItem? remoteItem)
    {
        var title = entry.MediaTitle!;
        var providerMediaId = remoteItem?.ProviderMediaId
            ?? title.ProviderLinks.First(link =>
                string.Equals(link.Provider, providerId, StringComparison.OrdinalIgnoreCase)).ExternalId;
        int? progressEpisodes = title.PrimaryProgressDimension == MediaProgressDimensions.Episode
            ? entry.ProgressEpisodes ?? 0
            : null;
        var tracksReadingProgress = title.MediaKind == MediaKinds.Manga
            || title.PrimaryProgressDimension is MediaProgressDimensions.Chapter or MediaProgressDimensions.Volume;
        int? progressChapters = tracksReadingProgress ? entry.ProgressChapters ?? 0 : null;
        int? progressVolumes = tracksReadingProgress ? entry.ProgressVolumes ?? 0 : null;
        var request = new MediaLibraryStateSyncRequest
        {
            ProviderMediaId = providerMediaId,
            Status = entry.Status,
            Score = entry.Score,
            ProgressEpisodes = progressEpisodes,
            ProgressChapters = progressChapters,
            ProgressVolumes = progressVolumes,
            LastKnownRemoteUpdateAt = remoteItem?.LastRemoteUpdateAt
        };

        var requiresWrite = remoteItem is null || (
            !string.Equals(entry.Status, remoteItem.Status, StringComparison.Ordinal)
            || ProjectProviderScore(providerId, entry.Score) != NormalizeScore(remoteItem.Score)
            || progressEpisodes.HasValue && progressEpisodes != (remoteItem.ProgressEpisodes ?? 0)
            || progressChapters.HasValue && progressChapters != (remoteItem.ProgressChapters ?? 0)
            || progressVolumes.HasValue && progressVolumes != (remoteItem.ProgressVolumes ?? 0));

        return new InitialSyncMatch
        {
            Entry = entry,
            RemoteItem = remoteItem,
            Request = request,
            RequiresWrite = requiresWrite
        };
    }

    private static string CreateFingerprint(
        IReadOnlyList<MediaLibraryEntry> entries,
        MediaProviderLibraryImportResult snapshot)
    {
        var canonical = entries
            .OrderBy(entry => entry.Id)
            .Select(entry => new
            {
                entry.Id,
                entry.Status,
                Score = NormalizeScore(entry.Score),
                entry.ProgressEpisodes,
                entry.ProgressChapters,
                entry.ProgressVolumes,
                ProviderLinks = entry.MediaTitle!.ProviderLinks
                    .OrderBy(link => link.Provider, StringComparer.Ordinal)
                    .ThenBy(link => link.ExternalId, StringComparer.Ordinal)
                    .Select(link => new { Provider = NormalizeProviderId(link.Provider), link.ExternalId })
            });
        var remote = snapshot.Items
            .OrderBy(item => item.ProviderMediaId, StringComparer.Ordinal)
            .Select(item => new
            {
                item.ProviderMediaId,
                item.Status,
                Score = NormalizeScore(item.Score),
                item.ProgressEpisodes,
                item.ProgressChapters,
                item.ProgressVolumes,
                item.LastRemoteUpdateAt,
                CrossReferences = item.CrossReferences
                    .OrderBy(reference => reference.ProviderId, StringComparer.Ordinal)
                    .ThenBy(reference => reference.ProviderMediaId, StringComparer.Ordinal)
                    .Select(reference => new
                    {
                        ProviderId = NormalizeProviderId(reference.ProviderId),
                        reference.ProviderMediaId
                    })
            });
        var json = JsonSerializer.Serialize(new
        {
            ProviderId = NormalizeProviderId(snapshot.ProviderId),
            Canonical = canonical,
            Remote = remote
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static MediaProviderInitialSyncPreviewDto MapPreview(
        InitialSyncPlan plan,
        IReadOnlyList<string> refreshedProviderIds)
    {
        return new MediaProviderInitialSyncPreviewDto
        {
            ProviderId = plan.ProviderId,
            Status = "ready",
            Fingerprint = plan.Fingerprint,
            RefreshedProviderIds = refreshedProviderIds,
            WillAdd = plan.AddCount,
            WillUpdate = plan.UpdateCount,
            AlreadyAligned = plan.AlignedCount,
            ProviderOnly = plan.ProviderOnlyItems.Count,
            NeedsMatching = plan.UnresolvedEntries.Count,
            UnresolvedTitles = plan.UnresolvedEntries.Select(entry => new MediaProviderInitialSyncTitleDto
            {
                MediaTitleId = entry.MediaTitleId,
                Title = entry.MediaTitle!.CanonicalTitle,
                MediaKind = entry.MediaTitle.MediaKind
            }).ToList(),
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private static MediaProviderInitialSyncPreviewDto StatusPreview(
        string providerId,
        string status,
        ProviderOperationState operationState,
        string message)
    {
        return new MediaProviderInitialSyncPreviewDto
        {
            ProviderId = providerId,
            Status = status,
            PendingOperations = operationState.Active + operationState.Failed,
            Message = message,
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private async Task<ProviderOperationState> ReadOperationStateAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        var states = await _dbContext.MediaProviderOperations
            .Where(operation => operation.MediaLibraryProviderBinding!.MediaLibraryEntry!.UserId == userId)
            .GroupBy(operation => operation.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        return new ProviderOperationState(
            states.Where(state => ActiveOperationStatuses.Contains(state.Status)).Sum(state => state.Count),
            states.Where(state => state.Status == MediaProviderOperationStatuses.Failed).Sum(state => state.Count));
    }

    private static async Task<ConnectedServiceAccount> RequireAccountAsync(
        IMediaProvider provider,
        int userId,
        CancellationToken cancellationToken)
    {
        return await provider.GetConnectedAccountAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException($"{provider.ProviderId} is not connected.");
    }

    private static string ProviderIdentityKey(string providerId, string providerMediaId)
        => $"{NormalizeProviderId(providerId)}\u001f{providerMediaId.Trim()}";

    private static string NormalizeProviderId(string providerId)
        => providerId.Trim().ToLowerInvariant();

    private static decimal? NormalizeScore(decimal? score)
        => score is >= 1m and <= 100m ? score : null;

    private static decimal? ProjectProviderScore(string providerId, decimal? score)
    {
        var normalized = NormalizeScore(score);
        if (normalized is null)
        {
            return null;
        }

        if (string.Equals(providerId, MediaObservationSiteIdentifiers.MyAnimeList, StringComparison.Ordinal))
        {
            return Math.Clamp(
                decimal.Round(normalized.Value / 10m, 0, MidpointRounding.AwayFromZero),
                1m,
                10m) * 10m;
        }

        return decimal.Round(normalized.Value, 0, MidpointRounding.AwayFromZero);
    }

    private sealed class InitialSyncPlan
    {
        public required string ProviderId { get; init; }
        public required IReadOnlyList<InitialSyncMatch> Matches { get; init; }
        public required IReadOnlyList<MediaProviderLibraryItem> ProviderOnlyItems { get; init; }
        public required IReadOnlyList<MediaLibraryEntry> UnresolvedEntries { get; init; }
        public required string Fingerprint { get; init; }

        public int AddCount => Matches.Count(match => match.RemoteItem is null);
        public int UpdateCount => Matches.Count(match => match.RemoteItem is not null && match.RequiresWrite);
        public int AlignedCount => Matches.Count(match => !match.RequiresWrite);
    }

    private sealed class InitialSyncMatch
    {
        public required MediaLibraryEntry Entry { get; init; }
        public required MediaProviderLibraryItem? RemoteItem { get; init; }
        public required MediaLibraryStateSyncRequest Request { get; init; }
        public required bool RequiresWrite { get; init; }
    }

    private sealed record ProviderOperationState(int Active, int Failed);
}
