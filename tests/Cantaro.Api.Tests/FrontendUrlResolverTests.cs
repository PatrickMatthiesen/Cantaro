using Cantaro.Api.Configuration;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public class FrontendUrlResolverTests
{
    [Fact]
    public void GetFrontendUrl_UsesTrustedOriginBeforeConfiguredAspireUrl()
    {
        var resolver = CreateResolver(
            context =>
            {
                context.Request.Scheme = "https";
                context.Request.Host = new HostString("localhost", 7203);
                context.Request.Headers.Origin = "https://snowfall.tail5a9a42.ts.net";
            },
            new Dictionary<string, string?>
            {
                ["services:web:http:0"] = "https://localhost:5173",
                ["Frontend:TrustedHostSuffixes:0"] = ".ts.net"
            });

        Assert.Equal("https://snowfall.tail5a9a42.ts.net", resolver.GetFrontendUrl());
    }

    [Fact]
    public void GetCallbackUrl_UsesTrustedForwardedHost()
    {
        var resolver = CreateResolver(
            context =>
            {
                context.Request.Scheme = "https";
                context.Request.Host = new HostString("localhost", 7203);
                context.Request.Headers["X-Forwarded-Proto"] = "https";
                context.Request.Headers["X-Forwarded-Host"] = "snowfall.tail5a9a42.ts.net";
            },
            new Dictionary<string, string?>
            {
                ["Frontend:TrustedHostSuffixes:0"] = ".ts.net"
            });

        Assert.Equal(
            "https://snowfall.tail5a9a42.ts.net/api/media/providers/anilist/callback",
            resolver.GetCallbackUrl("api/media/providers/anilist/callback"));
    }

    [Fact]
    public void GetCallbackUrls_ProvidesPreferredHttpAndHttpsCandidates()
    {
        var resolver = CreateResolver(
            context =>
            {
                context.Request.Scheme = "https";
                context.Request.Host = new HostString("localhost", 7203);
            },
            new Dictionary<string, string?>
            {
                ["Frontend:BaseUrl"] = "http://localhost:5173",
                ["Frontend:HttpBaseUrl"] = "http://localhost:5174",
                ["services:web:https:0"] = "https://localhost:5173"
            });

        var callbackUrls = resolver.GetCallbackUrls("api/platforms/spotify/callback");

        Assert.Equal(
            "http://localhost:5173/api/platforms/spotify/callback",
            callbackUrls.Preferred);
        Assert.Equal(
            "http://localhost:5174/api/platforms/spotify/callback",
            callbackUrls.Http);
        Assert.Equal(
            "https://localhost:7203/api/platforms/spotify/callback",
            callbackUrls.Https);
    }

    [Fact]
    public void GetCallbackUrls_UsesConfiguredHttpsOriginBehindHttpReverseProxy()
    {
        var resolver = CreateResolver(
            context =>
            {
                context.Request.Scheme = "http";
                context.Request.Host = new HostString("cantaro.example");
                context.Request.Headers["X-Forwarded-Proto"] = "https";
                context.Request.Headers["X-Forwarded-Host"] = "cantaro.example";
            },
            new Dictionary<string, string?>
            {
                ["Frontend:HttpsBaseUrl"] = "https://cantaro.example"
            });

        var callbackUrls = resolver.GetCallbackUrls("api/platforms/spotify/callback");

        Assert.Equal(
            "https://cantaro.example/api/platforms/spotify/callback",
            callbackUrls.Preferred);
        Assert.Equal(
            "https://cantaro.example/api/platforms/spotify/callback",
            callbackUrls.Https);
    }

    [Fact]
    public void GetFrontendUrl_KeepsConfiguredUrlForUntrustedOrigin()
    {
        var resolver = CreateResolver(
            context =>
            {
                context.Request.Scheme = "https";
                context.Request.Host = new HostString("localhost", 7203);
                context.Request.Headers.Origin = "https://example.com";
            },
            new Dictionary<string, string?>
            {
                ["Frontend:BaseUrl"] = "https://localhost:5173"
            });

        Assert.Equal("https://localhost:5173", resolver.GetFrontendUrl());
    }

    [Fact]
    public void GetFrontendUrl_TrustsLoopbackOriginOnlyWhenConfigured()
    {
        var resolver = CreateResolver(
            context =>
            {
                context.Request.Scheme = "https";
                context.Request.Host = new HostString("localhost", 7203);
                context.Request.Headers.Origin = "http://localhost:5173";
            },
            new Dictionary<string, string?>
            {
                ["Frontend:BaseUrl"] = "https://app.example.test",
                ["Frontend:TrustLoopbackOrigins"] = "true"
            });

        Assert.Equal("http://localhost:5173", resolver.GetFrontendUrl());
    }

    [Fact]
    public void GetFrontendUrl_DoesNotTrustLoopbackOriginUnlessConfigured()
    {
        var resolver = CreateResolver(
            context =>
            {
                context.Request.Scheme = "https";
                context.Request.Host = new HostString("api.example.test");
                context.Request.Headers.Origin = "http://localhost:5173";
            },
            new Dictionary<string, string?>
            {
                ["Frontend:BaseUrl"] = "https://app.example.test"
            });

        Assert.Equal("https://app.example.test", resolver.GetFrontendUrl());
    }

    [Fact]
    public void GetFrontendUrl_DoesNotTrustTailnetOriginUnlessConfigured()
    {
        var resolver = CreateResolver(
            context =>
            {
                context.Request.Scheme = "https";
                context.Request.Host = new HostString("localhost", 7203);
                context.Request.Headers.Origin = "https://snowfall.tail5a9a42.ts.net";
            },
            new Dictionary<string, string?>
            {
                ["services:web:http:0"] = "https://localhost:5173"
            });

        Assert.Equal("https://localhost:5173", resolver.GetFrontendUrl());
    }

    [Fact]
    public void GetFrontendUrl_IgnoresBlankTrustedHostSuffixes()
    {
        var resolver = CreateResolver(
            context =>
            {
                context.Request.Scheme = "https";
                context.Request.Host = new HostString("localhost", 7203);
                context.Request.Headers.Origin = "https://evil.example";
            },
            new Dictionary<string, string?>
            {
                ["Frontend:BaseUrl"] = "https://localhost:5173",
                ["Frontend:TrustedHostSuffixes:0"] = " "
            });

        Assert.Equal("https://localhost:5173", resolver.GetFrontendUrl());
    }

    private static FrontendUrlResolver CreateResolver(
        Action<DefaultHttpContext> configureContext,
        Dictionary<string, string?>? configurationValues = null)
    {
        var context = new DefaultHttpContext();
        configureContext(context);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues ?? [])
            .Build();
        var options = configuration
            .GetSection(FrontendUrlOptions.SectionName)
            .Get<FrontendUrlOptions>() ?? new FrontendUrlOptions();
        var accessor = new HttpContextAccessor
        {
            HttpContext = context
        };

        return new FrontendUrlResolver(
            configuration,
            Options.Create(options),
            accessor);
    }
}
