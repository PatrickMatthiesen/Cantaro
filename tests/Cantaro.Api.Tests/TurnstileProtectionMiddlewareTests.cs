using System.Text;
using Cantaro.Api.Configuration;
using Cantaro.Api.Middleware;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class TurnstileProtectionMiddlewareTests
{
    [Fact]
    public async Task EnabledProtectionRejectsRequestWithoutToken()
    {
        var context = CreateContext("{\"email\":\"user@example.com\",\"password\":\"password\"}");
        var middleware = CreateMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context, new RecordingValidator(false));

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task ValidTokenPassesRequestBodyToIdentityEndpoint()
    {
        var context = CreateContext("{\"email\":\"user@example.com\",\"password\":\"password\",\"turnstileToken\":\"token\"}");
        string? downstreamBody = null;
        var middleware = CreateMiddleware(async httpContext =>
        {
            using var reader = new StreamReader(httpContext.Request.Body);
            downstreamBody = await reader.ReadToEndAsync();
        });

        await middleware.InvokeAsync(context, new RecordingValidator(true));

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Contains("turnstileToken", downstreamBody, StringComparison.Ordinal);
    }

    private static TurnstileProtectionMiddleware CreateMiddleware(RequestDelegate next) =>
        new(next, Options.Create(new TurnstileOptions
        {
            SiteKey = "site-key",
            Secret = "secret",
            AllowedHostnames = ["cantaro.example"],
        }), NullLogger<TurnstileProtectionMiddleware>.Instance);

    private static DefaultHttpContext CreateContext(string body)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/login";
        context.Request.ContentType = "application/json";
        var bytes = Encoding.UTF8.GetBytes(body);
        context.Request.ContentLength = bytes.Length;
        context.Request.Body = new MemoryStream(bytes);
        return context;
    }

    private sealed class RecordingValidator(bool result) : ITurnstileValidator
    {
        public Task<bool> ValidateAsync(string token, string action, CancellationToken cancellationToken) => Task.FromResult(result);
    }
}
