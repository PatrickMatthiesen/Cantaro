using Cantaro.Api.Configuration;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class PostgresConnectionStringConfigurationTests
{
    [Fact]
    public void ConfigurePostgresConnection_DisablesGssAndUsesIpv4Loopback()
    {
        var configuration = new ConfigurationManager();
        configuration["ConnectionStrings:cantaro-db"] =
            "Host=localhost;Database=cantaro;Username=cantaro;Password=secret;SSL Mode=Require";

        configuration.ConfigurePostgresConnection("cantaro-db");

        var result = new NpgsqlConnectionStringBuilder(
            configuration.GetConnectionString("cantaro-db"));

        Assert.Equal(GssEncryptionMode.Disable, result.GssEncryptionMode);
        Assert.Equal("127.0.0.1", result.Host);
        Assert.Equal(SslMode.Require, result.SslMode);
        Assert.Equal("cantaro", result.Database);
        Assert.Equal("cantaro", result.Username);
    }

    [Fact]
    public void ConfigurePostgresConnection_PreservesNonLoopbackHost()
    {
        var configuration = new ConfigurationManager();
        configuration["ConnectionStrings:cantaro-db"] =
            "Host=postgres;Database=cantaro;Username=cantaro;Password=secret";

        configuration.ConfigurePostgresConnection("cantaro-db");

        var result = new NpgsqlConnectionStringBuilder(
            configuration.GetConnectionString("cantaro-db"));

        Assert.Equal("postgres", result.Host);
    }

    [Fact]
    public void ConfigurePostgresConnection_RequiresConfiguredConnection()
    {
        var configuration = new ConfigurationManager();

        var exception = Assert.Throws<InvalidOperationException>(
            () => configuration.ConfigurePostgresConnection("cantaro-db"));

        Assert.Contains("cantaro-db", exception.Message, StringComparison.Ordinal);
    }
}
