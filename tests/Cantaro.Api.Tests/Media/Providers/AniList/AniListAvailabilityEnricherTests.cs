using System.Net;
using System.Text;
using Cantaro.Api.Configuration;
using Cantaro.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class AniListAvailabilityEnricherTests
{
    [Fact]
    public async Task GetAvailabilityAsync_PrefersVerifiedAniListIdentityWithoutAuthorization()
    {
        var handler = new Handler("""
            {"data":{"Media":{"id":1,"idMal":1,"externalLinks":[
              {"site":"Netflix","url":"https://www.netflix.com/title/80001305","type":"STREAMING"}
            ],"streamingEpisodes":[]}}}
            """);
        var enricher = CreateEnricher(handler);

        var availability = await enricher.GetAvailabilityAsync(
        [
            new MediaProviderCrossReference { ProviderId = "anilist", ProviderMediaId = "1" },
            new MediaProviderCrossReference { ProviderId = "myanimelist", ProviderMediaId = "anime:1" }
        ], CancellationToken.None);

        Assert.Null(handler.Authorization);
        Assert.Contains("\"id\":1", handler.Body);
        Assert.DoesNotContain("$idMal", handler.Body);
        Assert.DoesNotContain("\"idMal\":", handler.Body);
        var netflix = Assert.Single(availability!);
        Assert.Equal("netflix", netflix.ServiceId);
        Assert.Equal("https://www.netflix.com/title/80001305", netflix.Url);
    }

    [Fact]
    public async Task GetAvailabilityAsync_UsesAndVerifiesMalIdentityWhenAniListIsMissing()
    {
        var handler = new Handler("""
            {"data":{"Media":{"id":1,"idMal":1,"externalLinks":[],"streamingEpisodes":[
              {"site":"Netflix","url":"https://www.netflix.com/title/80001305","title":"Cowboy Bebop"}
            ]}}}
            """);
        var enricher = CreateEnricher(handler);

        var availability = await enricher.GetAvailabilityAsync(
        [
            new MediaProviderCrossReference { ProviderId = "myanimelist", ProviderMediaId = "anime:1" }
        ], CancellationToken.None);

        Assert.DoesNotContain("$id:", handler.Body);
        Assert.DoesNotContain("\"id\":", handler.Body);
        Assert.Contains("\"idMal\":1", handler.Body);
        Assert.Equal("netflix", Assert.Single(availability!).ServiceId);
    }

    [Fact]
    public async Task GetAvailabilityAsync_RejectsMismatchedProviderIdentity()
    {
        var handler = new Handler("""
            {"data":{"Media":{"id":2,"idMal":2,"externalLinks":[],"streamingEpisodes":[]}}}
            """);
        var enricher = CreateEnricher(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => enricher.GetAvailabilityAsync(
        [
            new MediaProviderCrossReference { ProviderId = "anilist", ProviderMediaId = "1" }
        ], CancellationToken.None));
    }

    private static AniListAvailabilityEnricher CreateEnricher(Handler handler)
    {
        var apiClient = new AniListApiClient(
            new HttpClient(handler),
            Options.Create(new AniListOptions()),
            new AniListRequestGate(TimeProvider.System, TimeSpan.Zero),
            NullLogger<AniListApiClient>.Instance);
        return new AniListAvailabilityEnricher(apiClient);
    }

    private sealed class Handler(string responseBody) : HttpMessageHandler
    {
        public string? Authorization { get; private set; }
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
