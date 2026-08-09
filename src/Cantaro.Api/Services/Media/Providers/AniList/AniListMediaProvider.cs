using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

public class AniListMediaProvider(
    ApplicationDbContext dbContext,
    AniListApiClient apiClient,
    TokenEncryptionService tokenEncryptionService,
    ILogger<AniListMediaProvider> logger) : IMediaProvider
{
    private const string ProviderName = "anilist";
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly AniListApiClient _apiClient = apiClient;
    private readonly TokenEncryptionService _tokenEncryptionService = tokenEncryptionService;
    private readonly ILogger<AniListMediaProvider> _logger = logger;

    public string ProviderId => ProviderName;

    public string GetAuthorizationUrl(string redirectUri, string state, string codeChallenge)
    {
        return _apiClient.BuildAuthorizationUrl(redirectUri, state, codeChallenge);
    }

    public async Task<ConnectedServiceAccount?> GetConnectedAccountAsync(int userId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ConnectedServiceAccounts
            .FirstOrDefaultAsync(account => account.UserId == userId && account.Service == ProviderName, cancellationToken);
    }

    public async Task<ConnectedServiceAccount> ExchangeCodeAndSaveAsync(
        int userId,
        string authorizationCode,
        string redirectUri,
        string codeVerifier,
        CancellationToken cancellationToken)
    {
        var tokenResponse = await _apiClient.ExchangeCodeAsync(authorizationCode, redirectUri, codeVerifier, cancellationToken);
        if (string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
        {
            throw new InvalidOperationException("AniList did not return an access token.");
        }

        var viewerData = await _apiClient.SendGraphQlAsync<AniListViewerData>(
            tokenResponse.AccessToken,
            ViewerQuery,
            variables: null,
            cancellationToken);

        var viewer = viewerData.Viewer ?? throw new InvalidOperationException("AniList did not return viewer information.");
        var account = await GetConnectedAccountAsync(userId, cancellationToken);
        var now = DateTime.UtcNow;

        if (account is null)
        {
            account = new ConnectedServiceAccount
            {
                UserId = userId,
                Service = ProviderName,
                ExternalAccountId = viewer.Id.ToString(CultureInfo.InvariantCulture),
                DisplayName = viewer.Name,
                EncryptedRefreshToken = _tokenEncryptionService.Encrypt(tokenResponse.AccessToken),
                Scopes = "media_list",
                TokenExpiresAt = now.AddSeconds(tokenResponse.ExpiresIn),
                CreatedAt = now,
                UpdatedAt = now
            };

            _dbContext.ConnectedServiceAccounts.Add(account);
        }
        else
        {
            account.ExternalAccountId = viewer.Id.ToString(CultureInfo.InvariantCulture);
            account.DisplayName = viewer.Name;
            account.EncryptedRefreshToken = _tokenEncryptionService.Encrypt(tokenResponse.AccessToken);
            account.Scopes = "media_list";
            account.TokenExpiresAt = now.AddSeconds(tokenResponse.ExpiresIn);
            account.UpdatedAt = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return account;
    }

    public async Task DisconnectAsync(int userId, CancellationToken cancellationToken)
    {
        var account = await GetConnectedAccountAsync(userId, cancellationToken);
        if (account is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var libraryEntries = await _dbContext.MediaLibraryEntries
            .Where(entry => entry.UserId == userId && entry.Provider == ProviderName && entry.ConnectedServiceAccountId == account.Id)
            .ToListAsync(cancellationToken);

        foreach (var entry in libraryEntries)
        {
            entry.ConnectedServiceAccountId = null;
            entry.LastMutationSource = MediaMutationSources.ProviderDisconnect;
            entry.UpdatedAt = now;
        }

        _dbContext.ConnectedServiceAccounts.Remove(account);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<MediaProviderLibraryImportResult> ImportLibraryAsync(int userId, CancellationToken cancellationToken)
    {
        var account = await RequireConnectedAccountAsync(userId, cancellationToken);
        var accessToken = await ResolveAccessTokenAsync(account, cancellationToken);
        var viewerId = int.Parse(account.ExternalAccountId, CultureInfo.InvariantCulture);
        var importedAt = DateTimeOffset.UtcNow;

        var animeCollection = await _apiClient.SendGraphQlAsync<AniListMediaListCollectionData>(
            accessToken,
            MediaListCollectionQuery,
            new { userId = viewerId, type = "ANIME" },
            cancellationToken);

        var mangaCollection = await _apiClient.SendGraphQlAsync<AniListMediaListCollectionData>(
            accessToken,
            MediaListCollectionQuery,
            new { userId = viewerId, type = "MANGA" },
            cancellationToken);

        var items = animeCollection.MediaListCollection?.Lists?
            .Concat(mangaCollection.MediaListCollection?.Lists ?? [])
            .SelectMany(list => list.Entries?.Select(entry => MapLibraryItem(entry, list.Name)) ?? [])
            .Where(item => item is not null)
            .Cast<MediaProviderLibraryItem>()
            .ToList() ?? [];

        return new MediaProviderLibraryImportResult
        {
            ProviderId = ProviderName,
            ImportedAt = importedAt,
            Items = items
        };
    }

    public async Task<IReadOnlyList<MediaProviderSearchResult>> SearchAsync(int userId, MediaCatalogSearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Query);

        var account = await RequireConnectedAccountAsync(userId, cancellationToken);
        var accessToken = await ResolveAccessTokenAsync(account, cancellationToken);
        var mediaKinds = request.MediaKinds.Count == 0
            ? [MediaKinds.Anime, MediaKinds.Manga]
            : request.MediaKinds.Where(kind => kind is MediaKinds.Anime or MediaKinds.Manga).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var perKindLimit = Math.Max(1, request.Limit / Math.Max(1, mediaKinds.Length));
        var results = new List<MediaProviderSearchResult>();

        foreach (var mediaKind in mediaKinds)
        {
            var type = ToAniListMediaType(mediaKind);
            var data = await _apiClient.SendGraphQlAsync<AniListPageData>(
                accessToken,
                SearchMediaQuery,
                new { search = request.Query, perPage = perKindLimit, type },
                cancellationToken);

            if (data.Page?.Media is null)
            {
                continue;
            }

            results.AddRange(data.Page.Media.Select(MapSearchResult));
        }

        return results
            .GroupBy(result => result.ProviderMediaId, StringComparer.Ordinal)
            .Select(group => group.First())
            .Take(request.Limit)
            .ToList();
    }

    public async Task<MediaProviderTitleDetails?> GetTitleDetailsAsync(int userId, string providerMediaId, CancellationToken cancellationToken)
    {
        var account = await RequireConnectedAccountAsync(userId, cancellationToken);
        var accessToken = await ResolveAccessTokenAsync(account, cancellationToken);
        var data = await _apiClient.SendGraphQlAsync<AniListMediaData>(
            accessToken,
            MediaDetailsQuery,
            new { id = ParseProviderMediaId(providerMediaId) },
            cancellationToken);

        return data.Media is null ? null : MapTitleDetails(data.Media);
    }

    public async Task<MediaProviderMutationResult> UpdateProgressAsync(int userId, MediaProgressUpdateRequest request, CancellationToken cancellationToken)
    {
        var account = await RequireConnectedAccountAsync(userId, cancellationToken);
        var accessToken = await ResolveAccessTokenAsync(account, cancellationToken);
        var mediaId = ParseProviderMediaId(request.ProviderMediaId);
        var mutation = BuildSaveMediaListEntryMutation(
            mediaId,
            progress: request.ProgressEpisodes ?? request.ProgressChapters,
            progressVolumes: request.ProgressVolumes,
            status: null);

        var data = await _apiClient.SendGraphQlAsync<AniListSavedMediaListEntryData>(
            accessToken,
            mutation.Query,
            mutation.Variables,
            cancellationToken);

        var savedEntry = data.SaveMediaListEntry ?? throw new InvalidOperationException("AniList did not return the saved media list entry.");
        return MapMutationResult(savedEntry);
    }

    public async Task<MediaProviderMutationResult> UpdateStatusAsync(int userId, MediaStatusUpdateRequest request, CancellationToken cancellationToken)
    {
        var account = await RequireConnectedAccountAsync(userId, cancellationToken);
        var accessToken = await ResolveAccessTokenAsync(account, cancellationToken);
        var mediaId = ParseProviderMediaId(request.ProviderMediaId);
        var status = ToAniListStatus(request.Status);
        var mutation = BuildSaveMediaListEntryMutation(
            mediaId,
            progress: null,
            progressVolumes: null,
            status);

        var data = await _apiClient.SendGraphQlAsync<AniListSavedMediaListEntryData>(
            accessToken,
            mutation.Query,
            mutation.Variables,
            cancellationToken);

        var savedEntry = data.SaveMediaListEntry ?? throw new InvalidOperationException("AniList did not return the saved media list entry.");
        return MapMutationResult(savedEntry);
    }

    public async Task<MediaReleaseMetadata?> GetReleaseMetadataAsync(int userId, string providerMediaId, CancellationToken cancellationToken)
    {
        var details = await GetTitleDetailsAsync(userId, providerMediaId, cancellationToken);
        if (details is null)
        {
            return null;
        }

        var raw = JsonSerializer.Deserialize<AniListReleaseMetadataRaw>(details.RawMetadata ?? "{}");
        return new MediaReleaseMetadata
        {
            ProviderId = ProviderName,
            ProviderMediaId = providerMediaId,
            ReleaseStatusDimension = details.ReleaseStatusDimension,
            ReleasedCount = raw?.ReleasedCount,
            TotalKnownCount = raw?.TotalKnownCount,
            NextReleaseAt = raw?.NextReleaseAt,
            NextReleaseLabel = raw?.NextReleaseLabel,
            RawMetadata = details.RawMetadata
        };
    }

    private async Task<ConnectedServiceAccount> RequireConnectedAccountAsync(int userId, CancellationToken cancellationToken)
    {
        return await GetConnectedAccountAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException("AniList account not connected.");
    }

    private async Task<string> ResolveAccessTokenAsync(ConnectedServiceAccount account, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(account.EncryptedRefreshToken))
        {
            throw new InvalidOperationException("AniList account is missing an access token.");
        }

        if (account.TokenExpiresAt is { } expiresAt && expiresAt <= DateTime.UtcNow)
        {
            throw new InvalidOperationException("AniList access token expired. Reconnect your AniList account.");
        }

        return _tokenEncryptionService.Decrypt(account.EncryptedRefreshToken);
    }

    private static int ParseProviderMediaId(string providerMediaId)
    {
        if (!int.TryParse(providerMediaId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new InvalidOperationException($"AniList media id '{providerMediaId}' is invalid.");
        }

        return parsed;
    }

    private static MediaProviderLibraryItem? MapLibraryItem(AniListMediaListEntry entry, string? listName)
    {
        if (entry.Media is null || entry.Media.Id <= 0)
        {
            return null;
        }

        var mediaKind = MapMediaKind(entry.Media.Type);
        if (mediaKind is null)
        {
            return null;
        }

        var title = SelectCanonicalTitle(entry.Media.Title);
        var dimensions = GetDimensions(mediaKind);

        return new MediaProviderLibraryItem
        {
            ProviderMediaId = entry.Media.Id.ToString(CultureInfo.InvariantCulture),
            ProviderLibraryEntryId = entry.Id.ToString(CultureInfo.InvariantCulture),
            Title = title,
            NativeTitle = entry.Media.Title?.Native,
            OriginalTitle = entry.Media.Title?.Native,
            MediaKind = mediaKind,
            Synopsis = entry.Media.Description,
            ExternalUrl = entry.Media.SiteUrl,
            StartYear = entry.Media.StartDate?.Year,
            EpisodeCount = entry.Media.Episodes,
            ChapterCount = entry.Media.Chapters,
            VolumeCount = entry.Media.Volumes,
            NormalizedStatus = MapNormalizedStatus(entry.Status),
            RawStatus = entry.Status,
            RawListName = listName,
            ProgressEpisodes = mediaKind == MediaKinds.Anime ? entry.Progress : null,
            ProgressChapters = mediaKind == MediaKinds.Manga ? entry.Progress : null,
            ProgressVolumes = mediaKind == MediaKinds.Manga ? entry.ProgressVolumes : null,
            PrimaryProgressDimension = dimensions.PrimaryProgressDimension,
            ReleaseStatusDimension = dimensions.ReleaseStatusDimension,
            LastRemoteUpdateAt = ToDateTimeOffset(entry.UpdatedAt),
            RawMetadata = JsonSerializer.Serialize(entry.Media)
        };
    }

    private static MediaProviderSearchResult MapSearchResult(AniListMedia media)
    {
        var mediaKind = MapMediaKind(media.Type) ?? MediaKinds.Other;
        var dimensions = GetDimensions(mediaKind);

        return new MediaProviderSearchResult
        {
            ProviderId = ProviderName,
            ProviderMediaId = media.Id.ToString(CultureInfo.InvariantCulture),
            Title = SelectCanonicalTitle(media.Title),
            NativeTitle = media.Title?.Native,
            MediaKind = mediaKind,
            Synopsis = media.Description,
            PosterUrl = SelectPosterUrl(media.CoverImage),
            BackgroundUrl = media.BannerImage,
            StartYear = media.StartDate?.Year,
            EpisodeCount = media.Episodes,
            ChapterCount = media.Chapters,
            VolumeCount = media.Volumes,
            PrimaryProgressDimension = dimensions.PrimaryProgressDimension,
            ReleaseStatusDimension = dimensions.ReleaseStatusDimension,
            RawMetadata = JsonSerializer.Serialize(media)
        };
    }

    private static MediaProviderTitleDetails MapTitleDetails(AniListMedia media)
    {
        var mediaKind = MapMediaKind(media.Type) ?? MediaKinds.Other;
        var dimensions = GetDimensions(mediaKind);
        var releaseMetadata = BuildReleaseMetadata(media, dimensions.ReleaseStatusDimension);

        return new MediaProviderTitleDetails
        {
            ProviderId = ProviderName,
            ProviderMediaId = media.Id.ToString(CultureInfo.InvariantCulture),
            Title = SelectCanonicalTitle(media.Title),
            NativeTitle = media.Title?.Native,
            MediaKind = mediaKind,
            Synopsis = media.Description,
            PosterUrl = SelectPosterUrl(media.CoverImage),
            BackgroundUrl = media.BannerImage,
            StartYear = media.StartDate?.Year,
            EpisodeCount = media.Episodes,
            ChapterCount = media.Chapters,
            VolumeCount = media.Volumes,
            PrimaryProgressDimension = dimensions.PrimaryProgressDimension,
            ReleaseStatusDimension = dimensions.ReleaseStatusDimension,
            AvailabilityLinks = BuildAvailabilityLinks(media),
            Characters = MapCharacters(media.Characters),
            RawMetadata = JsonSerializer.Serialize(releaseMetadata)
        };
    }

    private static IReadOnlyList<MediaProviderCharacterCredit> MapCharacters(AniListCharacterConnection? connection)
    {
        return (connection?.Edges ?? [])
            .Where(edge => edge.Node is { Id: > 0 } && !string.IsNullOrWhiteSpace(SelectCharacterName(edge.Node.Name)))
            .Select((edge, index) => new MediaProviderCharacterCredit
            {
                CharacterId = edge.Node!.Id.ToString(CultureInfo.InvariantCulture),
                Name = SelectCharacterName(edge.Node.Name),
                ImageUrl = edge.Node.Image?.Large ?? edge.Node.Image?.Medium,
                Role = string.Equals(edge.Role, "MAIN", StringComparison.OrdinalIgnoreCase) ? "main" : "supporting",
                ProviderUrl = edge.Node.SiteUrl,
                Order = index
            })
            .ToList();
    }

    private static string SelectCharacterName(AniListCharacterName? name)
        => name?.UserPreferred ?? name?.Full ?? name?.Native ?? string.Empty;

    private static IReadOnlyList<MediaProviderAvailabilityLink> BuildAvailabilityLinks(AniListMedia media)
    {
        var results = new List<MediaProviderAvailabilityLink>();
        var seenServiceIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var link in media.ExternalLinks ?? [])
        {
            if (!string.Equals(link.Type, "STREAMING", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(link.Site)
                || string.IsNullOrWhiteSpace(link.Url))
            {
                continue;
            }

            var serviceId = NormalizeServiceId(link.Site);
            if (!seenServiceIds.Add(serviceId))
            {
                continue;
            }

            results.Add(new MediaProviderAvailabilityLink
            {
                ServiceId = serviceId,
                DisplayName = link.Site.Trim(),
                Url = link.Url,
                AvailabilityKind = "streaming",
                Notes = link.Language,
                IconUrl = link.Icon
            });
        }

        foreach (var episode in media.StreamingEpisodes ?? [])
        {
            if (string.IsNullOrWhiteSpace(episode.Site) || string.IsNullOrWhiteSpace(episode.Url))
            {
                continue;
            }

            var serviceId = NormalizeServiceId(episode.Site);
            if (!seenServiceIds.Add(serviceId))
            {
                continue;
            }

            results.Add(new MediaProviderAvailabilityLink
            {
                ServiceId = serviceId,
                DisplayName = episode.Site.Trim(),
                Url = episode.Url,
                AvailabilityKind = "streaming",
                Notes = episode.Title
            });
        }

        return results;
    }

    private static string NormalizeServiceId(string siteName)
    {
        Span<char> buffer = stackalloc char[siteName.Length];
        var length = 0;
        var previousWasSeparator = false;

        foreach (var character in siteName.Trim())
        {
            if (char.IsLetterOrDigit(character))
            {
                buffer[length++] = char.ToLowerInvariant(character);
                previousWasSeparator = false;
                continue;
            }

            if (length > 0 && !previousWasSeparator)
            {
                buffer[length++] = '-';
                previousWasSeparator = true;
            }
        }

        if (length > 0 && buffer[length - 1] == '-')
        {
            length--;
        }

        return length == 0 ? "unknown" : new string(buffer[..length]);
    }

    private static MediaProviderMutationResult MapMutationResult(AniListSavedMediaListEntry savedEntry)
    {
        return new MediaProviderMutationResult
        {
            ProviderId = ProviderName,
            ProviderMediaId = savedEntry.Media?.Id.ToString(CultureInfo.InvariantCulture)
                ?? throw new InvalidOperationException("AniList did not return a media id for the saved entry."),
            AppliedAt = DateTimeOffset.UtcNow,
            LastRemoteUpdateAt = ToDateTimeOffset(savedEntry.UpdatedAt),
            RawStatus = savedEntry.Status,
            RawMetadata = JsonSerializer.Serialize(savedEntry)
        };
    }

    private sealed record AniListSaveMediaListEntryMutation(string Query, IReadOnlyDictionary<string, object> Variables);

    private static AniListSaveMediaListEntryMutation BuildSaveMediaListEntryMutation(
        int mediaId,
        int? progress,
        int? progressVolumes,
        string? status)
    {
        var variableDefinitions = new List<string> { "$mediaId: Int" };
        var arguments = new List<string> { "mediaId: $mediaId" };
        var variables = new Dictionary<string, object>
        {
            ["mediaId"] = mediaId
        };

        if (progress.HasValue)
        {
            variableDefinitions.Add("$progress: Int");
            arguments.Add("progress: $progress");
            variables["progress"] = progress.Value;
        }

        if (progressVolumes.HasValue)
        {
            variableDefinitions.Add("$progressVolumes: Int");
            arguments.Add("progressVolumes: $progressVolumes");
            variables["progressVolumes"] = progressVolumes.Value;
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            variableDefinitions.Add("$status: MediaListStatus");
            arguments.Add("status: $status");
            variables["status"] = status;
        }

        var query = $$"""
            mutation ({{string.Join(", ", variableDefinitions)}}) {
              SaveMediaListEntry({{string.Join(", ", arguments)}}) {
                id
                status
                progress
                progressVolumes
                updatedAt
                media {
                  id
                }
              }
            }
            """;

        return new AniListSaveMediaListEntryMutation(query, variables);
    }

    private static AniListReleaseMetadataRaw BuildReleaseMetadata(AniListMedia media, string releaseStatusDimension)
    {
        int? releasedCount = null;
        int? totalKnownCount = releaseStatusDimension switch
        {
            MediaProgressDimensions.Episode => media.Episodes,
            MediaProgressDimensions.Chapter => media.Chapters,
            _ => null
        };

        if (releaseStatusDimension == MediaProgressDimensions.Episode)
        {
            if (string.Equals(media.Status, "FINISHED", StringComparison.OrdinalIgnoreCase))
            {
                releasedCount = media.Episodes;
            }
            else if (media.NextAiringEpisode?.Episode is int nextEpisode)
            {
                releasedCount = Math.Max(0, nextEpisode - 1);
            }
        }
        else if (releaseStatusDimension == MediaProgressDimensions.Chapter && string.Equals(media.Status, "FINISHED", StringComparison.OrdinalIgnoreCase))
        {
            releasedCount = media.Chapters;
        }

        return new AniListReleaseMetadataRaw
        {
            ReleasedCount = releasedCount,
            TotalKnownCount = totalKnownCount,
            NextReleaseAt = ToDateTimeOffset(media.NextAiringEpisode?.AiringAt),
            NextReleaseLabel = media.NextAiringEpisode?.Episode is int nextReleaseEpisode
                ? $"Episode {nextReleaseEpisode}"
                : null,
            CoverImage = media.CoverImage
        };
    }

    private static string? MapMediaKind(string? type)
    {
        return type?.ToUpperInvariant() switch
        {
            "ANIME" => MediaKinds.Anime,
            "MANGA" => MediaKinds.Manga,
            _ => null
        };
    }

    private static string MapNormalizedStatus(string? status)
    {
        return status?.ToUpperInvariant() switch
        {
            "CURRENT" => MediaLibraryStatuses.Current,
            "PLANNING" => MediaLibraryStatuses.Planned,
            "PAUSED" => MediaLibraryStatuses.Paused,
            "COMPLETED" => MediaLibraryStatuses.Completed,
            "DROPPED" => MediaLibraryStatuses.Dropped,
            "REPEATING" => MediaLibraryStatuses.Current,
            _ => MediaLibraryStatuses.Unknown
        };
    }

    private static string ToAniListStatus(string status)
    {
        return status switch
        {
            MediaLibraryStatuses.Current => "CURRENT",
            MediaLibraryStatuses.Planned => "PLANNING",
            MediaLibraryStatuses.Paused => "PAUSED",
            MediaLibraryStatuses.Completed => "COMPLETED",
            MediaLibraryStatuses.Dropped => "DROPPED",
            _ => throw new InvalidOperationException($"Media status '{status}' cannot be written to AniList.")
        };
    }

    private static string ToAniListMediaType(string mediaKind)
    {
        return mediaKind switch
        {
            MediaKinds.Anime => "ANIME",
            MediaKinds.Manga => "MANGA",
            _ => throw new InvalidOperationException($"Media kind '{mediaKind}' is not supported by the AniList provider.")
        };
    }

    private static (string PrimaryProgressDimension, string ReleaseStatusDimension) GetDimensions(string mediaKind)
    {
        return mediaKind switch
        {
            MediaKinds.Anime => (MediaProgressDimensions.Episode, MediaProgressDimensions.Episode),
            MediaKinds.Manga => (MediaProgressDimensions.Chapter, MediaProgressDimensions.Chapter),
            _ => (MediaProgressDimensions.Unavailable, MediaProgressDimensions.Unavailable)
        };
    }

    private static string SelectCanonicalTitle(AniListTitle? title)
    {
        return title?.English
            ?? title?.Romaji
            ?? title?.Native
            ?? "Unknown title";
    }

    private static string? SelectPosterUrl(AniListCoverImage? coverImage)
    {
        return coverImage?.ExtraLarge
            ?? coverImage?.Large
            ?? coverImage?.Medium;
    }

    private static DateTimeOffset? ToDateTimeOffset(long? unixTimestamp)
    {
        return unixTimestamp is null or <= 0
            ? null
            : DateTimeOffset.FromUnixTimeSeconds(unixTimestamp.Value);
    }

    private const string ViewerQuery = """
        query {
          Viewer {
            id
            name
          }
        }
        """;

    private const string MediaListCollectionQuery = """
        query ($userId: Int, $type: MediaType) {
          MediaListCollection(userId: $userId, type: $type) {
            lists {
              name
              entries {
                id
                status
                progress
                progressVolumes
                updatedAt
                media {
                  id
                  type
                  format
                  status
                  siteUrl
                  description(asHtml: true)
                  episodes
                  chapters
                  volumes
                  bannerImage
                  startDate {
                    year
                  }
                  title {
                    romaji
                    english
                    native
                  }
                  coverImage {
                    extraLarge
                    medium
                    large
                  }
                  nextAiringEpisode {
                    episode
                    airingAt
                  }
                }
              }
            }
          }
        }
        """;

    private const string SearchMediaQuery = """
        query ($search: String, $perPage: Int, $type: MediaType) {
          Page(page: 1, perPage: $perPage) {
            media(search: $search, type: $type) {
              id
              type
              format
              status
              siteUrl
              description(asHtml: true)
              episodes
              chapters
              volumes
              bannerImage
              startDate {
                year
              }
              title {
                romaji
                english
                native
              }
              coverImage {
                extraLarge
                medium
                large
              }
              nextAiringEpisode {
                episode
                airingAt
              }
            }
          }
        }
        """;

    private const string MediaDetailsQuery = """
        query ($id: Int) {
          Media(id: $id) {
            id
            type
            format
            status
            siteUrl
            description(asHtml: true)
            episodes
            chapters
            volumes
            bannerImage
            startDate {
                year
            }
            title {
                romaji
                english
                native
            }
            coverImage {
                extraLarge
                medium
                large
            }
            nextAiringEpisode {
                episode
                airingAt
            }
            externalLinks {
                url
                site
                type
                language
                icon
            }
            streamingEpisodes {
                title
                url
                site
            }
            characters(sort: [ROLE, RELEVANCE], perPage: 25) {
                edges {
                    role
                    node {
                        id
                        siteUrl
                        name { full native userPreferred }
                        image { large medium }
                    }
                }
            }
          }
        }
        """;

}

public class AniListViewerData
{
    [JsonPropertyName("Viewer")]
    public AniListViewer? Viewer { get; set; }
}

public class AniListViewer
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class AniListPageData
{
    [JsonPropertyName("Page")]
    public AniListPage? Page { get; set; }
}

public class AniListPage
{
    [JsonPropertyName("media")]
    public List<AniListMedia>? Media { get; set; }
}

public class AniListMediaData
{
    [JsonPropertyName("Media")]
    public AniListMedia? Media { get; set; }
}

public class AniListMediaListCollectionData
{
    [JsonPropertyName("MediaListCollection")]
    public AniListMediaListCollection? MediaListCollection { get; set; }
}

public class AniListMediaListCollection
{
    [JsonPropertyName("lists")]
    public List<AniListMediaListGroup>? Lists { get; set; }
}

public class AniListMediaListGroup
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("entries")]
    public List<AniListMediaListEntry>? Entries { get; set; }
}

public class AniListMediaListEntry
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("progress")]
    public int? Progress { get; set; }

    [JsonPropertyName("progressVolumes")]
    public int? ProgressVolumes { get; set; }

    [JsonPropertyName("updatedAt")]
    public long? UpdatedAt { get; set; }

    [JsonPropertyName("media")]
    public AniListMedia? Media { get; set; }
}

