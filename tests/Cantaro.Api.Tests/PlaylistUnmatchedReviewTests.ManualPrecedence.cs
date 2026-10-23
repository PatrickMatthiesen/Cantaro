using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed partial class PlaylistUnmatchedReviewTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Confirmation_PrecedesLaterOwnedObservationAndCurrentProviderOccurrence(
        bool currentOccurrence, bool sourceObservation)
    {
        await using var scope = await Scope.CreateAsync("spotify");
        // Keep the canonical entry and its metadata unchanged by subsequent provider reads.
        if (sourceObservation) scope.Link.SyncMode = "from_cantaro";
        else
        {
            scope.Entry.TrackObservationId = null;
            scope.Entry.TrackObservation = null;
        }
        await scope.Db.SaveChangesAsync();
        await scope.Review.ConfirmAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id,
            scope.Entry.Id, scope.Token(Candidate("chosen-id")), default);
        var proof = PlaylistMatchingProgress.Read(scope.Link.MatchingProgressJson)[scope.Entry.Id];
        await AddOwnedAlternativeAsync(scope, "alternative-a");
        var provider = new ReviewResolverProvider(scope.Db, scope.Playlist.Name)
        {
            RemoteIds = currentOccurrence ? ["alternative-a"] : []
        };
        var writer = new ReviewWriter(provider);
        scope.Db.ChangeTracker.Clear();

        Assert.Empty(await scope.Review.ListAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, default));
        Assert.Equal("chosen-id", await provider.ResolveAsync(
            new PlatformAccountContext(scope.UserId, 51, "account"), scope.Entry, default));
        await RunReviewSyncAsync(scope, provider, writer);
        await AssertManualChoiceAsync(scope, proof);
        Assert.Equal(["chosen-id"], Assert.Single(writer.Writes));

        scope.Db.ChangeTracker.Clear();
        await RunReviewSyncAsync(scope, provider, writer);
        await AssertManualChoiceAsync(scope, proof);
        Assert.Equal(["chosen-id"], provider.RemoteIds);
        Assert.Single(writer.Writes); // the next pass must retain B without rewriting A
        Assert.Equal(0, provider.Searches);
        Assert.Equal(5, provider.Reads); // initial, pre-write, verification; then two unchanged reads
    }

    [Fact]
    public async Task Confirmation_DoesNotConsumeAnotherIdNeededByLaterCanonicalOccurrences()
    {
        await using var scope = await Scope.CreateAsync("spotify");
        scope.Playlist.AllowDuplicateTracks = true;
        scope.Link.SyncMode = "from_cantaro";
        scope.Entry.TrackObservationId = null;
        scope.Entry.TrackObservation = null;
        await scope.Db.SaveChangesAsync();
        await scope.Review.ConfirmAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id,
            scope.Entry.Id, scope.Token(Candidate("chosen-id")), default);
        var proof = PlaylistMatchingProgress.Read(scope.Link.MatchingProgressJson)[scope.Entry.Id];
        await AddOwnedAlternativeAsync(scope, "alternative-a");
        scope.Db.TrackSourceIds.Add(new TrackSourceId { Id = Guid.NewGuid(), TrackId = scope.Entry.TrackId!.Value,
            SourceType = "spotify", ExternalId = "alternative-c" });
        var repeats = Enumerable.Range(1, 2).Select(position => new PlaylistEntry
        {
            Id = Guid.NewGuid(), PlaylistId = scope.Playlist.Id, TrackId = scope.Entry.TrackId, Position = position
        }).ToArray();
        scope.Db.PlaylistEntries.AddRange(repeats);
        await scope.Db.SaveChangesAsync();
        var expectedEntries = new[] { scope.Entry.Id }.Concat(repeats.Select(entry => entry.Id)).ToArray();
        var provider = new ReviewResolverProvider(scope.Db, scope.Playlist.Name)
        {
            RemoteIds = ["alternative-a", "alternative-c"]
        };
        var writer = new ReviewWriter(provider);
        scope.Db.ChangeTracker.Clear();

        await RunReviewSyncAsync(scope, provider, writer);
        Assert.Equal(["chosen-id", "alternative-a", "alternative-c"], Assert.Single(writer.Writes));
        await AssertManualChoiceAsync(scope, proof);
        scope.Db.ChangeTracker.Clear();
        await RunReviewSyncAsync(scope, provider, writer);
        Assert.Single(writer.Writes);
        Assert.Equal(["chosen-id", "alternative-a", "alternative-c"], provider.RemoteIds);
        Assert.Equal(expectedEntries, await scope.Db.PlaylistEntries.Where(entry => entry.PlaylistId == scope.Playlist.Id)
            .OrderBy(entry => entry.Position).Select(entry => entry.Id).ToArrayAsync());
        Assert.Equal(0, provider.Searches);
        await AssertManualChoiceAsync(scope, proof);
    }

    [Fact]
    public async Task ManualProof_PrecedesAttachedDestinationObservation()
    {
        await using var scope = await Scope.CreateAsync("spotify");
        scope.Link.SyncMode = "from_cantaro";
        scope.Entry.TrackObservation!.SourceType = "spotify";
        scope.Entry.TrackObservation.ExternalId = "alternative-a";
        scope.Db.TrackSourceIds.Add(new TrackSourceId { Id = Guid.NewGuid(), TrackId = scope.Entry.TrackId!.Value,
            SourceType = "spotify", ExternalId = "chosen-id" });
        var proof = PlaylistMatchingProgress.Create(scope.Link, scope.Entry, "chosen-id", manualConfirmation: true);
        scope.Link.MatchingProgressJson = PlaylistMatchingProgress.Serialize(new() { [scope.Entry.Id] = proof });
        await scope.Db.SaveChangesAsync();
        scope.Db.ChangeTracker.Clear();
        var provider = new ReviewResolverProvider(scope.Db, scope.Playlist.Name);
        var writer = new ReviewWriter(provider);

        Assert.Equal("chosen-id", await provider.ResolveAsync(
            new PlatformAccountContext(scope.UserId, 51, "account"), scope.Entry, default));
        await RunReviewSyncAsync(scope, provider, writer);
        Assert.Equal(["chosen-id"], Assert.Single(writer.Writes));
        Assert.Equal(0, provider.Searches);
        await AssertManualChoiceAsync(scope, proof);
    }

    [Theory]
    [InlineData("search-checkpoint")]
    [InlineData("metadata")]
    [InlineData("scope")]
    [InlineData("conflict")]
    [InlineData("legacy")]
    public async Task InvalidOrAutomaticProof_DoesNotOverrideOwnedObservationOrCurrentRead(string invalidation)
    {
        await using var scope = await Scope.CreateAsync("spotify");
        scope.Entry.TrackObservationId = null;
        scope.Entry.TrackObservation = null;
        await scope.Db.SaveChangesAsync();
        await scope.Review.ConfirmAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id,
            scope.Entry.Id, scope.Token(Candidate("chosen-id")), default);
        var proof = PlaylistMatchingProgress.Read(scope.Link.MatchingProgressJson)[scope.Entry.Id];
        switch (invalidation)
        {
            case "metadata": scope.Entry.Track!.SearchTitle = "Changed title"; break;
            case "scope": proof = proof with { Scope = proof.Scope! with { EntryId = Guid.NewGuid() } }; break;
            case "legacy": proof = proof with { ProofVersion = 1 }; break;
            case "conflict":
                var other = new Track { Id = Guid.NewGuid(), SearchTitle = "Other recording" };
                scope.Db.Tracks.Add(other);
                (await scope.Db.TrackSourceIds.SingleAsync()).TrackId = other.Id;
                break;
        }
        await AddOwnedAlternativeAsync(scope, "alternative-a");
        // A shared private mapping is never a fallback authorization.
        scope.Db.TrackSourceIds.Add(new TrackSourceId { Id = Guid.NewGuid(), TrackId = scope.Entry.TrackId!.Value,
            SourceType = "spotify", ExternalId = "global-private-id" });
        await scope.Db.SaveChangesAsync();
        if (invalidation == "search-checkpoint")
            proof = PlaylistMatchingProgress.Create(scope.Link, scope.Entry, "chosen-id");
        scope.Link.MatchingProgressJson = PlaylistMatchingProgress.Serialize(new() { [scope.Entry.Id] = proof });
        await scope.Db.SaveChangesAsync();
        scope.Db.ChangeTracker.Clear();
        if (invalidation == "search-checkpoint")
        {
            var entry = await scope.Db.PlaylistEntries.AsNoTracking().Include(item => item.Track)
                .ThenInclude(track => track!.SourceIds).SingleAsync(item => item.Id == scope.Entry.Id);
            Assert.True(PlaylistMatchingProgress.CanReuse(proof, entry, scope.Link));
            Assert.False(proof.ManualConfirmation);
        }
        var provider = new ReviewResolverProvider(scope.Db, scope.Playlist.Name) { RemoteIds = ["alternative-a"] };
        var writer = new ReviewWriter(provider);
        Assert.Empty(await scope.Review.ListAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, default));
        Assert.Equal("alternative-a", await provider.ResolveAsync(
            new PlatformAccountContext(scope.UserId, 51, "account"), scope.Entry, default));

        await RunReviewSyncAsync(scope, provider, writer);

        Assert.Equal(["alternative-a"], provider.RemoteIds);
        Assert.Empty(writer.Writes);
        Assert.Empty(PlaylistMatchingProgress.Read((await scope.Db.ServicePlaylistMappings.SingleAsync()).MatchingProgressJson));
        Assert.Equal(0, provider.Searches);
    }

    private static async Task AddOwnedAlternativeAsync(Scope scope, string externalId)
    {
        var playlist = new Playlist { Id = Guid.NewGuid(), UserId = scope.UserId, Name = "Another owned playlist" };
        var observation = new TrackObservation { Id = Guid.NewGuid(), SourceType = "spotify", ExternalId = externalId,
            TrackId = scope.Entry.TrackId, Title = "Everything Goes On", Artist = "Porter Robinson",
            MatchStatus = TrackMatchingStatuses.Matched };
        scope.Db.AddRange(new PlaylistEntry { Id = Guid.NewGuid(), Playlist = playlist, PlaylistId = playlist.Id,
            TrackId = scope.Entry.TrackId, TrackObservation = observation, TrackObservationId = observation.Id },
            new TrackSourceId { Id = Guid.NewGuid(), TrackId = scope.Entry.TrackId!.Value,
                SourceType = "spotify", ExternalId = externalId });
        await scope.Db.SaveChangesAsync();
    }

    private static async Task AssertManualChoiceAsync(Scope scope, PlaylistMatchingResult expected)
    {
        var link = await scope.Db.ServicePlaylistMappings.AsNoTracking().SingleAsync();
        Assert.Equal("success", link.LastSyncStatus);
        Assert.Equal(expected, Assert.Single(PlaylistMatchingProgress.Read(link.MatchingProgressJson)).Value);
        Assert.True(expected.ManualConfirmation);
    }
}
