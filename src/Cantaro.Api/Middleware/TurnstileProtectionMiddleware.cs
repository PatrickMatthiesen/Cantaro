using System.Text.Json;
using Cantaro.Api.Configuration;
using Cantaro.Api.Services;
using Microsoft.Extensions.Options;

namespace Cantaro.Api.Middleware;

public sealed class TurnstileProtectionMiddleware(
    RequestDelegate next,
    IOptions<TurnstileOptions> options,
    ILogger<TurnstileProtectionMiddleware> logger)
{
    private static readonly IReadOnlyDictionary<string, string> ProtectedActions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["/api/register"] = "signup",
        ["/api/login"] = "login",
        ["/api/forgotPassword"] = "forgot_password",
        ["/api/resetPassword"] = "reset_password"
    };

    private readonly TurnstileOptions _options = options.Value;

    public async Task InvokeAsync(HttpContext context, ITurnstileValidator validator)
    {
        var path = context.Request.Path.Value?.TrimEnd('/') ?? string.Empty;
        if (!_options.IsEnabled
            || !HttpMethods.IsPost(context.Request.Method)
            || !ProtectedActions.TryGetValue(path, out var action))
        {
            await next(context);
            return;
        }

        string? token;
        try
        {
            token = await ReadTokenAsync(context.Request);
        }
        catch (JsonException)
        {
            token = null;
        }
        catch (InvalidDataException)
        {
            token = null;
        }
        catch (IOException)
        {
            token = null;
        }
        if (string.IsNullOrWhiteSpace(token) || !await validator.ValidateAsync(token, action, context.RequestAborted))
        {
            logger.LogInformation("Rejected {Path} because Turnstile validation failed.", context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "turnstile_required",
                message = "Complete the bot check and try again."
            });
            return;
        }

        await next(context);
    }

    private static async Task<string?> ReadTokenAsync(HttpRequest request)
    {
        if (request.ContentLength is > 65_536)
        {
            return null;
        }

        if (request.HasFormContentType)
        {
            request.EnableBuffering(4_096, 65_536);
            var form = await request.ReadFormAsync(request.HttpContext.RequestAborted);
            request.Body.Position = 0;
            return form["cf-turnstile-response"].FirstOrDefault() ?? form["turnstileToken"].FirstOrDefault();
        }

        if (request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) != true)
        {
            return null;
        }

        request.EnableBuffering(4_096, 65_536);
        using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: request.HttpContext.RequestAborted);
        request.Body.Position = 0;

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (string.Equals(property.Name, "turnstileToken", StringComparison.OrdinalIgnoreCase)
                || string.Equals(property.Name, "cf-turnstile-response", StringComparison.OrdinalIgnoreCase))
            {
                return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
            }
        }

        return null;
    }
}

public static class TurnstileProtectionMiddlewareExtensions
{
    public static IApplicationBuilder UseTurnstileProtection(this IApplicationBuilder app) =>
        app.UseMiddleware<TurnstileProtectionMiddleware>();
}
