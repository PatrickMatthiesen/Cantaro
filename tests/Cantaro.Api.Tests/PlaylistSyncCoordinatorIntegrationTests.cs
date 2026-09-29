using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class PlaylistSyncCoordinatorIntegrationTests
{
    [Fact]
    public async Task UnchangedPlaylistDoesNotReadAgainAfterFreshnessCheck()
    {
        await using var scenario = await Scenario.CreateAsync(["A", "B"]);
        scenario.Link("spotify", ["A", "B"]);
        await scenario.SaveAsync();

        await scenario.Coordinator.RunAsync(42, scenario.Playlist.Id, "spotify", default);

        Assert.Equal("success", scenario.Mapping("spotify").LastSyncStatus);
        Assert.Equal(0, scenario.Writer("spotify").ReconcileCount);
        Assert.Equal(2, scenario.Provider("spotify").ReadCount);
    }

    [Fact]
    public async Task ChangedPlaylistStillReadsAfterWritingToVerifyResult()
    {
        await using var scenario = await Scenario.CreateAsync(["A", "B"]);
        scenario.Link("spotify", ["A"]);
        await scenario.SaveAsync();

        await scenario.Coordinator.RunAsync(42, scenario.Playlist.Id, "spotify", default);

        Assert.Equal("success", scenario.Mapping("spotify").LastSyncStatus);
        Assert.Equal(1, scenario.Writer("spotify").ReconcileCount);
        Assert.Equal(3, scenario.Provider("spotify").ReadCount);
    }

    [Fact]
    public async Task ActiveReadMatchAndWrite_ReportRunningUntilSyncCompletes()
    {
        await using var scenario = await Scenario.CreateAsync(["A", "B"]);
        scenario.Link("spotify", []);
        var readWasRunning = false;
        var matchWasRunning = false;
        var writeWasRunning = false;
        scenario.Provider("spotify").AfterRead = count =>
        {
            if (count == 1) readWasRunning = scenario.Mapping("spotify").LastSyncStatus == "running";
        };
        scenario.Provider("spotify").OnResolve = (entry, _) =>
        {
            if (scenario.TrackName(entry.TrackId!.Value) == "B")
                matchWasRunning = scenario.Mapping("spotify").LastSyncStatus == "running"
                    && scenario.Mapping("spotify").MatchingProcessedCount == 1;
            return Task.FromResult<string?>(scenario.TrackName(entry.TrackId.Value));
        };
        scenario.Writer("spotify").OnReconcile = () =>
            writeWasRunning = scenario.Mapping("spotify").LastSyncStatus == "running";
        await scenario.SaveAsync();

        await scenario.Coordinator.RunAsync(42, scenario.Playlist.Id, "spotify", CancellationToken.None);

        Assert.True(readWasRunning);
        Assert.True(matchWasRunning);
        Assert.True(writeWasRunning);
        Assert.Equal("success", scenario.Mapping("spotify").LastSyncStatus);
    }

    [Fact]
    public async Task CancelledMatching_ClearsRunningAndReleasesLease()
    {
        await using var scenario = await Scenario.CreateAsync(["A", "B"]);
        scenario.Link("spotify", []);
        using var cancellation = new CancellationTokenSource();
        scenario.Provider("spotify").OnResolve = (entry, ct) =>
        {
            if (scenario.TrackName(entry.TrackId!.Value) == "B")
            {
                cancellation.Cancel();
                throw new OperationCanceledException(ct);
            }
            return Task.FromResult<string?>("A");
        };
        await scenario.SaveAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scenario.Coordinator.RunAsync(
            42, scenario.Playlist.Id, "spotify", cancellation.Token));

        Assert.Equal("pending", scenario.Mapping("spotify").LastSyncStatus);
        Assert.Equal(1, scenario.Mapping("spotify").MatchingProcessedCount);
        Assert.Null(scenario.Playlist.SyncLeaseId);
    }

    [Fact]
    public async Task FullSyncRateLimit_DoesNotLeaveOtherSuccessfulLinksPending()
    {
        await using var scenario = await Scenario.CreateAsync(["A"]);
        scenario.Link("spotify", ["A"]);
        scenario.Link("youtube", ["A"]);
        scenario.Mapping("youtube").LastSyncStatus = "success";
        scenario.Provider("spotify").AfterRead = _ =>
            throw new PlatformApiException("spotify_rate_limited", "Wait", 429, TimeSpan.FromHours(3));
        await scenario.SaveAsync();

        await scenario.Coordinator.RunAsync(42, scenario.Playlist.Id, null, default);

        Assert.Equal("success", scenario.Mapping("youtube").LastSyncStatus);
        Assert.Equal("rate_limited", scenario.Mapping("spotify").LastSyncStatus);
        Assert.Equal(0, scenario.Writer("youtube").ReconcileCount);
    }

    [Fact]
    public async Task SpotifyQuotaExceeded_IsSavedAsSeparateCooldownStatus()
    {
        await using var scenario = await Scenario.CreateAsync(["A"]);
        scenario.Link("spotify", ["A"]);
        scenario.Provider("spotify").AfterRead = _ =>
            throw new PlatformApiException("spotify_quota_exceeded", "Quota reached", 429, TimeSpan.FromHours(3));
        await scenario.SaveAsync();

        await scenario.Coordinator.RunAsync(42, scenario.Playlist.Id, "spotify", default);

        Assert.Equal("quota_limited", scenario.Mapping("spotify").LastSyncStatus);
        Assert.True(scenario.Mapping("spotify").NextAttemptAt > DateTimeOffset.UtcNow.AddHours(2));
        Assert.Equal(0, scenario.Mapping("spotify").AttemptCount);
    }

    [Fact]
    public async Task SpotifyQuotaExceededDuringCreate_RemainsDefiniteFailureAndKeepsRetryDeadline()
    {
        await using var scenario = await Scenario.CreateAsync(["A"]);
        scenario.Writer("spotify").CreationFailure = new PlatformApiException(
            "spotify_quota_exceeded", "Quota reached", 429, TimeSpan.FromHours(3));

        await Assert.ThrowsAsync<PlatformApiException>(() =>
            scenario.Coordinator.CreateLinkAsync(42, scenario.Playlist.Id, "spotify", default));

        var mapping = scenario.Mapping("spotify");
        Assert.Equal("creation_failed", mapping.State);
        Assert.Equal("quota_limited", mapping.LastSyncStatus);
        Assert.True(mapping.NextAttemptAt > DateTimeOffset.UtcNow.AddHours(2));
        Assert.Equal(0, mapping.AttemptCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RateLimitedMatching_ResumesSavedNegativeResultsUnlessEvidenceChanges(bool evidenceChanged)
    {
        await using var scenario = await Scenario.CreateAsync(["A", "B", "C"]);
        scenario.Link("spotify", []);
        var calls = new List<string>();
        var limited = true;
        scenario.Provider("spotify").OnResolve = (entry, _) =>
        {
            var title = scenario.TrackName(entry.TrackId!.Value);
            calls.Add(title);
            if (title == "A") throw new PlatformApiException("no_match", "No match", 404);
            if (title == "B" && limited)
                throw new PlatformApiException("spotify_rate_limited", "Wait", 429, TimeSpan.FromMinutes(20));
            return Task.FromResult<string?>(title);
        };
        await scenario.SaveAsync();

        await scenario.Coordinator.RunAsync(42, scenario.Playlist.Id, "spotify", default);
        Assert.Equal("rate_limited", scenario.Mapping("spotify").LastSyncStatus);
        Assert.Equal(1, scenario.Mapping("spotify").MatchingProcessedCount);
        Assert.Equal(0, scenario.Writer("spotify").ReconcileCount);
        Assert.NotNull(scenario.Mapping("spotify").MatchingProgressJson);

        limited = false;
        if (evidenceChanged)
            (await scenario.Db.Tracks.SingleAsync(track => track.Id == scenario.TrackId("A"))).Isrc = "USAAA2600001";
        scenario.Mapping("spotify").NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        await scenario.SaveAsync();
        scenario.Db.ChangeTracker.Clear();
        await scenario.Coordinator.RunAsync(42, scenario.Playlist.Id, "spotify", default);

        Assert.Equal(evidenceChanged ? ["A", "B", "A", "B", "C"] : ["A", "B", "B", "C"], calls);
        Assert.Equal(["B", "C"], scenario.Provider("spotify").Items);
        Assert.Equal("partial", scenario.Mapping("spotify").LastSyncStatus);
        Assert.Null(scenario.Mapping("spotify").MatchingProgressJson);
        Assert.Equal(["A", "B", "C"], await scenario.CanonicalIdsAsync());
    }

    [Fact]
    public async Task MatchingProgress_CountsKnownIdentitiesBeforeAnEarlyRateLimit()
    {
        await using var scenario = await Scenario.CreateAsync(["A", "B", "C"]);
        scenario.Link("spotify", []);
        foreach (var title in new[] { "B", "C" })
            scenario.Db.TrackSourceIds.Add(new TrackSourceId
            {
                Id = Guid.NewGuid(), TrackId = scenario.TrackId(title), SourceType = "spotify", ExternalId = title
            });
        var calls = new List<Guid>();
        scenario.Provider("spotify").OnResolve = (entry, _) =>
        {
            calls.Add(entry.TrackId!.Value);
            throw new PlatformApiException("spotify_rate_limited", "Wait", 429, TimeSpan.FromMinutes(20));
        };
        await scenario.SaveAsync();

        await scenario.Coordinator.RunAsync(42, scenario.Playlist.Id, "spotify", default);

        Assert.Equal(2, scenario.Mapping("spotify").MatchingProcessedCount);
        Assert.Equal(3, scenario.Mapping("spotify").MatchingTotalCount);
        Assert.Equal("rate_limited", scenario.Mapping("spotify").LastSyncStatus);
        Assert.Equal([scenario.TrackId("A")], calls);
    }

    [Fact]
    public async Task MatchingCheckpoint_SurvivesRemoteAdditionBeforeRetry()
    {
        await using var scenario = await Scenario.CreateAsync(["A", "B", "C"]);
        scenario.Link("spotify", []);
        var calls = new List<string>();
        var limited = true;
        scenario.Provider("spotify").OnResolve = (entry, _) =>
        {
            var title = scenario.TrackName(entry.TrackId!.Value);
            calls.Add(title);
            if (title == "A") throw new PlatformApiException("no_match", "No match", 404);
            if (title == "B" && limited)
                throw new PlatformApiException("spotify_rate_limited", "Wait", 429, TimeSpan.FromMinutes(20));
            return Task.FromResult<string?>(title);
        };
        await scenario.SaveAsync();
        var originalEntries = await scenario.Db.PlaylistEntries.AsNoTracking()
            .ToDictionaryAsync(entry => entry.TrackId!.Value, entry => entry.Id);

        await scenario.Coordinator.RunAsync(42, scenario.Playlist.Id, "spotify", default);
        Assert.Equal("rate_limited", scenario.Mapping("spotify").LastSyncStatus);
        Assert.Equal(["A", "B"], calls);

        limited = false;
        scenario.AddTrack("D");
        scenario.Provider("spotify").Items = ["D"];
        scenario.Mapping("spotify").NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        await scenario.SaveAsync();
        scenario.Db.ChangeTracker.Clear();

        await scenario.Coordinator.RunAsync(42, scenario.Playlist.Id, "spotify", default);

        Assert.Equal(["A", "B", "B", "C"], calls);
        Assert.Equal(["D", "A", "B", "C"], await scenario.CanonicalIdsAsync());
        var originalTrackIds = originalEntries.Keys.ToArray();
        var retainedEntries = await scenario.Db.PlaylistEntries.AsNoTracking()
            .Where(entry => entry.TrackId.HasValue && originalTrackIds.Contains(entry.TrackId.Value))
            .ToDictionaryAsync(entry => entry.TrackId!.Value, entry => entry.Id);
        foreach (var (trackId, entryId) in originalEntries)
            Assert.Equal(entryId, retainedEntries[trackId]);
        Assert.Equal("partial", scenario.Mapping("spotify").LastSyncStatus);
    }

    [Fact]
    public async Task MatchingBatches_ContinueWithoutRepeatingSearchesOrWritingAnIncompleteBatch()
    {
        await using var scenario = await Scenario.CreateAsync([]);
        var titles = Enumerable.Range(0, 30).Select(index => $"Song{index}").ToArray();
        foreach (var (title, index) in titles.Select((title, index) => (title, index)))
            scenario.Db.PlaylistEntries.Add(new PlaylistEntry
            {
                Id = Guid.NewGuid(), PlaylistId = scenario.Playlist.Id,
                TrackId = scenario.AddTrack(title), Position = index
            });
        scenario.Link("spotify", []);
        var calls = new List<string>();
        scenario.Provider("spotify").OnResolve = (entry, _) =>
        {
            var title = scenario.TrackName(entry.TrackId!.Value);
            calls.Add(title);
            return Task.FromResult<string?>(title);
        };
        await scenario.SaveAsync();

        await scenario.Coordinator.RunAsync(42, scenario.Playlist.Id, "spotify", default);
        Assert.Equal(25, calls.Count);
        Assert.Equal(25, scenario.Mapping("spotify").MatchingProcessedCount);
        Assert.Equal(30, scenario.Mapping("spotify").MatchingTotalCount);
        Assert.Equal("pending", scenario.Mapping("spotify").LastSyncStatus);
        Assert.Equal(0, scenario.Writer("spotify").ReconcileCount);
        scenario.Db.ChangeTracker.Clear();

        await scenario.Coordinator.RunAsync(42, scenario.Playlist.Id, "spotify", default);
        Assert.Equal(titles, calls);
        Assert.Equal(titles, scenario.Provider("spotify").Items);
        Assert.Equal("success", scenario.Mapping("spotify").LastSyncStatus);
        Assert.Null(scenario.Mapping("spotify").MatchingProgressJson);
    }

    [Theory]
    [InlineData("rate_limited")]
    [InlineData("quota_limited")]
    public async Task ManualSync_DoesNotBypassAnActiveCooldown(string status)
    {
        await using var scenario = await Scenario.CreateAsync(["A"]);
        scenario.Link("spotify", []);
        var retryAt = DateTimeOffset.UtcNow.AddMinutes(20);
        scenario.Mapping("spotify").LastSyncStatus = status;
        scenario.Mapping("spotify").NextAttemptAt = retryAt;
        await scenario.SaveAsync();

        await scenario.Coordinator.QueueAsync(42, scenario.Playlist.Id, "spotify", default);
        await scenario.Coordinator.RunAsync(42, scenario.Playlist.Id, "spotify", default);
        await scenario.Coordinator.QueueAsync(42, scenario.Playlist.Id, null, default);
        await scenario.Coordinator.RunAsync(42, scenario.Playlist.Id, null, default);

        Assert.Equal(retryAt, scenario.Mapping("spotify").NextAttemptAt);
        Assert.Equal(retryAt, scenario.Playlist.NextSyncAt);
        Assert.Equal(status, scenario.Mapping("spotify").LastSyncStatus);
        Assert.Equal(0, scenario.Provider("spotify").ReadCount);
    }

    [Fact]
    public void DuplicateTrackRemoval_PreservesTheObservedOccurrence()
    {
        var trackId = Guid.NewGuid();
        var first = new PlaylistSyncItem(trackId, Guid.NewGuid(), "video-first", "youtube");
        var second = new PlaylistSyncItem(trackId, Guid.NewGuid(), "video-second", "youtube");

        var result = PlaylistSyncMerge.Merge([first, second], [first, second], [second], true);

        Assert.Equal("video-second", Assert.Single(result.Items).ExternalId);
    }

    [Fact]
    public async Task TwoPlatformSync_MergesSourceRemovalAndAdditionWithoutRestoringRemovedTrack()
    {
        await using var scenario = await Scenario.CreateAsync(["A", "B"]);
        scenario.Link("youtube", ["A", "B"]);
        scenario.Link("spotify", ["A", "B"]);
        scenario.Provider("youtube").Items = ["A", "C"];
        scenario.Provider("spotify").Items = ["A", "B"];
        await scenario.SaveAsync();

        await scenario.Coordinator.RunAsync(42, scenario.Playlist.Id, null, CancellationToken.None);

        Assert.Equal(["A", "C"], await scenario.CanonicalIdsAsync());
        Assert.Equal(["A", "C"], scenario.Provider("spotify").Items);
        Assert.Equal(["A", "C"], scenario.Provider("youtube").Items);
        Assert.Equal(1, scenario.Writer("spotify").ReconcileCount);
    }

    [Fact]
    public async Task InitializeLinkCombine_PreservesRemoteOrderWithoutUnnecessaryWriteOnNextRun()
    {
        await using var scenario = await Scenario.CreateAsync(["A", "B"]);
        scenario.Link("youtube", ["C", "A", "B"]);
        var link = scenario.Mapping("youtube");
        link.State = "pending";
        link.InitialMode = "combine";
        link.BaselineJson = null;
        await scenario.SaveAsync();

        await scenario.Coordinator.InitializeLinkAsync(42, scenario.Playlist.Id, link.Id,
            "combine", CancellationToken.None);
        await scenario.Coordinator.RunAsync(42, scenario.Playlist.Id, "youtube", CancellationToken.None);

        Assert.Equal(["C", "A", "B"], await scenario.CanonicalIdsAsync());
        Assert.Equal(["C", "A", "B"], scenario.Provider("youtube").Items);
        Assert.Equal(0, scenario.Writer("youtube").ReconcileCount);
    }

    [Fact]
    public async Task UnresolvedSourceOccurrence_RemainsCanonicalWhileOtherPlatformExportsKnownSubset()
    {
        await using var scenario = await Scenario.CreateAsync(["A"]);
        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = "X",
            Title = "Unmatched song", MatchStatus = TrackMatchingStatuses.Pending
        };
        scenario.Db.PlaylistEntries.Add(new PlaylistEntry
        {
            Id = Guid.NewGuid(), PlaylistId = scenario.Playlist.Id,
            TrackObservationId = observation.Id, TrackObservation = observation, Position = 1
        });
        scenario.Provider("youtube").Observations["X"] = observation.Id;
        scenario.Link("youtube", ["A", "X"]);
        scenario.Link("spotify", ["A"]);
        await scenario.SaveAsync();

        await scenario.Coordinator.RunAsync(42, scenario.Playlist.Id, null, CancellationToken.None);

        Assert.Equal(["A", "X"], await scenario.CanonicalIdsAsync());
        Assert.Equal(["A", "X"], scenario.Provider("youtube").Items);
        Assert.Equal(["A"], scenario.Provider("spotify").Items);
        Assert.Equal(1, scenario.Mapping("spotify").UnresolvedCount);
        Assert.Equal("partial", scenario.Mapping("spotify").LastSyncStatus);
    }

    [Fact]
    public async Task UnavailableVideos_DoNotBlockFanoutOrDeleteKnownTracks()
    {
        await using var scenario = await Scenario.CreateAsync(["A", "B"]);
        scenario.Link("youtube", ["A", "B"]);
        scenario.Link("spotify", ["A", "B"]);
        scenario.Provider("youtube").Items = ["A", "B", "hidden", "C"];
        scenario.Provider("youtube").Unavailable.UnionWith(["B", "hidden"]);
        await scenario.SaveAsync();

        await scenario.Coordinator.RunAsync(42, scenario.Playlist.Id, null, CancellationToken.None);

        Assert.Equal(["A", "B", "C"], await scenario.CanonicalIdsAsync());
        Assert.Equal(["A", "B", "C"], scenario.Provider("spotify").Items);
        Assert.Contains("hidden", scenario.Provider("youtube").Items);
        Assert.Equal("success", scenario.Mapping("youtube").LastSyncStatus);
        Assert.Equal("success", scenario.Mapping("spotify").LastSyncStatus);
    }

    [Fact]
    public async Task UnavailableVideos_DoNotBlockWritingReadableAdditions()
    {
        await using var scenario = await Scenario.CreateAsync(["A", "B", "C"]);
        scenario.Link("youtube", ["A", "B"]);
        scenario.Provider("youtube").Items = ["A", "hidden", "B"];
        scenario.Provider("youtube").Unavailable.UnionWith(["B", "hidden"]);
        await scenario.SaveAsync();

        await scenario.Coordinator.RunAsync(42, scenario.Playlist.Id, "youtube", CancellationToken.None);
        await scenario.Coordinator.RunAsync(42, scenario.Playlist.Id, "youtube", CancellationToken.None);

        Assert.Equal(["A", "B", "C"], await scenario.CanonicalIdsAsync());
        Assert.Equal(["A", "C"], scenario.Provider("youtube").Items.Where(id => !scenario.Provider("youtube").Unavailable.Contains(id)));
        Assert.Contains("hidden", scenario.Provider("youtube").Items);
        Assert.Contains("B", scenario.Provider("youtube").Items);
        Assert.Equal("success", scenario.Mapping("youtube").LastSyncStatus);
        Assert.Equal(1, scenario.Writer("youtube").ReconcileCount);
    }

    [Fact]
    public async Task RemovingKnownUnavailableTrackElsewhere_DoesNotRestoreItOnNextSync()
    {
        await using var scenario = await Scenario.CreateAsync(["A", "B"]);
        scenario.Link("youtube", ["A", "B"]);
        scenario.Link("spotify", ["A", "B"]);
        scenario.Provider("youtube").Unavailable.Add("B");
        scenario.Provider("spotify").Items = ["A"];
        await scenario.SaveAsync();

        await scenario.Coordinator.RunAsync(42, scenario.Playlist.Id, null, CancellationToken.None);
        await scenario.Coordinator.RunAsync(42, scenario.Playlist.Id, null, CancellationToken.None);

        Assert.Equal(["A"], await scenario.CanonicalIdsAsync());
        Assert.Equal(["A"], scenario.Provider("spotify").Items);
        Assert.Contains("B", scenario.Provider("youtube").Items);
        Assert.Equal("success", scenario.Mapping("youtube").LastSyncStatus);
    }

    [Fact]
    public async Task IncompleteSourceRead_DoesNotTreatMissingItemsAsDeletions()
    {
        await using var scenario = await Scenario.CreateAsync(["A", "B"]);
        scenario.Link("youtube", ["A", "B"]);
        scenario.Link("spotify", ["A", "B"]);
        scenario.Provider("youtube").Items = ["A"];
        scenario.Provider("youtube").IsComplete = false;
        await scenario.SaveAsync();

        await scenario.Coordinator.RunAsync(42, scenario.Playlist.Id, null, CancellationToken.None);

        Assert.Equal(["A", "B"], await scenario.CanonicalIdsAsync());
        Assert.Equal(["A", "B"], scenario.Provider("spotify").Items);
        Assert.Equal("error", scenario.Mapping("youtube").LastSyncStatus);
        Assert.Equal(0, scenario.Writer("spotify").ReconcileCount);
    }

    [Fact]
    public async Task DailyFanout_StopsBeforeAllWritesWhenOneSourceIsIncomplete()
    {
        await using var scenario = await Scenario.CreateAsync(["A", "C"]);
        scenario.Link("youtube", ["A"]);
        scenario.Link("spotify", ["A"]);
        scenario.Provider("youtube").IsComplete = false;
        await scenario.SaveAsync();

        await scenario.Coordinator.RunAsync(42, scenario.Playlist.Id, null, CancellationToken.None);

        Assert.Equal(["A", "C"], await scenario.CanonicalIdsAsync());
        Assert.Equal(["A"], scenario.Provider("spotify").Items);
        Assert.Equal(0, scenario.Writer("spotify").ReconcileCount);
    }

    [Fact]
    public async Task ChangedAccount_DoesNotReadOrWriteFormerOwnersPlaylist()
    {
        await using var scenario = await Scenario.CreateAsync(["A"]);
        scenario.Link("youtube", ["A"]);
        var account = await scenario.Db.ConnectedServiceAccounts.SingleAsync(item => item.Service == "youtube");
        account.ExternalAccountId = "different-channel";
        await scenario.SaveAsync();

        await scenario.Coordinator.RunAsync(42, scenario.Playlist.Id, "youtube", CancellationToken.None);

        Assert.Equal(0, scenario.Provider("youtube").ReadCount);
        Assert.Equal(0, scenario.Writer("youtube").ReconcileCount);
        Assert.Equal("youtube-owner", scenario.Mapping("youtube").ExternalAccountId);
        Assert.Equal(["A"], await scenario.CanonicalIdsAsync());
    }

    [Fact]
    public async Task ExternalNameRevertedToBaseline_ClearsStaleRenameProposal()
    {
        await using var scenario = await Scenario.CreateAsync(["A"]);
        scenario.Link("youtube", ["A"]);
        await scenario.SaveAsync();
        var provider = scenario.Provider("youtube");

        provider.Name = "External edit";
        await scenario.Coordinator.RunAsync(42, scenario.Playlist.Id, "youtube", CancellationToken.None, readOnly: true);
        Assert.Equal("External edit", scenario.Mapping("youtube").PendingName);

        provider.Name = "Road songs";
        await scenario.Coordinator.RunAsync(42, scenario.Playlist.Id, "youtube", CancellationToken.None, readOnly: true);

        Assert.Null(scenario.Mapping("youtube").PendingName);
        Assert.Equal("Road songs", scenario.Mapping("youtube").BaselineName);
    }

    [Fact]
    public async Task NewExternalName_CancelsPreviouslyApprovedRenameBeforeWrite()
    {
        await using var scenario = await Scenario.CreateAsync(["A"]);
        scenario.Link("youtube", ["A"]);
        scenario.Mapping("youtube").DesiredName = "Approved name";
        await scenario.SaveAsync();
        var provider = scenario.Provider("youtube");
        provider.Name = "Later external edit";

        await scenario.Coordinator.RunAsync(42, scenario.Playlist.Id, "youtube", CancellationToken.None);

        Assert.Null(scenario.Mapping("youtube").DesiredName);
        Assert.Equal("Later external edit", scenario.Mapping("youtube").PendingName);
        Assert.Equal("Later external edit", provider.Name);
        Assert.Equal(0, provider.RenameCount);
    }

    [Fact]
    public async Task RemoteDriftBeforeWrite_AbortsMutationAndQueuesRetry()
    {
        await using var scenario = await Scenario.CreateAsync(["A", "C"]);
        scenario.Link("spotify", ["A"]);
        var provider = scenario.Provider("spotify");
        provider.AfterRead = count => { if (count == 2) provider.Items = ["A", "B"]; };
        await scenario.SaveAsync();

        await scenario.Coordinator.RunAsync(42, scenario.Playlist.Id, "spotify", CancellationToken.None);

        Assert.Equal(["A", "C"], await scenario.CanonicalIdsAsync());
        Assert.Equal(["A", "B"], provider.Items);
        Assert.Equal(0, scenario.Writer("spotify").ReconcileCount);
        Assert.Equal("error", scenario.Mapping("spotify").LastSyncStatus);
        Assert.NotNull(scenario.Mapping("spotify").NextAttemptAt);
    }

    [Fact]
    public async Task MissingCounterpart_IsCreatedOnceAndLinkedToSameCanonicalPlaylist()
    {
        await using var scenario = await Scenario.CreateAsync(["A"]);
        await scenario.SaveAsync();

        await scenario.Coordinator.RunAsync(42, scenario.Playlist.Id, null, CancellationToken.None);
        await scenario.Coordinator.RunAsync(42, scenario.Playlist.Id, null, CancellationToken.None);

        Assert.Equal(1, scenario.Writer("youtube").CreateCount);
        Assert.Equal(1, scenario.Writer("spotify").CreateCount);
        Assert.Equal(["A"], scenario.Provider("youtube").Items);
        Assert.Equal(["A"], scenario.Provider("spotify").Items);
        Assert.Equal(2, await scenario.Db.ServicePlaylistMappings.CountAsync());
        Assert.All(await scenario.Db.ServicePlaylistMappings.ToListAsync(),
            mapping => Assert.Equal(scenario.Playlist.Id, mapping.PlaylistId));
    }

    [Fact]
    public async Task AmbiguousCreate_KeepsReservationAndNeverAutomaticallyCreatesAgain()
    {
        await using var scenario = await Scenario.CreateAsync(["A"]);
        var spotify = await scenario.Db.ConnectedServiceAccounts.SingleAsync(item => item.Service == "spotify");
        spotify.ConnectionState = "disconnected";
        scenario.Writer("youtube").CreationFailure = new PlatformApiException(
            "youtube_playlist_creation_unknown", "The create response was lost.", 409);
        await scenario.SaveAsync();

        await scenario.Coordinator.RunAsync(42, scenario.Playlist.Id, null, CancellationToken.None);
        await scenario.Coordinator.RunAsync(42, scenario.Playlist.Id, null, CancellationToken.None);

        Assert.Equal(1, scenario.Writer("youtube").CreateCount);
        var link = scenario.Mapping("youtube");
        Assert.Equal("creation_uncertain", link.State);
        Assert.StartsWith("pending:", link.ServicePlaylistId, StringComparison.Ordinal);
        await Assert.ThrowsAsync<PlatformApiException>(() => scenario.Coordinator.CreateLinkAsync(
            42, scenario.Playlist.Id, "youtube", CancellationToken.None));
        Assert.Equal(1, scenario.Writer("youtube").CreateCount);
    }

    [Theory]
    [InlineData("cantaro", "C,E,A,B")]
    [InlineData("platform", "B,E,A,C")]
    public async Task ResolveOrder_KeepsIndependentAdditionForBothChoices(string use, string expected)
    {
        await using var scenario = await Scenario.CreateAsync(["C", "A", "B"]);
        scenario.Db.PlaylistEntries.Add(new PlaylistEntry
        {
            Id = Guid.NewGuid(), PlaylistId = scenario.Playlist.Id,
            TrackId = scenario.AddTrack("E"), Position = 1
        });
        // Shift the existing entries to leave a slot for E.
        var entries = await scenario.Db.PlaylistEntries.OrderBy(item => item.Position).ToListAsync();
        foreach (var entry in entries.Where(item => item.TrackId != scenario.TrackId("E") && item.Position >= 1))
            entry.Position++;
        scenario.Link("spotify", ["A", "B", "C"]);
        scenario.Provider("spotify").Items = ["B", "A", "C"];
        await scenario.SaveAsync();

        await scenario.Coordinator.RunAsync(42, scenario.Playlist.Id, "spotify", CancellationToken.None);
        Assert.Equal("order_conflict", scenario.Mapping("spotify").LastSyncStatus);
        await scenario.Coordinator.ResolveOrderAsync(42, scenario.Playlist.Id,
            scenario.Mapping("spotify").Id, use, CancellationToken.None);

        Assert.Equal(expected.Split(','), await scenario.CanonicalIdsAsync());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Scheduler_GlobalDisableStopsDueWorkButAutomaticDisableAllowsLinkRetry(
        bool enabled, bool expectedProcessed)
    {
        await using var scenario = await Scenario.CreateAsync(["A"]);
        scenario.Link("youtube", ["A"]);
        scenario.Mapping("youtube").NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        scenario.Playlist.SyncEnabled = false;
        await scenario.SaveAsync();
        using var services = SchedulerServices(scenario);
        var scheduler = Scheduler(services, enabled, automaticEnabled: false);

        var processed = await scheduler.ProcessNextAsync(CancellationToken.None);

        Assert.Equal(expectedProcessed, processed);
        Assert.Equal(expectedProcessed, scenario.Provider("youtube").ReadCount > 0);
    }

    [Fact]
    public async Task Scheduler_DailyDueTimeAdvancesAfterRunAndDoesNotRepeatImmediately()
    {
        await using var scenario = await Scenario.CreateAsync(["A"]);
        scenario.Link("youtube", ["A"]);
        scenario.Playlist.NextSyncAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await scenario.SaveAsync();
        using var services = SchedulerServices(scenario);
        var scheduler = Scheduler(services, enabled: true, automaticEnabled: true);

        Assert.True(await scheduler.ProcessNextAsync(CancellationToken.None));
        Assert.True(scenario.Playlist.NextSyncAt > DateTimeOffset.UtcNow);
        Assert.False(await scheduler.ProcessNextAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RetryAfterPartialWrite_DoesNotImportTemporaryRemoteRemoval()
    {
        await using var scenario = await Scenario.CreateAsync(["A", "B", "C"]);
        scenario.Link("youtube", ["A", "B"]);
        var provider = scenario.Provider("youtube");
        var writer = scenario.Writer("youtube");
        writer.FailAfterRemoving = "B";
        await scenario.SaveAsync();

        await scenario.Coordinator.RunAsync(42, scenario.Playlist.Id, "youtube", CancellationToken.None);
        Assert.Equal(["A"], provider.Items);
        Assert.Equal(["A", "B", "C"], await scenario.CanonicalIdsAsync());

        writer.FailAfterRemoving = null;
        await scenario.Coordinator.RunAsync(42, scenario.Playlist.Id, "youtube", CancellationToken.None);

        Assert.Equal(["A", "B", "C"], await scenario.CanonicalIdsAsync());
        Assert.Equal(["A", "B", "C"], provider.Items);
    }

    [Fact]
    public async Task ResolutionThatDeduplicatesCanonicalEntries_DoesNotExportStaleDuplicate()
    {
        await using var scenario = await Scenario.CreateAsync(["A"]);
        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = "Y",
            Title = "Same recording", MatchStatus = TrackMatchingStatuses.Pending
        };
        scenario.Db.PlaylistEntries.Add(new PlaylistEntry
        {
            Id = Guid.NewGuid(), PlaylistId = scenario.Playlist.Id, Position = 1,
            TrackObservationId = observation.Id, TrackObservation = observation
        });
        scenario.Link("spotify", ["A"]);
        await scenario.SaveAsync();
        scenario.Provider("spotify").OnResolve = async (entry, ct) =>
        {
            if (entry.TrackObservation?.ExternalId != "Y") return null;
            // A destination match can canonicalize Y to the existing A recording.
            scenario.Db.PlaylistEntries.Remove(entry);
            await scenario.Db.SaveChangesAsync(ct);
            return "A";
        };

        await scenario.Coordinator.RunAsync(42, scenario.Playlist.Id, "spotify", CancellationToken.None);

        Assert.Equal(["A"], await scenario.CanonicalIdsAsync());
        Assert.Equal(["A"], scenario.Provider("spotify").Items);
    }

    [Fact]
    public async Task DuplicateMode_PreservesTheSurvivingProviderOccurrence()
    {
        await using var scenario = await Scenario.CreateAsync(["A"]);
        scenario.Playlist.AllowDuplicateTracks = true;
        scenario.AliasTrack("video-first", "A");
        scenario.AliasTrack("video-second", "A");
        var first = new TrackObservation
        {
            Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = "video-first",
            Title = "Song", MatchStatus = TrackMatchingStatuses.Matched,
            TrackId = scenario.TrackId("A")
        };
        var second = new TrackObservation
        {
            Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = "video-second",
            Title = "Song", MatchStatus = TrackMatchingStatuses.Matched,
            TrackId = scenario.TrackId("A")
        };
        scenario.Db.TrackObservations.AddRange(first, second);
        var firstEntry = await scenario.Db.PlaylistEntries.SingleAsync();
        var originalAddedAt = DateTimeOffset.UtcNow.AddDays(-3);
        firstEntry.TrackObservation = first;
        firstEntry.TrackObservationId = first.Id;
        firstEntry.AddedAt = originalAddedAt;
        var secondEntryId = Guid.NewGuid();
        scenario.Db.PlaylistEntries.Add(new PlaylistEntry
        {
            Id = secondEntryId, PlaylistId = scenario.Playlist.Id, Position = 1,
            TrackId = scenario.TrackId("A"), TrackObservation = second,
            TrackObservationId = second.Id, AddedAt = originalAddedAt.AddHours(1)
        });
        scenario.Provider("youtube").Observations["video-first"] = first.Id;
        scenario.Provider("youtube").Observations["video-second"] = second.Id;
        scenario.Link("youtube", ["video-first", "video-second"]);
        var link = scenario.Mapping("youtube");
        link.State = "paused";
        scenario.Provider("youtube").Items = ["video-second", "video-first"];
        await scenario.SaveAsync();

        await scenario.Coordinator.InitializeLinkAsync(42, scenario.Playlist.Id, link.Id, "platform", CancellationToken.None);

        var reordered = await scenario.Db.PlaylistEntries.AsNoTracking()
            .Where(entry => entry.PlaylistId == scenario.Playlist.Id)
            .OrderBy(entry => entry.Position).ToListAsync();
        Assert.Equal([secondEntryId, firstEntry.Id], reordered.Select(entry => entry.Id));
        Assert.Equal([originalAddedAt.AddHours(1), originalAddedAt], reordered.Select(entry => entry.AddedAt));

        scenario.Provider("youtube").Items = ["video-second"];

        await scenario.Coordinator.RunAsync(42, scenario.Playlist.Id, "youtube", CancellationToken.None);

        var survivor = Assert.Single(await scenario.Db.PlaylistEntries.AsNoTracking()
            .Include(entry => entry.TrackObservation).ToListAsync());
        Assert.Equal(secondEntryId, survivor.Id);
        Assert.Equal(originalAddedAt.AddHours(1), survivor.AddedAt);
        Assert.Equal(0, survivor.Position);
        Assert.Equal("video-second", survivor.TrackObservation?.ExternalId);
        Assert.Equal(["video-second"], scenario.Provider("youtube").Items);
    }

    private sealed class Scenario : IAsyncDisposable
    {
        private readonly Dictionary<string, Guid> _trackIds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, FakeProvider> _providers = [];
        private readonly Dictionary<string, FakeWriter> _writers = [];

        private Scenario(ApplicationDbContext db, Playlist playlist)
        {
            Db = db;
            Playlist = playlist;
            foreach (var service in new[] { "youtube", "spotify" })
            {
                var provider = new FakeProvider(service, _trackIds);
                _providers[service] = provider;
                _writers[service] = new FakeWriter(provider);
            }
            Coordinator = new PlaylistSyncCoordinator(db, _providers.Values, _writers.Values,
                TimeProvider.System, NullLogger<PlaylistSyncCoordinator>.Instance);
        }

        public ApplicationDbContext Db { get; }
        public Playlist Playlist { get; }
        public PlaylistSyncCoordinator Coordinator { get; }
        public FakeProvider Provider(string service) => _providers[service];
        public FakeWriter Writer(string service) => _writers[service];
        public Guid TrackId(string id) => _trackIds[id];
        public string TrackName(Guid id) => _trackIds.Single(pair => pair.Value == id).Key;
        public void AliasTrack(string alias, string existing) => _trackIds[alias] = _trackIds[existing];
        public Guid AddTrack(string id)
        {
            var trackId = Guid.NewGuid();
            _trackIds[id] = trackId;
            Db.Tracks.Add(new Track { Id = trackId, SearchTitle = id });
            return trackId;
        }
        public ServicePlaylistMapping Mapping(string service) => Db.ServicePlaylistMappings.Local.Single(item => item.Service == service);

        public static async Task<Scenario> CreateAsync(string[] initial)
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var playlist = new Playlist { Id = Guid.NewGuid(), UserId = 42, Name = "Road songs",
                SyncEnabled = true, NextSyncAt = DateTimeOffset.UtcNow };
            db.Playlists.Add(playlist);
            var scenario = new Scenario(db, playlist);
            foreach (var id in new[] { "A", "B", "C" })
            {
                var trackId = Guid.NewGuid();
                scenario._trackIds[id] = trackId;
                db.Tracks.Add(new Track { Id = trackId, SearchTitle = id });
            }
            for (var position = 0; position < initial.Length; position++)
                db.PlaylistEntries.Add(new PlaylistEntry
                {
                    Id = Guid.NewGuid(), PlaylistId = playlist.Id,
                    TrackId = scenario._trackIds[initial[position]], Position = position
                });
            foreach (var (service, accountId) in new[] { ("youtube", 7), ("spotify", 8) })
                db.ConnectedServiceAccounts.Add(new ConnectedServiceAccount
                {
                    Id = accountId, UserId = 42, Service = service,
                    ExternalAccountId = $"{service}-owner", ConnectionState = "connected"
                });
            await db.SaveChangesAsync();
            return scenario;
        }

        public void Link(string service, string[] baseline)
        {
            var provider = Provider(service);
            provider.Items = [.. baseline];
            var accountId = service == "youtube" ? 7 : 8;
            Db.ServicePlaylistMappings.Add(new ServicePlaylistMapping
            {
                Id = Guid.NewGuid(), PlaylistId = Playlist.Id, UserId = 42,
                ConnectedServiceAccountId = accountId, ExternalAccountId = $"{service}-owner",
                Service = service, ServicePlaylistId = $"{service}-playlist",
                SyncMode = "bidirectional", State = "active", BaselineName = Playlist.Name,
                BaselineJson = new PlaylistSyncBaseline(baseline.Select(provider.SyncItem).ToList()).Serialize()
            });
        }

        public Task SaveAsync() => Db.SaveChangesAsync();

        public async Task<string[]> CanonicalIdsAsync()
        {
            var entries = await Db.PlaylistEntries.AsNoTracking().Include(item => item.TrackObservation)
                .Where(item => item.PlaylistId == Playlist.Id).OrderBy(item => item.Position).ToListAsync();
            return entries.Select(entry => entry.TrackId is { } trackId
                ? _trackIds.Single(pair => pair.Value == trackId).Key
                : entry.TrackObservation?.ExternalId ?? "?").ToArray();
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class FakeProvider(string service, Dictionary<string, Guid> trackIds) : IPlaylistSyncProvider
    {
        public string PlatformId => service;
        public Task<IReadOnlyList<PlaylistRemoteCatalogItem>> ListPlaylistsAsync(
            PlatformAccountContext account, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<PlaylistRemoteCatalogItem>>([]);
        public List<string> Items { get; set; } = [];
        public string Name { get; set; } = "Road songs";
        public int RenameCount { get; private set; }
        public Dictionary<string, Guid> Observations { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Unavailable { get; } = [];
        public bool IsComplete { get; set; } = true;
        public int ReadCount { get; private set; }
        public Action<int>? AfterRead { get; set; }
        public Func<PlaylistEntry, CancellationToken, Task<string?>>? OnResolve { get; set; }
        public PlaylistSyncItem SyncItem(string id) => new(
            trackIds.TryGetValue(id, out var trackId) ? trackId : null,
            Observations.TryGetValue(id, out var observationId) ? observationId : null,
            id, PlatformId);

        public Task<PlaylistRemoteSnapshot> ReadAsync(PlatformAccountContext account,
            string playlistId, CancellationToken cancellationToken)
        {
            ReadCount++;
            AfterRead?.Invoke(ReadCount);
            var items = Items.Select((id, index) => new PlaylistRemoteTrack(id,
                trackIds.TryGetValue(id, out var trackId) ? trackId : null,
                Observations.TryGetValue(id, out var observationId) ? observationId : null,
                id, index, IsAvailable: !Unavailable.Contains(id))).ToArray();
            return Task.FromResult(new PlaylistRemoteSnapshot(playlistId, Name, items,
                $"revision-{ReadCount}", IsComplete));
        }

        public async Task<string?> ResolveAsync(PlatformAccountContext account, PlaylistEntry entry,
            CancellationToken cancellationToken)
        {
            if (OnResolve is not null && await OnResolve(entry, cancellationToken) is { } resolved)
                return resolved;
            if (entry.TrackObservation?.SourceType == PlatformId)
                return entry.TrackObservation.ExternalId;
            var id = entry.TrackId is { } trackId
                ? trackIds.FirstOrDefault(pair => pair.Value == trackId).Key : null;
            return id;
        }

        public Task RenameAsync(PlatformAccountContext account, string playlistId, string name,
            CancellationToken cancellationToken)
        {
            RenameCount++;
            Name = name;
            return Task.CompletedTask;
        }
        public Task DeleteAsync(PlatformAccountContext account, string playlistId,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeWriter(FakeProvider provider) : IPlaylistWriter
    {
        public string PlatformId => provider.PlatformId;
        public int ReconcileCount { get; private set; }
        public int CreateCount { get; private set; }
        public string? FailAfterRemoving { get; set; }
        public Exception? CreationFailure { get; set; }
        public Action? OnReconcile { get; set; }
        public Task ValidateCreationAsync(PlatformAccountContext account, CancellationToken cancellationToken)
            => Task.CompletedTask;
        public Task<string> CreatePlaylistAsync(PlatformAccountContext account, string name,
            CancellationToken cancellationToken)
        {
            CreateCount++;
            provider.Items = [];
            if (CreationFailure is { } failure) throw failure;
            return Task.FromResult($"{PlatformId}-created");
        }
        public Task ValidateDestinationAsync(PlatformAccountContext account, string playlistId,
            CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ReconcileAsync(PlatformAccountContext account, string playlistId,
            IReadOnlyList<string> trackIds, CancellationToken cancellationToken)
        {
            ReconcileCount++;
            OnReconcile?.Invoke();
            if (FailAfterRemoving is { } removed)
            {
                provider.Items.Remove(removed);
                throw new PlatformApiException("provider_unavailable", "The response was lost after partial mutation.", 503);
            }
            provider.Items = [.. trackIds.Where(id => !provider.Unavailable.Contains(id)), .. provider.Items.Where(provider.Unavailable.Contains)];
            return Task.CompletedTask;
        }
    }

    private static ServiceProvider SchedulerServices(Scenario scenario)
    {
        var services = new ServiceCollection();
        services.AddSingleton(scenario.Db);
        services.AddSingleton(scenario.Coordinator);
        return services.BuildServiceProvider();
    }

    private static PlaylistSyncScheduler Scheduler(ServiceProvider services, bool enabled, bool automaticEnabled)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MusicPlaylistSync:Enabled"] = enabled.ToString(),
            ["MusicPlaylistSync:AutomaticEnabled"] = automaticEnabled.ToString()
        }).Build();
        return new PlaylistSyncScheduler(services.GetRequiredService<IServiceScopeFactory>(), configuration,
            TimeProvider.System, NullLogger<PlaylistSyncScheduler>.Instance);
    }
}
