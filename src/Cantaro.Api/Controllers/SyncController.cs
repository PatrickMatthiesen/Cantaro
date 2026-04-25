using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace Cantaro.Api.Controllers;

/// <summary>
/// Response model for sync status
/// </summary>
public class SyncStatusResponse
{
    public required SyncStatusInfo Overall { get; set; }
    public required List<PlaylistSyncInfo> Playlists { get; set; }
}

public class SyncStatusInfo
{
    public DateTimeOffset? LastSyncedAt { get; set; }
    public bool NeedsAutoSync { get; set; }
    public bool CanSyncNow { get; set; }
    public int SongsSyncedInWindow { get; set; }
    public int RemainingSongsInWindow { get; set; }
    public int SongSyncLimit { get; set; }
    public int WindowMinutes { get; set; }
    public string? Message { get; set; }
}

public class PlaylistSyncInfo
{
    public required string PlaylistId { get; set; }
    public required string Name { get; set; }
    public required string Service { get; set; }
    public required string ServicePlaylistId { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
    public string? LastSyncStatus { get; set; }
}

/// <summary>
/// Request model for batch sync
/// </summary>
public class BatchSyncRequest
{
    public required string Service { get; set; }
    public List<string>? ServicePlaylistIds { get; set; } // null = sync all
}

/// <summary>
/// Response model for batch sync
/// </summary>
public class BatchSyncResponse
{
    public required List<BatchSyncResult> Results { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public int SongsRequested { get; set; }
    public int SongsSynced { get; set; }
}

public class BatchSyncResult
{
    public required string ServicePlaylistId { get; set; }
    public required string PlaylistName { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? CantaroPlaylistId { get; set; }
}

[ApiController]
[Route("api/sync")]
[Authorize]
public class SyncController : ControllerBase
{
    private sealed class SyncUsageBucket
    {
        public object SyncRoot { get; } = new();
        public List<SyncUsageEvent> Events { get; } = [];
    }

    private sealed class SyncUsageEvent
    {
        public DateTimeOffset OccurredAt { get; init; }
        public int SongsSynced { get; init; }
    }

    private sealed class SyncThrottleStatus
    {
        public int Usage { get; init; }
        public int Remaining { get; init; }
        public bool CanSyncNow { get; init; }
        public string? Message { get; init; }
    }

    private const int SongSyncLimitPerWindow = 2000;
    private const int SyncWindowMinutes = 10;
    // Cap how many songs from a single sync run are counted towards usage to avoid one very large run
    // overwhelming the rolling window accounting. This is set to 2x SongSyncLimitPerWindow (2 * 2000)
    // so that, even in edge cases (e.g. large initial sync or retries), a single run cannot contribute
    // more than twice the allowed per-window usage.
    private const int MaxSongsCountedPerSyncRun = SongSyncLimitPerWindow * 2;
    private static readonly ConcurrentDictionary<string, SyncUsageBucket> _syncUsageByUserAndService = new();

    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<User> _userManager;
    private readonly IPlatformRegistry _platformRegistry;
    private readonly ILogger<SyncController> _logger;

    public SyncController(
        ApplicationDbContext dbContext,
        UserManager<User> userManager,
        IPlatformRegistry platformRegistry,
        ILogger<SyncController> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _platformRegistry = platformRegistry;
        _logger = logger;
    }

