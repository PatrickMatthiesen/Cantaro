using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantaro.Api.Tests;

public class MediaObservationProgressServiceTests
{
    // ── TryParseProgressHint (internal static, tested directly) ──────────────

    [Theory]
    [InlineData("5", 5, true)]
    [InlineData("  12  ", 12, true)]
    [InlineData("1", 1, true)]
    [InlineData("0", 0, false)]        // zero is not a positive integer
    [InlineData("-3", 0, false)]       // negative
    [InlineData("", 0, false)]
    [InlineData(null, 0, false)]
    [InlineData("abc", 0, false)]
    [InlineData("3.5", 0, false)]      // decimal
    public void TryParseProgressHint_HandlesVariousInputs(string? hint, int expectedValue, bool expectedResult)
    {
        var result = MediaObservationProgressService.TryParseProgressHint(hint, out var value);

        Assert.Equal(expectedResult, result);
        Assert.Equal(expectedValue, value);
    }

    // ── TryEnqueueAutoProgressAsync: guard conditions ──────────────────────

    [Fact]
    public async Task TryEnqueueAutoProgress_ReturnsZero_WhenObservationNotMatched()
    {
        await using var fixture = await ProgressFixture.CreateAsync();

        var observation = fixture.MakeObservation(
            matchStatus: MediaObservationStatuses.Pending,
            progressHint: "5",
            mediaTitleId: null);

        fixture.Db.MediaObservations.Add(observation);
        await fixture.Db.SaveChangesAsync();

        var count = await fixture.Service.TryEnqueueAutoProgressAsync(observation, CancellationToken.None);

        Assert.Equal(0, count);
        Assert.Equal(0, await fixture.Db.MediaProviderOperations.CountAsync());
    }

    [Fact]
    public async Task TryEnqueueAutoProgress_ReturnsZero_WhenProgressHintUnparseable()
    {
        await using var fixture = await ProgressFixture.CreateAsync();
        var (title, entry) = await fixture.SeedTitleAndEntryAsync(currentEpisodes: 5);

        var observation = fixture.MakeObservation(
            matchStatus: MediaObservationStatuses.Matched,
            progressHint: "not-a-number",
            mediaTitleId: title.Id);

        fixture.Db.MediaObservations.Add(observation);
        await fixture.Db.SaveChangesAsync();

        var count = await fixture.Service.TryEnqueueAutoProgressAsync(observation, CancellationToken.None);

        Assert.Equal(0, count);
        Assert.Equal(0, await fixture.Db.MediaProviderOperations.CountAsync());
    }

    [Fact]
    public async Task TryEnqueueAutoProgress_EnqueuesProviderSync_WhenEntryIsConnected()
    {
        await using var fixture = await ProgressFixture.CreateAsync();
        var (title, entry) = await fixture.SeedTitleAndEntryAsync(currentEpisodes: 5);

        var observation = fixture.MakeObservation(
            matchStatus: MediaObservationStatuses.Matched,
            progressHint: "7",
            mediaTitleId: title.Id);

        fixture.Db.MediaObservations.Add(observation);
        await fixture.Db.SaveChangesAsync();

        var count = await fixture.Service.TryEnqueueAutoProgressAsync(observation, CancellationToken.None);

        Assert.Equal(1, count);
        Assert.Equal(1, await fixture.Db.MediaProviderOperations.CountAsync());

        var persistedEntry = await fixture.Db.MediaLibraryEntries.SingleAsync(e => e.Id == entry.Id);
        Assert.Equal(7, persistedEntry.ProgressEpisodes);
        Assert.Equal(MediaMutationSources.ObservationAutoProgress, persistedEntry.LastMutationSource);
        Assert.NotNull(persistedEntry.LastLocalEditAt);
    }

    [Fact]
    public async Task TryEnqueueAutoProgress_ReturnsZero_WhenProgressDoesNotAdvance()
    {
        await using var fixture = await ProgressFixture.CreateAsync();
        // Entry already at episode 10; hint says 10 → not strictly greater
        var (title, entry) = await fixture.SeedTitleAndEntryAsync(currentEpisodes: 10);

        var observation = fixture.MakeObservation(
            matchStatus: MediaObservationStatuses.Matched,
            progressHint: "10",
            mediaTitleId: title.Id);

        fixture.Db.MediaObservations.Add(observation);
        await fixture.Db.SaveChangesAsync();

        var count = await fixture.Service.TryEnqueueAutoProgressAsync(observation, CancellationToken.None);

        Assert.Equal(0, count);
        Assert.Equal(0, await fixture.Db.MediaProviderOperations.CountAsync());

        var persistedEntry = await fixture.Db.MediaLibraryEntries.SingleAsync(e => e.Id == entry.Id);
        Assert.Equal(10, persistedEntry.ProgressEpisodes);
    }

