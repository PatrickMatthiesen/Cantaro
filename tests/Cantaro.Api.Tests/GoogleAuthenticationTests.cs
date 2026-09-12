using Cantaro.Api.Configuration;
using Xunit;
using Cantaro.Api.Services;

namespace Cantaro.Api.Tests;

public sealed class GoogleAuthenticationTests
{
    [Fact]
    public void LocalMode_DisablesGoogleAndAllowsLocalLogin()
    {
        var options = new AuthenticationOptions();

        Assert.True(options.LocalLoginEnabled);
        Assert.False(options.GoogleEnabled);
        Assert.True(new AuthenticationOptionsValidator().Validate(null, options).Succeeded);
    }

    [Fact]
    public void GoogleOnlyMode_RequiresGoogleCredentials()
    {
        var options = new AuthenticationOptions { Mode = AuthenticationMode.GoogleOnly };

        var validation = new AuthenticationOptionsValidator().Validate(null, options);

        Assert.False(validation.Succeeded);
        Assert.Contains("Authentication:Google:ClientId", validation.FailureMessage);
        Assert.Contains("Authentication:Google:ClientSecret", validation.FailureMessage);
    }

    [Fact]
    public void BothMode_RequiresSafeCallbackPath()
    {
        var options = new AuthenticationOptions
        {
            Mode = AuthenticationMode.Both,
            Google = new GoogleAuthenticationOptions
            {
                ClientId = "client-id",
                ClientSecret = "client-secret",
                CallbackPath = "https://attacker.example/callback"
            }
        };

        var validation = new AuthenticationOptionsValidator().Validate(null, options);

        Assert.False(validation.Succeeded);
        Assert.Contains("CallbackPath", validation.FailureMessage);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/settings")]
    [InlineData("/extension-auth?returnTo=%2Fapi%2Fauth%2Fextension%2Fauthorize")]
    [InlineData("/extension-auth?returnTo=https%3A%2F%2Fcantaro.example%2Fapi%2Fauth%2Fextension%2Fauthorize%3Fstate%3Dabc")]
    public void SafeReturnUrl_AcceptsLocalRoutes(string returnUrl)
    {
        Assert.True(GoogleAuthService.TryGetSafeReturnUrl(returnUrl, out var safeReturnUrl));
        Assert.Equal(returnUrl, safeReturnUrl);
    }

    [Theory]
    [InlineData("https://attacker.example/")]
    [InlineData("//attacker.example/")]
    [InlineData(@"/\\attacker.example")]
    [InlineData("/settings://attacker.example")]
    public void SafeReturnUrl_RejectsExternalOrAmbiguousRoutes(string returnUrl)
    {
        Assert.False(GoogleAuthService.TryGetSafeReturnUrl(returnUrl, out _));
    }
}
