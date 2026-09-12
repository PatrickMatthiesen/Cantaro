using System.Security.Claims;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace Cantaro.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class GoogleAuthController(
    GoogleAuthService googleAuthService,
    UserManager<User> userManager,
    IFrontendUrlResolver frontendUrlResolver,
    TimeProvider timeProvider) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("methods")]
    public ActionResult<AuthenticationMethodsResponse> GetMethods() => Ok(googleAuthService.GetMethods());

    [AllowAnonymous]
    [HttpGet("google/login")]
    public IActionResult Login([FromQuery] string? returnUrl)
    {
        if (!googleAuthService.GoogleEnabled)
        {
            return NotFound(new { error = "google_disabled" });
        }

        if (!GoogleAuthService.TryGetSafeReturnUrl(returnUrl, out var safeReturnUrl))
        {
            return BadRequest(new { error = "invalid_return_url" });
        }

        return Challenge(
            googleAuthService.CreateLoginChallenge(safeReturnUrl),
            GoogleDefaults.AuthenticationScheme);
    }

    [Authorize]
    [HttpGet("google/status")]
    public async Task<ActionResult<GoogleAuthStatusResponse>> Status(CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        var logins = await userManager.GetLoginsAsync(user);
        var now = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var reauthenticated = long.TryParse(
                User.FindFirstValue(GoogleAuthService.GoogleReauthenticatedAtClaim),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var reauthenticatedAt)
            && reauthenticatedAt <= now
            && now - reauthenticatedAt <= 300;

        return Ok(new GoogleAuthStatusResponse(
            logins.Any(login => string.Equals(login.LoginProvider, GoogleDefaults.AuthenticationScheme, StringComparison.Ordinal)),
            !string.IsNullOrWhiteSpace(user.PasswordHash),
            reauthenticated));
    }

    [Authorize]
    [HttpPost("google/link")]
    public async Task<ActionResult<GoogleAuthorizationUrlResponse>> StartLink(
        [FromBody] GoogleLinkRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsSameOriginRequest())
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "cross_origin_request" });
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        if (!GoogleAuthService.TryGetSafeReturnUrl(request.ReturnUrl, out var safeReturnUrl))
        {
            return BadRequest(new { error = "invalid_return_url" });
        }

        var result = await googleAuthService.StartLinkAsync(user, request.CurrentPassword, safeReturnUrl, cancellationToken);
        return result.Succeeded
            ? Ok(new GoogleAuthorizationUrlResponse(result.AuthorizationUrl!))
            : BadRequest(new { error = result.ErrorCode, message = result.ErrorMessage });
    }

    [AllowAnonymous]
    [HttpGet("google/link")]
    public IActionResult LinkChallenge([FromQuery] string? token)
    {
        if (!googleAuthService.GoogleEnabled || string.IsNullOrWhiteSpace(token))
        {
            return BadRequest(new { error = "invalid_google_link_request" });
        }

        return Challenge(
            googleAuthService.CreateLinkChallenge(token),
            GoogleDefaults.AuthenticationScheme);
    }

    [Authorize]
    [HttpPost("google/reauth")]
    public async Task<IActionResult> StartReauthenticate([FromBody] GoogleReauthRequest request, CancellationToken cancellationToken)
    {
        if (!googleAuthService.GoogleEnabled)
        {
            return NotFound(new { error = "google_disabled" });
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        if (!IsSameOriginRequest())
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "cross_origin_request" });
        }

        if (!GoogleAuthService.TryGetSafeReturnUrl(request.ReturnUrl, out var safeReturnUrl))
        {
            return BadRequest(new { error = "invalid_return_url" });
        }

        var result = await googleAuthService.StartReauthenticationAsync(user, safeReturnUrl, cancellationToken);
        return result.Succeeded
            ? Ok(new GoogleAuthorizationUrlResponse(result.AuthorizationUrl!))
            : BadRequest(new { error = result.ErrorCode, message = result.ErrorMessage });
    }

    [AllowAnonymous]
    [HttpGet("google/reauth")]
    public IActionResult ReauthenticateChallenge([FromQuery] string? token)
    {
        if (!googleAuthService.GoogleEnabled || string.IsNullOrWhiteSpace(token))
        {
            return BadRequest(new { error = "invalid_google_reauth_request" });
        }

        return Challenge(
            googleAuthService.CreateReauthenticationChallenge(token),
            GoogleDefaults.AuthenticationScheme);
    }

    [AllowAnonymous]
    [HttpGet("google/callback")]
    public async Task<IActionResult> Callback(CancellationToken cancellationToken)
    {
        var result = await googleAuthService.CompleteCallbackAsync(cancellationToken);
        var returnUrl = BuildFrontendUrl(result.ReturnUrl);
        if (!result.Succeeded)
        {
            return Redirect(QueryHelpers.AddQueryString(returnUrl, "authError", result.ErrorCode ?? "google_auth_failed"));
        }

        return Redirect(QueryHelpers.AddQueryString(returnUrl, "authSuccess", result.Operation?.ToString().ToLowerInvariant() ?? "google"));
    }

    private string BuildFrontendUrl(string returnUrl)
    {
        var safeReturnUrl = GoogleAuthService.TryGetSafeReturnUrl(returnUrl, out var validatedReturnUrl)
            ? validatedReturnUrl
            : "/";
        return frontendUrlResolver.GetFrontendUrl().TrimEnd('/') + safeReturnUrl;
    }

    private bool IsSameOriginRequest()
    {
        if (Request.Headers.Origin.Count > 0 && !IsSameOrigin(Request.Headers.Origin.ToString()))
        {
            return false;
        }

        return Request.Headers.Referer.Count == 0 || IsSameOrigin(Request.Headers.Referer.ToString());
    }

    private bool IsSameOrigin(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && string.Equals(uri.Scheme, Request.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.Host, Request.Host.Host, StringComparison.OrdinalIgnoreCase)
            && uri.Port == Request.Host.Port.GetValueOrDefault(uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? 443 : 80);
    }
}

public sealed class GoogleLinkRequest
{
    public string? CurrentPassword { get; set; }

    public string? ReturnUrl { get; set; }
}

public sealed record GoogleAuthorizationUrlResponse(string AuthorizationUrl);

public sealed record GoogleAuthStatusResponse(bool Linked, bool HasPassword, bool Reauthenticated);

public sealed class GoogleReauthRequest
{
    public string? ReturnUrl { get; set; }
}
