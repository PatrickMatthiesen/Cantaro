using Cantaro.Api.Data;
using Cantaro.Api.Models;
using System.Data.Common;
using System.Diagnostics;
using Cantaro.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Cantaro.Api.Tests.Media.Catalog;

[Collection("Media catalog PostgreSQL")]
public sealed class MediaCatalogCanonicalizationPostgresTests(ITestOutputHelper output)
{
    [PostgresFact]
    public async Task CanonicalizeAsync_RetriesSummaryFillWhenDetailsChangeAfterPreload()
    {
        await using var schema = await SchemaContext.CreateAsync();
        var candidate = Candidate("anilist", "211778", "The World's Strongest Witch", "10");
        var ids = await MakeService(schema.Options).CanonicalizeAsync([candidate], default);
        var titleId = Assert.Single(ids.Values);
        var interleaved = new DetailUpdateBeforeSave(async cancellationToken =>
        {
            var title = await schema.Db.MediaTitles.SingleAsync(item => item.Id == titleId, cancellationToken);
            title.Synopsis = "Full details loaded concurrently";
            title.Synonyms.Add("Detailed alias");
            await schema.Db.SaveChangesAsync(cancellationToken);
        });
        var racingOptions = new DbContextOptionsBuilder<ApplicationDbContext>(schema.Options)
            .AddInterceptors(interleaved).Options;
        var summary = candidate with { Result = candidate.Result with
        {
            Synopsis = "Less detailed summary",
            Synonyms = ["Search alias"]
        } };

        await MakeService(racingOptions).CanonicalizeAsync([summary], default);

        var persisted = await schema.Db.MediaTitles.AsNoTracking().SingleAsync(item => item.Id == titleId);
        Assert.Equal("Full details loaded concurrently", persisted.Synopsis);
        Assert.Contains("Detailed alias", persisted.Synonyms);
        Assert.Contains("Search alias", persisted.Synonyms);
        Assert.Equal(1, interleaved.DetailWrites);
        Assert.Equal(2, interleaved.SaveAttempts);
    }

