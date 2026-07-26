using Cantaro.Api.Configuration;
using Xunit;

namespace Cantaro.Api.Tests;

public class SpotifyRedirectUriValidatorTests
{
    [Theory]
    [InlineData("https://cantaro.example/api/platforms/spotify/callback")]
    [InlineData("https://cantaro.example:8443/oauth/spotify/callback")]
    [InlineData("https://cantaro.example/oauth/callback?source=spotify")]
    [InlineData("http://127.0.0.1/oauth/spotify/callback")]
    [InlineData("http://127.0.0.1:5173/api/platforms/spotify/callback")]
    public void TryValidate_AcceptsSupportedRedirectUris(string redirectUri)
    {
        var isValid = SpotifyRedirectUriValidator.TryValidate(redirectUri, out var error);

        Assert.True(isValid);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("spotify/callback")]
    [InlineData("https://cantaro.example")]
    [InlineData("https://cantaro.example/")]
    [InlineData("http://localhost:5173/oauth/spotify/callback")]
    [InlineData("https://localhost/oauth/spotify/callback")]
    [InlineData("https://app.localhost/oauth/spotify/callback")]
    [InlineData("https://localhost./oauth/spotify/callback")]
    [InlineData("https://127.0.0.1/oauth/spotify/callback")]
    [InlineData("http://127.0.0.2/oauth/spotify/callback")]
    [InlineData("http://[::1]/oauth/spotify/callback")]
    [InlineData("http://cantaro.example/oauth/spotify/callback")]
    [InlineData("ftp://cantaro.example/oauth/spotify/callback")]
    [InlineData("https://user:password@cantaro.example/oauth/spotify/callback")]
    [InlineData("https://@cantaro.example/oauth/spotify/callback")]
    [InlineData("https://cantaro.example/oauth/spotify/callback#complete")]
    [InlineData("https://cantaro.example/oauth/spotify/callback#")]
    [InlineData("https://*.cantaro.example/oauth/spotify/callback")]
    [InlineData("https://cantaro.example/oauth/*/callback")]
    [InlineData("https://cantaro.example/oauth/%2A/callback")]
    public void TryValidate_RejectsUnsupportedRedirectUris(string? redirectUri)
    {
        var isValid = SpotifyRedirectUriValidator.TryValidate(redirectUri, out var error);

        Assert.False(isValid);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
