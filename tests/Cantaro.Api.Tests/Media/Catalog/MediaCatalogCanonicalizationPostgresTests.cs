using Cantaro.Api.Data;
using Cantaro.Api.Controllers;
using Cantaro.Api.Models;
using System.Data.Common;
using System.Diagnostics;
using System.Security.Claims;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Cantaro.Api.Tests.Media.Catalog;

[Collection("Media catalog PostgreSQL")]
public sealed class MediaCatalogCanonicalizationPostgresTests(ITestOutputHelper output)
{
    [PostgresFact]
    public async Task CanonicalizeAsync_RetriesSummaryFillWhenDetailsChangeAfterPreload()
    {
        await using var schema = await SchemaContext.CreateAsync();
        var candidate = Candidate("anilist", "211778", "The World's Strongest Witch", "10");
        var ids = await MakeService(schema.Options).CanonicalizeAsync([candidate], default);
        var titleId = Assert.Single(ids.Values);
        var interleaved = new DetailUpdateBeforeSave(async cancellationToken =>
        {
            var title = await schema.Db.MediaTitles.SingleAsync(item => item.Id == titleId, cancellationToken);
            title.Synopsis = "Full details loaded concurrently";
            title.Synonyms.Add("Detailed alias");
            await schema.Db.SaveChangesAsync(cancellationToken);
        });
        var racingOptions = new DbContextOptionsBuilder<ApplicationDbContext>(schema.Options)
            .AddInterceptors(interleaved).Options;
        var summary = candidate with { Result = candidate.Result with
        {
            Synopsis = "Less detailed summary",
            Synonyms = ["Search alias"]
        } };

        await MakeService(racingOptions).CanonicalizeAsync([summary], default);

        var persisted = await schema.Db.MediaTitles.AsNoTracking().SingleAsync(item => item.Id == titleId);
        Assert.Equal("Full details loaded concurrently", persisted.Synopsis);
        Assert.Contains("Detailed alias", persisted.Synonyms);
        Assert.Contains("Search alias", persisted.Synonyms);
        Assert.Equal(1, interleaved.DetailWrites);
        Assert.Equal(2, interleaved.SaveAttempts);
    }

