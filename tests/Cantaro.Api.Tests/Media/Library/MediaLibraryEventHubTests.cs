using System.Text.Json;
using Cantaro.Api.Services;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class MediaLibraryEventHubTests
{
    [Fact]
    public void Publish_IsIsolatedByUserAndFansOutToEverySubscriber()
    {
        var hub = new MediaLibraryEventHub();
        var first = hub.Subscribe(101, out _);
        var second = hub.Subscribe(101, out _);
        var otherUser = hub.Subscribe(202, out _);

        hub.Publish(101);

        Assert.True(first.TryRead(out _));
        Assert.True(second.TryRead(out _));
        Assert.False(otherUser.TryRead(out _));
    }

    [Fact]
    public void Publish_CoalescesBurstsIntoOneGenericInvalidation()
    {
        var hub = new MediaLibraryEventHub();
        var reader = hub.Subscribe(101, out _);

        for (var index = 0; index < 20; index++)
        {
            hub.Publish(101);
        }

        Assert.True(reader.TryRead(out var libraryEvent));
        Assert.NotNull(libraryEvent);
        Assert.False(reader.TryRead(out _));
        Assert.Equal("{}", JsonSerializer.Serialize(libraryEvent, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Fact]
    public async Task Unsubscribe_CompletesReaderAndRemovesSubscription()
    {
        var hub = new MediaLibraryEventHub();
        var reader = hub.Subscribe(101, out var subscriptionId);

        hub.Unsubscribe(101, subscriptionId);

        Assert.False(await reader.WaitToReadAsync());
        Assert.Equal(0, hub.GetSubscriberCount(101));
    }
}
