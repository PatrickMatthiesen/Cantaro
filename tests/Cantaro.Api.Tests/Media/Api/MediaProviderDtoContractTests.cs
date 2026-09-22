using System.Security.Claims;
using System.Text.Json;
using System.Threading.Channels;
using Cantaro.Api.Controllers;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public class MediaProviderDtoContractTests
{
    [Fact]
    public async Task Search_MapsServiceResultsToDtoContract()
    {
        var provider = new StubMediaProvider
        {
            SearchResults =
            [
                new MediaProviderSearchResult
                {
                    ProviderId = "anilist",
                    ProviderMediaId = "140960",
                    Title = "Spy x Family",
                    NativeTitle = "SPY x FAMILY",
                    MediaKind = MediaKinds.Anime,
                    Synopsis = "A spy starts a family.",
                    PosterUrl = "https://example.test/poster.jpg",
                    BackgroundUrl = "https://example.test/banner.jpg",
                    StartYear = 2022,
                    EpisodeCount = 25,
                    PrimaryProgressDimension = MediaProgressDimensions.Episode,
                    ReleaseStatusDimension = MediaProgressDimensions.Episode
                }
            ]
        };

        await using var fixture = await MediaControllerFixture.CreateAsync(provider);

        var result = await fixture.Controller.Search("anilist", "spy", null, 25, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsAssignableFrom<IReadOnlyList<MediaProviderSearchResultDto>>(ok.Value);
        var item = Assert.Single(payload);

        Assert.Equal("140960", item.ProviderMediaId);
        Assert.Equal("Spy x Family", item.Title);
        Assert.Equal(MediaProgressDimensions.Episode, item.PrimaryProgressDimension);
        Assert.Null(typeof(MediaProviderSearchResultDto).GetProperty("RawMetadata"));
    }

    [Fact]
    public async Task GetTitleDetails_MapsServiceDetailsToDtoContract()
    {
        var provider = new StubMediaProvider
        {
            TitleDetails = new MediaProviderTitleDetails
            {
                ProviderId = "anilist",
                ProviderMediaId = "140960",
                Title = "Spy x Family",
                NativeTitle = "SPY x FAMILY",
                MediaKind = MediaKinds.Anime,
                Synopsis = "A spy starts a family.",
                PosterUrl = "https://example.test/poster.jpg",
                BackgroundUrl = "https://example.test/banner.jpg",
                StartYear = 2022,
                EpisodeCount = 25,
                ReleasedCount = 25,
                TotalKnownCount = 25,
                PrimaryProgressDimension = MediaProgressDimensions.Episode,
                ReleaseStatusDimension = MediaProgressDimensions.Episode,
                StremioTarget = new MediaProviderStremioTarget
                {
                    Type = "series",
                    Id = "kitsu:1"
                },
                StremioTargets =
                [
                    new MediaProviderStremioTarget
                    {
                        Type = "series",
                        Id = "kitsu:1",
                        EpisodeMapping = new MediaProviderStremioEpisodeMapping
                        {
                            SeasonNumber = null,
                            EpisodeOffset = 0
                        }
                    },
                    new MediaProviderStremioTarget
                    {
                        Type = "series",
                        Id = "tt0213338"
                    }
                ],
                AvailabilityLinks =
                [
                    new MediaProviderAvailabilityLink
                    {
                        ServiceId = "crunchyroll",
                        DisplayName = "Crunchyroll",
                        Url = "https://www.crunchyroll.com/series/GEXH3W8XG",
                        AvailabilityKind = "streaming",
                        Notes = "Legal streaming",
                        IconUrl = "https://example.test/crunchyroll.png"
                    }
                ],
                Characters =
                [
                    new MediaProviderCharacterCredit
                    {
                        CharacterId = "170732",
                        Name = "Anya Forger",
                        ImageUrl = "https://example.test/anya.jpg",
                        Role = "main",
                        ProviderUrl = "https://anilist.co/character/170732/Anya-Forger",
                        Order = 0
                    }
                ]
            }
        };

        await using var fixture = await MediaControllerFixture.CreateAsync(provider);

        var result = await fixture.Controller.GetTitleDetails("anilist", "140960", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<MediaProviderTitleDetailsDto>(ok.Value);

        Assert.Equal("Spy x Family", payload.Title);
        Assert.NotEqual(Guid.Empty, payload.MediaTitleId);
        Assert.Equal("SPY x FAMILY", payload.NativeTitle);
        var availability = Assert.Single(payload.AvailabilityLinks);
        Assert.Equal("crunchyroll", availability.ServiceId);
        Assert.Equal("Crunchyroll", availability.DisplayName);
        Assert.Equal("streaming", availability.AvailabilityKind);
        Assert.Equal("https://www.crunchyroll.com/series/GEXH3W8XG", availability.Url);
        Assert.Equal("fresh", payload.AvailabilityStatus);
        Assert.NotNull(payload.AvailabilityLastVerifiedAt);
        Assert.Equal("series", payload.StremioTarget?.Type);
        Assert.Equal("kitsu:1", payload.StremioTarget?.Id);
        Assert.Equal(
            [("series", "kitsu:1"), ("series", "tt0213338")],
            payload.StremioTargets.Select(target => (target.Type, target.Id)));
        Assert.Null(payload.StremioTarget?.EpisodeMapping?.SeasonNumber);
        Assert.Equal(0, payload.StremioTarget?.EpisodeMapping?.EpisodeOffset);
        Assert.Null(payload.StremioTargets[1].EpisodeMapping);
        var stremioTargetsJson = JsonSerializer.Serialize(
            payload.StremioTargets,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(
            "[{\"type\":\"series\",\"id\":\"kitsu:1\",\"episodeMapping\":{\"seasonNumber\":null,\"episodeOffset\":0}},{\"type\":\"series\",\"id\":\"tt0213338\"}]",
            stremioTargetsJson);
        var character = Assert.Single(payload.Characters);
        Assert.Equal("170732", character.CharacterId);
        Assert.Equal("Anya Forger", character.Name);
        Assert.Equal("main", character.Role);
        Assert.Equal("https://example.test/anya.jpg", character.ImageUrl);
        Assert.Equal("https://anilist.co/character/170732/Anya-Forger", character.ProviderUrl);
        Assert.Equal(0, character.Order);
        Assert.Null(typeof(MediaProviderTitleDetailsDto).GetProperty("RawMetadata"));

        var canonicalTitle = await fixture.DbContext.MediaTitles.SingleAsync();
        Assert.Equal(canonicalTitle.Id, payload.MediaTitleId);
        Assert.Equal(25, canonicalTitle.TotalKnownCount);
        var providerLink = await fixture.DbContext.MediaProviderLinks.SingleAsync();
        Assert.Equal(canonicalTitle.Id, providerLink.MediaTitleId);
        Assert.Equal("140960", providerLink.ExternalId);
        Assert.Equal(MediaMappingSources.Imported, providerLink.LinkSource);
        Assert.NotNull(providerLink.AvailabilitySnapshot);
        Assert.Equal(
            "[{\"serviceId\":\"crunchyroll\",\"displayName\":\"Crunchyroll\",\"url\":\"https://www.crunchyroll.com/series/GEXH3W8XG\",\"availabilityKind\":\"streaming\",\"notes\":\"Legal streaming\",\"iconUrl\":\"https://example.test/crunchyroll.png\"}]",
            providerLink.AvailabilitySnapshot);
        var persistedAvailability = Assert.Single(
            MediaProviderAvailabilitySnapshotCodec.Deserialize(providerLink.AvailabilitySnapshot));
        Assert.Equal("crunchyroll", persistedAvailability.ServiceId);
        Assert.Equal("https://www.crunchyroll.com/series/GEXH3W8XG", persistedAvailability.Url);
        Assert.Equal(payload.AvailabilityLastVerifiedAt, providerLink.AvailabilityLastVerifiedAt);
        Assert.Equal(payload.AvailabilityLastVerifiedAt, providerLink.LastVerifiedAt);
    }

    [Fact]
    public async Task GetTitleDetails_EnrichesStremioTargetsBeforeMappingTheResponse()
    {
        var provider = new StubMediaProvider
        {
            TitleDetails = CreateTitleDetails()
        };
        var enricher = new StubAioStreamsAnimeEnricher
        {
            OnEnrich = details => details.StremioTargets =
            [
                new MediaProviderStremioTarget
                {
                    Type = "series",
                    Id = "tt0213338",
                    EpisodeMapping = new MediaProviderStremioEpisodeMapping
                    {
                        SeasonNumber = 1,
                        EpisodeOffset = 3
                    }
                }
            ]
        };
        await using var fixture = await MediaControllerFixture.CreateAsync(provider, enricher);

        var result = await fixture.Controller.GetTitleDetails("anilist", "140960", CancellationToken.None);

        var payload = Assert.IsType<MediaProviderTitleDetailsDto>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(1, enricher.CallCount);
        var target = Assert.Single(payload.StremioTargets);
        Assert.Equal("tt0213338", target.Id);
        Assert.Equal(1, target.EpisodeMapping?.SeasonNumber);
        Assert.Equal(3, target.EpisodeMapping?.EpisodeOffset);
        Assert.Equal("tt0213338", payload.StremioTarget?.Id);
    }

    [Fact]
    public async Task GetTitleDetails_MalExistingLink_PreservesAniListCanonicalMetadata()
    {
        var provider = new StubMediaProvider
        {
            ProviderId = MediaObservationSiteIdentifiers.MyAnimeList,
            TitleDetails = new MediaProviderTitleDetails
            {
                ProviderId = MediaObservationSiteIdentifiers.MyAnimeList,
                ProviderMediaId = "anime:140960",
                Title = "MAL title",
                MediaKind = MediaKinds.Anime,
                PosterUrl = "https://example.test/mal.jpg",
                PrimaryProgressDimension = MediaProgressDimensions.Episode,
                ReleaseStatusDimension = MediaProgressDimensions.Episode
            }
        };
        await using var fixture = await MediaControllerFixture.CreateAsync(provider);
        var now = DateTimeOffset.UtcNow;
        var title = new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = "AniList title",
            PosterUrl = "https://example.test/anilist.jpg",
            MediaKind = MediaKinds.Anime,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            CreatedAt = now,
            UpdatedAt = now
        };
        fixture.DbContext.Add(title);
        fixture.DbContext.MediaProviderLinks.AddRange(
            new MediaProviderLink
            {
                Id = Guid.NewGuid(), MediaTitleId = title.Id, Provider = MediaObservationSiteIdentifiers.AniList,
                ExternalId = "140960", LinkSource = MediaMappingSources.Imported, CreatedAt = now, UpdatedAt = now
            },
            new MediaProviderLink
            {
                Id = Guid.NewGuid(), MediaTitleId = title.Id, Provider = MediaObservationSiteIdentifiers.MyAnimeList,
                ExternalId = "anime:140960", LinkSource = MediaMappingSources.Imported, CreatedAt = now, UpdatedAt = now
            });
        await fixture.DbContext.SaveChangesAsync();

        await fixture.Controller.GetTitleDetails(
            MediaObservationSiteIdentifiers.MyAnimeList,
            "anime:140960",
            CancellationToken.None);

        var persisted = await fixture.DbContext.MediaTitles.SingleAsync();
        Assert.Equal("AniList title", persisted.CanonicalTitle);
        Assert.Equal("https://example.test/anilist.jpg", persisted.PosterUrl);
    }

    [Fact]
    public async Task GetTitleDetails_MalCrossReferenceAnchor_PreservesAniListCanonicalMetadata()
    {
        var provider = new StubMediaProvider
        {
            ProviderId = MediaObservationSiteIdentifiers.MyAnimeList,
            TitleDetails = new MediaProviderTitleDetails
            {
                ProviderId = MediaObservationSiteIdentifiers.MyAnimeList,
                ProviderMediaId = "anime:140960",
                Title = "MAL title",
                MediaKind = MediaKinds.Anime,
                PosterUrl = "https://example.test/mal.jpg",
                PrimaryProgressDimension = MediaProgressDimensions.Episode,
                ReleaseStatusDimension = MediaProgressDimensions.Episode,
                CrossReferences =
                [
                    new MediaProviderCrossReference
                    {
                        ProviderId = MediaObservationSiteIdentifiers.AniList,
                        ProviderMediaId = "140960"
                    }
                ]
            }
        };
        await using var fixture = await MediaControllerFixture.CreateAsync(provider);
        var now = DateTimeOffset.UtcNow;
        var title = new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = "AniList title",
            PosterUrl = "https://example.test/anilist.jpg",
            MediaKind = MediaKinds.Anime,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            CreatedAt = now,
            UpdatedAt = now
        };
        fixture.DbContext.Add(title);
        fixture.DbContext.MediaProviderLinks.Add(new MediaProviderLink
        {
            Id = Guid.NewGuid(), MediaTitleId = title.Id, Provider = MediaObservationSiteIdentifiers.AniList,
            ExternalId = "140960", LinkSource = MediaMappingSources.Imported, CreatedAt = now, UpdatedAt = now
        });
        await fixture.DbContext.SaveChangesAsync();

        await fixture.Controller.GetTitleDetails(
            MediaObservationSiteIdentifiers.MyAnimeList,
            "anime:140960",
            CancellationToken.None);

        var persisted = await fixture.DbContext.MediaTitles.SingleAsync();
        Assert.Equal("AniList title", persisted.CanonicalTitle);
        Assert.Equal("https://example.test/anilist.jpg", persisted.PosterUrl);
        Assert.Contains(
            await fixture.DbContext.MediaProviderLinks.ToListAsync(),
            link => link.Provider == MediaObservationSiteIdentifiers.MyAnimeList);
    }

    [Fact]
    public async Task GetTitleDetails_WhenProviderFails_ServesCachedAvailabilityAsStale()
    {
        var provider = new StubMediaProvider
        {
            TitleDetails = CreateTitleDetails(
                new MediaProviderAvailabilityLink
                {
                    ServiceId = "crunchyroll",
                    DisplayName = "Crunchyroll",
                    Url = "https://www.crunchyroll.com/series/GEXH3W8XG",
                    AvailabilityKind = "streaming"
                })
        };
        await using var fixture = await MediaControllerFixture.CreateAsync(provider);

        var firstResult = await fixture.Controller.GetTitleDetails("anilist", "140960", CancellationToken.None);
        var firstPayload = Assert.IsType<MediaProviderTitleDetailsDto>(
            Assert.IsType<OkObjectResult>(firstResult.Result).Value);
        var verifiedAt = firstPayload.AvailabilityLastVerifiedAt;
        Assert.NotNull(verifiedAt);

        provider.ThrowOnTitleDetails = true;
        var secondResult = await fixture.Controller.GetTitleDetails("anilist", "140960", CancellationToken.None);

        var stalePayload = Assert.IsType<MediaProviderTitleDetailsDto>(
            Assert.IsType<OkObjectResult>(secondResult.Result).Value);
        Assert.Equal("stale", stalePayload.AvailabilityStatus);
        Assert.Equal(verifiedAt, stalePayload.AvailabilityLastVerifiedAt);
        var cachedLink = Assert.Single(stalePayload.AvailabilityLinks);
        Assert.Equal("crunchyroll", cachedLink.ServiceId);
        Assert.Equal("https://www.crunchyroll.com/series/GEXH3W8XG", cachedLink.Url);

        var persistedLink = await fixture.DbContext.MediaProviderLinks.SingleAsync();
        Assert.Equal(verifiedAt, persistedLink.AvailabilityLastVerifiedAt);
    }

    [Fact]
    public async Task GetTitleDetails_WhenOptionalAvailabilityRefreshFails_PreservesVerificationTimestamp()
    {
        var cachedAvailability = new MediaProviderAvailabilityLink
        {
            ServiceId = "netflix",
            DisplayName = "Netflix",
            Url = "https://www.netflix.com/title/80001305",
            AvailabilityKind = "streaming"
        };
        var provider = new StubMediaProvider
        {
            ProviderId = MediaObservationSiteIdentifiers.Simkl,
            TitleDetails = CreateTitleDetails(cachedAvailability)
        };
        provider.TitleDetails.ProviderId = MediaObservationSiteIdentifiers.Simkl;
        provider.TitleDetails.ProviderMediaId = "anime:123";
        await using var fixture = await MediaControllerFixture.CreateAsync(provider);

        var firstResult = await fixture.Controller.GetTitleDetails("simkl", "anime:123", CancellationToken.None);
        var firstPayload = Assert.IsType<MediaProviderTitleDetailsDto>(
            Assert.IsType<OkObjectResult>(firstResult.Result).Value);
        var verifiedAt = Assert.IsType<DateTimeOffset>(firstPayload.AvailabilityLastVerifiedAt);

        provider.TitleDetails = CreateTitleDetails(cachedAvailability);
        provider.TitleDetails.ProviderId = MediaObservationSiteIdentifiers.Simkl;
        provider.TitleDetails.ProviderMediaId = "anime:123";
        provider.TitleDetails.AvailabilityRefreshSucceeded = false;
        var secondResult = await fixture.Controller.GetTitleDetails("simkl", "anime:123", CancellationToken.None);

        var stalePayload = Assert.IsType<MediaProviderTitleDetailsDto>(
            Assert.IsType<OkObjectResult>(secondResult.Result).Value);
        Assert.Equal("stale", stalePayload.AvailabilityStatus);
        Assert.Equal(verifiedAt, stalePayload.AvailabilityLastVerifiedAt);
        Assert.Equal("netflix", Assert.Single(stalePayload.AvailabilityLinks).ServiceId);
        var persistedLink = await fixture.DbContext.MediaProviderLinks.SingleAsync();
        Assert.Equal(verifiedAt, persistedLink.AvailabilityLastVerifiedAt);
    }

    [Fact]
    public async Task GetTitleDetails_ExplicitEmptyAvailabilitySnapshotFallsBackButNeverCheckedDoesNot()
    {
        var provider = new StubMediaProvider
        {
            TitleDetails = CreateTitleDetails()
        };
        await using var fixture = await MediaControllerFixture.CreateAsync(provider);

        var firstResult = await fixture.Controller.GetTitleDetails("anilist", "140960", CancellationToken.None);
        var firstPayload = Assert.IsType<MediaProviderTitleDetailsDto>(
            Assert.IsType<OkObjectResult>(firstResult.Result).Value);
        var verifiedAt = firstPayload.AvailabilityLastVerifiedAt;
        Assert.NotNull(verifiedAt);

        var verifiedEmptyLink = await fixture.DbContext.MediaProviderLinks.SingleAsync();
        Assert.Equal("[]", verifiedEmptyLink.AvailabilitySnapshot);
        Assert.Equal(verifiedAt, verifiedEmptyLink.AvailabilityLastVerifiedAt);

        provider.ThrowOnTitleDetails = true;
        var staleResult = await fixture.Controller.GetTitleDetails("anilist", "140960", CancellationToken.None);
        var stalePayload = Assert.IsType<MediaProviderTitleDetailsDto>(
            Assert.IsType<OkObjectResult>(staleResult.Result).Value);
        Assert.Equal("stale", stalePayload.AvailabilityStatus);
        Assert.Empty(stalePayload.AvailabilityLinks);
        Assert.Equal(verifiedAt, stalePayload.AvailabilityLastVerifiedAt);

        var neverCheckedProvider = new StubMediaProvider { ThrowOnTitleDetails = true };
        await using var neverCheckedFixture = await MediaControllerFixture.CreateAsync(neverCheckedProvider);
        var now = DateTimeOffset.UtcNow;
        var title = new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = "Never Checked",
            MediaKind = MediaKinds.Anime,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            CreatedAt = now,
            UpdatedAt = now
        };
        neverCheckedFixture.DbContext.MediaTitles.Add(title);
        neverCheckedFixture.DbContext.MediaProviderLinks.Add(new MediaProviderLink
        {
            Id = Guid.NewGuid(),
            MediaTitleId = title.Id,
            Provider = "anilist",
            ExternalId = "never-checked",
            LinkSource = MediaMappingSources.Imported,
            CreatedAt = now,
            UpdatedAt = now
        });
        await neverCheckedFixture.DbContext.SaveChangesAsync();

        var neverCheckedLink = await neverCheckedFixture.DbContext.MediaProviderLinks.SingleAsync();
        Assert.Null(neverCheckedLink.AvailabilitySnapshot);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            neverCheckedFixture.Controller.GetTitleDetails("anilist", "never-checked", CancellationToken.None));
    }

    [Fact]
    public async Task LibraryTitleDetails_ExposeCachedProviderAvailability()
    {
        var provider = new StubMediaProvider
        {
            TitleDetails = CreateTitleDetails(
                new MediaProviderAvailabilityLink
                {
                    ServiceId = "crunchyroll",
                    DisplayName = "Crunchyroll",
                    Url = "https://www.crunchyroll.com/series/GEXH3W8XG",
                    AvailabilityKind = "streaming"
                })
        };
        await using var fixture = await MediaControllerFixture.CreateAsync(provider);

        var result = await fixture.Controller.GetTitleDetails("anilist", "140960", CancellationToken.None);
        var titleDetails = Assert.IsType<MediaProviderTitleDetailsDto>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        var libraryTitle = await new MediaLibraryQueryService(fixture.DbContext)
            .GetMediaTitleAsync(titleDetails.MediaTitleId, CancellationToken.None);

        var providerLink = Assert.Single(Assert.IsType<MediaTitleDetailDto>(libraryTitle).ProviderLinks);
        var availability = Assert.Single(providerLink.AvailabilityLinks);
        Assert.Equal("crunchyroll", availability.ServiceId);
        Assert.Equal("https://www.crunchyroll.com/series/GEXH3W8XG", availability.Url);
        Assert.Equal(titleDetails.AvailabilityLastVerifiedAt, providerLink.AvailabilityLastVerifiedAt);
    }

    [Fact]
    public async Task Search_ResolvesLibraryStateThroughCanonicalProviderLink()
    {
        var provider = new StubMediaProvider
        {
            SearchResults =
            [
                new MediaProviderSearchResult
                {
                    ProviderId = "anilist",
                    ProviderMediaId = "140960",
                    Title = "Spy x Family",
                    MediaKind = MediaKinds.Anime,
                    PrimaryProgressDimension = MediaProgressDimensions.Episode,
                    ReleaseStatusDimension = MediaProgressDimensions.Episode
                }
            ]
        };
        await using var fixture = await MediaControllerFixture.CreateAsync(provider);
        var now = DateTimeOffset.UtcNow;
        var title = new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = "Spy x Family",
            MediaKind = MediaKinds.Anime,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            CreatedAt = now,
            UpdatedAt = now
        };
        fixture.DbContext.MediaTitles.Add(title);
        fixture.DbContext.MediaProviderLinks.Add(new MediaProviderLink
        {
            Id = Guid.NewGuid(),
            MediaTitleId = title.Id,
            Provider = "anilist",
            ExternalId = "140960",
            LinkSource = MediaMappingSources.Imported,
            CreatedAt = now,
            UpdatedAt = now
        });
        fixture.DbContext.MediaLibraryEntries.Add(new MediaLibraryEntry
        {
            Id = Guid.NewGuid(),
            UserId = fixture.UserId,
            MediaTitleId = title.Id,
            Status = MediaLibraryStatuses.Planned,
            CreatedAt = now,
            UpdatedAt = now
        });
        await fixture.DbContext.SaveChangesAsync();

        var result = await fixture.Controller.Search("anilist", "spy", null, 25, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsAssignableFrom<IReadOnlyList<MediaProviderSearchResultDto>>(ok.Value);
        var state = Assert.Single(payload).LibraryState;
        Assert.NotNull(state);
        Assert.True(state.IsInLibrary);
        Assert.Equal(title.Id, state.MediaTitleId);
    }

    [Fact]
    public async Task GetReleaseMetadata_MapsServiceMetadataToDtoContract()
    {
        var provider = new StubMediaProvider
        {
            ReleaseMetadata = new MediaReleaseMetadata
            {
                ProviderId = "anilist",
                ProviderMediaId = "140960",
                ReleaseStatusDimension = MediaProgressDimensions.Episode,
                ReleasedCount = 25,
                TotalKnownCount = 25,
                NextReleaseLabel = "Finished"
            }
        };

        await using var fixture = await MediaControllerFixture.CreateAsync(provider);

        var result = await fixture.Controller.GetReleaseMetadata("anilist", "140960", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<MediaReleaseMetadataDto>(ok.Value);

        Assert.Equal(25, payload.ReleasedCount);
        Assert.Equal("Finished", payload.NextReleaseLabel);
        Assert.Null(typeof(MediaReleaseMetadataDto).GetProperty("RawMetadata"));
    }

    [Fact]
    public async Task UpdateProgress_ReturnsAcceptedAndPersistsPendingOperationWithoutProviderCall()
    {
        var provider = new StubMediaProvider
        {
            ProgressMutationResult = new MediaProviderMutationResult
            {
                ProviderId = "anilist",
                ProviderMediaId = "140960",
                AppliedAt = DateTimeOffset.UtcNow,
                LastRemoteUpdateAt = DateTimeOffset.UtcNow,
            }
        };
        await using var fixture = await MediaControllerFixture.CreateAsync(provider);
        var entry = await SeedMediaEntryAsync(fixture);

        var result = await fixture.Controller.UpdateProgress(
            entry.MediaTitleId,
            new MediaProgressUpdateDto { ProgressEpisodes = 17 },
            CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);

        var persistedEntry = await fixture.DbContext.MediaLibraryEntries.SingleAsync(item => item.Id == entry.Id);
        Assert.Equal(17, persistedEntry.ProgressEpisodes);
        Assert.Equal(MediaProviderOperationStatuses.Pending,
            (await fixture.DbContext.MediaProviderOperations.SingleAsync()).Status);
        Assert.Equal(0, provider.ProgressUpdateCallCount);
    }

    [Fact]
    public async Task UpdateProgress_PersistsCantaroStateWithoutProviderBinding()
    {
        await using var fixture = await MediaControllerFixture.CreateAsync(new StubMediaProvider());
        var now = DateTimeOffset.UtcNow;
        var title = new MediaTitle
        {
            Id = Guid.NewGuid(), CanonicalTitle = "Local-only title", MediaKind = MediaKinds.Anime,
            SupportsEpisodeProgress = true,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            CreatedAt = now, UpdatedAt = now
        };
        var entry = new MediaLibraryEntry
        {
            Id = Guid.NewGuid(), UserId = fixture.UserId, MediaTitleId = title.Id,
            Status = MediaLibraryStatuses.Current, ProgressEpisodes = 2,
            CreatedAt = now, UpdatedAt = now
        };
        fixture.DbContext.AddRange(title, entry);
        await fixture.DbContext.SaveChangesAsync();

        var result = await fixture.Controller.UpdateProgress(
            title.Id,
            new MediaProgressUpdateDto { ProgressEpisodes = 3 },
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(3, (await fixture.DbContext.MediaLibraryEntries.SingleAsync()).ProgressEpisodes);
        Assert.Empty(fixture.DbContext.MediaProviderOperations);
    }

    [Fact]
    public async Task UpdateProgress_PersistsLocalProgressWhenOperationRemainsQueued()
    {
        var provider = new StubMediaProvider
        {
            ThrowOnProgressUpdate = true
        };
        await using var fixture = await MediaControllerFixture.CreateAsync(provider);
        var entry = await SeedMediaEntryAsync(fixture);

        var result = await fixture.Controller.UpdateProgress(
            entry.MediaTitleId,
            new MediaProgressUpdateDto { ProgressEpisodes = 17 },
            CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);

        var persistedEntry = await fixture.DbContext.MediaLibraryEntries.SingleAsync(item => item.Id == entry.Id);
        Assert.Equal(17, persistedEntry.ProgressEpisodes);
        Assert.Equal(MediaMutationSources.UserProgressUpdate, persistedEntry.LastMutationSource);
        Assert.NotNull(persistedEntry.LastLocalEditAt);

        var queuedOperation = await fixture.DbContext.MediaProviderOperations.SingleAsync();
        Assert.Equal(MediaProviderOperationStatuses.Pending, queuedOperation.Status);
        Assert.Null(queuedOperation.LastError);
        Assert.Equal(0, provider.ProgressUpdateCallCount);
    }

    [Fact]
    public async Task UpdateScore_PersistsCanonicalScoreLocally()
    {
        await using var fixture = await MediaControllerFixture.CreateAsync(new StubMediaProvider());
        var entry = await SeedMediaEntryAsync(fixture);

        var result = await fixture.Controller.UpdateScore(
            entry.MediaTitleId,
            new MediaScoreUpdateDto { Score = 87.5m },
            CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        var persistedEntry = await fixture.DbContext.MediaLibraryEntries.SingleAsync();
        Assert.Equal(87.5m, persistedEntry.Score);
        Assert.Equal(MediaMutationSources.UserScoreUpdate, persistedEntry.LastMutationSource);
        Assert.NotNull(persistedEntry.LastLocalEditAt);
        Assert.True(fixture.LibraryEvents.TryRead(out var libraryEvent));
        Assert.NotNull(libraryEvent);
    }

    [Fact]
    public async Task UpdateScore_PersistsLocallyWhenProviderOperationIsQueued()
    {
        var provider = new StubMediaProvider { ThrowOnStatusUpdate = true };
        await using var fixture = await MediaControllerFixture.CreateAsync(provider);
        var entry = await SeedMediaEntryAsync(fixture);

        var result = await fixture.Controller.UpdateScore(
            entry.MediaTitleId,
            new MediaScoreUpdateDto { Score = 75m },
            CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        Assert.Equal(75m, (await fixture.DbContext.MediaLibraryEntries.SingleAsync()).Score);
        var queuedOperation = await fixture.DbContext.MediaProviderOperations.SingleAsync();
        Assert.Equal(MediaProviderOperationTypes.UpdateScore, queuedOperation.OperationType);
        Assert.Equal(MediaProviderOperationStatuses.Pending, queuedOperation.Status);
        Assert.Equal(0, provider.ScoreUpdateCallCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100.01)]
    public async Task UpdateScore_RejectsScoresOutsideCanonicalRange(decimal score)
    {
        await using var fixture = await MediaControllerFixture.CreateAsync(new StubMediaProvider());
        var entry = await SeedMediaEntryAsync(fixture);

        var result = await fixture.Controller.UpdateScore(
            entry.MediaTitleId,
            new MediaScoreUpdateDto { Score = score },
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Null((await fixture.DbContext.MediaLibraryEntries.SingleAsync()).Score);
    }

    [Fact]
    public async Task UpdateStatus_ReturnsAcceptedWhenOperationRemainsQueued()
    {
        var provider = new StubMediaProvider
        {
            ThrowOnStatusUpdate = true
        };
        await using var fixture = await MediaControllerFixture.CreateAsync(provider);
        var entry = await SeedMediaEntryAsync(fixture);

        var result = await fixture.Controller.UpdateStatus(
            entry.MediaTitleId,
            new MediaStatusUpdateDto { Status = MediaLibraryStatuses.Repeating },
            CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);

        var persistedEntry = await fixture.DbContext.MediaLibraryEntries.SingleAsync(item => item.Id == entry.Id);
        Assert.Equal(MediaLibraryStatuses.Repeating, persistedEntry.Status);
        Assert.Equal(
            ["Favorites"],
            await fixture.DbContext.MediaProviderListMemberships.Select(membership => membership.Name).ToListAsync());
        Assert.Equal(MediaMutationSources.UserStatusUpdate, persistedEntry.LastMutationSource);
        Assert.NotNull(persistedEntry.LastLocalEditAt);

        var queuedOperation = await fixture.DbContext.MediaProviderOperations.SingleAsync();
        Assert.Equal(MediaProviderOperationStatuses.Pending, queuedOperation.Status);
        Assert.Null(queuedOperation.LastError);
        Assert.Equal(0, provider.StatusUpdateCallCount);
    }

    [Fact]
    public async Task UpdateStatus_ImmediatelyMovesEntryBetweenCanonicalLibraryStatusQueries()
    {
        var provider = new StubMediaProvider
        {
            ThrowOnStatusUpdate = true
        };
        await using var fixture = await MediaControllerFixture.CreateAsync(provider);
        var entry = await SeedMediaEntryAsync(fixture);
        var queryService = new MediaLibraryQueryService(fixture.DbContext);

        var currentBefore = await queryService.GetLibraryAsync(
            fixture.UserId,
            new MediaLibraryQueryOptions { Status = MediaLibraryStatuses.Current },
            CancellationToken.None);
        Assert.Equal(entry.Id, Assert.Single(currentBefore.Items).Id);

        var updateResult = await fixture.Controller.UpdateStatus(
            entry.MediaTitleId,
            new MediaStatusUpdateDto { Status = MediaLibraryStatuses.Completed },
            CancellationToken.None);
        Assert.IsType<AcceptedResult>(updateResult);
        fixture.DbContext.ChangeTracker.Clear();

        var currentAfter = await queryService.GetLibraryAsync(
            fixture.UserId,
            new MediaLibraryQueryOptions { Status = MediaLibraryStatuses.Current },
            CancellationToken.None);
        var completedAfter = await queryService.GetLibraryAsync(
            fixture.UserId,
            new MediaLibraryQueryOptions { Status = MediaLibraryStatuses.Completed },
            CancellationToken.None);

        Assert.Empty(currentAfter.Items);
        var completedEntry = Assert.Single(completedAfter.Items);
        Assert.Equal(entry.Id, completedEntry.Id);
        Assert.Equal(["Favorites"], completedEntry.ProviderListNames);
        Assert.Equal(MediaProviderOperationStatuses.Pending,
            (await fixture.DbContext.MediaProviderOperations.SingleAsync()).Status);
        Assert.Equal(0, provider.StatusUpdateCallCount);
    }

    private sealed class MediaControllerFixture : IAsyncDisposable
    {
        private MediaControllerFixture(
            SqliteConnection connection,
            ApplicationDbContext dbContext,
            MediaProvidersController controller,
            MediaLibraryEventHub eventHub,
            ChannelReader<MediaLibraryChangedEvent> libraryEvents,
            Guid libraryEventSubscriptionId,
            int userId)
        {
            _connection = connection;
            DbContext = dbContext;
            Controller = controller;
            EventHub = eventHub;
            LibraryEvents = libraryEvents;
            _libraryEventSubscriptionId = libraryEventSubscriptionId;
            UserId = userId;
        }

        private readonly SqliteConnection _connection;
        private readonly Guid _libraryEventSubscriptionId;

        public ApplicationDbContext DbContext { get; }

        public MediaProvidersController Controller { get; }
        public MediaLibraryEventHub EventHub { get; }
        public ChannelReader<MediaLibraryChangedEvent> LibraryEvents { get; }

        public int UserId { get; }

        public static async Task<MediaControllerFixture> CreateAsync(
            StubMediaProvider provider,
            IAioStreamsAnimeEnricher? aioStreamsAnimeEnricher = null)
        {
            const int userId = 901;
            const string email = "media.dto@example.com";

            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;

            var dbContext = new ApplicationDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            dbContext.Users.Add(TestUserFactory.Create(userId, email));
            await dbContext.SaveChangesAsync();

            var registry = new MediaProviderRegistry([provider]);
            var importQueue = new MediaLibraryImportQueue();
            var eventHub = new MediaLibraryEventHub();
            var libraryEvents = eventHub.Subscribe(userId, out var libraryEventSubscriptionId);
            var operationProcessor = new MediaProviderOperationProcessor(dbContext, registry, NullLogger<MediaProviderOperationProcessor>.Instance);
            var episodeIdentityService = new MediaEpisodeIdentityService(
                dbContext,
                new MediaProviderSeasonMappingService(
                    dbContext,
                    NullLogger<MediaProviderSeasonMappingService>.Instance),
                NullLogger<MediaEpisodeIdentityService>.Instance);
            var controller = new MediaProvidersController(
                dbContext,
                registry,
                importQueue,
                operationProcessor,
                eventHub,
                episodeIdentityService,
                aioStreamsAnimeEnricher ?? new StubAioStreamsAnimeEnricher(),
                CreateUserManager(dbContext),
                NullLogger<MediaProvidersController>.Instance,
                new PassthroughDataProtectionProvider(),
                new StubFrontendUrlResolver());

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(
                        new ClaimsIdentity(
                            [
                                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                                new Claim(ClaimTypes.Name, email)
                            ],
                            authenticationType: "Test"))
                }
            };

            return new MediaControllerFixture(
                connection,
                dbContext,
                controller,
                eventHub,
                libraryEvents,
                libraryEventSubscriptionId,
                userId);
        }

        public async ValueTask DisposeAsync()
        {
            EventHub.Unsubscribe(UserId, _libraryEventSubscriptionId);
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class StubAioStreamsAnimeEnricher : IAioStreamsAnimeEnricher
    {
        public Action<MediaProviderTitleDetails>? OnEnrich { get; init; }

        public int CallCount { get; private set; }

        public Task EnrichAsync(MediaProviderTitleDetails details, CancellationToken cancellationToken)
        {
            CallCount++;
            OnEnrich?.Invoke(details);
            return Task.CompletedTask;
        }
    }

    private sealed class StubMediaProvider : IMediaProvider
    {
        public string ProviderId { get; init; } = "anilist";

        public IReadOnlyList<MediaProviderSearchResult> SearchResults { get; init; } = [];

        public MediaProviderTitleDetails? TitleDetails { get; set; }

        public MediaReleaseMetadata? ReleaseMetadata { get; init; }

        public MediaProviderMutationResult? ProgressMutationResult { get; init; }

        public MediaProviderMutationResult? StatusMutationResult { get; init; }

        public MediaProviderMutationResult? ScoreMutationResult { get; init; }

        public ConnectedServiceAccount? ConnectedAccount { get; init; }

        public bool ThrowOnProgressUpdate { get; init; }

        public bool ThrowOnStatusUpdate { get; init; }

        public bool ThrowOnTitleDetails { get; set; }

        public int ProgressUpdateCallCount { get; private set; }
        public int StatusUpdateCallCount { get; private set; }
        public int ScoreUpdateCallCount { get; private set; }

        public Task<ConnectedServiceAccount?> GetConnectedAccountAsync(int userId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ConnectedAccount);
        }

        public string GetAuthorizationUrl(string redirectUri, string state, string codeChallenge)
        {
            throw new NotSupportedException();
        }

        public Task<ConnectedServiceAccount> ExchangeCodeAndSaveAsync(
            int userId,
            string authorizationCode,
            string redirectUri,
            string codeVerifier,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task DisconnectAsync(int userId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<MediaProviderLibraryImportResult> ImportLibraryAsync(int userId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<MediaProviderSearchResult>> SearchAsync(int userId, MediaCatalogSearchRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(SearchResults);
        }

        public Task<MediaProviderTitleDetails?> GetTitleDetailsAsync(int userId, string providerMediaId, CancellationToken cancellationToken)
        {
            if (ThrowOnTitleDetails)
            {
                throw new InvalidOperationException("Simulated title details failure.");
            }

            return Task.FromResult(TitleDetails);
        }

        public Task<MediaProviderMutationResult> UpdateProgressAsync(int userId, MediaProgressUpdateRequest request, CancellationToken cancellationToken)
        {
            ProgressUpdateCallCount++;
            if (ThrowOnProgressUpdate)
            {
                throw new InvalidOperationException("Simulated provider write failure.");
            }

            return Task.FromResult(ProgressMutationResult ?? new MediaProviderMutationResult
            {
                ProviderId = ProviderId,
                ProviderMediaId = request.ProviderMediaId,
                AppliedAt = DateTimeOffset.UtcNow,
                LastRemoteUpdateAt = DateTimeOffset.UtcNow
            });
        }

        public Task<MediaProviderMutationResult> UpdateStatusAsync(int userId, MediaStatusUpdateRequest request, CancellationToken cancellationToken)
        {
            StatusUpdateCallCount++;
            if (ThrowOnStatusUpdate)
            {
                throw new InvalidOperationException("Simulated provider write failure.");
            }

            return Task.FromResult(StatusMutationResult ?? new MediaProviderMutationResult
            {
                ProviderId = ProviderId,
                ProviderMediaId = request.ProviderMediaId,
                AppliedAt = DateTimeOffset.UtcNow,
                LastRemoteUpdateAt = DateTimeOffset.UtcNow,
            });
        }

        public Task<MediaProviderMutationResult> UpdateScoreAsync(int userId, MediaScoreUpdateRequest request, CancellationToken cancellationToken)
        {
            ScoreUpdateCallCount++;
            if (ThrowOnStatusUpdate)
            {
                throw new InvalidOperationException("Simulated provider write failure.");
            }

            return Task.FromResult(ScoreMutationResult ?? new MediaProviderMutationResult
            {
                ProviderId = ProviderId,
                ProviderMediaId = request.ProviderMediaId,
                AppliedAt = DateTimeOffset.UtcNow,
                LastRemoteUpdateAt = DateTimeOffset.UtcNow
            });
        }

        public Task<MediaReleaseMetadata?> GetReleaseMetadataAsync(int userId, string providerMediaId, CancellationToken cancellationToken)
        {
            return Task.FromResult(ReleaseMetadata);
        }
    }

    private sealed class PassthroughDataProtectionProvider : IDataProtectionProvider
    {
        public IDataProtector CreateProtector(string purpose)
        {
            return new PassthroughDataProtector();
        }
    }

    private sealed class PassthroughDataProtector : IDataProtector
    {
        public IDataProtector CreateProtector(string purpose)
        {
            return this;
        }

        public byte[] Protect(byte[] plaintext)
        {
            return plaintext;
        }

        public byte[] Unprotect(byte[] protectedData)
        {
            return protectedData;
        }
    }

    private sealed class StubFrontendUrlResolver : IFrontendUrlResolver
    {
        public string GetFrontendUrl()
        {
            return "https://frontend.test";
        }

        public string GetCurrentRequestBaseUrl()
        {
            return "https://api.test";
        }

        public string GetCallbackUrl(string relativePath)
        {
            return $"https://api.test/{relativePath.TrimStart('/')}";
        }
    }

    private static UserManager<User> CreateUserManager(ApplicationDbContext dbContext)
    {
        var store = new UserStore<User, IdentityRole<int>, ApplicationDbContext, int>(dbContext);
        return new UserManager<User>(
            store,
            optionsAccessor: Options.Create(new IdentityOptions()),
            passwordHasher: new PasswordHasher<User>(),
            userValidators: [],
            passwordValidators: [],
            keyNormalizer: new UpperInvariantLookupNormalizer(),
            errors: new IdentityErrorDescriber(),
            services: new ServiceCollection().BuildServiceProvider(),
            logger: NullLogger<UserManager<User>>.Instance);
    }

    private static MediaProviderTitleDetails CreateTitleDetails(params MediaProviderAvailabilityLink[] availabilityLinks)
        => new()
        {
            ProviderId = "anilist",
            ProviderMediaId = "140960",
            Title = "Spy x Family",
            NativeTitle = "SPY x FAMILY",
            MediaKind = MediaKinds.Anime,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            AvailabilityLinks = availabilityLinks
        };

    private static async Task<MediaLibraryEntry> SeedMediaEntryAsync(MediaControllerFixture fixture)
    {
        var now = DateTimeOffset.UtcNow;
        var account = new ConnectedServiceAccount
        {
            Id = 1901,
            UserId = fixture.UserId,
            Service = "anilist",
            ExternalAccountId = "viewer-901",
            DisplayName = "Queue Tester",
            CreatedAt = now.UtcDateTime,
            UpdatedAt = now.UtcDateTime
        };
        var title = new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = "Spy x Family",
            MediaKind = MediaKinds.Anime,
            SupportsEpisodeProgress = true,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            CreatedAt = now,
            UpdatedAt = now
        };
        var entry = new MediaLibraryEntry
        {
            Id = Guid.NewGuid(),
            UserId = fixture.UserId,
            MediaTitleId = title.Id,
            Status = MediaLibraryStatuses.Current,
            ProgressEpisodes = 12,
            CreatedAt = now,
            UpdatedAt = now
        };
        var link = new MediaProviderLink
        {
            Id = Guid.NewGuid(), MediaTitleId = title.Id, Provider = "anilist", ExternalId = "140960",
            LinkSource = MediaMappingSources.Imported, CreatedAt = now, UpdatedAt = now
        };
        var binding = new MediaLibraryProviderBinding
        {
            Id = Guid.NewGuid(), MediaLibraryEntryId = entry.Id, MediaProviderLinkId = link.Id,
            ConnectedServiceAccountId = account.Id, ProviderAccountId = account.ExternalAccountId,
            LastRemoteUpdateAt = now.AddMinutes(-2), CreatedAt = now, UpdatedAt = now,
            ProviderListMemberships = [new MediaProviderListMembership { Name = "Favorites" }]
        };

        fixture.DbContext.ConnectedServiceAccounts.Add(account);
        fixture.DbContext.MediaTitles.Add(title);
        fixture.DbContext.MediaProviderLinks.Add(link);
        fixture.DbContext.MediaLibraryEntries.Add(entry);
        fixture.DbContext.MediaLibraryProviderBindings.Add(binding);
        await fixture.DbContext.SaveChangesAsync();

        return entry;
    }
}
