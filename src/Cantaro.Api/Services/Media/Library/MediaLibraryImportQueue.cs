using System.Collections.Concurrent;
using System.Threading.Channels;
using Cantaro.Api.Models;

namespace Cantaro.Api.Services;

public sealed record MediaLibraryImportWorkItem(Guid ImportId, int UserId, string ProviderId);

public sealed class MediaLibraryImportQueue
{
    private readonly Channel<MediaLibraryImportWorkItem> _queue = Channel.CreateUnbounded<MediaLibraryImportWorkItem>();
    private readonly ConcurrentDictionary<string, Guid> _activeImports = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, MediaLibraryImportEventDto> _latestEvents = new();
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<Guid, Channel<MediaLibraryImportEventDto>>> _subscribers = new();

    public ValueTask<MediaLibraryImportWorkItem> EnqueueAsync(int userId, string providerId, CancellationToken cancellationToken)
    {
        var key = GetImportKey(userId, providerId);
        var importId = Guid.NewGuid();
        if (!_activeImports.TryAdd(key, importId))
        {
            return ValueTask.FromResult(new MediaLibraryImportWorkItem(_activeImports[key], userId, providerId));
        }

        var workItem = new MediaLibraryImportWorkItem(importId, userId, providerId);
        _latestEvents[importId] = new MediaLibraryImportEventDto
        {
            ProviderId = providerId,
            ImportId = importId,
            Status = "queued",
            OccurredAt = DateTimeOffset.UtcNow
        };

        _queue.Writer.TryWrite(workItem);

        return ValueTask.FromResult(workItem);
    }

    public IAsyncEnumerable<MediaLibraryImportWorkItem> ReadAllAsync(CancellationToken cancellationToken)
    {
        return _queue.Reader.ReadAllAsync(cancellationToken);
    }

    public ChannelReader<MediaLibraryImportEventDto> Subscribe(int userId, out Guid subscriptionId)
    {
        subscriptionId = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<MediaLibraryImportEventDto>();
        var userSubscribers = _subscribers.GetOrAdd(userId, _ => new ConcurrentDictionary<Guid, Channel<MediaLibraryImportEventDto>>());
        userSubscribers[subscriptionId] = channel;
        return channel.Reader;
    }

    public void Unsubscribe(int userId, Guid subscriptionId)
    {
        if (!_subscribers.TryGetValue(userId, out var userSubscribers)
            || !userSubscribers.TryRemove(subscriptionId, out var channel))
        {
            return;
        }

        channel.Writer.TryComplete();
        if (userSubscribers.IsEmpty)
        {
            _subscribers.TryRemove(userId, out _);
        }
    }

    public void Publish(int userId, MediaLibraryImportEventDto importEvent)
    {
        _latestEvents[importEvent.ImportId] = importEvent;

        if (!_subscribers.TryGetValue(userId, out var userSubscribers))
        {
            return;
        }

        foreach (var subscriber in userSubscribers.Values)
        {
            subscriber.Writer.TryWrite(importEvent);
        }
    }

    public bool TryGetLatestEvent(Guid importId, out MediaLibraryImportEventDto? importEvent)
    {
        return _latestEvents.TryGetValue(importId, out importEvent);
    }

    public void Complete(MediaLibraryImportWorkItem workItem)
    {
        _activeImports.TryRemove(GetImportKey(workItem.UserId, workItem.ProviderId), out _);
    }

    private static string GetImportKey(int userId, string providerId)
    {
        return $"{userId}:{providerId.ToLowerInvariant()}";
    }
}
