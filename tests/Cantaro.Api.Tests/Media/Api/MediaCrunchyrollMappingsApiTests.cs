using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Cantaro.Api.Controllers;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using SeasonMappingDto = Cantaro.Api.Controllers.MediaCrunchyrollMappingsController.MediaProviderSeasonMappingDto;

namespace Cantaro.Api.Tests;

public sealed class MediaCrunchyrollMappingsApiTests
{
    [Fact]
    public void ControllerRequiresAuthentication()
    {
        Assert.Single(typeof(MediaCrunchyrollMappingsController).GetCustomAttributes(typeof(AuthorizeAttribute), true));
    }

    [Fact]
    public async Task SeasonPutOverridesAutomaticMappingAndValidatesBounds()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Other.IsCompletionOnly = false;
        fixture.Other.SupportsEpisodeProgress = true;
        fixture.Other.PrimaryProgressDimension = MediaProgressDimensions.Episode;
        await fixture.Db.SaveChangesAsync();
        var original = await fixture.Mappings.EstablishAsync("crunchyroll", "SERIES", "SEASON2", 2,
            fixture.Other.Id, 0, "automatic", 1m, default, episodeStart: 13, episodeEnd: 24);

        var result = await fixture.Controller.PutSeason(fixture.Title.Id, SeasonRequest(13, 24), default);
        var edited = Assert.IsType<SeasonMappingDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(original!.Id, edited.Id);
        Assert.Equal(fixture.Title.Id, edited.MediaTitleId);
        Assert.Equal("manual", edited.MappingSource);
        Assert.Equal(-12, edited.EpisodeOffset);
        Assert.IsType<BadRequestObjectResult>(
            await fixture.Controller.PutSeason(fixture.Title.Id, SeasonRequest(24, 36), default));
        Assert.IsType<BadRequestObjectResult>(
            await fixture.Controller.PutSeason(fixture.Title.Id, SeasonRequest(24, 13), default));
        Assert.IsType<BadRequestObjectResult>(
            await fixture.Controller.PutSeason(fixture.Title.Id, SeasonRequest(0, 12), default));
        Assert.IsType<NotFoundResult>(
            await fixture.Controller.PutSeason(Guid.NewGuid(), SeasonRequest(13, 24), default));
        Assert.Equal(edited.Id, (await fixture.Db.MediaProviderSeasonMappings.SingleAsync()).Id);
    }

    [Theory]
    [InlineData(1, 24, 1, 12, false, true)]
    [InlineData(1, 24, 7, 12, true, true)]
    [InlineData(5, 10, 1, 12, false, false)]
    [InlineData(null, null, 1, 12, false, true)]
    public async Task SeasonPutCarvesAutomaticCoverage(int? autoStart, int? autoEnd, int manualStart,
        int manualEnd, bool keepLeft, bool keepRight)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Other.IsCompletionOnly = false;
        fixture.Other.SupportsEpisodeProgress = true;
        fixture.Other.PrimaryProgressDimension = MediaProgressDimensions.Episode;
        fixture.Other.EpisodeCount = 24;
        await fixture.Db.SaveChangesAsync();
        var observedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await fixture.Mappings.EstablishAsync("crunchyroll", "SERIES", null, 2, fixture.Other.Id, 0,
            MediaProviderSeasonMappingSources.Automatic, 0.9m, default, episodeStart: autoStart, episodeEnd: autoEnd,
            observedAt: observedAt);

        var request = SeasonRequest(manualStart, manualEnd, offset: 0);
        Assert.IsType<OkObjectResult>(await fixture.Controller.PutSeason(fixture.Title.Id, request, default));

        var rows = await fixture.Db.MediaProviderSeasonMappings.ToListAsync();
        var manual = Assert.Single(rows, row => row.MappingSource == MediaProviderSeasonMappingSources.Manual);
        Assert.Equal((manualStart, manualEnd, fixture.Title.Id),
            (manual.ProviderEpisodeStart, manual.ProviderEpisodeEnd, manual.MediaTitleId));
        var leftovers = rows.Where(row => row.MappingSource == MediaProviderSeasonMappingSources.Automatic)
            .OrderBy(row => row.ProviderEpisodeStart).ToArray();
        Assert.Equal((keepLeft ? 1 : 0) + (keepRight ? 1 : 0), leftovers.Length);
        Assert.All(leftovers, row =>
        {
            Assert.Equal(fixture.Other.Id, row.MediaTitleId);
            Assert.Equal(observedAt, row.LastObservedAt);
        });
        if (keepLeft)
        {
            Assert.Equal((autoStart, (int?)manualStart - 1),
                (leftovers[0].ProviderEpisodeStart, leftovers[0].ProviderEpisodeEnd));
        }
        if (keepRight)
        {
            Assert.Equal(((int?)manualEnd + 1, autoEnd ?? fixture.Other.EpisodeCount),
                (leftovers[^1].ProviderEpisodeStart, leftovers[^1].ProviderEpisodeEnd));
        }

        var overlap = SeasonRequest(10, 12, offset: 0);
        Assert.IsType<BadRequestObjectResult>(await fixture.Controller.PutSeason(fixture.Other.Id, overlap, default));
        Assert.Equal(rows.Count, await fixture.Db.MediaProviderSeasonMappings.CountAsync());
    }

    [Theory]
    [InlineData(0, 1, 12, 13, 24, -12)]
    [InlineData(-12, 13, 24, 1, 12, 0)]
    public async Task SeasonPutRemovesAutomaticCoverageOutsideCanonicalRange(int offset, int start, int end,
        int otherStart, int otherEnd, int otherOffset)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Other.IsCompletionOnly = false;
        fixture.Other.Format = MediaFormats.Tv;
        fixture.Other.SupportsEpisodeProgress = true;
        fixture.Other.PrimaryProgressDimension = MediaProgressDimensions.Episode;
        fixture.Other.EpisodeCount = 12;
        await fixture.Db.SaveChangesAsync();
        Assert.NotNull(await fixture.Mappings.EstablishAsync("crunchyroll", "SERIES", "SEASON2", 2,
            fixture.Title.Id, offset, "automatic", 1m, default));

        Assert.IsType<OkObjectResult>(
            await fixture.Controller.PutSeason(fixture.Title.Id, SeasonRequest(start, end, offset), default));
        Assert.Equal("manual", (await fixture.Db.MediaProviderSeasonMappings.SingleAsync()).MappingSource);
        Assert.NotNull(await fixture.Mappings.EstablishAsync("crunchyroll", "SERIES", "SEASON2", 2,
            fixture.Other.Id, otherOffset, "automatic", 1m, default, episodeStart: otherStart, episodeEnd: otherEnd));
    }

    [Theory]
    [InlineData(MediaKinds.Manga, MediaFormats.Tv, MediaProgressDimensions.Episode)]
    [InlineData(MediaKinds.Anime, MediaFormats.Novel, MediaProgressDimensions.Episode)]
    [InlineData(MediaKinds.Anime, MediaFormats.OneShot, MediaProgressDimensions.Episode)]
    [InlineData(MediaKinds.Anime, MediaFormats.Tv, MediaProgressDimensions.Chapter)]
    [InlineData(MediaKinds.Anime, MediaFormats.Tv, MediaProgressDimensions.Volume)]
    public async Task MappingWritesRejectIncompatibleCrunchyrollTitles(string kind, string format, string dimension)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Title.MediaKind = kind;
        fixture.Title.Format = format;
        fixture.Title.PrimaryProgressDimension = dimension;
        fixture.Other.MediaKind = kind;
        fixture.Other.Format = format;
        fixture.Other.PrimaryProgressDimension = dimension;
        var episode = await fixture.Db.MediaProviderEpisodes.SingleAsync();
        episode.ProviderSeriesId = null;
        episode.ProviderSeasonId = null;
        episode.ProviderSeasonNumber = null;
        await fixture.Db.SaveChangesAsync();

        Assert.IsType<BadRequestObjectResult>(
            await fixture.Controller.PutSeason(fixture.Title.Id, SeasonRequest(13, 24), default));
        Assert.IsType<BadRequestObjectResult>(await fixture.Controller.PutEpisode(fixture.Title.Id, "EPISODE",
            new() { CanonicalEpisodeNumber = 1 }, default));
        Assert.IsType<BadRequestObjectResult>(
            await fixture.Controller.PutEpisode(fixture.Other.Id, "EPISODE", new(), default));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Mappings.EstablishDirectAsync(
            "crunchyroll", "EPISODE", fixture.Title.Id, 1, "automatic", default));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Mappings.EstablishDirectAsync(
            "crunchyroll", "EPISODE", fixture.Other.Id, null, "manual", default));
        Assert.Empty(fixture.Db.MediaProviderSeasonMappings);
        Assert.Empty(fixture.Db.MediaProviderEpisodeMappings);
    }

    [Theory]
    [InlineData(1, 12, 12)]
    [InlineData(null, null, 12)]
    [InlineData(null, 12, -12)]
    [InlineData(1, 12, -1)]
    [InlineData(1, 12, 1)]
    public async Task SeasonPutRejectsInvalidCanonicalRangeAndPreservesAutomaticMapping(int? start, int? end, int offset)
    {
        await using var fixture = await Fixture.CreateAsync();
        var original = await fixture.Mappings.EstablishAsync("crunchyroll", "SERIES", "SEASON2", 2,
            fixture.Title.Id, 0, "automatic", 1m, default);

        Assert.IsType<BadRequestObjectResult>(await fixture.Controller.PutSeason(fixture.Title.Id, new()
        {
            ProviderSeriesId = "series", ProviderSeasonId = "season2", ProviderSeasonNumber = 2,
            ProviderEpisodeStart = start, ProviderEpisodeEnd = end, EpisodeOffset = offset
        }, default));

        var preserved = await fixture.Db.MediaProviderSeasonMappings.SingleAsync();
        Assert.Equal(original!.Id, preserved.Id);
        Assert.Equal("automatic", preserved.MappingSource);
        Assert.Equal(0, preserved.EpisodeOffset);
        Assert.Null(preserved.ProviderEpisodeStart);
        Assert.Null(preserved.ProviderEpisodeEnd);
    }

    [Theory]
    [InlineData("ova")]
    [InlineData("OVA")]
    [InlineData("movie")]
    [InlineData("Movie")]
    public async Task EpisodePutAllowsDirectMappingsForOvaAndMovie(string format)
    {
        await using var fixture = await Fixture.CreateAsync(format);
        await fixture.Mappings.EstablishDirectAsync("crunchyroll", "EPISODE", fixture.Other.Id, null, "automatic", default);
        Assert.IsType<NoContentResult>(await fixture.Controller.PutEpisode(fixture.Title.Id, "episode",
            new() { CanonicalEpisodeNumber = 1 }, default));
        var mapping = await fixture.Db.MediaProviderEpisodeMappings.SingleAsync();
        Assert.Equal(fixture.Title.Id, mapping.MediaTitleId);
        Assert.Equal(1, mapping.CanonicalEpisodeNumber);
        Assert.Equal("manual", mapping.MappingSource);
    }

    [Fact]
    public async Task EpisodePutEnforcesEpisodeCountProgressTypeAndSeasonMapping()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.IsType<BadRequestObjectResult>(await fixture.Controller.PutEpisode(fixture.Title.Id, "EPISODE",
            new() { CanonicalEpisodeNumber = 13 }, default));
        Assert.IsType<BadRequestObjectResult>(await fixture.Controller.PutEpisode(fixture.Title.Id, "EPISODE",
            new() { CanonicalEpisodeNumber = 1 }, default));
        Assert.IsType<BadRequestObjectResult>(await fixture.Controller.PutEpisode(fixture.Title.Id, "EPISODE", new(), default));
        Assert.IsType<BadRequestObjectResult>(await fixture.Controller.PutEpisode(fixture.Other.Id, "EPISODE",
            new() { CanonicalEpisodeNumber = 1 }, default));
        Assert.IsType<NotFoundResult>(await fixture.Controller.PutEpisode(fixture.Other.Id, "MISSING", new(), default));
        Assert.Empty(fixture.Db.MediaProviderEpisodeMappings);
        Assert.IsType<NoContentResult>(await fixture.Controller.PutEpisode(fixture.Other.Id, "EPISODE", new(), default));
        Assert.Null((await fixture.Db.MediaProviderEpisodeMappings.SingleAsync()).CanonicalEpisodeNumber);
    }

    [Fact]
    public async Task EpisodePutChecksEligibilityAfterConcurrentSeasonCoordinatesAreCommitted()
    {
        var concurrentScan = new PublishSeasonCoordinates();
        await using var fixture = await Fixture.CreateAsync(concurrentScan: concurrentScan);
        await fixture.Db.MediaProviderEpisodes.ExecuteUpdateAsync(setters => setters
            .SetProperty(row => row.ProviderSeasonId, (string?)null)
            .SetProperty(row => row.ProviderSeasonNumber, (int?)null));
        await fixture.Mappings.EstablishAsync("crunchyroll", "SERIES", "SEASON2", 2, fixture.Title.Id, -12,
            "automatic", 1m, default, episodeStart: 13, episodeEnd: 24);
        var request = new MediaCrunchyrollMappingsController.EpisodeMappingRequest { CanonicalEpisodeNumber = 1 };
        Assert.IsType<NoContentResult>(await fixture.Controller.PutEpisode(fixture.Title.Id, "EPISODE", request, default));
        Assert.IsType<NoContentResult>(await fixture.Controller.DeleteEpisode(fixture.Title.Id, "EPISODE", default));

        concurrentScan.Pending = true;
        var result = Assert.IsType<BadRequestObjectResult>(
            await fixture.Controller.PutEpisode(fixture.Title.Id, "EPISODE", request, default));
        Assert.Contains("Use a season mapping", JsonSerializer.Serialize(result.Value));
        Assert.False(concurrentScan.Pending);
        Assert.Empty(await fixture.Db.MediaProviderEpisodeMappings.ToListAsync());
        Assert.IsType<NoContentResult>(await fixture.Controller.PutEpisode(fixture.Other.Id, "EPISODE", new(), default));
    }

    [Fact]
    public async Task ListAndDeleteAreScopedToTheRequestedTitle()
    {
        await using var fixture = await Fixture.CreateAsync("OVA");
        var result = await fixture.Controller.PutSeason(fixture.Title.Id, SeasonRequest(13, 24), default);
        var season = Assert.IsType<SeasonMappingDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.IsType<NoContentResult>(await fixture.Controller.PutEpisode(fixture.Title.Id, "EPISODE",
            new() { CanonicalEpisodeNumber = 1 }, default));
        await fixture.Mappings.EstablishDirectAsync("crunchyroll", "OTHER", fixture.Other.Id, null, "manual", default);

        var list = Assert.IsType<OkObjectResult>(await fixture.Controller.List(fixture.Title.Id, default));
        var body = JsonSerializer.SerializeToElement(list.Value);
        Assert.Equal(season.Id, Assert.Single(body.GetProperty("seasons").EnumerateArray()).GetProperty("Id").GetGuid());
        Assert.Equal("EPISODE", Assert.Single(body.GetProperty("episodes").EnumerateArray()).GetProperty("ProviderEpisodeId").GetString());
        Assert.IsType<NotFoundResult>(await fixture.Controller.List(Guid.NewGuid(), default));
        Assert.IsType<NotFoundResult>(await fixture.Controller.DeleteSeason(fixture.Other.Id, season.Id, default));
        Assert.IsType<NotFoundResult>(await fixture.Controller.DeleteEpisode(fixture.Other.Id, "EPISODE", default));

        Assert.IsType<NoContentResult>(await fixture.Controller.DeleteSeason(fixture.Title.Id, season.Id, default));
        Assert.IsType<NoContentResult>(await fixture.Controller.DeleteEpisode(fixture.Title.Id, "episode", default));
        Assert.IsType<NotFoundResult>(await fixture.Controller.DeleteSeason(fixture.Title.Id, season.Id, default));
        Assert.IsType<NotFoundResult>(await fixture.Controller.DeleteEpisode(fixture.Title.Id, "episode", default));
        Assert.Empty(fixture.Db.MediaProviderSeasonMappings);
        Assert.Equal(fixture.Other.Id, (await fixture.Db.MediaProviderEpisodeMappings.SingleAsync()).MediaTitleId);
    }

    [Fact]
    public async Task MappingResponsesExposeOnlyConfigurationToAnotherAuthenticatedAccount()
    {
        await using var fixture = await Fixture.CreateAsync("OVA");
        var first = TestUserFactory.Create(41, "mapping-owner@example.test");
        var second = TestUserFactory.Create(42, "mapping-reader@example.test");
        fixture.Db.Users.AddRange(first, second);
        await fixture.Db.SaveChangesAsync();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(fixture.Db);
        builder.Services.AddSingleton(fixture.Mappings);
        builder.Services.AddControllers().AddApplicationPart(typeof(MediaCrunchyrollMappingsController).Assembly);
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();

        var cookie = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        using var client = new HttpClient(new HttpClientHandler { UseCookies = false }) { BaseAddress = new(app.Urls.Single()) };
        var route = $"/api/media/titles/{fixture.Title.Id}/crunchyroll-mappings";
        Authenticate(first.Id);
        using var put = await client.PutAsJsonAsync(route + "/seasons", SeasonRequest(13, 24));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        string[] seasonFields = ["episodeOffset", "id", "mappingSource", "mediaTitleId", "normalizedSeasonTitle",
            "provider", "providerEpisodeEnd", "providerEpisodeStart", "providerSeasonId", "providerSeasonNumber", "providerSeriesId"];
        AssertFields(await put.Content.ReadFromJsonAsync<JsonElement>(), seasonFields);

        var watchTime = DateTimeOffset.UtcNow.AddMinutes(-1);
        Assert.True(await fixture.Mappings.EstablishDirectAsync("crunchyroll", "EPISODE", fixture.Title.Id, 1,
            "automatic", default, watchTime));
        await fixture.Db.MediaProviderSeasonMappings.ExecuteUpdateAsync(
            setters => setters.SetProperty(row => row.LastObservedAt, watchTime));
        Authenticate(second.Id);
        var list = await client.GetFromJsonAsync<JsonElement>(route);
        AssertFields(Assert.Single(list.GetProperty("seasons").EnumerateArray()), seasonFields);
        AssertFields(Assert.Single(list.GetProperty("episodes").EnumerateArray()),
            ["episodeNumber", "mappingSource", "mediaTitleId", "provider", "providerEpisodeId"]);

        void Authenticate(int userId)
        {
            const string scheme = CookieAuthenticationDefaults.AuthenticationScheme;
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], scheme);
            var ticket = cookie.TicketDataFormat.Protect(new AuthenticationTicket(new ClaimsPrincipal(identity), scheme));
            client.DefaultRequestHeaders.Remove("Cookie");
            client.DefaultRequestHeaders.Add("Cookie", $"{cookie.Cookie.Name}={ticket}");
        }

        static void AssertFields(JsonElement value, string[] expected) =>
            Assert.Equal(expected, value.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    private static MediaCrunchyrollMappingsController.SeasonMappingRequest SeasonRequest(int start, int end, int offset = -12) =>
        new()
        {
            ProviderSeriesId = "series", ProviderSeasonId = "season2", ProviderSeasonNumber = 2,
            ProviderEpisodeStart = start, ProviderEpisodeEnd = end, EpisodeOffset = offset
        };

    private sealed class PublishSeasonCoordinates : DbTransactionInterceptor
    {
        public bool Pending { get; set; }

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            if (Pending)
            {
                Pending = false;
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    UPDATE "MediaProviderEpisodes"
                    SET "ProviderSeasonId" = 'SEASON2', "ProviderSeasonNumber" = 2
                    WHERE "Provider" = 'crunchyroll' AND "ProviderEpisodeId" = 'EPISODE'
                    """;
                Assert.Equal(1, await command.ExecuteNonQueryAsync(cancellationToken));
            }
            return result;
        }
    }

    private sealed class Fixture(SqliteConnection connection, ApplicationDbContext db, MediaTitle title, MediaTitle other)
        : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = db;
        public MediaTitle Title { get; } = title;
        public MediaTitle Other { get; } = other;
        public MediaProviderSeasonMappingService Mappings { get; } = new(db, NullLogger<MediaProviderSeasonMappingService>.Instance);
        public MediaCrunchyrollMappingsController Controller => new(Db, Mappings);

        public static async Task<Fixture> CreateAsync(string format = "tv", PublishSeasonCoordinates? concurrentScan = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection);
            if (concurrentScan is not null)
            {
                options.AddInterceptors(concurrentScan);
            }
            var db = new ApplicationDbContext(options.Options);
            await db.Database.EnsureCreatedAsync();
            var title = new MediaTitle
            {
                Id = Guid.NewGuid(), CanonicalTitle = "Series", MediaKind = MediaKinds.Anime,
                Format = format, EpisodeCount = 12, SupportsEpisodeProgress = true,
                PrimaryProgressDimension = MediaProgressDimensions.Episode,
                ReleaseStatusDimension = MediaProgressDimensions.Episode
            };
            var other = new MediaTitle
            {
                Id = Guid.NewGuid(), CanonicalTitle = "Film", MediaKind = MediaKinds.Anime,
                Format = "movie", IsCompletionOnly = true,
                PrimaryProgressDimension = MediaProgressDimensions.CompletionOnly,
                ReleaseStatusDimension = MediaProgressDimensions.CompletionOnly
            };
            db.MediaTitles.AddRange(title, other);
            db.MediaProviderEpisodes.Add(new MediaProviderEpisode
            {
                Provider = "crunchyroll", ProviderEpisodeId = "EPISODE", ProviderUrlPath = "/watch/EPISODE",
                ProviderSeriesId = "SERIES", ProviderSeasonId = "SEASON2", ProviderSeasonNumber = 2,
                ProviderEpisodeNumber = 13, SeenCount = 1
            });
            await db.SaveChangesAsync();
            return new Fixture(connection, db, title, other);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