    /// <summary>
    /// Get sync status for all playlists
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult<SyncStatusResponse>> GetSyncStatus([FromQuery] string? service, CancellationToken cancellationToken)
    {
        try
        {
            var userId = await GetCurrentUserIdAsync();

            // Get all synced playlists for this user
            var mappings = await _dbContext.ServicePlaylistMappings
                .Include(m => m.Playlist)
                .Where(m => m.Playlist!.UserId == userId)
                .OrderByDescending(m => m.LastSyncedAt)
                .ToListAsync(cancellationToken);

            var playlistInfos = mappings.Select(m => new PlaylistSyncInfo
            {
                PlaylistId = m.PlaylistId.ToString(),
                Name = m.Playlist?.Name ?? "Unknown",
                Service = m.Service,
                ServicePlaylistId = m.ServicePlaylistId,
                LastSyncedAt = m.LastSyncedAt,
                LastSyncStatus = m.LastSyncStatus
            }).ToList();

            // Calculate overall status
            var lastSync = mappings.MaxBy(m => m.LastSyncedAt)?.LastSyncedAt;
            var now = DateTimeOffset.UtcNow;
            var resolvedService = ResolveServiceForStatus(service, mappings.Select(m => m.Service));
            var throttleStatus = resolvedService is null
                ? new SyncThrottleStatus
                {
                    Usage = 0,
                    Remaining = SongSyncLimitPerWindow,
                    CanSyncNow = false,
                    Message = "No supported platform is available for sync."
                }
                : BuildSyncThrottleStatus(userId, resolvedService, now);
            bool needsAutoSync = lastSync == null || (now - lastSync.Value).TotalHours >= 24;

            return Ok(new SyncStatusResponse
            {
                Overall = new SyncStatusInfo
                {
                    LastSyncedAt = lastSync,
                    NeedsAutoSync = needsAutoSync,
                    CanSyncNow = throttleStatus.CanSyncNow,
                    SongsSyncedInWindow = throttleStatus.Usage,
                    RemainingSongsInWindow = throttleStatus.Remaining,
                    SongSyncLimit = SongSyncLimitPerWindow,
                    WindowMinutes = SyncWindowMinutes,
                    Message = throttleStatus.Message
                },
                Playlists = playlistInfos
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get sync status");
            return StatusCode(500, new { error = "Failed to get sync status" });
        }
    }

    /// <summary>
    /// Batch sync playlists from a service
    /// </summary>
    [HttpPost("batch")]
    public async Task<ActionResult<BatchSyncResponse>> BatchSync(
        [FromBody] BatchSyncRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = await GetCurrentUserIdAsync();

            if (string.IsNullOrWhiteSpace(request.Service))
            {
                return BadRequest(new { error = "Service is required" });
            }

            var normalizedService = request.Service.ToLowerInvariant();
            if (!_platformRegistry.IsSupported(normalizedService))
            {
                return BadRequest(new { error = $"Service '{request.Service}' not supported yet" });
            }

            var platform = _platformRegistry.GetRequired(normalizedService);

            var now = DateTimeOffset.UtcNow;
            // Get playlists to sync
            List<string> playlistIdsToSync;
            var playlists = await platform.GetPlaylistsAsync(userId);

            if (request.ServicePlaylistIds == null || request.ServicePlaylistIds.Count == 0)
            {
                // Sync all playlists
                playlistIdsToSync = playlists.Select(p => p.Id).ToList();
                _logger.LogInformation("Syncing all {Count} {Platform} playlists for user {UserId}",
                    playlistIdsToSync.Count, platform.PlatformId, userId);
            }
            else
            {
                playlistIdsToSync = request.ServicePlaylistIds;
                _logger.LogInformation("Syncing {Count} selected {Platform} playlists for user {UserId}",
                    playlistIdsToSync.Count, platform.PlatformId, userId);
            }

            var selectedPlaylists = playlists
                .Where(p => playlistIdsToSync.Contains(p.Id))
                .ToList();
            var selectedPlaylistIds = selectedPlaylists
                .Select(p => p.Id)
                .ToHashSet();

            var existingMappings = await _dbContext.ServicePlaylistMappings
                .Where(m => m.Service == normalizedService
                    && m.Playlist!.UserId == userId
                    && selectedPlaylistIds.Contains(m.ServicePlaylistId))
                .Select(m => new { m.ServicePlaylistId, m.PlaylistId })
                .ToListAsync(cancellationToken);

            var mappedPlaylistIds = existingMappings
                .Select(m => m.PlaylistId)
                .Distinct()
                .ToList();
            var existingEntryCountsByPlaylistId = mappedPlaylistIds.Count == 0
                ? []
                : await _dbContext.PlaylistEntries
                    .Where(e => mappedPlaylistIds.Contains(e.PlaylistId))
                    .GroupBy(e => e.PlaylistId)
                    .Select(g => new { PlaylistId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(g => g.PlaylistId, g => g.Count, cancellationToken);

            var existingEntryCountsByServicePlaylistId = existingMappings.ToDictionary(
                m => m.ServicePlaylistId,
                m => existingEntryCountsByPlaylistId.TryGetValue(m.PlaylistId, out var count) ? count : 0);

            var estimatedNewSongsToSync = selectedPlaylists.Sum(playlist =>
            {
                var remoteSongCount = Math.Max(0, playlist.ItemCount);
                var existingSongCount = existingEntryCountsByServicePlaylistId.TryGetValue(playlist.Id, out var count)
                    ? count
                    : 0;
                return Math.Max(0, remoteSongCount - existingSongCount);
            });

            var throttleStatus = BuildSyncThrottleStatus(userId, normalizedService, now);
            if (throttleStatus.Usage > 0 && estimatedNewSongsToSync > throttleStatus.Remaining)
            {
                return BadRequest(new
                {
                    error = $"This sync would process approximately {estimatedNewSongsToSync} new songs, but only {throttleStatus.Remaining} songs remain in the current {SyncWindowMinutes}-minute window."
                });
            }

            var playlistLookup = playlists.ToDictionary(p => p.Id, p => p);
            var results = new List<BatchSyncResult>();
            var songsSynced = 0;

            // Sync each playlist sequentially
            foreach (var playlistId in playlistIdsToSync)
            {
                try
                {
                    // Get playlist name
                    playlistLookup.TryGetValue(playlistId, out var playlist);
                    var playlistName = playlist?.Title ?? playlistId;

                    var cantaroPlaylistId = await platform.SyncPlaylistAsync(
                        userId,
                        playlistId,
                        cancellationToken);

                    results.Add(new BatchSyncResult
                    {
                        ServicePlaylistId = playlistId,
                        PlaylistName = playlistName,
                        Success = true,
                        CantaroPlaylistId = cantaroPlaylistId.ToString()
                    });
                    var existingSongCount = existingEntryCountsByServicePlaylistId.TryGetValue(playlistId, out var count)
                        ? count
                        : 0;
                    songsSynced += Math.Max(0, Math.Max(0, playlist?.ItemCount ?? 0) - existingSongCount);

                    _logger.LogInformation("Successfully synced playlist {PlaylistId} ({Name})",
                        playlistId, playlistName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to sync playlist {PlaylistId}", playlistId);

                    results.Add(new BatchSyncResult
                    {
                        ServicePlaylistId = playlistId,
                        PlaylistName = playlistId,
                        Success = false,
                        Error = ex.Message
                    });
                }
            }

            var response = new BatchSyncResponse
            {
                Results = results,
                SuccessCount = results.Count(r => r.Success),
                FailureCount = results.Count(r => !r.Success),
                SongsRequested = estimatedNewSongsToSync,
                SongsSynced = songsSynced
            };

            AddSyncUsage(userId, normalizedService, Math.Min(songsSynced, MaxSongsCountedPerSyncRun), now);

            _logger.LogInformation(
                "Batch sync completed: {Success} succeeded, {Failed} failed",
                response.SuccessCount, response.FailureCount);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform batch sync");
            return StatusCode(500, new { error = "Failed to perform batch sync" });
        }
    }

    private async Task<int> GetCurrentUserIdAsync()
    {
        var email = User.Identity?.Name;
        if (string.IsNullOrEmpty(email))
            throw new UnauthorizedAccessException("User not authenticated");

        var user = await _userManager.FindByEmailAsync(email);
        if (user == null)
            throw new UnauthorizedAccessException("User not found");

        return user.Id;
    }

    private string? ResolveServiceForStatus(string? requestedService, IEnumerable<string> mappedServices)
    {
        if (!string.IsNullOrWhiteSpace(requestedService))
        {
            var normalizedRequestedService = requestedService.ToLowerInvariant();
            return _platformRegistry.IsSupported(normalizedRequestedService)
                ? normalizedRequestedService
                : null;
        }

        var mappedService = mappedServices.FirstOrDefault(_platformRegistry.IsSupported);
        if (!string.IsNullOrWhiteSpace(mappedService))
        {
            return mappedService;
        }

        return _platformRegistry.GetSupportedPlatformIds().FirstOrDefault();
    }

    private static string BuildUsageKey(int userId, string service) => $"{userId}:{service}";

    private static SyncThrottleStatus BuildSyncThrottleStatus(int userId, string service, DateTimeOffset now)
    {
        var usage = GetSongsSyncedInWindow(userId, service, now);
        var remaining = Math.Max(0, SongSyncLimitPerWindow - usage);
        var canSyncNow = usage <= 0 || remaining > 0;

        string? message = null;
        if (usage > 0 && remaining <= 0)
        {
            message = $"Song sync limit reached ({SongSyncLimitPerWindow} songs per {SyncWindowMinutes} minutes).";
        }
        else if (usage > 0)
        {
            message = $"You can sync up to {remaining} more songs in the current {SyncWindowMinutes}-minute window.";
        }

        return new SyncThrottleStatus
        {
            Usage = usage,
            Remaining = remaining,
            CanSyncNow = canSyncNow,
            Message = message
        };
    }

    private static int GetSongsSyncedInWindow(int userId, string service, DateTimeOffset now)
    {
        var key = BuildUsageKey(userId, service);
        var bucket = _syncUsageByUserAndService.GetOrAdd(key, _ => new SyncUsageBucket());
        var cutoff = now.AddMinutes(-SyncWindowMinutes);

        lock (bucket.SyncRoot)
        {
            bucket.Events.RemoveAll(e => e.OccurredAt < cutoff);
            return bucket.Events.Sum(e => e.SongsSynced);
        }
    }

    private static void AddSyncUsage(int userId, string service, int songsSynced, DateTimeOffset now)
    {
        if (songsSynced <= 0)
        {
            return;
        }

        var key = BuildUsageKey(userId, service);
        var bucket = _syncUsageByUserAndService.GetOrAdd(key, _ => new SyncUsageBucket());
        var cutoff = now.AddMinutes(-SyncWindowMinutes);

        lock (bucket.SyncRoot)
        {
            bucket.Events.RemoveAll(e => e.OccurredAt < cutoff);
            bucket.Events.Add(new SyncUsageEvent
            {
                OccurredAt = now,
                SongsSynced = songsSynced
            });
        }
    }
}
