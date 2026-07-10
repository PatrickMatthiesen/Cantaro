using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Cantaro.Api.Configuration;

namespace Cantaro.Api.Services.Lyrics;

public sealed class LyricsProviderCache : IDisposable
{
    private readonly MemoryCache _cache;

    public LyricsProviderCache(IOptions<LyricsOptions> options)
    {
        _cache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = options.Value.CacheEntryLimit
        });
    }

    public bool TryGet(string key, out LyricsResult? result) => _cache.TryGetValue(key, out result);

    public void Set(string key, LyricsResult result, TimeSpan lifetime)
    {
        _cache.Set(key, result, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = lifetime,
            Size = 1
        });
    }

    public void Dispose() => _cache.Dispose();
}
