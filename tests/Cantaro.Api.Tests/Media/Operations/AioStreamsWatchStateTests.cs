using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Cantaro.Api.Configuration;
using Cantaro.Api.Controllers;
using Cantaro.Api.Data;
using Cantaro.Api.Migrations;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class AioStreamsWatchStateTests
{
    private const string MetaId = "tt0213338";
    private const string VideoId = "kitsu:1:1";
    private const string TvMetaId = "tt1000002";
    private const string Route = AioStreamsWatchState.Route;
    private const string PushRoute = $"{Route}/watch_state/push/anime/kitsu%3A1%3A1.json";
    private const string PullRoute = $"{Route}/watch_state/pull.json";
    private const long At = 1_794_000_000;

    [PostgresTheory]
    [InlineData("played", null)]
    [InlineData("stop", true)]
    public async Task DeliversResumeAndPlayedStateAndDeduplicatesConcurrentRetries(string completion, bool? played)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await using var fixture = await Fixture.StartAsync(schema.Db);
        using var previewRequest = new HttpRequestMessage(HttpMethod.Get, fixture.Authorize($"{Route}/manifest.json"));
        previewRequest.Headers.Add("Origin", "https://aiostreams.example.test");
        using var manifestResponse = await fixture.Client.SendAsync(previewRequest);
        manifestResponse.EnsureSuccessStatusCode();
        Assert.Equal("*", Assert.Single(manifestResponse.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.False(manifestResponse.Headers.Contains("Access-Control-Allow-Credentials"));
        Assert.True(manifestResponse.Headers.CacheControl?.NoStore);
        var manifest = await manifestResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, manifest.GetProperty("watchState").GetProperty("version").GetInt32());
        Assert.False(manifest.GetProperty("watchState").TryGetProperty("viewers", out _));
        Assert.Equal(new[] { "anime", "series", "movie" }, manifest.GetProperty("types").Deserialize<string[]>());

        var initial = await fixture.PullAsync();
        var initialVersion = initial.GetProperty("version").GetString();
        Assert.Empty(initial.GetProperty("items").EnumerateArray());
        Assert.Empty(initial.GetProperty("watched").GetProperty("episodes").EnumerateArray());

        var pause = Event("pause", "pause", At, position: 1_250, duration: null);
        await fixture.ExpectPushAsync(pause, HttpStatusCode.NoContent);
        var paused = await fixture.PullAsync(initialVersion);
        Assert.False(paused.TryGetProperty("watched", out _));
        var pauseItem = Assert.Single(paused.GetProperty("items").EnumerateArray());
        Assert.Equal(1_250, pauseItem.GetProperty("positionMs").GetInt64());
        Assert.False(pauseItem.TryGetProperty("durationMs", out _));

        var stop = Event("stop", "stop", At + 2, position: 2_500, duration: 9_000, played: false);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => fixture.ExpectPushAsync(stop, HttpStatusCode.NoContent)));
        await fixture.ExpectPushAsync(stop with { MetaId = "tt9999999" }, HttpStatusCode.NotFound);
        await fixture.ExpectPushAsync(Event("late", "pause", At + 1, position: 1_500), HttpStatusCode.NoContent);
        var resume = await fixture.PullAsync();
        var item = Assert.Single(resume.GetProperty("items").EnumerateArray());
        Assert.Equal(MetaId, item.GetProperty("metaId").GetString());
        Assert.Equal(VideoId, item.GetProperty("videoId").GetString());
        Assert.Equal("anime", item.GetProperty("type").GetString());
        Assert.Equal(1, item.GetProperty("season").GetInt32());
        Assert.Equal(1, item.GetProperty("episode").GetInt32());
        Assert.Equal(2_500, item.GetProperty("positionMs").GetInt64());
        Assert.Equal(9_000, item.GetProperty("durationMs").GetInt64());
        Assert.Null(await schema.Db.MediaLibraryEntries.Select(entry => entry.ProgressEpisodes).SingleAsync());
        Assert.Equal(3, await schema.Db.AioStreamsWatchEvents.CountAsync());

        var mark = Event("finish", completion, At + 3, played: played);
        await fixture.ExpectPushAsync(mark, HttpStatusCode.NoContent);
        var updatedAt = await schema.Db.MediaLibraryEntries.Select(entry => entry.LastLocalEditAt).SingleAsync();
        await fixture.ExpectPushAsync(mark, HttpStatusCode.NoContent);
        await fixture.ExpectPushAsync(stop, HttpStatusCode.NoContent);
        Assert.Equal(4, await schema.Db.AioStreamsWatchEvents.CountAsync());
        Assert.Equal(1, await schema.Db.MediaLibraryEntries.Select(entry => entry.ProgressEpisodes).SingleAsync());
        Assert.Equal(updatedAt, await schema.Db.MediaLibraryEntries.Select(entry => entry.LastLocalEditAt).SingleAsync());
        var completed = await fixture.PullAsync(initialVersion);
        Assert.Empty(completed.GetProperty("items").EnumerateArray());
        var watched = Assert.Single(completed.GetProperty("watched").GetProperty("episodes").EnumerateArray());
        Assert.Equal(VideoId, watched.GetString());
        Assert.NotEqual(initialVersion, completed.GetProperty("version").GetString());

        await fixture.ExpectPushAsync(Event("rewatch", "pause", At + 4, 1_800), HttpStatusCode.NoContent);
        var rewatch = await fixture.PullAsync();
        Assert.Equal(1_800, Assert.Single(rewatch.GetProperty("items").EnumerateArray()).GetProperty("positionMs").GetInt64());
        Assert.Equal(VideoId, Assert.Single(rewatch.GetProperty("watched").GetProperty("episodes").EnumerateArray()).GetString());
        await fixture.ExpectPushAsync(Event("rewatch-finish", "stop", At + 5, played: true), HttpStatusCode.NoContent);
        Assert.Empty((await fixture.PullAsync()).GetProperty("items").EnumerateArray());
    }

    [Theory]
    [InlineData("https://aio.test/hosted/private/manifest.json")]
    [InlineData("https://aio.test/hosted/private/%6danifest%2ejson")]
    [InlineData("https://aio.test/hosted/stremio/852a2fb0-764b-43ef-870e-054724c98f08")]
    [InlineData("https://aio.test/hosted/stremio/u/private")]
    [InlineData("https://aio.test/hosted/%73tremio%2fu%2fprivate/configure")]
    [InlineData("https://aio.test/hosted/chilllink/852a2fb0-764b-43ef-870e-054724c98f08/example/v/1")]
    [InlineData("https://aio.test/hosted/%73eanime%2f852a2fb0-764b-43ef-870e-054724c98f08/example/extensions/aiostreams-plugin.json")]
    [InlineData("https://aio.test/hosted/seanime/extensions/encoded-configuration/stremio-custom-source.json")]
    public void PlaybackInstanceRejectsPrivateInstallUrls(string url)
    {
        Assert.Null(AioStreamsPairingController.NormalizePlaybackInstance(url));
    }

    [Theory]
    [InlineData("https://aio.test:8452/deployment/", "https://aio.test:8452/deployment")]
    [InlineData("https://aio.test/hosted/stremio/custom/", "https://aio.test/hosted/stremio/custom")]
    public void PlaybackInstanceAcceptsDeploymentSubpaths(string url, string normalized)
    {
        Assert.Equal(normalized, AioStreamsPairingController.NormalizePlaybackInstance(url));
    }

    [PostgresFact]
    public async Task SignedInUsersManageTheirOwnTrackerCredentials()
    {
        const string pairingPath = "/api/media/aiostreams-pairing";
        await using var schema = await PostgresTestSchema.CreateAsync();
        await using var fixture = await Fixture.StartAsync(schema.Db);
        schema.Db.Users.Add(new User { Id = 2, UserName = "other-tracker-owner" });
        await schema.Db.SaveChangesAsync();

        async Task<HttpResponseMessage> ManageAsync(HttpMethod method, int account, string? instanceUrl = null)
        {
            using var request = new HttpRequestMessage(method, instanceUrl is null ? pairingPath : pairingPath + "/playback");
            request.Headers.Add("X-Fixture-User", account.ToString());
            if (instanceUrl is not null) request.Content = JsonContent.Create(new { instanceUrl });
            return await fixture.Client.SendAsync(request);
        }

        async Task<string> PairAsync(int account)
        {
            using var response = await ManageAsync(HttpMethod.Put, account);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("manifestPath").GetString()!;
        }

        using var unauthorized = await fixture.Client.PutAsync(pairingPath, null);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        var first = await PairAsync(1);
        var second = await PairAsync(2);
        const string privateInstallUrl = "https://aio.test/chilllink/852a2fb0-764b-43ef-870e-054724c98f08/example";
        await schema.Db.Set<AioStreamsPairing>().Where(p => p.UserId == 1)
            .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.PlaybackInstanceUrl, privateInstallUrl));
        using var legacyStatus = await ManageAsync(HttpMethod.Get, 1);
        var legacy = await legacyStatus.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(legacy.GetProperty("paired").GetBoolean());
        Assert.Equal(JsonValueKind.Null, legacy.GetProperty("playbackInstanceUrl").ValueKind);
        using var privateInstall = await ManageAsync(HttpMethod.Put, 1, privateInstallUrl);
        Assert.Equal(HttpStatusCode.BadRequest, privateInstall.StatusCode);
        foreach (var url in new[] { "http://aio.test", "https://name:password@aio.test", "https://aio.test/?token=example", "https://aio.test/jellyfin/account/configuration" })
        {
            using var invalid = await ManageAsync(HttpMethod.Put, 1, url);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        using var configured = await ManageAsync(HttpMethod.Put, 1, "https://aio.test:8452/deployment/");
        Assert.Equal(HttpStatusCode.OK, configured.StatusCode);
        using var other = await ManageAsync(HttpMethod.Get, 2);
        Assert.Equal(JsonValueKind.Null, (await other.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("playbackInstanceUrl").ValueKind);
        var firstPush = first.Replace("/manifest.json", "/watch_state/push/anime/kitsu%3A1%3A1.json");
        using var push = await fixture.Client.PostAsJsonAsync(firstPush, Event("paired", "stop", At, 1_000));
        Assert.Equal(HttpStatusCode.NoContent, push.StatusCode);
        var secondPull = await fixture.Client.GetFromJsonAsync<JsonElement>(second.Replace("/manifest.json", "/watch_state/pull.json"));
        Assert.Empty(secondPull.GetProperty("items").EnumerateArray());

        var rotated = await PairAsync(1);
        using var retained = await ManageAsync(HttpMethod.Get, 1);
        Assert.Equal("https://aio.test:8452/deployment", (await retained.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("playbackInstanceUrl").GetString());
        using var replaced = await fixture.Client.GetAsync(first);
        Assert.Equal(HttpStatusCode.Unauthorized, replaced.StatusCode);
        using var active = await fixture.Client.GetAsync(rotated);
        Assert.Equal(HttpStatusCode.OK, active.StatusCode);
        using var disconnected = await ManageAsync(HttpMethod.Delete, 1);
        Assert.Equal(HttpStatusCode.NoContent, disconnected.StatusCode);
        using var removed = await ManageAsync(HttpMethod.Get, 1);
        Assert.Equal(JsonValueKind.Null, (await removed.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("playbackInstanceUrl").ValueKind);
        using var revoked = await fixture.Client.GetAsync(rotated);
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        using var stillActive = await fixture.Client.GetAsync(second);
        Assert.Equal(HttpStatusCode.OK, stillActive.StatusCode);
    }

    [PostgresFact]
    public async Task RefusesAPlaybackWriteWhenThePairingIsRevokedBeforeTheTransaction()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        var revocation = new RevokeBeforePairingLock(async ct =>
            await schema.Db.Set<AioStreamsPairing>().ExecuteDeleteAsync(ct));
        await using var fixture = await Fixture.StartAsync(schema.Db, interceptor: revocation);
        await fixture.ExpectPushAsync(Event("revoked", "played", At), HttpStatusCode.Unauthorized);
        Assert.Empty(await schema.Db.AioStreamsWatchEvents.ToListAsync());
        Assert.Null(await schema.Db.MediaLibraryEntries.Select(e => e.ProgressEpisodes).SingleAsync());
    }

    [PostgresFact]
    public async Task TracksImdbMoviesAndTvEpisodesThroughVerifiedLibraryLinks()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await using var fixture = await Fixture.StartAsync(schema.Db);
        var movie = new MediaTitle
        {
            Id = Guid.NewGuid(), CanonicalTitle = "Fixture movie", MediaKind = MediaKinds.Movie,
            PrimaryProgressDimension = MediaProgressDimensions.CompletionOnly,
            ReleaseStatusDimension = MediaProgressDimensions.CompletionOnly
        };
        var series = new MediaTitle
        {
            Id = Guid.NewGuid(), CanonicalTitle = "Fixture series", MediaKind = MediaKinds.Series,
            PrimaryProgressDimension = MediaProgressDimensions.Episode, ReleaseStatusDimension = MediaProgressDimensions.Episode
        };
        AddLink(schema.Db, movie, "imdb", "tt1000001");
        AddLink(schema.Db, series, "imdb", "tt1000002");
        AddLink(schema.Db, series, "simkl", "tv:1000002");
        foreach (var title in new[] { movie, series })
            schema.Db.Add(new MediaLibraryEntry
            {
                Id = Guid.NewGuid(), UserId = 1, MediaTitle = title, Status = MediaLibraryStatuses.Current,
                ProgressEpisodes = title == series ? 3 : null
            });
        await schema.Db.SaveChangesAsync();
        var mappings = new MediaProviderSeasonMappingService(schema.Db, NullLogger<MediaProviderSeasonMappingService>.Instance);
        var catalog = new MediaEpisodeIdentityService(schema.Db, mappings, NullLogger<MediaEpisodeIdentityService>.Instance);
        await catalog.CacheProviderEpisodeCatalogAsync(series.Id, "simkl", "tv:1000002",
            [
                new() { SeasonNumber = 1, EpisodeNumber = 1 },
                new() { SeasonNumber = 2, EpisodeNumber = 1 },
                new() { SeasonNumber = 2, EpisodeNumber = 2 },
                new() { SeasonNumber = 2, EpisodeNumber = 3 }
            ], [], expectedEpisodeCount: 4, CancellationToken.None);
        await schema.Db.SaveChangesAsync();

        const string moviePath = Route + "/watch_state/push/movie/tt1000001.json";
        const string seriesPath = Route + "/watch_state/push/series/tt1000002%3A2%3A3.json";
        var movieStop = Event("imdb-movie", "stop", At, 2_000) with
            { MetaId = "tt1000001", VideoId = "tt1000001", Scope = "movie", Season = null, Episode = null };
        var tvStop = Event("imdb-tv", "stop", At, 3_000) with
            { MetaId = "tt1000002", VideoId = "tt1000002:2:3", Season = 2, Episode = 3 };
        await fixture.ExpectPushAsync(tvStop with { Episode = 4 }, HttpStatusCode.NotFound, seriesPath);
        await fixture.ExpectPushAsync(tvStop with { VideoId = "tt1000002:2:4", Episode = 4 }, HttpStatusCode.NotFound,
            Route + "/watch_state/push/series/tt1000002%3A2%3A4.json");
        await fixture.ExpectPushAsync(movieStop, HttpStatusCode.NoContent, moviePath);
        await fixture.ExpectPushAsync(tvStop, HttpStatusCode.NoContent, seriesPath);
        Assert.Equal(2, (await fixture.PullAsync()).GetProperty("items").GetArrayLength());

        await fixture.ExpectPushAsync(movieStop with { Id = "movie-played", Event = "played", At = At + 1 }, HttpStatusCode.NoContent, moviePath);
        await fixture.ExpectPushAsync(tvStop with { Id = "tv-played", Event = "played", At = At + 1 }, HttpStatusCode.NoContent, seriesPath);
        await fixture.ExpectPushAsync(movieStop, HttpStatusCode.NoContent, moviePath);
        Assert.Equal(4, await schema.Db.AioStreamsWatchEvents.CountAsync());
        var completed = await fixture.PullAsync();
        Assert.Empty(completed.GetProperty("items").EnumerateArray());
        Assert.Equal(new[] { "tt1000001" }, completed.GetProperty("watched").GetProperty("movies").Deserialize<string[]>());
        Assert.Equal(new[] { "tt1000002:2:3" }, completed.GetProperty("watched").GetProperty("episodes").Deserialize<string[]>());
        Assert.Equal(MediaLibraryStatuses.Completed, await schema.Db.MediaLibraryEntries.Where(e => e.MediaTitleId == movie.Id)
            .Select(e => e.Status).SingleAsync());
        Assert.Equal(4, await schema.Db.MediaLibraryEntries.Where(e => e.MediaTitleId == series.Id)
            .Select(e => e.ProgressEpisodes).SingleAsync());
    }

    [PostgresTheory]
    [InlineData("coordinates-replaced")]
    [InlineData("coordinates-removed")]
    [InlineData("imdb-replaced")]
    [InlineData("imdb-unverified")]
    public async Task PullHidesStaleImdbTvRoutesAndRetainsCanonicalReceipts(string change)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await using var fixture = await Fixture.StartAsync(schema.Db);
        var entry = await AddImdbTvEntryAsync(schema.Db);
        var played = ImdbTvEvent("tv-played", "played", At);
        var rewatch = ImdbTvEvent("tv-rewatch", "pause", At + 1);
        await fixture.ExpectPushAsync(played, HttpStatusCode.NoContent, ImdbTvPath(played));
        await fixture.ExpectPushAsync(rewatch, HttpStatusCode.NoContent, ImdbTvPath(rewatch));
        var before = await fixture.PullAsync();
        Assert.Equal(rewatch.VideoId, Assert.Single(before.GetProperty("items").EnumerateArray()).GetProperty("videoId").GetString());
        Assert.Equal(played.VideoId, Assert.Single(before.GetProperty("watched").GetProperty("episodes").EnumerateArray()).GetString());
        var canonicalEpisodeId = await schema.Db.MediaEpisodes.Where(episode => episode.MediaTitleId == entry.MediaTitleId
            && episode.EpisodeNumber == 2).Select(episode => episode.Id).SingleAsync();

        var imdb = TvMetaId;
        switch (change)
        {
            case "coordinates-replaced":
                await RefreshImdbTvCatalogAsync(schema.Db, entry.MediaTitleId, replaceCoordinates: true);
                break;
            case "coordinates-removed":
                await schema.Db.MediaEpisodeProviderContents.Where(content => content.MediaEpisodeId == canonicalEpisodeId)
                    .ExecuteDeleteAsync();
                break;
            case "imdb-replaced":
                imdb = "tt1000003";
                await schema.Db.MediaProviderLinks.Where(link => link.MediaTitleId == entry.MediaTitleId && link.Provider == "imdb")
                    .ExecuteUpdateAsync(setters => setters.SetProperty(link => link.ExternalId, imdb));
                break;
            case "imdb-unverified":
                await schema.Db.MediaProviderLinks.Where(link => link.MediaTitleId == entry.MediaTitleId && link.Provider == "imdb")
                    .ExecuteUpdateAsync(setters => setters.SetProperty(link => link.LastVerifiedAt, (DateTimeOffset?)null));
                break;
        }

        var stale = await fixture.PullAsync(before.GetProperty("version").GetString());
        Assert.Empty(stale.GetProperty("items").EnumerateArray());
        Assert.Empty(stale.GetProperty("watched").GetProperty("episodes").EnumerateArray());
        await fixture.ExpectPushAsync(rewatch, HttpStatusCode.NoContent, ImdbTvPath(rewatch));
        var receipts = await schema.Db.AioStreamsWatchEvents.Where(watch => watch.MediaLibraryEntryId == entry.Id)
            .OrderBy(watch => watch.At).ToListAsync();
        Assert.Equal(new[] { played.Id, rewatch.Id }, receipts.Select(watch => watch.EventId));
        Assert.All(receipts, watch => Assert.Equal(canonicalEpisodeId, watch.MediaEpisodeId));
        Assert.Equal(2, await schema.Db.MediaLibraryEntries.Where(candidate => candidate.Id == entry.Id)
            .Select(candidate => candidate.ProgressEpisodes).SingleAsync());

        if (change is "coordinates-replaced" or "imdb-replaced")
        {
            var replacement = change == "coordinates-replaced"
                ? ImdbTvEvent("replacement-rewatch", "pause", At + 2, season: 1, episode: 2)
                : ImdbTvEvent("replacement-rewatch", "pause", At + 2, imdb: imdb);
            var delayed = replacement with { Id = "replacement-delayed", At = At };
            await fixture.ExpectPushAsync(delayed, HttpStatusCode.NoContent, ImdbTvPath(delayed));
            var delayedPull = await fixture.PullAsync();
            Assert.Empty(delayedPull.GetProperty("items").EnumerateArray());
            Assert.Equal(replacement.VideoId,
                Assert.Single(delayedPull.GetProperty("watched").GetProperty("episodes").EnumerateArray()).GetString());

            await fixture.ExpectPushAsync(replacement, HttpStatusCode.NoContent, ImdbTvPath(replacement));
            var current = await fixture.PullAsync();
            Assert.Equal(replacement.VideoId, Assert.Single(current.GetProperty("items").EnumerateArray())
                .GetProperty("videoId").GetString());
            Assert.Equal(replacement.VideoId,
                Assert.Single(current.GetProperty("watched").GetProperty("episodes").EnumerateArray()).GetString());
        }
    }

    [PostgresFact]
    public async Task FreshImdbTvPushWaitsForCatalogRefreshAndRejectsReboundCoordinates()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        var lockAttempt = new ImdbTvLinkLockAttempt();
        await using var fixture = await Fixture.StartAsync(schema.Db, interceptor: lockAttempt);
        var entry = await AddImdbTvEntryAsync(schema.Db);
        var played = ImdbTvEvent("in-flight", "played", At);
        await schema.Db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var refresh = await schema.Db.Database.BeginTransactionAsync();
            await RefreshImdbTvCatalogAsync(schema.Db, entry.MediaTitleId, replaceCoordinates: true);
            var push = fixture.PushAsync(played, ImdbTvPath(played));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var pid = await lockAttempt.Pid.Task.WaitAsync(timeout.Token);
            while (!await schema.Db.Database.SqlQuery<bool>($"""
                SELECT cardinality(pg_blocking_pids({pid})) > 0 AS "Value"
                """).SingleAsync(timeout.Token))
                await Task.Delay(10, timeout.Token);

            await refresh.CommitAsync(timeout.Token);
            Assert.Equal(HttpStatusCode.NotFound, await push.WaitAsync(timeout.Token));
        });

        Assert.Empty(await schema.Db.AioStreamsWatchEvents.ToListAsync());
        Assert.Equal(1, await schema.Db.MediaLibraryEntries.Where(candidate => candidate.Id == entry.Id)
            .Select(candidate => candidate.ProgressEpisodes).SingleAsync());
        var nextPause = ImdbTvEvent("new-episode", "pause", At + 1);
        await fixture.ExpectPushAsync(nextPause, HttpStatusCode.NoContent, ImdbTvPath(nextPause));
        var boundEpisode = await schema.Db.AioStreamsWatchEvents.Select(watch => watch.MediaEpisodeId).SingleAsync();
        Assert.Equal(3, await schema.Db.MediaEpisodes.Where(episode => episode.Id == boundEpisode)
            .Select(episode => episode.EpisodeNumber).SingleAsync());
    }

    [PostgresTheory]
    [InlineData("unlinked")]
    [InlineData("ambiguous-canonical-episode")]
    [InlineData("ambiguous-provider-coordinates")]
    public async Task CatalogAndImdbTrackerRejectUnusableSimklCoordinates(string reason)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await using var fixture = await Fixture.StartAsync(schema.Db);
        var series = new MediaTitle
        {
            Id = Guid.NewGuid(), CanonicalTitle = "Fixture series", MediaKind = MediaKinds.Series,
            PrimaryProgressDimension = MediaProgressDimensions.Episode, ReleaseStatusDimension = MediaProgressDimensions.Episode
        };
        AddLink(schema.Db, series, "imdb", "tt1000002");
        AddLink(schema.Db, series, "simkl", "tv:1000002");
        var entry = new MediaLibraryEntry
        {
            Id = Guid.NewGuid(), UserId = 1, MediaTitle = series, Status = MediaLibraryStatuses.Current
        };
        schema.Db.Add(entry);
        await schema.Db.SaveChangesAsync();

        var mappings = new MediaProviderSeasonMappingService(schema.Db, NullLogger<MediaProviderSeasonMappingService>.Instance);
        var catalog = new MediaEpisodeIdentityService(schema.Db, mappings, NullLogger<MediaEpisodeIdentityService>.Instance);
        await catalog.CacheProviderEpisodeCatalogAsync(series.Id, "simkl", "tv:1000002",
            [new() { SeasonNumber = 1, EpisodeNumber = 1 }, new() { SeasonNumber = 2, EpisodeNumber = 1 }],
            [], expectedEpisodeCount: 2, CancellationToken.None);
        await schema.Db.SaveChangesAsync();

        var initial = await catalog.GetEpisodeCatalogAsync(series.Id, CancellationToken.None);
        Assert.Equal((2, 1), (initial!.Episodes[1].SeasonNumber, initial.Episodes[1].SeasonEpisodeNumber));

        if (reason == "unlinked")
        {
            var link = Assert.Single(series.ProviderLinks, item => item.Provider == "simkl");
            schema.Db.Remove(link);
        }
        else
        {
            var canonicalNumber = reason == "ambiguous-canonical-episode" ? 2 : 1;
            var episode = await schema.Db.MediaEpisodes.SingleAsync(item =>
                item.MediaTitleId == series.Id && item.EpisodeNumber == canonicalNumber);
            schema.Db.Add(new MediaEpisodeProviderContent
            {
                Id = Guid.NewGuid(),
                MediaEpisodeId = episode.Id,
                Provider = "simkl",
                ProviderSeriesId = "tv:1000002",
                ProviderContentKey = "conflicting-episode",
                ProviderSequenceNumber = canonicalNumber,
                ProviderSeasonNumber = 2,
                ProviderEpisodeNumber = canonicalNumber
            });
        }
        await schema.Db.SaveChangesAsync();

        var updated = await catalog.GetEpisodeCatalogAsync(series.Id, CancellationToken.None);
        Assert.Equal(2, updated!.Episodes.Count);
        Assert.Null(updated.Episodes[1].SeasonNumber);
        Assert.Null(updated.Episodes[1].SeasonEpisodeNumber);

        var played = Event("unusable-imdb-tv", "played", At, 3_000) with
            { MetaId = "tt1000002", VideoId = "tt1000002:2:1", Season = 2, Episode = 1 };
        await fixture.ExpectPushAsync(played, HttpStatusCode.NotFound,
            Route + "/watch_state/push/series/tt1000002%3A2%3A1.json");
        Assert.False(await schema.Db.AioStreamsWatchEvents.AnyAsync(item => item.MediaLibraryEntryId == entry.Id));
    }

    [PostgresFact]
    public async Task TracksVerifiedImdbMovieRouteForAnimeMovieFormat()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await using var fixture = await Fixture.StartAsync(schema.Db);
        var title = await schema.Db.MediaTitles.SingleAsync();
        title.Format = MediaFormats.Tv;
        AddLink(schema.Db, title, "imdb", MetaId);
        await schema.Db.SaveChangesAsync();

        const string path = Route + "/watch_state/push/movie/" + MetaId + ".json";
        var pause = Event("anime-film-pause", "pause", At, position: 2_000) with
            { VideoId = MetaId, Scope = "movie", Season = null, Episode = null };
        await fixture.ExpectPushAsync(pause, HttpStatusCode.NotFound, path);

        title.Format = MediaFormats.Movie;
        title.PrimaryProgressDimension = MediaProgressDimensions.CompletionOnly;
        title.ReleaseStatusDimension = MediaProgressDimensions.CompletionOnly;
        title.SupportsEpisodeProgress = false;
        await schema.Db.SaveChangesAsync();
        await fixture.ExpectPushAsync(pause, HttpStatusCode.NoContent, path);
        var item = Assert.Single((await fixture.PullAsync()).GetProperty("items").EnumerateArray());
        Assert.Equal("movie", item.GetProperty("type").GetString());
        Assert.Equal(MetaId, item.GetProperty("videoId").GetString());
        Assert.Equal(2_000, item.GetProperty("positionMs").GetInt64());

        await fixture.ExpectPushAsync(pause with { Id = "anime-film-played", Event = "played", At = At + 1 },
            HttpStatusCode.NoContent, path);
        var completed = await fixture.PullAsync();
        Assert.Empty(completed.GetProperty("items").EnumerateArray());
        Assert.Equal(MetaId, Assert.Single(completed.GetProperty("watched").GetProperty("movies").EnumerateArray()).GetString());
        Assert.Equal(MediaLibraryStatuses.Completed, await schema.Db.MediaLibraryEntries.Select(e => e.Status).SingleAsync());
    }

    [PostgresTheory]
    [InlineData("imdb-replaced")]
    [InlineData("imdb-unverified")]
    [InlineData("reclassified")]
    public async Task PullHidesStaleMovieRoutesAndRetainsCanonicalReceipts(string change)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await using var fixture = await Fixture.StartAsync(schema.Db);
        var title = await schema.Db.MediaTitles.SingleAsync();
        title.Format = MediaFormats.Movie;
        AddLink(schema.Db, title, "imdb", MetaId);
        await schema.Db.SaveChangesAsync();

        var played = Event("movie-played", "played", At) with
            { VideoId = MetaId, Scope = "movie", Season = null, Episode = null };
        var rewatch = played with { Id = "movie-rewatch", Event = "pause", At = At + 1, PositionMs = 2_000 };
        await fixture.ExpectPushAsync(played, HttpStatusCode.NoContent, PathFor(MetaId));
        await fixture.ExpectPushAsync(rewatch, HttpStatusCode.NoContent, PathFor(MetaId));
        var before = await fixture.PullAsync();
        Assert.Equal(MetaId, Assert.Single(before.GetProperty("items").EnumerateArray()).GetProperty("videoId").GetString());
        Assert.Equal(new[] { MetaId }, before.GetProperty("watched").GetProperty("movies").Deserialize<string[]>());

        const string replacement = "tt1000003";
        var link = await schema.Db.MediaProviderLinks.SingleAsync(link => link.Provider == "imdb");
        switch (change)
        {
            case "imdb-replaced":
                link.ExternalId = replacement;
                break;
            case "imdb-unverified":
                link.LastVerifiedAt = null;
                break;
            case "reclassified":
                title.Format = MediaFormats.Tv;
                break;
        }
        await schema.Db.SaveChangesAsync();

        var after = await fixture.PullAsync(before.GetProperty("version").GetString());
        Assert.Empty(after.GetProperty("items").EnumerateArray());
        Assert.Empty(after.GetProperty("watched").GetProperty("movies").EnumerateArray());
        await fixture.ExpectPushAsync(rewatch, HttpStatusCode.NoContent, PathFor(MetaId));
        Assert.Equal(2, await schema.Db.AioStreamsWatchEvents.CountAsync());
        Assert.Equal(MediaLibraryStatuses.Completed, await schema.Db.MediaLibraryEntries.Select(entry => entry.Status).SingleAsync());

        if (change == "imdb-replaced")
        {
            var pause = rewatch with { Id = "replacement-pause", MetaId = replacement, VideoId = replacement, At = At };
            await fixture.ExpectPushAsync(pause, HttpStatusCode.NoContent, PathFor(replacement));
            Assert.Empty((await fixture.PullAsync()).GetProperty("items").EnumerateArray());
            await fixture.ExpectPushAsync(pause with { Id = "replacement-rewatch", At = At + 2 }, HttpStatusCode.NoContent,
                PathFor(replacement));
            var current = await fixture.PullAsync();
            Assert.Equal(replacement, Assert.Single(current.GetProperty("items").EnumerateArray()).GetProperty("videoId").GetString());
            Assert.Equal(new[] { replacement }, current.GetProperty("watched").GetProperty("movies").Deserialize<string[]>());
        }

        static string PathFor(string id) => $"{Route}/watch_state/push/movie/{id}.json";
    }

    [PostgresFact]
    public async Task RejectsUnboundIdentityUnknownIdsAndRevokedCredential()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await using var fixture = await Fixture.StartAsync(schema.Db);
        var stop = Event("stop", "stop", At, position: 2_500);

        using var missing = await fixture.Client.GetAsync(PullRoute);
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        using var wrong = await fixture.Client.GetAsync($"{Route}/manifest.json?token={NewToken()}");
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        using var viewer = await fixture.Client.GetAsync(fixture.Authorize(PullRoute) + "&viewer=other");
        Assert.Equal(HttpStatusCode.NotFound, viewer.StatusCode);

        await fixture.ExpectPushAsync(stop with { MetaId = "tt9999999" }, HttpStatusCode.NotFound);
        await fixture.ExpectPushAsync(stop with { MetaId = "tmdb:30991" }, HttpStatusCode.NotFound);
        await fixture.ExpectPushAsync(stop with { Season = 0 }, HttpStatusCode.BadRequest);
        await fixture.ExpectPushAsync(stop with { VideoId = MetaId }, HttpStatusCode.NotFound);
        await fixture.ExpectPushAsync(stop with { VideoId = "kitsu:1:3" }, HttpStatusCode.NotFound, EpisodeRoute(1, 3));
        await fixture.ExpectPushAsync(stop with { VideoId = "kitsu:8:1" }, HttpStatusCode.NotFound, EpisodeRoute(8, 1));
        await fixture.ExpectPushAsync(stop with { VideoId = "kitsu:9:1" }, HttpStatusCode.ServiceUnavailable, EpisodeRoute(9, 1));
        await fixture.ExpectPushAsync(stop with { Scope = "season" }, HttpStatusCode.BadRequest);

        await schema.Db.Set<AioStreamsPairing>().ExecuteDeleteAsync();
        await fixture.ExpectPushAsync(stop, HttpStatusCode.Unauthorized);
        Assert.Equal(0, await schema.Db.AioStreamsWatchEvents.CountAsync());
    }

    [PostgresFact]
    public async Task RejectsConflictingKitsuCoordinatesAndPreservesVerifiedImdbOffsets()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await using var fixture = await Fixture.StartAsync(schema.Db, imdbSeason: 2, imdbFromEpisode: 10);
        var path = EpisodeRoute(1, 2);
        var pause = Event("second", "pause", At, position: 2_500) with { VideoId = "kitsu:1:2", Episode = 2 };
        var kitsuParent = pause with { MetaId = "kitsu:1" };
        await fixture.ExpectPushAsync(kitsuParent with { Episode = 11 }, HttpStatusCode.NotFound, path);
        await fixture.ExpectPushAsync(kitsuParent with { Season = 2 }, HttpStatusCode.NotFound, path);
        await fixture.ExpectPushAsync(pause with { Season = 2, Episode = 99 }, HttpStatusCode.NotFound, path);
        await fixture.ExpectPushAsync(pause with { Season = 3, Episode = 11 }, HttpStatusCode.NotFound, path);
        await fixture.ExpectPushAsync(pause with { Episode = 11 }, HttpStatusCode.NotFound, path);
        await fixture.ExpectPushAsync(pause with { Season = null, Episode = 11 }, HttpStatusCode.NotFound, path);
        Assert.Empty(await schema.Db.AioStreamsWatchEvents.ToListAsync());

        var local = kitsuParent with { Id = "local", Season = null, Episode = null };
        await fixture.ExpectPushAsync(local, HttpStatusCode.NoContent, path);
        await fixture.ExpectPushAsync(local, HttpStatusCode.NoContent, path);
        await fixture.ExpectPushAsync(local with { Episode = 2 }, HttpStatusCode.NotFound, path);
        var localItem = Assert.Single((await fixture.PullAsync()).GetProperty("items").EnumerateArray());
        Assert.Equal("kitsu:1:2", localItem.GetProperty("videoId").GetString());
        Assert.False(localItem.TryGetProperty("season", out _));
        Assert.False(localItem.TryGetProperty("episode", out _));

        await fixture.ExpectPushAsync(pause with { Id = "mixed-local" }, HttpStatusCode.NoContent, path);
        var mapped = pause with { At = At + 1, Season = 2, Episode = 11 };
        await fixture.ExpectPushAsync(mapped, HttpStatusCode.NoContent, path);
        await fixture.ExpectPushAsync(mapped, HttpStatusCode.NoContent, path);
        await fixture.ExpectPushAsync(mapped with { Episode = 12 }, HttpStatusCode.NotFound, path);

        var item = Assert.Single((await fixture.PullAsync()).GetProperty("items").EnumerateArray());
        Assert.Equal(MetaId, item.GetProperty("metaId").GetString());
        Assert.Equal("kitsu:1:2", item.GetProperty("videoId").GetString());
        Assert.Equal(2, item.GetProperty("season").GetInt32());
        Assert.Equal(11, item.GetProperty("episode").GetInt32());
        var episodeId = await schema.Db.MediaEpisodes.Where(e => e.EpisodeNumber == 2).Select(e => e.Id).SingleAsync();
        var receipts = await schema.Db.AioStreamsWatchEvents.ToListAsync();
        Assert.Equal(3, receipts.Count);
        Assert.All(receipts, receipt => Assert.Equal(episodeId, receipt.MediaEpisodeId));
    }

    [PostgresTheory]
    [InlineData("anime")]
    [InlineData("series")]
    public async Task TracksAnImdbVideoOfAnAnimeEpisodeThroughTheVerifiedOffset(string type)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await using var fixture = await Fixture.StartAsync(schema.Db, imdbSeason: 2, imdbFromEpisode: 10, nonImdbEpisodes: []);
        var pause = Event("imdb-anime", "pause", At, position: 2_500)
            with { VideoId = $"{MetaId}:2:11", Season = 2, Episode = 11 };
        string PathFor(string videoId) => $"{Route}/watch_state/push/{type}/{Uri.EscapeDataString(videoId)}.json";

        await fixture.ExpectPushAsync(pause, HttpStatusCode.NotFound, PathFor(pause.VideoId!));
        var title = await schema.Db.MediaTitles.SingleAsync();
        AddLink(schema.Db, title, "imdb", MetaId);
        await schema.Db.SaveChangesAsync();

        foreach (var invalid in new[] {
            pause with { VideoId = $"{MetaId}:1:11", Season = 1 },
            pause with { VideoId = $"{MetaId}:2:9", Episode = 9 },
            pause with { VideoId = $"{MetaId}:2:12", Episode = 12 },
            pause with { VideoId = "tt9999999:2:11", MetaId = "tt9999999" }
        })
            await fixture.ExpectPushAsync(invalid, HttpStatusCode.NotFound, PathFor(invalid.VideoId!));
        Assert.Empty(await schema.Db.AioStreamsWatchEvents.ToListAsync());

        var path = PathFor(pause.VideoId!);
        await fixture.ExpectPushAsync(pause, HttpStatusCode.NoContent, path);
        var item = Assert.Single((await fixture.PullAsync()).GetProperty("items").EnumerateArray());
        Assert.Equal(type, item.GetProperty("type").GetString());
        Assert.Equal(pause.VideoId, item.GetProperty("videoId").GetString());
        Assert.Equal(2, item.GetProperty("season").GetInt32());
        Assert.Equal(11, item.GetProperty("episode").GetInt32());

        var finish = pause with { Id = "imdb-finish", Event = "played", At = At + 1 };
        await fixture.ExpectPushAsync(finish, HttpStatusCode.NoContent, path);
        await fixture.ExpectPushAsync(finish, HttpStatusCode.NoContent, path);
        var state = await fixture.PullAsync();
        Assert.Empty(state.GetProperty("items").EnumerateArray());
        Assert.Equal(pause.VideoId, Assert.Single(state.GetProperty("watched").GetProperty("episodes").EnumerateArray()).GetString());
        var episodeId = await schema.Db.MediaEpisodes.Where(e => e.EpisodeNumber == 2).Select(e => e.Id).SingleAsync();
        var receipts = await schema.Db.AioStreamsWatchEvents.ToListAsync();
        Assert.Equal(2, receipts.Count);
        Assert.All(receipts, receipt => Assert.Equal(episodeId, receipt.MediaEpisodeId));
        Assert.Null(await schema.Db.MediaLibraryEntries.Select(entry => entry.ProgressEpisodes).SingleAsync());

        var first = finish with { Id = "imdb-first", VideoId = $"{MetaId}:2:10", Episode = 10 };
        await fixture.ExpectPushAsync(first, HttpStatusCode.NoContent, PathFor(first.VideoId!));
        Assert.Equal(2, await schema.Db.MediaLibraryEntries.Select(entry => entry.ProgressEpisodes).SingleAsync());
    }

    [PostgresTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task PullRevalidatesAnimeRoutesAfterTheIdentityCacheExpires(bool unavailable, bool empty)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        var api = new AnimeApiFixture("TV", MetaId, 2, 10, []);
        var clock = new TestTimeProvider();
        await using var fixture = await Fixture.StartAsync(schema.Db, animeApi: api, timeProvider: clock);
        AddLink(schema.Db, await schema.Db.MediaTitles.SingleAsync(), "imdb", MetaId);
        await schema.Db.SaveChangesAsync();

        const string kitsuVideo = "kitsu:1:2";
        const string imdbVideo = MetaId + ":2:11";
        var played = Event("played", "played", At) with { VideoId = imdbVideo, Season = 2, Episode = 11 };
        var native = Event("native", "pause", At + 1, 2_000) with { VideoId = kitsuVideo, MetaId = "kitsu:1", Episode = 2 };
        var mixed = native with { Id = "mixed", MetaId = MetaId, Season = 2, Episode = 11, At = At + 2 };
        var imdb = mixed with { Id = "imdb", VideoId = imdbVideo, At = At + 3 };
        var first = imdb with { Id = "earlier-episode", VideoId = MetaId + ":2:10", Episode = 10 };
        foreach (var payload in new[] { played, native, mixed, imdb, first })
            await fixture.ExpectPushAsync(payload, HttpStatusCode.NoContent, PathFor(payload.VideoId!));

        var before = await fixture.PullAsync();
        Assert.Equal(2, before.GetProperty("items").GetArrayLength());
        Assert.Contains(before.GetProperty("items").EnumerateArray(), item => item.GetProperty("videoId").GetString() == imdbVideo);
        Assert.Equal(new[] { kitsuVideo, imdbVideo }, before.GetProperty("watched").GetProperty("episodes").Deserialize<string[]>());
        Assert.Equal(2, api.RequestCount);

        api.ImdbFromEpisode = 11;
        api.Unavailable = unavailable;
        api.ReturnEmpty = empty;
        clock.Advance(AioStreamsAnimeEnricher.FreshDuration);

        if (unavailable || empty)
        {
            using var after = await fixture.Client.GetAsync(fixture.Authorize(PullRoute));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, after.StatusCode);
            Assert.Equal(3, api.RequestCount);
        }
        else
        {
            var after = await fixture.PullAsync(before.GetProperty("version").GetString());
            Assert.Equal(4, api.RequestCount);
            var item = Assert.Single(after.GetProperty("items").EnumerateArray());
            Assert.Equal(kitsuVideo, item.GetProperty("videoId").GetString());
            Assert.Equal(native.MetaId, item.GetProperty("metaId").GetString());
            Assert.Equal(new[] { kitsuVideo }, after.GetProperty("watched").GetProperty("episodes").Deserialize<string[]>());
        }

        await fixture.ExpectPushAsync(imdb, HttpStatusCode.NoContent, PathFor(imdbVideo));
        Assert.Equal(5, await schema.Db.AioStreamsWatchEvents.CountAsync());
        var episodeId = await schema.Db.MediaEpisodes.Where(episode => episode.EpisodeNumber == 2)
            .Select(episode => episode.Id).SingleAsync();
        Assert.All(await schema.Db.AioStreamsWatchEvents.Where(watch => watch.EventId != first.Id).ToListAsync(),
            watch => Assert.Equal(episodeId, watch.MediaEpisodeId));

        if (!unavailable)
        {
            const string replacement = MetaId + ":2:12";
            var delayed = imdb with { Id = "current-imdb", VideoId = replacement, Episode = 12, At = At };
            await fixture.ExpectPushAsync(delayed, HttpStatusCode.NoContent, PathFor(replacement));
            var current = await fixture.PullAsync();
            Assert.Equal(kitsuVideo, Assert.Single(current.GetProperty("items").EnumerateArray()).GetProperty("videoId").GetString());
            Assert.Equal(new[] { kitsuVideo, replacement }, current.GetProperty("watched").GetProperty("episodes").Deserialize<string[]>());
            Assert.Equal(4, api.RequestCount);
        }

        static string PathFor(string id) => $"{Route}/watch_state/push/anime/{Uri.EscapeDataString(id)}.json";
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TracksAnimeEpisodeWithAnEffectiveProviderMappingAndRejectsAnOrphanMapping(bool imdb)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await using var fixture = await Fixture.StartAsync(schema.Db, imdbSeason: 2, imdbFromEpisode: 10,
            nonImdbEpisodes: imdb ? [] : null);
        var title = await schema.Db.MediaTitles.SingleAsync();
        AddLink(schema.Db, title, "imdb", MetaId);
        const string provider = MediaObservationSiteIdentifiers.Crunchyroll;
        var now = DateTimeOffset.UtcNow;
        schema.Db.MediaProviderEpisodes.Add(new MediaProviderEpisode
        {
            Provider = provider, ProviderEpisodeId = "CRTHIRD", ProviderEpisodeNumber = 3,
            ProviderUrlPath = "/watch/CRTHIRD", SeenCount = 1, FirstSeenAt = now, LastSeenAt = now
        });
        var mappings = new MediaProviderSeasonMappingService(schema.Db, NullLogger<MediaProviderSeasonMappingService>.Instance);
        await schema.Db.SaveChangesAsync();
        await mappings.EstablishDirectAsync(provider, "CRTHIRD", title.Id, 3,
            MediaProviderSeasonMappingSources.Manual, CancellationToken.None);
        await mappings.EstablishDirectAsync(provider, "CRORPHAN", title.Id, 4,
            MediaProviderSeasonMappingSources.Automatic, CancellationToken.None, observedAt: now);

        var known = PauseFor("mapped-only", 3);
        var unknown = PauseFor("orphan", 4);
        await fixture.ExpectPushAsync(unknown, HttpStatusCode.NotFound, PathFor(unknown.VideoId!));
        Assert.False(await schema.Db.MediaEpisodes.AnyAsync(episode => episode.EpisodeNumber > 2));
        Assert.Empty(await schema.Db.AioStreamsWatchEvents.ToListAsync());

        await fixture.ExpectPushAsync(known, HttpStatusCode.NoContent, PathFor(known.VideoId!));
        await fixture.ExpectPushAsync(known, HttpStatusCode.NoContent, PathFor(known.VideoId!));
        var canonical = await schema.Db.MediaEpisodes.SingleAsync(episode => episode.EpisodeNumber == 3);
        var receipt = await schema.Db.AioStreamsWatchEvents.SingleAsync();
        Assert.Equal(title.Id, canonical.MediaTitleId);
        Assert.Equal(canonical.Id, receipt.MediaEpisodeId);
        Assert.Equal(known.VideoId, receipt.VideoId);
        var item = Assert.Single((await fixture.PullAsync()).GetProperty("items").EnumerateArray());
        Assert.Equal(known.VideoId, item.GetProperty("videoId").GetString());

        AioStreamsWatchStateController.PlaybackEvent PauseFor(string id, int number)
            => Event(id, "pause", At, position: 2_500) with
            {
                MetaId = imdb ? MetaId : "kitsu:1",
                VideoId = imdb ? $"{MetaId}:2:{number + 9}" : $"kitsu:1:{number}",
                Season = imdb ? 2 : 1,
                Episode = imdb ? number + 9 : number
            };
        static string PathFor(string videoId) => $"{Route}/watch_state/push/anime/{Uri.EscapeDataString(videoId)}.json";
    }

    [PostgresFact]
    public async Task RejectsOffsetMappingAcrossAKnownGapInImdbNumbering()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await using var fixture = await Fixture.StartAsync(schema.Db, imdbSeason: 2, imdbFromEpisode: 10, nonImdbEpisodes: [2]);
        AddLink(schema.Db, await schema.Db.MediaTitles.SingleAsync(), "imdb", MetaId);
        await schema.Db.SaveChangesAsync();
        var pause = Event("imdb-gap", "pause", At, position: 2_500) with { VideoId = $"{MetaId}:2:11", Season = 2, Episode = 11 };
        await fixture.ExpectPushAsync(pause, HttpStatusCode.NotFound, $"{Route}/watch_state/push/anime/{MetaId}%3A2%3A11.json");
        await fixture.ExpectPushAsync(pause with { VideoId = "kitsu:1:2" }, HttpStatusCode.NotFound, EpisodeRoute(1, 2));
        Assert.Empty(await schema.Db.AioStreamsWatchEvents.ToListAsync());
        await fixture.ExpectPushAsync(pause with { VideoId = "kitsu:1:2", Season = 1, Episode = 2 },
            HttpStatusCode.NoContent, EpisodeRoute(1, 2));
    }

    [PostgresFact]
    public async Task RejectsDirectImdbAnimeOffsetWithoutGapEvidence()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await using var fixture = await Fixture.StartAsync(schema.Db, imdbSeason: 2, imdbFromEpisode: 10);
        AddLink(schema.Db, await schema.Db.MediaTitles.SingleAsync(), "imdb", MetaId);
        await schema.Db.SaveChangesAsync();

        var pause = Event("imdb-without-gap-data", "pause", At, position: 2_500) with
            { VideoId = $"{MetaId}:2:11", Season = 2, Episode = 11 };
        await fixture.ExpectPushAsync(pause, HttpStatusCode.NotFound, $"{Route}/watch_state/push/anime/{MetaId}%3A2%3A11.json");
        Assert.Empty(await schema.Db.AioStreamsWatchEvents.ToListAsync());

        await fixture.ExpectPushAsync(pause with { VideoId = "kitsu:1:2" }, HttpStatusCode.NoContent, EpisodeRoute(1, 2));
        var item = Assert.Single((await fixture.PullAsync()).GetProperty("items").EnumerateArray());
        Assert.Equal("kitsu:1:2", item.GetProperty("videoId").GetString());
    }

    [PostgresFact]
    public async Task MixedImdbParentUsesItsVerifiedSeasonWhenNumberingOverlaps()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await using var fixture = await Fixture.StartAsync(schema.Db, imdbSeason: 1, imdbFromEpisode: 2);
        var pause = Event("overlap", "pause", At, position: 2_500) with { VideoId = "kitsu:1:2", Episode = 2 };
        var path = EpisodeRoute(1, 2);
        await fixture.ExpectPushAsync(pause, HttpStatusCode.NotFound, path);
        await fixture.ExpectPushAsync(pause with { Episode = 3 }, HttpStatusCode.NoContent, path);
        var item = Assert.Single((await fixture.PullAsync()).GetProperty("items").EnumerateArray());
        Assert.Equal(3, item.GetProperty("episode").GetInt32());
    }

    [PostgresFact]
    public async Task RejectsMixedImdbParentVerifiedOnAnotherTitleButAcceptsNativeKitsu()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await using var fixture = await Fixture.StartAsync(schema.Db);
        var otherTitle = new MediaTitle
        {
            Id = Guid.NewGuid(), CanonicalTitle = "Other title", MediaKind = MediaKinds.Series,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode
        };
        AddLink(schema.Db, otherTitle, "imdb", MetaId);
        await schema.Db.SaveChangesAsync();

        var played = Event("mixed", "played", At);
        await fixture.ExpectPushAsync(played, HttpStatusCode.NotFound);
        Assert.Empty(await schema.Db.AioStreamsWatchEvents.ToListAsync());
        Assert.Null(await schema.Db.MediaLibraryEntries.Select(entry => entry.ProgressEpisodes).SingleAsync());

        await fixture.ExpectPushAsync(played with { Id = "native", MetaId = "kitsu:1" }, HttpStatusCode.NoContent);
        Assert.Equal("native", (await schema.Db.AioStreamsWatchEvents.SingleAsync()).EventId);
        Assert.Equal(1, await schema.Db.MediaLibraryEntries.Select(entry => entry.ProgressEpisodes).SingleAsync());
    }

    [PostgresFact]
    public async Task CompletionSupersedesAnExistingPauseAtTheSameSecond()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await using var fixture = await Fixture.StartAsync(schema.Db);
        var pause = Event("before-completion", "pause", At, position: 1_250);
        await fixture.ExpectPushAsync(pause, HttpStatusCode.NoContent);

        await schema.Db.MediaLibraryEntries.ExecuteUpdateAsync(setters => setters
            .SetProperty(entry => entry.ProgressEpisodes, 1)
            .SetProperty(entry => entry.LastMutationSource, MediaMutationSources.UserProgressUpdate)
            .SetProperty(entry => entry.UpdatedAt, DateTimeOffset.FromUnixTimeSeconds(At)));
        Assert.Empty((await fixture.PullAsync()).GetProperty("items").EnumerateArray());

        await fixture.ExpectPushAsync(pause with { Id = "rewatch", At = At + 1, PositionMs = 2_000 },
            HttpStatusCode.NoContent);
        var item = Assert.Single((await fixture.PullAsync()).GetProperty("items").EnumerateArray());
        Assert.Equal(2_000, item.GetProperty("positionMs").GetInt64());
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TrackerCompletionWinsTheSameSecondRegardlessOfDeliveryOrder(bool differentAlias)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await using var fixture = await Fixture.StartAsync(schema.Db, imdbSeason: 1, imdbFromEpisode: 1, nonImdbEpisodes: []);
        AddLink(schema.Db, await schema.Db.MediaTitles.SingleAsync(), "imdb", MetaId);
        await schema.Db.SaveChangesAsync();

        await fixture.ExpectPushAsync(Event("played", "played", At), HttpStatusCode.NoContent);
        var pause = Event("late-pause", "pause", At, position: 1_250);
        var path = PushRoute;
        if (differentAlias)
        {
            pause = pause with { VideoId = MetaId + ":1:1" };
            path = $"{Route}/watch_state/push/series/{MetaId}%3A1%3A1.json";
        }

        await fixture.ExpectPushAsync(pause, HttpStatusCode.NoContent, path);
        Assert.Empty((await fixture.PullAsync()).GetProperty("items").EnumerateArray());
        await fixture.ExpectPushAsync(pause with { Id = "fresh-rewatch", At = At + 1 }, HttpStatusCode.NoContent, path);
        Assert.Single((await fixture.PullAsync()).GetProperty("items").EnumerateArray());
    }

    [PostgresFact]
    public async Task PullKeepsLibraryCompletionAcrossRouteAliasesAndScoreEdits()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await using var fixture = await Fixture.StartAsync(schema.Db, imdbSeason: 1, imdbFromEpisode: 1, nonImdbEpisodes: []);
        AddLink(schema.Db, await schema.Db.MediaTitles.SingleAsync(), "imdb", MetaId);
        await schema.Db.SaveChangesAsync();
        await fixture.ExpectPushAsync(Event("before-external", "pause", At, position: 1_250), HttpStatusCode.NoContent);
        Assert.Single((await fixture.PullAsync()).GetProperty("items").EnumerateArray());

        var completionTime = DateTimeOffset.FromUnixTimeSeconds(At + 20);
        await schema.Db.MediaLibraryEntries.ExecuteUpdateAsync(setters => setters.SetProperty(e => e.ProgressEpisodes, 1)
            .SetProperty(e => e.LastMutationSource, MediaMutationSources.UserProgressUpdate)
            .SetProperty(e => e.LastLocalEditAt, completionTime).SetProperty(e => e.UpdatedAt, completionTime));
        var completed = await fixture.PullAsync();
        Assert.Empty(completed.GetProperty("items").EnumerateArray());
        Assert.Equal(VideoId, Assert.Single(completed.GetProperty("watched").GetProperty("episodes").EnumerateArray()).GetString());

        var scoreTime = completionTime.AddSeconds(10);
        await schema.Db.MediaLibraryEntries.ExecuteUpdateAsync(setters => setters.SetProperty(e => e.Score, 8m)
            .SetProperty(e => e.LastMutationSource, MediaMutationSources.UserScoreUpdate)
            .SetProperty(e => e.LastLocalEditAt, scoreTime).SetProperty(e => e.UpdatedAt, scoreTime));
        const string imdbVideo = MetaId + ":1:1";
        const string imdbRoute = Route + "/watch_state/push/series/" + MetaId + "%3A1%3A1.json";
        var delayed = Event("delayed-alias", "pause", At + 10, position: 1_500) with { VideoId = imdbVideo };
        await fixture.ExpectPushAsync(delayed, HttpStatusCode.NoContent, imdbRoute);
        Assert.Empty((await fixture.PullAsync()).GetProperty("items").EnumerateArray());

        await fixture.ExpectPushAsync(delayed with { Id = "after-external", At = At + 21, PositionMs = 2_500 },
            HttpStatusCode.NoContent, imdbRoute);
        var rewatch = await fixture.PullAsync();
        Assert.Equal(2_500, Assert.Single(rewatch.GetProperty("items").EnumerateArray()).GetProperty("positionMs").GetInt64());
        Assert.Equal(new[] { VideoId, imdbVideo }, rewatch.GetProperty("watched").GetProperty("episodes").Deserialize<string[]>());
    }

    [PostgresFact]
    public async Task PullKeepsEarlierEpisodeRewatchWhenLibraryProgressAdvances()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await using var fixture = await Fixture.StartAsync(schema.Db);
        await fixture.ExpectPushAsync(Event("episode-one-pause", "pause", At - 1, position: 1_000),
            HttpStatusCode.NoContent);
        await SetProgressAsync(1, At);

        var rewatch = Event("episode-one-rewatch", "pause", At + 1, position: 1_250);
        await fixture.ExpectPushAsync(rewatch, HttpStatusCode.NoContent);
        var nextEpisode = Event("episode-two-pause", "pause", At + 2, position: 2_500) with
            { VideoId = "kitsu:1:2", Episode = 2 };
        await fixture.ExpectPushAsync(nextEpisode, HttpStatusCode.NoContent, EpisodeRoute(1, 2));
        await SetProgressAsync(2, At + 3);

        var state = await fixture.PullAsync();
        var item = Assert.Single(state.GetProperty("items").EnumerateArray());
        Assert.Equal(VideoId, item.GetProperty("videoId").GetString());
        Assert.Equal(1_250, item.GetProperty("positionMs").GetInt64());
        Assert.Equal(new[] { VideoId, nextEpisode.VideoId },
            state.GetProperty("watched").GetProperty("episodes").Deserialize<string[]>());

        Task<int> SetProgressAsync(int progress, long at)
        {
            var updated = DateTimeOffset.FromUnixTimeSeconds(at);
            return schema.Db.MediaLibraryEntries.ExecuteUpdateAsync(setters => setters.SetProperty(e => e.ProgressEpisodes, progress)
                .SetProperty(e => e.LastMutationSource, MediaMutationSources.UserProgressUpdate)
                .SetProperty(e => e.LastLocalEditAt, updated).SetProperty(e => e.UpdatedAt, updated));
        }
    }

    [PostgresFact]
    public async Task ResolvesFromKitsuEpisodeAndOnlyAdvancesConsecutiveCompletions()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await using var fixture = await Fixture.StartAsync(schema.Db);
        var second = Event("second", "played", At) with { VideoId = "kitsu:1:2", Episode = 2, Season = null };
        await fixture.ExpectPushAsync(second, HttpStatusCode.NoContent, EpisodeRoute(1, 2, "series"));
        Assert.Null(await schema.Db.MediaLibraryEntries.Select(entry => entry.ProgressEpisodes).SingleAsync());
        var watched = (await fixture.PullAsync()).GetProperty("watched").GetProperty("episodes");
        Assert.Equal(new[] { "kitsu:1:2" }, watched.Deserialize<string[]>());

        var first = Event("first", "played", At + 1);
        await fixture.ExpectPushAsync(first, HttpStatusCode.NoContent);
        Assert.Equal(2, await schema.Db.MediaLibraryEntries.Select(entry => entry.ProgressEpisodes).SingleAsync());
        watched = (await fixture.PullAsync()).GetProperty("watched").GetProperty("episodes");
        Assert.Equal(new[] { VideoId, "kitsu:1:2" }, watched.Deserialize<string[]>());
    }

    [PostgresFact]
    public async Task RejectsConflictingProviderLinksEvenWhenAnExactEpisodeExists()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await using var fixture = await Fixture.StartAsync(schema.Db);
        var title = await schema.Db.MediaTitles.SingleAsync();
        AddLink(schema.Db, title, "anilist", "999");
        await schema.Db.SaveChangesAsync();
        await fixture.ExpectPushAsync(Event("conflict", "played", At), HttpStatusCode.NotFound);
        Assert.Empty(await schema.Db.AioStreamsWatchEvents.ToListAsync());
    }

    [PostgresTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RejectsStandaloneSpecialsEvenWhenGroupedUnderSeasonOne(bool markedSpecialByAio)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await using var fixture = await Fixture.StartAsync(schema.Db, markedSpecialByAio ? "SPECIAL" : "TV");
        if (!markedSpecialByAio)
        {
            await schema.Db.MediaTitles.ExecuteUpdateAsync(setters => setters.SetProperty(title => title.Format, MediaFormats.Special));
        }

        await fixture.ExpectPushAsync(Event("special", "played", At), HttpStatusCode.NotFound);
        Assert.Empty(await schema.Db.AioStreamsWatchEvents.ToListAsync());
    }

    [PostgresFact]
    public async Task RejectsConflictingRoutingWhenConcurrentDeliveriesUseTheSameEventId()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await using var fixture = await Fixture.StartAsync(schema.Db, interceptor: new ReceiptReadBarrier());
        var stop = Event("same-id", "stop", At, position: 2_500);
        var statuses = await Task.WhenAll(fixture.PushAsync(stop), fixture.PushAsync(stop with { MetaId = "kitsu:1" }));
        Assert.Contains(HttpStatusCode.NoContent, statuses);
        Assert.Contains(HttpStatusCode.NotFound, statuses);
        Assert.Equal(1, await schema.Db.AioStreamsWatchEvents.CountAsync());
    }

    [PostgresFact]
    public async Task RejectsConflictingImdbIdentitiesFromTheAnimeApi()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await using var fixture = await Fixture.StartAsync(schema.Db, imdbHint: "tt9999999");
        await fixture.ExpectPushAsync(Event("ambiguous", "played", At), HttpStatusCode.NotFound);
        Assert.Empty(await schema.Db.AioStreamsWatchEvents.ToListAsync());
    }

    [PostgresFact]
    public async Task BackfillsRoutingWhenAnExistingReceiptIsRetried()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await using var fixture = await Fixture.StartAsync(schema.Db);
        schema.Db.AioStreamsWatchEvents.Add(new AioStreamsWatchEvent
        {
            MediaLibraryEntryId = await schema.Db.MediaLibraryEntries.Select(entry => entry.Id).SingleAsync(),
            MediaEpisodeId = await schema.Db.MediaEpisodes.Where(ep => ep.EpisodeNumber == 1).Select(ep => ep.Id).SingleAsync(),
            EventId = "legacy", At = At, PositionMs = 2_000
        });
        await schema.Db.SaveChangesAsync();
        Assert.Empty((await fixture.PullAsync()).GetProperty("items").EnumerateArray());

        await fixture.ExpectPushAsync(Event("legacy", "stop", At, 2_000), HttpStatusCode.NoContent);
        Assert.Equal(1, await schema.Db.AioStreamsWatchEvents.CountAsync());
        var item = Assert.Single((await fixture.PullAsync()).GetProperty("items").EnumerateArray());
        Assert.Equal(VideoId, item.GetProperty("videoId").GetString());
        Assert.Equal(2_000, item.GetProperty("positionMs").GetInt64());
    }

    [PostgresTheory]
    [InlineData(false, -1)]
    [InlineData(false, 0)]
    [InlineData(true, -1)]
    [InlineData(true, 0)]
    public async Task PullSuppressesTheFirstDelayedPauseAfterExternalCompletionAndKeepsARewatch(
        bool movie, int secondsAfterCompletion)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await using var fixture = await Fixture.StartAsync(schema.Db);
        var pause = Event("delayed-first-pause", "pause", At + secondsAfterCompletion, position: 2_000);
        var path = PushRoute;
        var completionTime = DateTimeOffset.FromUnixTimeSeconds(At).AddMilliseconds(500);

        if (movie)
        {
            var title = await schema.Db.MediaTitles.SingleAsync();
            title.Format = MediaFormats.Movie;
            title.PrimaryProgressDimension = MediaProgressDimensions.CompletionOnly;
            title.ReleaseStatusDimension = MediaProgressDimensions.CompletionOnly;
            title.SupportsEpisodeProgress = false;
            AddLink(schema.Db, title, "imdb", MetaId);
            await schema.Db.SaveChangesAsync();

            path = $"{Route}/watch_state/push/movie/{MetaId}.json";
            pause = pause with { Scope = "movie", VideoId = MetaId, Season = null, Episode = null };
            await schema.Db.MediaLibraryEntries.ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.Status, MediaLibraryStatuses.Completed)
                .SetProperty(e => e.LastMutationSource, MediaMutationSources.UserProgressUpdate)
                .SetProperty(e => e.UpdatedAt, completionTime));
        }
        else
        {
            await schema.Db.MediaLibraryEntries.ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.ProgressEpisodes, 1)
                .SetProperty(e => e.LastMutationSource, MediaMutationSources.UserProgressUpdate)
                .SetProperty(e => e.UpdatedAt, completionTime));
        }

        Assert.False(await schema.Db.AioStreamsWatchEvents.AnyAsync());
        await fixture.ExpectPushAsync(pause, HttpStatusCode.NoContent, path);
        var state = await fixture.PullAsync();
        Assert.Empty(state.GetProperty("items").EnumerateArray());
        var watchedType = movie ? "movies" : "episodes";
        Assert.Equal(pause.VideoId, Assert.Single(state.GetProperty("watched").GetProperty(watchedType).EnumerateArray()).GetString());

        await fixture.ExpectPushAsync(pause with { Id = "first-rewatch", At = At + 1, PositionMs = 2_500 },
            HttpStatusCode.NoContent, path);
        var rewatch = await fixture.PullAsync();
        Assert.Equal(2_500, Assert.Single(rewatch.GetProperty("items").EnumerateArray()).GetProperty("positionMs").GetInt64());
        Assert.Equal(pause.VideoId, Assert.Single(rewatch.GetProperty("watched").GetProperty(watchedType).EnumerateArray()).GetString());
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletionWhileDisconnectedSuppressesOldPausesAfterPairingAgain(bool pauseAlreadyReceived)
    {
        const string pairingPath = "/api/media/aiostreams-pairing";
        await using var schema = await PostgresTestSchema.CreateAsync();
        await using var fixture = await Fixture.StartAsync(schema.Db);
        var pause = Event("before-disconnect", "pause", At, position: 1_250);
        if (pauseAlreadyReceived)
        {
            await fixture.ExpectPushAsync(pause, HttpStatusCode.NoContent);
            Assert.Single((await fixture.PullAsync()).GetProperty("items").EnumerateArray());
        }

        using var disconnect = new HttpRequestMessage(HttpMethod.Delete, pairingPath);
        disconnect.Headers.Add("X-Fixture-User", "1");
        using var disconnected = await fixture.Client.SendAsync(disconnect);
        Assert.Equal(HttpStatusCode.NoContent, disconnected.StatusCode);
        Assert.False(await schema.Db.Set<AioStreamsPairing>().AnyAsync());

        await schema.Db.MediaLibraryEntries.ExecuteUpdateAsync(setters => setters
            .SetProperty(entry => entry.ProgressEpisodes, 1)
            .SetProperty(entry => entry.LastMutationSource, MediaMutationSources.UserProgressUpdate)
            .SetProperty(entry => entry.UpdatedAt, DateTimeOffset.FromUnixTimeSeconds(At + 1)));

        using var pair = new HttpRequestMessage(HttpMethod.Put, pairingPath);
        pair.Headers.Add("X-Fixture-User", "1");
        using var paired = await fixture.Client.SendAsync(pair);
        Assert.Equal(HttpStatusCode.OK, paired.StatusCode);
        var manifest = (await paired.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("manifestPath").GetString()!;
        if (!pauseAlreadyReceived)
        {
            using var delivered = await fixture.Client.PostAsJsonAsync(
                manifest.Replace("/manifest.json", "/watch_state/push/anime/kitsu%3A1%3A1.json"), pause);
            Assert.Equal(HttpStatusCode.NoContent, delivered.StatusCode);
        }
        var state = await fixture.Client.GetFromJsonAsync<JsonElement>(manifest.Replace("/manifest.json", "/watch_state/pull.json"));
        Assert.Empty(state.GetProperty("items").EnumerateArray());
        Assert.Equal(VideoId, Assert.Single(state.GetProperty("watched").GetProperty("episodes").EnumerateArray()).GetString());
    }

    [PostgresFact]
    public async Task CompletionSuppressesASameSecondPauseCommittedWhileTheCompletionIsInFlight()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await using var fixture = await Fixture.StartAsync(schema.Db);
        await schema.Db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var completion = await schema.Db.Database.BeginTransactionAsync();
            await schema.Db.MediaLibraryEntries.ExecuteUpdateAsync(setters => setters
                .SetProperty(entry => entry.ProgressEpisodes, 1)
                .SetProperty(entry => entry.LastMutationSource, MediaMutationSources.UserProgressUpdate)
                .SetProperty(entry => entry.UpdatedAt, DateTimeOffset.FromUnixTimeSeconds(At)));

            await fixture.ExpectPushAsync(Event("concurrent-pause", "pause", At, position: 1_250), HttpStatusCode.NoContent)
                .WaitAsync(TimeSpan.FromSeconds(10));
            await completion.CommitAsync();
        });

        var state = await fixture.PullAsync();
        Assert.Empty(state.GetProperty("items").EnumerateArray());
        Assert.Equal(VideoId, Assert.Single(state.GetProperty("watched").GetProperty("episodes").EnumerateArray()).GetString());
    }

    [PostgresFact]
    public async Task RejectsLegacyReplayWhenAnotherEpisodeClaimsTheEventIdDuringAdoption()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        var racingWrite = new BeforeLegacyUpdate(async cancellationToken =>
        {
            schema.Db.AioStreamsWatchEvents.Add(new AioStreamsWatchEvent
            {
                MediaLibraryEntryId = await schema.Db.MediaLibraryEntries.Select(entry => entry.Id).SingleAsync(cancellationToken),
                MediaEpisodeId = await schema.Db.MediaEpisodes.Where(ep => ep.EpisodeNumber == 2)
                    .Select(ep => ep.Id).SingleAsync(cancellationToken),
                EventId = "legacy", UserId = 1, At = At, Type = "anime", MetaId = MetaId,
                VideoId = "kitsu:1:2", Season = 1, Episode = 2, PositionMs = 1_700
            });
            await schema.Db.SaveChangesAsync(cancellationToken);
        });
        await using var fixture = await Fixture.StartAsync(schema.Db, interceptor: racingWrite);
        schema.Db.AioStreamsWatchEvents.Add(new AioStreamsWatchEvent
        {
            MediaLibraryEntryId = await schema.Db.MediaLibraryEntries.Select(entry => entry.Id).SingleAsync(),
            MediaEpisodeId = await schema.Db.MediaEpisodes.Where(ep => ep.EpisodeNumber == 1).Select(ep => ep.Id).SingleAsync(),
            EventId = "legacy", At = At, PositionMs = 2_000
        });
        await schema.Db.SaveChangesAsync();

        await fixture.ExpectPushAsync(Event("legacy", "stop", At, 2_000), HttpStatusCode.NotFound);
        Assert.Equal(2, await schema.Db.AioStreamsWatchEvents.CountAsync());
        var item = Assert.Single((await fixture.PullAsync()).GetProperty("items").EnumerateArray());
        Assert.Equal("kitsu:1:2", item.GetProperty("videoId").GetString());
    }

    private static string EpisodeRoute(int id, int number, string type = "anime")
        => $"{Route}/watch_state/push/{type}/kitsu%3A{id}%3A{number}.json";

    private static AioStreamsWatchStateController.PlaybackEvent ImdbTvEvent(string id, string kind, long at,
        int season = 2, int episode = 1, string imdb = TvMetaId)
        => Event(id, kind, at, 2_000) with { MetaId = imdb, VideoId = $"{imdb}:{season}:{episode}", Season = season, Episode = episode };

    private static string ImdbTvPath(AioStreamsWatchStateController.PlaybackEvent payload)
        => $"{Route}/watch_state/push/series/{Uri.EscapeDataString(payload.VideoId!)}.json";

    private static async Task<MediaLibraryEntry> AddImdbTvEntryAsync(ApplicationDbContext db)
    {
        var title = new MediaTitle
        {
            Id = Guid.NewGuid(), CanonicalTitle = "Fixture series", MediaKind = MediaKinds.Series,
            PrimaryProgressDimension = MediaProgressDimensions.Episode, ReleaseStatusDimension = MediaProgressDimensions.Episode
        };
        AddLink(db, title, "imdb", TvMetaId);
        AddLink(db, title, "simkl", "tv:1000002");
        var entry = new MediaLibraryEntry
        {
            Id = Guid.NewGuid(), MediaTitle = title, UserId = 1, Status = MediaLibraryStatuses.Current, ProgressEpisodes = 1
        };
        db.Add(entry);
        await db.SaveChangesAsync();
        await RefreshImdbTvCatalogAsync(db, title.Id);
        return entry;
    }

    private static async Task RefreshImdbTvCatalogAsync(ApplicationDbContext db, Guid titleId, bool replaceCoordinates = false)
    {
        var mappings = new MediaProviderSeasonMappingService(db, NullLogger<MediaProviderSeasonMappingService>.Instance);
        var catalog = new MediaEpisodeIdentityService(db, mappings, NullLogger<MediaEpisodeIdentityService>.Instance);
        List<MediaProviderWatchedEpisode> episodes = [new() { SeasonNumber = 1, EpisodeNumber = 1, ProviderEpisodeId = "1" }];
        if (replaceCoordinates)
        {
            episodes.Add(new() { SeasonNumber = 1, EpisodeNumber = 2, ProviderEpisodeId = "2" });
            episodes.Add(new() { SeasonNumber = 2, EpisodeNumber = 1, ProviderEpisodeId = "3" });
        }
        else
        {
            episodes.Add(new() { SeasonNumber = 2, EpisodeNumber = 1, ProviderEpisodeId = "2" });
        }
        await catalog.CacheProviderEpisodeCatalogAsync(titleId, "simkl", "tv:1000002", episodes, [],
            episodes.Count, CancellationToken.None);
        await db.SaveChangesAsync();
    }

    private static void AddLink(ApplicationDbContext db, MediaTitle title, string provider, string externalId)
        => db.MediaProviderLinks.Add(new MediaProviderLink
        {
            Id = Guid.NewGuid(), MediaTitle = title, Provider = provider, ExternalId = externalId,
            LinkSource = MediaMappingSources.Imported, LastVerifiedAt = DateTimeOffset.UtcNow
        });

    private static AioStreamsWatchStateController.PlaybackEvent Event(
        string id, string kind, long at, long? position = null, long? duration = 9_000, bool? played = null)
        => new(id, kind, "episode", at, MetaId, VideoId, 1, 1, position, duration, played);

    private static string NewToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private sealed class Fixture(WebApplication app, HttpClient client, string token) : IAsyncDisposable
    {
        public HttpClient Client => client;

        public static async Task<Fixture> StartAsync(ApplicationDbContext db, string animeType = "TV",
            string imdbHint = MetaId, DbCommandInterceptor? interceptor = null, int? imdbSeason = null,
            int? imdbFromEpisode = null, int[]? nonImdbEpisodes = null,
            AnimeApiFixture? animeApi = null, TimeProvider? timeProvider = null)
        {
            await db.Database.ExecuteSqlRawAsync(AddAioStreamsExternalCompletion.InstallTriggerSql);
            var title = new MediaTitle
            {
                Id = Guid.NewGuid(), CanonicalTitle = "Owned demo", MediaKind = MediaKinds.Anime,
                PrimaryProgressDimension = MediaProgressDimensions.Episode,
                ReleaseStatusDimension = MediaProgressDimensions.Episode, SupportsEpisodeProgress = true
            };
            var episode = new MediaEpisode { Id = Guid.NewGuid(), MediaTitle = title, EpisodeNumber = 1 };
            var user = new User { Id = 1, UserName = "tracker-fixture", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            db.Add(episode);
            db.Add(new MediaEpisode { Id = Guid.NewGuid(), MediaTitle = title, EpisodeNumber = 2 });
            AddLink(db, title, "simkl", "anime:37089");
            db.Add(new MediaLibraryEntry
            {
                Id = Guid.NewGuid(), User = user, MediaTitle = title, Status = MediaLibraryStatuses.Current
            });
            await db.SaveChangesAsync();

            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var token = NewToken();
            db.Set<AioStreamsPairing>().Add(new() { UserId = user.Id, TokenHash = AioStreamsWatchState.HashToken(token) });
            await db.SaveChangesAsync();
            builder.Services.AddCors(cors => cors.AddPolicy(AioStreamsWatchState.ManifestCorsPolicy,
                policy => policy.AllowAnyOrigin().WithMethods("GET")));
            builder.Services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseNpgsql(db.Database.GetConnectionString(), npgsql => npgsql.EnableRetryOnFailure(1));
                if (interceptor is not null) options.AddInterceptors(interceptor);
            });
            builder.Services.AddSingleton(timeProvider ?? TimeProvider.System);
            builder.Services.AddSingleton<MediaLibraryEventHub>();
            builder.Services.Configure<AioStreamsOptions>(options => options.BaseUrl = "https://aio.test");
            builder.Services.AddMemoryCache();
            builder.Services.AddHttpClient(AioStreamsAnimeEnricher.HttpClientName).ConfigurePrimaryHttpMessageHandler(
                () => animeApi ?? new AnimeApiFixture(animeType, imdbHint, imdbSeason, imdbFromEpisode, nonImdbEpisodes));
            builder.Services.AddSingleton<AioStreamsAnimeEnricher>();
            builder.Services.AddIdentityCore<User>().AddEntityFrameworkStores<ApplicationDbContext>();
            builder.Services.AddAuthentication("Fixture")
                .AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>("Fixture", _ => { });
            builder.Services.AddAuthorization();
            builder.Services.AddControllers().AddApplicationPart(typeof(AioStreamsWatchStateController).Assembly);

            var app = builder.Build();
            app.UseRouting();
            app.UseCors();
            app.MapControllers();
            await app.StartAsync();
            return new Fixture(app, new HttpClient { BaseAddress = new Uri(app.Urls.Single()) }, token);
        }

        public string Authorize(string path) => QueryHelpers.AddQueryString(path, "token", token);

        public Task<JsonElement> PullAsync(string? since = null) => Client.GetFromJsonAsync<JsonElement>(
            since is null ? Authorize(PullRoute) : QueryHelpers.AddQueryString(Authorize(PullRoute), "since", since));

        public async Task ExpectPushAsync(AioStreamsWatchStateController.PlaybackEvent payload,
            HttpStatusCode expected, string path = PushRoute)
            => Assert.Equal(expected, await PushAsync(payload, path));

        public async Task<HttpStatusCode> PushAsync(AioStreamsWatchStateController.PlaybackEvent payload, string path = PushRoute)
        {
            using var response = await Client.PostAsJsonAsync(Authorize(path), payload);
            return response.StatusCode;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private sealed class FixtureAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Fixture-User", out var id))
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    // Both reads must finish against the empty table before either delivery can insert.
    private sealed class ReceiptReadBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _bothRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _count;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"AioStreamsWatchEvents\"", StringComparison.Ordinal)
                && command.CommandText.Contains("\"EventId\"", StringComparison.Ordinal))
            {
                var count = Interlocked.Increment(ref _count);
                if (count == 2) _bothRead.TrySetResult();
                if (count <= 2) await _bothRead.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }

            return result;
        }
    }

    private sealed class BeforeLegacyUpdate(Func<CancellationToken, Task> write) : DbCommandInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("UPDATE \"AioStreamsWatchEvents\"", StringComparison.Ordinal))
                await write(cancellationToken);

            return result;
        }
    }

    private sealed class RevokeBeforePairingLock(Func<CancellationToken, Task> revoke) : DbCommandInterceptor
    {
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("AioStreamsPairings", StringComparison.Ordinal)
                && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
                await revoke(cancellationToken);
            return result;
        }
    }

    private sealed class ImdbTvLinkLockAttempt : DbCommandInterceptor
    {
        public TaskCompletionSource<int> Pid { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"MediaProviderLinks\"", StringComparison.Ordinal)
                && command.CommandText.Contains("FOR SHARE", StringComparison.Ordinal))
                Pid.TrySetResult(((NpgsqlConnection)command.Connection!).ProcessID);
            return new(result);
        }
    }

    private sealed class AnimeApiFixture(string animeType, string imdbHint, int? imdbSeason,
        int? imdbFromEpisode, int[]? nonImdbEpisodes) : HttpMessageHandler
    {
        private int _requestCount;

        public int RequestCount => Volatile.Read(ref _requestCount);
        public int? ImdbFromEpisode { get; set; } = imdbFromEpisode;
        public bool Unavailable { get; set; }
        public bool ReturnEmpty { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            var query = QueryHelpers.ParseQuery(request.RequestUri!.Query);
            var known = query["idType"].ToString() switch
            {
                "simklId" => query["idValue"] == "37089",
                "kitsuId" => query["idValue"] == "1",
                var type => throw new InvalidOperationException($"Unexpected anime lookup type {type}")
            };
            if (Unavailable || query["idType"] == "kitsuId" && query["idValue"] == "9")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

            var data = known && !ReturnEmpty
                ? new { type = animeType, imdb = new { id = imdbHint, seasonNumber = imdbSeason, fromEpisode = ImdbFromEpisode, nonImdbEpisodes },
                    mappings = new { kitsuId = 1, simklId = 37089, anilistId = 1, malId = 1, imdbId = MetaId } }
                : null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { success = true, data })
            });
        }
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
