using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Xunit;

namespace Cantaro.Api.Tests;

public class MediaLibraryImportQueueTests
{
    [Fact]
    public async Task EnqueueAsync_StoresQueuedEventForLateSubscribers()
    {
        var queue = new MediaLibraryImportQueue();

        var workItem = await queue.EnqueueAsync(123, "AniList", CancellationToken.None);

        Assert.True(queue.TryGetLatestEvent(workItem.ImportId, out var queuedEvent));
        Assert.NotNull(queuedEvent);
        Assert.Equal("AniList", queuedEvent.ProviderId);
        Assert.Equal(workItem.ImportId, queuedEvent.ImportId);
        Assert.Equal("queued", queuedEvent.Status);
    }

    [Fact]
    public async Task Publish_ReplacesLatestEventForImport()
    {
        var queue = new MediaLibraryImportQueue();
        var workItem = await queue.EnqueueAsync(123, "AniList", CancellationToken.None);
        var completedAt = DateTimeOffset.UtcNow;

        queue.Publish(123, new MediaLibraryImportEventDto
        {
            ProviderId = "AniList",
            ImportId = workItem.ImportId,
            Status = "completed",
            CreatedEntries = 2,
            UpdatedEntries = 3,
            OccurredAt = completedAt
        });

        Assert.True(queue.TryGetLatestEvent(workItem.ImportId, out var latestEvent));
        Assert.NotNull(latestEvent);
        Assert.Equal("completed", latestEvent.Status);
        Assert.Equal(2, latestEvent.CreatedEntries);
        Assert.Equal(3, latestEvent.UpdatedEntries);
        Assert.Equal(completedAt, latestEvent.OccurredAt);
    }

    [Fact]
    public async Task EnqueueAsync_ReusesActiveImport()
    {
        var queue = new MediaLibraryImportQueue();

        var firstWorkItem = await queue.EnqueueAsync(123, "AniList", CancellationToken.None);
        var secondWorkItem = await queue.EnqueueAsync(123, "anilist", CancellationToken.None);

        Assert.Equal(firstWorkItem.ImportId, secondWorkItem.ImportId);
    }
}
