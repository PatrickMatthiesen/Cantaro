using System.Net;
using System.Text;
using Cantaro.Api.Configuration;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class AnimeScheduleAvailabilitySyncServiceTests
{
    [Fact]
    public async Task SyncUserLibraryAsync_FinishedTitleUsesEpisodeCountAndTrackExistence()
    {
        var (db, connection, user, title) = await CreateLibraryAsync(12);
        await using var _ = connection;
        await using var __ = db;
        var handler = new StubAnimeScheduleHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/anime", StringComparison.Ordinal)
                ? AnimePageJson("Finished", 12, hasSub: true, hasDub: true)
                : throw new InvalidOperationException("Finished titles should not require timetable history."));
        var service = CreateService(db, handler);

        var changed = await service.SyncUserLibraryAsync(user.Id, CancellationToken.None);

        var episodes = await db.MediaEpisodes.OrderBy(episode => episode.EpisodeNumber).ToListAsync();
        Assert.Equal(24, changed);
        Assert.Equal(12, episodes.Count);
        Assert.All(episodes, episode => Assert.Contains("en", episode.AvailableSubtitleLanguageCodes));
        Assert.All(episodes, episode => Assert.Contains("en", episode.AvailableAudioLanguageCodes));
        Assert.Single(handler.Requests);
        Assert.Contains("mt=any", handler.Requests[0]);
        Assert.Contains("anilist-ids=123", handler.Requests[0]);
        Assert.Equal(title.Id, episodes[0].MediaTitleId);
    }

    [Fact]
    public async Task SyncUserLibraryAsync_FinishedTitleDoesNotInventMissingDub()
    {
        var (db, connection, user, _) = await CreateLibraryAsync(12);
        await using var _ = connection;
        await using var __ = db;
        var handler = new StubAnimeScheduleHandler(_ =>
            AnimePageJson("Finished", 12, hasSub: true, hasDub: false));
        var service = CreateService(db, handler);

        var changed = await service.SyncUserLibraryAsync(user.Id, CancellationToken.None);

        var episodes = await db.MediaEpisodes.OrderBy(episode => episode.EpisodeNumber).ToListAsync();
        Assert.Equal(12, changed);
        Assert.Equal(12, episodes.Count);
        Assert.All(episodes, episode => Assert.Contains("en", episode.AvailableSubtitleLanguageCodes));
        Assert.All(episodes, episode => Assert.Empty(episode.AvailableAudioLanguageCodes));
    }

    [Fact]
    public async Task SyncUserLibraryAsync_RunningTitleUsesAiredAndUpcomingTrackCounts()
    {
        var (db, connection, user, _) = await CreateLibraryAsync(12);
        await using var _ = connection;
        await using var __ = db;
        var now = DateTimeOffset.UtcNow;
        var handler = new StubAnimeScheduleHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/anime", StringComparison.Ordinal)
                ? AnimePageJson("Ongoing", 12, hasSub: true, hasDub: true)
                : $$"""
                    [
                      {"route":"example-anime","episodeNumber":9,"airType":"raw","airingStatus":"unaired","episodeDate":"{{now.AddDays(2):O}}"},
                      {"route":"example-anime","episodeNumber":8,"airType":"sub","airingStatus":"unaired","episodeDate":"{{now.AddDays(2):O}}"},
                      {"route":"example-anime","episodeNumber":5,"airType":"dub","airingStatus":"aired","episodeDate":"{{now.AddMinutes(-5):O}}"}
                    ]
                    """);
        var service = CreateService(db, handler);

        var changed = await service.SyncUserLibraryAsync(user.Id, CancellationToken.None);

        var episodes = await db.MediaEpisodes.OrderBy(episode => episode.EpisodeNumber).ToListAsync();
        Assert.Equal(12, changed);
        Assert.Equal(7, episodes.Count);
        Assert.All(episodes, episode => Assert.Contains("en", episode.AvailableSubtitleLanguageCodes));
        Assert.All(episodes.Take(5), episode => Assert.Contains("en", episode.AvailableAudioLanguageCodes));
        Assert.All(episodes.Skip(5), episode => Assert.Empty(episode.AvailableAudioLanguageCodes));
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("/timetables/all?", handler.Requests[1]);
        Assert.DoesNotContain("year=", handler.Requests[1]);
    }

    [Fact]
    public async Task SyncUserLibraryAsync_RunningTitleWalksBackUntilMissingTrackIsFound()
    {
        var (db, connection, user, _) = await CreateLibraryAsync(12);
        await using var _ = connection;
        await using var __ = db;
        var timetableRequestCount = 0;
        var now = DateTimeOffset.UtcNow;
        var handler = new StubAnimeScheduleHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/anime", StringComparison.Ordinal))
            {
                return AnimePageJson("Ongoing", 12, hasSub: true, hasDub: false);
            }

            timetableRequestCount++;
            return timetableRequestCount < 3
                ? "[]"
                : $$"""
                    [{"route":"example-anime","episodeNumber":6,"airType":"sub","airingStatus":"aired","episodeDate":"{{now.AddDays(-14):O}}"}]
                    """;
        });
        var service = CreateService(db, handler);

        var changed = await service.SyncUserLibraryAsync(user.Id, CancellationToken.None);

        var episodes = await db.MediaEpisodes.OrderBy(episode => episode.EpisodeNumber).ToListAsync();
        Assert.Equal(6, changed);
        Assert.Equal(6, episodes.Count);
        Assert.All(episodes, episode => Assert.Contains("en", episode.AvailableSubtitleLanguageCodes));
        Assert.Equal(3, timetableRequestCount);
        Assert.Contains("year=", handler.Requests[^1]);
        Assert.Contains("week=", handler.Requests[^1]);
    }

    private static AnimeScheduleAvailabilitySyncService CreateService(
        ApplicationDbContext db,
        StubAnimeScheduleHandler handler)
    {
        var client = new AnimeScheduleApiClient(
            new HttpClient(handler),
            Options.Create(new AnimeScheduleOptions { Token = "test-token" }));
        return new AnimeScheduleAvailabilitySyncService(
            db,
            client,
            NullLogger<AnimeScheduleAvailabilitySyncService>.Instance);
    }

    private static async Task<(ApplicationDbContext Db, SqliteConnection Connection, User User, MediaTitle Title)>
        CreateLibraryAsync(int episodeCount)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var now = DateTimeOffset.UtcNow;
        var user = TestUserFactory.Create(900, "availability@example.com");
        var title = new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = "Example Anime",
            MediaKind = MediaKinds.Anime,
            EpisodeCount = episodeCount,
            SupportsEpisodeProgress = true,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Users.Add(user);
        db.MediaTitles.Add(title);
        db.MediaProviderLinks.Add(new MediaProviderLink
        {
            Id = Guid.NewGuid(), MediaTitleId = title.Id, Provider = "anilist", ExternalId = "123",
            LinkSource = MediaMappingSources.Imported, CreatedAt = now, UpdatedAt = now
        });
        db.MediaLibraryEntries.Add(new MediaLibraryEntry
        {
            Id = Guid.NewGuid(), UserId = user.Id, MediaTitleId = title.Id,
            Status = MediaLibraryStatuses.Current,
            CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();
        return (db, connection, user, title);
    }

    private static string AnimePageJson(
        string status,
        int episodes,
        bool hasSub,
        bool hasDub)
    {
        var subTime = hasSub ? "2024-04-05T15:30:00Z" : "0001-01-01T00:00:00Z";
        var dubPremier = hasDub ? "2024-04-19T00:00:00Z" : "0001-01-01T00:00:00Z";
        var dubTime = hasDub ? "2024-04-23T21:30:00Z" : "0001-01-01T00:00:00Z";
        return $$"""
            {"page":1,"totalAmount":1,"anime":[{
              "route":"example-anime",
              "status":"{{status}}",
              "episodes":{{episodes}},
              "premier":"2024-04-05T00:00:00Z",
              "subPremier":"0001-01-01T00:00:00Z",
              "dubPremier":"{{dubPremier}}",
              "jpnTime":"2024-02-21T14:00:00Z",
              "subTime":"{{subTime}}",
              "dubTime":"{{dubTime}}",
              "websites":{"aniList":"anilist.co/anime/123/example"}
            }]}
            """;
    }

    private sealed class StubAnimeScheduleHandler(Func<HttpRequestMessage, string> responseFactory)
        : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("test-token", request.Headers.Authorization?.Parameter);
            Requests.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseFactory(request), Encoding.UTF8, "application/json")
            });
        }
    }
}
