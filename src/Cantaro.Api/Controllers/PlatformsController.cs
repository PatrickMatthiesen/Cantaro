using System.Text.Json;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Cantaro.Api.Controllers;

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
    private readonly IPlatformRegistry _platformRegistry;
    private readonly UserManager<User> _userManager;
    private readonly ILogger<PlatformsController> _logger;
    private readonly IDataProtector _stateProtector;
    private readonly IFrontendUrlResolver _urlResolver;

    public PlatformsController(
        IPlatformRegistry platformRegistry,
        UserManager<User> userManager,
        ILogger<PlatformsController> logger,
        IDataProtectionProvider dataProtectionProvider,
        IFrontendUrlResolver urlResolver)
    {
        _platformRegistry = platformRegistry;
        _userManager = userManager;
        _logger = logger;
        _stateProtector = dataProtectionProvider.CreateProtector("Platform.OAuth.State");
        _urlResolver = urlResolver;
    }

    [HttpGet("status")]
    public async Task<ActionResult<ConnectedAccountDto>> GetStatus(string platformId)
    {
        if (!_platformRegistry.IsSupported(platformId))
        {
            return NotFound(new { error = $"Platform '{platformId}' is not implemented" });
        }

        var platform = _platformRegistry.GetRequired(platformId);
        var userId = await GetCurrentUserIdAsync();
        var account = await platform.GetConnectedAccountAsync(userId);

        return Ok(new ConnectedAccountDto
        {
            IsConnected = account != null,
            DisplayName = account?.DisplayName,
            ExternalAccountId = account?.ExternalAccountId,
            ConnectedAt = account?.CreatedAt,
            PlatformId = platform.PlatformId
        });
    }

    [HttpGet("connect")]
    public async Task<ActionResult> Connect(string platformId, [FromQuery] string? route = null, [FromQuery] string? trigger = null)
    {
        if (!_platformRegistry.IsSupported(platformId))
        {
            return NotFound(new { error = $"Platform '{platformId}' is not implemented" });
        }

        var normalizedPlatformId = platformId.ToLowerInvariant();
        var platform = _platformRegistry.GetRequired(normalizedPlatformId);
        var userId = await GetCurrentUserIdAsync();
        var safeReturnRoute = SanitizeReturnUrl(route, defaultPath: $"/{normalizedPlatformId}");

        var statePayload = new PlatformOAuthState
        {
            UserId = userId,
            PlatformId = normalizedPlatformId,
            ReturnUrl = safeReturnRoute,
            Trigger = string.IsNullOrWhiteSpace(trigger) ? "platform-manager" : trigger,
            CreatedAtTicksUtc = DateTime.UtcNow.Ticks
        };

        var state = _stateProtector.Protect(JsonSerializer.Serialize(statePayload));
        var redirectUri = _urlResolver.GetCallbackUrl($"api/platforms/{normalizedPlatformId}/callback");

        var authUrl = platform.GetAuthorizationUrl(redirectUri, state);
        return Redirect(authUrl);
    }

    [HttpGet("callback")]
    [AllowAnonymous]
    public async Task<ActionResult> Callback(string platformId, [FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error)
    {
        var normalizedPlatformId = platformId.ToLowerInvariant();
        var frontendUrl = _urlResolver.GetFrontendUrl();
        var defaultPlatformPath = $"/{normalizedPlatformId}";

        if (!_platformRegistry.IsSupported(normalizedPlatformId))
        {
            return Redirect($"{frontendUrl}/?error=platform_not_supported");
        }

        var platform = _platformRegistry.GetRequired(normalizedPlatformId);

        if (!string.IsNullOrWhiteSpace(error))
        {
            _logger.LogWarning("{Platform} OAuth error: {Error}", normalizedPlatformId, error);
            return Redirect($"{frontendUrl}{defaultPlatformPath}?error=oauth_denied");
        }

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
        {
            return Redirect($"{frontendUrl}{defaultPlatformPath}?error=invalid_callback");
        }

        try
        {
            var stateData = _stateProtector.Unprotect(state);
            var payload = JsonSerializer.Deserialize<PlatformOAuthState>(stateData);

            if (payload is null || payload.UserId <= 0 || !string.Equals(payload.PlatformId, normalizedPlatformId, StringComparison.OrdinalIgnoreCase))
            {
                return Redirect($"{frontendUrl}{defaultPlatformPath}?error=invalid_state");
            }

            var createdAt = new DateTime(payload.CreatedAtTicksUtc, DateTimeKind.Utc);
            if (DateTime.UtcNow - createdAt > TimeSpan.FromMinutes(10))
            {
                _logger.LogWarning("{Platform} OAuth state expired for user {UserId}", normalizedPlatformId, payload.UserId);
                return Redirect($"{frontendUrl}{defaultPlatformPath}?error=state_expired");
            }

            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser != null && currentUser.Id != payload.UserId)
            {
                _logger.LogWarning("State userId {StateUserId} doesn't match authenticated user {ActualUserId}", payload.UserId, currentUser.Id);
                return Redirect($"{frontendUrl}{defaultPlatformPath}?error=user_mismatch");
            }

            var returnUrl = SanitizeReturnUrl(payload.ReturnUrl, defaultPath: defaultPlatformPath);
            var redirectUri = _urlResolver.GetCallbackUrl($"api/platforms/{normalizedPlatformId}/callback");

            await platform.ExchangeCodeAndSaveAsync(payload.UserId, code, redirectUri);

            _logger.LogInformation("Successfully connected {Platform} account for user {UserId}", normalizedPlatformId, payload.UserId);

            var separator = returnUrl.Contains('?') ? '&' : '?';
            return Redirect($"{frontendUrl}{returnUrl}{separator}connected=true&platform={normalizedPlatformId}&trigger={Uri.EscapeDataString(payload.Trigger)}");
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            _logger.LogWarning(ex, "Invalid or tampered platform OAuth state");
            return Redirect($"{frontendUrl}{defaultPlatformPath}?error=invalid_state");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to exchange {Platform} authorization code", normalizedPlatformId);
            return Redirect($"{frontendUrl}{defaultPlatformPath}?error=exchange_failed");
        }
    }

    [HttpPost("disconnect")]
    public async Task<ActionResult> Disconnect(string platformId)
    {
        if (!_platformRegistry.IsSupported(platformId))
        {
            return NotFound(new { error = $"Platform '{platformId}' is not implemented" });
        }

        var platform = _platformRegistry.GetRequired(platformId);
        var userId = await GetCurrentUserIdAsync();
        await platform.DisconnectAsync(userId);

        _logger.LogInformation("Disconnected {Platform} account for user {UserId}", platform.PlatformId, userId);

        return Ok(new { message = $"{platform.PlatformId} account disconnected" });
    }

    [HttpGet("playlists")]
    public async Task<ActionResult<List<PlatformPlaylistDto>>> GetPlaylists(string platformId)
    {
        if (!_platformRegistry.IsSupported(platformId))
        {
            return NotFound(new { error = $"Platform '{platformId}' is not implemented" });
        }

        try
        {
            var platform = _platformRegistry.GetRequired(platformId);
            var userId = await GetCurrentUserIdAsync();
            var playlists = await platform.GetPlaylistsAsync(userId);
            return Ok(playlists);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("playlists/{playlistId}/songs")]
    public async Task<ActionResult<List<PlatformSongDto>>> GetPlaylistSongs(string platformId, string playlistId)
    {
        if (!_platformRegistry.IsSupported(platformId))
        {
            return NotFound(new { error = $"Platform '{platformId}' is not implemented" });
        }

        var platform = _platformRegistry.GetRequired(platformId);
        if (!platform.TryValidatePlaylistId(playlistId, out var validationError))
        {
            return BadRequest(new { error = validationError });
        }

        try
        {
            var userId = await GetCurrentUserIdAsync();
            var items = await platform.GetPlaylistSongsAsync(userId, playlistId);
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
}
