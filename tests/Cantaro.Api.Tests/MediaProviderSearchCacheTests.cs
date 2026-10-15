using Cantaro.Api.Services;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class MediaProviderSearchCacheTests
{
    [Fact]
    public async Task GetOrCreateAsync_ReusesSuccessfulLargerSnapshotAndKeepsProjectionImmutable()
    {
        using var cache = new MediaProviderSearchCache();
        var synonyms = new List<string> { "Alias" };
        var references = new List<MediaProviderCrossReference>
        { new() { ProviderId = "myanimelist", ProviderMediaId = "anime:42", ExternalUrl = "https://example.test/42" } };
        var calls = 0;
        var first = await cache.GetOrCreateAsync(1, "anilist", "title", 10, _ =>
        {
            calls++;
            return Task.FromResult<IReadOnlyList<MediaProviderSearchResult>>([new()
            {
                ProviderId = "anilist", ProviderMediaId = "7", Title = "Title", MediaKind = "anime",
                PrimaryProgressDimension = "episode", ReleaseStatusDimension = "episode", Synonyms = synonyms,
                CrossReferences = references, Format = "tv", ExternalUrl = "https://anilist.co/anime/7"
            }]);
        }, default);
        synonyms[0] = "Changed";
        references.Clear();
        var reused = await cache.GetOrCreateAsync(1, "anilist", "title", 5, _ =>
        { calls++; return Task.FromResult<IReadOnlyList<MediaProviderSearchResult>>([]); }, default);

        Assert.Equal(1, calls);
        Assert.Equal(first.Select(item => item.ProviderMediaId), reused.Select(item => item.ProviderMediaId));
        Assert.Equal("Alias", reused[0].Synonyms[0]);
        Assert.Equal("myanimelist", Assert.Single(reused[0].CrossReferences).ProviderId);
        Assert.Equal("tv", reused[0].Format);
    }

    [Fact]
    public async Task GetOrCreateAsync_FetchFailureAndCancellationReleaseGateForRetry()
    {
        using var cache = new MediaProviderSearchCache();
        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetOrCreateAsync(1, "simkl", "title", 2,
            _ => throw new InvalidOperationException("failed"), default));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.GetOrCreateAsync(1, "simkl", "title", 2,
            _ => Task.FromResult<IReadOnlyList<MediaProviderSearchResult>>([]), cancellation.Token));

        var recovered = await cache.GetOrCreateAsync(1, "simkl", "title", 2,
            _ => Task.FromResult<IReadOnlyList<MediaProviderSearchResult>>([]), default);
        Assert.Empty(recovered);
    }

    [Fact]
    public async Task GetOrCreateAsync_CoalescesConcurrentFetches()
    {
        using var cache = new MediaProviderSearchCache();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        Task<IReadOnlyList<CachedMediaProviderSearchResult>> Fetch(CancellationToken ct) => cache.GetOrCreateAsync(
            1, "anilist", "same", 4, async _ =>
            {
                Interlocked.Increment(ref calls);
                entered.SetResult();
                await release.Task;
                return [new() { ProviderId = "anilist", ProviderMediaId = "1", Title = "One", MediaKind = "anime",
                    PrimaryProgressDimension = "episode", ReleaseStatusDimension = "episode" }];
            }, ct);
        var one = Fetch(default);
        await entered.Task;
        var two = Fetch(default);
        release.SetResult();
        await Task.WhenAll(one, two);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task GetOrCreateAsync_DifferentQueriesFetchWithoutBlockingEachOther()
    {
        using var cache = new MediaProviderSearchCache();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = cache.GetOrCreateAsync(1, "anilist", "slow query", 4, async _ =>
        {
            firstEntered.SetResult();
            await releaseFirst.Task;
            return Array.Empty<MediaProviderSearchResult>();
        }, default);
        await firstEntered.Task;

        var second = cache.GetOrCreateAsync(1, "anilist", "other query", 4, _ =>
        {
            secondEntered.SetResult();
            return Task.FromResult<IReadOnlyList<MediaProviderSearchResult>>([]);
        }, default);
        try
        {
            await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Empty(await second);
        }
        finally
        {
            releaseFirst.SetResult();
        }
        Assert.Empty(await first);
    }
}
