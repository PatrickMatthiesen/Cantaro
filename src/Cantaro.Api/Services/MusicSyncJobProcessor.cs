using System.Text.Json;
using Cantaro.Api.Controllers;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

public sealed class MusicSyncJobProcessor(
    ApplicationDbContext dbContext,
    IPlatformRegistry platformRegistry,
    MusicSyncThrottleService throttleService,
    ILogger<MusicSyncJobProcessor> logger)
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly IPlatformRegistry _platformRegistry = platformRegistry;
    private readonly MusicSyncThrottleService _throttleService = throttleService;
    private readonly ILogger<MusicSyncJobProcessor> _logger = logger;

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var job = await _dbContext.MusicSyncJobs
            .Where(item => item.Status == MusicSyncJobStatuses.Queued || item.Status == MusicSyncJobStatuses.Running)
            .OrderBy(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (job is null) return false;

        await ProcessAsync(job, cancellationToken);
        return true;
    }

    internal async Task ProcessAsync(MusicSyncJob job, CancellationToken cancellationToken)
    {
        var jobId = job.Id;
        var playlists = JsonSerializer.Deserialize<List<MusicSyncJobPlaylist>>(job.PlaylistsJson) ?? [];
        var results = string.IsNullOrWhiteSpace(job.ResultsJson)
            ? []
            : JsonSerializer.Deserialize<List<BatchSyncResult>>(job.ResultsJson) ?? [];

        job.Status = MusicSyncJobStatuses.Running;
        job.StartedAt ??= DateTimeOffset.UtcNow;
        job.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var platform = _platformRegistry.GetRequired(job.Service);
            if (job.ConnectedServiceAccountId is not { } connectedServiceAccountId)
            {
                throw new InvalidOperationException("Music sync job has no connected account target.");
            }
            var account = new PlatformAccountContext(job.UserId, connectedServiceAccountId);
            foreach (var playlist in playlists.Skip(job.ProcessedPlaylistCount))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var processedBeforePlaylist = job.ProcessedSongCount;
                var lastPersistedSongCount = -1;
                string? lastPersistedSongName = null;
                var lastProgressWriteAt = DateTimeOffset.MinValue;
                job.CurrentPlaylistName = playlist.Name;
                job.CurrentSongName = null;
                job.UpdatedAt = DateTimeOffset.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);

                BatchSyncResult result;
                try
                {
                    var cantaroPlaylistId = await platform.SyncPlaylistAsync(
                        account,
                        playlist.Id,
                        async (progress, progressCancellationToken) =>
                        {
                            var boundedCount = Math.Clamp(progress.ProcessedSongCount, 0, playlist.SongCount);
                            if (boundedCount == lastPersistedSongCount
                                && string.Equals(
                                    progress.CurrentSongName,
                                    lastPersistedSongName,
                                    StringComparison.Ordinal))
                            {
                                return;
                            }

                            var now = DateTimeOffset.UtcNow;
                            var isFinalUpdate = boundedCount == playlist.SongCount
                                && progress.CurrentSongName == null;
                            if (!isFinalUpdate && now - lastProgressWriteAt < TimeSpan.FromSeconds(1))
                            {
                                return;
                            }

                            job.ProcessedSongCount = Math.Min(
                                job.SongCount,
                                processedBeforePlaylist + boundedCount);
                            job.CurrentSongName = progress.CurrentSongName;
                            job.UpdatedAt = now;
                            lastPersistedSongCount = boundedCount;
                            lastPersistedSongName = progress.CurrentSongName;
                            lastProgressWriteAt = now;
                            await _dbContext.SaveChangesAsync(progressCancellationToken);
                        },
                        cancellationToken);
                    result = new BatchSyncResult
                    {
                        ServicePlaylistId = playlist.Id,
                        PlaylistName = playlist.Name,
                        Success = true,
                        CantaroPlaylistId = cantaroPlaylistId.ToString()
                    };
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Music sync job {JobId} failed playlist {PlaylistId}", jobId, playlist.Id);
                    var failure = MusicSyncFailureClassifier.Classify(ex);
                    result = new BatchSyncResult
                    {
                        ServicePlaylistId = playlist.Id,
                        PlaylistName = playlist.Name,
                        Success = false,
                        Error = failure.Message,
                        ErrorCode = failure.Code,
                        Retryable = failure.Retryable
                    };
                }

                // Platform sync uses this scoped DbContext and may roll back its own
                // transaction. Clear all tracked platform entities before persisting
                // job progress so rolled-back state cannot leak into the next playlist.
                job = await ReloadJobAsync(jobId, cancellationToken);
                results.Add(result);
                if (result.Success)
                {
                    job.SuccessCount++;
                }
                else
                {
                    job.FailureCount++;
                }
                job.ProcessedPlaylistCount++;
                job.ProcessedSongCount = Math.Min(
                    job.SongCount,
                    Math.Max(job.ProcessedSongCount, processedBeforePlaylist + playlist.SongCount));
                job.CurrentSongName = null;
                job.ResultsJson = JsonSerializer.Serialize(results);
                job.UpdatedAt = DateTimeOffset.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            job.Status = job.FailureCount == 0 ? MusicSyncJobStatuses.Completed : MusicSyncJobStatuses.Failed;
            job.ErrorMessage = job.FailureCount == 0 ? null : $"{job.FailureCount} playlist sync failed.";
            job.CurrentPlaylistName = null;
            job.CurrentSongName = null;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.UpdatedAt = job.CompletedAt.Value;
            _throttleService.AddUsage(job.UserId, job.Service, job.EstimatedNewSongCount, job.CompletedAt.Value);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Music sync job {JobId} completed: {SuccessCount} succeeded, {FailureCount} failed",
                jobId,
                job.SuccessCount,
                job.FailureCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            job = await ReloadJobAsync(jobId, CancellationToken.None);
            job.Status = MusicSyncJobStatuses.Queued;
            job.CurrentPlaylistName = null;
            job.CurrentSongName = null;
            job.UpdatedAt = DateTimeOffset.UtcNow;
            await _dbContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            job = await ReloadJobAsync(jobId, CancellationToken.None);
            job.Status = MusicSyncJobStatuses.Failed;
            job.ErrorMessage = MusicSyncFailureClassifier.Classify(ex).Message;
            job.CurrentPlaylistName = null;
            job.CurrentSongName = null;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.UpdatedAt = job.CompletedAt.Value;
            await _dbContext.SaveChangesAsync(CancellationToken.None);
            _logger.LogError(ex, "Music sync job {JobId} failed", jobId);
        }
    }

    private async Task<MusicSyncJob> ReloadJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.MusicSyncJobs.SingleAsync(item => item.Id == jobId, cancellationToken);
    }
}
