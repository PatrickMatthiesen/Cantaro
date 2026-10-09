using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace Cantaro.Api.Middleware;

public sealed class SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment environment)
{
    private const string ContentSecurityPolicy =
        "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; " +
        "form-action 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: blob: https:; font-src 'self' data:; connect-src 'self'; " +
        "media-src 'self' blob: https:; frame-src 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "camera=(), geolocation=(), microphone=()";
        headers["Content-Security-Policy-Report-Only"] = ContentSecurityPolicy;

        if (environment.IsProduction() && context.Request.IsHttps)
        {
            headers["Strict-Transport-Security"] = "max-age=31536000";
        }

        return next(context);
    }
}
