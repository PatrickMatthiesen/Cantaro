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
    internal static readonly TimeSpan ProcessingLeaseTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ExecutionTimeout = TimeSpan.FromMinutes(4);
    private static readonly Dictionary<Guid, BindingExecutionGate> BindingExecutionGates = new();
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly IMediaProviderRegistry _mediaProviderRegistry = mediaProviderRegistry;
    private readonly ILogger<MediaProviderOperationProcessor> _logger = logger;

    public async Task<MediaProviderOperation> EnqueueProgressUpdateAsync(
        int userId,
        MediaLibraryProviderBinding binding,
        MediaProgressUpdateRequest request,
        CancellationToken cancellationToken,
        bool deferSave = false)
    {
        return await EnqueueAsync(
            userId,
            binding,
            MediaProviderOperationTypes.UpdateProgress,
            request,
            cancellationToken,
            deferSave);
    }

    public async Task<MediaProviderOperation> EnqueueAutoProgressAsync(
        int userId,
        MediaLibraryProviderBinding binding,
        AutoProgressUpdatePayload payload,
        CancellationToken cancellationToken,
        bool deferSave = false)
    {
        return await EnqueueAsync(
            userId,
            binding,
            MediaProviderOperationTypes.AutoProgressUpdate,
            payload,
            cancellationToken,
            deferSave);
    }

    public async Task<MediaProviderOperation> EnqueueStatusUpdateAsync(
        int userId,
        MediaLibraryProviderBinding binding,
        MediaStatusUpdateRequest request,
        CancellationToken cancellationToken,
        bool deferSave = false)
    {
        return await EnqueueAsync(
            userId,
            binding,
            MediaProviderOperationTypes.UpdateStatus,
            request,
            cancellationToken,
            deferSave);
    }

    public async Task<MediaProviderOperation> EnqueueScoreUpdateAsync(
        int userId,
        MediaLibraryProviderBinding binding,
        MediaScoreUpdateRequest request,
        CancellationToken cancellationToken,
        bool deferSave = false)
    {
        return await EnqueueAsync(
            userId,
            binding,
            MediaProviderOperationTypes.UpdateScore,
            request,
            cancellationToken,
            deferSave);
    }

    public async Task<MediaProviderOperation> EnqueueLibraryStateSyncAsync(
        int userId,
        MediaLibraryProviderBinding binding,
        MediaLibraryStateSyncRequest request,
        CancellationToken cancellationToken)
    {
        return await EnqueueAsync(
            userId,
            binding,
            MediaProviderOperationTypes.SyncLibraryState,
            request,
            cancellationToken);
    }

    public async Task<MediaProviderOperationExecutionResult> ProcessOperationAsync(Guid operationId, CancellationToken cancellationToken)
    {
        DetachTrackedOperation(operationId);

        var claimedAt = await TryClaimOperationAsync(operationId, cancellationToken);
        if (claimedAt is null)
        {
            var failed = await _dbContext.MediaProviderOperations.AsNoTracking()
                .Where(item => item.Id == operationId && item.Status == MediaProviderOperationStatuses.Failed)
                .Select(item => new MediaProviderOperationExecutionResult
                {
                    Outcome = MediaProviderOperationExecutionOutcome.Failed,
                    LastError = item.LastError
                })
                .SingleOrDefaultAsync(cancellationToken);
            return failed ?? Queued();
        }

        var claim = OwnedClaim(operationId, claimedAt.Value);
        try
        {
            var operation = await ActiveClaim(operationId, claimedAt.Value)
                .Include(item => item.MediaLibraryProviderBinding)
                    .ThenInclude(binding => binding!.MediaLibraryEntry)
                .Include(item => item.MediaLibraryProviderBinding)
                    .ThenInclude(binding => binding!.MediaProviderLink)
                .SingleOrDefaultAsync(cancellationToken);
            if (operation is null)
            {
                return Queued();
            }

            AutoProgressUpdatePayload? autoProgress;
            MediaProviderMutationResult? mutationResult;
            DateTimeOffset? expectedRemoteUpdateAt;
            try
            {
                var binding = operation.MediaLibraryProviderBinding!;
                await _dbContext.Entry(binding).ReloadAsync(cancellationToken);
                await _dbContext.Entry(binding.MediaLibraryEntry!).ReloadAsync(cancellationToken);

                if (ShouldSkipStaleOperation(operation))
                {
                    var deleted = await ActiveClaim(operation).ExecuteDeleteAsync(cancellationToken) == 1;
                    if (deleted)
                    {
                        _logger.LogInformation("Skipped stale media provider operation {OperationId} ({OperationType}).",
                            operation.Id, operation.OperationType);
                    }
                    return CompletionResult(deleted);
                }

                var provider = _mediaProviderRegistry.GetRequired(binding.MediaProviderLink!.Provider);
                autoProgress = operation.OperationType == MediaProviderOperationTypes.AutoProgressUpdate
                    ? DeserializePayload<AutoProgressUpdatePayload>(operation.PayloadJson) : null;
                expectedRemoteUpdateAt = autoProgress is null
                    ? binding.LastRemoteUpdateAt
                    : autoProgress.LastKnownRemoteUpdateAt;
                using var execution = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var remaining = claimedAt.Value + ExecutionTimeout - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    throw new TimeoutException("The media provider operation execution deadline elapsed.");
                }
                execution.CancelAfter(remaining);
                mutationResult = await ExecuteOperationAsync(provider, operation, execution.Token);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                ApplyMutationFailure(operation, ex);
                var affected = await ActiveClaim(operationId, claimedAt.Value).ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.Status, operation.Status)
                    .SetProperty(item => item.AttemptCount, operation.AttemptCount)
                    .SetProperty(item => item.NextAttemptAt, operation.NextAttemptAt)
                    .SetProperty(item => item.LastError, operation.LastError)
                    .SetProperty(item => item.UpdatedAt, operation.UpdatedAt), cancellationToken);
                return affected == 0 ? Queued() : new MediaProviderOperationExecutionResult
                {
                    Outcome = operation.Status == MediaProviderOperationStatuses.Failed
                        ? MediaProviderOperationExecutionOutcome.Failed
                        : MediaProviderOperationExecutionOutcome.Queued,
                    LastError = operation.LastError
                };
            }

            if (mutationResult is null)
            {
                return CompletionResult(await ActiveClaim(operation).ExecuteDeleteAsync(cancellationToken) == 1);
            }

            return CompletionResult(autoProgress is not null
                ? await CompleteAutoProgressAsync(operation, autoProgress, mutationResult, cancellationToken)
                : await CompleteMutationAsync(operation, mutationResult, expectedRemoteUpdateAt, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await claim.ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, MediaProviderOperationStatuses.Retrying)
                .SetProperty(item => item.AttemptCount, item => item.AttemptCount - 1)
                .SetProperty(item => item.NextAttemptAt, (DateTimeOffset?)null)
                .SetProperty(item => item.UpdatedAt, DateTimeOffset.UtcNow), CancellationToken.None);
            throw;
        }
        finally
        {
            DetachTrackedOperation(operationId);
        }
    }

    private static MediaProviderOperationExecutionResult Queued() =>
        new() { Outcome = MediaProviderOperationExecutionOutcome.Queued };

    private sealed class BindingExecutionGate
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int References { get; set; }
    }

    private sealed class BindingExecutionLease : IDisposable
    {
        private readonly Guid _bindingId;
        private readonly BindingExecutionGate _gate;
        private bool _entered;

        public BindingExecutionLease(Guid bindingId)
        {
            _bindingId = bindingId;
            lock (BindingExecutionGates)
            {
                if (!BindingExecutionGates.TryGetValue(bindingId, out var gate))
                {
                    gate = new BindingExecutionGate();
                    BindingExecutionGates.Add(bindingId, gate);
                }
                _gate = gate;
                _gate.References++;
            }
        }

        public async Task EnterAsync(CancellationToken cancellationToken)
        {
            await _gate.Semaphore.WaitAsync(cancellationToken);
            _entered = true;
        }

        public void Dispose()
        {
            if (_entered)
            {
                _gate.Semaphore.Release();
            }
            lock (BindingExecutionGates)
            {
                if (--_gate.References == 0)
                {
                    BindingExecutionGates.Remove(_bindingId);
                    _gate.Semaphore.Dispose();
                }
            }
        }
    }

    private static MediaProviderOperationExecutionResult CompletionResult(bool completed) =>
        new()
        {
            Outcome = completed ? MediaProviderOperationExecutionOutcome.Succeeded
                : MediaProviderOperationExecutionOutcome.Queued
        };

    private IQueryable<MediaProviderOperation> OwnedClaim(Guid operationId, DateTimeOffset claimedAt) =>
        _dbContext.MediaProviderOperations.Where(item => item.Id == operationId
            && item.Status == MediaProviderOperationStatuses.Processing && item.UpdatedAt == claimedAt);

    private IQueryable<MediaProviderOperation> ActiveClaim(MediaProviderOperation operation) =>
        ActiveClaim(operation.Id, operation.UpdatedAt);

    private IQueryable<MediaProviderOperation> ActiveClaim(Guid operationId, DateTimeOffset claimedAt)
    {
        var cutoff = DateTimeOffset.UtcNow - ProcessingLeaseTimeout;
        return OwnedClaim(operationId, claimedAt).Where(item => item.UpdatedAt > cutoff);
    }

    private IQueryable<MediaProviderOperation> EligibleOperations(DateTimeOffset now)
    {
        var cutoff = now - ProcessingLeaseTimeout;
        return _dbContext.MediaProviderOperations.Where(item =>
                ((item.Status == MediaProviderOperationStatuses.Pending || item.Status == MediaProviderOperationStatuses.Retrying)
                    && (item.NextAttemptAt == null || item.NextAttemptAt <= now))
                || (item.Status == MediaProviderOperationStatuses.Processing && item.UpdatedAt <= cutoff))
            .Where(item => item.MediaLibraryProviderBinding!.ConnectedServiceAccount == null
                || item.MediaLibraryProviderBinding.ConnectedServiceAccount.ConnectionState != "reconnect_required")
            .Where(item => !_dbContext.MediaProviderOperations.Any(other =>
                other.MediaLibraryProviderBindingId == item.MediaLibraryProviderBindingId
                && other.Status == MediaProviderOperationStatuses.Processing && other.UpdatedAt > cutoff));
    }

    public async Task<int> ProcessDueOperationsAsync(CancellationToken cancellationToken)
    {
        var dueOperationIds = await EligibleOperations(DateTimeOffset.UtcNow)
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

    private Task<DateTimeOffset?> TryClaimOperationAsync(Guid operationId, CancellationToken cancellationToken)
    {
        // SQLite serializes writers for the single UPDATE. PostgreSQL needs a binding lock
        // before the UPDATE takes its statement snapshot, since claims can update different rows.
        return _dbContext.Database.IsNpgsql()
            ? _dbContext.Database.CreateExecutionStrategy().ExecuteAsync(ClaimOnPostgresAsync, cancellationToken)
            : ClaimAsync(cancellationToken);

        async Task<DateTimeOffset?> ClaimOnPostgresAsync(CancellationToken token)
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(token);
            var bindingId = await _dbContext.MediaProviderOperations.Where(item => item.Id == operationId)
                .Select(item => (Guid?)item.MediaLibraryProviderBindingId).SingleOrDefaultAsync(token);
            if (bindingId is null)
            {
                return null;
            }

            await LockBindingClaimsAsync(bindingId.Value, token);
            var claimedAt = await ClaimAsync(token);
            // Once the claim was written, finish the commit so the caller can release it on cancellation.
            await transaction.CommitAsync(CancellationToken.None);
            return claimedAt;
        }

        async Task<DateTimeOffset?> ClaimAsync(CancellationToken token)
        {
            // PostgreSQL stores microseconds. Keep an exact token and require it to
            // advance even when a cancelled attempt has restored the attempt count.
            var now = DateTimeOffset.UtcNow;
            var claimedAt = TruncateToMicroseconds(now);
            var count = await EligibleOperations(now)
                .Where(item => item.Id == operationId && (item.LastAttemptAt == null || item.LastAttemptAt < claimedAt))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.Status, MediaProviderOperationStatuses.Processing)
                    .SetProperty(item => item.LastAttemptAt, claimedAt)
                    .SetProperty(item => item.AttemptCount, item => item.AttemptCount + 1)
                    .SetProperty(item => item.UpdatedAt, claimedAt), token);
            return count == 1 ? claimedAt : null;
        }
    }

    private Task LockBindingClaimsAsync(Guid bindingId, CancellationToken cancellationToken)
    {
        var lockKey = $"media-provider-operation:{bindingId}";
        return _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);
    }

    private async Task<MediaProviderOperation> EnqueueAsync<TRequest>(
        int userId,
        MediaLibraryProviderBinding binding,
        string operationType,
        TRequest request,
        CancellationToken cancellationToken,
        bool deferSave = false)
    {
        var operation = new MediaProviderOperation
        {
            Id = Guid.NewGuid(),
            MediaLibraryProviderBindingId = binding.Id,
            OperationType = operationType,
            PayloadJson = JsonSerializer.Serialize(request, SerializerOptions),
            Status = MediaProviderOperationStatuses.Pending,
            AttemptCount = 0,
            NextAttemptAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.MediaProviderOperations.Add(operation);
        if (!deferSave)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        return operation;
    }

    private async Task<MediaProviderMutationResult?> ExecuteOperationAsync(
        IMediaProvider provider,
        MediaProviderOperation operation,
        CancellationToken cancellationToken)
    {
        if (operation.MediaLibraryProviderBinding?.MediaLibraryEntry is null
            || operation.MediaLibraryProviderBinding.MediaProviderLink is null)
        {
            throw new InvalidOperationException("Provider operation is missing a media library entry.");
        }

        return operation.OperationType switch
        {
            MediaProviderOperationTypes.UpdateProgress or MediaProviderOperationTypes.ImportFanOutProgress => await ExecuteProgressUpdateAsync(
                provider,
                operation,
                cancellationToken),
            MediaProviderOperationTypes.UpdateStatus or MediaProviderOperationTypes.ImportFanOutStatus => await DispatchAsync(operation,
                () => provider.UpdateStatusAsync(operation.MediaLibraryProviderBinding.MediaLibraryEntry.UserId,
                    DeserializePayload<MediaStatusUpdateRequest>(operation.PayloadJson), cancellationToken), cancellationToken),
            MediaProviderOperationTypes.UpdateScore or MediaProviderOperationTypes.ImportFanOutScore => await DispatchAsync(operation,
                () => provider.UpdateScoreAsync(operation.MediaLibraryProviderBinding.MediaLibraryEntry.UserId,
                    DeserializePayload<MediaScoreUpdateRequest>(operation.PayloadJson), cancellationToken), cancellationToken),
            MediaProviderOperationTypes.SyncLibraryState => await ExecuteLibraryStateSyncAsync(
                provider, operation, cancellationToken),
            MediaProviderOperationTypes.AutoProgressUpdate => await ExecuteAutoProgressAsync(
                provider,
                operation,
                cancellationToken),
            _ => throw new InvalidOperationException($"Provider operation type '{operation.OperationType}' is not supported.")
        };
    }

    private async Task<MediaProviderMutationResult?> DispatchAsync(MediaProviderOperation operation,
        Func<Task<MediaProviderMutationResult>> dispatch, CancellationToken cancellationToken,
        AutoProgressUpdatePayload? autoProgress = null)
    {
        using var bindingGate = new BindingExecutionLease(operation.MediaLibraryProviderBindingId);
        await bindingGate.EnterAsync(cancellationToken);
        var binding = operation.MediaLibraryProviderBinding!;
        await _dbContext.Entry(binding).ReloadAsync(cancellationToken);
        await _dbContext.Entry(binding.MediaLibraryEntry!).ReloadAsync(cancellationToken);
        if (!await ActiveClaim(operation).AnyAsync(cancellationToken)
            || DateTimeOffset.UtcNow >= operation.UpdatedAt + ProcessingLeaseTimeout)
        {
            throw new InvalidOperationException("The media provider operation lease was lost.");
        }
        if (DateTimeOffset.UtcNow >= operation.UpdatedAt + ExecutionTimeout)
        {
            throw new TimeoutException("The media provider operation execution deadline elapsed.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (ShouldSkipStaleOperation(operation)
            || (autoProgress is not null && IsAutoProgressRemoteVersionStale(autoProgress, binding.LastRemoteUpdateAt)))
        {
            return null;
        }
        return await dispatch();
    }

    private static TRequest DeserializePayload<TRequest>(string payloadJson)
    {
        return JsonSerializer.Deserialize<TRequest>(payloadJson, SerializerOptions)
            ?? throw new InvalidOperationException("Provider operation payload could not be deserialized.");
    }

    private static bool ShouldSkipStaleOperation(MediaProviderOperation operation)
    {
        var entry = operation.MediaLibraryProviderBinding?.MediaLibraryEntry;
        if (entry is null)
        {
            return false;
        }

        var isImportFanOut = operation.OperationType is
            MediaProviderOperationTypes.ImportFanOutStatus or
            MediaProviderOperationTypes.ImportFanOutScore or
            MediaProviderOperationTypes.ImportFanOutProgress;
        var isNewerLocalEdit = entry.LastLocalEditAt > operation.CreatedAt;
        if (operation.OperationType == MediaProviderOperationTypes.AutoProgressUpdate)
        {
            var progress = DeserializePayload<AutoProgressUpdatePayload>(operation.PayloadJson);
            return (progress.LocalProgressCommitted || isNewerLocalEdit) && AutoProgressDoesNotMatch(entry, progress);
        }

        if (!isImportFanOut && !isNewerLocalEdit
            && operation.OperationType != MediaProviderOperationTypes.SyncLibraryState)
        {
            return false;
        }

        return operation.OperationType switch
        {
            MediaProviderOperationTypes.UpdateStatus or MediaProviderOperationTypes.ImportFanOutStatus
                => DeserializePayload<MediaStatusUpdateRequest>(operation.PayloadJson).Status != entry.Status,
            MediaProviderOperationTypes.UpdateScore or MediaProviderOperationTypes.ImportFanOutScore
                => DeserializePayload<MediaScoreUpdateRequest>(operation.PayloadJson).Score != entry.Score,
            MediaProviderOperationTypes.UpdateProgress or MediaProviderOperationTypes.ImportFanOutProgress
                => ProgressDoesNotMatch(entry,
                    DeserializePayload<MediaProgressUpdateRequest>(operation.PayloadJson), isImportFanOut),
            MediaProviderOperationTypes.SyncLibraryState => operation.BatchId is null || SyncStateDoesNotMatch(
                entry, DeserializePayload<MediaLibraryStateSyncRequest>(operation.PayloadJson)),
            _ => false
        };
    }

    private static bool AutoProgressDoesNotMatch(MediaLibraryEntry entry, AutoProgressUpdatePayload payload) =>
        (payload.ProgressEpisodes.HasValue && payload.ProgressEpisodes != entry.ProgressEpisodes)
        || (payload.ProgressChapters.HasValue && payload.ProgressChapters != entry.ProgressChapters)
        || (payload.ProgressVolumes.HasValue && payload.ProgressVolumes != entry.ProgressVolumes);

    private static bool IsAutoProgressRemoteVersionStale(AutoProgressUpdatePayload payload, DateTimeOffset? currentRemoteUpdateAt) =>
        (payload.LocalProgressCommitted || payload.LastKnownRemoteUpdateAt.HasValue)
        && currentRemoteUpdateAt.HasValue
        && (payload.LastKnownRemoteUpdateAt is null || currentRemoteUpdateAt > payload.LastKnownRemoteUpdateAt);

    private static bool ProgressDoesNotMatch(
        MediaLibraryEntry entry, MediaProgressUpdateRequest request, bool compareNulls)
        => (compareNulls || request.ProgressEpisodes.HasValue) && request.ProgressEpisodes != entry.ProgressEpisodes
            || (compareNulls || request.ProgressChapters.HasValue) && request.ProgressChapters != entry.ProgressChapters
            || (compareNulls || request.ProgressVolumes.HasValue) && request.ProgressVolumes != entry.ProgressVolumes;

    private static bool SyncStateDoesNotMatch(MediaLibraryEntry entry, MediaLibraryStateSyncRequest request)
        => request.UpdateStatus && request.Status != entry.Status
            || request.UpdateScore && request.Score != entry.Score
            || request.UpdateProgress && (
                request.ProgressEpisodes is { } episodes && episodes != (entry.ProgressEpisodes ?? 0)
                || request.ProgressChapters is { } chapters && chapters != (entry.ProgressChapters ?? 0)
                || request.ProgressVolumes is { } volumes && volumes != (entry.ProgressVolumes ?? 0));

    private async Task<MediaProviderMutationResult?> ExecuteLibraryStateSyncAsync(
        IMediaProvider provider, MediaProviderOperation operation, CancellationToken cancellationToken)
    {
        var binding = operation.MediaLibraryProviderBinding!;
        var userId = binding.MediaLibraryEntry!.UserId;
        var account = await provider.GetConnectedAccountAsync(userId, cancellationToken);
        var request = DeserializePayload<MediaLibraryStateSyncRequest>(operation.PayloadJson);
        var sourceVerified = request.VerifiedSourceBindingId is { } sourceBindingId && request.VerifiedSourceVersion.HasValue
            && await _dbContext.MediaLibraryProviderBindings.AnyAsync(candidate =>
                candidate.Id == sourceBindingId && candidate.MediaLibraryEntryId == binding.MediaLibraryEntryId
                && candidate.ConnectedServiceAccountId.HasValue
                && candidate.LastRemoteUpdateAt == request.VerifiedSourceVersion, cancellationToken);

        request.SaveCheckpointAsync = async token =>
        {
            var payload = JsonSerializer.Serialize(request, SerializerOptions);
            var affected = await ActiveClaim(operation)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.PayloadJson, payload), token);
            if (affected == 0)
            {
                throw new InvalidOperationException("The media provider operation lease was lost.");
            }
            operation.PayloadJson = payload;
            _dbContext.Entry(operation).Property(item => item.PayloadJson).OriginalValue = payload;
        };
        return await DispatchAsync(operation, () =>
        {
            if (account is null || account.Id != binding.ConnectedServiceAccountId
                || account.ExternalAccountId != binding.ProviderAccountId)
            {
                throw new PlatformApiException(
                    "platform_account_changed", "The connected account changed. Create a new sync preview.", 409);
            }

            var entry = binding.MediaLibraryEntry;
            var sourceVersion = request.VerifiedSourceBindingId.HasValue ? null : entry.LastLocalEditAt;
            if (sourceVerified && entry.LastMutationSource == MediaMutationSources.ProviderImport)
            {
                sourceVersion = request.VerifiedSourceVersion;
            }
            if (request.LastKnownRemoteUpdateAt is { } expectedVersion
                ? binding.LastRemoteUpdateAt > expectedVersion
                    || !MediaLibraryStateSyncRequest.IsNewerVersion(sourceVersion, expectedVersion)
                : binding.LastRemoteUpdateAt.HasValue || binding.LastSyncedAt.HasValue || !request.ExpectMissingRemote)
            {
                throw new PlatformApiException(
                    "platform_sync_conflict", "The provider entry changed. Create a new sync preview.", 409);
            }

            return provider.SyncLibraryStateAsync(userId, request, cancellationToken);
        }, cancellationToken);
    }

    private async Task<MediaProviderMutationResult?> ExecuteProgressUpdateAsync(
        IMediaProvider provider,
        MediaProviderOperation operation,
        CancellationToken cancellationToken)
    {
        var request = DeserializePayload<MediaProgressUpdateRequest>(operation.PayloadJson);
        LogProviderProgressUpdateAttempt(operation, request);
        return await DispatchAsync(operation, () => provider.UpdateProgressAsync(
            operation.MediaLibraryProviderBinding!.MediaLibraryEntry!.UserId,
            request,
            cancellationToken), cancellationToken);
    }

    private async Task<MediaProviderMutationResult?> ExecuteAutoProgressAsync(
        IMediaProvider provider,
        MediaProviderOperation operation,
        CancellationToken cancellationToken)
    {
        var payload = DeserializePayload<AutoProgressUpdatePayload>(operation.PayloadJson);

        // Sync-metadata guard: re-read the current LastRemoteUpdateAt from the DB.
        // If the provider has written a newer timestamp since we enqueued this
        // operation, the entry may already be ahead and we should skip the write.
        if (operation.MediaLibraryProviderBinding is not null
            && (payload.LocalProgressCommitted || payload.LastKnownRemoteUpdateAt.HasValue))
        {
            var currentRemoteUpdateAt = await _dbContext.MediaLibraryProviderBindings
                .AsNoTracking()
                .Where(binding => binding.Id == operation.MediaLibraryProviderBindingId)
                .Select(binding => binding.LastRemoteUpdateAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (IsAutoProgressRemoteVersionStale(payload, currentRemoteUpdateAt))
            {
                _logger.LogInformation(
                    "Auto-progress skipped for operation {OperationId}: " +
                    "provider has newer remote state (current {RemoteAt}, expected {KnownAt}).",
                    operation.Id,
                    currentRemoteUpdateAt,
                    payload.LastKnownRemoteUpdateAt);

                return null;
            }
        }

        var request = new MediaProgressUpdateRequest
        {
            ProviderMediaId = payload.ProviderMediaId,
            ProgressEpisodes = payload.ProgressEpisodes,
            ProgressChapters = payload.ProgressChapters,
            ProgressVolumes = payload.ProgressVolumes,
            LastKnownRemoteUpdateAt = payload.LastKnownRemoteUpdateAt
        };
        LogProviderProgressUpdateAttempt(operation, request);
        return await DispatchAsync(operation, () => provider.UpdateProgressAsync(
            operation.MediaLibraryProviderBinding!.MediaLibraryEntry!.UserId,
            request,
            cancellationToken), cancellationToken, payload);
    }

    private Task<bool> CompleteMutationAsync(MediaProviderOperation operation,
        MediaProviderMutationResult result, DateTimeOffset? expectedRemoteUpdateAt, CancellationToken cancellationToken) =>
        MediaProviderSeasonMappingService.WithWriteAsync(_dbContext, async () =>
        {
            if (_dbContext.Database.IsNpgsql())
            {
                await LockBindingClaimsAsync(operation.MediaLibraryProviderBindingId, cancellationToken);
                await LockBindingRowAsync(operation.MediaLibraryProviderBindingId, cancellationToken);
            }
            if (await ActiveClaim(operation).ExecuteDeleteAsync(cancellationToken) == 0)
            {
                return false;
            }

            await _dbContext.Entry(operation.MediaLibraryProviderBinding!).ReloadAsync(cancellationToken);
            await ApplyMutationSuccessAsync(operation, result, expectedRemoteUpdateAt, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);

    private Task<bool> CompleteAutoProgressAsync(MediaProviderOperation operation, AutoProgressUpdatePayload payload,
        MediaProviderMutationResult result, CancellationToken cancellationToken) =>
        MediaProviderSeasonMappingService.WithWriteAsync(_dbContext, async () =>
        {
            var binding = operation.MediaLibraryProviderBinding!;
            var entry = binding.MediaLibraryEntry!;
            if (_dbContext.Database.IsNpgsql())
            {
                await LockBindingClaimsAsync(binding.Id, cancellationToken);
                if (!payload.LocalProgressCommitted)
                {
                    await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                        SELECT "Id" FROM "MediaLibraryEntries" WHERE "Id" = {entry.Id} FOR UPDATE
                        """, cancellationToken);
                }
                await LockBindingRowAsync(binding.Id, cancellationToken);
            }
            if (await ActiveClaim(operation).ExecuteDeleteAsync(cancellationToken) == 0)
            {
                return false;
            }

            await _dbContext.Entry(binding).ReloadAsync(cancellationToken);
            if (!payload.LocalProgressCommitted)
            {
                await _dbContext.Entry(entry).ReloadAsync(cancellationToken);
                ApplyLegacyAutoProgress(entry, payload);
            }
            await ApplyMutationSuccessAsync(operation, result, payload.LastKnownRemoteUpdateAt, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);

    private Task LockBindingRowAsync(Guid bindingId, CancellationToken cancellationToken) =>
        _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT "Id" FROM "MediaLibraryProviderBindings" WHERE "Id" = {bindingId} FOR UPDATE
            """, cancellationToken);

    private void LogProviderProgressUpdateAttempt(
        MediaProviderOperation operation,
        MediaProgressUpdateRequest request)
    {
        _logger.LogInformation(
            "Attempting provider progress update for operation {OperationId}: " +
            "provider={Provider}, entry={EntryId}, episodes={Episodes}, chapters={Chapters}, volumes={Volumes}.",
            operation.Id,
            operation.MediaLibraryProviderBinding?.MediaProviderLink?.Provider,
            operation.MediaLibraryProviderBinding?.MediaLibraryEntryId,
            request.ProgressEpisodes,
            request.ProgressChapters,
            request.ProgressVolumes);
    }

    private static void ApplyLegacyAutoProgress(MediaLibraryEntry entry, AutoProgressUpdatePayload payload)
    {
        var current = (entry.ProgressEpisodes, entry.ProgressChapters, entry.ProgressVolumes);
        var advanced = (Advance(current.ProgressEpisodes, payload.ProgressEpisodes),
            Advance(current.ProgressChapters, payload.ProgressChapters),
            Advance(current.ProgressVolumes, payload.ProgressVolumes));
        if (advanced == current)
        {
            return;
        }

        (entry.ProgressEpisodes, entry.ProgressChapters, entry.ProgressVolumes) = advanced;
        entry.LastMutationSource = MediaMutationSources.ObservationAutoProgress;
        entry.LastLocalEditAt = entry.UpdatedAt = DateTimeOffset.UtcNow;

        static int? Advance(int? current, int? requested) => requested > (current ?? 0) ? requested : current;
    }

    private async Task ApplyMutationSuccessAsync(MediaProviderOperation operation, MediaProviderMutationResult result,
        DateTimeOffset? expectedRemoteUpdateAt, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var binding = operation.MediaLibraryProviderBinding
            ?? throw new InvalidOperationException("Provider operation is missing a provider binding.");
        var entry = binding.MediaLibraryEntry
            ?? throw new InvalidOperationException("Provider binding is missing a media library entry.");

        var librarySyncRequest = operation.OperationType == MediaProviderOperationTypes.SyncLibraryState
            ? DeserializePayload<MediaLibraryStateSyncRequest>(operation.PayloadJson)
            : null;
        if (result.AppliedStatus is not null && librarySyncRequest?.UpdateStatus != false)
        {
            binding.LastRequestedStatus = operation.OperationType switch
            {
                MediaProviderOperationTypes.UpdateStatus => DeserializePayload<MediaStatusUpdateRequest>(operation.PayloadJson).Status,
                MediaProviderOperationTypes.ImportFanOutStatus => DeserializePayload<MediaStatusUpdateRequest>(operation.PayloadJson).Status,
                MediaProviderOperationTypes.SyncLibraryState => librarySyncRequest!.Status,
                _ => null
            };
            binding.LastAppliedStatus = result.AppliedStatus;
        }
        binding.LastSyncedAt = now;
        var remoteUpdateAt = RemoteVersionAtStoragePrecision(
            result.LastRemoteUpdateAt ?? (librarySyncRequest is null ? now : null));
        if (remoteUpdateAt.HasValue && (binding.LastRemoteUpdateAt is null || remoteUpdateAt > binding.LastRemoteUpdateAt))
        {
            var previousRemoteUpdateAt = binding.LastRemoteUpdateAt;
            binding.LastRemoteUpdateAt = remoteUpdateAt;
            // Carry this acknowledgement forward only if the binding still has the version this write started from.
            if (previousRemoteUpdateAt == RemoteVersionAtStoragePrecision(expectedRemoteUpdateAt))
            {
                var preservesRemoteProgress = operation.OperationType is
                    MediaProviderOperationTypes.UpdateStatus or MediaProviderOperationTypes.UpdateScore or
                    MediaProviderOperationTypes.ImportFanOutStatus or MediaProviderOperationTypes.ImportFanOutScore
                    || librarySyncRequest?.UpdateProgress == false;
                var leaseCutoff = now - ProcessingLeaseTimeout;
                var queued = await _dbContext.MediaProviderOperations.Where(item =>
                    item.Id != operation.Id && item.MediaLibraryProviderBindingId == binding.Id &&
                    item.OperationType == MediaProviderOperationTypes.AutoProgressUpdate &&
                    (item.Status == MediaProviderOperationStatuses.Pending ||
                        item.Status == MediaProviderOperationStatuses.Retrying ||
                        (item.Status == MediaProviderOperationStatuses.Processing && item.UpdatedAt <= leaseCutoff)))
                    .ToListAsync(cancellationToken);
                if (preservesRemoteProgress && queued.Count > 0)
                {
                    await _dbContext.Entry(entry).ReloadAsync(cancellationToken);
                }

                foreach (var next in queued.Where(item => preservesRemoteProgress || item.CreatedAt > operation.CreatedAt))
                {
                    var nextPayload = DeserializePayload<AutoProgressUpdatePayload>(next.PayloadJson);
                    if (nextPayload.LocalProgressCommitted
                        && RemoteVersionAtStoragePrecision(nextPayload.LastKnownRemoteUpdateAt) == previousRemoteUpdateAt
                        && (!preservesRemoteProgress || !AutoProgressDoesNotMatch(entry, nextPayload)))
                    {
                        nextPayload.LastKnownRemoteUpdateAt = remoteUpdateAt;
                        next.PayloadJson = JsonSerializer.Serialize(nextPayload, SerializerOptions);
                    }
                }
            }
        }
        binding.UpdatedAt = now;
        _logger.LogInformation(
            "Media provider operation {OperationId} ({OperationType}) succeeded for user {UserId} on provider {Provider}.",
            operation.Id,
            operation.OperationType,
            entry.UserId,
            binding.MediaProviderLink?.Provider);
    }

    // A queued JSON payload can retain the original tick precision of a tracked binding.
    private DateTimeOffset? RemoteVersionAtStoragePrecision(DateTimeOffset? version) =>
        version is { } timestamp && _dbContext.Database.IsNpgsql() ? TruncateToMicroseconds(timestamp) : version;

    private static DateTimeOffset TruncateToMicroseconds(DateTimeOffset timestamp) =>
        timestamp.AddTicks(-(timestamp.Ticks % 10));

    private void ApplyMutationFailure(MediaProviderOperation operation, Exception exception)
    {
        var now = DateTimeOffset.UtcNow;
        if (exception is PlatformReconnectRequiredException)
        {
            operation.Status = MediaProviderOperationStatuses.Retrying;
            operation.AttemptCount = Math.Max(0, operation.AttemptCount - 1);
            operation.NextAttemptAt = null;
            operation.LastError = "Reconnect the provider to resume.";
            operation.UpdatedAt = now;
            return;
        }
        var maxAttempts = 4;
        var shouldRetry = operation.AttemptCount < maxAttempts
            && exception is not PlatformApiException { Code: "platform_sync_conflict" or "platform_account_changed" };

        operation.Status = shouldRetry
            ? MediaProviderOperationStatuses.Retrying
            : MediaProviderOperationStatuses.Failed;
        operation.NextAttemptAt = shouldRetry
            ? now.Add(GetRetryDelay(operation.AttemptCount, exception))
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

    private static TimeSpan GetRetryDelay(int attemptCount, Exception exception)
    {
        var scheduledDelay = attemptCount switch
        {
            1 => TimeSpan.FromMinutes(1),
            2 => TimeSpan.FromMinutes(5),
            3 => TimeSpan.FromMinutes(15),
            _ => TimeSpan.FromMinutes(30)
        };

        var requestedDelay = exception switch
        {
            AniListRequestException { RetryAfter: { } retryAfter } => retryAfter,
            MyAnimeListRequestException { RetryAfter: { } retryAfter } => retryAfter,
            SimklRequestException { RetryAfter: { } retryAfter } => retryAfter,
            _ => TimeSpan.Zero
        };

        return requestedDelay > scheduledDelay ? requestedDelay : scheduledDelay;
    }
}
