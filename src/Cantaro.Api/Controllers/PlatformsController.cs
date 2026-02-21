using System.Text.Json;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Cantaro.Api.Controllers;

public class PlatformPlaylistDto
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public int ItemCount { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}

public class PlatformSongDto
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? ArtistName { get; set; }
    public int Index { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}

internal sealed class PlatformOAuthState
{
    public required int UserId { get; set; }
    public required string PlatformId { get; set; }
    public required string ReturnUrl { get; set; }
    public required string Trigger { get; set; }
    public required long CreatedAtTicksUtc { get; set; }
}

[ApiController]
[Route("api/platforms/{platformId}")]
[Authorize]
public class PlatformsController : ControllerBase
{
    private readonly YouTubeService _youtubeService;
    private readonly UserManager<User> _userManager;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PlatformsController> _logger;
    private readonly IDataProtector _stateProtector;
    private readonly IHostEnvironment _environment;

    public PlatformsController(
        YouTubeService youtubeService,
        UserManager<User> userManager,
        IConfiguration configuration,
        ILogger<PlatformsController> logger,
        IDataProtectionProvider dataProtectionProvider,
        IHostEnvironment environment)
    {
        _youtubeService = youtubeService;
        _userManager = userManager;
        _configuration = configuration;
        _logger = logger;
        _stateProtector = dataProtectionProvider.CreateProtector("Platform.OAuth.State");
        _environment = environment;
    }

    [HttpGet("status")]
    public async Task<ActionResult<ConnectedAccountDto>> GetStatus(string platformId)
    {
        if (!IsPlatformImplemented(platformId))
        {
            return NotFound(new { error = $"Platform '{platformId}' is not implemented" });
        }

        var userId = await GetCurrentUserIdAsync();
        var account = await _youtubeService.GetConnectedAccountAsync(userId);

        return Ok(new ConnectedAccountDto
        {
            IsConnected = account != null,
            DisplayName = account?.DisplayName,
            ExternalAccountId = account?.ExternalAccountId,
            ConnectedAt = account?.CreatedAt,
            PlatformId = "youtube"
        });
    }

    [HttpGet("connect")]
    public async Task<ActionResult> Connect(string platformId, [FromQuery] string? route = null, [FromQuery] string? trigger = null)
    {
        if (!IsPlatformImplemented(platformId))
        {
            return NotFound(new { error = $"Platform '{platformId}' is not implemented" });
        }

        var userId = await GetCurrentUserIdAsync();
        var safeReturnRoute = SanitizeReturnUrl(route, defaultPath: "/youtube");

        var statePayload = new PlatformOAuthState
        {
            UserId = userId,
            PlatformId = platformId.ToLowerInvariant(),
            ReturnUrl = safeReturnRoute,
            Trigger = string.IsNullOrWhiteSpace(trigger) ? "platform-manager" : trigger,
            CreatedAtTicksUtc = DateTime.UtcNow.Ticks
        };

        var state = _stateProtector.Protect(JsonSerializer.Serialize(statePayload));
        var baseUrl = GetBaseUrl();
        var redirectUri = $"{baseUrl}/api/platforms/{platformId.ToLowerInvariant()}/callback";

        var authUrl = _youtubeService.GetAuthorizationUrl(redirectUri, state);
        return Redirect(authUrl);
    }

    [HttpGet("callback")]
    [AllowAnonymous]
    public async Task<ActionResult> Callback(string platformId, [FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error)
    {
        var normalizedPlatformId = platformId.ToLowerInvariant();
        var frontendUrl = GetFrontendUrl();

        if (!IsPlatformImplemented(normalizedPlatformId))
        {
            return Redirect($"{frontendUrl}/?error=platform_not_supported");
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            _logger.LogWarning("{Platform} OAuth error: {Error}", normalizedPlatformId, error);
            return Redirect($"{frontendUrl}/youtube?error=oauth_denied");
        }

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
        {
            return Redirect($"{frontendUrl}/youtube?error=invalid_callback");
        }

        try
        {
            var stateData = _stateProtector.Unprotect(state);
            var payload = JsonSerializer.Deserialize<PlatformOAuthState>(stateData);

            if (payload is null || payload.UserId <= 0 || !string.Equals(payload.PlatformId, normalizedPlatformId, StringComparison.OrdinalIgnoreCase))
            {
                return Redirect($"{frontendUrl}/youtube?error=invalid_state");
            }

            var createdAt = new DateTime(payload.CreatedAtTicksUtc, DateTimeKind.Utc);
            if (DateTime.UtcNow - createdAt > TimeSpan.FromMinutes(10))
            {
                _logger.LogWarning("{Platform} OAuth state expired for user {UserId}", normalizedPlatformId, payload.UserId);
                return Redirect($"{frontendUrl}/youtube?error=state_expired");
            }

            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser != null && currentUser.Id != payload.UserId)
            {
                _logger.LogWarning("State userId {StateUserId} doesn't match authenticated user {ActualUserId}", payload.UserId, currentUser.Id);
                return Redirect($"{frontendUrl}/youtube?error=user_mismatch");
            }

            var returnUrl = SanitizeReturnUrl(payload.ReturnUrl, defaultPath: "/youtube");
            var baseUrl = GetBaseUrl();
            var redirectUri = $"{baseUrl}/api/platforms/{normalizedPlatformId}/callback";

            await _youtubeService.ExchangeCodeAndSaveAsync(payload.UserId, code, redirectUri);

            _logger.LogInformation("Successfully connected {Platform} account for user {UserId}", normalizedPlatformId, payload.UserId);

            var separator = returnUrl.Contains('?') ? '&' : '?';
            return Redirect($"{frontendUrl}{returnUrl}{separator}connected=true&platform={normalizedPlatformId}&trigger={Uri.EscapeDataString(payload.Trigger)}");
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            _logger.LogWarning(ex, "Invalid or tampered platform OAuth state");
            return Redirect($"{frontendUrl}/youtube?error=invalid_state");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to exchange {Platform} authorization code", normalizedPlatformId);
            return Redirect($"{frontendUrl}/youtube?error=exchange_failed");
        }
    }

