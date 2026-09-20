using System.Net;
using System.Text;
using Cantaro.Api.Configuration;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class SimklMediaProviderTests
{
    [Fact]
    public async Task AccountActivityDoesNotChangeAnUnrelatedItemsRemoteTimestamp()
    {
        await using var fixture = await Fixture.CreateAsync(request => request.RequestUri!.AbsolutePath switch
        {
            "/sync/activities" => Json("""{"all":"2026-09-21T00:00:00Z","tv_shows":{},"movies":{},"anime":{}}"""),
            "/sync/all-items/shows" or "/sync/all-items/anime" => Json("{}"),
            "/sync/all-items/movies" => Json("""
                {"movies":[{"status":"completed","last_watched_at":"2020-01-01T00:00:00Z",
                  "user_rated_at":"2021-01-01T00:00:00Z","movie":{"title":"Old film","ids":{"simkl":456}}}]}
                """),
            _ => throw new InvalidOperationException(request.RequestUri!.AbsolutePath)
        });
        var imported = await fixture.Provider.ImportLibraryAsync(1, CancellationToken.None);
        var movie = Assert.Single(imported.Items);
        Assert.Equal(DateTimeOffset.Parse("2021-01-01T00:00:00Z"), movie.LastRemoteUpdateAt);
    }

    [Fact]
    public async Task UnversionedSnapshotIsRebuiltEvenWhenActivityCursorIsUnchanged()
    {
        await using var fixture = await Fixture.CreateAsync(request => request.RequestUri!.AbsolutePath switch
        {
            "/sync/activities" => Json("""{"all":"2026-09-21T00:00:00Z","tv_shows":{},"movies":{},"anime":{}}"""),
            "/sync/all-items/shows" or "/sync/all-items/anime" => Json("{}"),
            "/sync/all-items/movies" => Json("""
                {"movies":[{"status":"completed","last_watched_at":"2020-01-01T00:00:00Z",
                  "movie":{"title":"Old film","ids":{"simkl":456}}}]}
                """),
            _ => throw new InvalidOperationException(request.RequestUri!.AbsolutePath)
        });
        var account = await fixture.Db.ConnectedServiceAccounts.SingleAsync();
        var oldSnapshot = new MediaProviderLibrarySnapshot
        {
            ConnectedServiceAccountId = account.Id,
            Cursor = """{"all":"2026-09-21T00:00:00Z","removed":{}}""",
            ItemsJson = """{"movie:456":{"providerMediaId":"movie:456","title":"Old film","mediaKind":"movie","status":"completed","primaryProgressDimension":"completion_only","releaseStatusDimension":"completion_only","lastRemoteUpdateAt":"2026-09-21T00:00:00Z"}}"""
        };
        fixture.Db.MediaProviderLibrarySnapshots.Add(oldSnapshot);
        await fixture.Db.SaveChangesAsync();

        var imported = await fixture.Provider.ImportLibraryAsync(1, CancellationToken.None);

        Assert.Equal(DateTimeOffset.Parse("2020-01-01T00:00:00Z"), Assert.Single(imported.Items).LastRemoteUpdateAt);
        Assert.Contains("/sync/all-items/movies", fixture.Handler.Paths);
        Assert.DoesNotContain("/sync/all-items", fixture.Handler.Paths);
        Assert.Same(oldSnapshot, await fixture.Db.MediaProviderLibrarySnapshots.SingleAsync());
        Assert.Contains("\"formatVersion\":1", oldSnapshot.Cursor);
        var callsAfterRebuild = fixture.Handler.Paths.Count;
        await fixture.Provider.ImportLibraryAsync(1, CancellationToken.None);
        Assert.Equal(callsAfterRebuild + 1, fixture.Handler.Paths.Count);
        Assert.Equal("/sync/activities", fixture.Handler.Paths.Last());
    }

    [Fact]
    public async Task ImportUsesActivitiesAndMergesDeltaWithoutLosingOtherTitles()
    {
        var revision = 1;
        await using var fixture = await Fixture.CreateAsync(request => request.RequestUri!.AbsolutePath switch
        {
            "/sync/activities" => Json("{\"all\":\"2026-09-0" + revision + "T00:00:00Z\",\"tv_shows\":{\"removed_from_list\":\"2026-09-0" + (revision >= 3 ? 3 : 1) + "T00:00:00Z\"},\"movies\":{},\"anime\":{}}"),
            "/sync/all-items/shows" => Json("""
                {"shows":[{"status":"watching","watched_episodes_count":2,"show":{"title":"Show","ids":{"simkl":123}},
                  "seasons":[{"number":1,"episodes":[{"number":1},{"number":3}]}]}]}
                """),
            "/sync/all-items/movies" => Json("""
                {"movies":[{"status":"completed","movie":{"title":"Film","ids":{"simkl":456}}}]}
                """),
            "/sync/all-items/anime" => Json("{}"),
            "/tv/episodes/123" => Json("""
                [{"season":1,"episode":1},{"season":1,"episode":2},{"season":1,"episode":3}]
                """),
            "/sync/all-items" when request.RequestUri.Query.Contains("simkl_ids_only") => Json("""
                {"movies":[{"ids":{"simkl":456}}]}
                """),
            "/sync/all-items" when revision >= 3 => Json("{}"),
            "/sync/all-items" => Json("""
                {"shows":[{"status":"completed","watched_episodes_count":3,"show":{"title":"Show","ids":{"simkl":123}},
                  "seasons":[{"number":1,"episodes":[{"number":1},{"number":2},{"number":3}]}]}]}
                """),
            _ => throw new InvalidOperationException(request.RequestUri!.AbsolutePath)
        });

        var initial = await fixture.Provider.ImportLibraryAsync(1, CancellationToken.None);
        Assert.Equal(2, initial.Items.Count);
        var show = Assert.Single(initial.Items, x => x.ProviderMediaId == "tv:123");
        Assert.Equal(1, show.ProgressEpisodes);
        Assert.True(show.HasNonContiguousProgress);
        var episodeRequestIndex = fixture.Handler.Paths.IndexOf("/tv/episodes/123");
        Assert.True(episodeRequestIndex >= 0);
        Assert.Equal("Bearer access-token", fixture.Handler.Authorizations[episodeRequestIndex]);
        var callsAfterInitial = fixture.Handler.Paths.Count;
        await fixture.Provider.ImportLibraryAsync(1, CancellationToken.None);
        Assert.Equal(callsAfterInitial + 1, fixture.Handler.Paths.Count);
        Assert.Equal("/sync/activities", fixture.Handler.Paths.Last());

        revision = 2;
        var updated = await fixture.Provider.ImportLibraryAsync(1, CancellationToken.None);
        Assert.Equal(2, updated.Items.Count);
        Assert.Contains(updated.Items, x => x.ProviderMediaId == "movie:456");
        show = Assert.Single(updated.Items, x => x.ProviderMediaId == "tv:123");
        Assert.Equal(3, show.ProgressEpisodes);
        Assert.False(show.HasNonContiguousProgress);
        Assert.Contains(fixture.Handler.Uris, x => x.AbsolutePath == "/sync/all-items" && x.Query.Contains("date_from="));

        revision = 3;
        var afterRemoval = await fixture.Provider.ImportLibraryAsync(1, CancellationToken.None);
        Assert.Single(afterRemoval.Items);
        Assert.Equal("movie:456", afterRemoval.Items[0].ProviderMediaId);
        Assert.Contains(fixture.Handler.Uris, x => x.AbsolutePath == "/sync/all-items" && x.Query.Contains("simkl_ids_only"));
    }

    [Fact]
    public async Task ProgressAddsNextEpisodeWithoutRemovingWatchedEpisodeAhead()
    {
        await using var fixture = await Fixture.CreateAsync(request => request.RequestUri!.AbsolutePath switch
        {
            "/sync/activities" => Json("""
                {"all":"2026-09-01T00:00:00Z","tv_shows":{},"movies":{},"anime":{}}
                """),
            "/sync/all-items/shows" => Json("""
                {"shows":[{"status":"watching","watched_episodes_count":2,"show":{"title":"Show","ids":{"simkl":123}},
                  "seasons":[{"number":1,"episodes":[{"number":1},{"number":3}]}]}]}
                """),
            "/sync/all-items/movies" or "/sync/all-items/anime" => Json("{}"),
            "/tv/episodes/123" => Json("""
                [{"season":1,"episode":1},{"season":1,"episode":2},{"season":1,"episode":3}]
                """),
            "/sync/history" => Json("""
                {"added":{"episodes":1,"statuses":[{"response":{"status":"watching"}}]},"not_found":{"shows":[],"episodes":[]}}
                """),
            _ => throw new InvalidOperationException(request.RequestUri!.AbsolutePath)
        });
        var result = await fixture.Provider.UpdateProgressAsync(1,
            new MediaProgressUpdateRequest { ProviderMediaId = "tv:123", ProgressEpisodes = 2 }, CancellationToken.None);
        Assert.Equal(2, result.AppliedProgressEpisodes);
        Assert.Equal(MediaLibraryStatuses.Current, result.AppliedStatus);
        Assert.DoesNotContain("/sync/history/remove", fixture.Handler.Paths);
        var body = Assert.Single(fixture.Handler.Bodies, x => x.Contains("\"shows\""));
        Assert.Contains("\"number\":2", body);
        Assert.DoesNotContain("\"number\":3", body);
    }

    [Fact]
    public async Task TitleDetailsUsesConnectedBearerToken()
    {
        await using var fixture = await Fixture.CreateAsync(request => request.RequestUri!.AbsolutePath == "/tv/123"
            ? Json("""{"title":"Show","ids":{"simkl":123},"total_episodes":12}""")
            : throw new InvalidOperationException(request.RequestUri!.AbsolutePath));
        var details = await fixture.Provider.GetTitleDetailsAsync(1, "tv:123", CancellationToken.None);
        Assert.Equal("Show", details?.Title);
        Assert.Equal("Bearer access-token", Assert.Single(fixture.Handler.Authorizations));
    }

    [Fact]
    public async Task CompletedWriteRecordsProviderResolvedWatchingStatus()
    {
        await using var fixture = await Fixture.CreateAsync(request => request.RequestUri!.AbsolutePath == "/sync/add-to-list"
            ? Json("""
                {"added":{"shows":[{"to":"watching","ids":{"simkl":123}}]},"not_found":{"shows":[]}}
                """) : throw new InvalidOperationException(request.RequestUri!.AbsolutePath));
        var result = await fixture.Provider.UpdateStatusAsync(1,
            new MediaStatusUpdateRequest { ProviderMediaId = "tv:123", Status = MediaLibraryStatuses.Completed }, CancellationToken.None);
        Assert.Equal(MediaLibraryStatuses.Current, result.AppliedStatus);
        Assert.Contains("\"to\":\"completed\"", Assert.Single(fixture.Handler.Bodies));
    }

    [Fact]
    public async Task WholeStateSyncDoesNotOverwriteHistoryResolvedWatchingStatus()
    {
        await using var fixture = await Fixture.CreateAsync(request => request.RequestUri!.AbsolutePath switch
        {
            "/sync/activities" => Json("""{"all":"2026-09-01T00:00:00Z","tv_shows":{},"movies":{},"anime":{}}"""),
            "/sync/all-items/shows" => Json("""
                {"shows":[{"status":"watching","watched_episodes_count":1,"show":{"title":"Show","ids":{"simkl":123}},
                  "seasons":[{"number":1,"episodes":[{"number":1}]}]}]}
                """),
            "/sync/all-items/movies" or "/sync/all-items/anime" => Json("{}"),
            "/tv/episodes/123" => Json("""[{"season":1,"episode":1},{"season":1,"episode":2}]"""),
            "/sync/history" => Json("""
                {"added":{"episodes":1,"statuses":[{"response":{"status":"watching"}}]},"not_found":{"shows":[],"episodes":[]}}
                """),
            "/sync/ratings/remove" => Json("""{"not_found":{"shows":[]}}"""),
            "/sync/add-to-list" => throw new InvalidOperationException("History status must not be overwritten."),
            _ => throw new InvalidOperationException(request.RequestUri!.AbsolutePath)
        });
        var result = await fixture.Provider.SyncLibraryStateAsync(1, new MediaLibraryStateSyncRequest
        {
            ProviderMediaId = "tv:123", Status = MediaLibraryStatuses.Completed, ProgressEpisodes = 2
        }, CancellationToken.None);
        Assert.Equal(MediaLibraryStatuses.Current, result.AppliedStatus);
        Assert.DoesNotContain("/sync/add-to-list", fixture.Handler.Paths);
    }

    [Fact]
    public async Task ScoreOnlySyncDoesNotRewriteStatusOrHistory()
    {
        await using var fixture = await Fixture.CreateAsync(request => request.RequestUri!.AbsolutePath == "/sync/ratings/remove"
            ? Json("""{"not_found":{"shows":[]}}""")
            : throw new InvalidOperationException(request.RequestUri!.AbsolutePath));
        await fixture.Provider.SyncLibraryStateAsync(1, new MediaLibraryStateSyncRequest
        {
            ProviderMediaId = "tv:123", Status = MediaLibraryStatuses.Completed,
            UpdateStatus = false, UpdateProgress = false, Score = null
        }, CancellationToken.None);
        Assert.Equal(["/sync/ratings/remove"], fixture.Handler.Paths);
    }

    [Fact]
    public async Task UnauthorizedReadRefreshesAndRetriesOnce()
    {
        var activities = 0;
        await using var fixture = await Fixture.CreateAsync(request => request.RequestUri!.AbsolutePath switch
        {
            "/sync/activities" when activities++ == 0 => new HttpResponseMessage(HttpStatusCode.Unauthorized),
            "/sync/activities" => Json("""{"all":"2026-09-01T00:00:00Z","tv_shows":{},"movies":{},"anime":{}}"""),
            "/oauth2/token" => Json("""
                {"access_token":"new-token","refresh_token":"refresh-token","expires_in":604800,"scope":"media:read media:write"}
                """),
            "/sync/all-items/shows" or "/sync/all-items/movies" or "/sync/all-items/anime" => Json("{}"),
            _ => throw new InvalidOperationException(request.RequestUri!.AbsolutePath)
        });
        await fixture.Provider.ImportLibraryAsync(1, CancellationToken.None);
        Assert.Equal(2, activities);
        Assert.Contains("Bearer new-token", fixture.Handler.Authorizations);
        Assert.Equal(1, fixture.Handler.Paths.Count(x => x == "/oauth2/token"));
    }

    [Fact]
    public async Task ConnectingDifferentAccountDetachesOldBindingsAndCancelsQueuedWrites()
    {
        await using var fixture = await Fixture.CreateAsync(request => request.RequestUri!.AbsolutePath switch
        {
            "/oauth2/token" => Json("""
                {"access_token":"new-token","refresh_token":"new-refresh","expires_in":604800,"scope":"media:read media:write"}
                """),
            "/users/settings" => Json("""{"user":{"name":"new user"},"account":{"id":2}}"""),
            "/oauth2/revoke" => Json("{}"),
            _ => throw new InvalidOperationException(request.RequestUri!.AbsolutePath)
        });
        var account = await fixture.Db.ConnectedServiceAccounts.SingleAsync();
        var title = new MediaTitle { Id = Guid.NewGuid(), CanonicalTitle = "Show", MediaKind = MediaKinds.Series,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode };
        var entry = new MediaLibraryEntry { Id = Guid.NewGuid(), UserId = 1, MediaTitleId = title.Id,
            Status = MediaLibraryStatuses.Current };
        var link = new MediaProviderLink { Id = Guid.NewGuid(), MediaTitleId = title.Id, Provider = "simkl",
            ExternalId = "tv:123", LinkSource = MediaMappingSources.Imported };
        var binding = new MediaLibraryProviderBinding { Id = Guid.NewGuid(), MediaLibraryEntryId = entry.Id,
            MediaProviderLinkId = link.Id, ConnectedServiceAccountId = account.Id, ProviderAccountId = "1" };
        fixture.Db.MediaTitles.Add(title);
        fixture.Db.MediaLibraryEntries.Add(entry);
        fixture.Db.MediaProviderLinks.Add(link);
        fixture.Db.MediaLibraryProviderBindings.Add(binding);
        fixture.Db.MediaProviderOperations.Add(new MediaProviderOperation { Id = Guid.NewGuid(),
            MediaLibraryProviderBindingId = binding.Id, OperationType = MediaProviderOperationTypes.UpdateStatus,
            PayloadJson = "{}", Status = MediaProviderOperationStatuses.Pending });
        fixture.Db.MediaProviderLibrarySnapshots.Add(new MediaProviderLibrarySnapshot
        { ConnectedServiceAccountId = account.Id, Cursor = "old", ItemsJson = "{}" });
        await fixture.Db.SaveChangesAsync();

        var connected = await fixture.Provider.ExchangeCodeAndSaveAsync(1, "code", "https://cantaro.test/callback", "verifier", CancellationToken.None);
        Assert.Equal("2", connected.ExternalAccountId);
        Assert.Null((await fixture.Db.MediaLibraryProviderBindings.SingleAsync()).ConnectedServiceAccountId);
        Assert.Empty(await fixture.Db.MediaProviderOperations.ToListAsync());
        Assert.Empty(await fixture.Db.MediaProviderLibrarySnapshots.ToListAsync());
        Assert.Contains("/oauth2/revoke", fixture.Handler.Paths);
    }

    [Fact]
    public async Task RejectedRefreshMarksAccountForReconnect()
    {
        await using var fixture = await Fixture.CreateAsync(request => request.RequestUri!.AbsolutePath == "/oauth2/token"
            ? new HttpResponseMessage(HttpStatusCode.BadRequest)
            : throw new InvalidOperationException(request.RequestUri!.AbsolutePath));
        var account = await fixture.Db.ConnectedServiceAccounts.SingleAsync();
        account.TokenExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await fixture.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Provider.ImportLibraryAsync(1, CancellationToken.None));
        Assert.Equal("reconnect_required", account.ConnectionState);
        Assert.Equal("token_refresh_rejected", account.ReconnectReason);
    }

    [Fact]
    public async Task ReadOnlyGrantCannotConnect()
    {
        await using var fixture = await Fixture.CreateAsync(request => request.RequestUri!.AbsolutePath == "/oauth2/token"
            ? Json("""{"access_token":"read-token","refresh_token":"refresh-token","expires_in":604800,"scope":"media:read"}""")
            : throw new InvalidOperationException(request.RequestUri!.AbsolutePath));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Provider.ExchangeCodeAndSaveAsync(
            1, "code", "https://cantaro.test/callback", "verifier", CancellationToken.None));
        Assert.Contains("media:write", error.Message);
        Assert.DoesNotContain("/users/settings", fixture.Handler.Paths);
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly string _directory;
        public ApplicationDbContext Db { get; }
        public SimklMediaProvider Provider { get; }
        public Handler Handler { get; }

        private Fixture(SqliteConnection connection, string directory, ApplicationDbContext db, SimklMediaProvider provider, Handler handler)
        { _connection = connection; _directory = directory; Db = db; Provider = provider; Handler = handler; }

        public static async Task<Fixture> CreateAsync(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var encryption = new TokenEncryptionService(DataProtectionProvider.Create(new DirectoryInfo(directory)));
            db.Users.Add(TestUserFactory.Create(1, "simkl-1@example.com"));
            db.ConnectedServiceAccounts.Add(new ConnectedServiceAccount
            {
                UserId = 1, Service = "simkl", ExternalAccountId = "1", DisplayName = "Simkl user",
                EncryptedAccessToken = encryption.Encrypt("access-token"),
                EncryptedRefreshToken = encryption.Encrypt("refresh-token"),
                TokenExpiresAt = DateTime.UtcNow.AddHours(1), RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(90),
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            var handler = new Handler(respond);
            var api = new SimklApiClient(new HttpClient(handler), Options.Create(new SimklOptions { ClientId = "client" }));
            return new Fixture(connection, directory, db, new SimklMediaProvider(db, api, encryption, new SimklTokenRefreshGate(), new SimklImportGate()), handler);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public List<Uri> Uris { get; } = [];
        public List<string> Bodies { get; } = [];
        public List<string?> Authorizations { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            Uris.Add(request.RequestUri);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            Authorizations.Add(request.Headers.Authorization?.ToString());
            return respond(request);
        }
    }
}
