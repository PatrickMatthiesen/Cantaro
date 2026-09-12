using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;

namespace Cantaro.Api.Configuration;

public static class GoogleAuthenticationConfiguration
{
    public static AuthenticationBuilder AddCantaroGoogle(
        this AuthenticationBuilder authentication,
        AuthenticationOptions options)
    {
        if (!options.GoogleEnabled)
        {
            return authentication;
        }

        return authentication.AddGoogle(GoogleDefaults.AuthenticationScheme, google =>
        {
            google.ClientId = options.Google.ClientId;
            google.ClientSecret = options.Google.ClientSecret;
            google.CallbackPath = options.Google.CallbackPath;
            google.SignInScheme = Microsoft.AspNetCore.Identity.IdentityConstants.ExternalScheme;

            // Google’s current OpenID Connect userinfo uses email_verified. The
            // older v2 userinfo response uses verified_email, so normalize both
            // to the claim consumed by GoogleAuthService.
            google.ClaimActions.MapCustomJson("email_verified", ReadEmailVerified);
            google.Events.OnRemoteFailure = context =>
            {
                string? requestedReturn = null;
                context.Properties?.Items.TryGetValue("cantaro.google.return_url", out requestedReturn);
                Services.GoogleAuthService.TryGetSafeReturnUrl(requestedReturn, out var returnUrl);
                var frontend = context.HttpContext.RequestServices.GetRequiredService<Services.IFrontendUrlResolver>().GetFrontendUrl();
                context.Response.Redirect(Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(
                    frontend.TrimEnd('/') + returnUrl, "authError", "google_auth_failed"));
                context.HandleResponse();
                return Task.CompletedTask;
            };
        });
    }

    private static string? ReadEmailVerified(JsonElement user)
    {
        if (TryReadBoolean(user, "email_verified", out var currentValue)
            || TryReadBoolean(user, "verified_email", out currentValue))
        {
            return currentValue ? "true" : "false";
        }

        return null;
    }

    private static bool TryReadBoolean(JsonElement user, string propertyName, out bool value)
    {
        value = false;
        if (!user.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = property.GetBoolean();
            return true;
        }

        return property.ValueKind == JsonValueKind.String
            && bool.TryParse(property.GetString(), out value);
    }
}