    [Fact]
    public async Task TryEnqueueAutoProgress_UpdatesLocalProgress_WhenEntryHasNoConnectedAccount()
    {
        await using var fixture = await ProgressFixture.CreateAsync();
        var (title, entry) = await fixture.SeedTitleAndEntryAsync(currentEpisodes: 5, connected: false);

        var observation = fixture.MakeObservation(
            matchStatus: MediaObservationStatuses.Matched,
            progressHint: "7",
            mediaTitleId: title.Id);

        fixture.Db.MediaObservations.Add(observation);
        await fixture.Db.SaveChangesAsync();

        var count = await fixture.Service.TryEnqueueAutoProgressAsync(observation, CancellationToken.None);

        Assert.Equal(0, count);
        Assert.Equal(0, await fixture.Db.MediaProviderOperations.CountAsync());

        var persistedEntry = await fixture.Db.MediaLibraryEntries.SingleAsync(e => e.Id == entry.Id);
        Assert.Equal(7, persistedEntry.ProgressEpisodes);
        Assert.Equal(MediaMutationSources.ObservationAutoProgress, persistedEntry.LastMutationSource);
        Assert.NotNull(persistedEntry.LastLocalEditAt);
    }

    // ── TryEnqueueAutoProgressAsync: success path ─────────────────────────

