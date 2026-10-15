using Microsoft.Extensions.Caching.Memory;

namespace Cantaro.Api.Services;

public sealed class MediaProviderSearchCache : IDisposable
{
    public const int MaximumEntries = 256;
    public static readonly TimeSpan EntryLifetime = TimeSpan.FromMinutes(10);
    private readonly MemoryCache _memoryCache = new(new MemoryCacheOptions { SizeLimit = MaximumEntries });

    public bool TryGet(int userId, string providerId, string query, int limit, out IReadOnlyList<CachedMediaProviderSearchResult> results)
    {
        if (_memoryCache.TryGetValue(BuildKey(userId, providerId, query), out CacheEntry? entry)
            && entry is not null && entry.Limit >= limit)
        { results = entry.Results; return true; }
        results = Array.Empty<CachedMediaProviderSearchResult>();
        return false;
    }

    public void Set(int userId, string providerId, string query, int limit, IReadOnlyList<CachedMediaProviderSearchResult> results)
    {
        _memoryCache.Set(BuildKey(userId, providerId, query), new CacheEntry(limit, Snapshot(results)), new MemoryCacheEntryOptions
        { AbsoluteExpirationRelativeToNow = EntryLifetime, Size = 1 });
    }

    public static IReadOnlyList<CachedMediaProviderSearchResult> CreatePublicProjection(IEnumerable<MediaProviderSearchResult> results)
    {
        var projection = results.Where(result => !string.IsNullOrWhiteSpace(result.ProviderMediaId))
            .Select(result => new CachedMediaProviderSearchResult(result.ProviderMediaId, result.Title, result.MediaKind, result.PosterUrl, result.StartYear)
            {
                NativeTitle = result.NativeTitle,
                Synonyms = Array.AsReadOnly((result.Synonyms ?? []).ToArray()),
                Synopsis = result.Synopsis,
                Format = result.Format,
                BackgroundUrl = result.BackgroundUrl,
                ExternalUrl = result.ExternalUrl,
                CrossReferences = Array.AsReadOnly((result.CrossReferences ?? []).Where(x => x is not null)
                    .Select(x => new CachedMediaProviderCrossReference(x.ProviderId, x.ProviderMediaId, x.ExternalUrl)).ToArray()),
                EpisodeCount = result.EpisodeCount, ChapterCount = result.ChapterCount, VolumeCount = result.VolumeCount,
                PrimaryProgressDimension = result.PrimaryProgressDimension, ReleaseStatusDimension = result.ReleaseStatusDimension
            }).ToArray();
        return Array.AsReadOnly(projection);
    }

    private static IReadOnlyList<CachedMediaProviderSearchResult> Snapshot(IReadOnlyList<CachedMediaProviderSearchResult> results)
        => Array.AsReadOnly(results.Select(x => x with
        {
            Synonyms = Array.AsReadOnly((x.Synonyms ?? []).ToArray()),
            CrossReferences = Array.AsReadOnly((x.CrossReferences ?? []).Select(r => r with { }).ToArray())
        }).ToArray());

    private static string BuildKey(int userId, string providerId, string query)
        => $"media-provider-search:{userId}:{providerId.Trim().ToLowerInvariant()}:{query.Trim().ToLowerInvariant()}";

    private sealed record CacheEntry(int Limit, IReadOnlyList<CachedMediaProviderSearchResult> Results);

    public void Dispose() => _memoryCache.Dispose();
}

public sealed record CachedMediaProviderSearchResult(string ProviderMediaId, string Title, string MediaKind, string? PosterUrl, int? StartYear)
{
    public string? NativeTitle { get; init; }
    public IReadOnlyList<string> Synonyms { get; init; } = Array.Empty<string>();
    public string? Synopsis { get; init; }
    public string? Format { get; init; }
    public string? BackgroundUrl { get; init; }
    public string? ExternalUrl { get; init; }
    public IReadOnlyList<CachedMediaProviderCrossReference> CrossReferences { get; init; } = Array.Empty<CachedMediaProviderCrossReference>();
    public int? EpisodeCount { get; init; }
    public int? ChapterCount { get; init; }
    public int? VolumeCount { get; init; }
    public string? PrimaryProgressDimension { get; init; }
    public string? ReleaseStatusDimension { get; init; }
}

public sealed record CachedMediaProviderCrossReference(string ProviderId, string ProviderMediaId, string? ExternalUrl);
