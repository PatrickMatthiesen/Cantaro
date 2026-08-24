using System.Net;
using System.Text;
using Cantaro.Api.Configuration;
using Cantaro.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class MyAnimeListApiClientTests
{
    [Fact]
    public void BuildAuthorizationUrl_UsesPlainPkce()
    {
        var client = CreateClient(new RecordingHandler());

        var url = client.BuildAuthorizationUrl("https://cantaro.test/callback", "state", "verifier-value");

        Assert.Contains("code_challenge=verifier-value", url);
        Assert.Contains("code_challenge_method=plain", url);
    }

    [Fact]
    public async Task PutFormAsync_UsesPutWithoutReplay()
    {
        var handler = new RecordingHandler(_ => JsonResponse("{\"updated_at\":\"2026-08-23T12:00:00Z\"}"));
        var client = CreateClient(handler);

        await client.PutFormAsync<MyAnimeListAnimeListStatus>(
            "/anime/1/my_list_status",
            "token",
            new Dictionary<string, string?> { ["score"] = "8" },
            CancellationToken.None);

        Assert.Equal([HttpMethod.Put], handler.Methods);
        Assert.Contains("score=8", Assert.Single(handler.Bodies));
    }

    [Fact]
    public async Task PutFormAsync_FallsBackToPatchOnlyAfterMethodNotAllowed()
    {
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Put
            ? new HttpResponseMessage(HttpStatusCode.MethodNotAllowed)
            : JsonResponse("{}"));
        var client = CreateClient(handler);

        await client.PutFormAsync<MyAnimeListAnimeListStatus>(
            "/anime/1/my_list_status",
            "token",
            new Dictionary<string, string?> { ["score"] = "8" },
            CancellationToken.None);

        Assert.Equal([HttpMethod.Put, HttpMethod.Patch], handler.Methods);
    }

    [Fact]
    public async Task PutFormAsync_DoesNotReplayOtherFailures()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<MyAnimeListRequestException>(() => client.PutFormAsync<object>(
            "/anime/1/my_list_status",
            "token",
            new Dictionary<string, string?> { ["score"] = "8" },
            CancellationToken.None));

        Assert.Equal([HttpMethod.Put], handler.Methods);
    }

    [Fact]
    public async Task GetAsync_ValidatedAbsolutePagingUrlPreservesV2Path()
    {
        var handler = new RecordingHandler(_ => JsonResponse("{}"));
        var client = CreateClient(handler);

        await client.GetAsync<object>(
            "https://api.myanimelist.net/v2/users/@me/animelist?offset=1000",
            "token",
            query: null,
            CancellationToken.None);

        Assert.Equal("/v2/users/@me/animelist?offset=1000", Assert.Single(handler.RequestUris).PathAndQuery);
    }

    [Fact]
    public async Task GetAsync_RejectsPagingUrlFromAnotherOriginBeforeSendingBearer()
    {
        var handler = new RecordingHandler(_ => JsonResponse("{}"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync<object>(
            "https://example.invalid/v2/users/@me/animelist",
            "token",
            query: null,
            CancellationToken.None));

        Assert.Empty(handler.Methods);
    }

    [Fact]
    public async Task RegisteredClient_DoesNotRetryBelowProviderLogic()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<MyAnimeListOptions>(options => options.ClientId = "client-id");
        services.ConfigureHttpClientDefaults(client => client.AddStandardResilienceHandler());
        services.AddMyAnimeListApiClient().ConfigurePrimaryHttpMessageHandler(() => handler);
        await using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<MyAnimeListApiClient>();
        await Assert.ThrowsAsync<MyAnimeListRequestException>(() => client.GetAsync<object>(
            "/anime",
            accessToken: null,
            query: null,
            CancellationToken.None));

        Assert.Single(handler.Methods);
    }

    [Fact]
    public async Task RequestGate_SerializesPhysicalRequests()
    {
        var gate = new MyAnimeListRequestGate(TimeProvider.System, TimeSpan.Zero);
        using var firstLease = await gate.AcquireAsync(CancellationToken.None);

        var secondLeaseTask = gate.AcquireAsync(CancellationToken.None);

        Assert.False(secondLeaseTask.IsCompleted);
    }

    [Fact]
    public async Task RequestGate_ObservesRetryAfterProviderWide()
    {
        var gate = new MyAnimeListRequestGate(TimeProvider.System, TimeSpan.Zero);
        using (var lease = await gate.AcquireAsync(CancellationToken.None))
        using (var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests))
        {
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(1));
            Assert.True(gate.ObserveResponse(response) > TimeSpan.Zero);
        }

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gate.AcquireAsync(cancellation.Token));
    }

    private static MyAnimeListApiClient CreateClient(HttpMessageHandler handler)
    {
        return new MyAnimeListApiClient(
            new HttpClient(handler),
            Options.Create(new MyAnimeListOptions
            {
                ClientId = "client-id",
                ClientSecret = "client-secret"
            }),
            new MyAnimeListRequestGate(TimeProvider.System, TimeSpan.Zero),
            NullLogger<MyAnimeListApiClient>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage>? responseFactory = null) : HttpMessageHandler
    {
        public List<HttpMethod> Methods { get; } = [];
        public List<Uri> RequestUris { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Methods.Add(request.Method);
            RequestUris.Add(request.RequestUri!);
            Bodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return responseFactory?.Invoke(request) ?? JsonResponse("{}");
        }
    }
}
