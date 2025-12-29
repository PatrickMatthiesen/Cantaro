using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Cantaro.Api.Data;

namespace Cantaro.Api.Controllers;

/// <summary>
/// Request model for syncing YouTube playlist
/// </summary>
public class SyncYouTubePlaylistRequest
{
    public required string YoutubePlaylistId { get; set; }
}

/// <summary>
/// Response model for sync operations
/// </summary>
public class SyncPlaylistResponse
{
    public required string PlaylistId { get; set; }
    public required string Status { get; set; }
}

[ApiController]
[Route("api/playlists")]
[Authorize]
public class PlaylistsController : ControllerBase
{
    private readonly YouTubePlaylistSyncService _syncService;
    private readonly UserManager<User> _userManager;
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<PlaylistsController> _logger;

    public PlaylistsController(
        YouTubePlaylistSyncService syncService,
        UserManager<User> userManager,
        ApplicationDbContext dbContext,
        ILogger<PlaylistsController> logger)
    {
        _syncService = syncService;
        _userManager = userManager;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Syncs a YouTube playlist to Cantaro
    /// </summary>
    [HttpPost("sync/youtube")]
    public async Task<ActionResult<SyncPlaylistResponse>> SyncYouTubePlaylist(
        [FromBody] SyncYouTubePlaylistRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            // Validate request
            if (string.IsNullOrWhiteSpace(request.YoutubePlaylistId))
            {
                return BadRequest(new { error = "YouTube playlist ID is required" });
            }

            // Get current user
            var userId = await GetCurrentUserIdAsync();

            // Validate that user has a connected YouTube account
            var hasYouTubeAccount = await _dbContext.ConnectedServiceAccounts
                .AnyAsync(a => a.UserId == userId && a.Service == "youtube", cancellationToken);

            if (!hasYouTubeAccount)
            {
                _logger.LogWarning("User {UserId} attempted to sync YouTube playlist without connected account", userId);
                return BadRequest(new { error = "No YouTube account connected. Please connect your YouTube account first." });
            }

            // Call sync service
            var playlistId = await _syncService.SyncYouTubePlaylistAsync(
                userId, 
                request.YoutubePlaylistId, 
                cancellationToken);

            _logger.LogInformation("Successfully synced YouTube playlist {YouTubePlaylistId} to playlist {PlaylistId} for user {UserId}",
                request.YoutubePlaylistId, playlistId, userId);

            return Accepted(new SyncPlaylistResponse
            {
                PlaylistId = playlistId.ToString(),
                Status = "synced"
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation while syncing YouTube playlist for user");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync YouTube playlist {PlaylistId}", request.YoutubePlaylistId);
            return StatusCode(500, new { error = "Failed to sync playlist. Please try again later." });
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
