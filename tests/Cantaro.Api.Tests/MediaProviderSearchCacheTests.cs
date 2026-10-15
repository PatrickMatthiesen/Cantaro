using Cantaro.Api.Services;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class MediaProviderSearchCacheTests
{
    [Fact]
    public void Set_ReusesLargerSnapshotAndKeepsProjectionImmutable()
    {
        using var cache = new MediaProviderSearchCache();
        var synonyms = new List<string> { "Alias" };
        var references = new List<MediaProviderCrossReference>
        { new() { ProviderId = "myanimelist", ProviderMediaId = "anime:42", ExternalUrl = "https://example.test/42" } };
        var projection = MediaProviderSearchCache.CreatePublicProjection([new()
        {
            ProviderId = "anilist", ProviderMediaId = "7", Title = "Title", MediaKind = "anime",
            PrimaryProgressDimension = "episode", ReleaseStatusDimension = "episode", Synonyms = synonyms,
            CrossReferences = references, Format = "tv", ExternalUrl = "https://anilist.co/anime/7"
        }]);

        synonyms[0] = "Changed";
        references.Clear();
        cache.Set(1, "anilist", "title", 10, projection);

        Assert.True(cache.TryGet(1, "anilist", "title", 5, out var reused));
        Assert.False(cache.TryGet(1, "anilist", "title", 11, out _));
        Assert.Equal("Alias", reused[0].Synonyms[0]);
        Assert.Equal("myanimelist", Assert.Single(reused[0].CrossReferences).ProviderId);
        Assert.Equal("tv", reused[0].Format);
    }
}
