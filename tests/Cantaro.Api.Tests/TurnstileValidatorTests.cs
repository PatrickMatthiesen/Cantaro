using System.Net;
using System.Net.Http.Json;
using Cantaro.Api.Configuration;
using Cantaro.Api.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class TurnstileValidatorTests
{
    [Fact]
    public async Task AcceptsSuccessfulResponseForExpectedActionAndHostname()
    {
        var handler = new QueueHandler(SiteVerifyResponse(success: true, action: "signup", hostname: "cantaro.example"));
        var validator = CreateValidator(handler);

        var result = await validator.ValidateAsync("token", "signup", CancellationToken.None);

        Assert.True(result);
        Assert.Equal(1, handler.RequestCount);
    }

    [Theory]
    [InlineData("login", "cantaro.example")]
    [InlineData("signup", "other.example")]
    public async Task RejectsSuccessfulResponseWithUnexpectedActionOrHostname(string action, string hostname)
    {
        var handler = new QueueHandler(SiteVerifyResponse(success: true, action, hostname));
        var validator = CreateValidator(handler);

        var result = await validator.ValidateAsync("token", "signup", CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task RejectsNetworkFailureAndMalformedResponse()
    {
        var networkFailureValidator = CreateValidator(new QueueHandler(new HttpRequestException("network failure")));
        var malformedValidator = CreateValidator(new QueueHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not-json")
        }));

        Assert.False(await networkFailureValidator.ValidateAsync("token", "signup", CancellationToken.None));
        Assert.False(await malformedValidator.ValidateAsync("token", "signup", CancellationToken.None));
    }

    [Fact]
    public async Task RejectsAReplayWhenSiteverifyRejectsTheSecondUse()
    {
        var handler = new QueueHandler(
            SiteVerifyResponse(success: true, action: "signup", hostname: "cantaro.example"),
            SiteVerifyResponse(success: false, action: null, hostname: null));
        var validator = CreateValidator(handler);

        Assert.True(await validator.ValidateAsync("single-use-token", "signup", CancellationToken.None));
        Assert.False(await validator.ValidateAsync("single-use-token", "signup", CancellationToken.None));
        Assert.Equal(2, handler.RequestCount);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("[::1]")]
    [InlineData("127.0.0.2")]
    public void ProductionRejectsLoopbackAllowedHostnames(string hostname)
    {
        var validator = new TurnstileOptionsValidator(new TestHostEnvironment(Environments.Production));
        var options = new TurnstileOptions
        {
            SiteKey = "site-key",
            Secret = "secret",
            AllowedHostnames = [hostname]
        };

        var result = validator.Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
    }

    [Fact]
    public void DevelopmentAllowsLoopbackAllowedHostnames()
    {
        var validator = new TurnstileOptionsValidator(new TestHostEnvironment(Environments.Development));
        var options = new TurnstileOptions
        {
            SiteKey = "site-key",
            Secret = "secret",
            AllowedHostnames = ["localhost"]
        };

        Assert.True(validator.Validate(Options.DefaultName, options).Succeeded);
    }

    private static TurnstileValidator CreateValidator(HttpMessageHandler handler) =>
        new(new StubHttpClientFactory(handler), Options.Create(new TurnstileOptions
        {
            SiteKey = "site-key",
            Secret = "secret",
            AllowedHostnames = ["cantaro.example"]
        }), NullLogger<TurnstileValidator>.Instance);

    private static HttpResponseMessage SiteVerifyResponse(bool success, string? action, string? hostname) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                success,
                action,
                hostname
            })
        };

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class QueueHandler(params object[] responses) : HttpMessageHandler
    {
        private readonly Queue<object> _responses = new(responses);
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var response = _responses.Dequeue();
            return response switch
            {
                HttpResponseMessage message => Task.FromResult(message),
                Exception exception => Task.FromException<HttpResponseMessage>(exception),
                _ => throw new InvalidOperationException("Unsupported test response.")
            };
        }
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Cantaro.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
