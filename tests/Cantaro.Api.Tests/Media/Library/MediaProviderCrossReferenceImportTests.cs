using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class MediaProviderCrossReferenceImportTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AniListAndMalImports_ReuseOneTitleAndViewerEntry_InEitherOrder(bool malFirst)
    {
        await using var fixture = await ImportFixture.CreateAsync();
        var first = malFirst ? fixture.MalImport : fixture.AniListImport;
        var firstAccount = malFirst ? fixture.MalAccount : fixture.AniListAccount;
        var second = malFirst ? fixture.AniListImport : fixture.MalImport;
        var secondAccount = malFirst ? fixture.AniListAccount : fixture.MalAccount;

        await fixture.Service.ImportAsync(fixture.User.Id, firstAccount, first, CancellationToken.None);
        await fixture.Service.ImportAsync(fixture.User.Id, secondAccount, second, CancellationToken.None);

        Assert.Equal(1, await fixture.Db.MediaTitles.CountAsync());
        Assert.Equal(1, await fixture.Db.MediaLibraryEntries.CountAsync());
        Assert.Equal(2, await fixture.Db.MediaProviderLinks.CountAsync());
        Assert.Equal(2, await fixture.Db.MediaLibraryProviderBindings.CountAsync());
        Assert.Contains(await fixture.Db.MediaProviderLinks.ToListAsync(),
            link => link.Provider == "anilist" && link.ExternalId == "154587");
        Assert.Contains(await fixture.Db.MediaProviderLinks.ToListAsync(),
            link => link.Provider == "myanimelist" && link.ExternalId == "anime:52991");
        AssertAniListMetadata(await fixture.Db.MediaTitles.SingleAsync());
    }

    [Fact]
    public async Task RepeatedMalRefresh_DoesNotReplaceAniListCanonicalMetadata()
    {
        await using var fixture = await ImportFixture.CreateAsync();
        await fixture.Service.ImportAsync(
            fixture.User.Id,
            fixture.AniListAccount,
            fixture.AniListImport,
            CancellationToken.None);
        await fixture.Service.ImportAsync(
            fixture.User.Id,
            fixture.MalAccount,
            fixture.MalImport,
            CancellationToken.None);

        var malItem = Assert.Single(fixture.MalImport.Items);
        malItem.Title = "MAL refreshed title";
        malItem.NativeTitle = "MAL refreshed native title";
        malItem.OriginalTitle = "MAL refreshed original title";
        malItem.Synonyms = ["MAL refreshed synonym"];
        malItem.Synopsis = "MAL refreshed synopsis";
        malItem.Format = "MOVIE";
        malItem.PosterUrl = "https://mal.example/refreshed-poster.jpg";
        malItem.BackgroundUrl = null;
        malItem.StartYear = 2025;
        malItem.EpisodeCount = 31;
        malItem.ReleasedCount = 31;
        malItem.TotalKnownCount = 31;
        malItem.NextReleaseAt = null;
        malItem.NextReleaseLabel = null;
        malItem.LastRemoteUpdateAt = new DateTimeOffset(2026, 8, 23, 13, 0, 0, TimeSpan.Zero);
        fixture.MalImport.ImportedAt = new DateTimeOffset(2026, 8, 23, 13, 0, 0, TimeSpan.Zero);

        var result = await fixture.Service.ImportAsync(
            fixture.User.Id,
            fixture.MalAccount,
            fixture.MalImport,
            CancellationToken.None);

        Assert.Equal(1, result.UpdatedEntries);
        AssertAniListMetadata(await fixture.Db.MediaTitles.SingleAsync());
        var malBinding = await fixture.Db.MediaLibraryProviderBindings
            .Include(binding => binding.MediaProviderLink)
            .SingleAsync(binding => binding.MediaProviderLink!.Provider == "myanimelist");
        Assert.Equal(malItem.LastRemoteUpdateAt, malBinding.LastRemoteUpdateAt);
    }

    [Fact]
    public async Task MalOnlyRefresh_UpdatesCanonicalMetadata()
    {
        await using var fixture = await ImportFixture.CreateAsync();
        await fixture.Service.ImportAsync(
            fixture.User.Id,
            fixture.MalAccount,
            fixture.MalImport,
            CancellationToken.None);

        var malItem = Assert.Single(fixture.MalImport.Items);
        malItem.Title = "MAL refreshed title";
        malItem.NativeTitle = "MAL refreshed native title";
        malItem.OriginalTitle = "MAL refreshed original title";
        malItem.Synonyms = ["MAL refreshed synonym"];
        malItem.Synopsis = "MAL refreshed synopsis";
        malItem.Format = "MOVIE";
        malItem.PosterUrl = "https://mal.example/refreshed-poster.jpg";
        malItem.StartYear = 2025;
        malItem.EpisodeCount = 31;
        malItem.ReleasedCount = 31;
        malItem.TotalKnownCount = 31;
        fixture.MalImport.ImportedAt = new DateTimeOffset(2026, 8, 23, 13, 0, 0, TimeSpan.Zero);

        await fixture.Service.ImportAsync(
            fixture.User.Id,
            fixture.MalAccount,
            fixture.MalImport,
            CancellationToken.None);

        var title = await fixture.Db.MediaTitles.SingleAsync();
        Assert.Equal("MAL refreshed title", title.CanonicalTitle);
        Assert.Equal("MAL refreshed title", title.SortTitle);
        Assert.Equal("MAL refreshed original title", title.OriginalTitle);
        Assert.Equal(["MAL refreshed synonym"], title.Synonyms);
        Assert.Equal("MAL refreshed synopsis", title.Synopsis);
        Assert.Equal("MOVIE", title.Format);
        Assert.Equal("https://mal.example/refreshed-poster.jpg", title.PosterUrl);
        Assert.Equal(2025, title.StartYear);
        Assert.Equal(31, title.EpisodeCount);
        Assert.Equal(31, title.ReleasedCount);
        Assert.Equal(31, title.TotalKnownCount);
    }

    [Fact]
    public async Task ChangedCrossReference_DoesNotCreateSecondLinkForSameProviderAndTitle()
    {
        await using var fixture = await ImportFixture.CreateAsync();
        await fixture.Service.ImportAsync(
            fixture.User.Id,
            fixture.AniListAccount,
            fixture.AniListImport,
            CancellationToken.None);
        fixture.AniListImport.Items[0].CrossReferences =
        [
            new MediaProviderCrossReference
            {
                ProviderId = "myanimelist",
                ProviderMediaId = "anime:99999"
            }
        ];

        await fixture.Service.ImportAsync(
            fixture.User.Id,
            fixture.AniListAccount,
            fixture.AniListImport,
            CancellationToken.None);

        var malLink = Assert.Single(await fixture.Db.MediaProviderLinks
            .Where(link => link.Provider == "myanimelist")
            .ToListAsync());
        Assert.Equal("anime:52991", malLink.ExternalId);
    }

    [Fact]
    public async Task NewerMalImport_QueuesChangedStateForConnectedAniListBinding()
    {
        await using var fixture = await ImportFixture.CreateAsync();
        await fixture.Service.ImportAsync(
            fixture.User.Id,
            fixture.AniListAccount,
            fixture.AniListImport,
            CancellationToken.None);

        fixture.MalImport.Items = [ImportFixture.CreateItem(
            "anime:52991",
            [],
            status: MediaLibraryStatuses.Completed,
            score: 90m,
            progressEpisodes: 28,
            remoteUpdatedAt: new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero))];

        await fixture.Service.ImportAsync(
            fixture.User.Id,
            fixture.MalAccount,
            fixture.MalImport,
            CancellationToken.None);

        var operations = await fixture.Db.MediaProviderOperations
            .Include(operation => operation.MediaLibraryProviderBinding)
                .ThenInclude(binding => binding!.MediaProviderLink)
            .OrderBy(operation => operation.OperationType)
            .ToListAsync();
        Assert.Equal(3, operations.Count);
        Assert.All(operations, operation =>
        {
            Assert.Equal("anilist", operation.MediaLibraryProviderBinding!.MediaProviderLink!.Provider);
            Assert.Equal("154587", operation.MediaLibraryProviderBinding.MediaProviderLink.ExternalId);
        });

        var status = Assert.Single(operations, operation => operation.OperationType == MediaProviderOperationTypes.UpdateStatus);
        Assert.Equal(MediaLibraryStatuses.Completed,
            JsonSerializer.Deserialize<MediaStatusUpdateRequest>(status.PayloadJson, SerializerOptions)!.Status);
        var score = Assert.Single(operations, operation => operation.OperationType == MediaProviderOperationTypes.UpdateScore);
        Assert.Equal(90m, JsonSerializer.Deserialize<MediaScoreUpdateRequest>(score.PayloadJson, SerializerOptions)!.Score);
        var progress = Assert.Single(operations, operation => operation.OperationType == MediaProviderOperationTypes.UpdateProgress);
        Assert.Equal(28, JsonSerializer.Deserialize<MediaProgressUpdateRequest>(progress.PayloadJson, SerializerOptions)!.ProgressEpisodes);
    }

    [Fact]
    public async Task OlderMalImport_DoesNotOverwriteOrFanOutNewerAniListState()
    {
        await using var fixture = await ImportFixture.CreateAsync();
        fixture.AniListImport.Items = [ImportFixture.CreateItem(
            "154587",
            fixture.AniListImport.Items[0].CrossReferences,
            status: MediaLibraryStatuses.Current,
            score: 80m,
            progressEpisodes: 8,
            remoteUpdatedAt: new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero))];
        await fixture.Service.ImportAsync(
            fixture.User.Id,
            fixture.AniListAccount,
            fixture.AniListImport,
            CancellationToken.None);

        fixture.MalImport.Items = [ImportFixture.CreateItem(
            "anime:52991",
            [],
            status: MediaLibraryStatuses.Completed,
            score: 90m,
            progressEpisodes: 28,
            remoteUpdatedAt: new DateTimeOffset(2026, 8, 23, 11, 0, 0, TimeSpan.Zero))];
        await fixture.Service.ImportAsync(
            fixture.User.Id,
            fixture.MalAccount,
            fixture.MalImport,
            CancellationToken.None);

        var entry = await fixture.Db.MediaLibraryEntries.SingleAsync();
        Assert.Equal(MediaLibraryStatuses.Current, entry.Status);
        Assert.Equal(80m, entry.Score);
        Assert.Equal(8, entry.ProgressEpisodes);
        Assert.Empty(await fixture.Db.MediaProviderOperations.ToListAsync());
    }

    private static void AssertAniListMetadata(MediaTitle title)
    {
        Assert.Equal("Frieren: Beyond Journey's End", title.CanonicalTitle);
        Assert.Equal("Frieren: Beyond Journey's End", title.SortTitle);
        Assert.Equal("Sousou no Frieren", title.OriginalTitle);
        Assert.Equal(["Frieren", "Sousou no Frieren"], title.Synonyms);
        Assert.Equal(MediaKinds.Anime, title.MediaKind);
        Assert.Equal("AniList synopsis", title.Synopsis);
        Assert.Equal("TV", title.Format);
        Assert.Equal("https://anilist.example/poster.jpg", title.PosterUrl);
        Assert.Equal("https://anilist.example/banner.jpg", title.BackgroundUrl);
        Assert.Equal(2023, title.StartYear);
        Assert.Equal(28, title.EpisodeCount);
        Assert.Equal(26, title.ReleasedCount);
        Assert.Equal(28, title.TotalKnownCount);
        Assert.Equal(new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero), title.NextReleaseAt);
        Assert.Equal("Episode 27", title.NextReleaseLabel);
        Assert.True(title.SupportsEpisodeProgress);
        Assert.False(title.SupportsChapterProgress);
        Assert.False(title.SupportsVolumeProgress);
        Assert.False(title.IsCompletionOnly);
        Assert.Equal(MediaProgressDimensions.Episode, title.PrimaryProgressDimension);
        Assert.Equal(MediaProgressDimensions.Episode, title.ReleaseStatusDimension);
    }

    private sealed class ImportFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private ImportFixture(
            SqliteConnection connection,
            ApplicationDbContext db,
            MediaLibraryImportService service,
            User user,
            ConnectedServiceAccount aniListAccount,
            ConnectedServiceAccount malAccount)
        {
            _connection = connection;
            Db = db;
            Service = service;
            User = user;
            AniListAccount = aniListAccount;
            MalAccount = malAccount;
        }

        public ApplicationDbContext Db { get; }
        public MediaLibraryImportService Service { get; }
        public User User { get; }
        public ConnectedServiceAccount AniListAccount { get; }
        public ConnectedServiceAccount MalAccount { get; }

        public MediaProviderLibraryImportResult AniListImport { get; } = new()
        {
            ProviderId = "anilist",
            ImportedAt = new DateTimeOffset(2026, 8, 23, 10, 0, 0, TimeSpan.Zero),
            Items =
            [
                CreateItem(
                    "154587",
                    [new MediaProviderCrossReference
                    {
                        ProviderId = "myanimelist",
                        ProviderMediaId = "anime:52991",
                        ExternalUrl = "https://myanimelist.net/anime/52991"
                    }],
                    title: "Frieren: Beyond Journey's End",
                    nativeTitle: "Sousou no Frieren",
                    synonyms: ["Frieren", "Sousou no Frieren"],
                    synopsis: "AniList synopsis",
                    format: "TV",
                    posterUrl: "https://anilist.example/poster.jpg",
                    backgroundUrl: "https://anilist.example/banner.jpg",
                    startYear: 2023,
                    episodeCount: 28,
                    releasedCount: 26,
                    totalKnownCount: 28,
                    nextReleaseAt: new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero),
                    nextReleaseLabel: "Episode 27")
            ]
        };

        public MediaProviderLibraryImportResult MalImport { get; } = new()
        {
            ProviderId = "myanimelist",
            ImportedAt = new DateTimeOffset(2026, 8, 23, 11, 0, 0, TimeSpan.Zero),
            Items =
            [
                CreateItem(
                    "anime:52991",
                    [],
                    title: "Sousou no Frieren",
                    nativeTitle: "葬送のフリーレン",
                    synonyms: ["MAL synonym"],
                    synopsis: "MyAnimeList synopsis",
                    format: "SPECIAL",
                    posterUrl: "https://mal.example/poster.jpg",
                    startYear: 2024,
                    episodeCount: 30,
                    releasedCount: 30,
                    totalKnownCount: 30)
            ]
        };

        public static async Task<ImportFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
            var db = new ApplicationDbContext(options);
            await db.Database.EnsureCreatedAsync();
            var user = TestUserFactory.Create(801, "cross-reference@example.com");
            var now = DateTime.UtcNow;
            var aniListAccount = new ConnectedServiceAccount
            {
                UserId = user.Id,
                Service = "anilist",
                ExternalAccountId = "anilist-user",
                CreatedAt = now,
                UpdatedAt = now
            };
            var malAccount = new ConnectedServiceAccount
            {
                UserId = user.Id,
                Service = "myanimelist",
                ExternalAccountId = "mal-user",
                CreatedAt = now,
                UpdatedAt = now
            };
            db.AddRange(user, aniListAccount, malAccount);
            await db.SaveChangesAsync();
            return new ImportFixture(
                connection,
                db,
                new MediaLibraryImportService(db, NullLogger<MediaLibraryImportService>.Instance),
                user,
                aniListAccount,
                malAccount);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }

        public static MediaProviderLibraryItem CreateItem(
            string providerMediaId,
            IReadOnlyList<MediaProviderCrossReference> crossReferences,
            string status = MediaLibraryStatuses.Current,
            decimal? score = 80m,
            int? progressEpisodes = 4,
            DateTimeOffset? remoteUpdatedAt = null,
            string title = "Frieren: Beyond Journey's End",
            string? nativeTitle = null,
            IReadOnlyList<string>? synonyms = null,
            string? synopsis = null,
            string? format = null,
            string? posterUrl = null,
            string? backgroundUrl = null,
            int? startYear = null,
            int? episodeCount = 28,
            int? releasedCount = 28,
            int? totalKnownCount = 28,
            DateTimeOffset? nextReleaseAt = null,
            string? nextReleaseLabel = null)
        {
            return new MediaProviderLibraryItem
            {
                ProviderMediaId = providerMediaId,
                Title = title,
                NativeTitle = nativeTitle,
                OriginalTitle = nativeTitle,
                Synonyms = synonyms ?? [],
                MediaKind = MediaKinds.Anime,
                Synopsis = synopsis,
                Format = format,
                PosterUrl = posterUrl,
                BackgroundUrl = backgroundUrl,
                StartYear = startYear,
                EpisodeCount = episodeCount,
                ReleasedCount = releasedCount,
                TotalKnownCount = totalKnownCount,
                NextReleaseAt = nextReleaseAt,
                NextReleaseLabel = nextReleaseLabel,
                Status = status,
                Score = score,
                ProgressEpisodes = progressEpisodes,
                PrimaryProgressDimension = MediaProgressDimensions.Episode,
                ReleaseStatusDimension = MediaProgressDimensions.Episode,
                LastRemoteUpdateAt = remoteUpdatedAt
                    ?? new DateTimeOffset(2026, 8, 23, 9, 0, 0, TimeSpan.Zero),
                CrossReferences = crossReferences
            };
        }
    }
}
