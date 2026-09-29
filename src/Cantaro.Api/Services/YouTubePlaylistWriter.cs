namespace Cantaro.Api.Services;

internal sealed record YouTubeWritablePlaylistItem(
    string Id, string? VideoId, long? Position, bool IsAvailable = true);

internal interface IYouTubePlaylistClient
{
    Task ValidatePlaylistCreationAsync(PlatformAccountContext account, CancellationToken cancellationToken);
    Task<string> CreatePrivatePlaylistAsync(PlatformAccountContext account, string name, CancellationToken cancellationToken);
    Task ValidateWritablePlaylistAsync(PlatformAccountContext account, string playlistId, CancellationToken cancellationToken);
    Task<IReadOnlyList<YouTubeWritablePlaylistItem>> GetRawPlaylistItemsAsync(
        PlatformAccountContext account, string playlistId, CancellationToken cancellationToken);
    Task<string> InsertPlaylistItemAsync(
        PlatformAccountContext account, string playlistId, string videoId, long position, CancellationToken cancellationToken);
    Task MovePlaylistItemAsync(
        PlatformAccountContext account, string playlistId, YouTubeWritablePlaylistItem item,
        long position, CancellationToken cancellationToken);
    Task DeletePlaylistItemAsync(PlatformAccountContext account, string itemId, CancellationToken cancellationToken);
}

public sealed class YouTubePlaylistWriter : IPlaylistWriter
{
    private readonly IYouTubePlaylistClient _client;

    public YouTubePlaylistWriter(YouTubeService youtubeService) : this((IYouTubePlaylistClient)youtubeService) { }

    internal YouTubePlaylistWriter(IYouTubePlaylistClient client) => _client = client;

    public string PlatformId => "youtube";