public class AniListSavedMediaListEntryData
{
    [JsonPropertyName("SaveMediaListEntry")]
    public AniListSavedMediaListEntry? SaveMediaListEntry { get; set; }
}

public class AniListSavedMediaListEntry
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("progress")]
    public int? Progress { get; set; }

    [JsonPropertyName("progressVolumes")]
    public int? ProgressVolumes { get; set; }

    [JsonPropertyName("updatedAt")]
    public long? UpdatedAt { get; set; }

    [JsonPropertyName("media")]
    public AniListMedia? Media { get; set; }
}

public class AniListMedia
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("format")]
    public string? Format { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("siteUrl")]
    public string? SiteUrl { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("episodes")]
    public int? Episodes { get; set; }

    [JsonPropertyName("chapters")]
    public int? Chapters { get; set; }

    [JsonPropertyName("volumes")]
    public int? Volumes { get; set; }

    [JsonPropertyName("bannerImage")]
    public string? BannerImage { get; set; }

    [JsonPropertyName("startDate")]
    public AniListStartDate? StartDate { get; set; }

    [JsonPropertyName("title")]
    public AniListTitle? Title { get; set; }

    [JsonPropertyName("coverImage")]
    public AniListCoverImage? CoverImage { get; set; }

    [JsonPropertyName("nextAiringEpisode")]
    public AniListNextAiringEpisode? NextAiringEpisode { get; set; }

    [JsonPropertyName("externalLinks")]
    public List<AniListExternalLink>? ExternalLinks { get; set; }

    [JsonPropertyName("streamingEpisodes")]
    public List<AniListStreamingEpisode>? StreamingEpisodes { get; set; }

    [JsonPropertyName("characters")]
    public AniListCharacterConnection? Characters { get; set; }
}

