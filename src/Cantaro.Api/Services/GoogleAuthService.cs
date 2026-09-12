using System.Security.Claims;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Cantaro.Api.Configuration;
using Cantaro.Api.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using AuthenticationOptions = Cantaro.Api.Configuration.AuthenticationOptions;

namespace Cantaro.Api.Services;

public sealed class GoogleAuthService
{
    public const string GoogleReauthenticatedAtClaim = "cantaro:google_reauthenticated_at";

    private const string OperationKey = "cantaro.google.operation";
    private const string ReturnUrlKey = "cantaro.google.return_url";
    private const string GrantKey = "cantaro.google.grant";
    private const string GrantPurpose = "Cantaro.GoogleAuth.Grant.v1";
    private const string Provider = GoogleDefaults.AuthenticationScheme;
    private const string CompletionPath = "/api/auth/google/callback";
    private static readonly TimeSpan GrantLifetime = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<string, long> ConsumedGrants = new(StringComparer.Ordinal);

    private readonly UserManager<User> _userManager;
    private readonly SignInManager<User> _signInManager;
    private readonly AuthenticationOptions _options;
    private readonly IDataProtector _grantProtector;
    private readonly TimeProvider _timeProvider;

    public GoogleAuthService(
        UserManager<User> userManager,
        SignInManager<User> signInManager,
        IOptions<AuthenticationOptions> options,
        IDataProtectionProvider dataProtectionProvider,
        TimeProvider timeProvider)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _options = options.Value;
        _grantProtector = dataProtectionProvider.CreateProtector(GrantPurpose);
        _timeProvider = timeProvider;
    }

    public bool GoogleEnabled => _options.GoogleEnabled;

    public bool LocalLoginEnabled => _options.LocalLoginEnabled;

    public AuthenticationMethodsResponse GetMethods() => new(LocalLoginEnabled, GoogleEnabled);

    public AuthenticationProperties CreateLoginChallenge(string returnUrl)
    {
        var properties = _signInManager.ConfigureExternalAuthenticationProperties(
            Provider,
            CompletionPath);
        properties.Items[OperationKey] = GoogleAuthOperation.Login.ToString();
        properties.Items[ReturnUrlKey] = returnUrl;
        return properties;
    }

    public async Task<GoogleAuthStartResult> StartLinkAsync(
        User user,
        string? currentPassword,
        string returnUrl,
        CancellationToken cancellationToken)
    {
        if (!GoogleEnabled)
        {
            return GoogleAuthStartResult.Failure("google_disabled", "Google sign-in is not enabled.");
        }

        if (!string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            if (string.IsNullOrWhiteSpace(currentPassword))
            {
                return GoogleAuthStartResult.Failure(
                    "fresh_auth_required",
                    "Enter your current Cantaro password before linking Google.");
            }

            if (!await _userManager.CheckPasswordAsync(user, currentPassword))
            {
                return GoogleAuthStartResult.Failure(
                    "invalid_current_password",
                    "The current Cantaro password is incorrect.");
            }
        }
        else
        {
            return GoogleAuthStartResult.Failure(
                "google_reauth_required",
                "This account uses Google sign-in. Use Google reauthentication instead of linking a new identity.");
        }

        var grant = CreateGrant(user.Id, GoogleAuthOperation.Link, returnUrl);
        var authorizationUrl = QueryHelpers.AddQueryString(
            "/api/auth/google/link",
            "token",
            grant);
        return GoogleAuthStartResult.Success(authorizationUrl);
    }

    public AuthenticationProperties CreateLinkChallenge(string grant)
    {
        var properties = _signInManager.ConfigureExternalAuthenticationProperties(
            Provider,
            CompletionPath);
        properties.Items[OperationKey] = GoogleAuthOperation.Link.ToString();
        properties.Items[GrantKey] = grant;
        properties.Items[GoogleChallengeProperties.PromptParameterKey] = "select_account";
        return properties;
    }

    public async Task<GoogleAuthStartResult> StartReauthenticationAsync(
        User user,
        string returnUrl,
        CancellationToken cancellationToken)
    {
        if (!GoogleEnabled)
        {
            return GoogleAuthStartResult.Failure("google_disabled", "Google sign-in is not enabled.");
        }

        var hasGoogleLogin = (await _userManager.GetLoginsAsync(user))
            .Any(login => string.Equals(login.LoginProvider, Provider, StringComparison.Ordinal));
        if (!hasGoogleLogin)
        {
            return GoogleAuthStartResult.Failure(
                "google_link_required",
                "Link a Google account before using Google reauthentication.");
        }

        var grant = CreateGrant(user.Id, GoogleAuthOperation.Reauthenticate, returnUrl);
        var authorizationUrl = QueryHelpers.AddQueryString(
            "/api/auth/google/reauth",
            "token",
            grant);
        return GoogleAuthStartResult.Success(authorizationUrl);
    }

    public AuthenticationProperties CreateReauthenticationChallenge(string grant)
    {
        var properties = _signInManager.ConfigureExternalAuthenticationProperties(
            Provider,
            CompletionPath);
        properties.Items[OperationKey] = GoogleAuthOperation.Reauthenticate.ToString();
        properties.Items[GrantKey] = grant;
        properties.Items[GoogleChallengeProperties.PromptParameterKey] = "select_account";
        return properties;
    }

    public async Task<GoogleAuthCallbackResult> CompleteCallbackAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!GoogleEnabled)
            {
                return GoogleAuthCallbackResult.Failure("google_disabled", "Google sign-in is not enabled.");
            }

            var externalLogin = await _signInManager.GetExternalLoginInfoAsync();
            if (externalLogin is null)
            {
                return GoogleAuthCallbackResult.Failure("google_callback_invalid", "The Google sign-in response could not be validated.");
            }

            if (!string.Equals(externalLogin.LoginProvider, Provider, StringComparison.Ordinal))
            {
                return GoogleAuthCallbackResult.Failure("google_provider_invalid", "The external sign-in provider is not supported.");
            }

            if (!TryReadGoogleIdentity(externalLogin, out var email, out var errorCode))
            {
                return GoogleAuthCallbackResult.Failure(errorCode!, "Google must return a verified email address.");
            }

            var properties = externalLogin.AuthenticationProperties;
            var operation = ReadOperation(properties);
            var returnUrl = ReadReturnUrl(properties);
            if (operation is null)
            {
                return GoogleAuthCallbackResult.Failure("google_callback_invalid", "The Google sign-in request could not be identified.", returnUrl);
            }

            if (operation is GoogleAuthOperation.Link or GoogleAuthOperation.Reauthenticate)
            {
                if (!TryReadGrant(properties, operation.Value, out var grant))
                {
                    return GoogleAuthCallbackResult.Failure("google_callback_invalid", "The Google sign-in request has expired.", returnUrl);
                }

                var currentUser = await _userManager.GetUserAsync(_signInManager.Context.User);
                if (currentUser is null || currentUser.Id != grant.UserId)
                {
                    return GoogleAuthCallbackResult.Failure("fresh_auth_required", "Sign in to Cantaro again before continuing.", grant.ReturnUrl);
                }

                if (operation == GoogleAuthOperation.Reauthenticate)
                {
                    return await CompleteReauthenticationAsync(currentUser, externalLogin, grant.ReturnUrl, cancellationToken);
                }

                return await CompleteLinkAsync(currentUser, externalLogin, grant.ReturnUrl, cancellationToken);
            }

            return await CompleteLoginAsync(externalLogin, email, returnUrl, cancellationToken);
        }
        finally
        {
            await _signInManager.Context.SignOutAsync(IdentityConstants.ExternalScheme);
        }
    }

    public static bool TryGetSafeReturnUrl(string? returnUrl, out string safeReturnUrl)
    {
        var candidate = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl.Trim();
        // Validate URL path syntax directly: Unix root paths are also absolute file URIs.
        var isSafe = candidate.Length <= 2048
            && candidate.StartsWith("/", StringComparison.Ordinal)
            && !candidate.StartsWith("//", StringComparison.Ordinal)
            && !candidate.StartsWith(@"/\\", StringComparison.Ordinal)
            && !candidate.Contains('\\', StringComparison.Ordinal)
            && !candidate.Contains("://", StringComparison.Ordinal)
            && !candidate.Any(char.IsControl);

        safeReturnUrl = isSafe ? candidate : "/";
        return isSafe;
    }

    public static string AddQuery(string returnUrl, string name, string value) =>
        QueryHelpers.AddQueryString(returnUrl, name, value);

    private async Task<GoogleAuthCallbackResult> CompleteLoginAsync(
        ExternalLoginInfo externalLogin,
        string email,
        string returnUrl,
        CancellationToken cancellationToken)
    {
        var existingLogin = await _userManager.FindByLoginAsync(Provider, externalLogin.ProviderKey);
        if (existingLogin is not null)
        {
            await _signInManager.SignInAsync(existingLogin, isPersistent: false, authenticationMethod: Provider);
            return GoogleAuthCallbackResult.Success(GoogleAuthOperation.Login, returnUrl);
        }

        var existingEmail = await _userManager.FindByEmailAsync(email);
        if (existingEmail is not null)
        {
            return GoogleAuthCallbackResult.Failure(
                "google_email_collision",
                "A Cantaro account already exists for this Google email. Sign in locally, then link Google from account settings.",
                returnUrl);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var user = new User
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        var createResult = await _userManager.CreateAsync(user);
        if (!createResult.Succeeded)
        {
            return GoogleAuthCallbackResult.Failure("google_account_create_failed", "Cantaro could not create the account.", returnUrl);
        }

        var loginResult = await _userManager.AddLoginAsync(user, externalLogin);
        if (!loginResult.Succeeded)
        {
            await _userManager.DeleteAsync(user);
            return GoogleAuthCallbackResult.Failure("google_account_link_failed", "Cantaro could not link the Google account.", returnUrl);
        }

        await _signInManager.SignInAsync(user, isPersistent: false, authenticationMethod: Provider);
        return GoogleAuthCallbackResult.Success(GoogleAuthOperation.Login, returnUrl);
    }

    private async Task<GoogleAuthCallbackResult> CompleteLinkAsync(
        User currentUser,
        ExternalLoginInfo externalLogin,
        string returnUrl,
        CancellationToken cancellationToken)
    {
        var linkedUser = await _userManager.FindByLoginAsync(Provider, externalLogin.ProviderKey);
        if (linkedUser is not null && linkedUser.Id != currentUser.Id)
        {
            return GoogleAuthCallbackResult.Failure("google_login_already_linked", "This Google account is already linked to another Cantaro account.", returnUrl);
        }

        if (linkedUser is null)
        {
            if (currentUser.PasswordHash is null)
            {
                return GoogleAuthCallbackResult.Failure(
                    "google_reauth_required",
                    "This Google-only account cannot link a new Google identity.",
                    returnUrl);
            }

            var emailOwner = await _userManager.FindByEmailAsync(externalLogin.Principal.FindFirstValue(ClaimTypes.Email)!);
            if (emailOwner is not null && emailOwner.Id != currentUser.Id)
            {
                return GoogleAuthCallbackResult.Failure("google_email_collision", "This Google email belongs to another Cantaro account. Sign in to that account before using this Google identity.", returnUrl);
            }

            var result = await _userManager.AddLoginAsync(currentUser, externalLogin);
            if (!result.Succeeded)
            {
                return GoogleAuthCallbackResult.Failure("google_account_link_failed", "Cantaro could not link the Google account.", returnUrl);
            }
        }

        await _signInManager.RefreshSignInAsync(currentUser);
        return GoogleAuthCallbackResult.Success(GoogleAuthOperation.Link, returnUrl);
    }

    private async Task<GoogleAuthCallbackResult> CompleteReauthenticationAsync(
        User currentUser,
        ExternalLoginInfo externalLogin,
        string returnUrl,
        CancellationToken cancellationToken)
    {
        var linkedUser = await _userManager.FindByLoginAsync(Provider, externalLogin.ProviderKey);
        if (linkedUser?.Id != currentUser.Id)
        {
            return GoogleAuthCallbackResult.Failure("google_reauth_mismatch", "The Google account does not match the signed-in Cantaro account.", returnUrl);
        }

        var timestamp = _timeProvider.GetUtcNow().ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        await _signInManager.SignInWithClaimsAsync(
            currentUser,
            isPersistent: false,
            new[] { new Claim(GoogleReauthenticatedAtClaim, timestamp) });
        return GoogleAuthCallbackResult.Reauthenticated(returnUrl, timestamp);
    }

    private string CreateGrant(int userId, GoogleAuthOperation operation, string returnUrl)
    {
        var payload = JsonSerializer.Serialize(new GoogleAuthGrant(
            userId,
            operation,
            returnUrl,
            _timeProvider.GetUtcNow().Add(GrantLifetime).ToUnixTimeSeconds()));
        var protectedPayload = _grantProtector.Protect(Encoding.UTF8.GetBytes(payload));
        return WebEncoders.Base64UrlEncode(protectedPayload);
    }

    private bool TryReadGrant(
        AuthenticationProperties? properties,
        GoogleAuthOperation expectedOperation,
        out GoogleAuthGrant grant)
    {
        grant = default!;
        if (properties is null || !properties.Items.TryGetValue(GrantKey, out var encoded) || string.IsNullOrWhiteSpace(encoded))
        {
            return false;
        }

        try
        {
            var payload = Encoding.UTF8.GetString(_grantProtector.Unprotect(WebEncoders.Base64UrlDecode(encoded)));
            grant = JsonSerializer.Deserialize<GoogleAuthGrant>(payload)!;
            if (grant is null
                || grant.Operation != expectedOperation
                || grant.ExpiresAtUnixSeconds < _timeProvider.GetUtcNow().ToUnixTimeSeconds()
                || !TryGetSafeReturnUrl(grant.ReturnUrl, out _))
            {
                return false;
            }

            RemoveExpiredConsumedGrants();
            return ConsumedGrants.TryAdd(encoded, grant.ExpiresAtUnixSeconds);
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static void RemoveExpiredConsumedGrants()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var item in ConsumedGrants)
        {
            if (item.Value < now)
            {
                ConsumedGrants.TryRemove(item.Key, out _);
            }
        }
    }

    private static GoogleAuthOperation? ReadOperation(AuthenticationProperties? properties)
    {
        return properties?.Items.TryGetValue(OperationKey, out var value) == true
            && Enum.TryParse<GoogleAuthOperation>(value, out var operation)
            ? operation
            : null;
    }

    private static string ReadReturnUrl(AuthenticationProperties? properties)
    {
        return properties?.Items.TryGetValue(ReturnUrlKey, out var returnUrl) == true
            && TryGetSafeReturnUrl(returnUrl, out var safeReturnUrl)
            ? safeReturnUrl
            : "/";
    }

    private static bool TryReadGoogleIdentity(ExternalLoginInfo login, out string email, out string? errorCode)
    {
        email = login.Principal.FindFirstValue(ClaimTypes.Email)?.Trim() ?? string.Empty;
        var verified = login.Principal.FindFirstValue("email_verified")
            ?? login.Principal.FindFirstValue("urn:google:email_verified")
            ?? login.Principal.FindFirstValue("verified_email")
            ?? login.Principal.FindFirstValue("urn:google:verified_email");
        if (string.IsNullOrWhiteSpace(login.ProviderKey))
        {
            errorCode = "google_subject_missing";
            return false;
        }

        if (string.IsNullOrWhiteSpace(email) || !string.Equals(verified, "true", StringComparison.OrdinalIgnoreCase))
        {
            errorCode = "google_email_unverified";
            return false;
        }

        errorCode = null;
        return true;
    }

    private sealed record GoogleAuthGrant(
        int UserId,
        GoogleAuthOperation Operation,
        string ReturnUrl,
        long ExpiresAtUnixSeconds);
}

