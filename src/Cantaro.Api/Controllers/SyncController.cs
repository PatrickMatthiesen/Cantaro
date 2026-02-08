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
    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<User> _userManager;
    private readonly YouTubePlaylistSyncService _youtubeSyncService;
    private readonly YouTubeService _youtubeService;
    private readonly ILogger<SyncController> _logger;

    public SyncController(
        ApplicationDbContext dbContext,
        UserManager<User> userManager,
        YouTubePlaylistSyncService youtubeSyncService,
        YouTubeService youtubeService,
        ILogger<SyncController> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _youtubeSyncService = youtubeSyncService;
        _youtubeService = youtubeService;
        _logger = logger;
    }

    /// <summary>
    /// Get sync status for all playlists
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult<SyncStatusResponse>> GetSyncStatus(CancellationToken cancellationToken)
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
            
            bool needsAutoSync = lastSync == null || (now - lastSync.Value).TotalHours >= 24;
            bool canSyncNow = lastSync == null || (now - lastSync.Value).TotalMinutes >= 5;

            string? message = null;
            if (!canSyncNow)
            {
                var timeUntilNext = TimeSpan.FromMinutes(5) - (now - lastSync!.Value);
                message = $"Please wait {Math.Ceiling(timeUntilNext.TotalMinutes)} minute(s) before syncing again";
            }

            return Ok(new SyncStatusResponse
            {
                Overall = new SyncStatusInfo
                {
                    LastSyncedAt = lastSync,
                    NeedsAutoSync = needsAutoSync,
                    CanSyncNow = canSyncNow,
                    Message = message
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

            // Validate service
            if (request.Service != "youtube")
            {
                return BadRequest(new { error = $"Service '{request.Service}' not supported yet" });
            }

            // Check rate limiting
            var lastSync = await _dbContext.ServicePlaylistMappings
                .Include(m => m.Playlist)
                .Where(m => m.Playlist!.UserId == userId && m.Service == request.Service)
                .OrderByDescending(m => m.LastSyncedAt)
                .Select(m => m.LastSyncedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (lastSync.HasValue && (DateTimeOffset.UtcNow - lastSync.Value).TotalMinutes < 5)
            {
                return BadRequest(new { error = "Please wait at least 5 minutes between syncs" });
            }

            // Get playlists to sync
            List<string> playlistIdsToSync;
            
            if (request.ServicePlaylistIds == null || request.ServicePlaylistIds.Count == 0)
            {
                // Sync all playlists
                var allPlaylists = await _youtubeService.GetPlaylistsAsync(userId);
                playlistIdsToSync = allPlaylists.Select(p => p.Id).ToList();
                _logger.LogInformation("Syncing all {Count} YouTube playlists for user {UserId}", 
                    playlistIdsToSync.Count, userId);
            }
            else
            {
                playlistIdsToSync = request.ServicePlaylistIds;
                _logger.LogInformation("Syncing {Count} selected YouTube playlists for user {UserId}", 
                    playlistIdsToSync.Count, userId);
            }

            var results = new List<BatchSyncResult>();

            // Sync each playlist sequentially
            foreach (var playlistId in playlistIdsToSync)
            {
                try
                {
                    // Get playlist name
                    var playlists = await _youtubeService.GetPlaylistsAsync(userId);
                    var playlist = playlists.FirstOrDefault(p => p.Id == playlistId);
                    var playlistName = playlist?.Title ?? playlistId;

                    var cantaroPlaylistId = await _youtubeSyncService.SyncYouTubePlaylistAsync(
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
                FailureCount = results.Count(r => !r.Success)
            };

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
}
