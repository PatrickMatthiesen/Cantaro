using Microsoft.Extensions.Caching.Memory;

namespace Cantaro.Api.Services;

public sealed class MediaProviderSearchCache : IDisposable
{
    public const int MaximumEntries = 256;
    public static readonly TimeSpan EntryLifetime = TimeSpan.FromMinutes(10);

    private readonly MemoryCache _memoryCache = new(new MemoryCacheOptions
    {
        SizeLimit = MaximumEntries
    });

    public bool TryGet(
        int userId,
        string providerId,
        string query,
        int limit,
        out IReadOnlyList<CachedMediaProviderSearchResult> results)
    {
        return _memoryCache.TryGetValue(
            BuildKey(userId, providerId, query, limit),
            out results!);
    }

    public void Set(
        int userId,
        string providerId,
        string query,
        int limit,
        IReadOnlyList<CachedMediaProviderSearchResult> results)
    {
        _memoryCache.Set(
            BuildKey(userId, providerId, query, limit),
            results,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = EntryLifetime,
                Size = 1
            });
    }

    public static IReadOnlyList<CachedMediaProviderSearchResult> CreatePublicProjection(
        IEnumerable<MediaProviderSearchResult> results)
    {
        var projection = results
            .Where(result => !string.IsNullOrWhiteSpace(result.ProviderMediaId))
            .Select(result => new CachedMediaProviderSearchResult(
                result.ProviderMediaId,
                result.Title,
                result.MediaKind,
                result.PosterUrl,
                result.StartYear))
            .ToArray();

        return Array.AsReadOnly(projection);
    }

    private static string BuildKey(
        int userId,
        string providerId,
        string query,
        int limit) =>
        $"media-provider-search:{userId}:{providerId.Trim().ToLowerInvariant()}:{query.Trim().ToLowerInvariant()}:{limit}";

    public void Dispose()
    {
        _memoryCache.Dispose();
    }
}

public sealed record CachedMediaProviderSearchResult(
    string ProviderMediaId,
    string Title,
    string MediaKind,
    string? PosterUrl,
    int? StartYear);
