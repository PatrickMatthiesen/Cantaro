using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Cantaro.Api.Controllers;

/// <summary>
/// DTO for connected account status
/// </summary>
public class ConnectedAccountDto
{
    public bool IsConnected { get; set; }
    public string? DisplayName { get; set; }
    public string? ExternalAccountId { get; set; }
    public DateTime? ConnectedAt { get; set; }
}

[ApiController]
[Route("api/youtube")]
[Authorize]
public class YouTubeController : ControllerBase
{
    private readonly YouTubeService _youtubeService;
    private readonly UserManager<User> _userManager;
    private readonly IConfiguration _configuration;
    private readonly ILogger<YouTubeController> _logger;

    public YouTubeController(
        YouTubeService youtubeService,
        UserManager<User> userManager,
        IConfiguration configuration,
        ILogger<YouTubeController> logger)
    {
        _youtubeService = youtubeService;
        _userManager = userManager;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Gets the connection status of the user's YouTube account
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult<ConnectedAccountDto>> GetStatus()
    {
        var userId = await GetCurrentUserIdAsync();
        var account = await _youtubeService.GetConnectedAccountAsync(userId);

        return Ok(new ConnectedAccountDto
        {
            IsConnected = account != null,
            DisplayName = account?.DisplayName,
            ExternalAccountId = account?.ExternalAccountId,
            ConnectedAt = account?.CreatedAt
        });
    }

    /// <summary>
    /// Initiates YouTube OAuth flow
    /// </summary>
    [HttpGet("connect")]
    public async Task<ActionResult> Connect([FromQuery] string? returnUrl = null)
    {
        var userId = await GetCurrentUserIdAsync();
        
        // Generate a state parameter that includes the user ID and return URL for security
        var state = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{userId}:{returnUrl ?? "/youtube"}"));

        var baseUrl = GetBaseUrl();
        var redirectUri = $"{baseUrl}/api/youtube/callback";

        var authUrl = _youtubeService.GetAuthorizationUrl(redirectUri, state);

        return Redirect(authUrl);
    }

    /// <summary>
    /// OAuth callback from Google
    /// </summary>
    [HttpGet("callback")]
    [AllowAnonymous] // Callback is from Google, user session is validated via state
    public async Task<ActionResult> Callback([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error)
    {
        if (!string.IsNullOrEmpty(error))
        {
            _logger.LogWarning("YouTube OAuth error: {Error}", error);
            return Redirect("/youtube?error=oauth_denied");
        }

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
        {
            return Redirect("/youtube?error=invalid_callback");
        }

        try
        {
            // Parse state to get user ID and return URL
            var stateBytes = Convert.FromBase64String(state);
            var stateString = System.Text.Encoding.UTF8.GetString(stateBytes);
            var stateParts = stateString.Split(':', 2);
            
            if (!int.TryParse(stateParts[0], out var userId))
            {
                return Redirect("/youtube?error=invalid_state");
            }

            var returnUrl = stateParts.Length > 1 ? stateParts[1] : "/youtube";

            var baseUrl = GetBaseUrl();
            var redirectUri = $"{baseUrl}/api/youtube/callback";

            await _youtubeService.ExchangeCodeAndSaveAsync(userId, code, redirectUri);

            _logger.LogInformation("Successfully connected YouTube account for user {UserId}", userId);

            return Redirect($"{returnUrl}?connected=true");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to exchange YouTube authorization code");
            return Redirect("/youtube?error=exchange_failed");
        }
    }

    /// <summary>
    /// Disconnects the user's YouTube account
    /// </summary>
    [HttpPost("disconnect")]
    public async Task<ActionResult> Disconnect()
    {
        var userId = await GetCurrentUserIdAsync();
        await _youtubeService.DisconnectAsync(userId);

        _logger.LogInformation("Disconnected YouTube account for user {UserId}", userId);

        return Ok(new { message = "YouTube account disconnected" });
    }

    /// <summary>
    /// Gets the user's YouTube playlists
    /// </summary>
    [HttpGet("playlists")]
    public async Task<ActionResult<List<YouTubePlaylistDto>>> GetPlaylists()
    {
        try
        {
            var userId = await GetCurrentUserIdAsync();
            var playlists = await _youtubeService.GetPlaylistsAsync(userId);
            return Ok(playlists);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Gets items in a specific YouTube playlist
    /// </summary>
    [HttpGet("playlists/{playlistId}/items")]
    public async Task<ActionResult<List<YouTubePlaylistItemDto>>> GetPlaylistItems(string playlistId)
    {
        try
        {
            var userId = await GetCurrentUserIdAsync();
            var items = await _youtubeService.GetPlaylistItemsAsync(userId, playlistId);
            return Ok(items);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
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

    private string GetBaseUrl()
    {
        // In development, use the configured API URL or construct from request
        var apiUrl = _configuration["ApiBaseUrl"];
        if (!string.IsNullOrEmpty(apiUrl))
            return apiUrl.TrimEnd('/');

        var request = HttpContext.Request;
        return $"{request.Scheme}://{request.Host}";
    }
}
