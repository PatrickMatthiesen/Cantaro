using Cantaro.Api.Configuration;
using Cantaro.Api.Controllers;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class ExtensionLoginRedirectTests
{
    [Theory]
    [InlineData("https://cantaro.example")]
    [InlineData("https://localhost:5173")]
    public async Task SignedOutBehindHttpProxy_ReturnTargetUsesLoginPageOrigin(string publicOrigin)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddIdentityCore<User>().AddEntityFrameworkStores<ApplicationDbContext>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("internal-api", 8080);
        context.Request.Path = "/api/auth/extension/authorize";
        const string clientId = "abcdefghijklmnopqrstuvwxyzabcdef";
        var redirectUri = $"https://{clientId}.chromiumapp.org/cantaro-auth";
        context.Request.QueryString = QueryString.Create(new Dictionary<string, string?>
        {
            ["response_type"] = "code", ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri, ["state"] = "opaque+state&value",
            ["code_challenge"] = "challenge", ["code_challenge_method"] = "S256"
        });
        var resolver = new FrontendUrlResolver(new ConfigurationBuilder().Build(),
            Options.Create(new FrontendUrlOptions { HttpsBaseUrl = publicOrigin }),
            new HttpContextAccessor { HttpContext = context });
        var controller = new AuthController(scope.ServiceProvider.GetRequiredService<UserManager<User>>(),
            null!, resolver, Options.Create(new ExtensionAuthOptions()), NullLogger<AuthController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };

        var result = await controller.AuthorizeExtension("code", clientId, redirectUri,
            "opaque+state&value", "challenge", "S256", CancellationToken.None);
        var login = new Uri(Assert.IsType<RedirectResult>(result).Url);
        var returnTo = new Uri(QueryHelpers.ParseQuery(login.Query)["returnTo"].ToString());
        Assert.Equal(publicOrigin, login.GetLeftPart(UriPartial.Authority));
        Assert.Equal(login.GetLeftPart(UriPartial.Authority), returnTo.GetLeftPart(UriPartial.Authority));
        Assert.Equal("/api/auth/extension/authorize", returnTo.AbsolutePath);
        Assert.Equal(context.Request.QueryString.Value, returnTo.Query);
    }
}
