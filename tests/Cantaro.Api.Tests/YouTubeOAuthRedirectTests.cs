using Cantaro.Api.Controllers;
using Cantaro.Api.Services;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class YouTubeOAuthRedirectTests
{
    [Fact]
    public void ResolveRedirectUri_UsesReachableLocalhostForAspireDevelopmentHost()
    {
        var connect = new CallbackUrlCandidates(
            "https://cantaro.dev.localhost:5173/api/platforms/youtube/callback",
            null,
            "https://cantaro.dev.localhost:5173/api/platforms/youtube/callback");
        var callback = new CallbackUrlCandidates(
            "https://localhost:5173/api/platforms/youtube/callback",
            null,
            "https://localhost:5173/api/platforms/youtube/callback");

        var redirectUri = YouTubeService.ResolveRedirectUri(connect);

        Assert.Equal("https://localhost:5173/api/platforms/youtube/callback", redirectUri);
        Assert.Equal(redirectUri, YouTubeService.ResolveRedirectUri(callback));
    }

    [Theory]
    [InlineData("https://cantaro.example.com/api/platforms/youtube/callback")]
    [InlineData("https://localhost:5173/api/platforms/youtube/callback")]
    [InlineData("http://127.0.0.1:5173/api/platforms/youtube/callback")]
    public void ResolveRedirectUri_PreservesOtherHosts(string preferred)
    {
        Assert.Equal(preferred, YouTubeService.ResolveRedirectUri(new CallbackUrlCandidates(preferred, null, null)));
    }

    [Theory]
    [InlineData("https://cantaro.dev.localhost:5173", "https://cantaro.dev.localhost:5173")]
    [InlineData("https://cantaro.dev.localhost:5173/path", null)]
    [InlineData("https://cantaro.dev.localhost:5173/?next=evil", null)]
    [InlineData("https://user@cantaro.dev.localhost:5173", null)]
    [InlineData("javascript:alert(1)", null)]
    public void GetSafeStateOrigin_AcceptsOnlyOrigin(string value, string? expected)
    {
        Assert.Equal(expected, PlatformsController.GetSafeStateOrigin(value));
    }
}
