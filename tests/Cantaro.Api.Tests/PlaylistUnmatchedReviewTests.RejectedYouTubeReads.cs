using System.Data.Common;
using Cantaro.Api.Configuration;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Google.Apis.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed partial class PlaylistUnmatchedReviewTests
{
    [Theory]
    [InlineData("external")]
    [InlineData("token")]
    [InlineData("disconnected")]
    [InlineData("incomplete")]
    public async Task RejectedYouTubeRead_RealHttpRejectionCannotPersistObservationsOrMatchingWork(string change)
    {
        await using var scope = await Scope.CreateAsync("youtube", relational: true);
        using var handler = new ProductionYouTubeReadHandler(scope.Playlist.Name);
        await InitializeYouTubeReadAsync(scope, handler);
        handler.RemoteIds.Add("fresh-id");
        if (change == "incomplete") handler.ReportedItemCountDelta = 1;
        else handler.OnItemsRead = () =>
        {
            using var other = scope.NewContext();
            if (change == "external") other.ConnectedServiceAccounts.ExecuteUpdate(s => s.SetProperty(a => a.ExternalAccountId, "replacement"));
            else if (change == "token") other.ConnectedServiceAccounts.ExecuteUpdate(s => s.SetProperty(a => a.TokenVersion, a => a.TokenVersion + 1));
            else other.ConnectedServiceAccounts.ExecuteUpdate(s => s.SetProperty(a => a.ConnectionState, "disconnected"));
        };
        var provider = CreateYouTubeReadProvider(scope.Db, handler);
        await new PlaylistSyncCoordinator(scope.Db, [provider], [new ProductionYouTubeReadWriter(handler)],
            TimeProvider.System, NullLogger<PlaylistSyncCoordinator>.Instance).RunAsync(scope.UserId, scope.Playlist.Id, "youtube", default);
        await scope.Db.SaveChangesAsync();
        await using var verify = scope.NewContext();
        Assert.Equal("error", (await verify.ServicePlaylistMappings.SingleAsync()).LastSyncStatus);
        await AssertYouTubeReadIsolationAsync(scope, verify);
    }

    [PostgresFact]
    public async Task RetainedYouTubeRead_PostgresLegitimateAdditionCommitsObservationAndMatchingWork()
    {
        await using var scope = await Scope.CreateAsync("youtube", postgres: true);
        using var handler = new ProductionYouTubeReadHandler(scope.Playlist.Name);
        await InitializeYouTubeReadAsync(scope, handler);
        handler.RemoteIds.Add("fresh-id");
        await new PlaylistSyncCoordinator(scope.Db, [CreateYouTubeReadProvider(scope.Db, handler)], [new ProductionYouTubeReadWriter(handler)],
            TimeProvider.System, NullLogger<PlaylistSyncCoordinator>.Instance).RunAsync(scope.UserId, scope.Playlist.Id, "youtube", default);
        await using var verify = scope.NewContext();
        Assert.Equal("success", (await verify.ServicePlaylistMappings.SingleAsync()).LastSyncStatus);
        Assert.False(await verify.TrackObservations.AnyAsync(o => o.SourceType == "youtube" && o.ExternalId == "chosen-id"));
        var added = await verify.TrackObservations.SingleAsync(o => o.SourceType == "youtube" && o.ExternalId == "fresh-id");
        Assert.Null(added.TrackId);
        Assert.Equal(added.Id, (await verify.TrackMatchQueueItems.SingleAsync()).TrackObservationId);
        Assert.Equal(2, await verify.PlaylistEntries.CountAsync());
    }

    [PostgresFact]
    public async Task RejectedYouTubeRead_PostgresRejectsAfterQueueInsertAndRollsBackEveryWrite()
    {
        await using var scope = await Scope.CreateAsync("youtube", postgres: true);
        using var handler = new ProductionYouTubeReadHandler(scope.Playlist.Name);
        await InitializeYouTubeReadAsync(scope, handler);
        handler.RemoteIds.Add("fresh-id");
        var late = new AfterQueueInsert(async () =>
        {
            await using var other = scope.NewContext();
            await other.ConnectedServiceAccounts.ExecuteUpdateAsync(s => s.SetProperty(a => a.TokenVersion, a => a.TokenVersion + 1));
        });
        await using var reader = scope.NewContext(late);
        await new PlaylistSyncCoordinator(reader, [CreateYouTubeReadProvider(reader, handler)], [new ProductionYouTubeReadWriter(handler)],
            TimeProvider.System, NullLogger<PlaylistSyncCoordinator>.Instance).RunAsync(scope.UserId, scope.Playlist.Id, "youtube", default);
        await reader.SaveChangesAsync();
        await using var verify = scope.NewContext();
        Assert.True(late.Changed, late.Failure ?? (await verify.ServicePlaylistMappings.SingleAsync()).LastError);
        Assert.Equal("error", (await verify.ServicePlaylistMappings.SingleAsync()).LastSyncStatus);
        await AssertYouTubeReadIsolationAsync(scope, verify);
    }

    private static async Task InitializeYouTubeReadAsync(Scope scope, ProductionYouTubeReadHandler handler, bool export = true)
    {
        if (scope.Db.Database.IsNpgsql())
        {
            // CreateTablesAsync omits this migration-only default. Match the
            // production AddTrackMatchRetryCycleCount migration for raw enqueue.
            await scope.Db.Database.ExecuteSqlRawAsync("ALTER TABLE \"TrackMatchQueueItems\" ALTER COLUMN \"RetryCount\" SET DEFAULT 0");
        }
        scope.Db.ChangeTracker.Clear();
        await scope.Review.ConfirmAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, scope.Entry.Id,
            scope.Token(new TrackMatchSearchCandidate { CandidateSource = "youtube", ExternalId = "chosen-id",
                Title = "Everything Goes On", Artist = "Porter Robinson", DurationSeconds = 160 }), default);
        var link = await scope.Db.ServicePlaylistMappings.SingleAsync();
        link.BaselineJson = new PlaylistSyncBaseline([]).Serialize();
        link.BaselineName = scope.Playlist.Name;
        await scope.Db.SaveChangesAsync();
        if (!export) return;
        scope.Db.ChangeTracker.Clear();
        await new PlaylistSyncCoordinator(scope.Db, [CreateYouTubeReadProvider(scope.Db, handler)], [new ProductionYouTubeReadWriter(handler)],
            TimeProvider.System, NullLogger<PlaylistSyncCoordinator>.Instance).RunAsync(scope.UserId, scope.Playlist.Id, "youtube", default);
        Assert.Equal("success", (await scope.Db.ServicePlaylistMappings.SingleAsync()).LastSyncStatus);
    }

    private static YouTubePlaylistSyncProvider CreateYouTubeReadProvider(ApplicationDbContext db, ProductionYouTubeReadHandler handler)
    {
        var service = new YouTubeService(new ConfigurationBuilder().Build(), db,
            new TokenEncryptionService(new EphemeralDataProtectionProvider()), NullLogger<YouTubeService>.Instance,
            (_, _) => Task.FromResult(new Google.Apis.YouTube.v3.YouTubeService(new BaseClientService.Initializer
            { ApplicationName = "Cantaro rejected read tests", HttpClientFactory = handler })));
        return new(db, service, new TrackMatchQueue(db, TimeProvider.System),
            new TrackMatchingService(db, [], NullLogger<TrackMatchingService>.Instance),
            new PlaylistCanonicalReconciliationService(db), Options.Create(new TrackMatchingOptions()));
    }

    private static async Task AssertYouTubeReadIsolationAsync(Scope scope, ApplicationDbContext verify)
    {
        Assert.Null((await verify.Playlists.SingleAsync()).SyncLeaseId);
        Assert.False(await verify.TrackObservations.AnyAsync(o => o.SourceType == "youtube"));
        Assert.Empty(await verify.TrackMatchQueueItems.ToArrayAsync());
        Assert.Empty(await verify.TrackSourceIds.ToArrayAsync());
        Assert.Empty(await verify.TrackArtistCredits.ToArrayAsync());
        Assert.Single(await verify.Tracks.ToArrayAsync());
        var entry = await verify.PlaylistEntries.SingleAsync();
        Assert.Equal((scope.Entry.Id, scope.Entry.TrackId, scope.Entry.TrackObservationId), (entry.Id, entry.TrackId, entry.TrackObservationId));
    }

    private sealed class AfterQueueInsert(Func<Task> change) : DbCommandInterceptor
    {
        public bool Changed { get; private set; }
        public string? Failure { get; private set; }
        public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData data, CancellationToken ct = default)
        {
            Failure = data.Exception.ToString();
            return Task.CompletedTask;
        }
        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData data,
            int result, CancellationToken ct = default)
        {
            if (!Changed && command.CommandText.Contains("INSERT INTO \"TrackMatchQueueItems\"", StringComparison.Ordinal))
            {
                Changed = true;
                await change();
            }
            return result;
        }
    }
}
