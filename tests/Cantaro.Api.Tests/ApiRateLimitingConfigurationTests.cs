using System.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using Cantaro.Api.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Xunit;

namespace Cantaro.Api.Tests;

public class ApiRateLimitingConfigurationTests
{
    [Theory]
    [InlineData("/api/register")]
    [InlineData("/api/register/")]
    [InlineData("/api/login")]
    [InlineData("/api/forgotPassword")]
    [InlineData("/api/resetPassword")]
    [InlineData("/api/auth/google/login")]
    [InlineData("/api/auth/google/link")]
    [InlineData("/api/auth/google/link/authorize")]
    [InlineData("/api/auth/google/reauth")]
    public void Classify_IdentityEndpoints_UsesIdentityBucket(string path)
    {
        var context = CreateContext(path);

        Assert.Equal(ApiRateLimitBucket.Identity, ApiRateLimitingConfiguration.Classify(context));
    }

    [Theory]
    [InlineData("/api/auth/extension/authorize")]
    [InlineData("/api/auth/extension/token")]
    [InlineData("/api/auth/extension/revoke")]
    public void Classify_ExtensionAuthenticationEndpoints_UsesExtensionBucket(string path)
    {
        var context = CreateContext(path);

        Assert.Equal(ApiRateLimitBucket.ExtensionAuthentication, ApiRateLimitingConfiguration.Classify(context));
    }

    [Theory]
    [InlineData("/api/sync")]
    [InlineData("/api/sync/jobs")]
    [InlineData("/api/matching/song-grouping/generate")]
    [InlineData("/api/media/providers/anilist/import")]
    [InlineData("/api/media/providers/anilist/initial-sync/apply")]
    public void Classify_CostlyEndpoints_UsesCostlyBucket(string path)
    {
        var context = CreateContext(path);

        Assert.Equal(ApiRateLimitBucket.Costly, ApiRateLimitingConfiguration.Classify(context));
    }

    [Fact]
    public void Classify_NonApiRequest_IsNotLimited()
    {
        var context = CreateContext("/assets/app.js");

        Assert.Equal(ApiRateLimitBucket.None, ApiRateLimitingConfiguration.Classify(context));
    }

    [Fact]
    public void Classify_ImportEventsStream_IsGeneralApiBucket()
    {
        var context = CreateContext("/api/media/providers/anilist/import/events", "GET");

        Assert.Equal(ApiRateLimitBucket.GeneralApi, ApiRateLimitingConfiguration.Classify(context));
    }

    [Fact]
    public async Task Configure_RejectsTheEleventhIdentityRequestInOneWindow()
    {
        var options = new RateLimiterOptions();
        ApiRateLimitingConfiguration.Configure(options);
        var limiter = options.GlobalLimiter ?? throw new InvalidOperationException("Global limiter was not configured.");
        var context = CreateContext("/api/login");

        for (var request = 0; request < 10; request++)
        {
            using var lease = await limiter.AcquireAsync(context);
            Assert.True(lease.IsAcquired);
        }

        using var rejectedLease = await limiter.AcquireAsync(context);
        Assert.False(rejectedLease.IsAcquired);
    }

    [Fact]
    public async Task Pipeline_Returns429WithRetryAfterWhenIdentityLimitIsExceeded()
    {
        await using var fixture = await StartAppAsync(app =>
            app.MapPost("/api/login", () => Results.Ok()));

        for (var request = 0; request < 10; request++)
        {
            using var response = await fixture.Client.PostAsync("/api/login", content: null);
            Assert.Equal(StatusCodes.Status200OK, (int)response.StatusCode);
        }

        using var rejectedResponse = await fixture.Client.PostAsync("/api/login", content: null);

        Assert.Equal(StatusCodes.Status429TooManyRequests, (int)rejectedResponse.StatusCode);
        Assert.True(
            rejectedResponse.Headers.TryGetValues("Retry-After", out var retryAfterValues)
            && int.TryParse(retryAfterValues.Single(), out var retryAfterSeconds)
            && retryAfterSeconds >= 1);
    }

