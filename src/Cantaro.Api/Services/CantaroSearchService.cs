using System.Data.Common;
using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

public sealed class CantaroSearchService(
    ApplicationDbContext dbContext,
    ILogger<CantaroSearchService> logger)
{
    public const int DefaultLimitPerGroup = 6;
    public const int MaximumLimitPerGroup = 20;
    public const int MaximumQueryLength = 200;

    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly ILogger<CantaroSearchService> _logger = logger;

    public async Task<SearchResponseDto> SearchAsync(
        int userId,
        string query,
        int limitPerGroup,
        CancellationToken cancellationToken)
    {
        var normalizedQuery = query.Trim();
        var normalizedLower = normalizedQuery.ToLowerInvariant();
        var limit = Math.Clamp(limitPerGroup, 1, MaximumLimitPerGroup);

        var songs = await ExecuteGroupAsync(
            "songs",
            () => SearchSongsAsync(userId, normalizedLower, limit, cancellationToken),
            cancellationToken);
        var playlists = await ExecuteGroupAsync(
            "playlists",
            () => SearchPlaylistsAsync(userId, normalizedLower, limit, cancellationToken),
            cancellationToken);
        var media = await ExecuteGroupAsync(
            "media",
            () => SearchMediaAsync(userId, normalizedLower, limit, cancellationToken),
            cancellationToken);

        return new SearchResponseDto
        {
            Query = normalizedQuery,
            Groups = new SearchGroupsDto
            {
                Songs = songs,
                Artists = new SearchGroupDto
                {
                    Status = SearchGroupStatuses.Unavailable,
                    Items = [],
                    Message = "Artist search will be available when artist detail pages are implemented."
                },
                Playlists = playlists,
                Media = media
            }
        };
    }

    private async Task<SearchGroupDto> SearchSongsAsync(
        int userId,
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        var candidates = await _dbContext.Tracks
            .AsNoTracking()
            .Where(track => track.PlaylistEntries.Any(entry =>
                entry.Playlist != null && entry.Playlist.UserId == userId))
            .Where(track =>
                (track.SearchTitle != null && track.SearchTitle.ToLower().Contains(query))
                || (track.SearchArtist != null && track.SearchArtist.ToLower().Contains(query)))
            .OrderBy(track =>
                track.SearchTitle != null && track.SearchTitle.ToLower() == query ? 0 :
                track.SearchTitle != null && track.SearchTitle.ToLower().StartsWith(query) ? 1 :
                track.SearchTitle != null && track.SearchTitle.ToLower().Contains(query) ? 2 :
                track.SearchArtist != null && track.SearchArtist.ToLower() == query ? 3 :
                track.SearchArtist != null && track.SearchArtist.ToLower().StartsWith(query) ? 4 : 5)
            .ThenBy(track => track.SearchTitle)
            .ThenBy(track => track.SearchArtist)
            .ThenBy(track => track.Id)
            .Select(track => new
            {
                track.Id,
                track.SearchTitle,
                track.SearchArtist,
                track.CanonicalMetadata
            })
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = candidates.Count > limit;
        var items = candidates.Take(limit).Select(track =>
        {
            var metadata = ParseTrackMetadata(track.CanonicalMetadata);
            return new SearchResultDto
            {
                EntityType = "song",
                Id = $"track:{track.Id}",
                Title = track.SearchTitle ?? "Unknown song",
                Subtitle = track.SearchArtist,
                Detail = metadata?.Albums?.FirstOrDefault(album => !string.IsNullOrWhiteSpace(album)),
                ArtworkUrl = metadata?.ThumbnailUrl,
                CanonicalRoute = $"/music/songs/{Uri.EscapeDataString($"track:{track.Id}")}"
            };
        }).ToList();

        return Successful(items, hasMore);
    }

    private async Task<SearchGroupDto> SearchPlaylistsAsync(
        int userId,
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        var candidates = await _dbContext.Playlists
            .AsNoTracking()
            .Where(playlist => playlist.UserId == userId)
            .Where(playlist =>
                playlist.Name.ToLower().Contains(query)
                || (playlist.Description != null && playlist.Description.ToLower().Contains(query)))
            .OrderBy(playlist =>
                playlist.Name.ToLower() == query ? 0 :
                playlist.Name.ToLower().StartsWith(query) ? 1 :
                playlist.Name.ToLower().Contains(query) ? 2 : 3)
            .ThenBy(playlist => playlist.Name)
            .ThenBy(playlist => playlist.Id)
            .Select(playlist => new
            {
                playlist.Id,
                playlist.Name,
                playlist.Description,
                EntryCount = playlist.Entries.Count
            })
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = candidates.Count > limit;
        var items = candidates.Take(limit).Select(playlist => new SearchResultDto
        {
            EntityType = "playlist",
            Id = playlist.Id.ToString(),
            Title = playlist.Name,
            Subtitle = playlist.Description,
            Detail = playlist.EntryCount == 1 ? "1 song" : $"{playlist.EntryCount} songs",
            CanonicalRoute = $"/music/playlists/{playlist.Id}"
        }).ToList();

        return Successful(items, hasMore);
    }

    private async Task<SearchGroupDto> SearchMediaAsync(
        int userId,
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        var candidates = await _dbContext.MediaLibraryEntries
            .AsNoTracking()
            .Where(entry => entry.UserId == userId && entry.MediaTitle != null)
            .Where(entry =>
                entry.MediaTitle!.CanonicalTitle.ToLower().Contains(query)
                || (entry.MediaTitle.OriginalTitle != null
                    && entry.MediaTitle.OriginalTitle.ToLower().Contains(query))
                || (entry.MediaTitle.SortTitle != null
                    && entry.MediaTitle.SortTitle.ToLower().Contains(query)))
            .OrderBy(entry =>
                entry.MediaTitle!.CanonicalTitle.ToLower() == query ? 0 :
                entry.MediaTitle.CanonicalTitle.ToLower().StartsWith(query) ? 1 :
                entry.MediaTitle.CanonicalTitle.ToLower().Contains(query) ? 2 :
                entry.MediaTitle.OriginalTitle != null
                    && entry.MediaTitle.OriginalTitle.ToLower() == query ? 3 :
                entry.MediaTitle.OriginalTitle != null
                    && entry.MediaTitle.OriginalTitle.ToLower().StartsWith(query) ? 4 : 5)
            .ThenBy(entry => entry.MediaTitle!.CanonicalTitle)
            .ThenBy(entry => entry.Id)
            .Select(entry => new
            {
                entry.Id,
                entry.MediaTitle!.CanonicalTitle,
                entry.MediaTitle.OriginalTitle,
                entry.MediaTitle.MediaKind,
                entry.MediaTitle.StartYear,
                entry.MediaTitle.CanonicalMetadata,
                entry.NormalizedStatus
            })
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = candidates.Count > limit;
        var items = candidates.Take(limit).Select(entry => new SearchResultDto
        {
            EntityType = "media",
            Id = entry.Id.ToString(),
            Title = entry.CanonicalTitle,
            Subtitle = BuildMediaSubtitle(entry.MediaKind, entry.StartYear),
            Detail = entry.NormalizedStatus,
            ArtworkUrl = GetMediaArtworkUrl(entry.CanonicalMetadata),
            CanonicalRoute = $"/media/library/{entry.Id}"
        }).ToList();

        return Successful(items, hasMore);
    }

    private async Task<SearchGroupDto> ExecuteGroupAsync(
        string groupName,
        Func<Task<SearchGroupDto>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            return await action();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is DbException or TimeoutException)
        {
            _logger.LogWarning(exception, "Cantaro search group {SearchGroup} failed.", groupName);
            return new SearchGroupDto
            {
                Status = SearchGroupStatuses.Failed,
                Items = [],
                Message = "This result group is temporarily unavailable."
            };
        }
    }

    private static SearchGroupDto Successful(IReadOnlyList<SearchResultDto> items, bool hasMore) => new()
    {
        Status = SearchGroupStatuses.Ok,
        Items = items,
        HasMore = hasMore
    };

    private static TrackCanonicalMetadata? ParseTrackMetadata(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TrackCanonicalMetadata>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? GetMediaArtworkUrl(string? canonicalMetadata)
    {
        if (string.IsNullOrWhiteSpace(canonicalMetadata))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(canonicalMetadata);
            if (!document.RootElement.TryGetProperty("coverImage", out var coverImage)
                || coverImage.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return GetString(coverImage, "extraLarge")
                ?? GetString(coverImage, "large")
                ?? GetString(coverImage, "medium");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? GetString(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string BuildMediaSubtitle(string mediaKind, int? startYear) =>
        startYear is null ? mediaKind : $"{mediaKind} · {startYear}";
}
