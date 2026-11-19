using System.Data.Common;
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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class AioStreamsWatchStateTests
{
    private const string MetaId = "tt0213338";
    private const string VideoId = "kitsu:1:1";
    private const string Route = AioStreamsWatchStateOptions.Route;
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
        using var manifestResponse = await fixture.Client.GetAsync(fixture.Authorize($"{Route}/manifest.json"));
        manifestResponse.EnsureSuccessStatusCode();
        Assert.True(manifestResponse.Headers.CacheControl?.NoStore);
        var manifest = await manifestResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, manifest.GetProperty("watchState").GetProperty("version").GetInt32());
        Assert.False(manifest.GetProperty("watchState").TryGetProperty("viewers", out _));
        Assert.Equal(new[] { "anime", "series" }, manifest.GetProperty("types").Deserialize<string[]>());

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

        fixture.App.Configuration[$"{AioStreamsWatchStateOptions.SectionName}:UserId"] = "2";
        ((IConfigurationRoot)fixture.App.Configuration).Reload();
        await fixture.ExpectPushAsync(stop, HttpStatusCode.NotFound);
        fixture.App.Configuration[$"{AioStreamsWatchStateOptions.SectionName}:TokenHash"] =
            AioStreamsWatchStateOptions.HashToken(NewToken());
        ((IConfigurationRoot)fixture.App.Configuration).Reload();
        await fixture.ExpectPushAsync(stop, HttpStatusCode.Unauthorized);
        Assert.Equal(0, await schema.Db.AioStreamsWatchEvents.CountAsync());
    }

    [PostgresFact]
    public async Task ResolvesFromKitsuEpisodeAndOnlyAdvancesConsecutiveCompletions()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await using var fixture = await Fixture.StartAsync(schema.Db);
        var second = Event("second", "played", At) with { VideoId = "kitsu:1:2", Episode = 11, Season = null };
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
        public WebApplication App => app;
        public HttpClient Client => client;

        public static async Task<Fixture> StartAsync(ApplicationDbContext db, string animeType = "TV",
            string imdbHint = MetaId, DbCommandInterceptor? interceptor = null)
        {
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
            var section = AioStreamsWatchStateOptions.SectionName;
            builder.Configuration[$"{section}:UserId"] = "1";
            builder.Configuration[$"{section}:TokenHash"] = AioStreamsWatchStateOptions.HashToken(token);
            builder.Services.Configure<AioStreamsWatchStateOptions>(builder.Configuration.GetSection(section));
            builder.Services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseNpgsql(db.Database.GetConnectionString(), npgsql => npgsql.EnableRetryOnFailure(1));
                if (interceptor is not null) options.AddInterceptors(interceptor);
            });
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddSingleton<MediaLibraryEventHub>();
            builder.Services.Configure<AioStreamsOptions>(options => options.BaseUrl = "https://aio.test");
            builder.Services.AddMemoryCache();
            builder.Services.AddHttpClient(AioStreamsAnimeEnricher.HttpClientName).ConfigurePrimaryHttpMessageHandler(
                () => new AnimeApiFixture(animeType, imdbHint));
            builder.Services.AddSingleton<AioStreamsAnimeEnricher>();
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

    private sealed class AnimeApiFixture(string animeType, string imdbHint) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = QueryHelpers.ParseQuery(request.RequestUri!.Query);
            Assert.Equal("kitsuId", query["idType"].ToString());
            if (query["idValue"] == "9") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

            var data = query["idValue"] == "1"
                ? new { type = animeType, imdb = new { id = imdbHint },
                    mappings = new { kitsuId = 1, simklId = 37089, anilistId = 1, malId = 1, imdbId = MetaId } }
                : null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { success = true, data })
            });
        }
    }
}
