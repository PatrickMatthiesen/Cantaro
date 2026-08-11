using Cantaro.Api.Controllers;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class ExtensionRedirectUriTests
{
    [Fact]
    public void AcceptsChromiumIdentityRedirect()
    {
        const string clientId = "abcdefghijklmnopqrstuvwxyzabcdef";

        Assert.True(AuthController.IsValidExtensionRedirectUri(
            clientId,
            $"https://{clientId}.chromiumapp.org/cantaro-auth"));
    }

    [Fact]
    public void RejectsAnotherFirefoxAddonsValidIdentityRedirect()
    {
        Assert.False(AuthController.IsValidExtensionRedirectUri(
            "identity@mozilla.org",
            "https://35b64b676900f491c00e7f618d43f7040e88422e.extensions.allizom.org/cantaro-auth"));
    }

    [Fact]
    public void AcceptsCantaroFirefoxIdentityRedirect()
    {
        Assert.True(AuthController.IsValidExtensionRedirectUri(
            "cantaro@bmstack.net",
            "https://725d82360dfa77e298a1db95698b956ebadb6651.extensions.allizom.org/cantaro-auth"));
    }

    [Theory]
    [InlineData("https://abcdefghijklmnopqrstuvwxyzabcdef.chromiumapp.org/other")]
    [InlineData("https://abcdefghijklmnopqrstuvwxyzabcdef.chromiumapp.org/cantaro-auth?redirect=evil")]
    [InlineData("https://abcdefghijklmnopqrstuvwxyzabcdef.chromiumapp.org.evil.test/cantaro-auth")]
    [InlineData("http://abcdefghijklmnopqrstuvwxyzabcdef.chromiumapp.org/cantaro-auth")]
    public void RejectsRedirectsOutsideExactBrowserIdentityEndpoint(string redirectUri)
    {
        Assert.False(AuthController.IsValidExtensionRedirectUri(
            "abcdefghijklmnopqrstuvwxyzabcdef",
            redirectUri));
    }
}
