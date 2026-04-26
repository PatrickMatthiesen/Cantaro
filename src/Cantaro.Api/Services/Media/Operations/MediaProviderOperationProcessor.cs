using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

public enum MediaProviderOperationExecutionOutcome
{
    Succeeded,
    Queued,
    Failed
}

public sealed class MediaProviderOperationExecutionResult
{
    public required MediaProviderOperationExecutionOutcome Outcome { get; init; }

    public string? LastError { get; init; }
}

public class MediaProviderOperationProcessor(
    ApplicationDbContext dbContext,
    IMediaProviderRegistry mediaProviderRegistry,
    ILogger<MediaProviderOperationProcessor> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly IMediaProviderRegistry _mediaProviderRegistry = mediaProviderRegistry;
    private readonly ILogger<MediaProviderOperationProcessor> _logger = logger;

    public async Task<MediaProviderOperation> EnqueueProgressUpdateAsync(
        int userId,
        MediaLibraryEntry entry,
        MediaProgressUpdateRequest request,
        CancellationToken cancellationToken)
    {
        return await EnqueueAsync(
            userId,
            entry,
            MediaProviderOperationTypes.UpdateProgress,
            request,
            cancellationToken);
    }

    public async Task<MediaProviderOperation> EnqueueAutoProgressAsync(
        int userId,
        MediaLibraryEntry entry,
        AutoProgressUpdatePayload payload,
        CancellationToken cancellationToken)
    {
        return await EnqueueAsync(
            userId,
            entry,
            MediaProviderOperationTypes.AutoProgressUpdate,
            payload,
            cancellationToken);
    }

    public async Task<MediaProviderOperation> EnqueueStatusUpdateAsync(
        int userId,
        MediaLibraryEntry entry,
        MediaStatusUpdateRequest request,
        CancellationToken cancellationToken)
    {
        return await EnqueueAsync(
            userId,
            entry,
            MediaProviderOperationTypes.UpdateStatus,
            request,
            cancellationToken);
    }

    public async Task<MediaProviderOperationExecutionResult> ProcessOperationAsync(Guid operationId, CancellationToken cancellationToken)
    {
        DetachTrackedOperation(operationId);

        var claimResult = await TryClaimOperationAsync(operationId, cancellationToken);
        if (claimResult is not null)
        {
            return claimResult;
        }

        var operation = await _dbContext.MediaProviderOperations
            .Include(item => item.MediaLibraryEntry)
            .FirstOrDefaultAsync(item => item.Id == operationId, cancellationToken)
            ?? throw new InvalidOperationException($"Media provider operation {operationId} was not found.");

        try
        {
            var provider = _mediaProviderRegistry.GetRequired(operation.Provider);
            var mutationResult = await ExecuteOperationAsync(provider, operation, cancellationToken);
            ApplyMutationSuccess(operation, mutationResult);
            _dbContext.MediaProviderOperations.Remove(operation);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new MediaProviderOperationExecutionResult
            {
                Outcome = MediaProviderOperationExecutionOutcome.Succeeded
            };
        }
        catch (Exception ex)
        {
            ApplyMutationFailure(operation, ex);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return new MediaProviderOperationExecutionResult
        {
            Outcome = operation.Status == MediaProviderOperationStatuses.Failed
                ? MediaProviderOperationExecutionOutcome.Failed
                : MediaProviderOperationExecutionOutcome.Queued,
            LastError = operation.LastError
        };
    }

    public async Task<int> ProcessDueOperationsAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var dueOperationIds = await _dbContext.MediaProviderOperations
            .Where(operation =>
                (operation.Status == MediaProviderOperationStatuses.Pending
                    || operation.Status == MediaProviderOperationStatuses.Retrying)
                && (operation.NextAttemptAt == null || operation.NextAttemptAt <= now))
            .OrderBy(operation => operation.CreatedAt)
            .Select(operation => operation.Id)
            .Take(10)
            .ToListAsync(cancellationToken);

        foreach (var operationId in dueOperationIds)
        {
            await ProcessOperationAsync(operationId, cancellationToken);
        }

        return dueOperationIds.Count;
    }

    private void DetachTrackedOperation(Guid operationId)
    {
        var trackedEntry = _dbContext.ChangeTracker
            .Entries<MediaProviderOperation>()
            .FirstOrDefault(entry => entry.Entity.Id == operationId);

        if (trackedEntry is not null)
        {
            trackedEntry.State = EntityState.Detached;
        }
    }

    private async Task<MediaProviderOperationExecutionResult?> TryClaimOperationAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var claimedAt = DateTimeOffset.UtcNow;
        var claimedCount = await _dbContext.MediaProviderOperations
            .Where(item =>
                item.Id == operationId
                && (item.Status == MediaProviderOperationStatuses.Pending
                    || item.Status == MediaProviderOperationStatuses.Retrying))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.Status, MediaProviderOperationStatuses.Processing)
                    .SetProperty(item => item.LastAttemptAt, claimedAt)
                    .SetProperty(item => item.AttemptCount, item => item.AttemptCount + 1)
                    .SetProperty(item => item.UpdatedAt, claimedAt),
                cancellationToken);

        if (claimedCount > 0)
        {
            return null;
        }

        var operationState = await _dbContext.MediaProviderOperations
            .AsNoTracking()
            .Where(item => item.Id == operationId)
            .Select(item => new
            {
                item.Status,
                item.LastError
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (operationState is null)
        {
            _logger.LogInformation(
                "Media provider operation {OperationId} was already completed before it could be claimed.",
                operationId);
            return new MediaProviderOperationExecutionResult
            {
                Outcome = MediaProviderOperationExecutionOutcome.Queued
            };
        }

        return operationState.Status == MediaProviderOperationStatuses.Failed
            ? new MediaProviderOperationExecutionResult
            {
                Outcome = MediaProviderOperationExecutionOutcome.Failed,
                LastError = operationState.LastError
            }
            : new MediaProviderOperationExecutionResult
            {
                Outcome = MediaProviderOperationExecutionOutcome.Queued
            };
    }

    private async Task<MediaProviderOperation> EnqueueAsync<TRequest>(
        int userId,
        MediaLibraryEntry entry,
        string operationType,
        TRequest request,
        CancellationToken cancellationToken)
    {
        var operation = new MediaProviderOperation
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            MediaLibraryEntryId = entry.Id,
            ConnectedServiceAccountId = entry.ConnectedServiceAccountId,
            Provider = entry.Provider,
            OperationType = operationType,
            PayloadJson = JsonSerializer.Serialize(request, SerializerOptions),
            Status = MediaProviderOperationStatuses.Pending,
            AttemptCount = 0,
            NextAttemptAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.MediaProviderOperations.Add(operation);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return operation;
    }

    private async Task<MediaProviderMutationResult> ExecuteOperationAsync(
        IMediaProvider provider,
        MediaProviderOperation operation,
        CancellationToken cancellationToken)
    {
        if (operation.MediaLibraryEntry is null)
        {
            throw new InvalidOperationException("Provider operation is missing a media library entry.");
        }

        return operation.OperationType switch
        {
            MediaProviderOperationTypes.UpdateProgress => await provider.UpdateProgressAsync(
                operation.UserId,
                DeserializePayload<MediaProgressUpdateRequest>(operation.PayloadJson),
                cancellationToken),
            MediaProviderOperationTypes.UpdateStatus => await provider.UpdateStatusAsync(
                operation.UserId,
                DeserializePayload<MediaStatusUpdateRequest>(operation.PayloadJson),
                cancellationToken),
            MediaProviderOperationTypes.AutoProgressUpdate => await ExecuteAutoProgressAsync(
                provider,
                operation,
                cancellationToken),
            _ => throw new InvalidOperationException($"Provider operation type '{operation.OperationType}' is not supported.")
        };
    }

    private static TRequest DeserializePayload<TRequest>(string payloadJson)
    {
        return JsonSerializer.Deserialize<TRequest>(payloadJson, SerializerOptions)
            ?? throw new InvalidOperationException("Provider operation payload could not be deserialized.");
    }

    private async Task<MediaProviderMutationResult> ExecuteAutoProgressAsync(
        IMediaProvider provider,
        MediaProviderOperation operation,
        CancellationToken cancellationToken)
    {
        var payload = DeserializePayload<AutoProgressUpdatePayload>(operation.PayloadJson);

        // Sync-metadata guard: re-read the current LastRemoteUpdateAt from the DB.
        // If the provider has written a newer timestamp since we enqueued this
        // operation, the entry may already be ahead and we should skip the write.
        if (operation.MediaLibraryEntry is not null
            && payload.LastKnownRemoteUpdateAt.HasValue)
        {
            var currentRemoteUpdateAt = await _dbContext.MediaLibraryEntries
                .AsNoTracking()
                .Where(e => e.Id == operation.MediaLibraryEntryId)
                .Select(e => e.LastRemoteUpdateAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (currentRemoteUpdateAt.HasValue
                && currentRemoteUpdateAt.Value > payload.LastKnownRemoteUpdateAt.Value)
            {
                _logger.LogInformation(
                    "Auto-progress skipped for operation {OperationId}: " +
                    "provider has newer remote state ({RemoteAt} > {KnownAt}). " +
                    "Observation: {ObservationId}.",
                    operation.Id,
                    currentRemoteUpdateAt.Value,
                    payload.LastKnownRemoteUpdateAt.Value,
                    payload.TriggeredByObservationId);

                // Return a synthetic "already up-to-date" result so the operation
                // is treated as successful and removed from the queue.
                return new MediaProviderMutationResult
                {
                    ProviderId = operation.Provider,
                    ProviderMediaId = payload.ProviderMediaId,
                    AppliedAt = DateTimeOffset.UtcNow,
                    LastRemoteUpdateAt = currentRemoteUpdateAt
                };
            }
        }

        return await provider.UpdateProgressAsync(
            operation.UserId,
            new MediaProgressUpdateRequest
            {
                ProviderMediaId = payload.ProviderMediaId,
                ProgressEpisodes = payload.ProgressEpisodes,
                ProgressChapters = payload.ProgressChapters,
                ProgressVolumes = payload.ProgressVolumes,
                LastKnownRemoteUpdateAt = payload.LastKnownRemoteUpdateAt
            },
            cancellationToken);
    }

    private void ApplyMutationSuccess(MediaProviderOperation operation, MediaProviderMutationResult result)
    {
        var now = DateTimeOffset.UtcNow;
        var entry = operation.MediaLibraryEntry ?? throw new InvalidOperationException("Provider operation is missing a media library entry.");

        switch (operation.OperationType)
        {
            case MediaProviderOperationTypes.UpdateProgress:
            {
                var request = DeserializePayload<MediaProgressUpdateRequest>(operation.PayloadJson);
                entry.ProgressEpisodes = request.ProgressEpisodes ?? entry.ProgressEpisodes;
                entry.ProgressChapters = request.ProgressChapters ?? entry.ProgressChapters;
                entry.ProgressVolumes = request.ProgressVolumes ?? entry.ProgressVolumes;
                entry.LastMutationSource = MediaMutationSources.UserProgressUpdate;
                break;
            }
            case MediaProviderOperationTypes.UpdateStatus:
            {
                var request = DeserializePayload<MediaStatusUpdateRequest>(operation.PayloadJson);
                entry.NormalizedStatus = request.Status;
                entry.LastMutationSource = MediaMutationSources.UserStatusUpdate;
                break;
            }
            case MediaProviderOperationTypes.AutoProgressUpdate:
            {
                var payload = DeserializePayload<AutoProgressUpdatePayload>(operation.PayloadJson);
                entry.ProgressEpisodes = payload.ProgressEpisodes ?? entry.ProgressEpisodes;
                entry.ProgressChapters = payload.ProgressChapters ?? entry.ProgressChapters;
                entry.ProgressVolumes = payload.ProgressVolumes ?? entry.ProgressVolumes;
                entry.LastMutationSource = MediaMutationSources.ObservationAutoProgress;

                _logger.LogInformation(
                    "Auto-progress applied for user {UserId}, entry {EntryId}: " +
                    "ep={Ep}/ch={Ch}/vol={Vol}. " +
                    "Provenance: observation={ObsId}, site={Site}, hint=\"{Hint}\", score={Score}.",
                    operation.UserId,
                    entry.Id,
                    payload.ProgressEpisodes,
                    payload.ProgressChapters,
                    payload.ProgressVolumes,
                    payload.TriggeredByObservationId,
                    payload.ObservedSiteIdentifier,
                    payload.ObservedProgressHint,
                    payload.ObservationMatchScore);
                break;
            }
        }

        entry.LastSyncedAt = now;
        entry.LastRemoteUpdateAt = result.LastRemoteUpdateAt ?? now;
        entry.LastLocalEditAt = now;
        entry.RawStatus = result.RawStatus ?? entry.RawStatus;
        entry.RawMetadata = result.RawMetadata ?? entry.RawMetadata;
        entry.UpdatedAt = now;
        _logger.LogInformation(
            "Media provider operation {OperationId} ({OperationType}) succeeded for user {UserId} on provider {Provider}.",
            operation.Id,
            operation.OperationType,
            operation.UserId,
            operation.Provider);
    }

    private void ApplyMutationFailure(MediaProviderOperation operation, Exception exception)
    {
        var now = DateTimeOffset.UtcNow;
        var maxAttempts = 4;
        var shouldRetry = operation.AttemptCount < maxAttempts;

        operation.Status = shouldRetry
            ? MediaProviderOperationStatuses.Retrying
            : MediaProviderOperationStatuses.Failed;
        operation.NextAttemptAt = shouldRetry
            ? now.Add(GetRetryDelay(operation.AttemptCount))
            : null;
        operation.LastError = exception.Message;
        operation.UpdatedAt = now;

        _logger.LogWarning(
            exception,
            "Media provider operation {OperationId} failed on attempt {AttemptCount}. Next status: {Status}",
            operation.Id,
            operation.AttemptCount,
            operation.Status);
    }

    private static TimeSpan GetRetryDelay(int attemptCount)
    {
        return attemptCount switch
        {
            1 => TimeSpan.FromMinutes(1),
            2 => TimeSpan.FromMinutes(5),
            3 => TimeSpan.FromMinutes(15),
            _ => TimeSpan.FromMinutes(30)
        };
    }
}
