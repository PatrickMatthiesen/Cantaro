using System.Text.Json;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class SimklJsonTests
{
    [Fact]
    public void LibraryMapping_PreservesSkippedEpisodeAndSpecial()
    {
        using var json = JsonDocument.Parse("""
            {"shows":[{"status":"watching","watched_episodes_count":4,"total_episodes_count":20,
              "user_rating":8,"show":{"title":"Example","year":2020,"ids":{"simkl":123}},
              "seasons":[{"number":0,"episodes":[{"number":1}]},
                         {"number":1,"episodes":[{"number":1},{"number":2}]},
                         {"number":2,"episodes":[{"number":3}]}]}]}
            """);
        var item = Assert.Single(SimklJson.MapLibraryItems(json.RootElement));
        Assert.Equal("tv:123", item.ProviderMediaId);
        Assert.Equal(MediaKinds.Series, item.MediaKind);
        Assert.Equal(80m, item.Score);
        Assert.Contains(item.WatchedEpisodes!, x => x.SeasonNumber == 0 && x.EpisodeNumber == 1);
        Assert.Contains(item.WatchedEpisodes!, x => x.SeasonNumber == 2 && x.EpisodeNumber == 3);
        Assert.Equal(0, item.ProgressEpisodes); // TV prefix awaits the complete episode catalog.
        Assert.True(item.HasNonContiguousProgress);
    }

    [Fact]
    public void CatalogComputesContiguousPrefixWithoutFillingGap()
    {
        using var json = JsonDocument.Parse("""
            {"shows":[{"status":"watching","watched_episodes_count":3,
              "show":{"title":"Example","ids":{"simkl":123}},
              "seasons":[{"number":1,"episodes":[{"number":1},{"number":2}]},
                         {"number":2,"episodes":[{"number":2}]}]}]}
            """);
        using var catalog = JsonDocument.Parse("""
            [{"season":1,"episode":1},{"season":1,"episode":2},
             {"season":2,"episode":1},{"season":2,"episode":2}]
            """);
        var item = Assert.Single(SimklJson.MapLibraryItems(json.RootElement));
        SimklJson.ApplyCatalog(item, SimklJson.MapEpisodeCatalog(catalog.RootElement, "tv"));
        Assert.Equal(2, item.ProgressEpisodes);
        Assert.True(item.HasNonContiguousProgress);
        Assert.Equal(3, item.WatchedEpisodes!.Count);
    }

    [Fact]
    public void EpisodeEnvelopeAddressesExactSeasonAndEpisode()
    {
        var payload = JsonSerializer.Serialize(SimklJson.EpisodeEnvelope(new SimklIdentity("tv", 123),
            [new MediaProviderWatchedEpisode { SeasonNumber = 2, EpisodeNumber = 3 }]));
        Assert.Contains("\"shows\"", payload);
        Assert.Contains("\"number\":2", payload);
        Assert.Contains("\"number\":3", payload);
        Assert.DoesNotContain("\"number\":1", payload);
    }

    [Fact]
    public void AnimeLibraryUsesShowObjectAndWritesFlatEpisodesToShowsBucket()
    {
        using var json = JsonDocument.Parse("""
            {"anime":[{"status":"watching","watched_episodes_count":2,
              "show":{"title":"Anime","ids":{"simkl":456,"mal":"12","anilist":"34"}},
              "seasons":[{"number":1,"episodes":[{"number":1},{"number":3}]}]}]}
            """);
        var item = Assert.Single(SimklJson.MapLibraryItems(json.RootElement));
        Assert.Equal("anime:456", item.ProviderMediaId);
        Assert.Equal(1, item.ProgressEpisodes);
        Assert.True(item.HasNonContiguousProgress);
        Assert.Contains(item.CrossReferences, x => x.ProviderId == "myanimelist" && x.ProviderMediaId == "anime:12");
        var payload = JsonSerializer.Serialize(SimklJson.EpisodeEnvelope(new SimklIdentity("anime", 456),
            [new MediaProviderWatchedEpisode { SeasonNumber = 1, EpisodeNumber = 2 }]));
        Assert.Contains("\"shows\"", payload);
        Assert.Contains("\"episodes\":[{\"number\":2}]", payload);
        Assert.DoesNotContain("\"seasons\"", payload);
    }
}
