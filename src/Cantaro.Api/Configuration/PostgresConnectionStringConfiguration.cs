using Npgsql;

namespace Cantaro.Api.Configuration;

public static class PostgresConnectionStringConfiguration
{
    public static void ConfigurePostgresConnection(
        this ConfigurationManager configuration,
        string connectionName)
    {
        var connectionString = configuration.GetConnectionString(connectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{connectionName}' is not configured.");

        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            GssEncryptionMode = GssEncryptionMode.Disable
        };

        // On Windows, localhost can try an unavailable IPv6 loopback endpoint
        // before the Aspire-published IPv4 port and add several seconds to every
        // new physical connection.
        if (string.Equals(
                connectionStringBuilder.Host,
                "localhost",
                StringComparison.OrdinalIgnoreCase))
        {
            connectionStringBuilder.Host = "127.0.0.1";
        }

        configuration[$"ConnectionStrings:{connectionName}"] =
            connectionStringBuilder.ConnectionString;
    }
}