    [PostgresFact]
    public async Task ImportAsync_RetriesIdentityRaceAtomicallyWithoutSavingCallerContextChanges()
    {
        await using var schema = await SchemaContext.CreateAsync();
        var user = TestUserFactory.Create(47811, "catalog-import-race@example.test");
        var account = new ConnectedServiceAccount
        {
            UserId = user.Id,
            Service = "anilist",
            ExternalAccountId = "catalog-import-race-account",
            ConnectionState = "connected",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        schema.Db.AddRange(user, account);
        await schema.Db.SaveChangesAsync();

        var candidateResult = Candidate("anilist", "211778", "The World's Strongest Witch", "10").Result
            with { CrossReferences = [] };
        var candidate = new MediaCatalogSearchCandidate("anilist", candidateResult);
        var interleaved = new ImportIdentityClaimBeforeSave(() => MakeService(schema.Options)
            .CanonicalizeAsync([candidate], default));
        var importOptions = new DbContextOptionsBuilder<ApplicationDbContext>(schema.Options)
            .AddInterceptors(interleaved).Options;
        await using var callerContext = new ApplicationDbContext(importOptions);
        var pendingChange = new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = "Unrelated caller change",
            MediaKind = "anime",
            PrimaryProgressDimension = "episode",
            ReleaseStatusDimension = "episode",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        callerContext.MediaTitles.Add(pendingChange);
        var importService = new MediaLibraryImportService(callerContext, new MediaLibraryEventHub(),
            MakeService(importOptions), NullLogger<MediaLibraryImportService>.Instance);
        var importedAt = DateTimeOffset.UtcNow;
        var import = new MediaProviderLibraryImportResult
        {
            ProviderId = "anilist",
            ImportedAt = importedAt,
            Items =
            [
                new MediaProviderLibraryItem
                {
                    ProviderMediaId = "211778",
                    Title = "The World's Strongest Witch",
                    MediaKind = "anime",
                    Status = MediaLibraryStatuses.Planned,
                    ProgressEpisodes = 3,
                    EpisodeCount = 12,
                    PrimaryProgressDimension = "episode",
                    ReleaseStatusDimension = "episode",
                    CrossReferences = []
                }
            ]
        };

        var result = await importService.ImportAsync(user.Id, account, import, default);

        Assert.Equal(1, interleaved.ClaimCount);
        Assert.Equal(2, interleaved.SaveAttempts);
        Assert.Equal(0, result.CreatedTitles);
        Assert.Equal(1, result.CreatedEntries);
        Assert.Equal(1, await schema.Db.MediaTitles.CountAsync());
        Assert.Equal(1, await schema.Db.MediaProviderLinks.CountAsync());
        var entry = await schema.Db.MediaLibraryEntries.SingleAsync();
        Assert.Equal(user.Id, entry.UserId);
        Assert.Equal(3, entry.ProgressEpisodes);
        var binding = await schema.Db.MediaLibraryProviderBindings.Include(item => item.MediaProviderLink).SingleAsync();
        Assert.Equal(account.Id, binding.ConnectedServiceAccountId);
        Assert.Equal("211778", binding.MediaProviderLink!.ExternalId);
        Assert.Equal(entry.MediaTitleId, binding.MediaProviderLink.MediaTitleId);
        Assert.Equal(EntityState.Added, callerContext.Entry(pendingChange).State);
        Assert.False(await schema.Db.MediaTitles.AnyAsync(title => title.Id == pendingChange.Id));

        pendingChange.Synopsis = "Caller context still works";
        await callerContext.SaveChangesAsync();
        Assert.Equal("Caller context still works", await schema.Db.MediaTitles.AsNoTracking()
            .Where(title => title.Id == pendingChange.Id).Select(title => title.Synopsis).SingleAsync());
        Assert.Equal(1, await schema.Db.MediaLibraryEntries.CountAsync());
    }

    [PostgresFact]
    public async Task Resolve_AddToLibrary_RetriesProviderIdentityRaceWithoutRepeatingRemoteCalls()
    {
        await using var schema = await SchemaContext.CreateAsync();
        const int userId = 47812;
        const string providerId = "anilist";
        const string providerMediaId = "observation-race-1";
        var user = TestUserFactory.Create(userId, "observation-add-race@example.test");
        var account = new ConnectedServiceAccount
        {
            UserId = user.Id,
            Service = providerId,
            ExternalAccountId = "observation-race-account",
            ConnectionState = "connected",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var observation = new MediaObservation
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            SiteIdentifier = MediaObservationSiteIdentifiers.Crunchyroll,
            ObservedUrl = "https://www.crunchyroll.com/watch/RACE/episode-3",
            SiteMediaId = "RACE",
            ObservedTitle = "Observation race title episode 3",
            ProgressHint = "3",
            ObservedAt = DateTimeOffset.UtcNow,
            MatchStatus = MediaObservationStatuses.NoMatch,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        schema.Db.AddRange(user, account, observation);
        await schema.Db.SaveChangesAsync();

        var details = new MediaProviderTitleDetails
        {
            ProviderId = providerId,
            ProviderMediaId = providerMediaId,
            Title = "Observation race title",
            MediaKind = MediaKinds.Anime,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            EpisodeCount = 12,
            CrossReferences = []
        };
        var provider = new ObservationRaceMediaProvider(account, details);
        var interleaved = new ProviderIdentityClaimBeforeSave(() => MakeService(schema.Options)
            .CanonicalizeAsync(
            [new MediaCatalogSearchCandidate(providerId, new CachedMediaProviderSearchResult(
                providerMediaId, details.Title, details.MediaKind, details.PosterUrl, details.StartYear)
            {
                PrimaryProgressDimension = details.PrimaryProgressDimension,
                ReleaseStatusDimension = details.ReleaseStatusDimension,
                EpisodeCount = details.EpisodeCount
            })], default));
        var controllerOptions = new DbContextOptionsBuilder<ApplicationDbContext>(schema.Options)
            .AddInterceptors(interleaved).Options;
        await using var controllerDb = new ApplicationDbContext(controllerOptions);
        var controllerObservation = await controllerDb.MediaObservations.SingleAsync(item => item.Id == observation.Id);
        var controller = CreateObservationsController(controllerDb, new MediaProviderRegistry([provider]), userId,
            "observation-add-race@example.test");

        var result = await controller.Resolve(observation.Id, new ResolveMediaObservationRequest
        {
            ProviderId = providerId,
            ProviderMediaId = providerMediaId,
            AddToLibraryConfirmed = true
        }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<MediaObservationDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(MediaObservationStatuses.Matched, response.MatchStatus);
        Assert.Equal("3", response.ProgressHint);
        Assert.Equal(3, response.ResolvedProgress);
        Assert.Equal(1, interleaved.ClaimCount);
        Assert.Equal(1, interleaved.UniqueViolationCount);
        Assert.Equal(1, provider.DetailsCalls);
        Assert.Equal(1, provider.StatusCalls);
        Assert.Equal(1, await schema.Db.MediaTitles.CountAsync());
        Assert.Equal(1, await schema.Db.MediaProviderLinks.CountAsync());
        var entry = await schema.Db.MediaLibraryEntries.SingleAsync();
        Assert.Equal(user.Id, entry.UserId);
        Assert.Equal(3, entry.ProgressEpisodes);
        var binding = await schema.Db.MediaLibraryProviderBindings
            .Include(item => item.MediaProviderLink).SingleAsync();
        Assert.Equal(account.Id, binding.ConnectedServiceAccountId);
        Assert.Equal(providerMediaId, binding.MediaProviderLink!.ExternalId);
        Assert.Equal(entry.MediaTitleId, binding.MediaProviderLink.MediaTitleId);
        Assert.False(await schema.Db.MediaObservations.AsNoTracking()
            .AnyAsync(item => item.Id == observation.Id));
        Assert.Equal(EntityState.Detached, controllerDb.Entry(controllerObservation).State);
        Assert.DoesNotContain(controllerDb.ChangeTracker.Entries(), entry => entry.State == EntityState.Added
            && entry.Entity is MediaTitle or MediaProviderLink or MediaLibraryEntry or MediaLibraryProviderBinding);
    }

    [PostgresFact]
    public async Task GetTitleDetails_RetriesDiscoveryIdentityRaceAndPersistsHydrationAtomically()
    {
        await using var schema = await SchemaContext.CreateAsync();
        const int userId = 47813;
        const string providerId = "anilist";
        const string providerMediaId = "details-discovery-race";
        const string titleName = "Details discovered during identity race";
        schema.Db.Users.Add(TestUserFactory.Create(userId, "details-discovery-race@example.test"));
        await schema.Db.SaveChangesAsync();
        var details = new MediaProviderTitleDetails
        {
            ProviderId = providerId,
            ProviderMediaId = providerMediaId,
            ExternalUrl = "https://anilist.co/anime/12345",
            Title = titleName,
            MediaKind = MediaKinds.Anime,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            EpisodeCount = 2,
            AvailabilityLinks =
            [
                new MediaProviderAvailabilityLink
                {
                    ServiceId = "crunchyroll",
                    DisplayName = "Crunchyroll",
                    Url = "https://www.crunchyroll.com/series/example",
                    AvailabilityKind = "subscription"
                }
            ],
            EpisodeCatalog =
            [
                new MediaProviderWatchedEpisode { SeasonNumber = 1, EpisodeNumber = 1, ProviderEpisodeId = "episode-1", Title = "First" },
                new MediaProviderWatchedEpisode { SeasonNumber = 1, EpisodeNumber = 2, ProviderEpisodeId = "episode-2", Title = "Second" }
            ],
            SpecialEpisodeCatalog =
            [
                new MediaProviderWatchedEpisode { SeasonNumber = 0, EpisodeNumber = 1, ProviderEpisodeId = "special-1", Title = "Special" }
            ]
        };
        var account = new ConnectedServiceAccount
        {
            UserId = userId,
            Service = providerId,
            ExternalAccountId = "details-discovery-race-account",
            ConnectionState = "connected",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var provider = new ObservationRaceMediaProvider(account, details);
        var candidate = new MediaCatalogSearchCandidate(providerId, new CachedMediaProviderSearchResult(
            providerMediaId, titleName, MediaKinds.Anime, null, null)
        {
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            EpisodeCount = 2,
            CrossReferences = []
        });
        var interleaved = new ProviderIdentityClaimBeforeSave(() => MakeService(schema.Options)
            .CanonicalizeAsync([candidate], default));
        var controllerOptions = new DbContextOptionsBuilder<ApplicationDbContext>(schema.Options)
            .AddInterceptors(interleaved).Options;
        await using var controllerDb = new ApplicationDbContext(controllerOptions);
        var registry = new MediaProviderRegistry([provider]);
        var controller = CreateMediaProvidersController(controllerDb, registry, userId,
            "details-discovery-race@example.test");

        var result = await controller.GetTitleDetails(providerId, providerMediaId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<MediaProviderTitleDetailsDto>(ok.Value);
        var canonicalLink = await schema.Db.MediaProviderLinks.AsNoTracking().SingleAsync();
        Assert.Equal(canonicalLink.MediaTitleId, response.MediaTitleId);
        Assert.Equal(titleName, (await schema.Db.MediaTitles.AsNoTracking().SingleAsync()).CanonicalTitle);
        Assert.Equal(1, await schema.Db.MediaTitles.CountAsync());
        Assert.Equal(1, await schema.Db.MediaProviderLinks.CountAsync());
        Assert.Equal("https://anilist.co/anime/12345", canonicalLink.ExternalUrl);
        Assert.Equal("fresh", response.AvailabilityStatus);
        Assert.NotNull(response.AvailabilityLastVerifiedAt);
        Assert.Equal("https://www.crunchyroll.com/series/example",
            Assert.Single(MediaProviderAvailabilitySnapshotCodec.Deserialize(canonicalLink.AvailabilitySnapshot)).Url);
        Assert.NotNull(canonicalLink.SpecialEpisodeCatalogSnapshot);
        Assert.Contains("Special", canonicalLink.SpecialEpisodeCatalogSnapshot);
        Assert.NotNull(canonicalLink.EpisodeCatalogLastVerifiedAt);
        Assert.Equal(2, await schema.Db.MediaEpisodes.CountAsync());
        Assert.Equal(2, await schema.Db.MediaEpisodeProviderContents.CountAsync());
        Assert.Equal(["First", "Second"], await schema.Db.MediaEpisodes.AsNoTracking()
            .OrderBy(episode => episode.EpisodeNumber).Select(episode => episode.Title).ToListAsync());
        Assert.Equal(0, await schema.Db.MediaLibraryEntries.CountAsync());
        Assert.Equal(1, interleaved.ClaimCount);
        Assert.Equal(1, interleaved.UniqueViolationCount);
        Assert.Equal(1, provider.DetailsCalls);
        Assert.DoesNotContain(controllerDb.ChangeTracker.Entries(), entry => entry.State == EntityState.Added
            && entry.Entity is MediaTitle or MediaProviderLink or MediaEpisode or MediaEpisodeProviderContent);
    }

    [PostgresFact]
    public async Task CanonicalizeAsync_BatchesWritesReusesStoredMetadataAndSupportsSynonymSearch()
    {
        var counter = new CommandCounter();
        await using var schema = await SchemaContext.CreateAsync(counter);
        var candidates = Enumerable.Range(1, 20).Select(index =>
            new MediaCatalogSearchCandidate("anilist", new CachedMediaProviderSearchResult(
                index.ToString(), $"Title {index}", "anime", null, 2026)
            {
                Synonyms = [$"Alias {index}"],
                PrimaryProgressDimension = "episode",
                ReleaseStatusDimension = "episode"
            })).ToArray();
        counter.Commands = 0;
        var timer = Stopwatch.StartNew();
        await MakeService(schema.Options).CanonicalizeAsync(candidates, default);
        output.WriteLine($"20 new summaries: {counter.Commands} database commands, {timer.Elapsed.TotalMilliseconds:F1} ms.");
        Assert.InRange(counter.Commands, 1, 4);
        counter.Commands = 0;
        timer.Restart();
        await MakeService(schema.Options).CanonicalizeAsync(candidates, default);
        output.WriteLine($"20 existing summaries: {counter.Commands} database commands, {timer.Elapsed.TotalMilliseconds:F1} ms.");
        Assert.Equal(1, counter.Commands);
        var user = TestUserFactory.Create(47810, "catalog-search@example.test");
        schema.Db.Users.Add(user);
        schema.Db.ConnectedServiceAccounts.Add(new ConnectedServiceAccount
        {
            UserId = user.Id,
            Service = "anilist",
            ExternalAccountId = "catalog-test-account",
            ConnectionState = "connected",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await schema.Db.SaveChangesAsync();
        var provider = new CantaroSearchServiceTests.FakeMediaProvider("anilist", []);
        using var cache = new MediaProviderSearchCache();
        var search = new CantaroSearchService(schema.Db, new MediaProviderRegistry([provider]), cache,
            MakeService(schema.Options), NullLogger<CantaroSearchService>.Instance);
        var matched = await search.SearchAsync(user.Id, "alias 20", 6, false, default);
        Assert.Equal("Title 20", Assert.Single(matched.Groups.Media.Items).Title);
        Assert.Equal(0, provider.SearchCallCount);
    }

    [PostgresFact]
    public async Task CanonicalizeAsync_ConcurrentSharedReferenceClaimsRetryIntoOneCanonicalTitle()
    {
        await using var schema = await SchemaContext.CreateAsync();
        var first = Candidate("anilist", "ani-1", "First", "mal-10");
        var second = Candidate("simkl", "sim-20", "First", "mal-10");
        var options = schema.Options;

        var concurrent = await Task.WhenAll(
            MakeService(options).CanonicalizeAsync([first], default),
            MakeService(options).CanonicalizeAsync([second], default));

        Assert.Equal(1, await schema.Db.MediaTitles.CountAsync());
        Assert.Equal(3, await schema.Db.MediaProviderLinks.CountAsync());
        Assert.All(concurrent, result => Assert.Single(result));
        Assert.Single(concurrent.SelectMany(result => result.Values).Distinct());
        Assert.Contains(await schema.Db.MediaProviderLinks.ToListAsync(), link => link.Provider == "myanimelist" && link.ExternalId == "anime:10");

        var repeated = await MakeService(options).CanonicalizeAsync([first, second], default);
        Assert.Single(repeated.Values.Distinct());
        Assert.Equal(1, await schema.Db.MediaTitles.CountAsync());
        Assert.Equal(3, await schema.Db.MediaProviderLinks.CountAsync());
    }

    [PostgresFact]
    public async Task CanonicalizeAsync_ConcurrentAmbiguousClaimsDoNotCreateOrphanAliases()
    {
        await using var schema = await SchemaContext.CreateAsync();
        var options = schema.Options;
        var first = Candidate("anilist", "ani-1", "First", "mal-10");
        var second = Candidate("anilist", "ani-2", "Second", "mal-10");

        await Task.WhenAll(
            MakeService(options).CanonicalizeAsync([first], default),
            MakeService(options).CanonicalizeAsync([second], default));

        Assert.Equal(2, await schema.Db.MediaTitles.CountAsync());
        Assert.Equal(3, await schema.Db.MediaProviderLinks.CountAsync());
        // The first individual request may already have verified the MAL link.
        // Later contradictory evidence must not move that established identity.
        var originalAlias = await schema.Db.MediaProviderLinks.SingleAsync(link => link.Provider == "myanimelist");
        var repeated = await MakeService(options).CanonicalizeAsync([first, second], default);
        Assert.Equal(2, repeated.Values.Distinct().Count());
        Assert.Equal(2, await schema.Db.MediaTitles.CountAsync());
        Assert.Equal(3, await schema.Db.MediaProviderLinks.CountAsync());
        Assert.Equal(originalAlias.MediaTitleId, (await schema.Db.MediaProviderLinks.AsNoTracking()
            .SingleAsync(link => link.Provider == "myanimelist")).MediaTitleId);
    }

    [PostgresFact]
    public async Task LinkProviderAsync_RetriesDiscoveryIdentityRaceAndReturnsWinningTitleConflict()
    {
        await using var schema = await SchemaContext.CreateAsync();
        const int userId = 47814;
        const string providerId = "anilist";
        const string providerMediaId = "manual-link-discovery-conflict";
        var user = TestUserFactory.Create(userId, "manual-link-discovery-conflict@example.test");
        var target = new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = "User's chosen title",
            MediaKind = MediaKinds.Anime,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var libraryEntry = new MediaLibraryEntry
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            MediaTitleId = target.Id,
            Status = MediaLibraryStatuses.Current,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        schema.Db.AddRange(user, target, libraryEntry);
        await schema.Db.SaveChangesAsync();

        var interleaved = new ProviderIdentityClaimBeforeSave(() => MakeService(schema.Options)
            .CanonicalizeAsync(
            [new MediaCatalogSearchCandidate(providerId, new CachedMediaProviderSearchResult(
                providerMediaId, "Discovery's different title", MediaKinds.Anime, null, 2024)
            {
                PrimaryProgressDimension = MediaProgressDimensions.Episode,
                ReleaseStatusDimension = MediaProgressDimensions.Episode
            })], default));
        var callerOptions = new DbContextOptionsBuilder<ApplicationDbContext>(schema.Options)
            .AddInterceptors(interleaved).Options;
        await using var callerDb = new ApplicationDbContext(callerOptions);

        var result = await new MediaLibraryLinkService(
            callerDb, NullLogger<MediaLibraryLinkService>.Instance)
            .LinkProviderAsync(user.Id, target.Id, providerId, providerMediaId, false, default);

        Assert.Equal(MediaLinkResultKind.ConflictingTitle, result.Kind);
        var winner = await schema.Db.MediaProviderLinks.AsNoTracking()
            .Include(link => link.MediaTitle)
            .SingleAsync(link => link.Provider == providerId && link.ExternalId == providerMediaId);
        Assert.Equal(1, interleaved.ClaimCount);
        Assert.Equal(1, interleaved.UniqueViolationCount);
        Assert.NotEqual(target.Id, winner.MediaTitleId);
        Assert.Equal("Discovery's different title", winner.MediaTitle!.CanonicalTitle);
        Assert.Equal(winner.MediaTitleId, result.ConflictingMediaTitleId);
        Assert.Equal(winner.MediaTitle.CanonicalTitle, result.ConflictingCanonicalTitle);
        Assert.Equal("User's chosen title", (await schema.Db.MediaTitles.AsNoTracking()
            .SingleAsync(title => title.Id == target.Id)).CanonicalTitle);
        Assert.Equal(target.Id, (await schema.Db.MediaLibraryEntries.AsNoTracking()
            .SingleAsync(entry => entry.Id == libraryEntry.Id)).MediaTitleId);
        Assert.DoesNotContain(callerDb.ChangeTracker.Entries<MediaProviderLink>(),
            entry => entry.State == EntityState.Added);
        Assert.Equal(2, await schema.Db.MediaTitles.CountAsync());
    }

    [PostgresFact]
    public async Task LinkProviderAsync_RetriesDiscoveryIdentityRaceAsAlreadyLinkedToOwnedTitle()
    {
        await using var schema = await SchemaContext.CreateAsync();
        const int userId = 47815;
        const string providerId = "anilist";
        const string providerMediaId = "manual-link-discovery-same-title";
        var user = TestUserFactory.Create(userId, "manual-link-discovery-same-title@example.test");
        var target = new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = "Existing canonical title",
            MediaKind = MediaKinds.Anime,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var entry = new MediaLibraryEntry
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            MediaTitleId = target.Id,
            Status = MediaLibraryStatuses.Current,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var knownIdentity = new MediaProviderLink
        {
            Id = Guid.NewGuid(),
            MediaTitleId = target.Id,
            Provider = "myanimelist",
            ExternalId = "anime:10",
            LinkSource = MediaMappingSources.Imported,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        schema.Db.AddRange(user, target, entry, knownIdentity);
        await schema.Db.SaveChangesAsync();

        var interleaved = new ProviderIdentityClaimBeforeSave(() => MakeService(schema.Options)
            .CanonicalizeAsync(
            [Candidate(providerId, providerMediaId, "Provider title", "mal-10")], default));
        var callerOptions = new DbContextOptionsBuilder<ApplicationDbContext>(schema.Options)
            .AddInterceptors(interleaved).Options;
        await using var callerDb = new ApplicationDbContext(callerOptions);

        var result = await new MediaLibraryLinkService(
            callerDb, NullLogger<MediaLibraryLinkService>.Instance)
            .LinkProviderAsync(user.Id, target.Id, providerId, providerMediaId, false, default);

        Assert.Equal(MediaLinkResultKind.AlreadyLinked, result.Kind);
        Assert.Equal(1, interleaved.ClaimCount);
        Assert.Equal(1, interleaved.UniqueViolationCount);
        Assert.Equal(1, await schema.Db.MediaTitles.CountAsync());
        var links = await schema.Db.MediaProviderLinks.AsNoTracking().OrderBy(link => link.Provider).ToListAsync();
        Assert.Equal(2, links.Count);
        Assert.All(links, link => Assert.Equal(target.Id, link.MediaTitleId));
        Assert.Contains(links, link => link.Provider == "myanimelist" && link.ExternalId == "anime:10");
        Assert.Contains(links, link => link.Provider == providerId && link.ExternalId == providerMediaId);
        Assert.Equal(target.Id, (await schema.Db.MediaLibraryEntries.AsNoTracking()
            .SingleAsync(item => item.Id == entry.Id)).MediaTitleId);
        Assert.DoesNotContain(callerDb.ChangeTracker.Entries<MediaProviderLink>(),
            link => link.State == EntityState.Added);
    }

    private static MediaCatalogCanonicalizationService MakeService(DbContextOptions<ApplicationDbContext> options)
        => new(options, NullLogger<MediaCatalogCanonicalizationService>.Instance);

    private static MediaObservationsController CreateObservationsController(
        ApplicationDbContext db,
        IMediaProviderRegistry registry,
        int userId,
        string email)
    {
        var seasonMappingService = new MediaProviderSeasonMappingService(
            db, NullLogger<MediaProviderSeasonMappingService>.Instance);
        var matchingService = new MediaObservationMatchingService(
            db, seasonMappingService, new MediaRelationGraphRefreshQueue(),
            NullLogger<MediaObservationMatchingService>.Instance);
        var eventHub = new MediaLibraryEventHub();
        var operationProcessor = new MediaProviderOperationProcessor(
            db, new MediaProviderRegistry([]), NullLogger<MediaProviderOperationProcessor>.Instance);
        var progressService = new MediaObservationProgressService(
            db, operationProcessor, eventHub, NullLogger<MediaObservationProgressService>.Instance);
        var episodeIdentityService = new MediaEpisodeIdentityService(
            db, seasonMappingService, NullLogger<MediaEpisodeIdentityService>.Instance);
        var controller = new MediaObservationsController(
            db,
            CreateUserManager(db),
            matchingService,
            progressService,
            episodeIdentityService,
            seasonMappingService,
            registry,
            eventHub,
            NullLogger<MediaObservationsController>.Instance,
            new MediaCatalogCanonicalizationService(
                (DbContextOptions<ApplicationDbContext>)db.GetService<IDbContextOptions>(),
                NullLogger<MediaCatalogCanonicalizationService>.Instance));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                    new Claim(ClaimTypes.Name, email),
                    new Claim(ClaimTypes.Email, email)
                ], "Test"))
            }
        };
        return controller;
    }

    private static MediaProvidersController CreateMediaProvidersController(
        ApplicationDbContext db,
        IMediaProviderRegistry registry,
        int userId,
        string email)
    {
        var seasonMappingService = new MediaProviderSeasonMappingService(
            db, NullLogger<MediaProviderSeasonMappingService>.Instance);
        var episodeIdentityService = new MediaEpisodeIdentityService(
            db, seasonMappingService, NullLogger<MediaEpisodeIdentityService>.Instance);
        var eventHub = new MediaLibraryEventHub();
        var controller = new MediaProvidersController(
            db,
            registry,
            new MediaLibraryImportQueue(),
            new MediaProviderOperationProcessor(db, registry,
                NullLogger<MediaProviderOperationProcessor>.Instance),
            eventHub,
            episodeIdentityService,
            new NoOpAioStreamsAnimeEnricher(),
            new MediaCatalogCanonicalizationService(
                (DbContextOptions<ApplicationDbContext>)db.GetService<IDbContextOptions>(),
                NullLogger<MediaCatalogCanonicalizationService>.Instance),
            CreateUserManager(db),
            NullLogger<MediaProvidersController>.Instance,
            new EphemeralDataProtectionProvider(),
            new StubFrontendUrlResolver());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                    new Claim(ClaimTypes.Name, email),
                    new Claim(ClaimTypes.Email, email)
                ], "Test"))
            }
        };
        return controller;
    }

    private static UserManager<User> CreateUserManager(ApplicationDbContext db)
    {
        var store = new UserStore<User, IdentityRole<int>, ApplicationDbContext, int>(db);
        return new UserManager<User>(store, Options.Create(new IdentityOptions()), new PasswordHasher<User>(),
            [], [], new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(),
            new ServiceCollection().BuildServiceProvider(), NullLogger<UserManager<User>>.Instance);
    }

    private sealed class NoOpAioStreamsAnimeEnricher : IAioStreamsAnimeEnricher
    {
        public Task EnrichAsync(MediaProviderTitleDetails details, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class StubFrontendUrlResolver : IFrontendUrlResolver
    {
        public string GetFrontendUrl() => "https://frontend.test";
        public string GetCurrentRequestBaseUrl() => "https://api.test";
        public string GetCallbackUrl(string relativePath) => $"https://api.test/{relativePath.TrimStart('/')}";
    }

    private static MediaCatalogSearchCandidate Candidate(string provider, string id, string title, string malId)
        => new(provider, new CachedMediaProviderSearchResult(provider == "simkl" ? $"anime:{id.Replace("sim-", "")}" : id, title, "anime", null, 2026)
        {
            PrimaryProgressDimension = "episode",
            ReleaseStatusDimension = "episode",
            CrossReferences = [new("myanimelist", $"anime:{malId.Replace("mal-", "")}", null)]
        });

    private sealed class SchemaContext : IAsyncDisposable
    {
        private readonly string _adminConnectionString;
        private readonly string _schema;
        private bool _disposed;

        private SchemaContext(string adminConnectionString, string schema, ApplicationDbContext db,
            DbContextOptions<ApplicationDbContext> options)
        { _adminConnectionString = adminConnectionString; _schema = schema; Db = db; Options = options; }

        public ApplicationDbContext Db { get; }
        public DbContextOptions<ApplicationDbContext> Options { get; }

        public static async Task<SchemaContext> CreateAsync(CommandCounter? counter = null)
        {
            var raw = Environment.GetEnvironmentVariable("CANTARO_TEST_POSTGRES");
            if (string.IsNullOrWhiteSpace(raw)) throw new InvalidOperationException("CANTARO_TEST_POSTGRES is required.");
            var admin = new NpgsqlConnectionStringBuilder(raw);
            if (admin.Host is not ("localhost" or "127.0.0.1" or "::1"))
                throw new InvalidOperationException("CANTARO_TEST_POSTGRES must point to a loopback PostgreSQL server.");
            if (string.IsNullOrWhiteSpace(admin.Database)
                || !admin.Database.Contains("test", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("CANTARO_TEST_POSTGRES must use a database whose name contains 'test'.");

            var schema = $"cantaro_catalog_test_{Guid.NewGuid():N}";
            var adminConnectionString = admin.ConnectionString;
            await using (var connection = new NpgsqlConnection(adminConnectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"CREATE SCHEMA \"{schema}\"";
                await command.ExecuteNonQueryAsync();
            }

            var scoped = new NpgsqlConnectionStringBuilder(adminConnectionString) { SearchPath = schema };
            var builder = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(scoped.ConnectionString, provider => provider.EnableRetryOnFailure(maxRetryCount: 1));
            if (counter is not null) builder.AddInterceptors(counter);
            var options = builder.Options;
            var db = new ApplicationDbContext(options);
            var context = new SchemaContext(adminConnectionString, schema, db, options);
            try
            {
                // Other suites use separate schemas in the same test database.
                // EnsureCreated checks for tables database-wide and may skip this schema.
                await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
                return context;
            }
            catch
            {
                await context.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            await Db.DisposeAsync();
            await using var connection = new NpgsqlConnection(_adminConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP SCHEMA IF EXISTS \"{_schema}\" CASCADE";
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int Commands { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Commands++; return base.ReaderExecutingAsync(command, eventData, result, cancellationToken); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Commands++; return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken); }
    }

    private sealed class DetailUpdateBeforeSave(Func<CancellationToken, Task> writeDetails) : SaveChangesInterceptor
    {
        public int DetailWrites { get; private set; }
        public int SaveAttempts { get; private set; }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            SaveAttempts++;
            if (DetailWrites == 0)
            {
                DetailWrites++;
                await writeDetails(cancellationToken);
            }
            return result;
        }
    }

    private sealed class ImportIdentityClaimBeforeSave(Func<Task> claimIdentity) : SaveChangesInterceptor
    {
        private bool _claimed;
        public int ClaimCount { get; private set; }
        public int SaveAttempts { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            SaveAttempts++;
            if (!_claimed)
            {
                _claimed = true;
                ClaimCount++;
                await claimIdentity();
            }
            return result;
        }
    }

    private sealed class ProviderIdentityClaimBeforeSave(Func<Task> claimIdentity) : SaveChangesInterceptor
    {
        private bool _claimed;
        public int ClaimCount { get; private set; }
        public int UniqueViolationCount { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!_claimed && eventData.Context?.ChangeTracker.Entries<MediaProviderLink>()
                    .Any(entry => entry.State == EntityState.Added) == true)
            {
                _claimed = true;
                ClaimCount++;
                await claimIdentity();
            }
            return result;
        }

        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Exception is DbUpdateException
                {
                    InnerException: PostgresException
                    {
                        SqlState: PostgresErrorCodes.UniqueViolation,
                        ConstraintName: "IX_MediaProviderLinks_Provider_ExternalId" or "IX_MediaProviderLinks_MediaTitleId_Provider"
                    }
                })
            {
                UniqueViolationCount++;
            }
            return base.SaveChangesFailedAsync(eventData, cancellationToken);
        }
    }

    private sealed class ObservationRaceMediaProvider(
        ConnectedServiceAccount account,
        MediaProviderTitleDetails details) : IMediaProvider
    {
        public string ProviderId => account.Service;
        public int DetailsCalls { get; private set; }
        public int StatusCalls { get; private set; }
        public Task<ConnectedServiceAccount?> GetConnectedAccountAsync(int userId, CancellationToken cancellationToken = default)
            => Task.FromResult<ConnectedServiceAccount?>(account);
        public Task<MediaProviderTitleDetails?> GetTitleDetailsAsync(int userId, string providerMediaId, CancellationToken cancellationToken)
        {
            DetailsCalls++;
            return Task.FromResult<MediaProviderTitleDetails?>(details);
        }
        public Task<MediaProviderMutationResult> UpdateStatusAsync(int userId, MediaStatusUpdateRequest request, CancellationToken cancellationToken)
        {
            StatusCalls++;
            return Task.FromResult(new MediaProviderMutationResult
            {
                ProviderId = ProviderId,
                ProviderMediaId = request.ProviderMediaId,
                AppliedAt = DateTimeOffset.UtcNow,
                LastRemoteUpdateAt = DateTimeOffset.UtcNow
            });
        }
        public bool SupportsMediaKind(string mediaKind) => true;
        public string GetAuthorizationUrl(string redirectUri, string state, string codeChallenge) => throw new NotSupportedException();
        public Task<ConnectedServiceAccount> ExchangeCodeAndSaveAsync(int userId, string authorizationCode, string redirectUri, string codeVerifier, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DisconnectAsync(int userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MediaProviderLibraryImportResult> ImportLibraryAsync(int userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<MediaProviderSearchResult>> SearchAsync(int userId, MediaCatalogSearchRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MediaProviderMutationResult> UpdateProgressAsync(int userId, MediaProgressUpdateRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MediaProviderMutationResult> UpdateScoreAsync(int userId, MediaScoreUpdateRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MediaReleaseMetadata?> GetReleaseMetadataAsync(int userId, string providerMediaId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CANTARO_TEST_POSTGRES")))
                Skip = "Set CANTARO_TEST_POSTGRES to a loopback database whose name contains 'test'.";
        }
    }
}

// Existing PostgreSQL suites use database-wide EnsureCreated checks while
// creating temporary schemas. Keep this schema-based suite from overlapping them.
[CollectionDefinition("Media catalog PostgreSQL", DisableParallelization = true)]
public sealed class MediaCatalogPostgresCollection;
