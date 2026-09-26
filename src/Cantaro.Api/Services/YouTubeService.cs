using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Xml;

namespace Cantaro.Api.Services;

public sealed record YouTubePlaylistRemovalResult(IReadOnlyList<long?> Positions)
{
    public bool Changed => Positions.Count > 0;
}

public sealed class YouTubePlaylistReconciliationException(string message, Exception innerException)
    : Exception(message, innerException);

internal sealed record YouTubePlaylistItemMappingResult(
    YouTubePlaylistItemDto? Item,
    string? SkipReason,
    long? ProviderPosition);

/// <summary>
/// DTO for YouTube playlist information
/// </summary>
public class YouTubePlaylistDto
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public int ItemCount { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}

/// <summary>
/// DTO for YouTube playlist item/video
/// </summary>
public class YouTubePlaylistItemDto
{
    public required string VideoId { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? ChannelTitle { get; set; }
    public int Position { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public int? DurationSeconds { get; set; }
}

public sealed class YouTubeVideoMetadataDto
{
    public required string VideoId { get; init; }
    public required string Title { get; init; }
    public string? ChannelTitle { get; init; }
    public string? Description { get; init; }
    public string? ThumbnailUrl { get; init; }
    public string? CategoryId { get; init; }
    public bool LicensedContent { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<string> TopicCategories { get; init; } = [];
    public int? DurationSeconds { get; init; }
}

/// <summary>
/// Service for YouTube OAuth and API operations
/// </summary>
public class YouTubeService : IYouTubePlaylistClient
{
    public static string ResolveRedirectUri(CallbackUrlCandidates callbackUrls)
    {
        var preferred = new Uri(callbackUrls.Preferred);
        if (!preferred.Host.EndsWith(".dev.localhost", StringComparison.OrdinalIgnoreCase))
        {
            return callbackUrls.Preferred;
        }

        // Google accepts localhost redirect hosts but rejects the Aspire-only
        // development subdomain. Keep the same scheme, port, and callback path.
        return new UriBuilder(preferred) { Host = "localhost" }.Uri.AbsoluteUri;
    }

    private readonly IConfiguration _configuration;
    private readonly ApplicationDbContext _dbContext;
    private readonly TokenEncryptionService _tokenEncryption;
    private readonly ILogger<YouTubeService> _logger;

    private const string ServiceName = "youtube";

    public YouTubeService(
        IConfiguration configuration,
        ApplicationDbContext dbContext,
        TokenEncryptionService tokenEncryption,
        ILogger<YouTubeService> logger)
    {
        _configuration = configuration;
        _dbContext = dbContext;
        _tokenEncryption = tokenEncryption;
        _logger = logger;
    }

    private static DateTimeOffset? ParsePublishedAtRaw(string? publishedAtRaw)
    {
        if (string.IsNullOrWhiteSpace(publishedAtRaw))
            return null;

        // Try a forgiving parse; assume UTC when no offset is present and adjust to UTC
        if (DateTimeOffset.TryParse(publishedAtRaw, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
        {
            return dto;
        }

        // If the API returns an unexpected format, return null rather than throw
        return null;
    }

    public async Task<YouTubeVideoMetadataDto?> GetVideoMetadataAsync(int userId, string videoId, CancellationToken cancellationToken)
    {
        var account = await GetConnectedAccountAsync(userId);
        if (account is null) return null;
        VideoListResponse response;
        try
        {
            using var youtubeService = await CreateYouTubeServiceAsync(account, cancellationToken);
            var request = youtubeService.Videos.List("snippet,contentDetails,topicDetails");
            request.Id = videoId;
            request.MaxResults = 1;
            response = await request.ExecuteAsync(cancellationToken);
        }
        catch (Google.Apis.Auth.OAuth2.Responses.TokenResponseException ex)
        {
            _logger.LogWarning(ex, "YouTube metadata is unavailable because the account for user {UserId} needs reconnecting", userId);
            return null;
        }
        var video = response.Items?.FirstOrDefault();
        if (video is null) return null;

        int? durationSeconds = null;
        if (!string.IsNullOrWhiteSpace(video.ContentDetails?.Duration))
        {
            try { durationSeconds = (int)Math.Round(XmlConvert.ToTimeSpan(video.ContentDetails.Duration).TotalSeconds); }
            catch (FormatException) { }
        }

        return new YouTubeVideoMetadataDto
        {
            VideoId = video.Id,
            Title = video.Snippet?.Title ?? videoId,
            ChannelTitle = video.Snippet?.ChannelTitle,
            Description = video.Snippet?.Description,
            ThumbnailUrl = video.Snippet?.Thumbnails?.High?.Url ?? video.Snippet?.Thumbnails?.Default__?.Url,
            CategoryId = video.Snippet?.CategoryId,
            LicensedContent = video.ContentDetails?.LicensedContent ?? false,
            Tags = video.Snippet?.Tags?.ToArray() ?? [],
            TopicCategories = video.TopicDetails?.TopicCategories?.ToArray() ?? [],
            DurationSeconds = durationSeconds
        };
    }

    /// <summary>
    /// Gets the Google OAuth authorization URL for YouTube
    /// </summary>
    public string GetAuthorizationUrl(string redirectUri, string state)
    {
        var clientId = _configuration["YouTube:ClientId"]
            ?? throw new InvalidOperationException("YouTube:ClientId is not configured");

        var scopes = new[]
        {
            "https://www.googleapis.com/auth/youtube.force-ssl",
            "openid",
            "email",
            "profile"
        };

        var url = $"https://accounts.google.com/o/oauth2/v2/auth?" +
            $"client_id={Uri.EscapeDataString(clientId)}&" +
            $"redirect_uri={Uri.EscapeDataString(redirectUri)}&" +
            $"response_type=code&" +
            $"scope={Uri.EscapeDataString(string.Join(" ", scopes))}&" +
            $"access_type=offline&" +
            $"prompt=consent&" +
            $"state={Uri.EscapeDataString(state)}";

        return url;
    }

    public async Task<bool> AddVideoToPlaylistAsync(int userId, string playlistId, string videoId, CancellationToken cancellationToken)
        => await AddVideoToPlaylistAsync(userId, playlistId, videoId, position: null, cancellationToken);

    public async Task<bool> AddVideoToPlaylistAsync(
        int userId,
        string playlistId,
        string videoId,
        long? position,
        CancellationToken cancellationToken)
    {
        var account = await GetConnectedAccountAsync(userId) ?? throw new InvalidOperationException("YouTube account not connected");
        if (!HasPlaylistWriteScope(account.Scopes))
            throw new InvalidOperationException("Reconnect YouTube to allow playlist edits.");
        using var service = await CreateYouTubeServiceAsync(account, cancellationToken);
        var existing = service.PlaylistItems.List("id");
        existing.PlaylistId = playlistId;
        existing.VideoId = videoId;
        existing.MaxResults = 1;
        if ((await existing.ExecuteAsync(cancellationToken)).Items?.Count > 0)
            return false;
        await InsertVideoIntoPlaylistAsync(service, playlistId, videoId, position, cancellationToken);
        return true;
    }

    public async Task RestoreVideoToPlaylistAsync(
        int userId,
        string playlistId,
        string videoId,
        long? position,
        CancellationToken cancellationToken)
    {
        var account = await GetConnectedAccountAsync(userId) ?? throw new InvalidOperationException("YouTube account not connected");
        if (!HasPlaylistWriteScope(account.Scopes))
            throw new InvalidOperationException("Reconnect YouTube to allow playlist edits.");
        using var service = await CreateYouTubeServiceAsync(account, cancellationToken);
        await InsertVideoIntoPlaylistAsync(service, playlistId, videoId, position, cancellationToken);
    }

    private static async Task InsertVideoIntoPlaylistAsync(
        Google.Apis.YouTube.v3.YouTubeService service,
        string playlistId,
        string videoId,
        long? position,
        CancellationToken cancellationToken)
    {
        var item = new PlaylistItem
        {
            Snippet = new PlaylistItemSnippet
            {
                PlaylistId = playlistId,
                Position = position,
                ResourceId = new ResourceId { Kind = "youtube#video", VideoId = videoId }
            }
        };
        await service.PlaylistItems.Insert(item, "snippet").ExecuteAsync(cancellationToken);
    }

    public async Task<YouTubePlaylistRemovalResult> RemoveVideoFromPlaylistAsync(int userId, string playlistId, string videoId, CancellationToken cancellationToken)
    {
        var account = await GetConnectedAccountAsync(userId) ?? throw new InvalidOperationException("YouTube account not connected");
        if (!HasPlaylistWriteScope(account.Scopes))
            throw new InvalidOperationException("Reconnect YouTube to allow playlist edits.");
        using var service = await CreateYouTubeServiceAsync(account, cancellationToken);
        var items = new List<PlaylistItem>();
        string? pageToken = null;
        do
        {
            var list = service.PlaylistItems.List("id,snippet,contentDetails");
            list.PlaylistId = playlistId;
            list.VideoId = videoId;
            list.MaxResults = 50;
            list.PageToken = pageToken;
            var response = await list.ExecuteAsync(cancellationToken);
            if (response.Items is not null) items.AddRange(response.Items);
            pageToken = response.NextPageToken;
        }
        while (!string.IsNullOrWhiteSpace(pageToken));

        if (items.Count == 0) return new YouTubePlaylistRemovalResult([]);
        var deletedItems = new List<PlaylistItem>();
        try
        {
            foreach (var item in items)
            {
                await service.PlaylistItems.Delete(item.Id).ExecuteAsync(cancellationToken);
                deletedItems.Add(item);
            }
        }
        catch (Exception deleteException)
        {
            try
            {
                foreach (var item in deletedItems.OrderBy(item => item.Snippet?.Position))
                    await InsertVideoIntoPlaylistAsync(service, playlistId, videoId, item.Snippet?.Position, CancellationToken.None);
            }
            catch (Exception restoreException)
            {
                throw new YouTubePlaylistReconciliationException(
                    "YouTube changed and the partial playlist update could not be rolled back.",
                    new AggregateException(deleteException, restoreException));
            }

            throw;
        }

        return new YouTubePlaylistRemovalResult(deletedItems.Select(item => item.Snippet?.Position).Order().ToList());
    }

    private static bool HasPlaylistWriteScope(string? scopes) => (scopes ?? string.Empty)
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Any(scope => scope is "https://www.googleapis.com/auth/youtube" or "https://www.googleapis.com/auth/youtube.force-ssl");

    internal sealed record SyncReadItem(string ExternalId, string PlaylistItemId, string Title, string? Artist,
        string? ThumbnailUrl, int? DurationSeconds, int Position, bool IsAvailable,
        string? Description = null);
    internal sealed record SyncReadSnapshot(string Id, string Name, string? Revision,
        IReadOnlyList<SyncReadItem> Items, bool IsComplete, int UnavailableItemCount);

    internal async Task ValidateSyncAccountAsync(
        PlatformAccountContext accountContext, CancellationToken cancellationToken)
        => _ = await RequireAccountAsync(accountContext, cancellationToken);

    internal async Task<SyncReadSnapshot> GetSyncReadSnapshotAsync(
        PlatformAccountContext accountContext, string playlistId, CancellationToken cancellationToken)
    {
        var account = await RequireAccountAsync(accountContext, cancellationToken);
        using var service = await CreateYouTubeServiceAsync(account, cancellationToken);
        var metadataRequest = service.Playlists.List("snippet,contentDetails");
        metadataRequest.Id = playlistId;
        var before = (await metadataRequest.ExecuteAsync(cancellationToken)).Items?.SingleOrDefault();
        if (before?.Id != playlistId || string.IsNullOrWhiteSpace(before.Snippet?.Title))
            throw new PlatformApiException("youtube_playlist_unavailable",
                "YouTube did not return complete playlist details.", 404);

        var items = new List<SyncReadItem>();
        var seenTokens = new HashSet<string>(StringComparer.Ordinal);
        string? pageToken = null;
        var complete = true;
        var pageCount = 0;
        var unavailableItemCount = 0;
        do
        {
            if (++pageCount > 120)
                throw new PlatformApiException("youtube_playlist_incomplete_response",
                    "YouTube playlist has too many pages to read completely.", 502);
            var request = service.PlaylistItems.List("id,snippet,contentDetails,status");
            request.PlaylistId = playlistId;
            request.MaxResults = 50;
            request.PageToken = pageToken;
            var response = await request.ExecuteAsync(cancellationToken);
            if (response.Items is null)
                throw new PlatformApiException("youtube_playlist_incomplete_response",
                    "YouTube returned an incomplete playlist page.", 502);
            var videoIds = response.Items.Select(item => item?.ContentDetails?.VideoId
                    ?? item?.Snippet?.ResourceId?.VideoId)
                .Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id!)
                .Distinct(StringComparer.Ordinal).ToArray();
            var videosById = new Dictionary<string, Video>(StringComparer.Ordinal);
            if (videoIds.Length > 0)
            {
                var videosRequest = service.Videos.List("snippet,contentDetails,status");
                videosRequest.Id = string.Join(',', videoIds);
                videosRequest.MaxResults = videoIds.Length;
                var videos = await videosRequest.ExecuteAsync(cancellationToken);
                foreach (var video in videos.Items ?? [])
                    if (!string.IsNullOrWhiteSpace(video.Id)) videosById[video.Id] = video;
            }
            foreach (var item in response.Items)
            {
                var position = items.Count;
                var videoId = item?.ContentDetails?.VideoId ?? item?.Snippet?.ResourceId?.VideoId;
                var foundVideo = videosById.TryGetValue(videoId ?? string.Empty, out var video);
                var unavailable = IsPlaylistItemUnavailable(foundVideo, video?.Status?.PrivacyStatus,
                    item?.Status?.PrivacyStatus);
                unavailable |= string.IsNullOrWhiteSpace(videoId);
                if (unavailable) unavailableItemCount++;
                var available = !unavailable;
                var playlistItemId = item?.Id;
                var structurallyReadable = IsPlaylistItemStructurallyReadable(item?.Snippet?.Position,
                    position, playlistItemId, item?.Snippet?.Title);
                if (!structurallyReadable) complete = false;
                items.Add(new SyncReadItem(videoId ?? $"!unavailable:{playlistItemId ?? position.ToString(CultureInfo.InvariantCulture)}",
                    playlistItemId ?? $"!missing-playlist-item:{position.ToString(CultureInfo.InvariantCulture)}",
                    item?.Snippet?.Title ?? "Unavailable YouTube item",
                    item?.Snippet?.VideoOwnerChannelTitle,
                    item?.Snippet?.Thumbnails?.Medium?.Url ?? item?.Snippet?.Thumbnails?.Default__?.Url,
                    available ? ParseVideoDuration(video!.ContentDetails?.Duration) : null,
                    position, available, available ? video!.Snippet?.Description : null));
            }
            pageToken = response.NextPageToken;
            if (!string.IsNullOrWhiteSpace(pageToken) && !seenTokens.Add(pageToken))
                throw new PlatformApiException("youtube_playlist_incomplete_response",
                    "YouTube repeated a playlist page.", 502);
        } while (!string.IsNullOrWhiteSpace(pageToken));

        if (before.ContentDetails?.ItemCount != items.Count) complete = false;
        var afterRequest = service.Playlists.List("snippet,contentDetails");
        afterRequest.Id = playlistId;
        var after = (await afterRequest.ExecuteAsync(cancellationToken)).Items?.SingleOrDefault();
        if (after?.Id != playlistId || after.ETag != before.ETag
            || after.ContentDetails?.ItemCount != before.ContentDetails?.ItemCount)
            complete = false;
        return new SyncReadSnapshot(playlistId, before.Snippet.Title, before.ETag, items, complete,
            unavailableItemCount);
    }

    internal static bool IsPlaylistItemUnavailable(
        bool videoMetadataFound, string? videoPrivacyStatus, string? playlistItemPrivacyStatus)
        => !videoMetadataFound
            || videoPrivacyStatus is not ("public" or "unlisted")
            || string.Equals(playlistItemPrivacyStatus, "private", StringComparison.OrdinalIgnoreCase);

    internal static bool IsPlaylistItemStructurallyReadable(
        long? providerPosition, int expectedPosition, string? playlistItemId, string? title)
        => providerPosition == expectedPosition
            && !string.IsNullOrWhiteSpace(playlistItemId)
            && !string.IsNullOrWhiteSpace(title);

    internal async Task<IReadOnlyList<TrackMatchSearchCandidate>> SearchVideoCandidatesAsync(
        PlatformAccountContext accountContext, TrackObservation evidence, CancellationToken cancellationToken)
    {
        var account = await RequireAccountAsync(accountContext, cancellationToken);
        using var service = await CreateYouTubeServiceAsync(account, cancellationToken);
        var request = service.Search.List("snippet");
        var parsedEvidence = TrackObservationParser.Parse(evidence);
        var searchTitle = parsedEvidence.SearchTitle ?? evidence.Title;
        var searchArtist = parsedEvidence.SearchArtist ?? evidence.Artist;
        request.Q = string.Join(" ", new[] { searchTitle, searchArtist }.Where(part => !string.IsNullOrWhiteSpace(part)));
        request.Type = "video";
        request.MaxResults = 10;
        var result = await request.ExecuteAsync(cancellationToken);
        var ids = result.Items?.Select(item => item.Id?.VideoId)
            .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray() ?? [];
        if (ids.Length == 0 && !string.IsNullOrWhiteSpace(searchArtist))
        {
            request.Q = searchTitle;
            result = await request.ExecuteAsync(cancellationToken);
            ids = result.Items?.Select(item => item.Id?.VideoId)
                .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray() ?? [];
        }
        if (ids.Length == 0) return [];
        var videosRequest = service.Videos.List("snippet,contentDetails,status");
        videosRequest.Id = string.Join(',', ids);
        videosRequest.MaxResults = ids.Length;
        var videos = await videosRequest.ExecuteAsync(cancellationToken);
        return videos.Items?.Where(video => video.Id is { Length: > 0 }
                && video.Snippet?.Title is { Length: > 0 }
                && video.Status?.PrivacyStatus == "public")
            .Select(video =>
            {
                var parsed = TrackMetadataParser.Parse(video.Snippet.Title, video.Snippet.ChannelTitle);
                return new TrackMatchSearchCandidate
                {
                    CandidateSource = "youtube", ExternalId = video.Id,
                    Title = parsed.DisplayTitle,
                    Artist = parsed.DisplayArtist ?? video.Snippet.ChannelTitle,
                    ArtistCredits = parsed.ArtistCredits,
                    DurationSeconds = ParseVideoDuration(video.ContentDetails?.Duration),
                    RawMetadata = System.Text.Json.JsonSerializer.Serialize(new TrackObservationMetadata
                    {
                        SourceType = "youtube", ExternalId = video.Id,
                        Title = parsed.DisplayTitle,
                        Artist = parsed.DisplayArtist ?? video.Snippet.ChannelTitle,
                        OriginalTitle = video.Snippet.Title,
                        OriginalArtist = video.Snippet.ChannelTitle,
                        ChannelTitle = video.Snippet.ChannelTitle,
                        Description = video.Snippet.Description,
                        DurationSeconds = ParseVideoDuration(video.ContentDetails?.Duration)
                    })
                };
            }).ToArray() ?? [];
    }

    private static int? ParseVideoDuration(string? duration)
    {
        if (string.IsNullOrWhiteSpace(duration)) return null;
        try { return (int)Math.Round(XmlConvert.ToTimeSpan(duration).TotalSeconds); }
        catch (FormatException) { return null; }
    }

    internal async Task RenamePlaylistForSyncAsync(
        PlatformAccountContext accountContext, string playlistId, string name, CancellationToken cancellationToken)
    {
        var account = await RequireWritableAccountAsync(accountContext, cancellationToken);
        using var service = await CreateYouTubeServiceAsync(account, cancellationToken, disableAutomaticRetries: true);
        var read = service.Playlists.List("snippet,status");
        read.Id = playlistId;
        var playlist = (await read.ExecuteAsync(cancellationToken)).Items?.SingleOrDefault();
        if (playlist?.Snippet?.ChannelId != account.ExternalAccountId)
            throw new PlatformApiException("youtube_playlist_not_owned", "Only the playlist owner can rename it.", 403);
        playlist.Snippet.Title = name;
        await service.Playlists.Update(playlist, "snippet").ExecuteAsync(cancellationToken);
    }

    internal async Task DeletePlaylistForSyncAsync(
        PlatformAccountContext accountContext, string playlistId, CancellationToken cancellationToken)
    {
        var account = await RequireWritableAccountAsync(accountContext, cancellationToken);
        using var service = await CreateYouTubeServiceAsync(account, cancellationToken, disableAutomaticRetries: true);
        var read = service.Playlists.List("snippet");
        read.Id = playlistId;
        var playlist = (await read.ExecuteAsync(cancellationToken)).Items?.SingleOrDefault();
        if (playlist?.Snippet?.ChannelId != account.ExternalAccountId)
            throw new PlatformApiException("youtube_playlist_not_owned", "Only the playlist owner can delete it.", 403);
        await service.Playlists.Delete(playlistId).ExecuteAsync(cancellationToken);
    }

    private async Task<ConnectedServiceAccount> RequireWritableAccountAsync(
        PlatformAccountContext accountContext, CancellationToken cancellationToken)
    {
        var account = await RequireAccountAsync(accountContext, cancellationToken);
        if (account.ConnectionState != "connected" || string.IsNullOrWhiteSpace(account.EncryptedRefreshToken))
            throw new InvalidOperationException("Reconnect the selected YouTube account before editing playlists.");
        if (!HasPlaylistWriteScope(account.Scopes))
            throw new InvalidOperationException("Reconnect YouTube to allow playlist edits.");
        return account;
    }

    async Task IYouTubePlaylistClient.ValidatePlaylistCreationAsync(
        PlatformAccountContext accountContext, CancellationToken cancellationToken)
    {
        var account = await RequireWritableAccountAsync(accountContext, cancellationToken);
        using var service = await CreateYouTubeServiceAsync(account, cancellationToken);
        var request = service.Channels.List("id");
        request.Mine = true;
        request.MaxResults = 50;
        var response = await request.ExecuteAsync(cancellationToken);
        if (response.Items?.Any(channel => channel.Id == account.ExternalAccountId) != true)
            throw new InvalidOperationException("The selected YouTube channel changed. Reconnect YouTube before creating a playlist.");
    }

    async Task<string> IYouTubePlaylistClient.CreatePrivatePlaylistAsync(
        PlatformAccountContext accountContext, string name, CancellationToken cancellationToken)
    {
        Google.Apis.YouTube.v3.YouTubeService service;
        try
        {
            var account = await RequireWritableAccountAsync(accountContext, cancellationToken);
            service = await CreateYouTubeServiceAsync(account, cancellationToken, disableAutomaticRetries: true);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception ex)
        {
            // ExecuteAsync has not started, so creation definitely did not happen.
            throw new PlatformApiException("youtube_playlist_creation_rejected",
                "YouTube playlist creation could not start. Check the connection and retry.",
                ex is InvalidOperationException or OperationCanceledException ? 409 : 503,
                innerException: ex);
        }
        using var ownedService = service;
        var playlist = new Google.Apis.YouTube.v3.Data.Playlist
        {
            Snippet = new PlaylistSnippet { Title = name },
            Status = new PlaylistStatus { PrivacyStatus = "private" }
        };
        var created = await service.Playlists.Insert(playlist, "snippet,status").ExecuteAsync(cancellationToken);
        return !string.IsNullOrWhiteSpace(created.Id)
            && created.Status?.PrivacyStatus == "private"
            ? created.Id
            : throw new InvalidOperationException("YouTube did not confirm the private playlist and its ID.");
    }

    async Task IYouTubePlaylistClient.ValidateWritablePlaylistAsync(
        PlatformAccountContext accountContext, string playlistId, CancellationToken cancellationToken)
    {
        var account = await RequireWritableAccountAsync(accountContext, cancellationToken);
        using var service = await CreateYouTubeServiceAsync(account, cancellationToken);
        var seenTokens = new HashSet<string>(StringComparer.Ordinal);
        string? pageToken = null;
        do
        {
            var request = service.Playlists.List("id");
            request.Mine = true;
            request.MaxResults = 50;
            request.PageToken = pageToken;
            var response = await request.ExecuteAsync(cancellationToken);
            if (response.Items?.Any(item => item.Id == playlistId) == true)
                return;
            pageToken = response.NextPageToken;
            if (!string.IsNullOrEmpty(pageToken) && !seenTokens.Add(pageToken))
                throw new InvalidOperationException("YouTube returned a repeated playlist page token.");
        } while (!string.IsNullOrEmpty(pageToken));
        throw new InvalidOperationException("The selected YouTube account does not own this playlist.");
    }

    async Task<IReadOnlyList<YouTubeWritablePlaylistItem>> IYouTubePlaylistClient.GetRawPlaylistItemsAsync(
        PlatformAccountContext accountContext, string playlistId, CancellationToken cancellationToken)
    {
        var account = await RequireWritableAccountAsync(accountContext, cancellationToken);
        using var service = await CreateYouTubeServiceAsync(account, cancellationToken);
        var items = new List<YouTubeWritablePlaylistItem>();
        var seenTokens = new HashSet<string>(StringComparer.Ordinal);
        var seenItemIds = new HashSet<string>(StringComparer.Ordinal);
        string? pageToken = null;
        do
        {
            var request = service.PlaylistItems.List("id,snippet,contentDetails,status");
            request.PlaylistId = playlistId;
            request.MaxResults = 50;
            request.PageToken = pageToken;
            var response = await request.ExecuteAsync(cancellationToken);
            var pageItems = response.Items
                ?? throw new InvalidOperationException("YouTube returned a missing playlist page. No changes were made.");
            var videoIds = pageItems.Select(item => item?.ContentDetails?.VideoId ?? item?.Snippet?.ResourceId?.VideoId)
                .Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id!)
                .Distinct(StringComparer.Ordinal).ToArray();
            var availableVideoIds = new HashSet<string>(StringComparer.Ordinal);
            if (videoIds.Length > 0)
            {
                var videosRequest = service.Videos.List("id,status");
                videosRequest.Id = string.Join(',', videoIds);
                videosRequest.MaxResults = videoIds.Length;
                var videosResponse = await videosRequest.ExecuteAsync(cancellationToken);
                foreach (var video in videosResponse.Items ?? [])
                {
                    if (!string.IsNullOrWhiteSpace(video.Id)
                        && video.Status?.PrivacyStatus is ("public" or "unlisted"))
                        availableVideoIds.Add(video.Id);
                }
            }
            foreach (var item in pageItems)
            {
                if (item is null)
                    throw new InvalidOperationException("YouTube returned a playlist item without details. No changes were made.");
                if (string.IsNullOrWhiteSpace(item.Id))
                    throw new InvalidOperationException("YouTube returned a playlist item without an ID. No changes were made.");
                var snippet = item.Snippet;
                if (!seenItemIds.Add(item.Id) || snippet is null || snippet.Position is null)
                    throw new InvalidOperationException("YouTube returned an incomplete playlist snapshot. No changes were made.");
                var videoId = item.ContentDetails?.VideoId ?? item.Snippet?.ResourceId?.VideoId;
                var isAvailable = !string.IsNullOrWhiteSpace(videoId)
                    && availableVideoIds.Contains(videoId)
                    && !string.Equals(item.Status?.PrivacyStatus, "private", StringComparison.OrdinalIgnoreCase);
                items.Add(new YouTubeWritablePlaylistItem(
                    item.Id,
                    videoId,
                    snippet.Position,
                    isAvailable));
            }
            pageToken = response.NextPageToken;
            if (!string.IsNullOrEmpty(pageToken) && !seenTokens.Add(pageToken))
                throw new InvalidOperationException("YouTube returned a repeated playlist item page token. No changes were made.");
        } while (!string.IsNullOrEmpty(pageToken));
        return items.OrderBy(item => item.Position ?? long.MaxValue).ToArray();
    }

    async Task<string> IYouTubePlaylistClient.InsertPlaylistItemAsync(
        PlatformAccountContext accountContext, string playlistId, string videoId, long position,
        CancellationToken cancellationToken)
    {
        var account = await RequireWritableAccountAsync(accountContext, cancellationToken);
        using var service = await CreateYouTubeServiceAsync(account, cancellationToken, disableAutomaticRetries: true);
        var item = new PlaylistItem
        {
            Snippet = new PlaylistItemSnippet
            {
                PlaylistId = playlistId,
                Position = position,
                ResourceId = new ResourceId { Kind = "youtube#video", VideoId = videoId }
            }
        };
        var inserted = await service.PlaylistItems.Insert(item, "snippet").ExecuteAsync(cancellationToken);
        return inserted.Id ?? throw new InvalidOperationException("YouTube inserted a playlist item without returning its ID.");
    }

    async Task IYouTubePlaylistClient.MovePlaylistItemAsync(
        PlatformAccountContext accountContext, string playlistId, YouTubeWritablePlaylistItem item,
        long position, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(item.VideoId))
            throw new InvalidOperationException("Cannot move a YouTube item without a video ID.");
        var account = await RequireWritableAccountAsync(accountContext, cancellationToken);
        using var service = await CreateYouTubeServiceAsync(account, cancellationToken, disableAutomaticRetries: true);
        var body = new PlaylistItem
        {
            Id = item.Id,
            Snippet = new PlaylistItemSnippet
            {
                PlaylistId = playlistId,
                Position = position,
                ResourceId = new ResourceId { Kind = "youtube#video", VideoId = item.VideoId }
            }
        };
        await service.PlaylistItems.Update(body, "snippet").ExecuteAsync(cancellationToken);
    }

    async Task IYouTubePlaylistClient.DeletePlaylistItemAsync(
        PlatformAccountContext accountContext, string itemId, CancellationToken cancellationToken)
    {
        var account = await RequireWritableAccountAsync(accountContext, cancellationToken);
        using var service = await CreateYouTubeServiceAsync(account, cancellationToken, disableAutomaticRetries: true);
        await service.PlaylistItems.Delete(itemId).ExecuteAsync(cancellationToken);
    }

    /// <summary>
    /// Exchanges authorization code for tokens and saves the connected account
    /// </summary>
    public async Task<ConnectedServiceAccount> ExchangeCodeAndSaveAsync(
        int userId,
        string authorizationCode,
        string redirectUri)
    {
        var clientId = _configuration["YouTube:ClientId"]
            ?? throw new InvalidOperationException("YouTube:ClientId is not configured");
        var clientSecret = _configuration["YouTube:ClientSecret"]
            ?? throw new InvalidOperationException("YouTube:ClientSecret is not configured");

        using var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = clientId,
                ClientSecret = clientSecret
            }
        });

