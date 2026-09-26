using Cantaro.Api.Services;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class YouTubePlaylistWriterTests
{
    private static readonly PlatformAccountContext Account = new(7, 11);

    [Fact]
    public async Task ReconcileAsync_PreservesUnavailableItemsAndOrdersReadableItems()
    {
        var client = new FakeClient("b", null, "a", "a", "c");
        var writer = new YouTubePlaylistWriter(client);

        await writer.ReconcileAsync(Account, "owned", ["a", "b", "a"], CancellationToken.None);

        Assert.Equal(new string?[] { "a", "b", null, "a" }.AsEnumerable(), client.VideoIds.AsEnumerable());
        Assert.Equal(1, client.DeleteCount);
        Assert.Equal(0, client.InsertCount);
        Assert.True(client.MoveCount > 0);
        Assert.True(client.ValidateCount > 0);
    }

    [Fact]
    public async Task ReconcileAsync_PreservesKnownUnavailableVideoInsteadOfInsertingIt()
    {
        var client = new FakeClient("private-video", "a");
        client.SetUnavailable("private-video");

        await new YouTubePlaylistWriter(client).ReconcileAsync(
            Account, "owned", ["private-video", "b", "a"], CancellationToken.None);

        Assert.Equal(new string?[] { "private-video", "b", "a" }.AsEnumerable(), client.VideoIds.AsEnumerable());
        Assert.Equal(new[] { "item-0" }.AsEnumerable(), client.ProtectedItemIds.AsEnumerable());
        Assert.Equal(1, client.InsertCount);
        Assert.Equal(0, client.DeleteCount);
        Assert.DoesNotContain("private-video", client.InsertedVideoIds);
        Assert.DoesNotContain("item-0", client.MovedItemIds);
        Assert.DoesNotContain("item-0", client.DeletedItemIds);
    }

    [Fact]
    public async Task ReconcileAsync_CanReorderAndInsertAroundUnavailableItems()
    {
        var client = new FakeClient("a", "private-video", "b");
        client.SetUnavailable("private-video");

        await new YouTubePlaylistWriter(client).ReconcileAsync(
            Account, "owned", ["b", "c", "a"], CancellationToken.None);

        Assert.Equal(new string?[] { "b", "c", "a", "private-video" }.AsEnumerable(), client.VideoIds.AsEnumerable());
        Assert.Equal(new[] { "b", "c", "a" }.AsEnumerable(), client.VideoIds.Where(id => id != "private-video").AsEnumerable());
        Assert.Equal(new[] { "item-1" }.AsEnumerable(), client.ProtectedItemIds.AsEnumerable());
        Assert.DoesNotContain("item-1", client.MovedItemIds);
        Assert.DoesNotContain("item-1", client.DeletedItemIds);
    }

    [Fact]
    public async Task ReconcileAsync_AlreadyEqualDoesNotWrite()
    {
        var client = new FakeClient("a", "a", "b");

        await new YouTubePlaylistWriter(client).ReconcileAsync(
            Account, "owned", ["a", "a", "b"], CancellationToken.None);

        Assert.Equal(0, client.WriteCount);
    }

    [Fact]
    public async Task ReconcileAsync_EmptyDestinationAppendsInOrder()
    {
        var client = new FakeClient();

        await new YouTubePlaylistWriter(client).ReconcileAsync(
            Account, "owned", ["a", "b"], CancellationToken.None);

        Assert.Equal(new string?[] { "a", "b" }.AsEnumerable(), client.VideoIds.AsEnumerable());
        Assert.Equal(2, client.InsertCount);
    }

    [Fact]
    public async Task ReconcileAsync_RetryAfterAppliedInsertDoesNotDuplicateIt()
    {
        var client = new FakeClient("a") { FailAfterFirstWrite = true };
        var writer = new YouTubePlaylistWriter(client);

        var failure = await Assert.ThrowsAsync<PlatformApiException>(() => writer.ReconcileAsync(
            Account, "owned", ["a", "b"], CancellationToken.None));
        await writer.ReconcileAsync(Account, "owned", ["a", "b"], CancellationToken.None);

        Assert.Equal("youtube_unavailable", failure.Code);
        Assert.True(MusicSyncFailureClassifier.Classify(failure).Retryable);
        Assert.Equal(new string?[] { "a", "b" }.AsEnumerable(), client.VideoIds.AsEnumerable());
        Assert.Equal(1, client.InsertCount);
    }

    [Fact]
    public async Task ReconcileAsync_FailedOwnershipValidationNeverWrites()
    {
        var client = new FakeClient("a") { IsOwned = false };

        var exception = await Assert.ThrowsAsync<PlatformApiException>(() =>
            new YouTubePlaylistWriter(client).ReconcileAsync(
                Account, "foreign", ["b"], CancellationToken.None));

        Assert.Equal("destination_unavailable", exception.Code);
        Assert.Equal(0, client.ReadCount);
        Assert.Equal(0, client.WriteCount);
    }

    [Fact]
    public async Task ValidateDestinationAsync_QuotaForbiddenIsRetryableRateLimit()
    {
        var client = new FakeClient()
        {
            ValidationException = new Google.GoogleApiException("youtube", "quotaExceeded")
            {
                HttpStatusCode = System.Net.HttpStatusCode.Forbidden
            }
        };

        var failure = await Assert.ThrowsAsync<PlatformApiException>(() =>
            new YouTubePlaylistWriter(client).ValidateDestinationAsync(
                Account, "owned", CancellationToken.None));

        Assert.Equal("youtube_rate_limited", failure.Code);
        Assert.True(MusicSyncFailureClassifier.Classify(failure).Retryable);
        Assert.Equal(0, client.WriteCount);
    }

    [Fact]
    public async Task CreatePlaylistAsync_ReturnsProviderIdOnlyAfterAccountValidation()
    {
        var client = new FakeClient();

        var id = await new YouTubePlaylistWriter(client).CreatePlaylistAsync(
            Account, "Road songs", CancellationToken.None);

        Assert.Equal("new-playlist-1", id);
        Assert.Equal(1, client.CreateCount);
        Assert.Equal("Road songs", Assert.Single(client.CreatedNames));
        Assert.Equal(1, client.CreationValidationCount);
    }

    [Fact]
    public async Task CreatePlaylistAsync_AmbiguousResponseIsTerminalAndNeverRetriesPost()
    {
        var client = new FakeClient { FailCreationAfterApplying = true };

        var failure = await Assert.ThrowsAsync<PlatformApiException>(() =>
            new YouTubePlaylistWriter(client).CreatePlaylistAsync(
                Account, "Road songs", CancellationToken.None));

        Assert.Equal("youtube_playlist_creation_unknown", failure.Code);
        Assert.False(MusicSyncFailureClassifier.Classify(failure).Retryable);
        Assert.Equal(1, client.CreateCount);
    }

    [Fact]
    public async Task ValidateCreationAsync_RejectsChangedAccountWithoutCreating()
    {
        var client = new FakeClient { IsOwned = false };

        var failure = await Assert.ThrowsAsync<PlatformApiException>(() =>
            new YouTubePlaylistWriter(client).CreatePlaylistAsync(
                Account, "Road songs", CancellationToken.None));

        Assert.Equal("youtube_playlist_creation_rejected", failure.Code);
        Assert.Equal(0, client.CreateCount);
    }

    [Fact]
    public async Task CreatePlaylistAsync_PreflightFailureCannotLeaveCreationUncertain()
    {
        var client = new FakeClient
        {
            CreationValidationException = new HttpRequestException("Channel lookup failed.")
        };

        var failure = await Assert.ThrowsAsync<PlatformApiException>(() =>
            new YouTubePlaylistWriter(client).CreatePlaylistAsync(
                Account, "Road songs", CancellationToken.None));

        Assert.Equal("youtube_playlist_creation_rejected", failure.Code);
        Assert.Equal(503, failure.StatusCode);
        Assert.Equal(0, client.CreateCount);
    }

    [Fact]
    public async Task CreatePlaylistAsync_CancellationDuringPreflightCannotLeaveCreationUncertain()
    {
        var client = new FakeClient
        {
            CreationValidationException = new OperationCanceledException()
        };

        var failure = await Assert.ThrowsAsync<PlatformApiException>(() =>
            new YouTubePlaylistWriter(client).CreatePlaylistAsync(
                Account, "Road songs", CancellationToken.None));

        Assert.Equal("youtube_playlist_creation_rejected", failure.Code);
        Assert.Equal(0, client.CreateCount);
    }

    [Fact]
    public async Task CreatePlaylistAsync_TimeoutResponseHasUnknownOutcome()
    {
        var client = new FakeClient
        {
            CreationException = new Google.GoogleApiException("youtube", "timeout")
            {
                HttpStatusCode = System.Net.HttpStatusCode.RequestTimeout
            }
        };

        var failure = await Assert.ThrowsAsync<PlatformApiException>(() =>
            new YouTubePlaylistWriter(client).CreatePlaylistAsync(
                Account, "Road songs", CancellationToken.None));

        Assert.Equal("youtube_playlist_creation_unknown", failure.Code);
        Assert.Equal(1, client.CreateCount);
    }

    [Fact]
    public async Task CreatePlaylistAsync_ExplicitProviderRejectionIsNotAnUnknownOutcome()
    {
        var client = new FakeClient
        {
            CreationException = new Google.GoogleApiException("youtube", "forbidden")
            {
                HttpStatusCode = System.Net.HttpStatusCode.Forbidden
            }
        };

        var failure = await Assert.ThrowsAsync<PlatformApiException>(() =>
            new YouTubePlaylistWriter(client).CreatePlaylistAsync(
                Account, "Road songs", CancellationToken.None));

        Assert.Equal("youtube_playlist_creation_rejected", failure.Code);
        Assert.Equal(403, failure.StatusCode);
        Assert.False(MusicSyncFailureClassifier.Classify(failure).Retryable);
        Assert.Equal(1, client.CreateCount);
    }

    private sealed class FakeClient(params string?[] videoIds) : IYouTubePlaylistClient
    {
        private readonly List<YouTubeWritablePlaylistItem> _items = videoIds.Select((id, index) =>
            new YouTubeWritablePlaylistItem($"item-{index}", id, index, id is not null)).ToList();
        private int _nextId = videoIds.Length;

        public bool IsOwned { get; set; } = true;
        public Exception? ValidationException { get; set; }
        public Exception? CreationException { get; set; }
        public Exception? CreationValidationException { get; set; }
        public bool FailAfterFirstWrite { get; set; }
        public bool FailCreationAfterApplying { get; set; }
        public int CreateCount { get; private set; }
        public int CreationValidationCount { get; private set; }
        public List<string> CreatedNames { get; } = [];
        public int ValidateCount { get; private set; }
        public int ReadCount { get; private set; }
        public int DeleteCount { get; private set; }
        public int InsertCount { get; private set; }
        public int MoveCount { get; private set; }
        public int WriteCount => DeleteCount + InsertCount + MoveCount;
        public string?[] VideoIds => _items.Select(item => item.VideoId).ToArray();
        public string[] ProtectedItemIds => _items.Where(item => !item.IsAvailable).Select(item => item.Id).ToArray();
        public List<string> InsertedVideoIds { get; } = [];
        public List<string> MovedItemIds { get; } = [];
        public List<string> DeletedItemIds { get; } = [];

        public void SetUnavailable(string videoId)
        {
            for (var index = 0; index < _items.Count; index++)
                if (_items[index].VideoId == videoId)
                    _items[index] = _items[index] with { IsAvailable = false };
        }

        public Task ValidatePlaylistCreationAsync(
            PlatformAccountContext account, CancellationToken cancellationToken)
        {
            CreationValidationCount++;
            if (CreationValidationException is { } exception) throw exception;
            if (!IsOwned || account != Account)
                throw new InvalidOperationException("The selected channel changed.");
            return Task.CompletedTask;
        }

        public Task<string> CreatePrivatePlaylistAsync(
            PlatformAccountContext account, string name, CancellationToken cancellationToken)
        {
            CreateCount++;
            CreatedNames.Add(name);
            if (CreationException is { } exception) throw exception;
            if (FailCreationAfterApplying)
                throw new HttpRequestException("Response lost after playlist creation.");
            return Task.FromResult($"new-playlist-{CreateCount}");
        }

        public Task ValidateWritablePlaylistAsync(
            PlatformAccountContext account, string playlistId, CancellationToken cancellationToken)
        {
            ValidateCount++;
            if (ValidationException is { } exception) throw exception;
            if (!IsOwned || account != Account || playlistId != "owned")
                throw new InvalidOperationException("The selected account does not own the playlist.");
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<YouTubeWritablePlaylistItem>> GetRawPlaylistItemsAsync(
            PlatformAccountContext account, string playlistId, CancellationToken cancellationToken)
        {
            ReadCount++;
            return Task.FromResult<IReadOnlyList<YouTubeWritablePlaylistItem>>(_items.ToArray());
        }

        public Task<string> InsertPlaylistItemAsync(
            PlatformAccountContext account, string playlistId, string videoId, long position,
            CancellationToken cancellationToken)
        {
            var id = $"item-{_nextId++}";
            _items.Insert((int)position, new YouTubeWritablePlaylistItem(id, videoId, position));
            InsertedVideoIds.Add(videoId);
            InsertCount++;
            MaybeFail();
            return Task.FromResult(id);
        }

        public Task MovePlaylistItemAsync(
            PlatformAccountContext account, string playlistId, YouTubeWritablePlaylistItem item,
            long position, CancellationToken cancellationToken)
        {
            var currentIndex = _items.FindIndex(candidate => candidate.Id == item.Id);
            var current = _items[currentIndex];
            _items.RemoveAt(currentIndex);
            _items.Insert((int)position, current);
            MovedItemIds.Add(item.Id);
            MoveCount++;
            MaybeFail();
            return Task.CompletedTask;
        }

        public Task DeletePlaylistItemAsync(
            PlatformAccountContext account, string itemId, CancellationToken cancellationToken)
        {
            _items.RemoveAll(item => item.Id == itemId);
            DeletedItemIds.Add(itemId);
            DeleteCount++;
            MaybeFail();
            return Task.CompletedTask;
        }

        private void MaybeFail()
        {
            if (!FailAfterFirstWrite || WriteCount != 1) return;
            FailAfterFirstWrite = false;
            throw new HttpRequestException("Provider applied the write but the response was lost.");
        }
    }
}
