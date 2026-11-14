using System.Data.Common;
using Cantaro.Api.Data;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace Cantaro.Api.Tests;

public sealed class ProviderSourceSelectionTests(ITestOutputHelper output)
{
    [PostgresFact]
    public async Task SqlSelectionUsesOrdinalKeysAndExcludesStaleConflictedAndExpiredSources()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        var db = schema.Db;
        var now = DateTimeOffset.UtcNow;
        var track = new Track {  Id = Guid.NewGuid()  };
        var chosen = Source(track, "Z-key", "Chosen", now);
        var lower = Source(track, "a-key", "Lowercase sorts later", now);
        var expired = Source(track, "0-expired", "Expired", now);
        expired.RevalidateAfter = now;
        var stale = Source(track, "1-stale", "Stale", now);
        stale.AssociationFactsRevision = 1;
        stale.Revision = 2;
        var conflict = Source(track, "2-conflicted", "Conflict", now);
        conflict.AssociationState = RecordingAssociationState.Conflicted;
        track.ProviderItems = [lower, expired, conflict, stale, chosen];

        var user = TestUserFactory.Create(991, "postgres-selection@example.test");
        var playlist = new Playlist { Id = Guid.NewGuid(), UserId = user.Id, Name = "Selection" };
        db.AddRange(track, user, playlist, new PlaylistEntry { OwnerUserId = playlist.UserId,
            Id = Guid.NewGuid(), PlaylistId = playlist.Id, TrackId = track.Id, Position = 0
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var current = ProviderCatalogPolicy.CurrentSources(db.ProviderCatalogItems.AsNoTracking(), now);
        var query = db.Tracks.Include(track => track.ProviderItems).AsNoTracking().Where(item => item.Id == track.Id)
            .Select(item => new
            {
                item.Id,
                Title = current.Where(source => source.TrackId == item.Id)
                    .OrderBy(source => EF.Functions.Collate(source.SourceType, "C"))
                    .ThenBy(source => EF.Functions.Collate(source.ExternalId, "C"))
                    .Select(source => source.NormalizedTitle).FirstOrDefault()
            }).OrderBy(item => item.Id).Take(2);
        var sql = query.ToQueryString();
        output.WriteLine(sql);
        Assert.Contains("COLLATE \"C\"", sql);
        Assert.Contains("LIMIT 1", sql);
        Assert.Contains("LIMIT", sql);
        Assert.DoesNotContain("FactsJson", sql);
        Assert.Equal("chosen", Assert.Single(await query.ToArrayAsync()).Title);

        var loaded = await db.Tracks.Include(track => track.ProviderItems).AsNoTracking().Include(item => item.ProviderItems)
            .SingleAsync();
        Assert.Equal(chosen.ExternalId, ProviderCatalogPolicy.SharedSource(loaded, now)!.ExternalId);

        var capture = new QueryCapture();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(db.Database.GetConnectionString()).AddInterceptors(capture).Options;
        await using var searchDb = new ApplicationDbContext(options);
        var search = new CantaroSearchService(searchDb, new MediaProviderRegistry([]), new MediaProviderSearchCache(),
            new MediaCatalogCanonicalizationService(options, NullLogger<MediaCatalogCanonicalizationService>.Instance),
            NullLogger<CantaroSearchService>.Instance);
        var results = await search.SearchAsync(user.Id, "Chosen", 1, false, default);
        Assert.Equal("Chosen", Assert.Single(results.Groups.Songs.Items).Title);
        var searchSql = Assert.Single(capture.Commands.Where(command => command.Contains("FROM \"Tracks\"")));
        output.WriteLine("Actual song search command:\n" + searchSql);
        Assert.Contains("COLLATE \"C\"", searchSql);
        Assert.Contains("LIMIT", searchSql);
        Assert.DoesNotContain("CanonicalMetadata", searchSql);
        Assert.DoesNotContain("IndependentEvidenceJson", searchSql);
        Assert.DoesNotContain("RawMetadata", searchSql);
    }

    private sealed class QueryCapture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private static ProviderCatalogItem Source(Track track, string key, string title, DateTimeOffset now)
    {
        var item = new ProviderCatalogItem
        {
            Id = Guid.NewGuid(), SourceType = "spotify", ExternalId = key,
            FactsJson = JsonSerializer.Serialize(new ProviderCatalogFacts { Title = title, Artist = "Artist" }),
            VisibilityState = CatalogVisibility.Public, VerificationMethod = CatalogVerificationMethods.SpotifyApplication,
            VerifiedAt = now.AddMinutes(-1), RevalidateAfter = now.AddDays(1), Revision = 1
        };
        return CatalogFixture.Associate(item, track.Id, RecordingAssociationState.Verified, item.Revision);
    }
}
