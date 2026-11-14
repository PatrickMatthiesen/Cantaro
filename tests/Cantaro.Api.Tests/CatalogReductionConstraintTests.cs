using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class CatalogReductionConstraintTests
{
    [PostgresTheory]
    [InlineData("observation_owner")]
    [InlineData("playlist_owner")]
    [InlineData("account_owner")]
    [InlineData("account_service")]
    [InlineData("source_key")]
    [InlineData("accepted_candidate")]
    public async Task DatabaseRejectsInconsistentReferences(string kind)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        var db = schema.Db;
        db.Users.AddRange(TestUserFactory.Create(1, "one@constraints.test"), TestUserFactory.Create(2, "two@constraints.test"));
        var one = new Playlist { Id = Guid.NewGuid(), UserId = 1, Name = "One" };
        var two = new Playlist { Id = Guid.NewGuid(), UserId = 2, Name = "Two" };
        var observation = Private("one"); var other = Private("other");
        var item = new ProviderCatalogItem { Id = Guid.NewGuid(), SourceType = "spotify", ExternalId = "shared", FactsJson = "{}" };
        var account = new ConnectedServiceAccount { UserId = 1, Service = "spotify", ExternalAccountId = "bound" };
        var candidate = new TrackResolutionCandidate { Id = Guid.NewGuid(), TrackObservationId = other.Id,
            CandidateSource = "manual", ExternalId = "candidate", Title = "Candidate", Score = 1 };
        db.AddRange(one, two, observation, other, item, account, candidate);
        await db.SaveChangesAsync();
        switch (kind)
        {
            case "observation_owner":
                db.PlaylistEntries.Add(new PlaylistEntry { Id = Guid.NewGuid(), PlaylistId = two.Id, OwnerUserId = 2,
                    TrackObservationId = observation.Id });
                break;
            case "playlist_owner":
                db.PlaylistEntries.Add(new PlaylistEntry { Id = Guid.NewGuid(), PlaylistId = one.Id, OwnerUserId = 2 });
                break;
            case "account_owner": case "account_service":
                db.ServicePlaylistMappings.Add(new ServicePlaylistMapping { Id = Guid.NewGuid(),
                    PlaylistId = kind == "account_owner" ? two.Id : one.Id,
                    UserId = kind == "account_owner" ? 2 : 1, ConnectedServiceAccountId = account.Id,
                    Service = kind == "account_service" ? "youtube" : "spotify", ExternalAccountId = "bound",
                    ServicePlaylistId = "remote", SyncMode = "bidirectional" });
                break;
            case "source_key": observation.ProviderCatalogItemId = item.Id; break;
            case "accepted_candidate": observation.AcceptedCandidateId = candidate.Id; break;
        }
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
    }

    [PostgresTheory]
    [InlineData(ObservationInputKind.Public, "copied public title")]
    [InlineData(ObservationInputKind.Reference, "copied reference title")]
    [InlineData(ObservationInputKind.Private, " ")]
    public async Task DatabaseEnforcesPrivateOnlyInput(ObservationInputKind kind, string invalidTitle)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        var db = schema.Db;
        db.Users.Add(TestUserFactory.Create(1, "input@constraints.test"));
        var item = new ProviderCatalogItem { Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = "one", FactsJson = "{}" };
        db.ProviderCatalogItems.Add(item);
        await db.SaveChangesAsync();
        var observation = Private("one");
        observation.ProviderCatalogItemId = item.Id;
        observation.InputKind = kind; observation.Title = invalidTitle;
        db.TrackObservations.Add(observation);
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
    }

    private static TrackObservation Private(string key) => new()
    {
        Id = Guid.NewGuid(), OwnerUserId = 1, SourceType = "youtube", ExternalId = key,
        InputKind = ObservationInputKind.Private, Title = "Private input", MatchStatus = "pending",
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
    };
}
