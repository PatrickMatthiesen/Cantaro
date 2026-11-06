using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class TrackMatchingManualCatalogTests
{
    [Fact]
    public async Task AcceptCandidateAsync_KeepsTrustedCatalogFactsWithoutCreatingGlobalAssociation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var user = TestUserFactory.Create(900001, "manual-catalog-owner@example.test");
        var now = DateTimeOffset.UtcNow;
        var catalogItem = new ProviderCatalogItem
        {
            Id = Guid.NewGuid(),
            SourceType = "spotify",
            ExternalId = "manual-selected-public-item",
            FactsJson = JsonSerializer.Serialize(new ProviderCatalogFacts
            {
                Title = "Public facts song",
                Artist = "Public facts artist",
                Isrc = "USABC1234567"
            }),
            VerificationMethod = CatalogVerificationMethods.SpotifyApplication,
            VerifiedAt = now.AddMinutes(-1),
            RevalidateAfter = now.AddHours(1),
            VisibilityState = CatalogVisibility.Public,
            Revision = 7
        };
        var observation = new TrackObservation
        {
            OwnerUserId = user.Id,
            Id = Guid.NewGuid(),
            SourceType = "youtube",
            ExternalId = "owner-video",
            Title = "A user provided title",
            Artist = "A user provided artist",
            MatchStatus = TrackMatchingStatuses.Ambiguous,
            CreatedAt = now,
            UpdatedAt = now
        };
        var candidate = new TrackResolutionCandidate
        {
            Id = Guid.NewGuid(),
            TrackObservationId = observation.Id,
            CandidateSource = catalogItem.SourceType,
            ExternalId = catalogItem.ExternalId,
            Title = "Public facts song",
            Artist = "Public facts artist",
            Isrc = "USABC1234567",
            Score = 0.99m,
            CreatedAt = now,
            RawMetadata = JsonSerializer.Serialize(new TrackMatchCandidateStoredMetadata
            {
                ProviderCatalogItemId = catalogItem.Id,
                ProviderCatalogRevision = catalogItem.Revision
            })
        };
        observation.Candidates.Add(candidate);
        db.AddRange(user, catalogItem, observation);
        await db.SaveChangesAsync();

        var result = await new TrackMatchingService(db, [], NullLogger<TrackMatchingService>.Instance)
            .AcceptCandidateAsync(observation.Id, candidate.Id, CancellationToken.None);

        Assert.Equal(TrackMatchingStatuses.Matched, result.MatchStatus);
        Assert.NotNull(result.TrackId);
        Assert.Equal(CatalogVisibility.Public, catalogItem.VisibilityState);
        Assert.Equal("Public facts song", JsonSerializer.Deserialize<ProviderCatalogFacts>(catalogItem.FactsJson)!.Title);
        Assert.Empty(await db.TrackSourceIds.ToListAsync());
        var track = await db.Tracks.SingleAsync();
        Assert.Equal(result.TrackId, track.Id);
        Assert.Null(track.CanonicalMetadata);
    }
}
