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
                    }])
            ]
        };

        public MediaProviderLibraryImportResult MalImport { get; } = new()
        {
            ProviderId = "myanimelist",
            ImportedAt = new DateTimeOffset(2026, 8, 23, 11, 0, 0, TimeSpan.Zero),
            Items = [CreateItem("anime:52991", [])]
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
            DateTimeOffset? remoteUpdatedAt = null)
        {
            return new MediaProviderLibraryItem
            {
                ProviderMediaId = providerMediaId,
                Title = "Frieren: Beyond Journey's End",
                MediaKind = MediaKinds.Anime,
                EpisodeCount = 28,
                ReleasedCount = 28,
                TotalKnownCount = 28,
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
