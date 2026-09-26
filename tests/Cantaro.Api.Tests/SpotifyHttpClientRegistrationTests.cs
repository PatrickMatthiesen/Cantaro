using System.Net;
using System.Text;
using Cantaro.Api.Configuration;
using Cantaro.Api.Services;
using Cantaro.Api.Services.Spotify;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Http.Resilience;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class SpotifyHttpClientRegistrationTests
{
    [Fact]
    public void SpotifyCooldown_IsSharedAcrossTypedClients()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(new SpotifyCooldown(TimeProvider.System));
        services.AddLogging();
        services.Configure<SpotifyOptions>(_ => { });
        services.AddSingleton<ISpotifyRetryDelay, NoDelay>();
        services.AddSpotifyApiClient();
        using var provider = services.BuildServiceProvider();

        var cooldown = provider.GetRequiredService<SpotifyCooldown>();
        Assert.Same(cooldown, provider.GetRequiredService<SpotifyCooldown>());
        Assert.NotSame(provider.GetRequiredService<SpotifyApiClient>(),
            provider.GetRequiredService<SpotifyApiClient>());
    }

    [Fact]
    public async Task SpotifyPlaylistAppend_IsNeverRetriedByDefaultHttpResilience()
    {
        var handler = new AppliedButUnavailableHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(new SpotifyCooldown(TimeProvider.System));
        services.AddLogging();
        services.Configure<SpotifyOptions>(_ => { });
        services.ConfigureHttpClientDefaults(client => client.AddStandardResilienceHandler());
        services.AddSingleton<ISpotifyRetryDelay, NoDelay>();
        services.AddSpotifyApiClient().ConfigurePrimaryHttpMessageHandler(() => handler);
        await using var provider = services.BuildServiceProvider();
        var apiClient = provider.GetRequiredService<SpotifyApiClient>();

        var failure = await Assert.ThrowsAsync<PlatformApiException>(() =>
            apiClient.AddPlaylistItemsAsync(
                "access-token",
                "playlist1",
                ["track1"],
                CancellationToken.None));

        Assert.Equal(503, failure.StatusCode);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(["spotify:track:track1"], handler.AppliedUris);
    }

    private sealed class AppliedButUnavailableHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public List<string> AppliedUris { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v1/playlists/playlist1/items", request.RequestUri!.AbsolutePath);
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var json = System.Text.Json.JsonDocument.Parse(body);
            AppliedUris.AddRange(json.RootElement.GetProperty("uris")
                .EnumerateArray()
                .Select(uri => uri.GetString()!));
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class NoDelay : ISpotifyRetryDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
