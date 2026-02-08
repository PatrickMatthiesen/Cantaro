using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
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
    private readonly IDataProtector _stateProtector;
    private readonly IHostEnvironment _environment;

    public YouTubeController(
        YouTubeService youtubeService,
        UserManager<User> userManager,
        IConfiguration configuration,
        ILogger<YouTubeController> logger,
        IDataProtectionProvider dataProtectionProvider,
        IHostEnvironment environment)
    {
        _youtubeService = youtubeService;
        _userManager = userManager;
        _configuration = configuration;
        _logger = logger;
        _stateProtector = dataProtectionProvider.CreateProtector("YouTube.OAuth.State");
        _environment = environment;
    }

    /// <summary>
    /// Gets the connection status of the user's YouTube account
    /// <returns>ConnectedAccountDto with connection details, or ConnectedAccountDto with IsConnected: false</returns>
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
        
        // Generate a cryptographically protected state parameter
        var stateData = $"{userId}:{DateTime.UtcNow.Ticks}:{returnUrl ?? "/youtube"}";
        var state = _stateProtector.Protect(stateData);

        var baseUrl = GetBaseUrl();
        var redirectUri = $"{baseUrl}/api/youtube/callback";

        var authUrl = _youtubeService.GetAuthorizationUrl(redirectUri, state);

        return Redirect(authUrl);
    }

    /// <summary>
    /// OAuth callback from Google
    /// </summary>
    [HttpGet("callback")]
    [AllowAnonymous] // Callback is from Google, user session is validated via encrypted state
    public async Task<ActionResult> Callback([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error)
    {
        var frontendUrl = GetFrontendUrl();
        
        if (!string.IsNullOrEmpty(error))
        {
            _logger.LogWarning("YouTube OAuth error: {Error}", error);
            return Redirect($"{frontendUrl}/youtube?error=oauth_denied");
        }

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
        {
            return Redirect($"{frontendUrl}/youtube?error=invalid_callback");
        }

        try
        {
            // Decrypt and parse state to get user ID and return URL
            var stateData = _stateProtector.Unprotect(state);
            var stateParts = stateData.Split(':', 3);
            
            if (stateParts.Length < 2 || !int.TryParse(stateParts[0], out var userId))
            {
                return Redirect($"{frontendUrl}/youtube?error=invalid_state");
            }

            // Validate state is not too old (max 10 minutes)
            if (long.TryParse(stateParts[1], out var ticks))
            {
                var stateTime = new DateTime(ticks, DateTimeKind.Utc);
                if (DateTime.UtcNow - stateTime > TimeSpan.FromMinutes(10))
                {
                    _logger.LogWarning("YouTube OAuth state expired for user {UserId}", userId);
                    return Redirect($"{frontendUrl}/youtube?error=state_expired");
                }
            }

            // Defense-in-depth: if user is authenticated, verify the state userId matches
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser != null && currentUser.Id != userId)
            {
                _logger.LogWarning("State userId {StateUserId} doesn't match authenticated user {ActualUserId}", userId, currentUser.Id);
                return Redirect($"{frontendUrl}/youtube?error=user_mismatch");
            }

            var returnUrl = stateParts.Length > 2 ? stateParts[2] : "/youtube";

            // Validate returnUrl to prevent open redirect attacks
            if (!string.IsNullOrEmpty(returnUrl))
            {
                // Ensure returnUrl is a safe relative path
                if (!returnUrl.StartsWith('/') || returnUrl.StartsWith("//") || 
                    returnUrl.Contains("://") || returnUrl.Contains('\\'))
                {
                    _logger.LogWarning("Invalid returnUrl in OAuth state for user {UserId}: {ReturnUrl}", userId, returnUrl);
                    returnUrl = "/youtube";
                }
            }

            var baseUrl = GetBaseUrl();
            var redirectUri = $"{baseUrl}/api/youtube/callback";

            await _youtubeService.ExchangeCodeAndSaveAsync(userId, code, redirectUri);

            _logger.LogInformation("Successfully connected YouTube account for user {UserId}", userId);

            return Redirect($"{frontendUrl}{returnUrl}?connected=true");
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            _logger.LogWarning(ex, "Invalid or tampered YouTube OAuth state");
            return Redirect($"{frontendUrl}/youtube?error=invalid_state");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to exchange YouTube authorization code");
            return Redirect($"{frontendUrl}/youtube?error=exchange_failed");
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
        // Validate playlistId: non-empty, matches YouTube playlist ID format
        if (string.IsNullOrWhiteSpace(playlistId))
        {
            return BadRequest(new { error = "Playlist ID is required" });
        }
        // YouTube playlist IDs: typically alphanumeric with _, -, and reasonable length
        if (playlistId.Length < 11 || playlistId.Length > 100 || !playlistId.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-'))
        {
            return BadRequest(new { error = "Invalid playlist ID format" });
        }

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
        var request = HttpContext.Request;
        return $"{request.Scheme}://{request.Host}";
    }

    private string GetFrontendUrl()
    {
        // In Aspire, the frontend reference is injected as services__web__http__0
        // or services__web__0 depending on the endpoint name
        var frontendUrl = _configuration["services:web:http:0"] 
            ?? _configuration["services:web:0"];
        
        if (!string.IsNullOrEmpty(frontendUrl))
        {
            return frontendUrl.TrimEnd('/');
        }

        // Fallback for non-Aspire environments or when not configured
        // In development without Aspire, assume frontend is on localhost:8080 (matches AppHost port)
        if (_environment.IsDevelopment())
        {
            _logger.LogWarning("Frontend URL not configured via Aspire, using development fallback: http://localhost:8080");
            return "http://localhost:8080";
        }

        _logger.LogError("Frontend URL not configured in production environment");

        // In production, frontend should be served from the same origin as the API
        var request = HttpContext.Request;
        return $"{request.Scheme}://{request.Host}";
    }
}
