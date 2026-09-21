using System.Net;
using System.Text;
using Cantaro.Api.Configuration;
using Cantaro.Api.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class SimklApiClientTests
{
    [Fact]
    public void AuthorizationUrl_UsesV2PkceAndWriteScope()
    {
        var api = CreateClient(new Handler(_ => Json("{}")));
        var uri = new Uri(api.BuildAuthorizationUrl("https://cantaro.test/callback", "state", "challenge"));
        Assert.Equal("simkl.com", uri.Host);
        Assert.Contains("code_challenge_method=S256", uri.Query);
        Assert.Contains("media%3Aread%20media%3Awrite", uri.Query);
        Assert.Contains("state=state", uri.Query);
    }

    [Fact]
    public async Task PublicCatalogOmitsBearerAndIncludesRequiredApplicationParameters()
    {
        var handler = new Handler(_ => Json("{}"));
        var api = CreateClient(handler);
        using var _ = await api.GetAsync("/tv/123", null, null, CancellationToken.None);
        var request = Assert.Single(handler.Requests);
        Assert.Null(request.Authorization);
        Assert.Contains("client_id=client", request.Uri.Query);
        Assert.Contains("app-name=cantaro", request.Uri.Query);
        Assert.Contains("app-version=1.0", request.Uri.Query);
    }

    [Fact]
    public async Task HistoryPostIsSentOnceAndFailureDoesNotLeakResponseBody()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("secret-token-in-provider-body")
        });
        var api = CreateClient(handler);
        var error = await Assert.ThrowsAsync<SimklRequestException>(() => api.PostAsync(
            "/sync/history", "access-token", new { shows = new[] { new { ids = new { simkl = 123 } } } }, CancellationToken.None));
        Assert.Equal(HttpStatusCode.TooManyRequests, error.StatusCode);
        Assert.DoesNotContain("secret-token", error.Message);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer access-token", request.Authorization);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("\"simkl\":123", request.Body);
    }

    private static SimklApiClient CreateClient(Handler handler)
        => new(new HttpClient(handler), Options.Create(new SimklOptions
        {
            ClientId = "client", ClientSecret = "secret"
        }));

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(Uri Uri, string? Authorization, HttpMethod Method, string Body)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!, request.Headers.Authorization?.ToString(), request.Method,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
            return respond(request);
        }
    }
}
