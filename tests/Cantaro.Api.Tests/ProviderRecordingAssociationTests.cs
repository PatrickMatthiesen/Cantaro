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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AssociationAnchorExcludesDisplayPayloadAndCannotRevalidateChangedRecordingAgainstItself(bool hasUnverifiedEvidence)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.UtcNow;
        var track = new Track {  Id = Guid.NewGuid()  };
        var facts = new ProviderCatalogFacts
        {
            Title = "Original", Artist = "Artist", Isrc = "USABC1234567", DurationSeconds = 180,
            Albums = ["Album payload"], ThumbnailUrl = "https://example.test/cover.jpg",
            ProviderUrl = "https://example.test/recording",
            ArtistCredits = [new ArtistCreditMetadata
            {
                Name = "Artist", Provider = "spotify", ExternalId = "artist-1",
                ProfileUrl = "https://example.test/artist", ArtworkUrl = "https://example.test/artist.jpg",
                RawMetadata = "private payload"
            }]
        };
        var item = new ProviderCatalogItem
        {
            Id = Guid.NewGuid(), SourceType = "spotify", ExternalId = "anchor", Revision = 1,
            FactsJson = JsonSerializer.Serialize(facts), VisibilityState = CatalogVisibility.Public,
            VerificationMethod = CatalogVerificationMethods.SpotifyApplication,
            VerifiedAt = now, RevalidateAfter = now.AddDays(1),
            AssociationEvidenceJson = hasUnverifiedEvidence ? "{\"Title\":\"Unverified previous title\"}" : null
        };
        db.AddRange(track, item);
        var associations = new ProviderRecordingAssociations(db);
        Assert.True(await associations.TryAssociateAsync(track, item, null, true, default));
        await db.SaveChangesAsync();
        var mapping = await db.ProviderCatalogItems.SingleAsync();
        var anchorJson = mapping.AssociationEvidenceJson!;
        using var anchor = JsonDocument.Parse(anchorJson);
        Assert.Equal("Original", anchor.RootElement.GetProperty("Title").GetString());
        Assert.False(anchor.RootElement.TryGetProperty("Albums", out _));
        Assert.False(anchor.RootElement.TryGetProperty("ThumbnailUrl", out _));
        Assert.False(anchor.RootElement.TryGetProperty("ProviderUrl", out _));
        var credit = Assert.Single(anchor.RootElement.GetProperty("ArtistCredits").EnumerateArray());
        Assert.False(credit.TryGetProperty("ProfileUrl", out _));
        Assert.False(credit.TryGetProperty("ArtworkUrl", out _));
        Assert.False(credit.TryGetProperty("RawMetadata", out _));
        Assert.Equal("artist-1", credit.GetProperty("ExternalId").GetString());

        item.FactsJson = JsonSerializer.Serialize(new ProviderCatalogFacts
        {
            Title = "Replacement", Artist = "Artist", Isrc = "USABC7654321", DurationSeconds = 180
        });
        item.Revision++;
        await db.SaveChangesAsync();
        Assert.Null(await associations.RevalidateExactAsync(item, default));
        Assert.Equal(anchorJson, mapping.AssociationEvidenceJson);
        Assert.False(ProviderCatalogPolicy.IsVerified(mapping, DateTimeOffset.UtcNow));
        Assert.Equal(track.Id, mapping.TrackId);
    }

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
            var track = new Track {  Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now  };
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
        var sourceTrack = await associating.Tracks.Include(track => track.ProviderItems).SingleAsync();
        var catalog = await associating.ProviderCatalogItems.SingleAsync();
        var associations = new ProviderRecordingAssociations(associating);
        Assert.True(await associations.TryAssociateAsync(sourceTrack, catalog, null, providerOrigin: true, default));
        Assert.Equal(1, associating.ChangeTracker.Entries<ProviderCatalogItem>().Count(entry => entry.State == EntityState.Modified));

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
        Assert.Null(withdrawn.TrackId);
        Assert.Equal(0, withdrawn.AssociationRevision);
        var storedTrack = await verify.Tracks.Include(track => track.ProviderItems).SingleAsync();
        Assert.Null(ProviderCatalogPolicy.SharedSource(storedTrack, DateTimeOffset.UtcNow)?.Id);
        Assert.Null(storedTrack.Isrc);
    }
    [Fact]
    public async Task ConcurrentAssociationSaveChecksAssociationGenerationWithoutChangingFactsGeneration()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var setup = new ApplicationDbContext(options);
        await setup.Database.EnsureCreatedAsync();
        var track = new Track { Id = Guid.NewGuid() };
        var now = DateTimeOffset.UtcNow;
        var item = new ProviderCatalogItem
        {
            Id = Guid.NewGuid(), SourceType = "spotify", ExternalId = "concurrent-association",
            FactsJson = JsonSerializer.Serialize(new ProviderCatalogFacts { Title = "Song", Artist = "Artist" }),
            Revision = 1, VisibilityState = CatalogVisibility.Public,
            VerificationMethod = CatalogVerificationMethods.SpotifyApplication,
            VerifiedAt = now, RevalidateAfter = now.AddDays(1)
        };
        setup.AddRange(track, item);
        await setup.SaveChangesAsync();
        await using var first = new ApplicationDbContext(options);
        await using var second = new ApplicationDbContext(options);
        var firstItem = await first.ProviderCatalogItems.SingleAsync();
        var secondItem = await second.ProviderCatalogItems.SingleAsync();
        Assert.True(await new ProviderRecordingAssociations(first).TryAssociateAsync(
            await first.Tracks.Include(track => track.ProviderItems).SingleAsync(), firstItem, null, true, default));
        Assert.True(await new ProviderRecordingAssociations(second).TryAssociateAsync(
            await second.Tracks.Include(track => track.ProviderItems).SingleAsync(), secondItem, null, true, default));
        await first.SaveChangesAsync();
        Assert.Equal(1, firstItem.Revision);
        Assert.Equal(1, firstItem.AssociationRevision);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

}
