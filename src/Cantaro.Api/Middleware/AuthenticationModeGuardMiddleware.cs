using System.Text.Json;
using Cantaro.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Cantaro.Api.Middleware;

public sealed class AuthenticationModeGuardMiddleware(
    RequestDelegate next,
    IOptions<AuthenticationOptions> options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (options.Value.Mode == AuthenticationMode.GoogleOnly
            && IsLocalAuthenticationEndpoint(context.Request.Path, context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = "local_authentication_disabled",
                message = "Local password authentication is disabled for this server. Use Google sign-in."
            }));
            return;
        }

        await next(context);
    }

    internal static bool IsLocalAuthenticationEndpoint(PathString path, string method)
    {
        path = new PathString(path.Value?.TrimEnd('/'));
        if (path.Equals("/api/register", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/login", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/refresh", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/forgotPassword", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/resetPassword", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/confirmEmail", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/resendConfirmationEmail", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/api/manage/2fa", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/api/manage/info", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/profile/change-password", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}

public static class AuthenticationModeGuardApplicationBuilderExtensions
{
    public static IApplicationBuilder UseAuthenticationModeGuard(this IApplicationBuilder app) =>
        app.UseMiddleware<AuthenticationModeGuardMiddleware>();
}