public enum GoogleAuthOperation
{
    Login,
    Link,
    Reauthenticate
}

public sealed record AuthenticationMethodsResponse(bool LocalLoginEnabled, bool GoogleEnabled);

public sealed record GoogleAuthStartResult(bool Succeeded, string? AuthorizationUrl, string? ErrorCode, string? ErrorMessage)
{
    public static GoogleAuthStartResult Success(string authorizationUrl) => new(true, authorizationUrl, null, null);

    public static GoogleAuthStartResult Failure(string errorCode, string errorMessage) => new(false, null, errorCode, errorMessage);
}

public sealed record GoogleAuthCallbackResult(
    bool Succeeded,
    GoogleAuthOperation? Operation,
    string ReturnUrl,
    string? ErrorCode,
    string? ErrorMessage,
    string? ReauthenticatedAt)
{
    public static GoogleAuthCallbackResult Success(GoogleAuthOperation operation, string returnUrl) => new(true, operation, returnUrl, null, null, null);

    public static GoogleAuthCallbackResult Reauthenticated(string returnUrl, string timestamp) => new(true, GoogleAuthOperation.Reauthenticate, returnUrl, null, null, timestamp);

    public static GoogleAuthCallbackResult Failure(string errorCode, string errorMessage, string returnUrl = "/") => new(false, null, returnUrl, errorCode, errorMessage, null);
}
