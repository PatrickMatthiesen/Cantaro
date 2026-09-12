using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cantaro.Api.Configuration;

public static class TrustedProxyConfiguration
{
    private const string SectionName = "RateLimiting:TrustedProxies";

    public static void Configure(IServiceCollection services, IConfiguration configuration)
    {
        var configuredProxies = configuration.GetSection(SectionName).Get<string[]>() ?? [];
        var proxyAddresses = configuredProxies
            .SelectMany(value => (value ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Select(ParseProxyAddress)
            .Distinct()
            .ToArray();

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = proxyAddresses.Length == 0
                ? ForwardedHeaders.None
                : ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var proxyAddress in proxyAddresses)
            {
                options.KnownProxies.Add(proxyAddress);
            }
        });
    }

    private static IPAddress ParseProxyAddress(string value)
    {
        if (IPAddress.TryParse(value, out var address))
        {
            return address;
        }

        throw new InvalidOperationException(
            $"{SectionName} contains an invalid IP address. Configure explicit proxy IP addresses only.");
    }
}
