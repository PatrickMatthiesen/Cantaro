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
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed partial class PlaylistUnmatchedReviewTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("USABC2400002", false)]
    [InlineData(null, true)]
    public Task ProductionSpotifyRead_ScopedExportDoesNotIngestDestination(string? isrc, bool canonicalOnly)
        => AssertProductionReadIsolationAsync(isrc, canonicalOnly);

    [Theory]
    [InlineData(null)]
    [InlineData("USABC2400002")]
    public async Task ProductionSpotifyRead_FirstVerificationDoesNotPromoteScalarsOrCreateRecording(string? isrc)
    {
        await using var fixture = await ProductionSpotifyReadScope.CreateAsync(isrc);
        var before = await fixture.Db.Tracks.AsNoTracking().SingleAsync();
        await fixture.SyncAsync();
        var after = await fixture.Db.Tracks.AsNoTracking().SingleAsync(item => item.Id == before.Id);
        Assert.Equal((before.Isrc, before.CanonicalMetadata), (after.Isrc, after.CanonicalMetadata));
        Assert.Single(await fixture.Db.Tracks.AsNoTracking().ToArrayAsync());
        await fixture.AssertNoDestinationIngestionAsync();
    }

    private static async Task AssertProductionReadIsolationAsync(string? isrc, bool canonicalOnly, bool postgres = false)
    {
        await using var fixture = await ProductionSpotifyReadScope.CreateAsync(isrc, canonicalOnly, postgres);
        var before = await fixture.Db.Tracks.AsNoTracking().SingleAsync();
        for (var pass = 0; pass < 2; pass++) await fixture.SyncAsync();
        await fixture.AssertOriginalMembershipAsync();
        Assert.Equal(["chosen-id"], fixture.Handler.RemoteIds);
        Assert.Single(fixture.Writer.Writes);
        Assert.DoesNotContain(fixture.Writer.Writes, ids => ids.Length == 0);
        await fixture.AssertNoDestinationIngestionAsync();
        var after = await fixture.Db.Tracks.AsNoTracking().SingleAsync();
        Assert.Equal((before.Isrc, before.CanonicalMetadata, before.MbidRecording),
            (after.Isrc, after.CanonicalMetadata, after.MbidRecording));
        Assert.True(fixture.Handler.Reads >= 5); // initial, pre-write, verification, then repeated current/pre-write reads
    }

    [Theory]
    [InlineData("artwork")]
    [InlineData("title")]
    [InlineData("duration")]
    public async Task ProductionSpotifyRead_MetadataChangesDoNotPromoteReadMembership(string change)
    {
        await using var fixture = await ProductionSpotifyReadScope.CreateAsync();
        await fixture.SyncAsync();
        await fixture.ChangeMetadataAsync(change);
        for (var pass = 0; pass < 2; pass++) await fixture.SyncAsync();
        await fixture.AssertOriginalMembershipAsync();
        await fixture.AssertNoDestinationIngestionAsync();
        Assert.Equal(["chosen-id"], fixture.Handler.RemoteIds);
        Assert.Single(fixture.Writer.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProductionSpotifyRead_RepeatedOccurrencesRespectDuplicatePolicy(bool duplicates)
    {
        await using var fixture = await ProductionSpotifyReadScope.CreateAsync();
        (await fixture.Db.Playlists.SingleAsync()).AllowDuplicateTracks = duplicates;
        await fixture.Db.SaveChangesAsync();
        await fixture.SyncAsync();
        fixture.Handler.RemoteIds = ["chosen-id", "chosen-id"];
        for (var pass = 0; pass < 2; pass++) await fixture.SyncAsync();
        var entries = await fixture.Db.PlaylistEntries.AsNoTracking().OrderBy(item => item.Position).ToArrayAsync();
        Assert.Equal(duplicates ? 2 : 1, entries.Length);
        Assert.Equal(fixture.Scope.Entry.Id, entries[0].Id);
        Assert.All(entries, item => Assert.Equal((fixture.Scope.Entry.TrackId, fixture.Scope.Entry.TrackObservationId),
            (item.TrackId, item.TrackObservationId)));
        Assert.Equal(Enumerable.Repeat("chosen-id", entries.Length), fixture.Handler.RemoteIds);
        await fixture.AssertNoDestinationIngestionAsync();
    }

    [Theory]
    [InlineData("write")]
    [InlineData("verification")]
    public async Task ProductionSpotifyRead_InterruptedExportRecoversBeforeMaterialization(string interruption)
    {
        await using var fixture = await ProductionSpotifyReadScope.CreateAsync("USABC2400002");
        fixture.Writer.ThrowAfterWrite = interruption == "write";
        fixture.Writer.FailVerificationRead = interruption == "verification";
        await fixture.SyncAsync();
        var link = await fixture.Db.ServicePlaylistMappings.SingleAsync();
        Assert.Equal("error", link.LastSyncStatus);
        Assert.NotNull(link.PendingWriteJson);
        Assert.Null((await fixture.Db.Playlists.AsNoTracking().SingleAsync()).SyncLeaseId);
        await fixture.ChangeMetadataAsync("artwork");
        for (var pass = 0; pass < 2; pass++) await fixture.SyncAsync();
        await fixture.AssertOriginalMembershipAsync();
        await fixture.AssertNoDestinationIngestionAsync();
        Assert.Equal(["chosen-id"], fixture.Handler.RemoteIds);
        Assert.Single(fixture.Writer.Writes);
        Assert.Null((await fixture.Db.ServicePlaylistMappings.SingleAsync()).PendingWriteJson);
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("initialize")]
    [InlineData("order")]
    public async Task ProductionSpotifyRead_OtherReadCallersUseScopedContext(string caller)
    {
        await using var fixture = await ProductionSpotifyReadScope.CreateAsync();
        await fixture.SyncAsync();
        await fixture.ChangeMetadataAsync("artwork");
        fixture.Db.ChangeTracker.Clear();
        if (caller == "direct")
        {
            var snapshot = await fixture.Provider.ReadAsync(fixture.Account, "playlist", default);
            var item = Assert.Single(snapshot.Tracks);
            Assert.Equal((fixture.Scope.Entry.TrackId, fixture.Scope.Entry.TrackObservationId), (item.TrackId, item.ObservationId));
        }
        else if (caller == "initialize")
            await fixture.Coordinator().InitializeLinkAsync(fixture.Account.UserId, fixture.Scope.Playlist.Id,
                fixture.Scope.Link.Id, "combine", default);
        else
        {
            (await fixture.Db.ServicePlaylistMappings.SingleAsync()).LastSyncStatus = "order_conflict";
            await fixture.Db.SaveChangesAsync();
            await fixture.Coordinator().ResolveOrderAsync(fixture.Account.UserId, fixture.Scope.Playlist.Id,
                fixture.Scope.Link.Id, "platform", default);
        }
        await fixture.AssertNoDestinationIngestionAsync();
        await fixture.AssertOriginalMembershipAsync(requireSuccess: false);
    }

    [Fact]
    public async Task ProductionSpotifyRead_NewRemoteContentStillImportsAlongsideScopedExport()
    {
        await using var fixture = await ProductionSpotifyReadScope.CreateAsync("USABC2400002");
        await fixture.SyncAsync();
        fixture.Handler.RemoteIds.Add("fresh-id");
        for (var pass = 0; pass < 2; pass++) await fixture.SyncAsync();
        var entries = await fixture.Db.PlaylistEntries.AsNoTracking().OrderBy(item => item.Position).ToArrayAsync();
        Assert.Equal(2, entries.Length);
        Assert.Equal(fixture.Scope.Entry.Id, entries[0].Id);
        var source = await fixture.Db.TrackSourceIds.SingleAsync(item => item.SourceType == "spotify" && item.ExternalId == "fresh-id");
        Assert.Equal(source.TrackId, entries[1].TrackId);
        Assert.Equal("USNEW2400001", (await fixture.Db.Tracks.SingleAsync(item => item.Id == source.TrackId)).Isrc);
        Assert.True(await fixture.Db.TrackArtistCredits.AnyAsync(item => item.TrackId == source.TrackId));
        await fixture.AssertNoDestinationIngestionAsync();
        Assert.Equal(["chosen-id", "fresh-id"], fixture.Handler.RemoteIds);
    }

    [Fact]
    public async Task ProductionSpotifyRead_PreWriteExternalEditRemainsIntactAndImportsOnRetry()
    {
        await using var fixture = await ProductionSpotifyReadScope.CreateAsync("USABC2400002");
        await fixture.SyncAsync();
        var freshRead = fixture.Handler.Reads + 2;
        fixture.Handler.OnRead = number =>
        {
            if (number == freshRead) fixture.Handler.RemoteIds.Add("fresh-id");
        };
        await fixture.SyncAsync();
        Assert.Equal("error", (await fixture.Db.ServicePlaylistMappings.SingleAsync()).LastSyncStatus);
        Assert.Equal(["chosen-id", "fresh-id"], fixture.Handler.RemoteIds);
        Assert.Single(fixture.Writer.Writes);
        await fixture.AssertNoDestinationIngestionAsync();
        await fixture.SyncAsync();
        Assert.Equal(2, await fixture.Db.PlaylistEntries.CountAsync());
        Assert.Equal("success", (await fixture.Db.ServicePlaylistMappings.SingleAsync()).LastSyncStatus);
        Assert.Equal(["chosen-id", "fresh-id"], fixture.Handler.RemoteIds);
        Assert.Single(fixture.Writer.Writes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProductionSpotifyRead_RealRemovalStillPropagates(bool removeLocal)
    {
        await using var fixture = await ProductionSpotifyReadScope.CreateAsync();
        await fixture.SyncAsync();
        if (removeLocal) fixture.Db.PlaylistEntries.Remove(await fixture.Db.PlaylistEntries.SingleAsync());
        else fixture.Handler.RemoteIds = [];
        await fixture.Db.SaveChangesAsync();
        for (var pass = 0; pass < 2; pass++) await fixture.SyncAsync();
        Assert.Empty(await fixture.Db.PlaylistEntries.ToArrayAsync());
        Assert.Empty(fixture.Handler.RemoteIds);
        Assert.Equal(removeLocal ? 2 : 1, fixture.Writer.Writes.Count);
        await fixture.AssertNoDestinationIngestionAsync();
        Assert.Equal("success", (await fixture.Db.ServicePlaylistMappings.SingleAsync()).LastSyncStatus);
    }

    [Fact]
    public async Task ProductionSpotifyRead_IndependentPlaylistImportDoesNotBorrowScopedMembership()
    {
        await using var fixture = await ProductionSpotifyReadScope.CreateAsync("USABC2400002");
        await fixture.SyncAsync();
        var importedId = await fixture.Coordinator().ImportAsync(fixture.Account, "spotify", "independent-playlist", default);
        var imported = await fixture.Db.PlaylistEntries.SingleAsync(item => item.PlaylistId == importedId);
        Assert.NotEqual(fixture.Scope.Entry.TrackId, imported.TrackId);
        var source = await fixture.Db.TrackSourceIds.SingleAsync(item => item.SourceType == "spotify" && item.ExternalId == "chosen-id");
        Assert.Equal(source.TrackId, imported.TrackId);
        Assert.Equal("USABC2400001", (await fixture.Db.Tracks.SingleAsync(item => item.Id == source.TrackId)).Isrc);
        Assert.True(await fixture.Db.TrackArtistCredits.AnyAsync(item => item.TrackId == source.TrackId));
        Assert.Equal("USABC2400002", (await fixture.Db.Tracks.SingleAsync(item => item.Id == fixture.Scope.Entry.TrackId)).Isrc);
    }

    [Fact]
    public async Task ProductionSpotifyRead_AccountChangeRejectsReadBeforeIngestion()
    {
        await using var fixture = await ProductionSpotifyReadScope.CreateAsync();
        await fixture.SyncAsync();
        var reads = fixture.Handler.Reads;
        (await fixture.Db.ConnectedServiceAccounts.SingleAsync()).ExternalAccountId = "replacement-account";
        await fixture.Db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<PlatformApiException>(() => fixture.Provider.ReadAsync(fixture.Account, "playlist", default));
        Assert.Equal("spotify_account_changed", error.Code);
        Assert.Equal(reads, fixture.Handler.Reads);
        await fixture.AssertNoDestinationIngestionAsync();
    }

    [Theory]
    [InlineData("spotify", "baseline")]
    [InlineData("youtube", "baseline")]
    [InlineData("spotify", "pending")]
    [InlineData("youtube", "pending")]
    [InlineData("spotify", "progress")]
    [InlineData("youtube", "progress")]
    public async Task LegacyPlaylistRead_RequiresCoordinatorForScopedSyncState(string service, string state)
    {
        await using var scope = await Scope.CreateAsync(service, relational: true);
        if (state == "baseline") scope.Link.BaselineJson = new PlaylistSyncBaseline([]).Serialize();
        else if (state == "pending") scope.Link.PendingWriteJson = new PlaylistSyncBaseline([]).Serialize();
        else scope.Link.MatchingProgressJson = "{}";
        await scope.Db.SaveChangesAsync();
        // A null network service makes an accidental legacy provider read fail.
        var error = await Assert.ThrowsAsync<PlatformApiException>(() => service == "spotify"
            ? new SpotifyPlaylistSyncService(scope.Db, null!, null!, null!, TimeProvider.System,
                NullLogger<SpotifyPlaylistSyncService>.Instance).SyncPlaylistAsync(new(scope.UserId, 51, "account"), "playlist", default)
            : new YouTubePlaylistSyncService(scope.Db, null!, null!, null!,
                NullLogger<YouTubePlaylistSyncService>.Instance).SyncYouTubePlaylistAsync(new(scope.UserId, 51, "account"), "playlist", default));
        Assert.Equal("outbound_mapping", error.Code);
        Assert.Equal(scope.Entry.Id, (await scope.Db.PlaylistEntries.SingleAsync()).Id);
    }

    [PostgresFact]
    public async Task ProductionSpotifyRead_PostgresMissingAndConflictingRecordingEvidence()
    {
        await AssertProductionReadIsolationAsync(null, canonicalOnly: false, postgres: true);
        await AssertProductionReadIsolationAsync("USABC2400002", canonicalOnly: false, postgres: true);
    }

    // Real Spotify HTTP snapshot parsing, account checks, resolver and coordinator.
    // Keep this fixture for combined recording/artist evidence assertions in PR179.
    private sealed class ProductionSpotifyReadScope : IAsyncDisposable
    {
        private readonly HttpClient _http;
        private readonly TokenEncryptionService _encryption;
        private ProductionSpotifyReadScope(Scope scope, HttpClient http, ProductionSpotifyReadHandler handler,
            SpotifyPlaylistSyncProvider provider, PlatformAccountContext account, TokenEncryptionService encryption)
        {
            Scope = scope; _http = http; Handler = handler; Provider = provider; Account = account; _encryption = encryption;
            Writer = new ProductionSpotifyReadWriter(handler);
        }
        public Scope Scope { get; }
        public ApplicationDbContext Db => Scope.Db;
        public ProductionSpotifyReadHandler Handler { get; }
        public SpotifyPlaylistSyncProvider Provider { get; }
        public ProductionSpotifyReadWriter Writer { get; }
        public PlatformAccountContext Account { get; }
        public SpotifyPlaylistSyncProvider ProviderFor(ApplicationDbContext db) => CreateProvider(db, _http, _encryption);
        public PlaylistSyncCoordinator Coordinator() => new(Db, [Provider], [Writer], TimeProvider.System,
            NullLogger<PlaylistSyncCoordinator>.Instance);
        public async Task SyncAsync()
        {
            Db.ChangeTracker.Clear();
            await Coordinator().RunAsync(Account.UserId, Scope.Playlist.Id, "spotify", default);
        }
        public async Task ChangeMetadataAsync(string change)
        {
            var track = await Db.Tracks.SingleAsync(item => item.Id == Scope.Entry.TrackId);
            if (change == "artwork") TrackArtworkUpdater.FillMissingCanonicalThumbnail(track, "https://example.invalid/local-art.jpg");
            else if (change == "title") track.SearchTitle += " updated";
            else
            {
                var metadata = JsonSerializer.Deserialize<TrackCanonicalMetadata>(track.CanonicalMetadata!)!;
                metadata.DurationSeconds++;
                track.CanonicalMetadata = JsonSerializer.Serialize(metadata);
            }
            await Db.SaveChangesAsync();
        }
        public async Task AssertOriginalMembershipAsync(bool requireSuccess = true)
        {
            var entry = await Db.PlaylistEntries.AsNoTracking().SingleAsync(item => item.PlaylistId == Scope.Playlist.Id);
            Assert.Equal((Scope.Entry.Id, Scope.Entry.TrackId, Scope.Entry.TrackObservationId),
                (entry.Id, entry.TrackId, entry.TrackObservationId));
            if (requireSuccess) Assert.Equal("success", (await Db.ServicePlaylistMappings.SingleAsync()).LastSyncStatus);
        }
        public async Task AssertNoDestinationIngestionAsync()
        {
            Assert.False(await Db.TrackSourceIds.AnyAsync(item => item.SourceType == "spotify" && item.ExternalId == "chosen-id"));
            Assert.False(await Db.TrackObservations.AnyAsync(item => item.SourceType == "spotify"
                && item.ExternalId == "chosen-id" && item.TrackId != null));
            Assert.False(await Db.TrackArtistCredits.AnyAsync(item => item.TrackId == Scope.Entry.TrackId));
            Assert.NotEqual("USABC2400001", (await Db.Tracks.AsNoTracking().SingleAsync(item => item.Id == Scope.Entry.TrackId)).Isrc);
        }
        public static async Task<ProductionSpotifyReadScope> CreateAsync(string? isrc = null, bool canonicalOnly = false, bool postgres = false)
        {
            var scope = await Scope.CreateAsync("spotify", relational: !postgres, postgres: postgres);
            scope.Entry.Track!.Isrc = isrc;
            scope.Entry.TrackObservation!.RawMetadata = JsonSerializer.Serialize(new TrackObservationMetadata
            {
                SourceType = "youtube", ExternalId = "source-video", Title = "Everything Goes On",
                OriginalTitle = "Everything Goes On", Artist = "Porter Robinson", OriginalArtist = "Porter Robinson",
                DurationSeconds = 160
            });
            if (canonicalOnly) { scope.Entry.TrackObservationId = null; scope.Entry.TrackObservation = null; }
            scope.Link.BaselineName = scope.Playlist.Name;
            scope.Link.BaselineJson = new PlaylistSyncBaseline([]).Serialize();
            var encryption = new TokenEncryptionService(new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider());
            var account = await scope.Db.ConnectedServiceAccounts.SingleAsync();
            account.EncryptedAccessToken = encryption.Encrypt("read-token");
            account.Scopes = "playlist-read-private playlist-modify-private";
            await scope.Db.SaveChangesAsync();
            scope.Db.ChangeTracker.Clear();
            await scope.Review.ConfirmAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, scope.Entry.Id,
                scope.Token(new TrackMatchSearchCandidate { CandidateSource = "spotify", ExternalId = "chosen-id",
                    Title = "Everything Goes On", Artist = "Porter Robinson", ArtistCredits = ["Porter Robinson"],
                    DurationSeconds = 160, Isrc = "USABC2400001" }), default);
            var handler = new ProductionSpotifyReadHandler(scope.Playlist.Name);
            var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.spotify.com") };
            var provider = CreateProvider(scope.Db, http, encryption);
            var fixture = new ProductionSpotifyReadScope(scope, http, handler, provider,
                new(scope.UserId, account.Id, "account"), encryption);
            await fixture.AssertNoDestinationIngestionAsync();
            return fixture;
        }
        private static SpotifyPlaylistSyncProvider CreateProvider(ApplicationDbContext db, HttpClient http, TokenEncryptionService encryption)
        {
            var options = Options.Create(new SpotifyOptions { ClientId = "test", ClientSecret = "test" });
            var api = new SpotifyApiClient(http, options, new NoDelay());
            var tokens = new SpotifyTokenManager(db, api, encryption, TimeProvider.System, null!);
            var search = new SpotifySearchProvider(api, new SpotifyCatalogTokenProvider(api, options,
                TimeProvider.System, new SpotifyCatalogTokenCache()));
            return new SpotifyPlaylistSyncProvider(db, api, tokens,
                [search], new TrackMatchingService(db, [], NullLogger<TrackMatchingService>.Instance),
                new PlaylistCanonicalReconciliationService(db), Options.Create(new TrackMatchingOptions()));
        }
        public async ValueTask DisposeAsync() { _http.Dispose(); await Scope.DisposeAsync(); }
    }

    private sealed class ProductionSpotifyReadHandler(string name) : HttpMessageHandler
    {
        public List<string> RemoteIds { get; set; } = [];
        public int Reads { get; private set; }
        public bool FailNextRead { get; set; }
        public bool MissingNextRead { get; set; }
        public string ProfileId { get; set; } = "account";
        public Action<int>? OnRead { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (MissingNextRead && path.StartsWith("/v1/playlists/", StringComparison.Ordinal))
            {
                MissingNextRead = false;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                { Content = new StringContent("{\"error\":{\"status\":404,\"message\":\"Temporarily unavailable\"}}") });
            }
            object response;
            if (path == "/v1/me") response = new { id = ProfileId };
            else if (path == "/api/token") response = new { access_token = "catalog-token", expires_in = 3600, token_type = "Bearer" };
            else if (path == "/v1/search") response = new { tracks = new { items = Array.Empty<object>() } };
            else if (path.EndsWith("/items", StringComparison.Ordinal))
            {
                Reads++;
                OnRead?.Invoke(Reads);
                if (FailNextRead) { FailNextRead = false; throw new HttpRequestException("Interrupted snapshot read"); }
                response = new { items = RemoteIds.Select(id => new { is_local = false, item = Track(id) }),
                    total = RemoteIds.Count, next = (string?)null };
            }
            else if (path.StartsWith("/v1/playlists/", StringComparison.Ordinal))
                response = new { id = path.Split('/').Last(), name, snapshot_id = string.Join(',', RemoteIds) + "-snapshot",
                    owner = new { id = "account" }, @public = false };
            else throw new InvalidOperationException($"Unexpected request {request.Method} {request.RequestUri}");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(JsonSerializer.Serialize(response), Encoding.UTF8, "application/json") });
        }
        private static object Track(string id) => new
        {
            id, type = "track", name = id == "chosen-id" ? "Everything Goes On" : "New remote song", duration_ms = 160000,
            artists = new[] { new { id = id == "chosen-id" ? "artist-one" : "new-artist", name = id == "chosen-id" ? "Porter Robinson" : "New artist" } },
            external_ids = new { isrc = id == "chosen-id" ? "USABC2400001" : "USNEW2400001" },
            album = new { name = "Provider album", images = new[] { new { url = "https://example.invalid/provider-art.jpg" } } }
        };
    }

    private sealed class ProductionSpotifyReadWriter(ProductionSpotifyReadHandler handler) : IPlaylistWriter
    {
        public string PlatformId => "spotify";
        public bool ThrowAfterWrite { get; set; }
        public bool FailVerificationRead { get; set; }
        public string CreatedPlaylistId { get; set; } = "playlist";
        public List<string[]> Writes { get; } = [];
        public Task ValidateCreationAsync(PlatformAccountContext account, CancellationToken ct) => Task.CompletedTask;
        public Task<string> CreatePlaylistAsync(PlatformAccountContext account, string name, CancellationToken ct) => Task.FromResult(CreatedPlaylistId);
        public Task ValidateDestinationAsync(PlatformAccountContext account, string id, CancellationToken ct) => Task.CompletedTask;
        public Task ReconcileAsync(PlatformAccountContext account, string id, IReadOnlyList<string> ids, CancellationToken ct)
        {
            Writes.Add(ids.ToArray());
            handler.RemoteIds = ids.ToList();
            handler.FailNextRead = FailVerificationRead;
            FailVerificationRead = false;
            if (ThrowAfterWrite) { ThrowAfterWrite = false; throw new PlatformApiException("provider_error", "Acknowledgement lost", 503); }
            return Task.CompletedTask;
        }
    }
}
