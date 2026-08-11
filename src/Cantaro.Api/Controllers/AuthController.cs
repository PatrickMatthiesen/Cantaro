using Cantaro.Api.Models;
using Cantaro.Api.Configuration;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace Cantaro.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private const string CantaroFirefoxExtensionId = "cantaro@bmstack.net";
    private readonly UserManager<User> _userManager;
    private readonly ExtensionAuthService _extensionAuthService;
    private readonly IFrontendUrlResolver _frontendUrlResolver;
    private readonly ExtensionAuthOptions _extensionAuthOptions;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        UserManager<User> userManager,
        ExtensionAuthService extensionAuthService,
        IFrontendUrlResolver frontendUrlResolver,
        IOptions<ExtensionAuthOptions> extensionAuthOptions,
        ILogger<AuthController> logger)
    {
        _userManager = userManager;
        _extensionAuthService = extensionAuthService;
        _frontendUrlResolver = frontendUrlResolver;
        _extensionAuthOptions = extensionAuthOptions.Value;
        _logger = logger;
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> GetCurrentUser()
    {
        var user = await _userManager.GetUserAsync(User);

        if (user == null)
        {
            return NotFound("User not found");
        }

        var dto = new UserDto
        {
            Id = user.Id,
            Email = user.Email ?? "Could not retrieve email",
            CreatedAt = user.CreatedAt
        };

        return Ok(dto);
    }

    [AllowAnonymous]
    [HttpGet("extension/authorize")]
    public async Task<ActionResult> AuthorizeExtension(
        [FromQuery(Name = "response_type")] string? responseType,
        [FromQuery(Name = "client_id")] string? clientId,
        [FromQuery(Name = "redirect_uri")] string? redirectUri,
        [FromQuery(Name = "state")] string? state,
        [FromQuery(Name = "code_challenge")] string? codeChallenge,
        [FromQuery(Name = "code_challenge_method")] string? codeChallengeMethod,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(responseType, "code", StringComparison.Ordinal))
        {
            return BadRequest(new
            {
                error = "unsupported_response_type",
                error_description = "Only response_type=code is supported for the browser extension auth flow."
            });
        }

        if (string.IsNullOrWhiteSpace(clientId)
            || string.IsNullOrWhiteSpace(redirectUri)
            || string.IsNullOrWhiteSpace(state)
            || string.IsNullOrWhiteSpace(codeChallenge))
        {
            return BadRequest(new
            {
                error = "invalid_request",
                error_description = "client_id, redirect_uri, state, and code_challenge are required."
            });
        }

        if (!IsValidExtensionRedirectUri(clientId, redirectUri))
        {
            return BadRequest(new
            {
                error = "invalid_request",
                error_description = "The redirect_uri must be the extension redirect URI returned by browser.identity.getRedirectURL()."
            });
        }

        if (!string.Equals(codeChallengeMethod, "S256", StringComparison.OrdinalIgnoreCase))
        {
            return RedirectExtensionError(redirectUri, "invalid_request", state, "Only S256 PKCE challenges are supported.");
        }

        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            var frontendUrl = _frontendUrlResolver.GetFrontendUrl();
            var loginUrl = QueryHelpers.AddQueryString(
                $"{frontendUrl}{_extensionAuthOptions.FrontendLoginPath}",
                "returnTo",
                Request.GetDisplayUrl());

            return Redirect(loginUrl);
        }

        var authorizationCode = await _extensionAuthService.CreateAuthorizationCodeAsync(
            user,
            clientId,
            redirectUri,
            codeChallenge,
            cancellationToken);

        _logger.LogInformation(
            "Issued extension authorization code for user {UserId} and client {ClientId}.",
            user.Id,
            clientId);

        return Redirect(QueryHelpers.AddQueryString(redirectUri, new Dictionary<string, string?>
        {
            ["code"] = authorizationCode,
            ["state"] = state
        }));
    }

    [AllowAnonymous]
    [Consumes("application/x-www-form-urlencoded")]
    [HttpPost("extension/token")]
    public async Task<ActionResult<ExtensionTokenResponse>> ExchangeExtensionToken(
        [FromForm] ExtensionTokenRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.Equals(request.GrantType, "authorization_code", StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(request.Code)
                    || string.IsNullOrWhiteSpace(request.ClientId)
                    || string.IsNullOrWhiteSpace(request.RedirectUri)
                    || string.IsNullOrWhiteSpace(request.CodeVerifier))
                {
                    return BadRequest(new
                    {
                        error = "invalid_request",
                        error_description = "code, client_id, redirect_uri, and code_verifier are required for an authorization_code exchange."
                    });
                }

                if (!IsValidExtensionRedirectUri(request.ClientId, request.RedirectUri))
                {
                    return BadRequest(new
                    {
                        error = "invalid_request",
                        error_description = "The redirect_uri must match the original extension redirect URI."
                    });
                }

                var tokenResponse = await _extensionAuthService.ExchangeAuthorizationCodeAsync(
                    request.Code,
                    request.ClientId,
                    request.RedirectUri,
                    request.CodeVerifier,
                    cancellationToken);

                return Ok(tokenResponse);
            }

            if (string.Equals(request.GrantType, "refresh_token", StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(request.RefreshToken) || string.IsNullOrWhiteSpace(request.ClientId))
                {
                    return BadRequest(new
                    {
                        error = "invalid_request",
                        error_description = "refresh_token and client_id are required for a refresh_token exchange."
                    });
                }

                var tokenResponse = await _extensionAuthService.RefreshAccessTokenAsync(
                    request.RefreshToken,
                    request.ClientId,
                    cancellationToken);

                return Ok(tokenResponse);
            }

            return BadRequest(new
            {
                error = "unsupported_grant_type",
                error_description = "Only authorization_code and refresh_token grants are supported."
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Extension token exchange rejected for grant {GrantType}.", request.GrantType);
            return BadRequest(new
            {
                error = "invalid_grant",
                error_description = ex.Message
            });
        }
    }

    [AllowAnonymous]
    [Consumes("application/x-www-form-urlencoded")]
    [HttpPost("extension/revoke")]
    public async Task<IActionResult> RevokeExtensionToken(
        [FromForm] ExtensionTokenRevokeRequest request,
        CancellationToken cancellationToken)
    {
        await _extensionAuthService.RevokeRefreshTokenAsync(request.RefreshToken, request.ClientId, cancellationToken);
        return NoContent();
    }

    internal static bool IsValidExtensionRedirectUri(string clientId, string redirectUri)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(redirectUri))
        {
            return false;
        }

        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || !string.Equals(uri.AbsolutePath, "/cantaro-auth", StringComparison.Ordinal)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        if (string.Equals(uri.Host, $"{clientId}.chromiumapp.org", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.Equals(clientId, CantaroFirefoxExtensionId, StringComparison.Ordinal))
        {
            return false;
        }

        var firefoxClientHash = Convert.ToHexStringLower(
            SHA1.HashData(Encoding.UTF8.GetBytes(clientId)));
        return string.Equals(
            uri.Host,
            $"{firefoxClientHash}.extensions.allizom.org",
            StringComparison.OrdinalIgnoreCase);
    }

    private static RedirectResult RedirectExtensionError(string redirectUri, string error, string? state, string? description = null)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["error"] = error,
            ["state"] = state
        };

        if (!string.IsNullOrWhiteSpace(description))
        {
            parameters["error_description"] = description;
        }

        return new RedirectResult(QueryHelpers.AddQueryString(redirectUri, parameters));
    }
}
