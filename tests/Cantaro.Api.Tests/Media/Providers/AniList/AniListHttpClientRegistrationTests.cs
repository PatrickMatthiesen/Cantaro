using System.Net;
using System.Net.Http.Headers;
using Cantaro.Api.Configuration;
using Cantaro.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class AniListHttpClientRegistrationTests
{
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
