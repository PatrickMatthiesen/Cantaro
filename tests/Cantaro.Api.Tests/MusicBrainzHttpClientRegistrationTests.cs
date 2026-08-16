using System.Net;
using System.Net.Http.Headers;
using Cantaro.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class MusicBrainzHttpClientRegistrationTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(30, true)]
    public async Task MusicBrainzClient_DoesNotRetryBelowThePackageRateLimiter(
        int retryAfterSeconds,
        bool expectCooldown)
    {
        var handler = new UnavailableHandler(retryAfterSeconds);
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureHttpClientDefaults(client => client.AddStandardResilienceHandler());
        services
            .AddMusicBrainzQueryClient()
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        await using var serviceProvider = services.BuildServiceProvider();
        var client = serviceProvider.GetRequiredService<IMusicBrainzQueryClient>();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            client.FindRecordingsAsync("recording:Test", 1, CancellationToken.None));

        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(expectCooldown, serviceProvider.GetRequiredService<MusicBrainzRequestGate>().IsCoolingDown);
    }

    [Fact]
    public void MusicBrainzRequestGate_HonorsTheEntireRetryAfterDelay()
    {
        var now = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
        var gate = new MusicBrainzRequestGate(new FixedTimeProvider(now));

        var delay = gate.Defer(new RetryConditionHeaderValue(TimeSpan.FromMinutes(2)));

        Assert.Equal(TimeSpan.FromMinutes(2), delay);
        Assert.True(gate.IsCoolingDown);
    }

    [Fact]
    public void MusicBrainzRequestGate_UsesIncreasingFallbackWhenRetryAfterIsMissing()
    {
        var now = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
        var gate = new MusicBrainzRequestGate(new FixedTimeProvider(now));

        Assert.Equal(TimeSpan.FromSeconds(30), gate.Defer(retryAfter: null));
        Assert.Equal(TimeSpan.FromMinutes(1), gate.Defer(retryAfter: null));
    }

    private sealed class UnavailableHandler(int retryAfterSeconds) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(
                TimeSpan.FromSeconds(retryAfterSeconds));
            return Task.FromResult(response);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
