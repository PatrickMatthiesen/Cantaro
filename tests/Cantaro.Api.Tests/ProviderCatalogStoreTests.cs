using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class ProviderCatalogStoreTests
{
    [Fact]
    public async Task PublicFactsAreSharedWithoutTrackAssociationAndLeaseRefreshDoesNotChangeRevision()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection()
            .AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection))
            .AddSingleton<TimeProvider>(TimeProvider.System)
            .BuildServiceProvider();
        await using (var scope = services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync();
        var store = new ProviderCatalogStore(services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System);
        var facts = new ProviderCatalogFacts { Title = "Song", Artist = "Artist", Isrc = "USABC1234567" };

        var first = await store.RecordVerifiedAsync("spotify", "provider-id",
            CatalogVerificationMethods.SpotifyApplication, facts, default);
        var refreshed = await store.RecordVerifiedAsync("spotify", "provider-id",
            CatalogVerificationMethods.SpotifyApplication, facts, default);

        Assert.Equal(first.Id, refreshed.Id);
        Assert.Equal(first.Revision, refreshed.Revision);
        Assert.True(refreshed.VerifiedAt >= first.VerifiedAt);
        Assert.Equal(CatalogVisibility.Public, refreshed.VisibilityState);
        Assert.Empty(await ReadSourcesAsync(services));

        var changed = await store.RecordVerifiedAsync("spotify", "provider-id",
            CatalogVerificationMethods.SpotifyApplication,
            new ProviderCatalogFacts { Title = "Song (Live)", Artist = "Artist", Isrc = "USABC1234567" }, default);
        Assert.Equal(first.Revision + 1, changed.Revision);

        await store.MarkWithdrawnAsync("spotify", "provider-id", default);
        var withdrawn = await store.FindAsync("spotify", "provider-id", default);
        Assert.Equal(CatalogVisibility.Withdrawn, withdrawn!.VisibilityState);
        Assert.Equal(changed.Revision + 1, withdrawn.Revision);
    }

    private static async Task<TrackSourceId[]> ReadSourcesAsync(ServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().TrackSourceIds.ToArrayAsync();
    }
}
