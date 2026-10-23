using System.Text.Json;
using Cantaro.Api.Configuration;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class PlaylistSyncDestinationProvenanceTests
{
    [Fact]
    public async Task RelatedSongAdd_DoesNotExportAnotherUsersGlobalIdentityOrLegacySavedResults()
    {
        await using var scenario = await Scenario.CreateAsync();
        var entry = await scenario.AddRelatedSongAsync();
        scenario.Mapping.MatchingProgressJson = JsonSerializer.Serialize(new Dictionary<Guid, object>
        {
            [entry.Id] = new
            {
                Fingerprint = PlaylistMatchingProgress.Fingerprint(entry),
                ExternalId = Scenario.PrivateId
            }
        });
        scenario.Mapping.PendingWriteJson = new PlaylistSyncBaseline(
            [new PlaylistSyncItem(scenario.RelatedTrack.Id, null, Scenario.PrivateId, "spotify")]).Serialize();
        scenario.Provider.SearchResults = [];
        await scenario.Db.SaveChangesAsync();

        await scenario.Coordinator.RunAsync(Scenario.RequesterId, scenario.SyncPlaylist.Id, "spotify", default);

        Assert.Equal(1, scenario.Provider.SearchCount);
        Assert.Equal("partial", scenario.Mapping.LastSyncStatus);
        Assert.Equal(1, scenario.Mapping.UnresolvedCount);
        Assert.DoesNotContain(scenario.Writer.Writes.SelectMany(ids => ids), id => id == Scenario.PrivateId);
        Assert.DoesNotContain(Scenario.PrivateId, scenario.Provider.RemoteIds);
        Assert.Null(scenario.Mapping.MatchingProgressJson);
        Assert.Null(scenario.Mapping.PendingWriteJson);
    }

    [Fact]
    public async Task RelatedSongAdd_WritesOnlyFreshlyMatchedProviderCandidate()
    {
        await using var scenario = await Scenario.CreateAsync();
        await scenario.AddRelatedSongAsync();
        scenario.Provider.SearchResults = [Scenario.Candidate("safe-result")];

        await scenario.Coordinator.RunAsync(Scenario.RequesterId, scenario.SyncPlaylist.Id, "spotify", default);

        Assert.Equal(1, scenario.Provider.SearchCount);
        Assert.Equal(["safe-result"], Assert.Single(scenario.Writer.Writes));
        Assert.DoesNotContain(Scenario.PrivateId, scenario.Writer.Writes.SelectMany(ids => ids));
        Assert.Equal("success", scenario.Mapping.LastSyncStatus);
        Assert.Contains(await scenario.Db.TrackSourceIds.ToListAsync(), source =>
            source.TrackId == scenario.RelatedTrack.Id && source.SourceType == "spotify"
            && source.ExternalId == "safe-result");
    }

    [Fact]
    public async Task RelatedSongAdd_ReusesRequesterOwnedObservationInsteadOfAnotherUsersMapping()
    {
        await using var scenario = await Scenario.CreateAsync();
        await scenario.AddOwnedObservationAsync("spotify", "requester-owned-id");
        await scenario.AddRelatedSongAsync();

        await scenario.Coordinator.RunAsync(Scenario.RequesterId, scenario.SyncPlaylist.Id, "spotify", default);

        Assert.Equal(0, scenario.Provider.SearchCount);
        Assert.Equal(["requester-owned-id"], Assert.Single(scenario.Writer.Writes));
        Assert.DoesNotContain(Scenario.PrivateId, scenario.Writer.Writes.SelectMany(ids => ids));
        Assert.Equal("success", scenario.Mapping.LastSyncStatus);
    }

    [Fact]
    public async Task RelatedSongAdd_DifferentProviderObservationDoesNotAuthorizeGlobalDestinationIdentity()
    {
        await using var scenario = await Scenario.CreateAsync();
        await scenario.AddOwnedObservationAsync("youtube", "requester-youtube-id");
        await scenario.AddRelatedSongAsync();
        scenario.Provider.SearchResults = [];

        await scenario.Coordinator.RunAsync(Scenario.RequesterId, scenario.SyncPlaylist.Id, "spotify", default);

        Assert.Equal(1, scenario.Provider.SearchCount);
        Assert.Empty(scenario.Writer.Writes);
        Assert.DoesNotContain(Scenario.PrivateId, scenario.Provider.RemoteIds);
        Assert.Equal("partial", scenario.Mapping.LastSyncStatus);
    }

    [Fact]
    public async Task RelatedSongAdd_CurrentProviderPlaylistReadAuthorizesItsIdentity()
    {
        await using var scenario = await Scenario.CreateAsync();
        await scenario.AddRelatedSongAsync();
        scenario.Provider.RemoteIds = [Scenario.PrivateId];

        await scenario.Coordinator.RunAsync(Scenario.RequesterId, scenario.SyncPlaylist.Id, "spotify", default);

        Assert.Equal(0, scenario.Provider.SearchCount);
        Assert.Empty(scenario.Writer.Writes);
        Assert.Equal("success", scenario.Mapping.LastSyncStatus);
        Assert.Equal(0, scenario.Mapping.UnresolvedCount);
    }

    private sealed class Scenario : IAsyncDisposable
    {
        public const int RequesterId = 801;
        public const string PrivateId = "other-users-private-id";
        private Scenario(ApplicationDbContext db, Playlist syncPlaylist,
            Track relatedTrack, ServicePlaylistMapping mapping)
        {
            Db = db;
            SyncPlaylist = syncPlaylist;
            RelatedTrack = relatedTrack;
            Mapping = mapping;
            Provider = new ResolverBackedProvider(db);
            Writer = new RecordingWriter(Provider);
            Coordinator = new PlaylistSyncCoordinator(db, [Provider], [Writer], TimeProvider.System,
                NullLogger<PlaylistSyncCoordinator>.Instance);
        }

        public ApplicationDbContext Db { get; }
        public Playlist SyncPlaylist { get; }
        public Track RelatedTrack { get; }
        public ServicePlaylistMapping Mapping { get; private set; }
        public ResolverBackedProvider Provider { get; }
        public RecordingWriter Writer { get; }
        public PlaylistSyncCoordinator Coordinator { get; }

        public static async Task<Scenario> CreateAsync()
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var requester = TestUserFactory.Create(RequesterId, "sync-requester@example.com");
            var other = TestUserFactory.Create(802, "sync-other@example.com");
            var accessPlaylist = new Playlist { Id = Guid.NewGuid(), UserId = requester.Id, Name = "Library" };
            var syncPlaylist = new Playlist
            {
                Id = Guid.NewGuid(), UserId = requester.Id, Name = "Synced",
                SyncEnabled = true, NextSyncAt = DateTimeOffset.UtcNow
            };
            var privatePlaylist = new Playlist { Id = Guid.NewGuid(), UserId = other.Id, Name = "Private" };
            var anchor = new Track { Id = Guid.NewGuid(), SearchTitle = "Anchor", SearchArtist = "Artist" };
            var related = new Track
            {
                Id = Guid.NewGuid(), SearchTitle = "Song", SearchArtist = "Artist",
                CanonicalMetadata = JsonSerializer.Serialize(new TrackCanonicalMetadata
                {
                    Title = "Song", Artist = "Artist", DurationSeconds = 180
                })
            };
            var privateObservation = new TrackObservation
            {
                Id = Guid.NewGuid(), TrackId = related.Id, SourceType = "spotify", ExternalId = PrivateId,
                Title = "Song", Artist = "Artist", DurationSeconds = 180,
                MatchStatus = TrackMatchingStatuses.Matched
            };
            related.SourceIds.Add(new TrackSourceId
            {
                Id = Guid.NewGuid(), TrackId = related.Id, SourceType = "spotify", ExternalId = PrivateId,
                Confidence = 1, LastVerifiedAt = DateTimeOffset.UtcNow
            });
            var account = new ConnectedServiceAccount
            {
                Id = 91, UserId = requester.Id, Service = "spotify",
                ExternalAccountId = "requester-account", ConnectionState = "connected"
            };
            var mapping = new ServicePlaylistMapping
            {
                Id = Guid.NewGuid(), PlaylistId = syncPlaylist.Id, UserId = requester.Id,
                ConnectedServiceAccountId = account.Id, ExternalAccountId = account.ExternalAccountId,
                Service = "spotify", ServicePlaylistId = "destination", SyncMode = "bidirectional",
                State = "active", BaselineName = syncPlaylist.Name,
                BaselineJson = new PlaylistSyncBaseline([]).Serialize()
            };
            db.AddRange(requester, other, accessPlaylist, syncPlaylist, privatePlaylist, anchor, related,
                privateObservation, account, mapping);
            db.TrackRelations.Add(new TrackRelation
            {
                FromTrackId = anchor.Id, ToTrackId = related.Id, RelationType = TrackRelationType.RemixOf
            });
            db.PlaylistEntries.AddRange(
                new PlaylistEntry
                {
                    Id = Guid.NewGuid(), PlaylistId = accessPlaylist.Id, TrackId = anchor.Id,
                    Position = 0, SourceService = "cantaro"
                },
                new PlaylistEntry
                {
                    Id = Guid.NewGuid(), PlaylistId = privatePlaylist.Id, TrackId = related.Id,
                    TrackObservationId = privateObservation.Id, Position = 0, SourceService = "spotify"
                });
            await db.SaveChangesAsync();
            return new Scenario(db, syncPlaylist, related, mapping);
        }

        public async Task<PlaylistEntry> AddRelatedSongAsync()
        {
            var library = new MusicLibraryQueryService(Db);
            Assert.NotNull(await library.GetCanonicalSongAsync(RelatedTrack.Id, RequesterId, default));
            Assert.True(await library.AddCanonicalSongToPlaylistAsync(
                RelatedTrack.Id, SyncPlaylist.Id, RequesterId, default));
            Db.ChangeTracker.Clear();
            Mapping = await Db.ServicePlaylistMappings.SingleAsync();
            return await Db.PlaylistEntries.AsNoTracking()
                .Include(entry => entry.Track).ThenInclude(track => track!.SourceIds)
                .Include(entry => entry.TrackObservation)
                .SingleAsync(entry => entry.PlaylistId == SyncPlaylist.Id && entry.TrackId == RelatedTrack.Id);
        }

        public async Task AddOwnedObservationAsync(string service, string externalId)
        {
            var playlist = new Playlist { Id = Guid.NewGuid(), UserId = RequesterId, Name = $"Owned {service}" };
            var observation = new TrackObservation
            {
                Id = Guid.NewGuid(), TrackId = RelatedTrack.Id, SourceType = service, ExternalId = externalId,
                Title = "Song", Artist = "Artist", DurationSeconds = 180,
                MatchStatus = TrackMatchingStatuses.Matched
            };
            Db.PlaylistEntries.Add(new PlaylistEntry
            {
                Id = Guid.NewGuid(), Playlist = playlist, PlaylistId = playlist.Id,
                TrackId = RelatedTrack.Id, TrackObservation = observation,
                TrackObservationId = observation.Id, SourceService = service
            });
            await Db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
        }

        public static TrackMatchSearchCandidate Candidate(string externalId) => new()
        {
            CandidateSource = "spotify", ExternalId = externalId, Title = "Song", Artist = "Artist",
            ArtistCredits = ["Artist"], DurationSeconds = 180
        };
    }

    private sealed class ResolverBackedProvider(ApplicationDbContext db) : IPlaylistSyncProvider
    {
        public string PlatformId => "spotify";
        public List<string> RemoteIds { get; set; } = [];
        public IReadOnlyList<TrackMatchSearchCandidate> SearchResults { get; set; } = [];
        public int SearchCount { get; private set; }

        public Task<IReadOnlyList<PlaylistRemoteCatalogItem>> ListPlaylistsAsync(
            PlatformAccountContext account, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<PlaylistRemoteCatalogItem>>([]);

        public async Task<PlaylistRemoteSnapshot> ReadAsync(
            PlatformAccountContext account, string playlistId, CancellationToken cancellationToken)
        {
            var tracks = new List<PlaylistRemoteTrack>();
            for (var index = 0; index < RemoteIds.Count; index++)
            {
                var externalId = RemoteIds[index];
                var observation = await db.TrackObservations.AsNoTracking().SingleOrDefaultAsync(item =>
                    item.SourceType == PlatformId && item.ExternalId == externalId, cancellationToken);
                var trackId = observation?.TrackId ?? await db.TrackSourceIds.AsNoTracking()
                    .Where(item => item.SourceType == PlatformId && item.ExternalId == externalId)
                    .Select(item => (Guid?)item.TrackId).SingleOrDefaultAsync(cancellationToken);
                tracks.Add(new PlaylistRemoteTrack(externalId, trackId, observation?.Id, externalId, index));
            }
            return new PlaylistRemoteSnapshot(playlistId, "Synced", tracks, $"revision-{RemoteIds.Count}");
        }

        public Task<string?> ResolveAsync(
            PlatformAccountContext account, PlaylistEntry entry, CancellationToken cancellationToken)
            => PlaylistDestinationIdentityResolver.ResolveAsync(db, new TrackMatchingOptions(),
                (_, _, _) => throw new InvalidOperationException("Test entries are already canonical."),
                (_, _, _) => throw new InvalidOperationException("Test entries are already reconciled."),
                account, PlatformId, entry,
                (_, _) =>
                {
                    SearchCount++;
                    return Task.FromResult(SearchResults);
                }, cancellationToken);

        public Task RenameAsync(PlatformAccountContext account, string playlistId, string name,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeleteAsync(PlatformAccountContext account, string playlistId,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingWriter(ResolverBackedProvider provider) : IPlaylistWriter
    {
        public string PlatformId => "spotify";
        public List<string[]> Writes { get; } = [];
        public Task ValidateCreationAsync(PlatformAccountContext account, CancellationToken cancellationToken)
            => Task.CompletedTask;
        public Task<string> CreatePlaylistAsync(PlatformAccountContext account, string name,
            CancellationToken cancellationToken) => Task.FromResult("created");
        public Task ValidateDestinationAsync(PlatformAccountContext account, string playlistId,
            CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ReconcileAsync(PlatformAccountContext account, string playlistId,
            IReadOnlyList<string> trackIds, CancellationToken cancellationToken)
        {
            Writes.Add(trackIds.ToArray());
            provider.RemoteIds = trackIds.ToList();
            return Task.CompletedTask;
        }
    }
}
