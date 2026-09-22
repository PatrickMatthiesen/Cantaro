using System.Net;
using System.Text;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class AioStreamsAnimeEnricherTests
{
    [Fact]
    public async Task EnrichAsync_AddsIndependentKitsuAndMappedImdbTargets()
    {
        var handler = new QueueHandler(Response("""
            {"success":true,"data":{"mappings":{"simklId":40398,"kitsuId":6448,"imdbId":"tt2098220"},"imdb":{"id":"tt2098220","seasonNumber":2,"fromEpisode":13},"episodeMappings":[{"start":1,"end":24,"tvdbSeason":9,"offset":99}]}}
            """));
        var details = CreateDetails();

        await CreateEnricher(handler).EnrichAsync(details, CancellationToken.None);

        Assert.Equal("https://aio.example/api/v1/anime?idType=simklId&idValue=40398", handler.RequestUris.Single().AbsoluteUri);
        var kitsu = Assert.Single(details.StremioTargets, target => target.Id == "kitsu:6448");
        Assert.Equal("series", kitsu.Type);
        Assert.Null(kitsu.EpisodeMapping!.SeasonNumber);
        Assert.Equal(0, kitsu.EpisodeMapping.EpisodeOffset);
        var imdb = Assert.Single(details.StremioTargets, target => target.Id == "tt2098220");
        Assert.Equal(2, imdb.EpisodeMapping!.SeasonNumber);
        Assert.Equal(12, imdb.EpisodeMapping.EpisodeOffset);
    }

    [Fact]
    public async Task EnrichAsync_UsesValidTargetsIndependently()
    {
        var kitsuOnly = new QueueHandler(Response("""
            {"success":true,"data":{"mappings":{"anilistId":11061,"kitsuId":6448}}}
            """));
        var aniListDetails = CreateDetails("anilist", "11061");
        await CreateEnricher(kitsuOnly).EnrichAsync(aniListDetails, CancellationToken.None);
        Assert.Equal("kitsu:6448", Assert.Single(aniListDetails.StremioTargets).Id);

        var imdbOnly = new QueueHandler(Response("""
            {"success":true,"data":{"mappings":{"malId":9253,"imdbId":"tt2098220"},"imdb":{"id":"tt2098220"}}}
            """));
        var malDetails = CreateDetails("myanimelist", "anime:9253");
        await CreateEnricher(imdbOnly).EnrichAsync(malDetails, CancellationToken.None);
        var imdb = Assert.Single(malDetails.StremioTargets);
        Assert.Equal("tt2098220", imdb.Id);
        Assert.Null(imdb.EpisodeMapping);
    }

    [Fact]
    public async Task EnrichAsync_DisabledStillAnnotatesKnownKitsuWithoutHttp()
    {
        var handler = new QueueHandler(Response("{}"));
        var details = CreateDetails();
        details.StremioTarget = new MediaProviderStremioTarget { Type = "series", Id = "kitsu:6448" };

        await CreateEnricher(handler, baseUrl: string.Empty).EnrichAsync(details, CancellationToken.None);

        Assert.Empty(handler.RequestUris);
        var target = Assert.Single(details.StremioTargets);
        Assert.Null(target.EpisodeMapping!.SeasonNumber);
        Assert.Equal(0, target.EpisodeMapping.EpisodeOffset);
    }

    [Theory]
    [InlineData("{\"success\":true,\"data\":{\"mappings\":{\"simklId\":999,\"kitsuId\":6448}}}")]
    [InlineData("{\"success\":true,\"data\":{\"mappings\":{\"simklId\":40398,\"kitsuId\":0}}}")]
    [InlineData("not-json")]
    public async Task EnrichAsync_MismatchedOrMalformedResponsePreservesTargets(string body)
    {
        var handler = new QueueHandler(Response(body));
        var original = new MediaProviderStremioTarget { Type = "series", Id = "kitsu:7000" };
        var details = CreateDetails();
        details.StremioTargets = [original];

        await CreateEnricher(handler).EnrichAsync(details, CancellationToken.None);

        var retained = Assert.Single(details.StremioTargets);
        Assert.Equal("kitsu:7000", retained.Id);
        Assert.Equal(0, retained.EpisodeMapping!.EpisodeOffset);
    }

    [Fact]
    public async Task EnrichAsync_ConflictingExistingIdentityRejectsWholeResponse()
    {
        var handler = new QueueHandler(Response("""
            {"success":true,"data":{"mappings":{"simklId":40398,"kitsuId":6448,"imdbId":"tt2098220"},"imdb":{"id":"tt2098220","seasonNumber":1,"fromEpisode":1}}}
            """));
        var details = CreateDetails();
        details.StremioTargets =
        [
            new MediaProviderStremioTarget { Type = "series", Id = "kitsu:7000" }
        ];

        await CreateEnricher(handler).EnrichAsync(details, CancellationToken.None);

        Assert.Equal("kitsu:7000", Assert.Single(details.StremioTargets).Id);
    }

    [Fact]
    public async Task EnrichAsync_CachesOneSourceAcrossCallers()
    {
        var handler = new QueueHandler(Response("""
            {"success":true,"data":{"mappings":{"simklId":40398,"kitsuId":6448}}}
            """));
        var enricher = CreateEnricher(handler);
        var first = CreateDetails();
        var second = CreateDetails();

        await enricher.EnrichAsync(first, CancellationToken.None);
        await enricher.EnrichAsync(second, CancellationToken.None);

        Assert.Single(handler.RequestUris);
        Assert.Equal("kitsu:6448", Assert.Single(first.StremioTargets).Id);
        Assert.Equal("kitsu:6448", Assert.Single(second.StremioTargets).Id);
    }

    [Fact]
    public async Task EnrichAsync_UsesStaleSuccessDuringOutageAndAppliesFailureCooldown()
    {
        var handler = new QueueHandler(
            Response("""
                {"success":true,"data":{"mappings":{"simklId":40398,"kitsuId":6448}}}
                """),
            Response("{}", HttpStatusCode.ServiceUnavailable));
        var clock = new TestTimeProvider();
        var enricher = CreateEnricher(handler, clock: clock);
        await enricher.EnrichAsync(CreateDetails(), CancellationToken.None);
        clock.Advance(TimeSpan.FromHours(25));

        var duringOutage = CreateDetails();
        await enricher.EnrichAsync(duringOutage, CancellationToken.None);
        var duringCooldown = CreateDetails();
        await enricher.EnrichAsync(duringCooldown, CancellationToken.None);

        Assert.Equal(2, handler.RequestUris.Count);
        Assert.Equal("kitsu:6448", Assert.Single(duringOutage.StremioTargets).Id);
        Assert.Equal("kitsu:6448", Assert.Single(duringCooldown.StremioTargets).Id);
    }

    [Fact]
    public async Task EnrichAsync_OutageWithoutCacheDoesNotAddTargets()
    {
        var handler = new QueueHandler(Response("{}", HttpStatusCode.ServiceUnavailable));
        var enricher = CreateEnricher(handler);
        var first = CreateDetails();
        var second = CreateDetails();

        await enricher.EnrichAsync(first, CancellationToken.None);
        await enricher.EnrichAsync(second, CancellationToken.None);

        Assert.Single(handler.RequestUris);
        Assert.Empty(first.StremioTargets);
        Assert.Empty(second.StremioTargets);
    }

    [Fact]
    public async Task EnrichAsync_DoesNotUseSuccessOlderThanSevenDaysDuringOutage()
    {
        var handler = new QueueHandler(
            Response("""
                {"success":true,"data":{"mappings":{"simklId":40398,"kitsuId":6448}}}
                """),
            Response("{}", HttpStatusCode.ServiceUnavailable));
        var clock = new TestTimeProvider();
        var enricher = CreateEnricher(handler, clock: clock);
        await enricher.EnrichAsync(CreateDetails(), CancellationToken.None);
        clock.Advance(TimeSpan.FromDays(8));

        var details = CreateDetails();
        await enricher.EnrichAsync(details, CancellationToken.None);

        Assert.Equal(2, handler.RequestUris.Count);
        Assert.Empty(details.StremioTargets);
    }

    [Fact]
    public async Task EnrichAsync_PropagatesCallerCancellation()
    {
        var handler = new CancelHandler();
        var enricher = CreateEnricher(handler);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => enricher.EnrichAsync(CreateDetails(), cancellation.Token));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public void Options_RequireServerConfiguredHttpsUrl()
    {
        Assert.True(AioStreamsOptions.HasValidBaseUrl(new AioStreamsOptions()));
        Assert.True(AioStreamsOptions.HasValidBaseUrl(new AioStreamsOptions { BaseUrl = "https://aio.example/base" }));
        Assert.False(AioStreamsOptions.HasValidBaseUrl(new AioStreamsOptions { BaseUrl = "http://aio.example" }));
        Assert.False(AioStreamsOptions.HasValidBaseUrl(new AioStreamsOptions { BaseUrl = "https://user:secret@aio.example" }));
        Assert.False(AioStreamsOptions.HasValidBaseUrl(new AioStreamsOptions { BaseUrl = "https://aio.example?token=secret" }));
    }

    private static AioStreamsAnimeEnricher CreateEnricher(
        HttpMessageHandler handler,
        string baseUrl = "https://aio.example",
        TimeProvider? clock = null)
        => new(
            new HttpClientFactory(handler),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new AioStreamsOptions { BaseUrl = baseUrl }),
            clock ?? TimeProvider.System);

    private static MediaProviderTitleDetails CreateDetails(
        string providerId = "simkl",
        string providerMediaId = "anime:40398")
        => new()
        {
            ProviderId = providerId,
            ProviderMediaId = providerMediaId,
            Title = "Steins;Gate",
            MediaKind = MediaKinds.Anime,
            Format = MediaFormats.Tv,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode
        };

    private static HttpResponseMessage Response(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class HttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        private readonly HttpClient _client = new(handler);

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class QueueHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class CancelHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException();
        }
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now += duration;
    }
}