    [HttpPost("disconnect")]
    public async Task<ActionResult> Disconnect(string platformId)
    {
        if (!IsPlatformImplemented(platformId))
        {
            return NotFound(new { error = $"Platform '{platformId}' is not implemented" });
        }

        var userId = await GetCurrentUserIdAsync();
        await _youtubeService.DisconnectAsync(userId);

        _logger.LogInformation("Disconnected {Platform} account for user {UserId}", platformId, userId);

        return Ok(new { message = "YouTube account disconnected" });
    }

    [HttpGet("playlists")]
    public async Task<ActionResult<List<PlatformPlaylistDto>>> GetPlaylists(string platformId)
    {
        if (!IsPlatformImplemented(platformId))
        {
            return NotFound(new { error = $"Platform '{platformId}' is not implemented" });
        }

        try
        {
            var userId = await GetCurrentUserIdAsync();
            var playlists = await _youtubeService.GetPlaylistsAsync(userId);

            var mapped = playlists.Select(playlist => new PlatformPlaylistDto
            {
                Id = playlist.Id,
                Title = playlist.Title,
                Description = playlist.Description,
                ThumbnailUrl = playlist.ThumbnailUrl,
                ItemCount = playlist.ItemCount,
                PublishedAt = playlist.PublishedAt
            }).ToList();

            return Ok(mapped);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("playlists/{playlistId}/songs")]
    public async Task<ActionResult<List<PlatformSongDto>>> GetPlaylistSongs(string platformId, string playlistId)
    {
        if (!IsPlatformImplemented(platformId))
        {
            return NotFound(new { error = $"Platform '{platformId}' is not implemented" });
        }

        if (string.IsNullOrWhiteSpace(playlistId))
        {
            return BadRequest(new { error = "Playlist ID is required" });
        }

        if (playlistId.Length < 11 || playlistId.Length > 100 || !playlistId.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-'))
        {
            return BadRequest(new { error = "Invalid playlist ID format" });
        }

        try
        {
            var userId = await GetCurrentUserIdAsync();
            var items = await _youtubeService.GetPlaylistItemsAsync(userId, playlistId);

            var mapped = items.Select(item => new PlatformSongDto
            {
                Id = item.VideoId,
                Title = item.Title,
                Description = item.Description,
                ThumbnailUrl = item.ThumbnailUrl,
                ArtistName = item.ChannelTitle,
                Index = item.Position,
                PublishedAt = item.PublishedAt
            }).ToList();

            return Ok(mapped);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private static bool IsPlatformImplemented(string platformId)
    {
        return string.Equals(platformId, "youtube", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<int> GetCurrentUserIdAsync()
    {
        var email = User.Identity?.Name;
        if (string.IsNullOrEmpty(email))
        {
            throw new UnauthorizedAccessException("User not authenticated");
        }

        var user = await _userManager.FindByEmailAsync(email);
        if (user == null)
        {
            throw new UnauthorizedAccessException("User not found");
        }

        return user.Id;
    }

    private static string SanitizeReturnUrl(string? route, string defaultPath)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return defaultPath;
        }

        if (!route.StartsWith('/') || route.StartsWith("//") || route.Contains("://") || route.Contains('\\'))
        {
            return defaultPath;
        }

        return route;
    }

    private string GetBaseUrl()
    {
        var request = HttpContext.Request;
        return $"{request.Scheme}://{request.Host}";
    }

    private string GetFrontendUrl()
    {
        var frontendUrl = _configuration["services:web:http:0"]
            ?? _configuration["services:web:0"];

        if (!string.IsNullOrEmpty(frontendUrl))
        {
            return frontendUrl.TrimEnd('/');
        }

        if (_environment.IsDevelopment())
        {
            _logger.LogWarning("Frontend URL not configured via Aspire, using development fallback: http://localhost:8080");
            return "http://localhost:8080";
        }

        _logger.LogError("Frontend URL not configured in production environment");
        var request = HttpContext.Request;
        return $"{request.Scheme}://{request.Host}";
    }
}
