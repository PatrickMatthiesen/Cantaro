using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Cantaro.Api.Services;

public sealed record MediaRelationGraphRefreshWorkItem(
    int UserId,
    string ProviderId,
    string ProviderMediaId);

public sealed class MediaRelationGraphRefreshQueue
{
    private readonly Channel<MediaRelationGraphRefreshWorkItem> _channel =
        Channel.CreateUnbounded<MediaRelationGraphRefreshWorkItem>();
    private readonly ConcurrentDictionary<string, byte> _queued = new(StringComparer.Ordinal);

    public bool Enqueue(int userId, string providerId, string providerMediaId)
    {
        var normalizedProvider = providerId.Trim().ToLowerInvariant();
        var normalizedMediaId = providerMediaId.Trim();
        var key = $"{userId}:{normalizedProvider}:{normalizedMediaId}";
        return _queued.TryAdd(key, 0)
            && _channel.Writer.TryWrite(new MediaRelationGraphRefreshWorkItem(
                userId,
                normalizedProvider,
                normalizedMediaId));
    }

    public IAsyncEnumerable<MediaRelationGraphRefreshWorkItem> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);

    public void Complete(MediaRelationGraphRefreshWorkItem item)
        => _queued.TryRemove($"{item.UserId}:{item.ProviderId}:{item.ProviderMediaId}", out _);
}
