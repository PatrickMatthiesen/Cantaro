using System.Data.Common;
using System.Text.Json;
using Cantaro.Api.Controllers;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantaro.Api.Tests;

public partial class MediaObservationsApiTests
{
    [Theory]
    [InlineData(EntityState.Added, -1)]
    [InlineData(EntityState.Added, 0)]
    [InlineData(EntityState.Added, 1)]
    [InlineData(EntityState.Unchanged, -1)]
    [InlineData(EntityState.Unchanged, 0)]
    [InlineData(EntityState.Modified, -1)]
    [InlineData(EntityState.Modified, 0)]
    [InlineData(EntityState.Modified, 1)]
    public Task Submit_OrdersWatchEvidenceByObservationTimeOnSqlite(EntityState competingSave, int contenderMinutes) =>
        WithSqliteObservationDatabaseAsync(db => AssertWatchEvidenceOrderAsync(db, competingSave, contenderMinutes));

    [PostgresTheory]
    [InlineData(EntityState.Added, -1)]
    [InlineData(EntityState.Added, 0)]
    [InlineData(EntityState.Added, 1)]
    [InlineData(EntityState.Unchanged, -1)]
    [InlineData(EntityState.Unchanged, 0)]
    [InlineData(EntityState.Modified, -1)]
    [InlineData(EntityState.Modified, 0)]
    [InlineData(EntityState.Modified, 1)]
    public async Task Submit_OrdersWatchEvidenceByObservationTimeOnPostgres(EntityState competingSave, int contenderMinutes)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await AssertWatchEvidenceOrderAsync(schema.Db, competingSave, contenderMinutes);
    }

    private static async Task AssertWatchEvidenceOrderAsync(ApplicationDbContext db, EntityState competingSave, int contenderMinutes)
    {
        db.Users.Add(TestUserFactory.Create(MediaObservationFixture.TestUserId, MediaObservationFixture.TestEmail));
        await db.SaveChangesAsync();
        var timestamp = new DateTimeOffset(2026, 11, 20, 10, 2, 0, TimeSpan.Zero).AddMinutes(-Math.Max(contenderMinutes, 0));
        var first = CreateRequest("FIRST", timestamp.AddTicks(7));
        var second = CreateRequest("SECOND", timestamp.AddMinutes(contenderMinutes).AddTicks(7));
        MediaObservation? winner = null;
        if (competingSave == EntityState.Modified)
        {
            db.MediaObservations.Add(new MediaObservation
            {
                Id = Guid.NewGuid(), UserId = MediaObservationFixture.TestUserId, SiteIdentifier = first.SiteIdentifier,
                SiteMediaId = first.SiteMediaId, ObservedUrl = first.ObservedUrl, ObservedTitle = "Original watch",
                ObservedAt = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero),
                MatchStatus = MediaObservationStatuses.Pending
            });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }

        async Task SaveFirstWriterAsync()
        {
            var submission = await MediaObservationFixture.CreateController(db).Submit(first, default);
            var response = Assert.IsType<SubmitMediaObservationResponse>(Assert.IsType<OkObjectResult>(submission.Result).Value);
            Assert.Equal(competingSave == EntityState.Modified, response.WasDeduplicated);
            winner = await db.MediaObservations.AsNoTracking().SingleAsync();
        }

        if (competingSave == EntityState.Unchanged)
        {
            await SaveFirstWriterAsync();
        }
        var options = new DbContextOptionsBuilder<ApplicationDbContext>(
            (DbContextOptions<ApplicationDbContext>)db.GetService<IDbContextOptions>());
        var collision = new DuplicateObservationSaveInterceptor(SaveFirstWriterAsync, competingSave, null);
        if (competingSave != EntityState.Unchanged)
        {
            options.AddInterceptors(collision);
        }
        await using var contenderDb = new ApplicationDbContext(options.Options);
        var result = await MediaObservationFixture.CreateController(contenderDb).Submit(second, default);

        Assert.Equal(competingSave == EntityState.Added ? 1 : 0, collision.UniqueViolationCount);
        Assert.Equal(competingSave == EntityState.Modified ? 1 : 0, collision.ConcurrencyViolationCount);
        Assert.NotNull(winner);
        var duplicate = Assert.IsType<SubmitMediaObservationResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.True(duplicate.WasDeduplicated);
        Assert.Equal(winner.Id.ToString(), duplicate.ObservationId);
        Assert.False(duplicate.ProgressUpdated);
        var contenderIsLatest = contenderMinutes > 0 || (contenderMinutes == 0 && competingSave == EntityState.Unchanged);
        var expected = contenderIsLatest ? second : first;
        var expectedAt = timestamp.AddMinutes(contenderIsLatest ? contenderMinutes : 0);
        var saved = await db.MediaObservations.AsNoTracking().Include(row => row.Episodes).SingleAsync();
        Assert.Equal(expected.ObservedTitle, saved.ObservedTitle);
        Assert.Equal(expected.EpisodeTitle, saved.EpisodeTitle);
        Assert.Equal(expectedAt, saved.ObservedAt);
        Assert.Equal(expected.ObservedEpisodes.Single().ProviderEpisodeId, Assert.Single(saved.Episodes).ProviderEpisodeId);
        Assert.Equal(expected.NextEpisodeProviderId, saved.NextEpisodeProviderId);
        Assert.Equal(expected.NextEpisodeUrl, saved.NextEpisodeUrl);
        Assert.Equal(expected.NextEpisodeTitle, saved.NextEpisodeTitle);
        Assert.Equal(expected.NextEpisodeNumber, saved.NextEpisodeNumber);
        if (!contenderIsLatest)
        {
            Assert.Equal(winner.UpdatedAt, saved.UpdatedAt);
            Assert.Equal(winner.MatchAttemptCount, saved.MatchAttemptCount);
        }

        var raw = await db.MediaProviderEpisodes.AsNoTracking().ToDictionaryAsync(row => row.ProviderEpisodeId);
        Assert.Equal(5, raw.Count);
        Assert.Equal(2, raw["DUPLICATEWATCH"].SeenCount);
        if (contenderMinutes != 0)
        {
            Assert.Equal(expected.EpisodeTitle, raw["DUPLICATEWATCH"].EpisodeTitle);
            Assert.Equal(expectedAt, raw["DUPLICATEWATCH"].LastSeenAt);
        }
        foreach (var request in new[] { first, second })
        {
            var episode = Assert.Single(request.ObservedEpisodes);
            Assert.Equal(episode.EpisodeNumber, raw[episode.ProviderEpisodeId].ProviderEpisodeNumber);
            Assert.Equal(episode.EpisodeTitle, raw[episode.ProviderEpisodeId].EpisodeTitle);
            Assert.Equal(request.NextEpisodeNumber, raw[request.NextEpisodeProviderId!].ProviderEpisodeNumber);
            Assert.Equal(request.NextEpisodeTitle, raw[request.NextEpisodeProviderId!].EpisodeTitle);
        }

        static SubmitMediaObservationRequest CreateRequest(string source, DateTimeOffset observedAt) => new()
        {
            SiteIdentifier = "crunchyroll", SiteMediaId = "DUPLICATEWATCH",
            ObservedUrl = "https://www.crunchyroll.com/watch/DUPLICATEWATCH",
            ObservedTitle = $"{source} watch", ObservedAt = observedAt, EpisodeNumber = 2,
            EpisodeTitle = $"{source} watched episode",
            NextEpisodeProviderId = $"{source}NEXT", NextEpisodeUrl = $"https://www.crunchyroll.com/watch/{source}NEXT",
            NextEpisodeTitle = $"{source} next episode", NextEpisodeNumber = 3,
            ObservedEpisodes =
            [
                new ObservedProviderEpisodeDto
                {
                    ProviderEpisodeId = $"{source}CATALOG",
                    ProviderUrl = $"https://www.crunchyroll.com/watch/{source}CATALOG",
                    EpisodeNumber = 4, EpisodeTitle = $"{source} catalog episode"
                }
            ]
        };
    }

    [Theory]
    [InlineData("reject")]
    [InlineData("resolve")]
    [InlineData("retry")]
    [InlineData("match")]
    [InlineData("submit-update")]
    [InlineData("submit-match")]
    public Task NewerSubmissionSurvivesObservationWriteOnSqlite(string operation) =>
        WithSqliteObservationDatabaseAsync(db => AssertNewerSubmissionSurvivesAsync(db, operation));

    [PostgresTheory]
    [InlineData("reject")]
    [InlineData("resolve")]
    [InlineData("retry")]
    [InlineData("match")]
    [InlineData("submit-update")]
    [InlineData("submit-match")]
    public async Task NewerSubmissionSurvivesObservationWriteOnPostgres(string operation)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await AssertNewerSubmissionSurvivesAsync(schema.Db, operation);
    }

    [Theory]
    [InlineData("reject")]
    [InlineData("resolve")]
    [InlineData("retry")]
    public Task StaleReviewConflictsOnSqlite(string operation) =>
        WithSqliteObservationDatabaseAsync(db => AssertNewerSubmissionSurvivesAsync(db, operation, staleReview: true));

    [PostgresTheory]
    [InlineData("reject")]
    [InlineData("resolve")]
    [InlineData("retry")]
    public async Task StaleReviewConflictsOnPostgres(string operation)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await AssertNewerSubmissionSurvivesAsync(schema.Db, operation, staleReview: true);
    }

    private static async Task AssertNewerSubmissionSurvivesAsync(ApplicationDbContext db, string operation,
        bool staleReview = false)
    {
        var usesMappingTransaction = operation is "resolve" or "retry" or "submit-match";
        var submitsWatchUpdate = operation == "submit-update";
        var site = usesMappingTransaction || staleReview || submitsWatchUpdate
            ? MediaObservationSiteIdentifiers.Crunchyroll : "netflix";
        var title = new MediaTitle
        {
            Id = Guid.NewGuid(), CanonicalTitle = "Canonical title", MediaKind = MediaKinds.Anime,
            EpisodeCount = 12, SupportsEpisodeProgress = true, PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode
        };
        var observation = new MediaObservation
        {
            Id = Guid.NewGuid(), UserId = MediaObservationFixture.TestUserId, SiteIdentifier = site,
            SiteMediaId = "OVERLAP2", ObservedTitle = "Older submission",
            ObservedUrl = site == MediaObservationSiteIdentifiers.Crunchyroll
                ? "https://www.crunchyroll.com/watch/OVERLAP2" : "https://www.netflix.com/watch/123",
            EpisodeNumber = 2, MatchStatus = MediaObservationStatuses.Ambiguous
        };
        var candidate = new MediaObservationCandidate
        {
            Id = Guid.NewGuid(), MediaObservationId = observation.Id, MediaTitleId = title.Id,
            Title = title.CanonicalTitle, MediaKind = title.MediaKind,
            CandidateSource = MediaObservationCandidateSources.ProviderLinkExact
        };
        db.AddRange(TestUserFactory.Create(MediaObservationFixture.TestUserId, MediaObservationFixture.TestEmail),
            title, observation, candidate);
        if (staleReview || submitsWatchUpdate)
        {
            db.MediaLibraryEntries.Add(new MediaLibraryEntry
            {
                Id = Guid.NewGuid(), UserId = observation.UserId, MediaTitleId = title.Id,
                    Status = MediaLibraryStatuses.Current, ProgressEpisodes = submitsWatchUpdate ? 2 : 1
            });
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        DateTimeOffset? winningVersion = null;

        async Task SaveNewerSubmissionAsync()
        {
            var current = await db.MediaObservations.SingleAsync();
            current.ObservedTitle = "Newer submission";
            current.ObservedAt = DateTimeOffset.UtcNow.AddMinutes(1);
            current.EpisodeTitle = "Newer episode evidence";
            current.EpisodeNumber = 4;
            current.MatchStatus = MediaObservationStatuses.Ambiguous;
            current.Touch(current.UpdatedAt);
            winningVersion = current.UpdatedAt;
            await db.SaveChangesAsync();
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>(
            (DbContextOptions<ApplicationDbContext>)db.GetService<IDbContextOptions>());
        MediaObservationVersionRequest? version = null;
        if (staleReview)
        {
            var review = await MediaObservationFixture.CreateController(db).Get(observation.Id, default);
            var dto = Assert.IsType<MediaObservationDto>(Assert.IsType<OkObjectResult>(review.Result).Value);
            var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            using var body = JsonDocument.Parse(json);
            version = new MediaObservationVersionRequest
            {
                ExpectedUpdatedAt = body.RootElement.GetProperty("updatedAt").GetDateTimeOffset()
            };
            Assert.Equal((await db.MediaObservations.AsNoTracking().SingleAsync()).UpdatedAt, version.ExpectedUpdatedAt);
            await SaveNewerSubmissionAsync();
        }
        else if (usesMappingTransaction)
        {
            options.AddInterceptors(new BeforeObservationTransaction(SaveNewerSubmissionAsync,
                row => operation != "submit-match" || row.MatchAttemptCount > 0));
        }
        else if (operation != "match")
        {
            options.AddInterceptors(new DuplicateObservationSaveInterceptor(SaveNewerSubmissionAsync, EntityState.Modified, null));
        }
        const string nextEpisodeUrl = "https://www.crunchyroll.com/watch/OVERLAP3";
        await using var staleDb = new ApplicationDbContext(options.Options);
        var controller = MediaObservationFixture.CreateController(staleDb);
        switch (operation)
        {
            case "reject":
                AssertUpdatedConflict((await controller.Reject(observation.Id, default, version)).Result);
                break;
            case "resolve":
                AssertUpdatedConflict((await controller.Resolve(observation.Id,
                    new ResolveMediaObservationRequest
                    {
                        CandidateId = candidate.Id, ExpectedUpdatedAt = version?.ExpectedUpdatedAt
                    }, default)).Result);
                break;
            case "retry":
                AssertUpdatedConflict((await controller.Retry(observation.Id, default, version)).Result);
                break;
            case "match":
                var stale = await staleDb.MediaObservations.Include(row => row.Candidates).SingleAsync();
                await SaveNewerSubmissionAsync();
                var matcher = new MediaObservationMatchingService(staleDb,
                    new MediaProviderSeasonMappingService(staleDb, NullLogger<MediaProviderSeasonMappingService>.Instance),
                    new MediaRelationGraphRefreshQueue(), NullLogger<MediaObservationMatchingService>.Instance);
                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => matcher.ProcessObservationAsync(stale, default));
                break;
            default:
                var result = await controller.Submit(new SubmitMediaObservationRequest
                {
                    SiteIdentifier = site, SiteMediaId = observation.SiteMediaId,
                    ObservedTitle = "Stale request", ObservedUrl = observation.ObservedUrl, EpisodeNumber = 2,
                    ProviderSeriesId = submitsWatchUpdate ? "OVERLAPSERIES" : null,
                    ProviderSeasonId = submitsWatchUpdate ? "OVERLAPSEASON" : null,
                    NextEpisodeProviderId = submitsWatchUpdate ? "OVERLAP3" : null,
                    NextEpisodeUrl = submitsWatchUpdate ? nextEpisodeUrl : null,
                    NextEpisodeNumber = submitsWatchUpdate ? 3 : null
                }, default);
                var response = Assert.IsType<SubmitMediaObservationResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
                Assert.True(response.WasDeduplicated);
                Assert.Equal(observation.Id.ToString(), response.ObservationId);
                Assert.Equal(MediaObservationStatuses.Ambiguous, response.MatchStatus);
                break;
        }

        Assert.NotNull(winningVersion);
        var saved = await db.MediaObservations.AsNoTracking().Include(row => row.Candidates).SingleAsync();
        Assert.Equal(observation.Id, saved.Id);
        Assert.Equal(winningVersion, saved.UpdatedAt);
        Assert.Equal(MediaObservationStatuses.Ambiguous, saved.MatchStatus);
        Assert.Equal("Newer submission", saved.ObservedTitle);
        Assert.Equal("Newer episode evidence", saved.EpisodeTitle);
        Assert.Equal(4, saved.EpisodeNumber);
        Assert.Equal(candidate.Id, Assert.Single(saved.Candidates).Id);
        Assert.Empty(await db.MediaProviderEpisodeMappings.ToListAsync());
        Assert.Empty(await db.MediaProviderSeasonMappings.ToListAsync());
        if (submitsWatchUpdate)
        {
            Assert.Null(saved.NextEpisodeProviderId);
            var rawEpisodes = await db.MediaProviderEpisodes.AsNoTracking()
                .OrderBy(row => row.ProviderEpisodeId).ToListAsync();
            Assert.Equal(["OVERLAP2", "OVERLAP3"], rawEpisodes.Select(row => row.ProviderEpisodeId));
            Assert.All(rawEpisodes, row => Assert.Equal(1, row.SeenCount));
            Assert.Equal(3, rawEpisodes[1].ProviderEpisodeNumber);
            var mappings = new MediaProviderSeasonMappingService(db, NullLogger<MediaProviderSeasonMappingService>.Instance);
            await mappings.EstablishAsync(site, "OVERLAPSERIES", "OVERLAPSEASON", null, title.Id, 0,
                MediaProviderSeasonMappingSources.Manual, 1m, default);
            var playback = new MediaEpisodeIdentityService(db, mappings, NullLogger<MediaEpisodeIdentityService>.Instance);
            var next = Assert.IsType<MediaContinueWatchingDto>(
                await playback.ResolveContinueWatchingAsync(observation.UserId, title.Id, default));
            Assert.Equal("direct", next.Outcome);
            Assert.Equal(3, next.EpisodeNumber);
            Assert.Equal(nextEpisodeUrl, next.Url);
        }
        if (staleReview || operation is "resolve" or "retry")
        {
            Assert.Empty(await db.MediaProviderEpisodes.ToListAsync());
        }
        if (staleReview)
        {
            var entry = await db.MediaLibraryEntries.AsNoTracking().SingleAsync();
            Assert.Equal(1, entry.ProgressEpisodes);
            Assert.Equal(MediaLibraryStatuses.Current, entry.Status);
            Assert.Empty(await db.MediaProviderOperations.ToListAsync());
        }
    }

    [Fact]
    public Task Submit_SkipsProgressAfterObservationIsRejectedOnSqlite() =>
        WithSqliteObservationDatabaseAsync(AssertNoProgressAfterRejectionAsync);

    [PostgresFact]
    public async Task Submit_SkipsProgressAfterObservationIsRejectedOnPostgres()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await AssertNoProgressAfterRejectionAsync(schema.Db);
    }

    private static async Task AssertNoProgressAfterRejectionAsync(ApplicationDbContext db)
    {
        const int userId = MediaObservationFixture.TestUserId;
        const string episodeId = "REJECTED4";
        var title = new MediaTitle
        {
            Id = Guid.NewGuid(), CanonicalTitle = "Rejected Playback", MediaKind = MediaKinds.Anime,
            EpisodeCount = 12, SupportsEpisodeProgress = true, PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode
        };
        var entry = new MediaLibraryEntry
        {
            Id = Guid.NewGuid(), UserId = userId, MediaTitleId = title.Id,
            Status = MediaLibraryStatuses.Current, ProgressEpisodes = 1
        };
        var link = new MediaProviderLink
        {
            Id = Guid.NewGuid(), MediaTitleId = title.Id, Provider = "anilist",
            ExternalId = "7124", LinkSource = MediaMappingSources.Imported
        };
        var account = new ConnectedServiceAccount
        {
            Id = 7124, UserId = userId, Service = "anilist", ExternalAccountId = "viewer-7124",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        db.AddRange(TestUserFactory.Create(userId, MediaObservationFixture.TestEmail), title, entry, link, account,
            new MediaLibraryProviderBinding
            {
                Id = Guid.NewGuid(), MediaLibraryEntryId = entry.Id, MediaProviderLinkId = link.Id,
                ConnectedServiceAccountId = account.Id, ProviderAccountId = account.ExternalAccountId
            });
        await db.SaveChangesAsync();
        var mappings = new MediaProviderSeasonMappingService(db, NullLogger<MediaProviderSeasonMappingService>.Instance);
        await mappings.EstablishDirectAsync("crunchyroll", episodeId, title.Id, 4,
            MediaProviderSeasonMappingSources.Manual, default);

        async Task RejectMatchedObservationAsync()
        {
            var current = await db.MediaObservations.AsNoTracking().SingleAsync();
            Assert.Equal(MediaObservationStatuses.Matched, current.MatchStatus);
            Assert.Equal(4, current.ResolvedProgress);
            var rejected = await MediaObservationFixture.CreateController(db).Reject(current.Id, default,
                new MediaObservationVersionRequest { ExpectedUpdatedAt = current.UpdatedAt });
            Assert.IsType<OkObjectResult>(rejected.Result);
            Assert.Empty(await db.MediaObservations.ToListAsync());
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>(
                (DbContextOptions<ApplicationDbContext>)db.GetService<IDbContextOptions>())
            .AddInterceptors(new BeforeObservationTransaction(RejectMatchedObservationAsync,
                row => row.MatchStatus == MediaObservationStatuses.Matched));
        await using var submitDb = new ApplicationDbContext(options.Options);
        var submission = await MediaObservationFixture.CreateController(submitDb).Submit(new SubmitMediaObservationRequest
        {
            SiteIdentifier = "crunchyroll", SiteMediaId = episodeId, ObservedTitle = title.CanonicalTitle,
            ObservedUrl = $"https://www.crunchyroll.com/watch/{episodeId}", EpisodeNumber = 4
        }, default);

        var response = Assert.IsType<SubmitMediaObservationResponse>(Assert.IsType<OkObjectResult>(submission.Result).Value);
        Assert.False(response.ProgressUpdated);
        Assert.Empty(await db.MediaObservations.ToListAsync());
        Assert.Equal(1, await db.MediaLibraryEntries.Select(row => row.ProgressEpisodes).SingleAsync());
        Assert.Empty(await db.MediaProviderOperations.ToListAsync());
    }

    [Fact]
    public Task Resolve_SkipsProgressAfterConcurrentManualRemapOnSqlite() =>
        WithSqliteObservationDatabaseAsync(AssertNoProgressAfterManualRemapAsync);

    [PostgresFact]
    public async Task Resolve_SkipsProgressAfterConcurrentManualRemapOnPostgres()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await AssertNoProgressAfterManualRemapAsync(schema.Db);
    }

    private static async Task AssertNoProgressAfterManualRemapAsync(ApplicationDbContext db)
    {
        const int userId = MediaObservationFixture.TestUserId;
        const int otherUserId = userId + 1;
        const string episodeId = "MANUALREMAP";
        var first = CreateReviewTitle("First Review Title");
        var second = CreateReviewTitle("Second Review Title");
        var (observation, candidate) = CreateReview(first, userId);
        var (otherObservation, otherCandidate) = CreateReview(second, otherUserId);
        db.AddRange(first, second, observation, candidate, otherObservation, otherCandidate,
            TestUserFactory.Create(userId, MediaObservationFixture.TestEmail),
            TestUserFactory.Create(otherUserId, "other-reviewer@example.test"));
        db.MediaLibraryEntries.AddRange(new[] { first, second }.Select(title => new MediaLibraryEntry
        {
            Id = Guid.NewGuid(), UserId = userId, MediaTitleId = title.Id,
            Status = MediaLibraryStatuses.Current, ProgressEpisodes = 1
        }));
        await db.SaveChangesAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>(
                (DbContextOptions<ApplicationDbContext>)db.GetService<IDbContextOptions>())
            .AddInterceptors(new BeforeObservationTransaction(async () =>
            {
                Assert.Equal(first.Id, (await db.MediaProviderEpisodeMappings.AsNoTracking().SingleAsync()).MediaTitleId);
                var competingReview = await MediaObservationFixture.CreateController(db, userId: otherUserId)
                    .Resolve(otherObservation.Id, new ResolveMediaObservationRequest { CandidateId = otherCandidate.Id }, default);
                Assert.IsType<OkObjectResult>(competingReview.Result);
            }, row => row.MatchStatus == MediaObservationStatuses.Matched));
        await using var reviewDb = new ApplicationDbContext(options.Options);
        var result = await MediaObservationFixture.CreateController(reviewDb)
            .Resolve(observation.Id, new ResolveMediaObservationRequest { CandidateId = candidate.Id }, default);

        Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal(second.Id, (await db.MediaProviderEpisodeMappings.AsNoTracking().SingleAsync()).MediaTitleId);
        Assert.All(await db.MediaLibraryEntries.AsNoTracking().ToListAsync(), entry => Assert.Equal(1, entry.ProgressEpisodes));

        static (MediaObservation, MediaObservationCandidate) CreateReview(MediaTitle title, int reviewerId)
        {
            var review = new MediaObservation
            {
                Id = Guid.NewGuid(), UserId = reviewerId, ObservedTitle = title.CanonicalTitle,
                MatchStatus = MediaObservationStatuses.Pending, SiteIdentifier = "crunchyroll", SiteMediaId = episodeId,
                ObservedUrl = $"https://www.crunchyroll.com/watch/{episodeId}", ObservedAt = DateTimeOffset.UtcNow, EpisodeNumber = 4
            };
            return (review, new MediaObservationCandidate
            {
                Id = Guid.NewGuid(), MediaObservationId = review.Id, MediaTitleId = title.Id,
                Title = title.CanonicalTitle, MediaKind = title.MediaKind,
                CandidateSource = MediaObservationCandidateSources.ProviderLinkExact
            });
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Resolve_SkipsProgressAfterConcurrentMappingDeleteOnSqlite(bool selectProviderResult) =>
        WithSqliteObservationDatabaseAsync(db => AssertNoProgressAfterMappingDeleteAsync(db, selectProviderResult));

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Resolve_SkipsProgressAfterConcurrentMappingDeleteOnPostgres(bool selectProviderResult)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await AssertNoProgressAfterMappingDeleteAsync(schema.Db, selectProviderResult);
    }

    private static async Task AssertNoProgressAfterMappingDeleteAsync(ApplicationDbContext db, bool selectProviderResult)
    {
        const int userId = MediaObservationFixture.TestUserId;
        const string episodeId = "MANUALDELETE";
        const string externalId = "90001";
        var title = CreateReviewTitle("Mapping deletion review");
        var observation = new MediaObservation
        {
            Id = Guid.NewGuid(), UserId = userId, ObservedTitle = title.CanonicalTitle,
            MatchStatus = MediaObservationStatuses.Pending, SiteIdentifier = "crunchyroll", SiteMediaId = episodeId,
            ObservedUrl = $"https://www.crunchyroll.com/watch/{episodeId}", ObservedAt = DateTimeOffset.UtcNow, EpisodeNumber = 4,
            ProviderSeriesId = selectProviderResult ? "SERIES" : null,
            ProviderSeasonId = selectProviderResult ? "SEASON" : null
        };
        var candidate = new MediaObservationCandidate
        {
            Id = Guid.NewGuid(), MediaObservationId = observation.Id, MediaTitleId = title.Id,
            Title = title.CanonicalTitle, MediaKind = title.MediaKind,
            CandidateSource = MediaObservationCandidateSources.ProviderLinkExact
        };
        var entry = new MediaLibraryEntry
        {
            Id = Guid.NewGuid(), UserId = userId, MediaTitleId = title.Id,
            Status = MediaLibraryStatuses.Current, ProgressEpisodes = 1
        };
        var link = new MediaProviderLink
        {
            Id = Guid.NewGuid(), MediaTitleId = title.Id, Provider = "anilist", ExternalId = externalId,
            LinkSource = MediaMappingSources.UserConfirmed
        };
        db.AddRange(title, observation, candidate, entry, link,
            new MediaLibraryProviderBinding
            {
                Id = Guid.NewGuid(), MediaLibraryEntryId = entry.Id, MediaProviderLinkId = link.Id,
                ProviderAccountId = "reviewer"
            },
            TestUserFactory.Create(userId, MediaObservationFixture.TestEmail));
        await db.SaveChangesAsync();
        var mappings = new MediaProviderSeasonMappingService(db, NullLogger<MediaProviderSeasonMappingService>.Instance);
        var mappingController = new MediaCrunchyrollMappingsController(db, mappings);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>(
                (DbContextOptions<ApplicationDbContext>)db.GetService<IDbContextOptions>())
            .AddInterceptors(new BeforeObservationTransaction(async () =>
            {
                var effective = await mappings.ResolveAsync(observation, default);
                Assert.Equal((title.Id, (int?)4), (effective!.Title.Id, effective.EpisodeNumber));
                var deleted = selectProviderResult
                    ? await mappingController.DeleteSeason(title.Id,
                        await db.MediaProviderSeasonMappings.Select(row => row.Id).SingleAsync(), default)
                    : await mappingController.DeleteEpisode(title.Id, episodeId, default);
                Assert.IsType<NoContentResult>(deleted);
                Assert.Null(await mappings.ResolveAsync(observation, default));
            }, row => row.MatchStatus == MediaObservationStatuses.Matched));
        await using var reviewDb = new ApplicationDbContext(options.Options);
        var controller = MediaObservationFixture.CreateController(reviewDb, new SingleProviderRegistry(new SearchOnlyMediaProvider()));
        var request = selectProviderResult
            ? new ResolveMediaObservationRequest { ProviderId = "anilist", ProviderMediaId = externalId }
            : new ResolveMediaObservationRequest { CandidateId = candidate.Id };
        var result = await controller.Resolve(observation.Id, request, default);

        Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal(1, await db.MediaLibraryEntries.Select(row => row.ProgressEpisodes).SingleAsync());
        Assert.Empty(await db.MediaProviderOperations.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task RawEpisodeNumberDoesNotCrossSeasonsOnSqlite(bool newestFirst) =>
        WithSqliteObservationDatabaseAsync(db => AssertRawEpisodeNumberDoesNotCrossSeasonsAsync(db, newestFirst));

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RawEpisodeNumberDoesNotCrossSeasonsOnPostgres(bool newestFirst)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await AssertRawEpisodeNumberDoesNotCrossSeasonsAsync(schema.Db, newestFirst);
    }

    private static async Task AssertRawEpisodeNumberDoesNotCrossSeasonsAsync(ApplicationDbContext db, bool newestFirst)
    {
        var mappings = new MediaProviderSeasonMappingService(db, NullLogger<MediaProviderSeasonMappingService>.Instance);
        var identities = new MediaEpisodeIdentityService(db, mappings, NullLogger<MediaEpisodeIdentityService>.Instance);
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var older = Watch("SEASON1", 1, 13, now.AddMinutes(-1));
        var newer = Watch("SEASON2", 2, null, now);
        await identities.RecordObservationAsync(newestFirst ? newer : older, default);
        await identities.RecordObservationAsync(newestFirst ? older : newer, default);
        var raw = await db.MediaProviderEpisodes.SingleAsync();
        Assert.Equal("SEASON2", raw.ProviderSeasonId);
        Assert.Equal(2, raw.ProviderSeasonNumber);
        Assert.Null(raw.ProviderEpisodeNumber);
        Assert.Equal(now, raw.LastSeenAt);
        Assert.Equal(2, raw.SeenCount);

        static MediaObservation Watch(string seasonId, int season, int? episode, DateTimeOffset at) => new()
        {
            SiteIdentifier = "crunchyroll", SiteMediaId = "CROSSEASON", ObservedTitle = "Series",
            ObservedUrl = "https://www.crunchyroll.com/watch/CROSSEASON", ProviderSeriesId = "SERIES",
            ProviderSeasonId = seasonId, SeasonNumber = season, EpisodeNumber = episode, ObservedAt = at,
            MatchStatus = MediaObservationStatuses.Pending
        };
    }

    [Fact]
    public Task Submit_PersistsReplacementAsPendingBeforeMatching() =>
        WithSqliteObservationDatabaseAsync(async db =>
        {
            const int userId = MediaObservationFixture.TestUserId;
            const string episodeId = "REPLACEDWATCH";
            const string correctedTitle = "Corrected report";
            var title = CreateReviewTitle("Old review title");
            var entryId = Guid.NewGuid();
            var previous = new MediaObservation
            {
                Id = Guid.NewGuid(), UserId = userId, ObservedTitle = title.CanonicalTitle,
                SiteIdentifier = "crunchyroll", SiteMediaId = episodeId, ObservedUrl = $"https://www.crunchyroll.com/watch/{episodeId}",
                MatchStatus = MediaObservationStatuses.Matched, MediaTitleId = title.Id,
                EpisodeNumber = 1, ResolvedProgress = 1, ResolvedLibraryEntryId = entryId,
                ObservedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
            };
            db.AddRange(title, previous, TestUserFactory.Create(userId, MediaObservationFixture.TestEmail),
                new MediaLibraryEntry
                {
                    Id = entryId, UserId = userId, MediaTitleId = title.Id, ProgressEpisodes = 1,
                    Status = MediaLibraryStatuses.Current
                });
            await db.SaveChangesAsync();

            var checkedRetention = false;
            var options = new DbContextOptionsBuilder<ApplicationDbContext>(
                    (DbContextOptions<ApplicationDbContext>)db.GetService<IDbContextOptions>())
                .AddInterceptors(new BeforeObservationTransaction(async () =>
                {
                    checkedRetention = true;
                    Assert.Equal(0, await new MediaObservationRetentionService(db).CleanupCompletedAsync());
                    var saved = await db.MediaObservations.AsNoTracking().SingleAsync();
                    Assert.Equal(MediaObservationStatuses.Pending, saved.MatchStatus);
                    Assert.Equal(correctedTitle, saved.ObservedTitle);
                    Assert.Null(saved.MediaTitleId);
                    Assert.Null(saved.ResolvedProgress);
                    Assert.Null(saved.ResolvedLibraryEntryId);
                }, row => row.ObservedTitle == correctedTitle));
            await using var submitDb = new ApplicationDbContext(options.Options);
            var result = await MediaObservationFixture.CreateController(submitDb).Submit(new SubmitMediaObservationRequest
            {
                SiteIdentifier = "crunchyroll", SiteMediaId = episodeId, ObservedUrl = previous.ObservedUrl,
                ObservedTitle = correctedTitle, EpisodeNumber = 2
            }, default);

            Assert.IsType<OkObjectResult>(result.Result);
            Assert.True(checkedRetention);
        });

    private static MediaTitle CreateReviewTitle(string name) => new()
    {
        Id = Guid.NewGuid(), CanonicalTitle = name, MediaKind = MediaKinds.Anime,
        EpisodeCount = 12, SupportsEpisodeProgress = true,
        PrimaryProgressDimension = MediaProgressDimensions.Episode, ReleaseStatusDimension = MediaProgressDimensions.Episode
    };

    private static void AssertUpdatedConflict(ActionResult? result)
    {
        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var payload = JsonSerializer.SerializeToElement(conflict.Value);
        Assert.Equal("Observation was updated by a newer submission.", payload.GetProperty("error").GetString());
    }

    internal static async Task WithSqliteObservationDatabaseAsync(Func<ApplicationDbContext, Task> test)
    {
        await using var keeper = new SqliteConnection($"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await keeper.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(keeper.ConnectionString).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await test(db);
    }

    internal sealed class BeforeObservationTransaction(Func<Task> supersede, Func<MediaObservation, bool> shouldRun)
        : DbTransactionInterceptor
    {
        private bool _interleaved;

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            if (!_interleaved && eventData.Context!.ChangeTracker.Entries<MediaObservation>()
                    .Any(entry => shouldRun(entry.Entity)))
            {
                _interleaved = true;
                await supersede();
            }
            return result;
        }
    }
}
