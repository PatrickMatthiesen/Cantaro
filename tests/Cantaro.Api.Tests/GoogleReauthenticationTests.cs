using System.Security.Claims;
using Cantaro.Api.Controllers;
using Cantaro.Api.Services;
using Xunit;

namespace Cantaro.Api.Tests;

public class GoogleReauthenticationTests
{
    [Theory]
    [InlineData(-301, false)]
    [InlineData(-300, true)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void DeletionRequiresRecentProofAndRejectsFutureProof(int age, bool expected)
    {
        var now = DateTimeOffset.UtcNow;
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(GoogleAuthService.GoogleReauthenticatedAtClaim, now.AddSeconds(age).ToUnixTimeSeconds().ToString())
        ], "test"));
        Assert.Equal(expected, ProfileController.HasRecentGoogleReauthentication(principal, now));
    }

    [Fact]
    public void OrdinarySessionIsNotEnoughToDeleteWithoutPassword()
    {
        Assert.False(ProfileController.HasRecentGoogleReauthentication(new ClaimsPrincipal(), DateTimeOffset.UtcNow));
    }
}
