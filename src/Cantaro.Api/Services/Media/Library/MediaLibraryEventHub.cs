using System.Threading.Channels;

namespace Cantaro.Api.Services;

public sealed record MediaLibraryChangedEvent;

public sealed class MediaLibraryEventHub
{
    private const int SubscriberCapacity = 1;
    private readonly object _gate = new();
    private readonly Dictionary<int, Dictionary<Guid, Channel<MediaLibraryChangedEvent>>> _subscribers = [];

    public ChannelReader<MediaLibraryChangedEvent> Subscribe(int userId, out Guid subscriptionId)
    {
        var channel = Channel.CreateBounded<MediaLibraryChangedEvent>(new BoundedChannelOptions(SubscriberCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
        subscriptionId = Guid.NewGuid();

        lock (_gate)
        {
            if (!_subscribers.TryGetValue(userId, out var userSubscribers))
            {
                userSubscribers = [];
                _subscribers[userId] = userSubscribers;
            }

            userSubscribers[subscriptionId] = channel;
        }

        return channel.Reader;
    }

    public void Unsubscribe(int userId, Guid subscriptionId)
    {
        Channel<MediaLibraryChangedEvent>? channel = null;
        lock (_gate)
        {
            if (_subscribers.TryGetValue(userId, out var userSubscribers)
                && userSubscribers.Remove(subscriptionId, out channel)
                && userSubscribers.Count == 0)
            {
                _subscribers.Remove(userId);
            }
        }

        channel?.Writer.TryComplete();
    }

    public void Publish(int userId)
    {
        Channel<MediaLibraryChangedEvent>[] subscribers;
        lock (_gate)
        {
            if (!_subscribers.TryGetValue(userId, out var userSubscribers))
            {
                return;
            }

            subscribers = [.. userSubscribers.Values];
        }

        foreach (var subscriber in subscribers)
        {
            subscriber.Writer.TryWrite(new MediaLibraryChangedEvent());
        }
    }

    internal int GetSubscriberCount(int userId)
    {
        lock (_gate)
        {
            return _subscribers.TryGetValue(userId, out var subscribers) ? subscribers.Count : 0;
        }
    }
}
