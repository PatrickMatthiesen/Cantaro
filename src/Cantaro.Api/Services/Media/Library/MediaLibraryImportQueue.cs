using System.Collections.Concurrent;
using System.Threading.Channels;
using Cantaro.Api.Models;

namespace Cantaro.Api.Services;

public sealed record MediaLibraryImportWorkItem(Guid ImportId, int UserId, string ProviderId)
{
    internal int? AccountId { get; set; }
    internal string? ExternalAccountId { get; set; }
}

public sealed class MediaLibraryImportQueue
{
    private readonly ConcurrentDictionary<Guid, MediaLibraryImportWorkItem> _paused = new();
    private readonly Channel<MediaLibraryImportWorkItem> _queue = Channel.CreateUnbounded<MediaLibraryImportWorkItem>();
    private readonly ConcurrentDictionary<string, Guid> _activeImports = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, MediaLibraryImportEventDto> _latestEvents = new();
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<Guid, Channel<MediaLibraryImportEventDto>>> _subscribers = new();

    public ValueTask<MediaLibraryImportWorkItem> EnqueueAsync(int userId, string providerId, CancellationToken cancellationToken)
    {
        var key = GetImportKey(userId, providerId);
        var importId = Guid.NewGuid();
        while (!_activeImports.TryAdd(key, importId))
        {
            if (!_activeImports.TryGetValue(key, out var activeId))
            {
                continue;
            }

            if (_paused.TryRemove(activeId, out var paused))
            {
                Queue(paused);
                return ValueTask.FromResult(paused);
            }

            return ValueTask.FromResult(new MediaLibraryImportWorkItem(activeId, userId, providerId));
        }

        var workItem = new MediaLibraryImportWorkItem(importId, userId, providerId);
        Queue(workItem);
        return ValueTask.FromResult(workItem);
    }

    private void Queue(MediaLibraryImportWorkItem workItem)
    {
        Publish(workItem.UserId, new MediaLibraryImportEventDto
        {
            ProviderId = workItem.ProviderId,
            ImportId = workItem.ImportId,
            Status = "queued",
            OccurredAt = DateTimeOffset.UtcNow
        });

        _queue.Writer.TryWrite(workItem);
    }

    public async IAsyncEnumerable<MediaLibraryImportWorkItem> ReadAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var nextPausedReview = DateTimeOffset.UtcNow.AddSeconds(30);
        while (!cancellationToken.IsCancellationRequested)
        {
            if (DateTimeOffset.UtcNow >= nextPausedReview)
            {
                nextPausedReview = DateTimeOffset.UtcNow.AddSeconds(30);
                foreach (var entry in _paused.ToArray())
                    if (_paused.TryRemove(entry.Key, out var paused)) yield return paused;
            }
            if (_queue.Reader.TryRead(out var item))
            {
                yield return item;
                continue;
            }
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var delay = nextPausedReview - DateTimeOffset.UtcNow;
            idle.CancelAfter(delay > TimeSpan.Zero ? delay : TimeSpan.Zero);
            try { await _queue.Reader.WaitToReadAsync(idle.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        }
    }

    public void Pause(MediaLibraryImportWorkItem item) => _paused[item.ImportId] = item;

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
