using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Cantaro.Api.Tests;

public class MediaEpisodeIdentityRemappingPolicyTests
{
    [Fact]
    public async Task CatalogEvidenceRemapsAnUntrustedIdentityToTheNewCanonicalEpisode()
    {
        await using var fixture = await RemappingFixture.CreateAsync();
        var originalTitle = fixture.AddTitle("Original title");
        var correctedTitle = fixture.AddTitle("Corrected title");

        await fixture.Service.RecordObservationAsync(
            fixture.MakeWatchObservation(originalTitle.Id, 1),
            CancellationToken.None);

        var catalogObservation = fixture.MakeCatalogObservation(correctedTitle.Id);
        await fixture.Service.RecordCatalogObservationAsync(catalogObservation, CancellationToken.None);

        var identity = await fixture.Db.MediaEpisodeProviderIdentities
            .Include(item => item.Content)
            .SingleAsync();
        var remappedEpisode = await fixture.Db.MediaEpisodes
            .SingleAsync(item => item.Id == identity.Content!.MediaEpisodeId);
        Assert.Equal(correctedTitle.Id, remappedEpisode.MediaTitleId);
        Assert.False(identity.IsTrusted);
        Assert.False(identity.HasConflict);
        Assert.Equal(MediaObservationStatuses.Matched, catalogObservation.MatchStatus);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CatalogRemapReusesContentForAnotherLocaleOnTheCorrectEpisode(bool submitCatalog)
    {
        await using var fixture = await RemappingFixture.CreateAsync();
        await AssertCatalogRemapReusesContentAsync(fixture, submitCatalog);
    }

    [PostgresFact]
    public async Task CatalogRemapReusesContentForAnotherLocaleOnPostgres()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await using var fixture = new RemappingFixture(schema.Db);
        await AssertCatalogRemapReusesContentAsync(fixture, submitCatalog: false);
    }

    [PostgresFact]
    public async Task ConcurrentLocaleRemapsShareOneNewDestinationOnPostgres()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await using var fixture = new RemappingFixture(schema.Db);
        var title = fixture.AddTitle("Title");
        const string englishId = "GE00375194ENUS";
        const string japaneseId = "GE00375194JAJP";
        await fixture.Service.RecordObservationAsync(fixture.MakeWatchObservation(title.Id, 1, englishId), CancellationToken.None);
        await fixture.Service.RecordObservationAsync(fixture.MakeWatchObservation(title.Id, 2, japaneseId), CancellationToken.None);
        fixture.Db.ChangeTracker.Clear();

