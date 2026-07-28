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
    [InlineData(0, 2)]
    [InlineData(30, 1)]
    public async Task MusicBrainzPipeline_RetriesShortDelayOnceAndRejectsLongWait(
        int retryAfterSeconds,
        int expectedRequestCount)
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

        Assert.Equal(expectedRequestCount, handler.RequestCount);
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
}
