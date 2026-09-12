using System.Net;
using Microsoft.AspNetCore.Builder;
using Cantaro.Api.Configuration;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class TrustedProxyConfigurationTests
{
    [Fact]
    public void CommaSeparatedAndIndexedAddressesAreCombinedAndDeduplicated()
    {
        var options = Configure(" 192.168.1.151, 2001:db8::1, ,192.168.1.151 ", "192.168.1.152");
        Assert.Equal(new[] { IPAddress.Parse("192.168.1.151"), IPAddress.Parse("2001:db8::1"), IPAddress.Parse("192.168.1.152") }, options.KnownProxies);
        Assert.Empty(options.KnownIPNetworks);
        Assert.Equal(1, options.ForwardLimit);
        Assert.Equal(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto, options.ForwardedHeaders);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" , , ")]
    public void EmptyListDisablesTrust(string value)
    {
        var options = Configure(value);
        Assert.Equal(ForwardedHeaders.None, options.ForwardedHeaders);
        Assert.Empty(options.KnownProxies);
        Assert.Empty(options.KnownIPNetworks);
    }

    [Theory]
    [InlineData("192.168.1.151,invalid")]
    [InlineData("192.168.1.0/24")]
    public void InvalidEntryRejectsWholeConfiguration(string value)
    {
        Assert.Throws<InvalidOperationException>(() => Configure(value));
    }

    private static ForwardedHeadersOptions Configure(params string[] entries)
    {
        var values = entries.Select((value, index) => new KeyValuePair<string, string?>($"RateLimiting:TrustedProxies:{index}", value));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddOptions();
        TrustedProxyConfiguration.Configure(services, configuration);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
    }
}
