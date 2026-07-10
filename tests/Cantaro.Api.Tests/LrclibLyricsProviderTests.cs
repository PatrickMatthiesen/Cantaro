using System.Net;
using System.Text;
using Cantaro.Api.Configuration;
using Cantaro.Api.Services.Lyrics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class LrclibLyricsProviderTests
{
    [Fact]
    public async Task GetLyricsAsync_ReturnsPlainAndSyncedLyricsFromExactMatch()
    {
        var handler = new StubHandler(request => Json(HttpStatusCode.OK, Record(42)));
        using var provider = CreateProvider(handler);

        var result = await provider.Instance.GetLyricsAsync(CompleteLookup(), CancellationToken.None);

        Assert.Equal(LyricsStates.Available, result.State);
        Assert.Equal(LyricsMatchStatuses.Exact, result.MatchStatus);
        Assert.Equal("42", result.ProviderRecordId);
        Assert.Equal("Line one", result.PlainLyrics);
        Assert.Equal("[00:01.00]Line one", result.SyncedLyrics);
        Assert.Equal(1m, result.Confidence);
        Assert.Contains("api/get?", handler.Requests.Single().RequestUri!.ToString());
    }

    [Fact]
    public async Task GetLyricsAsync_UsesConservativeSearchWhenSignatureIsIncomplete()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, $"[{Record(43)}]"));
        using var provider = CreateProvider(handler);
        var lookup = CompleteLookup() with { Album = null };

        var result = await provider.Instance.GetLyricsAsync(lookup, CancellationToken.None);

        Assert.Equal(LyricsStates.Available, result.State);
        Assert.Equal(LyricsMatchStatuses.Fallback, result.MatchStatus);
        Assert.Contains("api/search?", handler.Requests.Single().RequestUri!.ToString());
    }

    [Fact]
    public async Task GetLyricsAsync_DoesNotSelectAmbiguousSearchResults()
    {
        var body = $"[{Record(43)},{Record(44)}]";
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, body));
        using var provider = CreateProvider(handler);

        var result = await provider.Instance.GetLyricsAsync(CompleteLookup() with { Album = null }, CancellationToken.None);

        Assert.Equal(LyricsStates.Ambiguous, result.State);
        Assert.Equal(LyricsMatchStatuses.Ambiguous, result.MatchStatus);
        Assert.Null(result.PlainLyrics);
        Assert.Null(result.ProviderRecordId);
    }

    [Fact]
    public async Task GetLyricsAsync_ReturnsExplicitInstrumentalState()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, Record(45, instrumental: true)));
        using var provider = CreateProvider(handler);

        var result = await provider.Instance.GetLyricsAsync(CompleteLookup(), CancellationToken.None);

        Assert.Equal(LyricsStates.Instrumental, result.State);
        Assert.Equal(LyricsMatchStatuses.Exact, result.MatchStatus);
        Assert.Null(result.PlainLyrics);
        Assert.Null(result.SyncedLyrics);
    }

    [Fact]
    public async Task GetLyricsAsync_ReturnsUnavailableWhenSearchHasNoConfidentCandidate()
    {
        const string mismatch = """
            [{"id":50,"trackName":"A Different Song","artistName":"Another Artist","albumName":"Other","duration":200,"instrumental":false,"plainLyrics":"Wrong","syncedLyrics":null}]
            """;
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, mismatch));
        using var provider = CreateProvider(handler);

        var result = await provider.Instance.GetLyricsAsync(CompleteLookup() with { Album = null }, CancellationToken.None);

        Assert.Equal(LyricsStates.Unavailable, result.State);
        Assert.Equal(LyricsMatchStatuses.Unavailable, result.MatchStatus);
        Assert.Null(result.PlainLyrics);
    }

    [Fact]
    public async Task GetLyricsAsync_ConvertsProviderFailureToNonThrowingErrorState()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var provider = CreateProvider(handler);

        var result = await provider.Instance.GetLyricsAsync(CompleteLookup(), CancellationToken.None);

        Assert.Equal(LyricsStates.ProviderError, result.State);
        Assert.Equal(LyricsMatchStatuses.ProviderError, result.MatchStatus);
    }

    [Fact]
    public async Task GetLyricsAsync_CachesRepeatedRequests()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, Record(46)));
        using var provider = CreateProvider(handler);
        var lookup = CompleteLookup();

        await provider.Instance.GetLyricsAsync(lookup, CancellationToken.None);
        await provider.Instance.GetLyricsAsync(lookup, CancellationToken.None);

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetLyricsAsync_ReportsDisabledProviderWithoutCallingLrclib()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("HTTP should not be called."));
        using var provider = CreateProvider(handler, enabled: false);

        var result = await provider.Instance.GetLyricsAsync(CompleteLookup(), CancellationToken.None);

        Assert.Equal(LyricsStates.Disabled, result.State);
        Assert.Empty(handler.Requests);
    }

    private static ProviderScope CreateProvider(StubHandler handler, bool enabled = true)
    {
        var options = Options.Create(new LyricsOptions
        {
            Enabled = enabled,
            BaseUrl = "https://lrclib.test",
            CacheEntryLimit = 10,
            AvailableCacheMinutes = 60,
            UnavailableCacheMinutes = 10,
            TimeoutSeconds = 5
        });
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://lrclib.test/") };
        var cache = new LyricsProviderCache(options);
        var provider = new LrclibLyricsProvider(
            new StubHttpClientFactory(client),
            cache,
            options,
            NullLogger<LrclibLyricsProvider>.Instance);
        return new ProviderScope(provider, client, cache);
    }

    private static LyricsLookup CompleteLookup() => new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        "Test Song",
        "Test Artist",
        "Test Album",
        200,
        "recording-id",
        "ISRC123");

    private static string Record(long id, bool instrumental = false) => $$"""
        {"id":{{id}},"trackName":"Test Song","artistName":"Test Artist","albumName":"Test Album","duration":200,"instrumental":{{instrumental.ToString().ToLowerInvariant()}},"plainLyrics":"Line one","syncedLyrics":"[00:01.00]Line one"}
        """;

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class ProviderScope(
        LrclibLyricsProvider instance,
        HttpClient client,
        LyricsProviderCache cache) : IDisposable
    {
        public LrclibLyricsProvider Instance { get; } = instance;

        public void Dispose()
        {
            client.Dispose();
            cache.Dispose();
        }
    }
}