        await using var otherDb = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(schema.Db.Database.GetConnectionString(), options => options.EnableRetryOnFailure(maxRetryCount: 1))
            .Options);
        await using var other = new RemappingFixture(otherDb);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        await Task.WhenAll(
            fixture.Service.RecordObservationAsync(fixture.MakeCatalogObservation(title.Id, englishId, 3), timeout.Token),
            other.Service.RecordCatalogObservationAsync(other.MakeCatalogObservation(title.Id, japaneseId, 3), timeout.Token));
        fixture.Db.ChangeTracker.Clear();

        var episode = await fixture.Db.MediaEpisodes
            .Include(item => item.ProviderContents)
                .ThenInclude(item => item.Variants)
            .SingleAsync(item => item.MediaTitleId == title.Id && item.EpisodeNumber == 3);
        var content = Assert.Single(episode.ProviderContents);
        Assert.Equal("GE00375194", content.ProviderContentKey);
        Assert.Equal(new[] { englishId, japaneseId }, content.Variants.Select(item => item.ProviderEpisodeId).OrderBy(id => id));
    }

    [PostgresFact]
    public async Task AutomaticRemapWaitsForConcurrentManualConfirmationAcrossTitles()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await using var fixture = new RemappingFixture(schema.Db);
        var original = fixture.AddTitle("Original title");
        var confirmed = fixture.AddTitle("Confirmed title");
        var automatic = fixture.AddTitle("Automatic title");
        await fixture.Service.RecordObservationAsync(fixture.MakeWatchObservation(original.Id, 1), CancellationToken.None);
        fixture.Db.ChangeTracker.Clear();

        var connection = schema.Db.Database.GetConnectionString();
        var automaticConnection = new NpgsqlConnectionStringBuilder(connection)
        {
            ApplicationName = $"cantaro-remap-test-{Guid.NewGuid():N}",
        };
        var holdManualSave = new HoldSaveChanges();
        var holdAutomaticSave = new HoldSaveChanges();
        await using var manualDb = CreateRecordingContext(connection, holdManualSave);
        await using var automaticDb = CreateRecordingContext(automaticConnection.ConnectionString, holdAutomaticSave);
        await using var manualRecorder = new RemappingFixture(manualDb);
        await using var automaticRecorder = new RemappingFixture(automaticDb);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var manualWrite = manualRecorder.Service.RecordObservationAsync(
            fixture.MakeWatchObservation(confirmed.Id, 1), timeout.Token,
            isUserConfirmed: true, allowTrustedIdentityRemap: true);
        var automaticObservation = fixture.MakeCatalogObservation(automatic.Id);
        Task<int>? automaticWrite = null;
        try
        {
            await holdManualSave.Started.Task.WaitAsync(timeout.Token);
            automaticWrite = automaticRecorder.Service.RecordCatalogObservationAsync(automaticObservation, timeout.Token);
            await WaitForAdvisoryLockOrSaveAsync(connection!, automaticConnection.ApplicationName!, holdAutomaticSave, timeout.Token);
            holdManualSave.Release.TrySetResult();
            await manualWrite;
        }
        finally
        {
            holdManualSave.Release.TrySetResult();
            holdAutomaticSave.Release.TrySetResult();
            await Task.WhenAll(manualWrite, automaticWrite ?? Task.CompletedTask);
        }

        var identity = await fixture.Db.MediaEpisodeProviderIdentities
            .Include(item => item.Content).ThenInclude(content => content!.MediaEpisode)
            .SingleAsync();
        Assert.Equal(confirmed.Id, identity.Content!.MediaEpisode!.MediaTitleId);
        Assert.True(identity.IsTrusted);
        Assert.True(identity.HasConflict);
        Assert.Equal(MediaObservationStatuses.Ambiguous, automaticObservation.MatchStatus);
    }

    private static ApplicationDbContext CreateRecordingContext(string? connection, HoldSaveChanges interceptor) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connection, options => options.EnableRetryOnFailure(maxRetryCount: 1))
            .AddInterceptors(interceptor)
            .Options);

    private static async Task WaitForAdvisoryLockOrSaveAsync(
        string connectionString, string applicationName, HoldSaveChanges save, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT FROM pg_stat_activity WHERE application_name = @app AND wait_event = 'advisory'
            )
            """, connection);
        command.Parameters.AddWithValue("app", applicationName);
        while (!save.Started.Task.IsCompleted && await command.ExecuteScalarAsync(cancellationToken) is false)
        {
            await Task.Delay(20, cancellationToken);
        }
    }

    private sealed class HoldSaveChanges : SaveChangesInterceptor
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return result;
        }
    }

    private static async Task AssertCatalogRemapReusesContentAsync(RemappingFixture fixture, bool submitCatalog)
    {
        var title = fixture.AddTitle("Title");
        const string englishId = "GE00375194ENUS";
        const string japaneseId = "GE00375194JAJP";

        await fixture.Service.RecordObservationAsync(
            fixture.MakeWatchObservation(title.Id, 1, englishId),
            CancellationToken.None);
        await fixture.Service.RecordObservationAsync(
            fixture.MakeWatchObservation(title.Id, 2, japaneseId),
            CancellationToken.None,
            isUserConfirmed: true);
        fixture.Db.ChangeTracker.Clear();

        var observation = fixture.MakeCatalogObservation(title.Id, englishId, episodeNumber: 2);
        if (submitCatalog)
        {
            await fixture.Service.RecordCatalogObservationAsync(observation, CancellationToken.None);
        }
        else
        {
            await fixture.Service.RecordObservationAsync(observation, CancellationToken.None);
        }
        await fixture.Service.RecordObservationAsync(observation, CancellationToken.None);
        fixture.Db.ChangeTracker.Clear();

        var content = await fixture.Db.MediaEpisodeProviderContents
            .Include(item => item.Variants)
            .Include(item => item.MediaEpisode)
            .SingleAsync(item => item.Variants.Any());
        Assert.Equal(2, content.MediaEpisode!.EpisodeNumber);
        Assert.Equal("GE00375194", content.ProviderContentKey);
        Assert.Equal(
            new[] { englishId, japaneseId },
            content.Variants.Select(item => item.ProviderEpisodeId).OrderBy(id => id));
        Assert.True(content.Variants.Single(item => item.ProviderEpisodeId == japaneseId).IsTrusted);
        Assert.Equal(MediaObservationStatuses.Matched, observation.MatchStatus);
        var catalog = await fixture.Service.GetEpisodeCatalogAsync(title.Id, CancellationToken.None);
        Assert.Empty(catalog!.Episodes.Single(item => item.EpisodeNumber == 1).Destinations);
        Assert.Equal(2, catalog.Episodes.Single(item => item.EpisodeNumber == 2).Destinations.Count);
    }

    [Fact]
    public async Task CatalogObservationGroupsNewLocalesForTheSameEpisodeBeforeSaving()
    {
        await using var fixture = await RemappingFixture.CreateAsync();
        var title = fixture.AddTitle("Title");
        var observation = fixture.MakeCatalogObservation(title.Id, "GE00375194ENUS");
        observation.Episodes.Add(RemappingFixture.MakeCatalogEpisode("GE00375194JAJP", 1));

        await fixture.Service.RecordCatalogObservationAsync(observation, CancellationToken.None);
        fixture.Db.ChangeTracker.Clear();

        var content = await fixture.Db.MediaEpisodeProviderContents.Include(item => item.Variants).SingleAsync();
        Assert.Equal("GE00375194", content.ProviderContentKey);
        Assert.Equal(2, content.Variants.Count);
    }

    [Fact]
    public async Task NewUntrustedWatchEvidenceAlsoRemapsThePreviousUntrustedIdentity()
    {
        await using var fixture = await RemappingFixture.CreateAsync();
        var originalTitle = fixture.AddTitle("Original title");
        var correctedTitle = fixture.AddTitle("Corrected title");

        await fixture.Service.RecordObservationAsync(
            fixture.MakeWatchObservation(originalTitle.Id, 1),
            CancellationToken.None);
        await fixture.Service.RecordObservationAsync(
            fixture.MakeWatchObservation(correctedTitle.Id, 1),
            CancellationToken.None);

        var identity = await fixture.Db.MediaEpisodeProviderIdentities
            .Include(item => item.Content)
            .SingleAsync();
        var remappedEpisode = await fixture.Db.MediaEpisodes
            .SingleAsync(item => item.Id == identity.Content!.MediaEpisodeId);
        Assert.Equal(correctedTitle.Id, remappedEpisode.MediaTitleId);
        Assert.False(identity.IsTrusted);
        Assert.False(identity.HasConflict);
    }

    [Fact]
    public async Task CatalogEvidenceLeavesTrustedIdentityConflictedAndQueuesObservationForReview()
    {
        await using var fixture = await RemappingFixture.CreateAsync();
        var originalTitle = fixture.AddTitle("Original title");
        var correctedTitle = fixture.AddTitle("Corrected title");

        await fixture.Service.RecordObservationAsync(
            fixture.MakeWatchObservation(originalTitle.Id, 1),
            CancellationToken.None,
            isUserConfirmed: true);

        var catalogObservation = fixture.MakeCatalogObservation(correctedTitle.Id);
        await fixture.Service.RecordCatalogObservationAsync(catalogObservation, CancellationToken.None);

        var identity = await fixture.Db.MediaEpisodeProviderIdentities
            .Include(item => item.Content)
            .SingleAsync();
        var originalEpisode = await fixture.Db.MediaEpisodes
            .SingleAsync(item => item.MediaTitleId == originalTitle.Id);
        Assert.Equal(originalEpisode.Id, identity.Content!.MediaEpisodeId);
        Assert.True(identity.IsTrusted);
        Assert.True(identity.HasConflict);
        Assert.Equal(MediaObservationStatuses.Ambiguous, catalogObservation.MatchStatus);
        Assert.Null(catalogObservation.AcceptedCandidateId);
        Assert.Contains("trusted provider episode identity", catalogObservation.ResolutionNotes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExplicitResolutionCanMoveAConflictedTrustedIdentityToTheSelectedEpisode()
    {
        await using var fixture = await RemappingFixture.CreateAsync();
        var originalTitle = fixture.AddTitle("Original title");
        var correctedTitle = fixture.AddTitle("Corrected title");

        await fixture.Service.RecordObservationAsync(
            fixture.MakeWatchObservation(originalTitle.Id, 1),
            CancellationToken.None,
            isUserConfirmed: true);
        await fixture.Service.RecordCatalogObservationAsync(
            fixture.MakeCatalogObservation(correctedTitle.Id),
            CancellationToken.None);

        var explicitObservation = fixture.MakeWatchObservation(correctedTitle.Id, 1);
        await fixture.Service.RecordObservationAsync(
            explicitObservation,
            CancellationToken.None,
            isUserConfirmed: true,
            allowTrustedIdentityRemap: true);

        var identity = await fixture.Db.MediaEpisodeProviderIdentities
            .Include(item => item.Content)
            .SingleAsync();
        var correctedEpisode = await fixture.Db.MediaEpisodes
            .SingleAsync(item => item.MediaTitleId == correctedTitle.Id);
        Assert.Equal(correctedEpisode.Id, identity.Content!.MediaEpisodeId);
        Assert.False(identity.HasConflict);
        Assert.True(identity.IsTrusted);
        Assert.Equal(MediaObservationStatuses.Matched, explicitObservation.MatchStatus);
    }

    [Fact]
    public async Task ExplicitConfirmationOfTheExistingCanonicalEpisodeClearsItsConflict()
    {
        await using var fixture = await RemappingFixture.CreateAsync();
        var originalTitle = fixture.AddTitle("Original title");
        var correctedTitle = fixture.AddTitle("Corrected title");

        await fixture.Service.RecordObservationAsync(
            fixture.MakeWatchObservation(originalTitle.Id, 1),
            CancellationToken.None,
            isUserConfirmed: true);
        await fixture.Service.RecordCatalogObservationAsync(
            fixture.MakeCatalogObservation(correctedTitle.Id),
            CancellationToken.None);

        var explicitObservation = fixture.MakeWatchObservation(originalTitle.Id, 1);
        await fixture.Service.RecordObservationAsync(
            explicitObservation,
            CancellationToken.None,
            isUserConfirmed: true,
            allowTrustedIdentityRemap: true);

        var identity = await fixture.Db.MediaEpisodeProviderIdentities.SingleAsync();
        Assert.False(identity.HasConflict);
        Assert.Equal(MediaObservationStatuses.Matched, explicitObservation.MatchStatus);
    }

    [Fact]
    public async Task AutomaticConfirmationOfAConflictedCanonicalEpisodeStillRequiresReview()
    {
        await using var fixture = await RemappingFixture.CreateAsync();
        var originalTitle = fixture.AddTitle("Original title");
        var correctedTitle = fixture.AddTitle("Corrected title");

        await fixture.Service.RecordObservationAsync(
            fixture.MakeWatchObservation(originalTitle.Id, 1),
            CancellationToken.None,
            isUserConfirmed: true);
        await fixture.Service.RecordCatalogObservationAsync(
            fixture.MakeCatalogObservation(correctedTitle.Id),
            CancellationToken.None);

        var automaticObservation = fixture.MakeWatchObservation(originalTitle.Id, 1);
        await fixture.Service.RecordObservationAsync(automaticObservation, CancellationToken.None);

        var identity = await fixture.Db.MediaEpisodeProviderIdentities.SingleAsync();
        Assert.True(identity.HasConflict);
        Assert.Equal(MediaObservationStatuses.Ambiguous, automaticObservation.MatchStatus);
        Assert.Contains("trusted provider episode identity", automaticObservation.ResolutionNotes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NewEvidenceCanClearAConflictOnAnUntrustedIdentity()
    {
        await using var fixture = await RemappingFixture.CreateAsync();
        var title = fixture.AddTitle("Title");
        await fixture.Service.RecordObservationAsync(
            fixture.MakeWatchObservation(title.Id, 1),
            CancellationToken.None);

        var identity = await fixture.Db.MediaEpisodeProviderIdentities.SingleAsync();
        identity.HasConflict = true;
        await fixture.Db.SaveChangesAsync();

        var observation = fixture.MakeWatchObservation(title.Id, 1);
        await fixture.Service.RecordObservationAsync(observation, CancellationToken.None);

        identity = await fixture.Db.MediaEpisodeProviderIdentities.SingleAsync();
        Assert.False(identity.HasConflict);
        Assert.Equal(MediaObservationStatuses.Matched, observation.MatchStatus);
    }

    private sealed class RemappingFixture : IAsyncDisposable
    {
        private readonly SqliteConnection? _connection;

        public RemappingFixture(ApplicationDbContext db, SqliteConnection? connection = null)
        {
            _connection = connection;
            Db = db;
            Service = new MediaEpisodeIdentityService(
                db,
                new MediaProviderSeasonMappingService(db, NullLogger<MediaProviderSeasonMappingService>.Instance),
                NullLogger<MediaEpisodeIdentityService>.Instance);
        }

        public ApplicationDbContext Db { get; }
        public MediaEpisodeIdentityService Service { get; }

        public static async Task<RemappingFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseSqlite(connection)
                    .Options);
            await db.Database.EnsureCreatedAsync();
            return new RemappingFixture(db, connection);
        }

        public MediaTitle AddTitle(string title)
        {
            var now = DateTimeOffset.UtcNow;
            var mediaTitle = new MediaTitle
            {
                Id = Guid.NewGuid(),
                CanonicalTitle = title,
                MediaKind = MediaKinds.Anime,
                EpisodeCount = 12,
                SupportsEpisodeProgress = true,
                PrimaryProgressDimension = MediaProgressDimensions.Episode,
                ReleaseStatusDimension = MediaProgressDimensions.Episode,
                CreatedAt = now,
                UpdatedAt = now
            };
            Db.MediaTitles.Add(mediaTitle);
            Db.SaveChanges();
            return mediaTitle;
        }

        public MediaObservation MakeWatchObservation(Guid titleId, int episodeNumber, string providerEpisodeId = "SAMEEPISODE")
        {
            var now = DateTimeOffset.UtcNow;
            return new MediaObservation
            {
                Id = Guid.NewGuid(),
                UserId = 1,
                SiteIdentifier = MediaObservationSiteIdentifiers.Crunchyroll,
                SiteMediaId = providerEpisodeId,
                ObservedUrl = $"https://www.crunchyroll.com/watch/{providerEpisodeId}/episode-{episodeNumber}",
                ObservedTitle = "A title - Episode 1",
                ObservedAt = now,
                MatchStatus = MediaObservationStatuses.Matched,
                MediaTitleId = titleId,
                ResolvedProgress = episodeNumber,
                EpisodeNumber = episodeNumber,
                ProviderSeriesId = "SERIES",
                SeasonNumber = 1,
                IsCatalogObservation = false,
                CreatedAt = now,
                UpdatedAt = now
            };
        }

        public MediaObservation MakeCatalogObservation(Guid titleId, string providerEpisodeId = "SAMEEPISODE", int episodeNumber = 1)
        {
            var observation = MakeWatchObservation(titleId, episodeNumber, providerEpisodeId);
            observation.IsCatalogObservation = true;
            observation.ResolvedProgress = null;
            observation.SiteMediaId = "catalog:SERIES:SEASON:1";
            observation.ObservedUrl = "https://www.crunchyroll.com/series/SERIES/show";
            observation.Episodes.Add(MakeCatalogEpisode(providerEpisodeId, episodeNumber));
            return observation;
        }

        public static MediaObservationEpisode MakeCatalogEpisode(string providerEpisodeId, int episodeNumber) =>
            new()
            {
                Id = Guid.NewGuid(),
                ProviderEpisodeId = providerEpisodeId,
                ProviderUrl = $"https://www.crunchyroll.com/watch/{providerEpisodeId}/episode-{episodeNumber}",
                EpisodeNumber = episodeNumber,
                EpisodeTitle = $"Episode {episodeNumber}"
            };

        public async ValueTask DisposeAsync()
        {
            if (_connection is not null)
            {
                await Db.DisposeAsync();
                await _connection.DisposeAsync();
            }
        }
    }
}
