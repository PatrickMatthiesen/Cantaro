using Cantaro.Api.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Cantaro.Api.Tests;

public class ProductionHostFilteringConfigurationTests
{
    [Fact]
    public void Configure_UsesProductionHttpsHostnameForAllowedHosts()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "*",
            ["Frontend:HttpsBaseUrl"] = "https://cantaro.example:8443/"
        });

        ProductionHostFilteringConfiguration.Configure(configuration, isProduction: true);

        Assert.Equal("cantaro.example", configuration["AllowedHosts"]);
    }

    [Fact]
    public void Configure_PreservesConfiguredAllowedHostsOutsideProduction()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "*",
            ["Frontend:HttpsBaseUrl"] = "http://localhost:5173"
        });

        ProductionHostFilteringConfiguration.Configure(configuration, isProduction: false);

        Assert.Equal("*", configuration["AllowedHosts"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://cantaro.example")]
    [InlineData("https://localhost")]
    [InlineData("https://cantaro.localhost")]
    [InlineData("https://127.0.0.1")]
    [InlineData("https://192.0.2.1")]
    [InlineData("https://cantaro.example/app")]
    [InlineData("https://cantaro.example?tenant=one")]
    [InlineData("https://user@cantaro.example")]
    public void Configure_RejectsInvalidProductionOrigin(string? configuredOrigin)
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "*",
            ["Frontend:HttpsBaseUrl"] = configuredOrigin
        });

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionHostFilteringConfiguration.Configure(configuration, isProduction: true));

        Assert.Contains("Frontend:HttpsBaseUrl", exception.Message, StringComparison.Ordinal);
        Assert.Equal("*", configuration["AllowedHosts"]);
    }

    [Fact]
    public async Task Configure_RejectsRequestsForOtherHosts()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration["AllowedHosts"] = "*";
        builder.Configuration["Frontend:HttpsBaseUrl"] = "https://cantaro.example";
        ProductionHostFilteringConfiguration.Configure(
            builder.Configuration,
            builder.Environment.IsProduction());

        await using var app = builder.Build();
        app.MapGet("/", () => Results.Ok());
        await app.StartAsync();
        try
        {
            var server = app.Services.GetRequiredService<IServer>();
            var address = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };

            using var allowedResponse = await SendWithHostAsync(client, "cantaro.example");
            using var rejectedDnsResponse = await SendWithHostAsync(client, "untrusted.example");
            using var rejectedIpResponse = await SendWithHostAsync(client, "127.0.0.1");

            Assert.Equal(StatusCodes.Status200OK, (int)allowedResponse.StatusCode);
            Assert.Equal(StatusCodes.Status400BadRequest, (int)rejectedDnsResponse.StatusCode);
            Assert.Equal(StatusCodes.Status400BadRequest, (int)rejectedIpResponse.StatusCode);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private static async Task<HttpResponseMessage> SendWithHostAsync(HttpClient client, string host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = host;
        return await client.SendAsync(request);
    }

    private static IConfiguration CreateConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