public class AniListCharacterConnection
{
    [JsonPropertyName("edges")]
    public List<AniListCharacterEdge>? Edges { get; set; }
}

public class AniListCharacterEdge
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }
    [JsonPropertyName("node")]
    public AniListCharacter? Node { get; set; }
}

public class AniListCharacter
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    [JsonPropertyName("siteUrl")]
    public string? SiteUrl { get; set; }
    [JsonPropertyName("name")]
    public AniListCharacterName? Name { get; set; }
    [JsonPropertyName("image")]
    public AniListCharacterImage? Image { get; set; }
}

public class AniListCharacterName
{
    [JsonPropertyName("full")]
    public string? Full { get; set; }
    [JsonPropertyName("native")]
    public string? Native { get; set; }
    [JsonPropertyName("userPreferred")]
    public string? UserPreferred { get; set; }
}

public class AniListCharacterImage
{
    [JsonPropertyName("large")]
    public string? Large { get; set; }
    [JsonPropertyName("medium")]
    public string? Medium { get; set; }
}

public class AniListExternalLink
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("site")]
    public string? Site { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }
}

public class AniListStreamingEpisode
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("site")]
    public string? Site { get; set; }
}

public class AniListStartDate
{
    [JsonPropertyName("year")]
    public int? Year { get; set; }
}

