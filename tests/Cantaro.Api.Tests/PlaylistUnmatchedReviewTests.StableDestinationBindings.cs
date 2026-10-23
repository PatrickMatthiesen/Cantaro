using System.Text.Json;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed partial class PlaylistUnmatchedReviewTests
{
    [Theory]
    [InlineData(true, "artwork")]
    [InlineData(false, "artwork")]
    [InlineData(true, "title")]
    [InlineData(false, "title")]
    [InlineData(true, "duration")]
    [InlineData(false, "duration")]
    public Task AcknowledgedDestination_MetadataUpdatePreservesOriginalMembership(bool manual, string update)
        => MetadataUpdatePreservesOriginalMembershipAsync(manual, update);

    private static async Task MetadataUpdatePreservesOriginalMembershipAsync(bool manual, string update, bool postgres = false)
    {
        await using var scope = await Scope.CreateAsync("youtube", relational: !postgres, postgres: postgres);
        var (provider, writer) = await ExportScopedDestinationAsync(scope, manual);
        var track = await scope.Db.Tracks.SingleAsync(item => item.Id == scope.Entry.TrackId);
        switch (update)
        {
            case "artwork":
                Assert.True(TrackArtworkUpdater.FillMissingCanonicalThumbnail(track, "https://example.invalid/art.jpg"));
                break;
            case "title": track.SearchTitle += " (updated)"; break;
            case "duration":
                var metadata = JsonSerializer.Deserialize<TrackCanonicalMetadata>(track.CanonicalMetadata!)!;
                metadata.DurationSeconds += 1;
                track.CanonicalMetadata = JsonSerializer.Serialize(metadata);
                break;
        }
        await scope.Db.SaveChangesAsync();
        var changed = await scope.Db.PlaylistEntries.AsNoTracking().Include(item => item.Track)
            .ThenInclude(item => item!.SourceIds).Include(item => item.TrackObservation).SingleAsync();
        var proof = PlaylistMatchingProgress.Read((await scope.Db.ServicePlaylistMappings.SingleAsync()).MatchingProgressJson)[changed.Id];
        Assert.False(PlaylistMatchingProgress.CanReuse(proof, changed, scope.Link));

        for (var pass = 0; pass < 2; pass++)
        {
            await RunDestinationSyncAsync(scope, provider, writer);
            var saved = await scope.Db.PlaylistEntries.AsNoTracking().SingleAsync();
            Assert.Equal((scope.Entry.Id, scope.Entry.TrackId, scope.Entry.TrackObservationId),
                (saved.Id, saved.TrackId, saved.TrackObservationId));
            Assert.Equal(["chosen-id"], provider.RemoteIds);
            Assert.Equal("success", (await scope.Db.ServicePlaylistMappings.SingleAsync()).LastSyncStatus);
        }
        Assert.Single(writer.Writes);
        await AssertDestinationRemainsScopedAsync(scope);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public Task AcknowledgedDestination_RepeatedRemoteIdNormalizesBeforeMerge(bool manual, bool duplicates)
        => RepeatedRemoteIdNormalizesBeforeMergeAsync(manual, duplicates);

    private static async Task RepeatedRemoteIdNormalizesBeforeMergeAsync(bool manual, bool duplicates, bool postgres = false)
    {
        await using var scope = await Scope.CreateAsync("youtube", relational: !postgres, postgres: postgres);
        scope.Playlist.AllowDuplicateTracks = duplicates;
        await scope.Db.SaveChangesAsync();
        var (provider, writer) = await ExportScopedDestinationAsync(scope, manual);
        provider.RemoteIds = ["chosen-id", "chosen-id"];
        for (var pass = 0; pass < 2; pass++)
        {
            await RunDestinationSyncAsync(scope, provider, writer);
            var saved = await scope.Db.PlaylistEntries.AsNoTracking().OrderBy(item => item.Position).ToArrayAsync();
            Assert.Equal(duplicates ? 2 : 1, saved.Length);
            Assert.Equal(scope.Entry.Id, saved[0].Id);
            Assert.All(saved, item => Assert.Equal(scope.Entry.TrackId, item.TrackId));
            Assert.Equal(Enumerable.Repeat("chosen-id", saved.Length), provider.RemoteIds);
            Assert.Equal("success", (await scope.Db.ServicePlaylistMappings.SingleAsync()).LastSyncStatus);
        }
        Assert.Equal(duplicates ? 1 : 2, writer.Writes.Count);
        await AssertDestinationRemainsScopedAsync(scope);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public Task AcknowledgedDestination_RealRemovalOnEitherSidePropagates(bool manual, bool removeLocal)
        => RealRemovalOnEitherSidePropagatesAsync(manual, removeLocal);

    private static async Task RealRemovalOnEitherSidePropagatesAsync(bool manual, bool removeLocal, bool postgres = false)
    {
        await using var scope = await Scope.CreateAsync("youtube", relational: !postgres, postgres: postgres);
        var (provider, writer) = await ExportScopedDestinationAsync(scope, manual);
        if (removeLocal)
            scope.Db.PlaylistEntries.Remove(await scope.Db.PlaylistEntries.SingleAsync());
        else provider.RemoteIds = [];
        await scope.Db.SaveChangesAsync();

        for (var pass = 0; pass < 2; pass++)
        {
            await RunDestinationSyncAsync(scope, provider, writer);
            Assert.Empty(await scope.Db.PlaylistEntries.ToArrayAsync());
            Assert.Empty(provider.RemoteIds);
            Assert.Equal("success", (await scope.Db.ServicePlaylistMappings.SingleAsync()).LastSyncStatus);
        }
        Assert.Equal(removeLocal ? 2 : 1, writer.Writes.Count);
        await AssertDestinationRemainsScopedAsync(scope);
    }

    [PostgresFact]
    public async Task AcknowledgedDestination_PostgresMetadataDuplicatesAndRemovalMatrix()
    {
        foreach (var manual in new[] { true, false })
        {
            foreach (var update in new[] { "artwork", "title", "duration" })
                await MetadataUpdatePreservesOriginalMembershipAsync(manual, update, postgres: true);
            foreach (var duplicates in new[] { true, false })
                await RepeatedRemoteIdNormalizesBeforeMergeAsync(manual, duplicates, postgres: true);
            foreach (var removeLocal in new[] { true, false })
                await RealRemovalOnEitherSidePropagatesAsync(manual, removeLocal, postgres: true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AcknowledgedDestination_DoesNotRefreshStalePermissionForNewExport(bool manual)
    {
        await using var scope = await Scope.CreateAsync("youtube", relational: true);
        var (provider, writer) = await ExportScopedDestinationAsync(scope, manual);
        var track = await scope.Db.Tracks.SingleAsync(item => item.Id == scope.Entry.TrackId);
        track.SearchTitle += " updated";
        var link = await scope.Db.ServicePlaylistMappings.SingleAsync();
        var proof = PlaylistMatchingProgress.Read(link.MatchingProgressJson)[scope.Entry.Id];
        link.SyncMode = "from_cantaro";
        await scope.Db.SaveChangesAsync();
        await RunDestinationSyncAsync(scope, provider, writer);
        Assert.Equal(proof, PlaylistMatchingProgress.Read((await scope.Db.ServicePlaylistMappings.SingleAsync()).MatchingProgressJson)[scope.Entry.Id]);
        var searches = provider.Searches;
        provider.RemoteIds = [];

        await RunDestinationSyncAsync(scope, provider, writer);

        Assert.Equal(searches + 1, provider.Searches);
        Assert.Single(writer.Writes);
        Assert.Empty(provider.RemoteIds);
        Assert.Equal("partial", (await scope.Db.ServicePlaylistMappings.SingleAsync()).LastSyncStatus);
        Assert.Equal(scope.Entry.Id, (await scope.Db.PlaylistEntries.SingleAsync()).Id);
    }

    [Theory]
    [InlineData("account")]
    [InlineData("playlist")]
    [InlineData("track")]
    [InlineData("observation")]
    [InlineData("global")]
    public async Task AcknowledgedDestination_RejectsChangedBindingOrIdentity(string change)
    {
        await using var scope = await Scope.CreateAsync("youtube", relational: true);
        var (provider, writer) = await ExportScopedDestinationAsync(scope, manual: true);
        var link = await scope.Db.ServicePlaylistMappings.SingleAsync();
        var other = new Track { Id = Guid.NewGuid(), SearchTitle = "Other recording" };
        scope.Db.Tracks.Add(other);
        switch (change)
        {
            case "account":
                var account = await scope.Db.ConnectedServiceAccounts.SingleAsync();
                account.ExternalAccountId = "other-account";
                link.ExternalAccountId = account.ExternalAccountId;
                break;
            case "playlist": link.ServicePlaylistId = "other-playlist"; break;
            case "track":
                var entry = await scope.Db.PlaylistEntries.SingleAsync();
                entry.TrackId = other.Id;
                entry.TrackObservationId = null;
                break;
            case "observation":
                (await scope.Db.TrackObservations.SingleAsync(item => item.Id == scope.Entry.TrackObservationId)).TrackId = other.Id;
                break;
            case "global":
                scope.Db.TrackSourceIds.Add(new TrackSourceId { Id = Guid.NewGuid(), TrackId = other.Id,
                    SourceType = "youtube", ExternalId = "chosen-id" });
                break;
        }
        await scope.Db.SaveChangesAsync();
        await RunDestinationSyncAsync(scope, provider, writer);
        var baseline = PlaylistSyncBaseline.Parse((await scope.Db.ServicePlaylistMappings.SingleAsync()).BaselineJson)!;
        Assert.DoesNotContain(baseline.Items, item => item.TrackId == scope.Entry.TrackId);
        Assert.DoesNotContain(await scope.Db.PlaylistEntries.AsNoTracking().ToArrayAsync(), item => item.TrackId == scope.Entry.TrackId);
        Assert.Single(writer.Writes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AcknowledgedDestination_LegacyBaselineUsesScopedProofAndStableIdentity(bool removeLocal)
    {
        await using var scope = await Scope.CreateAsync("youtube", relational: true);
        var (provider, writer) = await ExportScopedDestinationAsync(scope, manual: true);
        var link = await scope.Db.ServicePlaylistMappings.SingleAsync();
        var baseline = PlaylistSyncBaseline.Parse(link.BaselineJson)!;
        link.BaselineJson = new PlaylistSyncBaseline(baseline.Items.Select(item => item with { DestinationBinding = null }).ToList()).Serialize();
        (await scope.Db.Tracks.SingleAsync()).SearchTitle += " updated";
        if (removeLocal) scope.Db.PlaylistEntries.Remove(await scope.Db.PlaylistEntries.SingleAsync());
        await scope.Db.SaveChangesAsync();

        await RunDestinationSyncAsync(scope, provider, writer);
        if (removeLocal)
        {
            Assert.Empty(await scope.Db.PlaylistEntries.ToArrayAsync());
            Assert.Empty(provider.RemoteIds);
        }
        else
        {
            Assert.Equal(scope.Entry.Id, (await scope.Db.PlaylistEntries.SingleAsync()).Id);
            Assert.NotNull(Assert.Single(PlaylistSyncBaseline.Parse((await scope.Db.ServicePlaylistMappings.SingleAsync()).BaselineJson)!.Items).DestinationBinding);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcknowledgedDestination_SharedRemoteIdPreservesEachCanonicalAndSourceOccurrence(bool differentCanonical)
    {
        await using var scope = await Scope.CreateAsync("youtube", relational: true);
        scope.Playlist.AllowDuplicateTracks = true;
        var second = scope.AddEntry("Everything Goes On", "second-source");
        second.TrackObservation!.SourceType = "spotify";
        second.TrackObservation.Artist = "Porter Robinson";
        second.TrackObservation.DurationSeconds = 160;
        if (!differentCanonical)
        {
            second.TrackId = scope.Entry.TrackId;
            second.TrackObservation.TrackId = scope.Entry.TrackId;
        }
        await scope.Db.SaveChangesAsync();
        var candidate = new TrackMatchSearchCandidate { CandidateSource = "youtube", ExternalId = "chosen-id",
            Title = "Everything Goes On", Artist = "Porter Robinson", ArtistCredits = ["Porter Robinson"], DurationSeconds = 160 };
        foreach (var entry in new[] { scope.Entry, second })
            await scope.Review.ConfirmAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id,
                entry.Id, scope.Token(candidate, entryId: entry.Id), default);
        var expected = await scope.Db.PlaylistEntries.AsNoTracking().OrderBy(item => item.Position)
            .Select(item => new { item.Id, item.TrackId, item.TrackObservationId }).ToArrayAsync();
        scope.Link.BaselineName = scope.Playlist.Name;
        scope.Link.BaselineJson = new PlaylistSyncBaseline([]).Serialize();
        await scope.Db.SaveChangesAsync();
        var provider = new ReviewResolverProvider(scope.Db, scope.Playlist.Name, "youtube");
        var writer = new ReviewWriter(provider);
        await RunDestinationSyncAsync(scope, provider, writer);
        (await scope.Db.Tracks.SingleAsync(item => item.Id == scope.Entry.TrackId)).SearchTitle += " updated";
        await scope.Db.SaveChangesAsync();
        for (var pass = 0; pass < 2; pass++)
        {
            await RunDestinationSyncAsync(scope, provider, writer);
            Assert.Equal(expected, await scope.Db.PlaylistEntries.AsNoTracking().OrderBy(item => item.Position)
                .Select(item => new { item.Id, item.TrackId, item.TrackObservationId }).ToArrayAsync());
            Assert.Equal(["chosen-id", "chosen-id"], provider.RemoteIds);
        }
        Assert.Single(writer.Writes);
        Assert.Equal(0, provider.Searches);
        await AssertDestinationRemainsScopedAsync(scope);
        scope.Db.PlaylistEntries.Remove(await scope.Db.PlaylistEntries.SingleAsync(item => item.Id == scope.Entry.Id));
        await scope.Db.SaveChangesAsync();
        for (var pass = 0; pass < 2; pass++)
        {
            await RunDestinationSyncAsync(scope, provider, writer);
            var survivor = await scope.Db.PlaylistEntries.AsNoTracking().SingleAsync();
            Assert.Equal((second.Id, second.TrackId, second.TrackObservationId),
                (survivor.Id, survivor.TrackId, survivor.TrackObservationId));
            Assert.Equal(["chosen-id"], provider.RemoteIds);
            var baseline = PlaylistSyncBaseline.Parse((await scope.Db.ServicePlaylistMappings.SingleAsync()).BaselineJson)!;
            Assert.Equal(second.Id, Assert.Single(baseline.Items).DestinationBinding!.EntryId);
        }
        Assert.Equal(2, writer.Writes.Count);
    }

    private static async Task<(ReviewResolverProvider Provider, ReviewWriter Writer)> ExportScopedDestinationAsync(Scope scope, bool manual)
    {
        // PostgreSQL normalizes JSON metadata. Confirm against persisted evidence.
        scope.Db.ChangeTracker.Clear();
        var candidate = new TrackMatchSearchCandidate { CandidateSource = "youtube", ExternalId = "chosen-id",
            Title = "Everything Goes On", Artist = "Porter Robinson", ArtistCredits = ["Porter Robinson"], DurationSeconds = 160 };
        if (manual)
            await scope.Review.ConfirmAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id,
                scope.Entry.Id, scope.Token(candidate), default);
        var provider = new ReviewResolverProvider(scope.Db, scope.Playlist.Name, "youtube")
        {
            SearchResults = manual ? [] : [candidate]
        };
        var writer = new ReviewWriter(provider);
        var link = await scope.Db.ServicePlaylistMappings.SingleAsync();
        link.BaselineName = scope.Playlist.Name;
        link.BaselineJson = new PlaylistSyncBaseline([]).Serialize();
        await scope.Db.SaveChangesAsync();
        await RunDestinationSyncAsync(scope, provider, writer);
        Assert.Equal(["chosen-id"], Assert.Single(writer.Writes));
        Assert.Equal(manual, PlaylistMatchingProgress.Read((await scope.Db.ServicePlaylistMappings.SingleAsync()).MatchingProgressJson)
            [scope.Entry.Id].ManualConfirmation);
        provider.SearchResults = [];
        return (provider, writer);
    }

    private static async Task RunDestinationSyncAsync(Scope scope, ReviewResolverProvider provider, ReviewWriter writer)
    {
        scope.Db.ChangeTracker.Clear();
        var coordinator = new PlaylistSyncCoordinator(scope.Db, [provider], [writer], TimeProvider.System,
            NullLogger<PlaylistSyncCoordinator>.Instance);
        await coordinator.RunAsync(scope.UserId, scope.Playlist.Id, "youtube", default);
    }

    private static async Task AssertDestinationRemainsScopedAsync(Scope scope)
    {
        Assert.False(await scope.Db.TrackSourceIds.AnyAsync(item => item.SourceType == "youtube" && item.ExternalId == "chosen-id"));
        Assert.Null((await scope.Db.TrackObservations.SingleAsync(item => item.SourceType == "youtube" && item.ExternalId == "chosen-id")).TrackId);
        Assert.Empty(await scope.Db.TrackArtistCredits.ToArrayAsync());
    }
}