    [PostgresFact]
    public async Task CanonicalizeAsync_BatchesWritesReusesStoredMetadataAndSupportsSynonymSearch()
    {
        var counter = new CommandCounter();
        await using var schema = await SchemaContext.CreateAsync(counter);
        var candidates = Enumerable.Range(1, 20).Select(index =>
            new MediaCatalogSearchCandidate("anilist", new CachedMediaProviderSearchResult(
                index.ToString(), $"Title {index}", "anime", null, 2026)
            {
                Synonyms = [$"Alias {index}"],
                PrimaryProgressDimension = "episode",
                ReleaseStatusDimension = "episode"
            })).ToArray();
        counter.Commands = 0;
        var timer = Stopwatch.StartNew();
        await MakeService(schema.Options).CanonicalizeAsync(candidates, default);
        output.WriteLine($"20 new summaries: {counter.Commands} database commands, {timer.Elapsed.TotalMilliseconds:F1} ms.");
        Assert.InRange(counter.Commands, 1, 4);
        counter.Commands = 0;
        timer.Restart();
        await MakeService(schema.Options).CanonicalizeAsync(candidates, default);
        output.WriteLine($"20 existing summaries: {counter.Commands} database commands, {timer.Elapsed.TotalMilliseconds:F1} ms.");
        Assert.Equal(1, counter.Commands);
        var user = TestUserFactory.Create(47810, "catalog-search@example.test");
        schema.Db.Users.Add(user);
        schema.Db.ConnectedServiceAccounts.Add(new ConnectedServiceAccount
        {
            UserId = user.Id,
            Service = "anilist",
            ExternalAccountId = "catalog-test-account",
            ConnectionState = "connected",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await schema.Db.SaveChangesAsync();
        var provider = new CantaroSearchServiceTests.FakeMediaProvider("anilist", []);
        using var cache = new MediaProviderSearchCache();
        var search = new CantaroSearchService(schema.Db, new MediaProviderRegistry([provider]), cache,
            MakeService(schema.Options), NullLogger<CantaroSearchService>.Instance);
        var matched = await search.SearchAsync(user.Id, "alias 20", 6, false, default);
        Assert.Equal("Title 20", Assert.Single(matched.Groups.Media.Items).Title);
        Assert.Equal(0, provider.SearchCallCount);
    }

    [PostgresFact]
    public async Task CanonicalizeAsync_ConcurrentSharedReferenceClaimsRetryIntoOneCanonicalTitle()
    {
        await using var schema = await SchemaContext.CreateAsync();
        var first = Candidate("anilist", "ani-1", "First", "mal-10");
        var second = Candidate("simkl", "sim-20", "First", "mal-10");
        var options = schema.Options;

        var concurrent = await Task.WhenAll(
            MakeService(options).CanonicalizeAsync([first], default),
            MakeService(options).CanonicalizeAsync([second], default));

        Assert.Equal(1, await schema.Db.MediaTitles.CountAsync());
        Assert.Equal(3, await schema.Db.MediaProviderLinks.CountAsync());
        Assert.All(concurrent, result => Assert.Single(result));
        Assert.Single(concurrent.SelectMany(result => result.Values).Distinct());
        Assert.Contains(await schema.Db.MediaProviderLinks.ToListAsync(), link => link.Provider == "myanimelist" && link.ExternalId == "anime:10");

        var repeated = await MakeService(options).CanonicalizeAsync([first, second], default);
        Assert.Single(repeated.Values.Distinct());
        Assert.Equal(1, await schema.Db.MediaTitles.CountAsync());
        Assert.Equal(3, await schema.Db.MediaProviderLinks.CountAsync());
    }

    [PostgresFact]
    public async Task CanonicalizeAsync_ConcurrentAmbiguousClaimsDoNotCreateOrphanAliases()
    {
        await using var schema = await SchemaContext.CreateAsync();
        var options = schema.Options;
        var first = Candidate("anilist", "ani-1", "First", "mal-10");
        var second = Candidate("anilist", "ani-2", "Second", "mal-10");

        await Task.WhenAll(
            MakeService(options).CanonicalizeAsync([first], default),
            MakeService(options).CanonicalizeAsync([second], default));

        Assert.Equal(2, await schema.Db.MediaTitles.CountAsync());
        Assert.Equal(3, await schema.Db.MediaProviderLinks.CountAsync());
        // The first individual request may already have verified the MAL link.
        // Later contradictory evidence must not move that established identity.
        var originalAlias = await schema.Db.MediaProviderLinks.SingleAsync(link => link.Provider == "myanimelist");
        var repeated = await MakeService(options).CanonicalizeAsync([first, second], default);
        Assert.Equal(2, repeated.Values.Distinct().Count());
        Assert.Equal(2, await schema.Db.MediaTitles.CountAsync());
        Assert.Equal(3, await schema.Db.MediaProviderLinks.CountAsync());
        Assert.Equal(originalAlias.MediaTitleId, (await schema.Db.MediaProviderLinks.AsNoTracking()
            .SingleAsync(link => link.Provider == "myanimelist")).MediaTitleId);
    }

    private static MediaCatalogCanonicalizationService MakeService(DbContextOptions<ApplicationDbContext> options)
        => new(options, NullLogger<MediaCatalogCanonicalizationService>.Instance);

    private static MediaCatalogSearchCandidate Candidate(string provider, string id, string title, string malId)
        => new(provider, new CachedMediaProviderSearchResult(provider == "simkl" ? $"anime:{id.Replace("sim-", "")}" : id, title, "anime", null, 2026)
        {
            PrimaryProgressDimension = "episode",
            ReleaseStatusDimension = "episode",
            CrossReferences = [new("myanimelist", $"anime:{malId.Replace("mal-", "")}", null)]
        });

    private sealed class SchemaContext : IAsyncDisposable
    {
        private readonly string _adminConnectionString;
        private readonly string _schema;
        private bool _disposed;

        private SchemaContext(string adminConnectionString, string schema, ApplicationDbContext db,
            DbContextOptions<ApplicationDbContext> options)
        { _adminConnectionString = adminConnectionString; _schema = schema; Db = db; Options = options; }

        public ApplicationDbContext Db { get; }
        public DbContextOptions<ApplicationDbContext> Options { get; }

        public static async Task<SchemaContext> CreateAsync(CommandCounter? counter = null)
        {
            var raw = Environment.GetEnvironmentVariable("CANTARO_TEST_POSTGRES");
            if (string.IsNullOrWhiteSpace(raw)) throw new InvalidOperationException("CANTARO_TEST_POSTGRES is required.");
            var admin = new NpgsqlConnectionStringBuilder(raw);
            if (admin.Host is not ("localhost" or "127.0.0.1" or "::1"))
                throw new InvalidOperationException("CANTARO_TEST_POSTGRES must point to a loopback PostgreSQL server.");
            if (string.IsNullOrWhiteSpace(admin.Database)
                || !admin.Database.Contains("test", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("CANTARO_TEST_POSTGRES must use a database whose name contains 'test'.");

            var schema = $"cantaro_catalog_test_{Guid.NewGuid():N}";
            var adminConnectionString = admin.ConnectionString;
            await using (var connection = new NpgsqlConnection(adminConnectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"CREATE SCHEMA \"{schema}\"";
                await command.ExecuteNonQueryAsync();
            }

            var scoped = new NpgsqlConnectionStringBuilder(adminConnectionString) { SearchPath = schema };
            var builder = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(scoped.ConnectionString, provider => provider.EnableRetryOnFailure(maxRetryCount: 1));
            if (counter is not null) builder.AddInterceptors(counter);
            var options = builder.Options;
            var db = new ApplicationDbContext(options);
            var context = new SchemaContext(adminConnectionString, schema, db, options);
            try
            {
                // Other suites use separate schemas in the same test database.
                // EnsureCreated checks for tables database-wide and may skip this schema.
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

    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int Commands { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Commands++; return base.ReaderExecutingAsync(command, eventData, result, cancellationToken); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Commands++; return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken); }
    }

    private sealed class DetailUpdateBeforeSave(Func<CancellationToken, Task> writeDetails) : SaveChangesInterceptor
    {
        public int DetailWrites { get; private set; }
        public int SaveAttempts { get; private set; }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            SaveAttempts++;
            if (DetailWrites == 0)
            {
                DetailWrites++;
                await writeDetails(cancellationToken);
            }
            return result;
        }
    }

    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CANTARO_TEST_POSTGRES")))
                Skip = "Set CANTARO_TEST_POSTGRES to a loopback database whose name contains 'test'.";
        }
    }
}

// Existing PostgreSQL suites use database-wide EnsureCreated checks while
// creating temporary schemas. Keep this schema-based suite from overlapping them.
[CollectionDefinition("Media catalog PostgreSQL", DisableParallelization = true)]
public sealed class MediaCatalogPostgresCollection;
