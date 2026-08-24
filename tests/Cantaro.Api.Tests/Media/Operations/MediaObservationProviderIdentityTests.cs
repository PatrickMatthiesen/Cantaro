using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class MediaObservationProviderIdentityTests
{
    [Fact]
    public void MyAnimeListUrl_QualifiesBareIdFromAnimePath()
    {
        var candidates = MediaObservationMatchingService.GetProviderMediaIdCandidates(
            MediaObservationSiteIdentifiers.MyAnimeList,
            "52991",
            "https://myanimelist.net/anime/52991/Frieren");

        Assert.Equal(["anime:52991"], candidates);
    }

    [Fact]
    public void MyAnimeListBareIdWithoutKind_RemainsAmbiguousAcrossNamespaces()
    {
        var candidates = MediaObservationMatchingService.GetProviderMediaIdCandidates(
            MediaObservationSiteIdentifiers.MyAnimeList,
            "42",
            "https://myanimelist.net/profile/example");

        Assert.Equal(["anime:42", "manga:42"], candidates);
    }

    [Fact]
    public void NonMalProviderId_PreservesCase()
    {
        var candidates = MediaObservationMatchingService.GetProviderMediaIdCandidates(
            MediaObservationSiteIdentifiers.AniList,
            "AbC-123",
            "https://anilist.co/anime/AbC-123");

        Assert.Equal(["AbC-123"], candidates);
    }
}
