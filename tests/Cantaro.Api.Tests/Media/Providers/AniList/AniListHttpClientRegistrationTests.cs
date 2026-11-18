using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Cantaro.Api.Configuration;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class AniListHttpClientRegistrationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AniListClient_NegotiatesGzipAndReadsTheResponse(bool compressed)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await using var server = builder.Build();
        string? acceptEncoding = null;
        server.MapPost("/", async (HttpContext context) =>
        {
            acceptEncoding = context.Request.Headers.AcceptEncoding.ToString();
            context.Response.ContentType = "application/json";
            var body = """{"data":{"Viewer":{"id":42}}}"""u8.ToArray();
            if (compressed)
            {
                context.Response.Headers.ContentEncoding = "gzip";
                await using var gzip = new GZipStream(context.Response.Body, CompressionLevel.Fastest, leaveOpen: true);
                await gzip.WriteAsync(body, context.RequestAborted);
                return;
            }

            await context.Response.Body.WriteAsync(body, context.RequestAborted);
        });
        await server.StartAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<AniListOptions>(options => options.GraphQlUrl = server.Urls.Single());
        services.AddAniListApiClient();
        await using var serviceProvider = services.BuildServiceProvider();
        var client = serviceProvider.GetRequiredService<AniListApiClient>();

        var result = await client.SendGraphQlAsync<JsonElement>(
            "test-token", "query { Viewer { id } }", null, CancellationToken.None);

        Assert.Contains("gzip", acceptEncoding);
        Assert.Equal(42, result.GetProperty("Viewer").GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task AniListClient_DoesNotRetryBelowTheGlobalRequestGate()
    {
        var handler = new UnavailableHandler();
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<AniListOptions>(options =>
        {
            options.ClientId = "client-id";
            options.ClientSecret = "client-secret";
        });
        services.ConfigureHttpClientDefaults(client => client.AddStandardResilienceHandler());
        services
            .AddAniListApiClient()
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        await using var serviceProvider = services.BuildServiceProvider();
        var client = serviceProvider.GetRequiredService<AniListApiClient>();

        var exception = await Assert.ThrowsAsync<AniListRequestException>(() =>
            client.SendGraphQlAsync<object>("token", "query { Viewer { id } }", null, CancellationToken.None));

        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(TimeSpan.FromSeconds(30), exception.RetryAfter);
    }

    [Fact]
    public void RequestGate_UsesTheLaterRetryAfterOrResetTimeWithoutCappingIt()
    {
        var now = new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);
        var gate = new AniListRequestGate(new FixedTimeProvider(now), TimeSpan.Zero);
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
        response.Headers.TryAddWithoutValidation(
            "X-RateLimit-Reset",
            now.AddMinutes(2).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));

        var delay = gate.ObserveResponse(response);

        Assert.Equal(TimeSpan.FromMinutes(2), delay);
    }

    [Fact]
    public async Task RequestGate_SerializesConcurrentCallers()
    {
        var gate = new AniListRequestGate(TimeProvider.System, TimeSpan.Zero);
        using var firstLease = await gate.AcquireAsync(CancellationToken.None);

        var secondLeaseTask = gate.AcquireAsync(CancellationToken.None);
        Assert.False(secondLeaseTask.IsCompleted);

        firstLease.Dispose();
        using var secondLease = await secondLeaseTask;
    }

    [Fact]
    public void RelationRefresh_DoesNotCapAniListRetryAfter()
    {
        var exception = new AniListRequestException(
            HttpStatusCode.TooManyRequests,
            TimeSpan.FromMinutes(2));

        var delay = MediaRelationGraphRefreshWorker.GetRetryDelay(exception, attempt: 1);

        Assert.Equal(TimeSpan.FromMinutes(2), delay);
    }

    private sealed class UnavailableHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return Task.FromResult(response);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
