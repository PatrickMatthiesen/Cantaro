using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Cantaro.Api.Configuration;
using Cantaro.Api.Controllers;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class AioStreamsWatchStateTests
{
    private const string MetaId = "cantaro:owned-demo";
    private const string VideoId = "cantaro:owned-demo:1:1";
    private const string Route = AioStreamsWatchStateOptions.Route;
    private const string PushRoute = $"{Route}/watch_state/push/series/{VideoId}.json";
    private const string PullRoute = $"{Route}/watch_state/pull.json";
    private const long At = 1_794_000_000;

    [PostgresTheory]
    [InlineData("played", null)]
    [InlineData("stop", true)]
    public async Task DeliversResumeAndPlayedStateAndDeduplicatesConcurrentRetries(string completion, bool? played)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await using var fixture = await Fixture.StartAsync(schema.Db);
        using var manifestResponse = await fixture.Client.GetAsync(fixture.Authorize($"{Route}/manifest.json"));
        manifestResponse.EnsureSuccessStatusCode();
        Assert.True(manifestResponse.Headers.CacheControl?.NoStore);
        var manifest = await manifestResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, manifest.GetProperty("watchState").GetProperty("version").GetInt32());
        Assert.False(manifest.GetProperty("watchState").TryGetProperty("viewers", out _));

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
        await fixture.ExpectPushAsync(Event("late", "pause", At + 1, position: 1_500), HttpStatusCode.NoContent);
        var resume = await fixture.PullAsync();
        var item = Assert.Single(resume.GetProperty("items").EnumerateArray());
        Assert.Equal(MetaId, item.GetProperty("metaId").GetString());
        Assert.Equal(VideoId, item.GetProperty("videoId").GetString());
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

        await fixture.ExpectPushAsync(stop with { MetaId = "other" }, HttpStatusCode.NotFound);
        await fixture.ExpectPushAsync(stop with { Season = null }, HttpStatusCode.NotFound);
        await fixture.ExpectPushAsync(stop with { Episode = 2 }, HttpStatusCode.NotFound);
        await fixture.ExpectPushAsync(stop with { VideoId = MetaId }, HttpStatusCode.NotFound);
        await fixture.ExpectPushAsync(stop, HttpStatusCode.NotFound, PushRoute.Replace(VideoId, "unknown"));
        await fixture.ExpectPushAsync(stop with { Scope = "season" }, HttpStatusCode.BadRequest);

        fixture.App.Configuration[$"{AioStreamsWatchStateOptions.SectionName}:UserId"] = "2";
        ((IConfigurationRoot)fixture.App.Configuration).Reload();
        await fixture.ExpectPushAsync(stop, HttpStatusCode.NotFound);
        fixture.App.Configuration[$"{AioStreamsWatchStateOptions.SectionName}:TokenHash"] =
            AioStreamsWatchStateOptions.HashToken(NewToken());
        ((IConfigurationRoot)fixture.App.Configuration).Reload();
        await fixture.ExpectPushAsync(stop, HttpStatusCode.Unauthorized);
        Assert.Equal(0, await schema.Db.AioStreamsWatchEvents.CountAsync());
    }

    private static AioStreamsWatchStateController.PlaybackEvent Event(
        string id, string kind, long at, long? position = null, long? duration = 9_000, bool? played = null)
        => new(id, kind, "episode", at, MetaId, VideoId, 1, 1, position, duration, played);

    private static string NewToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private sealed class Fixture(WebApplication app, HttpClient client, string token) : IAsyncDisposable
    {
        public WebApplication App => app;
        public HttpClient Client => client;

        public static async Task<Fixture> StartAsync(ApplicationDbContext db)
        {
            var title = new MediaTitle
            {
                Id = Guid.NewGuid(), CanonicalTitle = "Owned demo", MediaKind = MediaKinds.Series,
                PrimaryProgressDimension = MediaProgressDimensions.Episode,
                ReleaseStatusDimension = MediaProgressDimensions.Episode, SupportsEpisodeProgress = true
            };
            var episode = new MediaEpisode { Id = Guid.NewGuid(), MediaTitle = title, EpisodeNumber = 1 };
            var user = new User { Id = 1, UserName = "tracker-fixture", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            db.Add(episode);
            db.Add(new MediaLibraryEntry
            {
                Id = Guid.NewGuid(), User = user, MediaTitle = title, Status = MediaLibraryStatuses.Current
            });
            await db.SaveChangesAsync();

            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var token = NewToken();
            var section = AioStreamsWatchStateOptions.SectionName;
            builder.Configuration[$"{section}:UserId"] = "1";
            builder.Configuration[$"{section}:MediaEpisodeId"] = episode.Id.ToString();
            builder.Configuration[$"{section}:MetaId"] = MetaId;
            builder.Configuration[$"{section}:VideoId"] = VideoId;
            builder.Configuration[$"{section}:Season"] = "1";
            builder.Configuration[$"{section}:Episode"] = "1";
            builder.Configuration[$"{section}:TokenHash"] = AioStreamsWatchStateOptions.HashToken(token);
            builder.Services.Configure<AioStreamsWatchStateOptions>(builder.Configuration.GetSection(section));
            builder.Services.AddDbContext<ApplicationDbContext>(options =>
                options.UseNpgsql(db.Database.GetConnectionString(), npgsql => npgsql.EnableRetryOnFailure(1)));
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddSingleton<MediaLibraryEventHub>();
            builder.Services.AddControllers().AddApplicationPart(typeof(AioStreamsWatchStateController).Assembly);

            var app = builder.Build();
            app.MapControllers();
            await app.StartAsync();
            return new Fixture(app, new HttpClient { BaseAddress = new Uri(app.Urls.Single()) }, token);
        }

        public string Authorize(string path) => QueryHelpers.AddQueryString(path, "token", token);

        public Task<JsonElement> PullAsync(string? since = null) => Client.GetFromJsonAsync<JsonElement>(
            since is null ? Authorize(PullRoute) : QueryHelpers.AddQueryString(Authorize(PullRoute), "since", since));

        public async Task ExpectPushAsync(AioStreamsWatchStateController.PlaybackEvent payload,
            HttpStatusCode expected, string path = PushRoute)
        {
            using var response = await Client.PostAsJsonAsync(Authorize(path), payload);
            Assert.Equal(expected, response.StatusCode);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
