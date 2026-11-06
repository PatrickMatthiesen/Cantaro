using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class ProviderRecordingAssociationTests
{
    [Fact]
    public async Task TryAssociateAsync_RejectsCatalogWithdrawnAfterAssociationWasStaged()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using (var setup = new ApplicationDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            var now = DateTimeOffset.UtcNow;
            var song = new Song { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now };
            var track = new Track { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now };
            var item = new ProviderCatalogItem
            {
                Id = Guid.NewGuid(), SourceType = "spotify", ExternalId = "recording-key", Revision = 1,
                FactsJson = JsonSerializer.Serialize(new ProviderCatalogFacts { Title = "Public title", Artist = "Public artist" }),
                VerificationMethod = CatalogVerificationMethods.SpotifyApplication,
                VisibilityState = CatalogVisibility.Public, VerifiedAt = now, RevalidateAfter = now.AddDays(1)
            };
            setup.AddRange(song, track, item, new SongTrack { SongId = song.Id, Song = song, TrackId = track.Id, Track = track });
            await setup.SaveChangesAsync();
        }

        await using var associating = new ApplicationDbContext(options);
        var sourceTrack = await associating.Tracks.SingleAsync();
        var catalog = await associating.ProviderCatalogItems.SingleAsync();
        var associations = new ProviderRecordingAssociations(associating);
        Assert.True(await associations.TryAssociateAsync(sourceTrack, catalog, null, providerOrigin: true, default));
        Assert.Equal(1, associating.ChangeTracker.Entries<TrackSourceId>().Count(entry => entry.State == EntityState.Added));

        await using (var withdrawing = new ApplicationDbContext(options))
        {
            var current = await withdrawing.ProviderCatalogItems.SingleAsync();
            current.VisibilityState = CatalogVisibility.Withdrawn;
            current.Revision++;
            await withdrawing.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => associating.SaveChangesAsync());

        await using var verify = new ApplicationDbContext(options);
        var withdrawn = await verify.ProviderCatalogItems.SingleAsync();
        Assert.Equal(CatalogVisibility.Withdrawn, withdrawn.VisibilityState);
        Assert.Equal(2, withdrawn.Revision);
        Assert.Empty(await verify.TrackSourceIds.ToListAsync());
        var storedTrack = await verify.Tracks.SingleAsync();
        Assert.Null(storedTrack.PreferredCatalogItemId);
        Assert.Null(storedTrack.Isrc);
    }
}
