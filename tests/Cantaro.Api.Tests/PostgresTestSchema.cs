using Cantaro.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Xunit;

namespace Cantaro.Api.Tests;

// Each test owns a private schema in an explicitly configured local test database.
internal sealed class PostgresTestSchema : IAsyncDisposable
{
    private readonly string _adminConnectionString;
    private readonly string _schema;
    private bool _disposed;

    private PostgresTestSchema(string adminConnectionString, string schema, ApplicationDbContext db)
    {
        _adminConnectionString = adminConnectionString;
        _schema = schema;
        Db = db;
    }

    public ApplicationDbContext Db { get; }

    public static async Task<PostgresTestSchema> CreateAsync()
    {
        var raw = Environment.GetEnvironmentVariable("CANTARO_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(raw)) throw new InvalidOperationException("CANTARO_TEST_POSTGRES is required.");
        var admin = new NpgsqlConnectionStringBuilder(raw);
        if (admin.Host is not ("localhost" or "127.0.0.1" or "::1"))
            throw new InvalidOperationException("CANTARO_TEST_POSTGRES must point to a loopback PostgreSQL server.");
        if (string.IsNullOrWhiteSpace(admin.Database)
            || !admin.Database.Contains("test", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("CANTARO_TEST_POSTGRES must use a database whose name contains 'test'.");

        var schema = $"cantaro_sync_test_{Guid.NewGuid():N}";
        var adminConnectionString = admin.ConnectionString;
        await using (var connection = new NpgsqlConnection(adminConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE SCHEMA \"{schema}\"";
            await command.ExecuteNonQueryAsync();
        }

        var scoped = new NpgsqlConnectionStringBuilder(adminConnectionString) { SearchPath = schema };
        var testConnectionString = scoped.ConnectionString;
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(testConnectionString, options => options.EnableRetryOnFailure(maxRetryCount: 1))
            .Options);
        var context = new PostgresTestSchema(adminConnectionString, schema, db);
        try
        {
            // EnsureCreated checks for tables database-wide and can skip this
            // private schema when another PostgreSQL fixture runs concurrently.
            await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
            return context;
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await Db.DisposeAsync();
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP SCHEMA IF EXISTS \"{_schema}\" CASCADE";
        await command.ExecuteNonQueryAsync();
    }
}

internal sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CANTARO_TEST_POSTGRES")))
            Skip = "Set CANTARO_TEST_POSTGRES to a loopback database whose name contains 'test'.";
    }
}
