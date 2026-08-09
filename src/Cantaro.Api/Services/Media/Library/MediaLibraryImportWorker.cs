using Cantaro.Api.Models;

namespace Cantaro.Api.Services;

public sealed class MediaLibraryImportWorker(
    MediaLibraryImportQueue importQueue,
    IServiceScopeFactory serviceScopeFactory,
    ILogger<MediaLibraryImportWorker> logger) : BackgroundService
{
    private readonly MediaLibraryImportQueue _importQueue = importQueue;
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;
    private readonly ILogger<MediaLibraryImportWorker> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var workItem in _importQueue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(workItem, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Media library import worker failed for {ProviderId} import {ImportId}.", workItem.ProviderId, workItem.ImportId);
                PublishFailure(workItem, ex.Message);
            }
            finally
            {
                _importQueue.Complete(workItem);
            }
        }
    }

    private async Task ProcessAsync(MediaLibraryImportWorkItem workItem, CancellationToken cancellationToken)
    {
        Publish(workItem, "running");

        using var scope = _serviceScopeFactory.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IMediaProviderRegistry>();
        var importService = scope.ServiceProvider.GetRequiredService<MediaLibraryImportService>();
        var availabilitySyncService = scope.ServiceProvider.GetRequiredService<AnimeScheduleAvailabilitySyncService>();

        var provider = registry.GetRequired(workItem.ProviderId);
        var account = await provider.GetConnectedAccountAsync(workItem.UserId, cancellationToken)
            ?? throw new InvalidOperationException($"{provider.ProviderId} is not connected.");

        var importResult = await provider.ImportLibraryAsync(workItem.UserId, cancellationToken);
        var persisted = await importService.ImportAsync(workItem.UserId, account, importResult, cancellationToken);
        try
        {
            await availabilitySyncService.SyncUserLibraryAsync(workItem.UserId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                exception,
                "AnimeSchedule availability refresh failed after {ProviderId} import for user {UserId}.",
                workItem.ProviderId,
                workItem.UserId);
        }

        Publish(workItem, "completed", new MediaLibraryImportEventDto
        {
            ProviderId = persisted.ProviderId,
            ImportId = workItem.ImportId,
            Status = "completed",
            ImportedCount = persisted.ImportedCount,
            CreatedTitles = persisted.CreatedTitles,
            CreatedEntries = persisted.CreatedEntries,
            UpdatedEntries = persisted.UpdatedEntries,
            LibraryChanged = persisted.CreatedTitles > 0 || persisted.CreatedEntries > 0 || persisted.UpdatedEntries > 0,
            OccurredAt = persisted.ImportedAt
        });
    }

    private void PublishFailure(MediaLibraryImportWorkItem workItem, string errorMessage)
    {
        Publish(workItem, "failed", new MediaLibraryImportEventDto
        {
            ProviderId = workItem.ProviderId,
            ImportId = workItem.ImportId,
            Status = "failed",
            ErrorMessage = errorMessage,
            OccurredAt = DateTimeOffset.UtcNow
        });
    }

    private void Publish(MediaLibraryImportWorkItem workItem, string status, MediaLibraryImportEventDto? importEvent = null)
    {
        _importQueue.Publish(workItem.UserId, importEvent ?? new MediaLibraryImportEventDto
        {
            ProviderId = workItem.ProviderId,
            ImportId = workItem.ImportId,
            Status = status,
            OccurredAt = DateTimeOffset.UtcNow
        });
    }
}
