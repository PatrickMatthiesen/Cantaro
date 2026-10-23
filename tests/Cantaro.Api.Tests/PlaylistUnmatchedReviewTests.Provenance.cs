using System.Net;
using System.Text;
using System.Text.Json;
using Cantaro.Api.Configuration;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Cantaro.Api.Services.Spotify;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed partial class PlaylistUnmatchedReviewTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfirmThenSync_ExportsChosenIdWithoutAutomaticSearch(bool customQuery)
    {
        await using var scope = await Scope.CreateAsync("spotify");
        using var http = new HttpClient(new ReviewSearchHandler()) { BaseAddress = new Uri("https://api.spotify.com") };
        var review = await scope.SearchReviewAsync(http);
        var search = await review.SearchAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id,
            scope.Entry.Id, customQuery ? "find the recording" : null, default);
        await review.ConfirmAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id,
            scope.Entry.Id, Assert.Single(search.Candidates).CandidateToken, default);
        var provider = new ReviewResolverProvider(scope.Db, scope.Playlist.Name);
        var writer = new ReviewWriter(provider);

        scope.Db.ChangeTracker.Clear(); // persisted proof must work in a new worker's read
        await RunReviewSyncAsync(scope, provider, writer);

        Assert.Equal(["chosen-id"], Assert.Single(writer.Writes));
        Assert.Equal(1, provider.Resolutions);
        Assert.Equal(0, provider.Searches);
        var link = await scope.Db.ServicePlaylistMappings.SingleAsync();
        Assert.Equal("success", link.LastSyncStatus);
        Assert.Equal(0, link.UnresolvedCount);
        Assert.Empty(await review.ListAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanonicalOnlyPrivateMapping_RemainsReviewableAndExportsConfirmedSafeId(bool customQuery)
    {
        await using var scope = await Scope.CreateAsync("spotify");
        scope.Entry.TrackObservationId = null;
        scope.Entry.TrackObservation = null;
        scope.Db.TrackSourceIds.Add(new TrackSourceId
        {
            Id = Guid.NewGuid(), TrackId = scope.Entry.TrackId!.Value,
            SourceType = "spotify", ExternalId = "other-users-private-id"
        });
        await scope.Db.SaveChangesAsync();
        using var http = new HttpClient(new ReviewSearchHandler()) { BaseAddress = new Uri("https://api.spotify.com") };
        var review = await scope.SearchReviewAsync(http);
        Assert.Equal(scope.Entry.Id, Assert.Single(await review.ListAsync(
            scope.UserId, scope.Playlist.Id, scope.Link.Id, default)).EntryId);
        var search = await review.SearchAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id,
            scope.Entry.Id, customQuery ? "custom query" : null, default);
        await review.ConfirmAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id,
            scope.Entry.Id, Assert.Single(search.Candidates).CandidateToken, default);
        var provider = new ReviewResolverProvider(scope.Db, scope.Playlist.Name);
        var writer = new ReviewWriter(provider);
        scope.Db.ChangeTracker.Clear();

        await RunReviewSyncAsync(scope, provider, writer);

        Assert.Equal(["chosen-id"], Assert.Single(writer.Writes));
        Assert.Equal(1, provider.Resolutions);
        Assert.Equal(0, provider.Searches);
        Assert.DoesNotContain("other-users-private-id", provider.RemoteIds);
        Assert.Empty(await review.ListAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, default));
    }

    [Theory]
    [InlineData("existing")]
    [InlineData("new")]
    [InlineData("reused")]
    public async Task Confirmation_PersistsAfterMaterializationAndSyncAndCanRetry(string canonical)
    {
        await using var scope = await Scope.CreateAsync("spotify");
        if (canonical != "existing")
        {
            scope.Entry.TrackId = null;
            scope.Entry.Track = null;
            scope.Entry.TrackObservation!.TrackId = null;
            scope.Entry.TrackObservation.Track = null;
            scope.Entry.TrackObservation.MatchStatus = TrackMatchingStatuses.Pending;
        }
        if (canonical == "new") scope.Db.Tracks.Remove(await scope.Db.Tracks.SingleAsync());
        if (canonical == "reused")
            scope.Db.TrackSourceIds.Add(new TrackSourceId
            {
                Id = Guid.NewGuid(), TrackId = (await scope.Db.Tracks.SingleAsync()).Id,
                SourceType = "spotify", ExternalId = "chosen-id"
            });
        await scope.Db.SaveChangesAsync();
        var token = scope.Token(Candidate("chosen-id"));
        await scope.Review.ConfirmAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, scope.Entry.Id, token, default);
        // A retried HTTP confirmation must not create duplicate identities or proof.
        await scope.Review.ConfirmAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, scope.Entry.Id, token, default);
        Assert.Single(await scope.Db.TrackSourceIds.ToListAsync(), source => source.SourceType == "spotify");
        var provider = new ReviewResolverProvider(scope.Db, scope.Playlist.Name);
        var writer = new ReviewWriter(provider);
        scope.Db.ChangeTracker.Clear();
        await RunReviewSyncAsync(scope, provider, writer);
        var savedLink = await scope.Db.ServicePlaylistMappings.SingleAsync();
        Assert.True(Assert.Single(PlaylistMatchingProgress.Read(savedLink.MatchingProgressJson)).Value.ManualConfirmation);

        // After a successful export and process restart, keep the choice even when
        // unrelated users add or remove shared catalog mappings.
        scope.Db.TrackSourceIds.Add(new TrackSourceId
        {
            Id = Guid.NewGuid(), TrackId = (await scope.Db.PlaylistEntries.SingleAsync()).TrackId!.Value,
            SourceType = "spotify", ExternalId = "unrelated-global-id"
        });
        await scope.Db.SaveChangesAsync();
        savedLink.BaselineJson = new PlaylistSyncBaseline([]).Serialize();
        (await scope.Db.ConnectedServiceAccounts.SingleAsync()).TokenVersion++;
        provider.RemoteIds = [];
        await scope.Db.SaveChangesAsync();
        scope.Db.ChangeTracker.Clear();
        await RunReviewSyncAsync(scope, provider, writer);
        Assert.Equal(0, provider.Searches);
        Assert.Equal(2, writer.Writes.Count);
        Assert.All(writer.Writes, ids => Assert.Equal(["chosen-id"], ids));
    }

    [Theory]
    [InlineData("external-account")]
    [InlineData("account-id")]
    [InlineData("mapping-id")]
    [InlineData("remote-playlist")]
    [InlineData("metadata")]
    [InlineData("version")]
    [InlineData("observation-metadata")]
    [InlineData("track")]
    [InlineData("selected-identity-conflict")]
    public async Task StalePersistedConfirmation_IsNotResolvedAndReviewRemainsRecoverable(string change)
    {
        await using var scope = await Scope.CreateAsync("spotify");
        await scope.Review.ConfirmAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id,
            scope.Entry.Id, scope.Token(Candidate("chosen-id")), default);
        var account = await scope.Db.ConnectedServiceAccounts.SingleAsync();
        var link = await scope.Db.ServicePlaylistMappings.SingleAsync();
        switch (change)
        {
            case "external-account":
                account.ExternalAccountId = link.ExternalAccountId = "new-account";
                break;
            case "account-id":
                var replacement = new ConnectedServiceAccount { Id = 52, UserId = scope.UserId,
                    Service = "spotify", ExternalAccountId = "account", ConnectionState = "connected" };
                scope.Db.ConnectedServiceAccounts.Add(replacement);
                link.ConnectedServiceAccountId = replacement.Id;
                break;
            case "mapping-id":
                scope.Db.ServicePlaylistMappings.Remove(link);
                link = new ServicePlaylistMapping { Id = Guid.NewGuid(), UserId = scope.UserId,
                    PlaylistId = scope.Playlist.Id, Service = "spotify", ServicePlaylistId = "playlist",
                    ConnectedServiceAccountId = account.Id, ExternalAccountId = account.ExternalAccountId,
                    State = "active", SyncMode = "bidirectional", MatchingProgressJson = link.MatchingProgressJson };
                scope.Db.ServicePlaylistMappings.Add(link);
                break;
            case "remote-playlist": link.ServicePlaylistId = "new-playlist"; break;
            case "metadata": scope.Entry.Track!.CanonicalMetadata = "{\"Title\":\"Different song\",\"Artist\":\"Other\"}"; break;
            case "version": scope.Entry.Track!.VersionFlags = TrackVersionFlags.Live; break;
            case "observation-metadata": scope.Entry.TrackObservation!.RawMetadata = "{\"Description\":\"Changed credits\"}"; break;
            case "track":
                var other = new Track { Id = Guid.NewGuid(), SearchTitle = "Different", SearchArtist = "Other" };
                scope.Db.Tracks.Add(other);
                scope.Entry.TrackId = scope.Entry.TrackObservation!.TrackId = other.Id;
                break;
            case "selected-identity-conflict":
                var conflicting = new Track { Id = Guid.NewGuid(), SearchTitle = "Other" };
                scope.Db.Tracks.Add(conflicting);
                (await scope.Db.TrackSourceIds.SingleAsync()).TrackId = conflicting.Id;
                break;
        }
        await scope.Db.SaveChangesAsync();
        var mappingId = link.Id;
        var provider = new ReviewResolverProvider(scope.Db, scope.Playlist.Name);
        var writer = new ReviewWriter(provider);
        scope.Db.ChangeTracker.Clear();
        Assert.Single(await scope.Review.ListAsync(scope.UserId, scope.Playlist.Id, mappingId, default));

        await RunReviewSyncAsync(scope, provider, writer, mappingId);

        Assert.Empty(writer.Writes);
        Assert.Equal(1, provider.Searches);
        Assert.Single(await scope.Review.ListAsync(scope.UserId, scope.Playlist.Id, mappingId, default));
        Assert.Equal("partial", (await scope.Db.ServicePlaylistMappings.SingleAsync()).LastSyncStatus);
    }

    [Theory]
    [InlineData("token")]
    [InlineData("account")]
    [InlineData("owner")]
    [InlineData("disconnected")]
    [InlineData("remote-playlist")]
    public async Task SearchChoice_RejectsChangedAuthorizationBeforeConfirming(string change)
    {
        await using var scope = await Scope.CreateAsync("spotify");
        using var http = new HttpClient(new ReviewSearchHandler()) { BaseAddress = new Uri("https://api.spotify.com") };
        var review = await scope.SearchReviewAsync(http);
        var searched = await review.SearchAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, scope.Entry.Id,
            "custom query", default);
        var account = await scope.Db.ConnectedServiceAccounts.SingleAsync();
        switch (change)
        {
            case "token": account.TokenVersion++; break;
            case "account": account.ExternalAccountId = scope.Link.ExternalAccountId = "other-account"; break;
            case "owner": account.UserId++; break;
            case "disconnected": account.ConnectionState = "reconnect_required"; break;
            case "remote-playlist": scope.Link.ServicePlaylistId = "other-playlist"; break;
        }
        await scope.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<PlatformApiException>(() => review.ConfirmAsync(scope.UserId, scope.Playlist.Id,
            scope.Link.Id, scope.Entry.Id, Assert.Single(searched.Candidates).CandidateToken, default));
        Assert.Empty(await scope.Db.TrackSourceIds.ToListAsync());
    }

    [Fact]
    public async Task Confirmation_ReconcilesSharedObservationWithoutSharingProofWithOtherOwner()
    {
        await using var scope = await Scope.CreateAsync("spotify");
        var trackId = scope.Entry.TrackId!.Value;
        scope.Entry.TrackId = null;
        scope.Entry.Track = null;
        scope.Entry.TrackObservation!.TrackId = null;
        scope.Entry.TrackObservation.Track = null;
        scope.Db.TrackSourceIds.Add(new TrackSourceId { Id = Guid.NewGuid(), TrackId = trackId,
            SourceType = "spotify", ExternalId = "chosen-id" });
        var survivor = new PlaylistEntry { Id = Guid.NewGuid(), PlaylistId = scope.Playlist.Id,
            TrackId = trackId, Position = -1 };
        var otherPlaylist = new Playlist { Id = Guid.NewGuid(), UserId = scope.UserId + 1, Name = "Other" };
        var otherEntry = new PlaylistEntry { Id = Guid.NewGuid(), Playlist = otherPlaylist,
            PlaylistId = otherPlaylist.Id, TrackObservationId = scope.Entry.TrackObservationId };
        scope.Db.AddRange(survivor, otherEntry);
        await scope.Db.SaveChangesAsync();
        await scope.Review.ConfirmAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id,
            scope.Entry.Id, scope.Token(Candidate("chosen-id")), default);
        Assert.False(await scope.Db.PlaylistEntries.AnyAsync(entry => entry.Id == scope.Entry.Id));
        Assert.Equal(survivor.Id, Assert.Single(PlaylistMatchingProgress.Read(
            (await scope.Db.ServicePlaylistMappings.SingleAsync()).MatchingProgressJson)).Key);
        scope.Db.ChangeTracker.Clear();
        var provider = new ReviewResolverProvider(scope.Db, scope.Playlist.Name);
        var writer = new ReviewWriter(provider);
        await RunReviewSyncAsync(scope, provider, writer);
        Assert.Equal(["chosen-id"], Assert.Single(writer.Writes));
        var other = await scope.Db.PlaylistEntries.SingleAsync(entry => entry.Id == otherEntry.Id);
        var otherResult = await PlaylistDestinationIdentityResolver.ResolveAsync(scope.Db, new TrackMatchingOptions(),
            (_, _, _) => throw new InvalidOperationException(), (_, _, _) => throw new InvalidOperationException(),
            new PlatformAccountContext(otherPlaylist.UserId, 99, "other-account"), "spotify", other,
            (_, _) => Task.FromResult<IReadOnlyList<TrackMatchSearchCandidate>>([]), default);
        Assert.Null(otherResult);
    }

    [Fact]
    public async Task Confirmation_SurvivesBatchResumeWhileNegativeSearchesRemainCheckpointed()
    {
        await using var scope = await Scope.CreateAsync("spotify");
        await scope.Review.ConfirmAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id,
            scope.Entry.Id, scope.Token(Candidate("chosen-id")), default);
        for (var i = 0; i < 26; i++)
        {
            var entry = scope.AddEntry($"Unknown {i}", $"video-{i}");
            entry.Position = i + 1;
            entry.Track!.SearchArtist = "Artist";
            entry.TrackObservation!.Artist = "Artist";
        }
        await scope.Db.SaveChangesAsync();
        var provider = new ReviewResolverProvider(scope.Db, scope.Playlist.Name);
        var writer = new ReviewWriter(provider);
        await RunReviewSyncAsync(scope, provider, writer);
        var link = await scope.Db.ServicePlaylistMappings.SingleAsync();
        Assert.Equal("pending", link.LastSyncStatus);
        Assert.Empty(writer.Writes);
        Assert.Equal(25, provider.Searches);
        Assert.Equal(26, link.MatchingProcessedCount);
        scope.Db.ChangeTracker.Clear();

        await RunReviewSyncAsync(scope, provider, writer);

        Assert.Equal(26, provider.Searches);
        Assert.Equal(["chosen-id"], Assert.Single(writer.Writes));
        link = await scope.Db.ServicePlaylistMappings.SingleAsync();
        Assert.Equal("partial", link.LastSyncStatus);
        Assert.Equal(26, link.UnresolvedCount);
        Assert.True(Assert.Single(PlaylistMatchingProgress.Read(link.MatchingProgressJson)).Value.ManualConfirmation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnedObservation_ReviewAndResolverAgreeEvenWithoutGlobalMapping(bool elsewhere)
    {
        await using var scope = await Scope.CreateAsync("spotify");
        var observation = scope.Entry.TrackObservation!;
        observation.SourceType = "spotify";
        if (elsewhere)
        {
            scope.Entry.TrackObservationId = null;
            scope.Entry.TrackObservation = null;
            var playlist = new Playlist { Id = Guid.NewGuid(), UserId = scope.UserId, Name = "Owned" };
            scope.Db.PlaylistEntries.Add(new PlaylistEntry { Id = Guid.NewGuid(), Playlist = playlist,
                PlaylistId = playlist.Id, TrackId = scope.Entry.TrackId, TrackObservationId = observation.Id });
        }
        await scope.Db.SaveChangesAsync();
        scope.Db.ChangeTracker.Clear();
        Assert.Empty(await scope.Review.ListAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, default));
        var error = await Assert.ThrowsAsync<PlatformApiException>(() => scope.Review.SearchAsync(scope.UserId,
            scope.Playlist.Id, scope.Link.Id, scope.Entry.Id, "query", default));
        Assert.Equal("unmatched_entry_not_found", error.Code);
        var provider = new ReviewResolverProvider(scope.Db, scope.Playlist.Name);
        var writer = new ReviewWriter(provider);
        // Fake reads need the global identity to map remote responses back to the
        // canonical track after writing, just as production imports observations.
        scope.Db.TrackSourceIds.Add(new TrackSourceId { Id = Guid.NewGuid(), TrackId = scope.Entry.TrackId!.Value,
            SourceType = "spotify", ExternalId = observation.ExternalId });
        await scope.Db.SaveChangesAsync();
        await RunReviewSyncAsync(scope, provider, writer);
        Assert.Equal([observation.ExternalId], Assert.Single(writer.Writes));
        Assert.Equal(0, provider.Searches);
    }

    [Fact]
    public async Task Confirmation_CannotFollowPlaylistAndAccountToAnotherOwner()
    {
        await using var scope = await Scope.CreateAsync("spotify");
        await scope.Review.ConfirmAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id,
            scope.Entry.Id, scope.Token(Candidate("chosen-id")), default);
        var newOwner = scope.UserId + 1;
        scope.Playlist.UserId = scope.Link.UserId = newOwner;
        (await scope.Db.ConnectedServiceAccounts.SingleAsync()).UserId = newOwner;
        scope.Link.BaselineJson = new PlaylistSyncBaseline([]).Serialize();
        scope.Link.BaselineName = scope.Playlist.Name;
        await scope.Db.SaveChangesAsync();
        scope.Db.ChangeTracker.Clear();
        Assert.Single(await scope.Review.ListAsync(newOwner, scope.Playlist.Id, scope.Link.Id, default));
        var provider = new ReviewResolverProvider(scope.Db, scope.Playlist.Name);
        var writer = new ReviewWriter(provider);
        var coordinator = new PlaylistSyncCoordinator(scope.Db, [provider], [writer], TimeProvider.System,
            NullLogger<PlaylistSyncCoordinator>.Instance);

        await coordinator.RunAsync(newOwner, scope.Playlist.Id, "spotify", default);

        Assert.Empty(writer.Writes);
        Assert.Equal(1, provider.Searches);
        Assert.Single(await scope.Review.ListAsync(newOwner, scope.Playlist.Id, scope.Link.Id, default));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task LegacyCheckpoint_DoesNotHideUnmatchedEntryOrBlockRecovery(int proofVersion)
    {
        await using var scope = await Scope.CreateAsync("spotify");
        scope.Entry.TrackObservationId = null;
        scope.Entry.TrackObservation = null;
        scope.Link.MatchingProgressJson = JsonSerializer.Serialize(new Dictionary<Guid, PlaylistMatchingResult>
        {
            [scope.Entry.Id] = new(PlaylistMatchingProgress.Fingerprint(scope.Entry), "private-id", proofVersion)
        });
        await scope.Db.SaveChangesAsync();
        Assert.Single(await scope.Review.ListAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, default));
        var provider = new ReviewResolverProvider(scope.Db, scope.Playlist.Name);
        var writer = new ReviewWriter(provider);
        await RunReviewSyncAsync(scope, provider, writer);
        Assert.Empty(writer.Writes);
        Assert.Equal("partial", (await scope.Db.ServicePlaylistMappings.SingleAsync()).LastSyncStatus);
        using var http = new HttpClient(new ReviewSearchHandler()) { BaseAddress = new Uri("https://api.spotify.com") };
        var review = await scope.SearchReviewAsync(http);
        var searched = await review.SearchAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, scope.Entry.Id,
            "custom query", default);
        await review.ConfirmAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, scope.Entry.Id,
            Assert.Single(searched.Candidates).CandidateToken, default);
        await RunReviewSyncAsync(scope, provider, writer);
        Assert.Equal(["chosen-id"], Assert.Single(writer.Writes));
    }

    private static async Task RunReviewSyncAsync(Scope scope, ReviewResolverProvider provider, ReviewWriter writer, Guid? mappingId = null)
    {
        var linkId = mappingId ?? scope.Link.Id;
        var link = await scope.Db.ServicePlaylistMappings.SingleAsync(item => item.Id == linkId);
        link.BaselineName ??= scope.Playlist.Name;
        link.BaselineJson ??= new PlaylistSyncBaseline([]).Serialize();
        await scope.Db.SaveChangesAsync();
        var coordinator = new PlaylistSyncCoordinator(scope.Db, [provider], [writer], TimeProvider.System,
            NullLogger<PlaylistSyncCoordinator>.Instance);
        await coordinator.RunAsync(scope.UserId, scope.Playlist.Id, "spotify", default);
    }

    private sealed class ReviewSearchHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.True(request.Method == HttpMethod.Get || request.RequestUri!.AbsolutePath == "/api/token");
            object response = request.RequestUri!.AbsolutePath switch
            {
                "/v1/me" => new { id = "account" },
                "/api/token" => new { access_token = "catalog-token", expires_in = 3600, token_type = "Bearer" },
                "/v1/search" => new { tracks = new { items = new[] { new
                {
                    type = "track", id = "chosen-id", name = "Everything Goes On", duration_ms = 160000,
                    artists = new[] { new { name = "Porter Robinson" } }
                } } } },
                _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(response), Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class NoDelay : ISpotifyRetryDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class ReviewResolverProvider(ApplicationDbContext db, string name) : IPlaylistSyncProvider
    {
        public string PlatformId => "spotify";
        public int Searches { get; private set; }
        public bool PauseSearch { get; init; }
        public TaskCompletionSource SearchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ContinueSearch { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Resolutions { get; private set; }
        public List<string> RemoteIds { get; set; } = [];
        public Task<IReadOnlyList<PlaylistRemoteCatalogItem>> ListPlaylistsAsync(PlatformAccountContext account, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<PlaylistRemoteCatalogItem>>([]);
        public async Task<PlaylistRemoteSnapshot> ReadAsync(PlatformAccountContext account, string playlistId, CancellationToken ct)
        {
            var items = new List<PlaylistRemoteTrack>();
            for (var i = 0; i < RemoteIds.Count; i++)
            {
                var id = RemoteIds[i];
                var trackId = await db.TrackSourceIds.Where(s => s.SourceType == PlatformId && s.ExternalId == id)
                    .Select(s => (Guid?)s.TrackId).SingleOrDefaultAsync(ct);
                items.Add(new PlaylistRemoteTrack(id, trackId, null, id, i));
            }
            return new PlaylistRemoteSnapshot(playlistId, name, items, "test");
        }
        public Task<string?> ResolveAsync(PlatformAccountContext account, PlaylistEntry entry, CancellationToken ct)
        {
            Resolutions++;
            return PlaylistDestinationIdentityResolver.ResolveAsync(db, new TrackMatchingOptions(),
                (_, _, _) => throw new InvalidOperationException("Already canonical."),
                (observation, track, token) => new PlaylistCanonicalReconciliationService(db)
                    .ReconcileObservationAsync(observation, track, token), account, PlatformId, entry,
                async (_, token) =>
                {
                    Searches++;
                    if (PauseSearch && Searches == 1)
                    {
                        SearchStarted.TrySetResult();
                        await ContinueSearch.Task.WaitAsync(token);
                    }
                    return [];
                }, ct);
        }
        public Task RenameAsync(PlatformAccountContext account, string id, string title, CancellationToken ct) => Task.CompletedTask;
        public Task DeleteAsync(PlatformAccountContext account, string id, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class ReviewWriter(ReviewResolverProvider provider) : IPlaylistWriter
    {
        public string PlatformId => "spotify";
        public List<string[]> Writes { get; } = [];
        public Task ValidateCreationAsync(PlatformAccountContext account, CancellationToken ct) => Task.CompletedTask;
        public Task<string> CreatePlaylistAsync(PlatformAccountContext account, string name, CancellationToken ct) => Task.FromResult("created");
        public Task ValidateDestinationAsync(PlatformAccountContext account, string id, CancellationToken ct) => Task.CompletedTask;
        public Task ReconcileAsync(PlatformAccountContext account, string id, IReadOnlyList<string> ids, CancellationToken ct)
        {
            Writes.Add(ids.ToArray());
            provider.RemoteIds = ids.ToList();
            return Task.CompletedTask;
        }
    }
}