    [Fact]
    public async Task Pipeline_CostlyBucketEnforcesConcurrencyAndWindowLimits()
    {
        var firstTwoEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRequests = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var activeRequests = 0;

        await using var fixture = await StartAppAsync(app =>
            app.MapPost("/api/sync/jobs", async () =>
            {
                if (Interlocked.Increment(ref activeRequests) == 2)
                {
                    firstTwoEntered.TrySetResult();
                }

                await releaseRequests.Task;
                Interlocked.Decrement(ref activeRequests);
                return Results.Ok();
            }));

        var firstRequest = fixture.Client.PostAsync("/api/sync/jobs", content: null);
        var secondRequest = fixture.Client.PostAsync("/api/sync/jobs", content: null);
        await firstTwoEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using (var concurrentRejection = await fixture.Client.PostAsync("/api/sync/jobs", content: null))
        {
            Assert.Equal(StatusCodes.Status429TooManyRequests, (int)concurrentRejection.StatusCode);
        }

        releaseRequests.TrySetResult();
        using var firstResponse = await firstRequest;
        using var secondResponse = await secondRequest;
        Assert.Equal(StatusCodes.Status200OK, (int)firstResponse.StatusCode);
        Assert.Equal(StatusCodes.Status200OK, (int)secondResponse.StatusCode);

        for (var request = 0; request < 18; request++)
        {
            using var response = await fixture.Client.PostAsync("/api/sync/jobs", content: null);
            Assert.Equal(StatusCodes.Status200OK, (int)response.StatusCode);
        }

        using var windowRejection = await fixture.Client.PostAsync("/api/sync/jobs", content: null);
        Assert.Equal(StatusCodes.Status429TooManyRequests, (int)windowRejection.StatusCode);
    }

    [Fact]
    public async Task Pipeline_GeneralApiLimitIsPartitionedByAuthenticatedUser()
    {
        await using var fixture = await StartAppAsync(app =>
            app.MapGet("/api/profile", () => Results.Ok()));

        for (var request = 0; request < 120; request++)
        {
            using var response = await SendAsync(fixture, HttpMethod.Get, "/api/profile", userId: "user-one");
            Assert.Equal(StatusCodes.Status200OK, (int)response.StatusCode);
        }

        using var otherUserResponse = await SendAsync(fixture, HttpMethod.Get, "/api/profile", userId: "user-two");
        Assert.Equal(StatusCodes.Status200OK, (int)otherUserResponse.StatusCode);
    }

    [Fact]
    public async Task Pipeline_IgnoresForwardedIpSpoofingFromUntrustedPeer()
    {
        await using var fixture = await StartAppAsync(app =>
            app.MapPost("/api/login", () => Results.Ok()));

        for (var request = 0; request < 10; request++)
        {
            using var response = await SendAsync(
                fixture,
                HttpMethod.Post,
                "/api/login",
                forwardedFor: $"198.51.100.{request + 1}");
            Assert.Equal(StatusCodes.Status200OK, (int)response.StatusCode);
        }

        using var rejectedResponse = await SendAsync(
            fixture,
            HttpMethod.Post,
            "/api/login",
            forwardedFor: "198.51.100.250");

        Assert.Equal(StatusCodes.Status429TooManyRequests, (int)rejectedResponse.StatusCode);
    }

    private static async Task<TestAppFixture> StartAppAsync(Action<WebApplication> mapEndpoints)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        TrustedProxyConfiguration.Configure(builder.Services, builder.Configuration);
        builder.Services.AddRateLimiter(ApiRateLimitingConfiguration.Configure);

        var app = builder.Build();
        app.UseForwardedHeaders();
        app.UseRouting();
        app.Use(async (context, next) =>
        {
            if (context.Request.Headers.TryGetValue("X-Test-User", out var userId)
                && !string.IsNullOrWhiteSpace(userId.ToString()))
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
                    authenticationType: "test"));
            }

            await next();
        });
        app.UseRateLimiter();
        mapEndpoints(app);

        await app.StartAsync();
        var server = app.Services.GetRequiredService<IServer>();
        var address = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new TestAppFixture(app, new HttpClient { BaseAddress = new Uri(address) });
    }

    private static async Task<HttpResponseMessage> SendAsync(
        TestAppFixture fixture,
        HttpMethod method,
        string path,
        string? userId = null,
        string? forwardedFor = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (userId is not null)
        {
            request.Headers.Add("X-Test-User", userId);
        }

        if (forwardedFor is not null)
        {
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor);
        }

        return await fixture.Client.SendAsync(request);
    }

    private sealed class TestAppFixture(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public WebApplication App { get; } = app;
        public HttpClient Client { get; } = client;

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
        }
    }

    private static DefaultHttpContext CreateContext(string path, string method = "POST")
    {
        return new DefaultHttpContext
        {
            Connection = { RemoteIpAddress = IPAddress.Parse("192.0.2.10") },
            Request = { Method = method, Path = path }
        };
    }
}