    [Fact]
    public async Task TryEnqueueAutoProgress_EnqueuesOperation_WhenAllConditionsMet()
    {
        await using var fixture = await ProgressFixture.CreateAsync();
        var (title, entry) = await fixture.SeedTitleAndEntryAsync(currentEpisodes: 5);

        var observation = fixture.MakeObservation(
            matchStatus: MediaObservationStatuses.Matched,
            progressHint: "8",
            mediaTitleId: title.Id);

        fixture.Db.MediaObservations.Add(observation);
        await fixture.Db.SaveChangesAsync();

        var count = await fixture.Service.TryEnqueueAutoProgressAsync(observation, CancellationToken.None);

        Assert.Equal(1, count);

        var persistedEntry = await fixture.Db.MediaLibraryEntries.SingleAsync(e => e.Id == entry.Id);
        Assert.Equal(8, persistedEntry.ProgressEpisodes);
        Assert.Equal(MediaMutationSources.ObservationAutoProgress, persistedEntry.LastMutationSource);
        Assert.NotNull(persistedEntry.LastLocalEditAt);

        var op = await fixture.Db.MediaProviderOperations.SingleAsync();
        Assert.Equal(MediaProviderOperationTypes.AutoProgressUpdate, op.OperationType);
        Assert.Equal(entry.Provider, op.Provider);
        Assert.Equal(entry.Id, op.MediaLibraryEntryId);
        Assert.Equal(MediaProviderOperationStatuses.Pending, op.Status);

        var payload = System.Text.Json.JsonSerializer.Deserialize<AutoProgressUpdatePayload>(
            op.PayloadJson,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;

        Assert.Equal(8, payload.ProgressEpisodes);
        Assert.Null(payload.ProgressChapters);
        Assert.Equal(observation.Id.ToString(), payload.TriggeredByObservationId);
        Assert.Equal(observation.SiteIdentifier, payload.ObservedSiteIdentifier);
        Assert.Equal("8", payload.ObservedProgressHint);
        Assert.Equal(entry.LastRemoteUpdateAt, payload.LastKnownRemoteUpdateAt);
    }

    [Fact]
    public async Task TryEnqueueAutoProgress_UsesResolvedProgress_WhenOffsetWasApplied()
    {
        await using var fixture = await ProgressFixture.CreateAsync();
        var (title, _) = await fixture.SeedTitleAndEntryAsync(currentEpisodes: 3);

        var observation = fixture.MakeObservation(
            matchStatus: MediaObservationStatuses.Matched,
            progressHint: "16",
            mediaTitleId: title.Id);
        observation.EpisodeOffset = -12;
        observation.ResolvedProgress = 4;

        fixture.Db.MediaObservations.Add(observation);
        await fixture.Db.SaveChangesAsync();

        var count = await fixture.Service.TryEnqueueAutoProgressAsync(observation, CancellationToken.None);

        Assert.Equal(1, count);
        var op = await fixture.Db.MediaProviderOperations.SingleAsync();
        var payload = System.Text.Json.JsonSerializer.Deserialize<AutoProgressUpdatePayload>(
            op.PayloadJson,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        Assert.Equal(4, payload.ProgressEpisodes);
        Assert.Equal("16", payload.ObservedProgressHint);
    }

    [Fact]
    public async Task TryEnqueueAutoProgress_RoutesToChapterDimension_ForManga()
    {
        await using var fixture = await ProgressFixture.CreateAsync();
        var (title, _) = await fixture.SeedTitleAndEntryAsync(
            currentEpisodes: 0,
            primaryDimension: MediaProgressDimensions.Chapter,
            currentChapters: 10);

        var observation = fixture.MakeObservation(
            matchStatus: MediaObservationStatuses.Matched,
            progressHint: "15",
            mediaTitleId: title.Id);

        fixture.Db.MediaObservations.Add(observation);
        await fixture.Db.SaveChangesAsync();

        var count = await fixture.Service.TryEnqueueAutoProgressAsync(observation, CancellationToken.None);

        Assert.Equal(1, count);

        var op = await fixture.Db.MediaProviderOperations.SingleAsync();
        var payload = System.Text.Json.JsonSerializer.Deserialize<AutoProgressUpdatePayload>(
            op.PayloadJson,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;

        Assert.Null(payload.ProgressEpisodes);
        Assert.Equal(15, payload.ProgressChapters);
        Assert.Null(payload.ProgressVolumes);
    }

    [Fact]
    public async Task TryEnqueueAutoProgress_EnqueuesForMultipleConnectedEntries()
    {
        await using var fixture = await ProgressFixture.CreateAsync();
        var (title, _) = await fixture.SeedTitleAndEntryAsync(currentEpisodes: 3, providerMediaId: "111");
        await fixture.SeedAdditionalEntryAsync(title, currentEpisodes: 2, providerMediaId: "222");

        var observation = fixture.MakeObservation(
            matchStatus: MediaObservationStatuses.Matched,
            progressHint: "5",
            mediaTitleId: title.Id);

        fixture.Db.MediaObservations.Add(observation);
        await fixture.Db.SaveChangesAsync();

        var count = await fixture.Service.TryEnqueueAutoProgressAsync(observation, CancellationToken.None);

        Assert.Equal(2, count);
        Assert.Equal(2, await fixture.Db.MediaProviderOperations.CountAsync());
    }

    [Fact]
    public async Task TryEnqueueAutoProgress_SkipsEntryBelowMonotonicThreshold_EnqueuesOther()
    {
        await using var fixture = await ProgressFixture.CreateAsync();
        // Entry A is at episode 10 (hint=8 would regress — skip)
        // Entry B is at episode 5 (hint=8 would advance — enqueue)
        var (title, _) = await fixture.SeedTitleAndEntryAsync(currentEpisodes: 10, providerMediaId: "111");
        await fixture.SeedAdditionalEntryAsync(title, currentEpisodes: 5, providerMediaId: "222");

        var observation = fixture.MakeObservation(
            matchStatus: MediaObservationStatuses.Matched,
            progressHint: "8",
            mediaTitleId: title.Id);

        fixture.Db.MediaObservations.Add(observation);
        await fixture.Db.SaveChangesAsync();

        var count = await fixture.Service.TryEnqueueAutoProgressAsync(observation, CancellationToken.None);

        Assert.Equal(1, count);
        var op = await fixture.Db.MediaProviderOperations.SingleAsync();
        var payload = System.Text.Json.JsonSerializer.Deserialize<AutoProgressUpdatePayload>(
            op.PayloadJson,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        Assert.Equal("222", payload.ProviderMediaId);
    }

    // ── Fixture ──────────────────────────────────────────────────────────────

    private sealed class ProgressFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        public ApplicationDbContext Db { get; }
        public MediaObservationProgressService Service { get; }
        public int UserId { get; } = 901;

        private ProgressFixture(SqliteConnection connection, ApplicationDbContext db, MediaObservationProgressService service)
        {
            _connection = connection;
            Db = db;
            Service = service;
        }

        public static async Task<ProgressFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;

            var db = new ApplicationDbContext(options);
            await db.Database.EnsureCreatedAsync();

            db.Users.Add(TestUserFactory.Create(901, "progress.test@example.com"));
            await db.SaveChangesAsync();

            var registry = new MediaProviderRegistry([new NoOpMediaProvider("anilist")]);
            var processor = new MediaProviderOperationProcessor(db, registry, NullLogger<MediaProviderOperationProcessor>.Instance);
            var service = new MediaObservationProgressService(db, processor, NullLogger<MediaObservationProgressService>.Instance);

            return new ProgressFixture(connection, db, service);
        }

        public async Task<(MediaTitle title, MediaLibraryEntry entry)> SeedTitleAndEntryAsync(
            int currentEpisodes,
            string primaryDimension = MediaProgressDimensions.Episode,
            int currentChapters = 0,
            string providerMediaId = "140960",
            bool connected = true)
        {
            var now = DateTimeOffset.UtcNow;
            var accountId = 5000 + UserId;

            if (connected && !Db.ConnectedServiceAccounts.Any(a => a.Id == accountId))
            {
                Db.ConnectedServiceAccounts.Add(new ConnectedServiceAccount
                {
                    Id = accountId,
                    UserId = UserId,
                    Service = "anilist",
                    ExternalAccountId = $"viewer-{UserId}",
                    CreatedAt = now.UtcDateTime,
                    UpdatedAt = now.UtcDateTime
                });
                await Db.SaveChangesAsync();
            }

            var title = new MediaTitle
            {
                Id = Guid.NewGuid(),
                CanonicalTitle = $"Test Title {Guid.NewGuid().ToString("N")[..8]}",
                MediaKind = MediaKinds.Anime,
                PrimaryProgressDimension = primaryDimension,
                ReleaseStatusDimension = primaryDimension,
                SupportsEpisodeProgress = primaryDimension == MediaProgressDimensions.Episode,
                SupportsChapterProgress = primaryDimension == MediaProgressDimensions.Chapter,
                CreatedAt = now,
                UpdatedAt = now
            };
            Db.MediaTitles.Add(title);

            var entry = new MediaLibraryEntry
            {
                Id = Guid.NewGuid(),
                UserId = UserId,
                MediaTitleId = title.Id,
                ConnectedServiceAccountId = connected ? accountId : null,
                Provider = "anilist",
                ProviderAccountId = $"viewer-{UserId}",
                ProviderMediaId = providerMediaId,
                Status = MediaLibraryStatuses.Current,
                ProgressEpisodes = currentEpisodes,
                ProgressChapters = currentChapters > 0 ? currentChapters : null,
                LastRemoteUpdateAt = now.AddMinutes(-10),
                CreatedAt = now,
                UpdatedAt = now
            };
            Db.MediaLibraryEntries.Add(entry);
            await Db.SaveChangesAsync();

            return (title, entry);
        }

        public async Task<MediaLibraryEntry> SeedAdditionalEntryAsync(
            MediaTitle title,
            int currentEpisodes,
            string providerMediaId)
        {
            var now = DateTimeOffset.UtcNow;
            var accountId = 5000 + UserId;

            var entry = new MediaLibraryEntry
            {
                Id = Guid.NewGuid(),
                UserId = UserId,
                MediaTitleId = title.Id,
                ConnectedServiceAccountId = accountId,
                Provider = "anilist",
                ProviderAccountId = $"viewer-{UserId}-alt",
                ProviderMediaId = providerMediaId,
                Status = MediaLibraryStatuses.Current,
                ProgressEpisodes = currentEpisodes,
                LastRemoteUpdateAt = now.AddMinutes(-10),
                CreatedAt = now,
                UpdatedAt = now
            };
            Db.MediaLibraryEntries.Add(entry);
            await Db.SaveChangesAsync();
            return entry;
        }

        public MediaObservation MakeObservation(
            string matchStatus,
            string? progressHint,
            Guid? mediaTitleId)
        {
            var now = DateTimeOffset.UtcNow;
            return new MediaObservation
            {
                Id = Guid.NewGuid(),
                UserId = UserId,
                SiteIdentifier = MediaObservationSiteIdentifiers.Crunchyroll,
                ObservedUrl = "https://crunchyroll.com/watch/test",
                ObservedTitle = "Test Episode",
                ProgressHint = progressHint,
                ObservedAt = now,
                MatchStatus = matchStatus,
                MediaTitleId = mediaTitleId,
                CreatedAt = now,
                UpdatedAt = now
            };
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    /// <summary>
    /// No-op provider used in service tests — operations are enqueued (persisted)
    /// but never executed, so provider calls never happen.
    /// </summary>
    private sealed class NoOpMediaProvider(string providerId) : IMediaProvider
    {
        public string ProviderId => providerId;

        public Task<ConnectedServiceAccount?> GetConnectedAccountAsync(int userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public string GetAuthorizationUrl(string redirectUri, string state, string codeChallenge) =>
            throw new NotSupportedException();

        public Task<ConnectedServiceAccount> ExchangeCodeAndSaveAsync(int userId, string authorizationCode, string redirectUri, string codeVerifier, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DisconnectAsync(int userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MediaProviderLibraryImportResult> ImportLibraryAsync(int userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MediaProviderSearchResult>> SearchAsync(int userId, MediaCatalogSearchRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MediaProviderTitleDetails?> GetTitleDetailsAsync(int userId, string providerMediaId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MediaProviderMutationResult> UpdateProgressAsync(int userId, MediaProgressUpdateRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MediaProviderMutationResult> UpdateStatusAsync(int userId, MediaStatusUpdateRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MediaReleaseMetadata?> GetReleaseMetadataAsync(int userId, string providerMediaId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
