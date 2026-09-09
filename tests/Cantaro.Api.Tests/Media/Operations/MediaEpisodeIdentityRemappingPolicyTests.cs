using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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
        private readonly SqliteConnection _connection;

        private RemappingFixture(SqliteConnection connection, ApplicationDbContext db, MediaEpisodeIdentityService service)
        {
            _connection = connection;
            Db = db;
            Service = service;
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
            var mappingService = new MediaProviderSeasonMappingService(
                db,
                NullLogger<MediaProviderSeasonMappingService>.Instance);
            return new RemappingFixture(
                connection,
                db,
                new MediaEpisodeIdentityService(
                    db,
                    mappingService,
                    NullLogger<MediaEpisodeIdentityService>.Instance));
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

        public MediaObservation MakeWatchObservation(Guid titleId, int episodeNumber)
        {
            var now = DateTimeOffset.UtcNow;
            return new MediaObservation
            {
                Id = Guid.NewGuid(),
                UserId = 1,
                SiteIdentifier = MediaObservationSiteIdentifiers.Crunchyroll,
                SiteMediaId = "SAMEEPISODE",
                ObservedUrl = "https://www.crunchyroll.com/watch/SAMEEPISODE/episode-1",
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

        public MediaObservation MakeCatalogObservation(Guid titleId)
        {
            var observation = MakeWatchObservation(titleId, 1);
            observation.IsCatalogObservation = true;
            observation.SiteMediaId = "catalog:SERIES:SEASON:1";
            observation.ObservedUrl = "https://www.crunchyroll.com/series/SERIES/show";
            observation.Episodes.Add(new MediaObservationEpisode
            {
                Id = Guid.NewGuid(),
                ProviderEpisodeId = "SAMEEPISODE",
                ProviderUrl = "https://www.crunchyroll.com/watch/SAMEEPISODE/episode-1",
                EpisodeNumber = 1,
                EpisodeTitle = "Episode 1"
            });
            return observation;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
