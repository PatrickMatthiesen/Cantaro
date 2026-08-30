using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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

public class BatchSyncResult
{
    public required string ServicePlaylistId { get; set; }
    public required string PlaylistName { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? ErrorCode { get; set; }
    public bool Retryable { get; set; }
    public string? CantaroPlaylistId { get; set; }
}

[ApiController]
[Route("api/sync")]
[Authorize]
public class SyncController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<User> _userManager;
    private readonly IPlatformRegistry _platformRegistry;
    private readonly MusicSyncThrottleService _throttleService;
    private readonly ILogger<SyncController> _logger;

    public SyncController(
        ApplicationDbContext dbContext,
        UserManager<User> userManager,
        IPlatformRegistry platformRegistry,
        MusicSyncThrottleService throttleService,
        ILogger<SyncController> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _platformRegistry = platformRegistry;
        _throttleService = throttleService;
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
                ? new MusicSyncThrottleStatus(
                    0,
                    MusicSyncThrottleService.SongSyncLimitPerWindow,
                    false,
                    "No supported platform is available for sync.")
                : _throttleService.GetStatus(userId, resolvedService, now);
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
                    SongSyncLimit = MusicSyncThrottleService.SongSyncLimitPerWindow,
                    WindowMinutes = MusicSyncThrottleService.SyncWindowMinutes,
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

    private async Task<int> GetCurrentUserIdAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
            throw new UnauthorizedAccessException("User not authenticated");

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

}
