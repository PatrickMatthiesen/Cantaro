using Cantaro.Api.Configuration;
using Cantaro.Api.Services;
using Xunit;

namespace Cantaro.Api.Tests;

public class ExtensionAuthSigningKeyResolverTests
{
    [Fact]
    public void ResolveSigningKey_ReturnsTrimmedConfiguredKey()
    {
        var options = new ExtensionAuthOptions
        {
            JwtSigningKey = "  Cantaro.Test.Extension.Auth.Signing.Key.2026.05.06  "
        };

        var signingKey = ExtensionAuthSigningKeyResolver.ResolveSigningKey(options);

        Assert.Equal("Cantaro.Test.Extension.Auth.Signing.Key.2026.05.06", signingKey);
    }

    [Fact]
    public void ResolveSigningKey_ThrowsWhenMissing()
    {
        var options = new ExtensionAuthOptions();

        var error = Assert.Throws<InvalidOperationException>(() => ExtensionAuthSigningKeyResolver.ResolveSigningKey(options));

        Assert.Equal(
            "ExtensionAuth:JwtSigningKey must be configured. Are you missing a configuration value or environment variable?",
            error.Message);
    }
}