public class AniListTitle
{
    [JsonPropertyName("romaji")]
    public string? Romaji { get; set; }

    [JsonPropertyName("english")]
    public string? English { get; set; }

    [JsonPropertyName("native")]
    public string? Native { get; set; }
}

public class AniListCoverImage
{
    [JsonPropertyName("extraLarge")]
    public string? ExtraLarge { get; set; }

    [JsonPropertyName("medium")]
    public string? Medium { get; set; }

    [JsonPropertyName("large")]
    public string? Large { get; set; }
}

public class AniListNextAiringEpisode
{
    [JsonPropertyName("episode")]
    public int? Episode { get; set; }

    [JsonPropertyName("airingAt")]
    public long? AiringAt { get; set; }
}

public class AniListReleaseMetadataRaw
{
    [JsonPropertyName("releasedCount")]
    public int? ReleasedCount { get; set; }

    [JsonPropertyName("totalKnownCount")]
    public int? TotalKnownCount { get; set; }

    [JsonPropertyName("nextReleaseAt")]
    public DateTimeOffset? NextReleaseAt { get; set; }

    [JsonPropertyName("nextReleaseLabel")]
    public string? NextReleaseLabel { get; set; }

    [JsonPropertyName("coverImage")]
    public AniListCoverImage? CoverImage { get; set; }
}
