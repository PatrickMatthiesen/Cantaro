using Cantaro.Api.Configuration;
using Xunit;
using Cantaro.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Cantaro.Api.Tests;

public sealed class AuthenticationModeGuardTests
{
    [Theory]
    [InlineData("/api/register")]
    [InlineData("/api/login")]
    [InlineData("/api/refresh")]
    [InlineData("/api/forgotPassword")]
    [InlineData("/api/resetPassword")]
    [InlineData("/api/confirmEmail")]
    [InlineData("/api/resendConfirmationEmail")]
    [InlineData("/api/manage/2fa")]
    [InlineData("/api/manage/info")]
    [InlineData("/api/profile/change-password")]
    public void GoogleOnly_BlocksLocalAuthenticationEndpoints(string path)
    {
        Assert.True(AuthenticationModeGuardMiddleware.IsLocalAuthenticationEndpoint(path, HttpMethods.Post));
    }

    [Theory]
    [InlineData("/api/profile")]
    [InlineData("/api/logout")]
    [InlineData("/api/auth/extension/token")]
    [InlineData("/api/auth/google/login")]
    public void GoogleOnly_AllowsProfileLogoutExtensionAndGoogleRoutes(string path)
    {
        Assert.False(AuthenticationModeGuardMiddleware.IsLocalAuthenticationEndpoint(path, HttpMethods.Post));
    }

    [Fact]
    public async Task Middleware_ReturnsActionableErrorForBlockedPath()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/login";
        context.Request.Method = HttpMethods.Post;
        await using var body = new MemoryStream();
        context.Response.Body = body;

        var called = false;
        var middleware = new AuthenticationModeGuardMiddleware(
            _ =>
            {
                called = true;
                return Task.CompletedTask;
            },
            Options.Create(new AuthenticationOptions { Mode = AuthenticationMode.GoogleOnly }));

        await middleware.InvokeAsync(context);

        Assert.False(called);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        body.Position = 0;
        using var reader = new StreamReader(body);
        Assert.Contains("local_authentication_disabled", await reader.ReadToEndAsync());
    }
}
