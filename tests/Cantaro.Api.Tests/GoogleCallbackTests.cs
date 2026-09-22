using System.Net;
using Cantaro.Api.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using CantaroAuthenticationOptions = Cantaro.Api.Configuration.AuthenticationOptions;

namespace Cantaro.Api.Tests;

public sealed class GoogleCallbackTests
{
    [Theory]
    [InlineData("https://cantaro.example", "", "/signin-google", "https://cantaro.example/signin-google")]
    [InlineData("https://cantaro.example:8443/", "/cantaro", "/google/callback", "https://cantaro.example:8443/cantaro/google/callback")]
    [InlineData(null, "", "/signin-google", "http://localhost:5000/signin-google")]
    public async Task ChallengeAndCodeExchange_UseTheSamePublicCallback(
        string? publicOrigin, string pathBase, string callbackPath, string expectedCallback)
    {
        using var backchannel = new GoogleBackchannel();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.Configure<FrontendUrlOptions>(options => options.HttpsBaseUrl = publicOrigin);
        services.AddAuthentication()
            .AddCookie(IdentityConstants.ExternalScheme)
            .AddCantaroGoogle(new CantaroAuthenticationOptions
            {
                Mode = AuthenticationMode.GoogleOnly,
                Google = new GoogleAuthenticationOptions
                {
                    ClientId = "test-client",
                    ClientSecret = "test-secret",
                    CallbackPath = callbackPath
                }
            });
        services.Configure<GoogleOptions>(GoogleDefaults.AuthenticationScheme, options =>
        {
            options.Backchannel = new HttpClient(backchannel);
            options.UsePkce = true;
        });
        using var provider = services.BuildServiceProvider();
        using var challengeScope = provider.CreateScope();
        var challenge = CreateContext(challengeScope.ServiceProvider, pathBase, "/api/auth/google/login");
        challenge.Request.Headers["X-Forwarded-Host"] = "attacker.example";
        challenge.Request.Headers["X-Forwarded-Proto"] = "https";
        await challenge.ChallengeAsync(GoogleDefaults.AuthenticationScheme, new AuthenticationProperties
        {
            RedirectUri = "/api/auth/google/callback",
            Items = { ["cantaro.google.return_url"] = "/extension-auth?returnTo=test" }
        });

        var authorization = QueryHelpers.ParseQuery(new Uri(challenge.Response.Headers.Location.ToString()).Query);
        Assert.Equal(expectedCallback, authorization["redirect_uri"].ToString());
        Assert.Equal("S256", authorization["code_challenge_method"].ToString());
        var options = provider.GetRequiredService<IOptionsMonitor<GoogleOptions>>().Get(GoogleDefaults.AuthenticationScheme);
        var properties = options.StateDataFormat.Unprotect(authorization["state"]);
        Assert.NotNull(properties);
        Assert.Equal("/extension-auth?returnTo=test", properties.Items["cantaro.google.return_url"]);

        // Exercise the real callback pipeline, including protected state,
        // correlation validation, PKCE redemption and external-cookie sign-in.
        using var callbackScope = provider.CreateScope();
        var callback = CreateContext(callbackScope.ServiceProvider, pathBase, callbackPath);
        callback.Request.Host = new HostString("different-internal-host");
        callback.Request.QueryString = QueryString.Create(new Dictionary<string, string?>
        {
            ["code"] = "test-code",
            ["state"] = authorization["state"].ToString()
        });
        callback.Request.Headers.Cookie = string.Join("; ", challenge.Response.Headers.SetCookie
            .Select(cookie => cookie!.Split(';')[0]));
        var handler = await callback.RequestServices.GetRequiredService<IAuthenticationHandlerProvider>()
            .GetHandlerAsync(callback, GoogleDefaults.AuthenticationScheme);
        Assert.True(await Assert.IsAssignableFrom<IAuthenticationRequestHandler>(handler).HandleRequestAsync());
        Assert.Equal(expectedCallback, backchannel.RedirectUri);
        Assert.False(string.IsNullOrWhiteSpace(backchannel.CodeVerifier));
        Assert.Equal("/api/auth/google/callback", callback.Response.Headers.Location.ToString());
    }

    private static DefaultHttpContext CreateContext(IServiceProvider services, string pathBase, string path)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost", 5000);
        context.Request.PathBase = pathBase;
        context.Request.Path = path;
        return context;
    }

    private sealed class GoogleBackchannel : HttpMessageHandler
    {
        public string? RedirectUri { get; private set; }
        public string? CodeVerifier { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post)
            {
                var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken));
                RedirectUri = form["redirect_uri"].ToString();
                CodeVerifier = form["code_verifier"].ToString();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"test-token\",\"token_type\":\"Bearer\"}")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":\"test-user\",\"email\":\"test@example.com\",\"verified_email\":true}")
            };
        }
    }
}
