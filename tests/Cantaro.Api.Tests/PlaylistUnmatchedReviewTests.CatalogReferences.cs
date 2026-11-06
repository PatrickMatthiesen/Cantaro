using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed partial class PlaylistUnmatchedReviewTests
{
    [Fact]
    public Task SourceReferenceRetainsAcknowledgedMembershipWithoutRenewingWritePermission()
        => CheckSourceReferenceMembershipAsync(false);

    [PostgresFact]
    public Task SourceReferencePostgresRetainsAcknowledgedMembershipWithoutRenewingWritePermission()
        => CheckSourceReferenceMembershipAsync(true);

    private static async Task CheckSourceReferenceMembershipAsync(bool postgres)
    {
        await using var fixture = await ProductionSpotifyReadScope.CreateAsync(postgres: postgres);
        await fixture.SyncAsync();
        Assert.Single(fixture.Writer.Writes);
        var observation = await fixture.Db.TrackObservations.SingleAsync();
        observation.Title = "";
        observation.Artist = null;
        observation.RawMetadata = null;
        observation.SourceType = "spotify";
        observation.ExternalId = "chosen-id";
        var link = await fixture.Db.ServicePlaylistMappings.SingleAsync();
        link.MatchingProgressJson = null;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var read = await fixture.Provider.ReadAsync(fixture.Account, "playlist", default);
        var retained = Assert.Single(read.Tracks);
        Assert.NotNull(retained.ReadScope);
        Assert.Equal(fixture.Scope.Entry.TrackId, retained.TrackId);
        Assert.Equal(observation.Id, retained.ObservationId);
        await fixture.SyncAsync();
        Assert.Equal("success", (await fixture.Db.ServicePlaylistMappings.AsNoTracking().SingleAsync()).LastSyncStatus);
        Assert.Single(fixture.Writer.Writes);
        Assert.Empty(await fixture.Db.TrackSourceIds.ToListAsync());
        Assert.Empty(await fixture.Db.TrackMatchQueueItems.ToListAsync());

        fixture.Handler.RemoteIds = [];
        link = await fixture.Db.ServicePlaylistMappings.SingleAsync();
        link.SyncMode = "from_cantaro";
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        await fixture.SyncAsync();

        Assert.Empty(fixture.Handler.RemoteIds);
        Assert.Single(fixture.Writer.Writes);
        Assert.Equal("", (await fixture.Db.TrackObservations.AsNoTracking().SingleAsync()).Title);
        Assert.Equal(1, (await fixture.Db.ServicePlaylistMappings.AsNoTracking().SingleAsync()).UnresolvedCount);
    }

    [Fact]
    public async Task SourceReferenceDoesNotExemptForgottenExportFromReadGuard()
    {
        await using var fixture = await ProductionSpotifyReadScope.CreateAsync();
        await fixture.SyncAsync();
        var observation = await fixture.Db.TrackObservations.SingleAsync();
        observation.Title = "";
        observation.RawMetadata = null;
        observation.Artist = null;
        observation.SourceType = "spotify";
        observation.ExternalId = "chosen-id";
        await fixture.Db.SaveChangesAsync();
        await fixture.Db.ServicePlaylistMappings.ExecuteDeleteAsync();
        fixture.Db.ChangeTracker.Clear();

        var failure = await Assert.ThrowsAsync<PlatformApiException>(() =>
            fixture.Provider.ReadAsync(fixture.Account, "playlist", default));

        Assert.Equal("remote_changed", failure.Code);
        Assert.Equal("", (await fixture.Db.TrackObservations.AsNoTracking().SingleAsync()).Title);
        Assert.Empty(await fixture.Db.TrackSourceIds.ToListAsync());
        Assert.Single(await fixture.Db.PlaylistExportAttempts.ToListAsync());
    }
}
