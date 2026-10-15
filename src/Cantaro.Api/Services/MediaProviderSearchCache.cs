using Microsoft.Extensions.Caching.Memory;

namespace Cantaro.Api.Services;

public sealed class MediaProviderSearchCache : IDisposable
{
    public const int MaximumEntries = 256;
    public static readonly TimeSpan EntryLifetime = TimeSpan.FromMinutes(10);
    private readonly MemoryCache _memoryCache = new(new MemoryCacheOptions { SizeLimit = MaximumEntries });
    private readonly object _gateLock = new();
    private readonly Dictionary<string, GateEntry> _gates = new(StringComparer.Ordinal);

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

    public async Task<IReadOnlyList<CachedMediaProviderSearchResult>> GetOrCreateAsync(
        int userId, string providerId, string query, int limit,
        Func<CancellationToken, Task<IReadOnlyList<MediaProviderSearchResult>>> fetch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fetch);
        if (TryGet(userId, providerId, query, limit, out var cached)) return cached;
        var key = BuildKey(userId, providerId, query);
        var gate = AddGateReference(key);
        var acquired = false;
        try
        {
            await gate.Semaphore.WaitAsync(cancellationToken);
            acquired = true;
            if (TryGet(userId, providerId, query, limit, out cached)) return cached;
            var fetched = await fetch(cancellationToken);
            var projection = CreatePublicProjection(fetched);
            Set(userId, providerId, query, limit, projection);
            return projection;
        }
        finally
        {
            if (acquired) gate.Semaphore.Release();
            RemoveGateReference(key, gate);
        }
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

    private GateEntry AddGateReference(string key)
    {
        lock (_gateLock)
        {
            if (!_gates.TryGetValue(key, out var entry)) _gates[key] = entry = new GateEntry();
            entry.ReferenceCount++;
            return entry;
        }
    }

    private void RemoveGateReference(string key, GateEntry entry)
    {
        lock (_gateLock)
        {
            entry.ReferenceCount--;
            if (entry.ReferenceCount == 0 && _gates.TryGetValue(key, out var current) && ReferenceEquals(entry, current))
            {
                _gates.Remove(key);
                entry.Semaphore.Dispose();
            }
        }
    }

    private sealed class GateEntry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int ReferenceCount { get; set; }
    }

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
