using Cantaro.Api.Data;
using System.Data.Common;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantaro.Api.Tests.Media.Catalog;

public sealed class MediaCatalogCanonicalizationTests
{
    [Fact]
    public void Identity_NormalizesProviderButPreservesExternalIdCase()
    {
        var identity = MediaCatalogIdentity.Create(" AniList ", "  AbC-42  ");

        Assert.Equal("anilist", identity.ProviderId);
        Assert.Equal("AbC-42", identity.ProviderMediaId);
    }

    [Fact]
    public async Task CanonicalizeAsync_MergesTransitiveCrossReferencesIndependentOfCandidateOrder()
    {
        var (db, connection, options) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var candidates = new[]
        {
            Candidate("simkl", "sim-1", "World's Strongest Witch", "anilist", "ani-1"),
            Candidate("myanimelist", "mal-1", "The World's Strongest Witch"),
            Candidate("anilist", "ani-1", "The World's Strongest Witch", "myanimelist", "mal-1")
        };

        var first = await Canonicalizer(options).CanonicalizeAsync(candidates, default);
        var second = await Canonicalizer(options).CanonicalizeAsync(candidates.Reverse().ToArray(), default);

        Assert.Single(first.Values.Distinct());
        Assert.Equal(first, second);
        Assert.Equal(1, await db.MediaTitles.CountAsync());
        Assert.Equal(new[] { "anilist", "myanimelist", "simkl" },
            await db.MediaProviderLinks.OrderBy(link => link.Provider).Select(link => link.Provider).ToArrayAsync());
        var title = await db.MediaTitles.SingleAsync();
        Assert.Equal("The World's Strongest Witch", title.CanonicalTitle);
    }

    [Fact]
    public async Task CanonicalizeAsync_DoesNotMergeSameTitleWithoutIdentityEvidence()
    {
        var (db, connection, options) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;

        var ids = await Canonicalizer(options).CanonicalizeAsync([
            Candidate("anilist", "1", "Same title"), Candidate("simkl", "2", "Same title")
        ], default);

        Assert.Equal(2, ids.Values.Distinct().Count());
        Assert.Equal(2, await db.MediaTitles.CountAsync());
    }

    [Fact]
    public async Task CanonicalizeAsync_DoesNotArbitrarilyAssignSharedCrossReferenceWhenComponentHasTwoSameProviderPrimaries()
    {
        var (db, connection, options) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;

        var ids = await Canonicalizer(options).CanonicalizeAsync([
            Candidate("anilist", "ani-1", "First", "myanimelist", "mal-1"),
            Candidate("anilist", "ani-2", "Second", "myanimelist", "mal-1")
        ], default);

        Assert.Equal(2, ids.Values.Distinct().Count());
        Assert.Equal(2, await db.MediaTitles.CountAsync());
        Assert.Equal(new[] { "ani-1", "ani-2" }, await db.MediaProviderLinks.OrderBy(link => link.ExternalId)
            .Select(link => link.ExternalId).ToArrayAsync());
        Assert.DoesNotContain(await db.MediaProviderLinks.ToListAsync(), link => link.Provider == "myanimelist");
    }

    [Fact]
    public async Task CanonicalizeAsync_AnchoredIdentityConflictCreatesSeparatePrimaryWithoutCrossReferences()
    {
        var (db, connection, options) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var existing = MakeTitle("Existing");
        db.AddRange(existing, MakeLink(existing, "anilist", "ani-1"), MakeLink(existing, "simkl", "anime:old"));
        await db.SaveChangesAsync();

        var ids = await Canonicalizer(options).CanonicalizeAsync([
            Candidate("simkl", "sim-1", "Candidate", "anilist", "ani-1"),
            Candidate("anilist", "ani-1", "Existing")
        ], default);

        Assert.NotEqual(existing.Id, ids[Key("simkl", "sim-1")]);
        Assert.Equal(existing.Id, ids[MediaCatalogIdentity.Create("anilist", "ani-1")]);
        Assert.Equal(2, await db.MediaTitles.CountAsync());
    }

    [Fact]
    public async Task CanonicalizeAsync_DoesNotMergeDifferentMediaKinds()
    {
        var (db, connection, options) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;

        var ids = await Canonicalizer(options).CanonicalizeAsync([
            Candidate("anilist", "ani-1", "Same", "myanimelist", "mal-1", kind: MediaKinds.Anime),
            Candidate("myanimelist", "mal-1", "Same", kind: MediaKinds.Manga)
        ], default);

        Assert.Equal(2, ids.Values.Distinct().Count());
    }