        var tokenResponse = await flow.ExchangeCodeForTokenAsync(
            userId.ToString(),
            authorizationCode,
            redirectUri,
            CancellationToken.None);

        // Get user info from Google
        var credential = new UserCredential(flow, userId.ToString(), tokenResponse);
        using var youtubeService = new Google.Apis.YouTube.v3.YouTubeService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Cantaro"
        });

        // Get the authenticated user's channel
        var channelRequest = youtubeService.Channels.List("snippet");
        channelRequest.Mine = true;
        var channelResponse = await channelRequest.ExecuteAsync();
        var channel = channelResponse.Items?.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(channel?.Id))
        {
            throw new PlatformApiException("youtube_channel_missing_id",
                "YouTube did not return a stable channel ID. No playlist links were changed.",
                StatusCodes.Status502BadGateway);
        }

        var displayName = channel.Snippet?.Title ?? "Unknown";

        // Check if account already exists
        var existingAccount = await _dbContext.ConnectedServiceAccounts
            .FirstOrDefaultAsync(a => a.UserId == userId && a.Service == ServiceName);

        if (existingAccount != null)
        {
            if (tokenResponse.RefreshToken == null
                && (string.IsNullOrWhiteSpace(existingAccount.EncryptedRefreshToken)
                    || !string.Equals(existingAccount.ExternalAccountId, channel.Id, StringComparison.Ordinal)))
            {
                throw new PlatformApiException("youtube_refresh_token_missing",
                    "YouTube did not provide a refresh token. Reconnect YouTube with account access before syncing.",
                    StatusCodes.Status409Conflict);
            }

            existingAccount.ExternalAccountId = channel.Id;
            existingAccount.DisplayName = displayName;
            // Only update refresh token if we received a new one
            if (tokenResponse.RefreshToken != null)
            {
                existingAccount.EncryptedRefreshToken = _tokenEncryption.Encrypt(tokenResponse.RefreshToken);
                _logger.LogInformation("Updated refresh token for YouTube account of user {UserId}", userId);
            }
            else
            {
                _logger.LogDebug("No new refresh token received for user {UserId}, keeping existing token", userId);
            }
            existingAccount.Scopes = tokenResponse.Scope;
            existingAccount.TokenExpiresAt = tokenResponse.IssuedUtc.AddSeconds(tokenResponse.ExpiresInSeconds ?? 3600);
            existingAccount.ConnectionState = "connected";
            existingAccount.ReconnectRequiredAt = null;
            existingAccount.ReconnectReason = null;
            existingAccount.TokenVersion++;
            existingAccount.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            // For new accounts, refresh token is required for background sync
            if (tokenResponse.RefreshToken == null)
            {
                throw new InvalidOperationException("No refresh token received from Google. User may need to revoke access at https://myaccount.google.com/permissions and reconnect.");
            }

            existingAccount = new ConnectedServiceAccount
            {
                UserId = userId,
                Service = ServiceName,
                ExternalAccountId = channel.Id,
                DisplayName = displayName,
                EncryptedRefreshToken = _tokenEncryption.Encrypt(tokenResponse.RefreshToken),
                Scopes = tokenResponse.Scope,
                TokenExpiresAt = tokenResponse.IssuedUtc.AddSeconds(tokenResponse.ExpiresInSeconds ?? 3600),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _dbContext.ConnectedServiceAccounts.Add(existingAccount);
        }

        await _dbContext.SaveChangesAsync();
        await RestoreMappingsForConnectedAccountAsync(existingAccount);
        return existingAccount;
    }

    private async Task RestoreMappingsForConnectedAccountAsync(ConnectedServiceAccount account)
    {
        var mappings = await _dbContext.ServicePlaylistMappings
            .Where(mapping => mapping.ConnectedServiceAccountId == account.Id
                && mapping.UserId == account.UserId && mapping.Service == ServiceName)
            .ToListAsync();
        foreach (var mapping in mappings)
        {
            if (!string.Equals(mapping.ExternalAccountId, account.ExternalAccountId, StringComparison.Ordinal))
            {
                if (mapping.State is "active" or "paused")
                {
                    mapping.State = "paused";
                    mapping.LastError = "account_changed";
                    mapping.NextAttemptAt = null;
                }
            }
            else if (mapping.State == "paused" && mapping.LastError is "account_disconnected" or "account_changed")
            {
                mapping.State = "active";
                mapping.LastError = null;
            }
        }

        await _dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Gets the connected YouTube account for a user, or null if not connected
    /// </summary>
    public async Task<ConnectedServiceAccount?> GetConnectedAccountAsync(int userId)
    {
        return await _dbContext.ConnectedServiceAccounts
            .FirstOrDefaultAsync(a => a.UserId == userId && a.Service == ServiceName);
    }

    private async Task<ConnectedServiceAccount> RequireAccountAsync(
        PlatformAccountContext accountContext,
        CancellationToken cancellationToken)
    {
        return await _dbContext.ConnectedServiceAccounts.AsNoTracking().SingleOrDefaultAsync(
                account => account.Id == accountContext.ConnectedServiceAccountId
                    && account.UserId == accountContext.UserId
                    && account.Service == ServiceName
                    && (accountContext.ExpectedExternalAccountId == null
                        || account.ExternalAccountId == accountContext.ExpectedExternalAccountId),
                cancellationToken)
            ?? throw new InvalidOperationException("The selected YouTube account has changed or is no longer connected.");
    }

    /// <summary>
    /// Disconnects a YouTube account for a user
    /// </summary>
    public async Task DisconnectAsync(int userId)
    {
        var account = await _dbContext.ConnectedServiceAccounts
            .FirstOrDefaultAsync(a => a.UserId == userId && a.Service == ServiceName);

        if (account != null)
        {
            // Revoke the token with Google before deleting locally
            if (!string.IsNullOrEmpty(account.EncryptedRefreshToken))
            {
                try
                {
                    var refreshToken = _tokenEncryption.Decrypt(account.EncryptedRefreshToken);
                    using var httpClient = new HttpClient();
                    await httpClient.PostAsync($"https://oauth2.googleapis.com/revoke?token={Uri.EscapeDataString(refreshToken)}", null);
                    _logger.LogInformation("Revoked YouTube token for user {UserId}", userId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to revoke YouTube token for user {UserId}", userId);
                    // Continue with local deletion even if revocation fails
                }
            }

            var mappings = await _dbContext.ServicePlaylistMappings
                .Where(mapping => mapping.ConnectedServiceAccountId == account.Id
                    && mapping.UserId == userId && mapping.Service == ServiceName)
                .ToListAsync();
            foreach (var mapping in mappings.Where(mapping => mapping.State == "active"))
            {
                mapping.State = "paused";
                mapping.LastError = "account_disconnected";
                mapping.NextAttemptAt = null;
            }

            account.EncryptedAccessToken = null;
            account.EncryptedRefreshToken = null;
            account.TokenExpiresAt = null;
            account.RefreshTokenExpiresAt = null;
            account.Scopes = null;
            account.TokenVersion++;
            account.TokenRefreshLeaseId = null;
            account.TokenRefreshLeaseExpiresAt = null;
            account.ConnectionState = "disconnected";
            account.ReconnectRequiredAt = null;
            account.ReconnectReason = null;
            account.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Gets the user's YouTube playlists
    /// </summary>
    public async Task<List<YouTubePlaylistDto>> GetPlaylistsAsync(int userId)
    {
        var account = await GetConnectedAccountAsync(userId)
            ?? throw new InvalidOperationException("YouTube account not connected");
        return await GetPlaylistsAsync(
            new PlatformAccountContext(userId, account.Id),
            CancellationToken.None);
    }

    public async Task<List<YouTubePlaylistDto>> GetPlaylistsAsync(
        PlatformAccountContext accountContext,
        CancellationToken cancellationToken)
    {
        var account = await RequireAccountAsync(accountContext, cancellationToken);
        var userId = accountContext.UserId;

        using var youtubeService = await CreateYouTubeServiceAsync(account);
        var playlists = new List<YouTubePlaylistDto>();
        string? nextPageToken = null;
        const int maxPages = 20; // Limit to ~1000 playlists to prevent runaway requests
        int pageCount = 0;

        do
        {
            if (++pageCount > maxPages)
            {
                _logger.LogWarning("Hit max page limit ({MaxPages}) fetching playlists for user {UserId}", maxPages, userId);
                break;
            }

            var request = youtubeService.Playlists.List("snippet,contentDetails");
            request.Mine = true;
            request.MaxResults = 50;
            request.PageToken = nextPageToken;

            var response = await request.ExecuteAsync(cancellationToken);

            if (response.Items != null)
            {
                foreach (var playlist in response.Items)
                {
                    if (playlist == null)
                    {
                        _logger.LogWarning("Encountered null playlist item for user {UserId}", userId);
                        continue;
                    }

                    if (playlist.Snippet == null)
                    {
                        _logger.LogWarning("Encountered playlist with null snippet for user {UserId}, playlist ID {PlaylistId}", userId, playlist.Id);
                        continue;
                    }

                    playlists.Add(new YouTubePlaylistDto
                    {
                        Id = playlist.Id,
                        Title = playlist.Snippet.Title,
                        Description = playlist.Snippet.Description,
                        ThumbnailUrl = playlist.Snippet.Thumbnails?.Medium?.Url
                            ?? playlist.Snippet.Thumbnails?.Default__?.Url,
                        ItemCount = (int)(playlist.ContentDetails?.ItemCount ?? 0),
                        PublishedAt = ParsePublishedAtRaw(playlist.Snippet?.PublishedAtRaw)
                    });
                }
            }

            nextPageToken = response.NextPageToken;
        } while (!string.IsNullOrEmpty(nextPageToken));

        return playlists;
    }

    /// <summary>
    /// Gets items in a YouTube playlist
    /// </summary>
    public async Task<List<YouTubePlaylistItemDto>> GetPlaylistItemsAsync(int userId, string playlistId)
    {
        var account = await GetConnectedAccountAsync(userId)
            ?? throw new InvalidOperationException("YouTube account not connected");
        return await GetPlaylistItemsAsync(
            new PlatformAccountContext(userId, account.Id),
            playlistId,
            CancellationToken.None);
    }

    public async Task<List<YouTubePlaylistItemDto>> GetPlaylistItemsAsync(
        PlatformAccountContext accountContext,
        string playlistId,
        CancellationToken cancellationToken)
    {
        var account = await RequireAccountAsync(accountContext, cancellationToken);
        var userId = accountContext.UserId;

        using var youtubeService = await CreateYouTubeServiceAsync(account);
        var items = new List<YouTubePlaylistItemDto>();
        string? nextPageToken = null;
        const int maxPages = 40; // Limit to ~2000 items to prevent runaway requests
        int pageCount = 0;

        do
        {
            if (++pageCount > maxPages)
            {
                _logger.LogWarning("Hit max page limit ({MaxPages}) fetching playlist items for user {UserId}, playlist {PlaylistId}", maxPages, userId, playlistId);
                break;
            }

            var request = youtubeService.PlaylistItems.List("snippet,contentDetails,status");
            request.PlaylistId = playlistId;
            request.MaxResults = 50;
            request.PageToken = nextPageToken;

            var response = await request.ExecuteAsync(cancellationToken);

            if (response.Items != null)
            {
                var videoIds = response.Items
                    .Select(item => item?.ContentDetails?.VideoId)
                    .Where(videoId => !string.IsNullOrWhiteSpace(videoId))
                    .Cast<string>()
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                var availableVideos = await GetAvailableVideosAsync(youtubeService, videoIds);

                foreach (var item in response.Items)
                {
                    var mapping = MapAvailablePlaylistItem(item, availableVideos);
                    if (mapping.Item == null)
                    {
                        _logger.LogInformation(
                            "Skipping YouTube playlist item at position {Position} in playlist {PlaylistId}: {Reason}",
                            mapping.ProviderPosition,
                            playlistId,
                            mapping.SkipReason);
                        continue;
                    }

                    items.Add(mapping.Item);
                }
            }

            nextPageToken = response.NextPageToken;
        } while (!string.IsNullOrEmpty(nextPageToken));

        return CompactPlaylistPositions(items);
    }

    internal static List<YouTubePlaylistItemDto> CompactPlaylistPositions(
        IEnumerable<YouTubePlaylistItemDto> items)
    {
        var orderedItems = items.OrderBy(item => item.Position).ToList();
        for (var index = 0; index < orderedItems.Count; index++)
        {
            orderedItems[index].Position = index;
        }

        return orderedItems;
    }

    internal static YouTubePlaylistItemMappingResult MapAvailablePlaylistItem(
        PlaylistItem? item,
        IReadOnlyDictionary<string, AvailableVideoMetadata> availableVideos)
    {
        if (item?.Snippet == null)
        {
            return new(null, item == null ? "missing_item" : "missing_snippet", null);
        }

        var position = item.Snippet.Position;
        if (string.IsNullOrWhiteSpace(item.ContentDetails?.VideoId))
        {
            return new(null, "missing_video_id", position);
        }

        if (string.Equals(item.Status?.PrivacyStatus, "private", StringComparison.OrdinalIgnoreCase))
        {
            return new(null, "private", position);
        }

        var videoId = item.ContentDetails.VideoId;
        if (!availableVideos.TryGetValue(videoId, out var video))
        {
            return new(null, "provider_unavailable", position);
        }

        return new(
            new YouTubePlaylistItemDto
            {
                VideoId = videoId,
                Title = item.Snippet.Title,
                Description = string.IsNullOrWhiteSpace(video.Description)
                    ? item.Snippet.Description
                    : video.Description,
                ThumbnailUrl = item.Snippet.Thumbnails?.Medium?.Url
                    ?? item.Snippet.Thumbnails?.Default__?.Url,
                ChannelTitle = item.Snippet.VideoOwnerChannelTitle,
                Position = (int)(position ?? 0),
                PublishedAt = ParsePublishedAtRaw(item.Snippet.PublishedAtRaw),
                DurationSeconds = video.DurationSeconds
            },
            null,
            position);
    }

    private async Task<Dictionary<string, AvailableVideoMetadata>> GetAvailableVideosAsync(
        Google.Apis.YouTube.v3.YouTubeService youtubeService,
        IReadOnlyCollection<string> videoIds)
    {
        if (videoIds.Count == 0)
        {
            return [];
        }

        var request = youtubeService.Videos.List("snippet,contentDetails,status");
        request.Id = string.Join(",", videoIds);
        request.MaxResults = videoIds.Count;

        var response = await request.ExecuteAsync();
        var availableVideos = new Dictionary<string, AvailableVideoMetadata>(StringComparer.Ordinal);

        if (response.Items == null)
        {
            return availableVideos;
        }

        foreach (var video in response.Items)
        {
            if (string.IsNullOrWhiteSpace(video.Id)
                || string.Equals(video.Status?.PrivacyStatus, "private", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int? durationSeconds = null;
            if (!string.IsNullOrWhiteSpace(video.ContentDetails?.Duration))
            {
                try
                {
                    var duration = XmlConvert.ToTimeSpan(video.ContentDetails.Duration);
                    durationSeconds = (int)Math.Round(duration.TotalSeconds, MidpointRounding.AwayFromZero);
                }
                catch (FormatException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse YouTube duration '{Duration}' for video {VideoId}", video.ContentDetails.Duration, video.Id);
                }
            }

            availableVideos[video.Id] = new AvailableVideoMetadata(
                durationSeconds, video.Snippet?.Description);
        }

        return availableVideos;
    }

    internal sealed record AvailableVideoMetadata(int? DurationSeconds, string? Description);

    private async Task<Google.Apis.YouTube.v3.YouTubeService> CreateYouTubeServiceAsync(
        ConnectedServiceAccount account, CancellationToken cancellationToken = default,
        bool disableAutomaticRetries = false)
    {
        if (string.IsNullOrEmpty(account.EncryptedRefreshToken))
            throw new InvalidOperationException("No refresh token available");

        var clientId = _configuration["YouTube:ClientId"]
            ?? throw new InvalidOperationException("YouTube:ClientId is not configured");
        var clientSecret = _configuration["YouTube:ClientSecret"]
            ?? throw new InvalidOperationException("YouTube:ClientSecret is not configured");

        var refreshToken = _tokenEncryption.Decrypt(account.EncryptedRefreshToken);

        using var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = clientId,
                ClientSecret = clientSecret
            }
        });

        var token = new TokenResponse
        {
            RefreshToken = refreshToken
        };

        var credential = new UserCredential(flow, account.UserId.ToString(), token);

        // Refresh the access token
        if (await credential.RefreshTokenAsync(cancellationToken))
        {
            _logger.LogDebug("Successfully refreshed YouTube access token for user {UserId}", account.UserId);
        }

        var service = new Google.Apis.YouTube.v3.YouTubeService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Cantaro",
            // An insert can succeed before its response is lost. The next job attempt
            // rereads the playlist; an SDK retry here could create a duplicate video.
            DefaultExponentialBackOffPolicy = disableAutomaticRetries
                ? Google.Apis.Http.ExponentialBackOffPolicy.None
                : Google.Apis.Http.ExponentialBackOffPolicy.UnsuccessfulResponse503
        });
        // The credential's 401 handler can replay a POST too. Access is already
        // refreshed above; a failed mutation must return to the caller once.
        if (disableAutomaticRetries && credential is Google.Apis.Http.IHttpUnsuccessfulResponseHandler handler)
            service.HttpClient.MessageHandler.RemoveUnsuccessfulResponseHandler(handler);
        return service;
    }
}
