using System.Text.Json;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class ObservationInputTests
{
    [Fact]
    public void PublicInputReadsLoadedFactsWithoutPrivateFieldsAndExpires()
    {
        var now = DateTimeOffset.UtcNow;
        var item = PublicItem(now);
        var observation = new TrackObservation
        { InputKind = ObservationInputKind.Private,
            Id = Guid.NewGuid(), OwnerUserId = 1, SourceType = item.SourceType,
            ExternalId = item.ExternalId, MatchStatus = TrackMatchingStatuses.Pending
        };
        ObservationInput.SetPublic(observation, item);
        Assert.Null(observation.Title);
        Assert.Null(observation.PrivateInputJson);
        Assert.Equal("Public song", ObservationInput.Read(observation, 1, now)!.Title);
        Assert.Null(ObservationInput.Read(observation, 2, now));
        Assert.Null(ObservationInput.Read(observation, 1, now.AddHours(2)));
        observation.InputKind = ObservationInputKind.Reference;
        Assert.Null(ObservationInput.Read(observation, 1, now));
    }

    [Fact]
    public void PrivateInputStoresOnlyRemainderAndDoesNotRenewPublicVerification()
    {
        var now = DateTimeOffset.UtcNow;
        var item = PublicItem(now);
        var observation = new TrackObservation
        { InputKind = ObservationInputKind.Private,
            Id = Guid.NewGuid(), OwnerUserId = 1, SourceType = item.SourceType,
            ExternalId = item.ExternalId, MatchStatus = TrackMatchingStatuses.Pending,
            ProviderCatalogItem = item, ProviderCatalogItemId = item.Id
        };
        ObservationInput.SetPrivate(observation, "Private song", "Private artist", 90, null,
            new TrackObservationMetadata
            {
                Title = "Private song", Artist = "Private artist", DurationSeconds = 90,
                SearchTitle = "Transient hypothesis", SourceUrl = "https://example.test/?token=secret",
                Description = "Private description", OriginalTitle = "Private song",
                ArtistIdentities = [new ArtistCreditMetadata { Name = "Private artist", Provider = "spotify", ExternalId = "artist-id",
                    ProfileUrl = "https://profile.test/unused", ArtworkUrl = "https://artwork.test/unused", RawMetadata = "unused raw provider response" }]
            });
        Assert.DoesNotContain("Private song", observation.PrivateInputJson!);
        Assert.DoesNotContain("secret", observation.PrivateInputJson!);
        Assert.DoesNotContain("Transient hypothesis", observation.PrivateInputJson!);
        Assert.DoesNotContain("unused", observation.PrivateInputJson!);
        Assert.Equal("artist-id", Assert.Single(ObservationInput.Read(observation, 1, now)!.Metadata.ArtistIdentities).ExternalId);
        Assert.Equal("Private description", ObservationInput.Read(observation, 1, now)!.Metadata.Description);
        Assert.Null(ObservationInput.Read(observation, 2, now));
        Assert.Equal(now.AddHours(1), item.RevalidateAfter);
        Assert.Equal("Public description", JsonSerializer.Deserialize<ProviderCatalogFacts>(item.FactsJson)!.Description);
    }

    [Fact]
    public void PublicCandidateRequiresItsExactCurrentRevision()
    {
        var now = DateTimeOffset.UtcNow;
        var item = PublicItem(now);
        var candidate = new TrackResolutionCandidate
        {
            Id = Guid.NewGuid(), TrackObservationId = Guid.NewGuid(), CandidateSource = item.SourceType,
            ExternalId = item.ExternalId, ProviderCatalogItem = item,
            ProviderCatalogItemId = item.Id, ProviderCatalogRevision = item.Revision
        };
        Assert.Equal("Public song", TrackMatchCandidateInput.Read(candidate, now)!.Title);
        item.Revision++;
        Assert.Null(TrackMatchCandidateInput.Read(candidate, now));
    }

    private static ProviderCatalogItem PublicItem(DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = "video", Revision = 1,
        VerificationMethod = CatalogVerificationMethods.YouTubePublicVideo,
        VisibilityState = CatalogVisibility.Public, VerifiedAt = now, RevalidateAfter = now.AddHours(1),
        FactsJson = JsonSerializer.Serialize(new ProviderCatalogFacts
        {
            Title = "Public song", Artist = "Public artist", Description = "Public description"
        })
    };
}