    [Fact]
    public async Task CanonicalizeAsync_UsesExistingIdentityAndFillsOnlyMissingMetadata()
    {
        var (db, connection, options) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var title = MakeTitle("Curated name");
        title.Synopsis = "Keep this synopsis";
        title.PosterUrl = "https://example.test/curated.jpg";
        title.EpisodeCount = 12;
        title.Format = "tv";
        var link = MakeLink(title, "anilist", "ani-1");
        db.AddRange(title, link);
        await db.SaveChangesAsync();

        var ids = await Canonicalizer(options).CanonicalizeAsync([
            WithResult(Candidate("anilist", "ani-1", "Provider title", "simkl", "sim-1"),
                synopsis: "Provider synopsis", poster: "https://example.test/provider.jpg", count: 24),
            Candidate("simkl", "sim-1", "Other provider title")
        ], default);

        Assert.Equal(title.Id, ids[Key("simkl", "sim-1")]);
        db.ChangeTracker.Clear();
        var reloaded = await db.MediaTitles.SingleAsync();
        Assert.Equal("Curated name", reloaded.CanonicalTitle);
        Assert.Equal("Keep this synopsis", reloaded.Synopsis);
        Assert.Equal("https://example.test/curated.jpg", reloaded.PosterUrl);
        Assert.Equal(12, reloaded.EpisodeCount);
        Assert.Equal("tv", reloaded.Format);
    }

    [Fact]
    public async Task CanonicalizeAsync_CreatesCatalogOnlyRowsAndBoundsDatabaseCommandsForTwentyResults()
    {
        var counter = new CommandCounter();
        var (db, connection, options) = await CreateDbAsync(counter);
        await using var _ = connection;
        await using var __ = db;
        var candidates = Enumerable.Range(1, 20)
            .Select(index => Candidate("simkl", $"sim-{index}", $"Title {index}")).ToArray();

        counter.Commands = 0;
        var ids = await Canonicalizer(options).CanonicalizeAsync(candidates, default);
        // SQLite emits separate INSERT commands; only preloading reads must
        // remain bounded independently of the number of discovered titles.
        var persistenceCommands = counter.Commands;

        Assert.Equal(20, ids.Values.Distinct().Count());
        Assert.Equal(20, await db.MediaTitles.CountAsync());
        Assert.Equal(20, await db.MediaProviderLinks.CountAsync());
        Assert.Empty(await db.MediaLibraryEntries.ToListAsync());
        Assert.Empty(await db.MediaLibraryProviderBindings.ToListAsync());
        Assert.InRange(persistenceCommands, 40, 42);
        counter.Commands = 0;
        await Canonicalizer(options).CanonicalizeAsync(candidates, default);
        Assert.InRange(counter.Commands, 1, 2);
    }

    [Fact]
    public async Task CanonicalizeAsync_HonorsCancellationBeforeAccessingDatabase()
    {
        var (db, connection, options) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Canonicalizer(options).CanonicalizeAsync([Candidate("simkl", "1", "Title")], cancellation.Token));
        Assert.Empty(await db.MediaTitles.ToListAsync());
    }

    private static MediaCatalogCanonicalizationService Canonicalizer(DbContextOptions<ApplicationDbContext> options)
        => new(options, NullLogger<MediaCatalogCanonicalizationService>.Instance);

    private static MediaCatalogSearchCandidate Candidate(string provider, string id, string title,
        string? crossProvider = null, string? crossId = null, string kind = MediaKinds.Anime)
        => new(provider, new CachedMediaProviderSearchResult(StoredId(provider, id), title, kind, null, 2026)
        {
            PrimaryProgressDimension = kind == MediaKinds.Manga ? MediaProgressDimensions.Chapter : MediaProgressDimensions.Episode,
            ReleaseStatusDimension = kind == MediaKinds.Manga ? MediaProgressDimensions.Chapter : MediaProgressDimensions.Episode,
            CrossReferences = crossProvider is null ? [] : [new(crossProvider, StoredId(crossProvider, crossId!), null)]
        });

    private static MediaCatalogIdentity Key(string provider, string id) => MediaCatalogIdentity.Create(provider, StoredId(provider, id));

    private static string StoredId(string provider, string id) =>
        (provider is "simkl" or "myanimelist") && !id.Contains(':', StringComparison.Ordinal) ? $"anime:{id}" : id;

    private static MediaCatalogSearchCandidate WithResult(MediaCatalogSearchCandidate candidate,
        string? synopsis = null, string? poster = null, int? count = null)
        => candidate with { Result = candidate.Result with { Synopsis = synopsis, PosterUrl = poster, EpisodeCount = count } };

    private static async Task<(ApplicationDbContext Db, SqliteConnection Connection, DbContextOptions<ApplicationDbContext> Options)>
        CreateDbAsync(CommandCounter? counter = null)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection);
        if (counter is not null) builder.AddInterceptors(counter);
        var options = builder.Options;
        var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return (db, connection, options);
    }

    private static MediaTitle MakeTitle(string title) => new()
    {
        Id = Guid.NewGuid(), CanonicalTitle = title, MediaKind = MediaKinds.Anime,
        PrimaryProgressDimension = MediaProgressDimensions.Episode,
        ReleaseStatusDimension = MediaProgressDimensions.Episode,
        SupportsEpisodeProgress = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
    };

    private static MediaProviderLink MakeLink(MediaTitle title, string provider, string id) => new()
    {
        Id = Guid.NewGuid(), MediaTitleId = title.Id, Provider = provider, ExternalId = id,
        LinkSource = MediaMappingSources.Imported, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
    };

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
}
