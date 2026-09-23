using System.Net;
using Cantaro.Api.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using CantaroAuthenticationOptions = Cantaro.Api.Configuration.AuthenticationOptions;

namespace Cantaro.Api.Tests;

public sealed class GoogleCallbackTests
{
    [Theory]
    [InlineData("http", "192.168.1.151", "https", "", "/signin-google", "https://cantaro.example/signin-google")]
    [InlineData("http", "::ffff:192.168.1.151", "https", "/cantaro", "/google/callback", "https://cantaro.example/cantaro/google/callback")]
    [InlineData("http", "192.168.1.99", "https", "", "/signin-google", "http://cantaro.example/signin-google")]
    [InlineData("https", "127.0.0.1", null, "", "/signin-google", "https://cantaro.example/signin-google")]
    [InlineData("http", "127.0.0.1", null, "", "/signin-google", "http://cantaro.example/signin-google")]
    public async Task ChallengeAndCodeExchange_UseRequestOriginAfterTrustedForwarding(
        string scheme, string peer, string? forwardedScheme, string pathBase, string callbackPath, string expectedCallback)
    {
        using var backchannel = new GoogleBackchannel();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        // Frontend return URLs must not override the Google handler callback.
        services.Configure<FrontendUrlOptions>(options => options.HttpsBaseUrl = "https://frontend.example");
        TrustedProxyConfiguration.Configure(services, new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:TrustedProxies:0"] = "192.168.1.151"
            }).Build());
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
        var challenge = CreateContext(challengeScope.ServiceProvider, pathBase, "/api/auth/google/login", scheme, peer, forwardedScheme);
        await ApplyForwardedHeaders(challenge);
        Assert.Equal(new Uri(expectedCallback).Scheme, challenge.Request.Scheme);
        Assert.Equal("cantaro.example", challenge.Request.Host.Value);
        var expectedClient = peer.EndsWith("192.168.1.151", StringComparison.Ordinal)
            ? IPAddress.Parse("203.0.113.42") : IPAddress.Parse(peer);
        Assert.Equal(expectedClient, challenge.Connection.RemoteIpAddress);
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
        var callback = CreateContext(callbackScope.ServiceProvider, pathBase, callbackPath, scheme, peer, forwardedScheme);
        await ApplyForwardedHeaders(callback);
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

    private static DefaultHttpContext CreateContext(
        IServiceProvider services, string pathBase, string path, string scheme, string peer, string? forwardedScheme)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Scheme = scheme;
        context.Request.Host = new HostString("cantaro.example");
        context.Request.PathBase = pathBase;
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        context.Request.Headers["X-Forwarded-Host"] = "attacker.example";
        if (forwardedScheme is not null)
        {
            context.Request.Headers["X-Forwarded-Proto"] = forwardedScheme;
            context.Request.Headers["X-Forwarded-For"] = "203.0.113.42";
        }
        return context;
    }

    private static Task ApplyForwardedHeaders(HttpContext context)
    {
        var app = new ApplicationBuilder(context.RequestServices);
        app.UseForwardedHeaders();
        app.Run(_ => Task.CompletedTask);
        return app.Build()(context);
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