    public async Task ValidateCreationAsync(PlatformAccountContext account, CancellationToken cancellationToken)
    {
        try
        {
            await _client.ValidatePlaylistCreationAsync(account, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            throw new PlatformApiException("youtube_creation_unavailable", ex.Message, 409, innerException: ex);
        }
        catch (Google.GoogleApiException ex)
        {
            throw ClassifyProviderFailure(ex);
        }
        catch (HttpRequestException ex)
        {
            throw ProviderUnavailable(ex);
        }
        catch (Google.Apis.Auth.OAuth2.Responses.TokenResponseException ex)
        {
            throw new PlatformReconnectRequiredException(
                "The YouTube connection expired. Reconnect YouTube before creating a playlist.", ex);
        }
    }

    public async Task<string> CreatePlaylistAsync(
        PlatformAccountContext account, string name, CancellationToken cancellationToken)
    {
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            await ValidateCreationAsync(account, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (ArgumentException ex)
        {
            throw new PlatformApiException("youtube_playlist_creation_rejected",
                "The Cantaro playlist needs a name before YouTube can create it.",
                400, innerException: ex);
        }
        catch (PlatformApiException ex)
        {
            // No create request has been sent, so the pending reservation can be released.
            throw new PlatformApiException("youtube_playlist_creation_rejected",
                ex.Message, ex.StatusCode, ex.RetryAfter, ex);
        }
        catch (OperationCanceledException ex)
        {
            // Cancellation during preflight cannot have created a playlist.
            throw new PlatformApiException("youtube_playlist_creation_rejected",
                "YouTube playlist creation was cancelled before the request was sent.",
                409, innerException: ex);
        }
        try
        {
            var id = await _client.CreatePrivatePlaylistAsync(account, name, cancellationToken);
            return !string.IsNullOrWhiteSpace(id) ? id : throw CreationOutcomeUnknown();
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw CreationOutcomeUnknown(ex);
        }
        catch (HttpRequestException ex)
        {
            throw CreationOutcomeUnknown(ex);
        }
        catch (Google.GoogleApiException ex) when ((int)ex.HttpStatusCode >= 500
            || (int)ex.HttpStatusCode < 400 || (int)ex.HttpStatusCode == 408)
        {
            throw CreationOutcomeUnknown(ex);
        }
        catch (Google.GoogleApiException ex)
        {
            throw new PlatformApiException("youtube_playlist_creation_rejected",
                "YouTube rejected the new playlist. Reconnect the account or check its playlist permissions.",
                (int)ex.HttpStatusCode, innerException: ex);
        }
        catch (InvalidOperationException ex)
        {
            // A successful POST without an ID is still an ambiguous create result.
            throw CreationOutcomeUnknown(ex);
        }
    }

    public async Task ValidateDestinationAsync(
        PlatformAccountContext account, string playlistId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playlistId);
        try
        {
            await _client.ValidateWritablePlaylistAsync(account, playlistId, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            throw new PlatformApiException("destination_unavailable", ex.Message, 409,
                innerException: ex);
        }
        catch (Google.GoogleApiException ex)
        {
            throw ClassifyProviderFailure(ex);
        }
        catch (HttpRequestException ex)
        {
            throw ProviderUnavailable(ex);
        }
    }

    public async Task ReconcileAsync(
        PlatformAccountContext account, string playlistId, IReadOnlyList<string> trackIds,
        CancellationToken cancellationToken)
    {
        try
        {
            await ReconcileCoreAsync(account, playlistId, trackIds, cancellationToken);
        }
        catch (Google.GoogleApiException ex)
        {
            throw ClassifyProviderFailure(ex);
        }
        catch (HttpRequestException ex)
        {
            throw ProviderUnavailable(ex);
        }
    }

    private async Task ReconcileCoreAsync(
        PlatformAccountContext account, string playlistId, IReadOnlyList<string> trackIds,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playlistId);
        ArgumentNullException.ThrowIfNull(trackIds);
        if (trackIds.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("YouTube video IDs cannot be empty.", nameof(trackIds));

        // Validate ownership and scope before reading or changing any playlist item.
        await ValidateDestinationAsync(account, playlistId, cancellationToken);
        var current = (await _client.GetRawPlaylistItemsAsync(account, playlistId, cancellationToken)).ToList();
        var protectedItemIds = current.Where(item => !item.IsAvailable).Select(item => item.Id).ToArray();
        var protectedVideoIds = current.Where(item => !item.IsAvailable && item.VideoId is not null)
            .Select(item => item.VideoId!).ToHashSet(StringComparer.Ordinal);
        var desiredVideoIds = trackIds.Where(id => !protectedVideoIds.Contains(id)).ToArray();

        // Keep private, deleted, or otherwise unreadable entries in place. Reconcile the
        // remaining playlist items against the desired readable sequence.
        var remaining = desiredVideoIds.GroupBy(id => id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        for (var index = current.Count - 1; index >= 0; index--)
        {
            var item = current[index];
            if (!item.IsAvailable)
                continue;
            if (item.VideoId is { } videoId
                && remaining.TryGetValue(videoId, out var count)
                && count > 0)
            {
                remaining[videoId] = count - 1;
                continue;
            }

            await _client.DeletePlaylistItemAsync(account, item.Id, cancellationToken);
            current.RemoveAt(index);
        }

        // Reconcile the readable sequence while leaving every unavailable playlist item
        // untouched. A readable item moves to the physical position of the corresponding
        // readable slot, so unavailable items can shift as a side effect without a direct
        // update or delete request for them.
        for (var readableIndex = 0; readableIndex < desiredVideoIds.Length; readableIndex++)
        {
            var readableItems = current.Where(item => item.IsAvailable).ToArray();
            var physicalPosition = readableIndex < readableItems.Length
                ? current.FindIndex(item => item.Id == readableItems[readableIndex].Id)
                : current.Count;

            if (readableIndex < readableItems.Length
                && readableItems[readableIndex].VideoId == desiredVideoIds[readableIndex])
                continue;

            var itemToMove = readableItems.Skip(readableIndex)
                .FirstOrDefault(item => item.VideoId == desiredVideoIds[readableIndex]);
            if (itemToMove is not null)
            {
                await _client.MovePlaylistItemAsync(
                    account, playlistId, itemToMove, physicalPosition, cancellationToken);
                var existingIndex = current.FindIndex(item => item.Id == itemToMove.Id);
                current.RemoveAt(existingIndex);
                current.Insert(physicalPosition, itemToMove);
            }
            else
            {
                var id = await _client.InsertPlaylistItemAsync(
                    account, playlistId, desiredVideoIds[readableIndex], physicalPosition, cancellationToken);
                current.Insert(physicalPosition, new YouTubeWritablePlaylistItem(
                    id, desiredVideoIds[readableIndex], physicalPosition));
            }
        }

        var expectedProtectedIds = protectedItemIds;
        var expectedReadableIds = desiredVideoIds;
        // Verify both readable order and the presence/order of protected entries.
        if (!current.Where(item => item.IsAvailable).Select(item => item.VideoId)
                .SequenceEqual(expectedReadableIds, StringComparer.Ordinal)
            || !current.Where(item => !item.IsAvailable).Select(item => item.Id)
                .SequenceEqual(expectedProtectedIds, StringComparer.Ordinal))
            throw new InvalidOperationException("The YouTube playlist could not be reconciled.");
        var actual = await _client.GetRawPlaylistItemsAsync(account, playlistId, cancellationToken);
        if (!actual.Where(item => item.IsAvailable).Select(item => item.VideoId)
                .SequenceEqual(expectedReadableIds, StringComparer.Ordinal)
            || !actual.Where(item => !item.IsAvailable).Select(item => item.Id)
                .SequenceEqual(expectedProtectedIds, StringComparer.Ordinal))
            throw new PlatformApiException("youtube_order_changed",
                "YouTube did not retain the requested playlist order and unavailable entries. Retry synchronization.", 503);
    }

    private static PlatformApiException ClassifyProviderFailure(Google.GoogleApiException ex)
    {
        var status = (int)ex.HttpStatusCode;
        var quotaExceeded = ex.Error?.Errors?.Any(error => error.Reason is
            "quotaExceeded" or "dailyLimitExceeded" or "userRateLimitExceeded" or "rateLimitExceeded") == true
            || ex.Message.Contains("quotaExceeded", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("dailyLimitExceeded", StringComparison.OrdinalIgnoreCase);
        if (status == 429 || quotaExceeded)
            return new PlatformApiException("youtube_rate_limited",
                "YouTube's playlist API quota is exhausted. Retry synchronization later.", 429,
                innerException: ex);
        if (status >= 500)
            return ProviderUnavailable(ex);
        if (ex.Message.Contains("manualSortRequired", StringComparison.OrdinalIgnoreCase))
            return new PlatformApiException("youtube_manual_sort_required",
                "Set the YouTube playlist order to Manual, then retry synchronization.", 422,
                innerException: ex);
        if (status is 401 or 403 or 404)
            return new PlatformApiException("destination_unavailable",
                "Reconnect YouTube and confirm that the selected playlist is still yours.", 409,
                innerException: ex);
        return new PlatformApiException("youtube_playlist_rejected",
            "YouTube rejected the playlist update. Check that the videos can be added to this playlist.", 422,
            innerException: ex);
    }

    private static PlatformApiException ProviderUnavailable(Exception ex) => new(
        "youtube_unavailable", "YouTube is unavailable. Retry synchronization later.", 503,
        innerException: ex);

    private static PlatformApiException CreationOutcomeUnknown(Exception? ex = null) => new(
        "youtube_playlist_creation_unknown",
        "YouTube may have created the playlist, but Cantaro did not receive its ID. Check YouTube before trying again.",
        409, innerException: ex);
}
