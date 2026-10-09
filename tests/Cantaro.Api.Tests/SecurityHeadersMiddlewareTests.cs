using Cantaro.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class SecurityHeadersMiddlewareTests
{
    [Fact]
    public async Task AddsSecurityHeadersAndReportOnlyPolicy()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        var middleware = CreateMiddleware("Production");

        await middleware.InvokeAsync(context);

        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"]);
        Assert.Equal("DENY", context.Response.Headers["X-Frame-Options"]);
        Assert.Equal("strict-origin-when-cross-origin", context.Response.Headers["Referrer-Policy"]);
        Assert.Equal("camera=(), geolocation=(), microphone=()", context.Response.Headers["Permissions-Policy"]);
        Assert.Equal("max-age=31536000", context.Response.Headers["Strict-Transport-Security"]);

        var policy = Assert.Single(context.Response.Headers["Content-Security-Policy-Report-Only"]);
        Assert.Contains("object-src 'none'", policy);
        Assert.Contains("frame-ancestors 'none'", policy);
        Assert.Contains("img-src 'self' data: blob: https:", policy);
        Assert.Contains("media-src 'self' blob: https:", policy);
    }

    [Theory]
    [InlineData("Development", "https")]
    [InlineData("Production", "http")]
    public async Task AddsHstsOnlyForProductionHttps(string environmentName, string scheme)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = scheme;

        await CreateMiddleware(environmentName).InvokeAsync(context);

        Assert.False(context.Response.Headers.ContainsKey("Strict-Transport-Security"));
    }

    private static SecurityHeadersMiddleware CreateMiddleware(string environmentName) => new(
        _ => Task.CompletedTask,
        new TestHostEnvironment(environmentName));

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Cantaro.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
