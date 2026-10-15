using System.Data.Common;
using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

public sealed class CantaroSearchService(
    ApplicationDbContext dbContext,
    IMediaProviderRegistry mediaProviderRegistry,
    MediaProviderSearchCache mediaProviderSearchCache,
    MediaCatalogCanonicalizationService catalogCanonicalizationService,
    ILogger<CantaroSearchService> logger)
{
    public const int DefaultLimitPerGroup = 6;
    public const int MaximumLimitPerGroup = 20;
    public const int MaximumQueryLength = 200;

    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly IMediaProviderRegistry _mediaProviderRegistry = mediaProviderRegistry;
    private readonly MediaProviderSearchCache _mediaProviderSearchCache = mediaProviderSearchCache;
    private readonly MediaCatalogCanonicalizationService _catalogCanonicalizationService = catalogCanonicalizationService;
    private readonly ILogger<CantaroSearchService> _logger = logger;

    public async Task<SearchResponseDto> SearchAsync(
        int userId,
        string query,
        int limitPerGroup,
        bool includeDiscovery,
        CancellationToken cancellationToken)
    {
        var normalizedQuery = query.Trim();
        var normalizedLower = normalizedQuery.ToLowerInvariant();
        var queryTokens = normalizedLower.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var limit = Math.Clamp(limitPerGroup, 1, MaximumLimitPerGroup);

        var songs = await ExecuteGroupAsync(
            "songs",
            () => SearchSongsAsync(userId, normalizedLower, queryTokens, limit, cancellationToken),
            cancellationToken);
        var playlists = await ExecuteGroupAsync(
            "playlists",
            () => SearchPlaylistsAsync(userId, normalizedLower, queryTokens, limit, cancellationToken),
            cancellationToken);
        var media = await ExecuteGroupAsync(
            "media",
            () => SearchMediaAsync(
                userId,
                normalizedQuery,
                normalizedLower,
                queryTokens,
                limit,
                includeDiscovery,
                cancellationToken),
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
        string[] queryTokens,
        int limit,
        CancellationToken cancellationToken)
    {
        var candidates = await _dbContext.Tracks
            .AsNoTracking()
            .Where(track => track.PlaylistEntries.Any(entry =>
                entry.Playlist != null && entry.Playlist.UserId == userId))
            .Where(track =>
                (track.SearchTitle != null && track.SearchTitle.ToLower().Contains(query))
                || (track.SearchArtist != null && track.SearchArtist.ToLower().Contains(query))
                || queryTokens.All(token =>
                    (track.SearchTitle != null && track.SearchTitle.ToLower().Contains(token))
                    || (track.SearchArtist != null && track.SearchArtist.ToLower().Contains(token))))
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
        string[] queryTokens,
        int limit,
        CancellationToken cancellationToken)
    {
        var candidates = await _dbContext.Playlists
            .AsNoTracking()
            .Where(playlist => playlist.UserId == userId)
            .Where(playlist =>
                playlist.Name.ToLower().Contains(query)
                || (playlist.Description != null && playlist.Description.ToLower().Contains(query))
                || queryTokens.All(token =>
                    playlist.Name.ToLower().Contains(token)
                    || (playlist.Description != null && playlist.Description.ToLower().Contains(token))))
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
        string displayQuery,
        string query,
        string[] queryTokens,
        int limit,
        bool includeDiscovery,
        CancellationToken cancellationToken)
    {
        var connectedProviderIds = await GetConnectedProviderIdsAsync(userId, cancellationToken);
        var canonicalCandidates = await _dbContext.MediaTitles
            .AsNoTracking()
            .Where(title =>
                title.LibraryEntries.Any(entry => entry.UserId == userId)
                || title.ProviderLinks.Any(link => connectedProviderIds.Contains(link.Provider)))
            .Where(title =>
                title.CanonicalTitle.ToLower().Contains(query)
                || (title.OriginalTitle != null && title.OriginalTitle.ToLower().Contains(query))
                || (title.SortTitle != null && title.SortTitle.ToLower().Contains(query))
                || title.Synonyms.Any(synonym => synonym.ToLower().Contains(query))
                || queryTokens.All(token =>
                    title.CanonicalTitle.ToLower().Contains(token)
                    || (title.OriginalTitle != null && title.OriginalTitle.ToLower().Contains(token))
                    || (title.SortTitle != null && title.SortTitle.ToLower().Contains(token))
                    || title.Synonyms.Any(synonym => synonym.ToLower().Contains(token))))
            .OrderBy(title =>
                title.CanonicalTitle.ToLower() == query ? 0 :
                title.CanonicalTitle.ToLower().StartsWith(query) ? 1 :
                title.CanonicalTitle.ToLower().Contains(query) ? 2 :
                title.OriginalTitle != null && title.OriginalTitle.ToLower() == query ? 3 :
                title.OriginalTitle != null && title.OriginalTitle.ToLower().StartsWith(query) ? 4 : 5)
            .ThenBy(title => title.CanonicalTitle)
            .ThenBy(title => title.Id)
            .Select(title => new CanonicalMediaCandidate(
                title.Id,
                title.CanonicalTitle,
                title.MediaKind,
                title.StartYear,
                title.PosterUrl))
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = canonicalCandidates.Count > limit;
        var items = await BuildCanonicalMediaItemsAsync(
            userId,
            canonicalCandidates.Take(limit).ToList(),
            connectedProviderIds,
            cancellationToken);

        if (!includeDiscovery)
        {
            return Successful(items, hasMore);
        }

        var discovery = await DiscoverProviderMediaAsync(
            userId,
            displayQuery,
            limit,
            connectedProviderIds,
            cancellationToken);
        var mergedCandidates = new List<SearchResultDto>(items);
        var seenCanonicalIds = items
            .Select(item => item.Id)
            .Where(id => id.StartsWith("media-title:", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        IReadOnlyDictionary<MediaCatalogIdentity, Guid> canonicalIds;
        try
        {
            canonicalIds = await _catalogCanonicalizationService.CanonicalizeAsync(
                discovery.Results.Select(item => new MediaCatalogSearchCandidate(item.ProviderId, item.Result)).ToArray(),
                cancellationToken);
        }
        catch (Exception exception) when (exception is DbUpdateException or DbException or TimeoutException)
        {
            _logger.LogWarning(exception, "Media discovery catalog persistence failed.");
            return new SearchGroupDto
            {
                Status = items.Count > 0 ? SearchGroupStatuses.Ok : SearchGroupStatuses.Failed,
                Items = items,
                HasMore = hasMore,
                Message = "Discovered media could not be saved. Try the search again."
            };
        }

        var linkedTitleIds = canonicalIds.Values.Distinct().ToList();
        var linkedTitles = linkedTitleIds.Count == 0
            ? []
            : await _dbContext.MediaTitles
                .AsNoTracking()
                .Where(title => linkedTitleIds.Contains(title.Id))
                .Select(title => new CanonicalMediaCandidate(
                    title.Id,
                    title.CanonicalTitle,
                    title.MediaKind,
                    title.StartYear,
                    title.PosterUrl))
                .ToListAsync(cancellationToken);
        var linkedItems = await BuildCanonicalMediaItemsAsync(
            userId,
            linkedTitles,
            connectedProviderIds,
            cancellationToken);
        foreach (var linkedItem in linkedItems
            .OrderBy(item => item.Title.ToLowerInvariant() == query ? 0 :
                item.Title.ToLowerInvariant().StartsWith(query, StringComparison.Ordinal) ? 1 :
                item.Title.ToLowerInvariant().Contains(query, StringComparison.Ordinal) ? 2 : 5)
            .ThenBy(item => item.Title, StringComparer.Ordinal)
            .ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (seenCanonicalIds.Add(linkedItem.Id)) mergedCandidates.Add(linkedItem);
        }

        hasMore = hasMore || mergedCandidates.Count > limit || discovery.HasMore;
        var allProvidersFailed = discovery.ConnectedProviderCount > 0
            && discovery.FailedProviderCount == discovery.ConnectedProviderCount;
        if (mergedCandidates.Count == 0 && allProvidersFailed)
        {
            return new SearchGroupDto
            {
                Status = SearchGroupStatuses.Failed,
                Items = [],
                Message = "Connected media providers could not be searched."
            };
        }

        var result = Successful(mergedCandidates.Take(limit).ToList(), hasMore);
        if (discovery.FailedProviderCount > 0)
        {
            result = new SearchGroupDto
            {
                Status = result.Status,
                Items = result.Items,
                HasMore = result.HasMore,
                Message = discovery.FailedProviderCount == 1
                    ? "One connected media provider could not be searched."
                    : $"{discovery.FailedProviderCount} connected media providers could not be searched."
            };
        }

        return result;
    }

    private async Task<List<SearchResultDto>> BuildCanonicalMediaItemsAsync(
        int userId,
        IReadOnlyList<CanonicalMediaCandidate> candidates,
        IReadOnlyCollection<string> connectedProviderIds,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        var titleIds = candidates.Select(candidate => candidate.Id).Distinct().ToList();
        var libraryEntries = await _dbContext.MediaLibraryEntries
            .AsNoTracking()
            .Where(entry => entry.UserId == userId && titleIds.Contains(entry.MediaTitleId))
            .Select(entry => new MediaLibrarySearchState(
                entry.MediaTitleId,
                entry.Id,
                entry.Status,
                entry.ProviderBindings.Any(binding => binding.ConnectedServiceAccountId != null),
                entry.UpdatedAt))
            .ToListAsync(cancellationToken);
        var libraryStateByTitle = libraryEntries
            .GroupBy(entry => entry.MediaTitleId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(entry => entry.IsConnected)
                    .ThenByDescending(entry => entry.UpdatedAt)
                    .ThenBy(entry => entry.LibraryEntryId)
                    .First());

        return candidates.Select(candidate =>
        {
            libraryStateByTitle.TryGetValue(candidate.Id, out var libraryState);
            var route = $"/media/{candidate.Id}";

            return new SearchResultDto
            {
                EntityType = "media",
                Id = $"media-title:{candidate.Id}",
                Title = candidate.CanonicalTitle,
                Subtitle = BuildMediaSubtitle(candidate.MediaKind, candidate.StartYear),
                Detail = libraryState?.Status,
                ArtworkUrl = candidate.PosterUrl,
                CanonicalRoute = route,
                MediaKind = candidate.MediaKind,
                IsInLibrary = libraryState is not null,
                LibraryStatus = libraryState?.Status
            };
        }).ToList();
    }

    private async Task<ProviderDiscoveryResult> DiscoverProviderMediaAsync(
        int userId,
        string query,
        int limit,
        IReadOnlyList<string> connectedProviderIds,
        CancellationToken cancellationToken)
    {
        var providerLimit = Math.Min(limit + 1, MaximumLimitPerGroup + 1);
        var results = new List<ProviderDiscoveryItem>();
        var failedProviderCount = 0;
        var hasMore = false;

        foreach (var providerId in connectedProviderIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var providerResults = await _mediaProviderSearchCache.GetOrCreateAsync(
                    userId,
                    providerId,
                    query,
                    providerLimit,
                    token => _mediaProviderRegistry.GetRequired(providerId).SearchAsync(
                        userId,
                        new MediaCatalogSearchRequest
                        {
                            Query = query,
                            Limit = providerLimit
                        },
                        token),
                    cancellationToken);

                hasMore |= providerResults.Count > limit;
                results.AddRange(providerResults.Select(result =>
                    new ProviderDiscoveryItem(providerId, result)));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failedProviderCount++;
                _logger.LogWarning(
                    exception,
                    "Connected media provider {ProviderId} search failed.",
                    providerId);
            }
        }

        return new ProviderDiscoveryResult(
            results,
            connectedProviderIds.Count,
            failedProviderCount,
            hasMore);
    }

    private async Task<List<string>> GetConnectedProviderIdsAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        var supportedProviderIds = _mediaProviderRegistry
            .GetSupportedProviderIds()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var connectedServices = await _dbContext.ConnectedServiceAccounts
            .AsNoTracking()
            .Where(account =>
                account.UserId == userId
                && account.ConnectionState == "connected")
            .Select(account => account.Service)
            .ToListAsync(cancellationToken);

        return connectedServices
            .Where(supportedProviderIds.Contains)
            .Select(service => _mediaProviderRegistry.GetRequired(service).ProviderId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(providerId => providerId)
            .ToList();
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

    private static string BuildMediaSubtitle(string mediaKind, int? startYear) =>
        startYear is null ? mediaKind : $"{mediaKind} · {startYear}";

    private sealed record CanonicalMediaCandidate(
        Guid Id,
        string CanonicalTitle,
        string MediaKind,
        int? StartYear,
        string? PosterUrl);

    private sealed record MediaLibrarySearchState(
        Guid MediaTitleId,
        Guid LibraryEntryId,
        string Status,
        bool IsConnected,
        DateTimeOffset UpdatedAt);

    private sealed record ProviderDiscoveryItem(
        string ProviderId,
        CachedMediaProviderSearchResult Result);

    private sealed record ProviderDiscoveryResult(
        IReadOnlyList<ProviderDiscoveryItem> Results,
        int ConnectedProviderCount,
        int FailedProviderCount,
        bool HasMore);
}
