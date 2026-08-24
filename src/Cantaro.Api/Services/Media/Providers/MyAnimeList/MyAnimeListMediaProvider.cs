using System.Globalization;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

public sealed class MyAnimeListMediaProvider(
    ApplicationDbContext dbContext,
    MyAnimeListApiClient apiClient,
    TokenEncryptionService tokenEncryptionService,
    MyAnimeListTokenRefreshGate tokenRefreshGate,
    ILogger<MyAnimeListMediaProvider> logger) : IMediaProvider
{
    private const string ProviderName = "myanimelist";
    private const string AnimeFields = "alternative_titles,start_date,synopsis,media_type,status,num_episodes,main_picture";
    private const string MangaFields = "alternative_titles,start_date,synopsis,media_type,status,num_chapters,num_volumes,main_picture";
    private static readonly TimeSpan AccessTokenRefreshMargin = TimeSpan.FromMinutes(1);
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly MyAnimeListApiClient _apiClient = apiClient;
    private readonly TokenEncryptionService _tokenEncryptionService = tokenEncryptionService;
    private readonly MyAnimeListTokenRefreshGate _tokenRefreshGate = tokenRefreshGate;
    private readonly ILogger<MyAnimeListMediaProvider> _logger = logger;

    public string ProviderId => ProviderName;

    // MAL currently supports only plain PKCE, so the challenge is the verifier itself.
    public string BuildCodeChallenge(string codeVerifier) => codeVerifier;

    public string GetAuthorizationUrl(string redirectUri, string state, string codeChallenge)
        => _apiClient.BuildAuthorizationUrl(redirectUri, state, codeChallenge);

    public Task<ConnectedServiceAccount?> GetConnectedAccountAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.ConnectedServiceAccounts.FirstOrDefaultAsync(
            account => account.UserId == userId && account.Service == ProviderName,
            cancellationToken);
    }

    public async Task<ConnectedServiceAccount> ExchangeCodeAndSaveAsync(
        int userId,
        string authorizationCode,
        string redirectUri,
        string codeVerifier,
        CancellationToken cancellationToken)
    {
        var token = await _apiClient.ExchangeCodeAsync(
            authorizationCode,
            redirectUri,
            codeVerifier,
            cancellationToken);
        var viewer = await _apiClient.GetAsync<MyAnimeListUser>(
            "/users/@me",
            token.AccessToken,
            query: null,
            cancellationToken);
        if (viewer.Id <= 0)
        {
            throw new InvalidOperationException("MyAnimeList did not return viewer information.");
        }

        var now = DateTime.UtcNow;
        var account = await GetConnectedAccountAsync(userId, cancellationToken);
        if (account is null)
        {
            account = new ConnectedServiceAccount
            {
                UserId = userId,
                Service = ProviderName,
                ExternalAccountId = viewer.Id.ToString(CultureInfo.InvariantCulture),
                DisplayName = viewer.Name,
                CreatedAt = now
            };
            _dbContext.ConnectedServiceAccounts.Add(account);
        }

        account.ExternalAccountId = viewer.Id.ToString(CultureInfo.InvariantCulture);
        account.DisplayName = viewer.Name;
        account.EncryptedAccessToken = _tokenEncryptionService.Encrypt(token.AccessToken);
        if (!string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            account.EncryptedRefreshToken = _tokenEncryptionService.Encrypt(token.RefreshToken);
            account.RefreshTokenExpiresAt = now.AddMonths(1);
        }
        account.Scopes = "write:users";
        account.TokenExpiresAt = now.AddSeconds(token.ExpiresIn);
        account.ConnectionState = "connected";
        account.ReconnectRequiredAt = null;
        account.ReconnectReason = null;
        account.TokenVersion++;
        account.UpdatedAt = now;

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
        var bindings = await _dbContext.MediaLibraryProviderBindings
            .Include(binding => binding.MediaLibraryEntry)
            .Include(binding => binding.MediaProviderLink)
            .Where(binding => binding.MediaLibraryEntry!.UserId == userId
                && binding.MediaProviderLink!.Provider == ProviderName
                && binding.ConnectedServiceAccountId == account.Id)
            .ToListAsync(cancellationToken);
        foreach (var binding in bindings)
        {
            binding.ConnectedServiceAccountId = null;
            binding.UpdatedAt = now;
            if (binding.MediaLibraryEntry is { } entry)
            {
                entry.LastMutationSource = MediaMutationSources.ProviderDisconnect;
                entry.UpdatedAt = now;
            }
        }

        _dbContext.ConnectedServiceAccounts.Remove(account);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<MediaProviderLibraryImportResult> ImportLibraryAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        var account = await RequireConnectedAccountAsync(userId, cancellationToken);
        var accessToken = await ResolveAccessTokenAsync(account, cancellationToken);
        var anime = await ReadAllPagesAsync<MyAnimeListAnimeListEdge>(
            "/users/@me/animelist",
            accessToken,
            $"{AnimeFields},list_status",
            cancellationToken);
        var manga = await ReadAllPagesAsync<MyAnimeListMangaListEdge>(
            "/users/@me/mangalist",
            accessToken,
            $"{MangaFields},list_status",
            cancellationToken);

        return new MediaProviderLibraryImportResult
        {
            ProviderId = ProviderName,
            ImportedAt = DateTimeOffset.UtcNow,
            Items = anime.Select(MapAnimeLibraryItem)
                .Concat(manga.Select(MapMangaLibraryItem))
                .Where(item => item is not null)
                .Cast<MediaProviderLibraryItem>()
                .ToList()
        };
    }

    public async Task<IReadOnlyList<MediaProviderSearchResult>> SearchAsync(
        int userId,
        MediaCatalogSearchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Query);
        var kinds = request.MediaKinds.Count == 0
            ? [MediaKinds.Anime, MediaKinds.Manga]
            : request.MediaKinds
                .Where(kind => kind is MediaKinds.Anime or MediaKinds.Manga)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        var perKindLimit = Math.Max(1, request.Limit / Math.Max(1, kinds.Length));
        var results = new List<MediaProviderSearchResult>();

        foreach (var kind in kinds)
        {
            var path = kind == MediaKinds.Anime ? "/anime" : "/manga";
            var fields = kind == MediaKinds.Anime ? AnimeFields : MangaFields;
            var page = await _apiClient.GetAsync<MyAnimeListPage<MyAnimeListSearchEdge>>(
                path,
                accessToken: null,
                new Dictionary<string, string?>
                {
                    ["q"] = request.Query,
                    ["limit"] = Math.Min(100, perKindLimit).ToString(CultureInfo.InvariantCulture),
                    ["fields"] = fields
                },
                cancellationToken);
            results.AddRange(page.Data
                .Where(edge => edge.Node is { Id: > 0 })
                .Select(edge => MapSearchResult(edge.Node!, kind)));
        }

        return results.Take(request.Limit).ToList();
    }

    public async Task<MediaProviderTitleDetails?> GetTitleDetailsAsync(
        int userId,
        string providerMediaId,
        CancellationToken cancellationToken)
    {
        var identity = ParseProviderMediaId(providerMediaId);
        try
        {
            var media = await _apiClient.GetAsync<MyAnimeListMedia>(
                $"/{identity.PathKind}/{identity.Id}",
                accessToken: null,
                new Dictionary<string, string?>
                {
                    ["fields"] = identity.MediaKind == MediaKinds.Anime ? AnimeFields : MangaFields
                },
                cancellationToken);
            return MapTitleDetails(media, identity.MediaKind);
        }
        catch (MyAnimeListRequestException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<MediaProviderMutationResult> UpdateProgressAsync(
        int userId,
        MediaProgressUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var identity = ParseProviderMediaId(request.ProviderMediaId);
        var form = new Dictionary<string, string?>();
        if (identity.MediaKind == MediaKinds.Anime)
        {
            if (request.ProgressEpisodes is not { } episodes)
            {
                throw new InvalidOperationException("MyAnimeList anime progress requires an episode count.");
            }
            form["num_watched_episodes"] = episodes.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            if (request.ProgressChapters is { } chapters)
            {
                form["num_chapters_read"] = chapters.ToString(CultureInfo.InvariantCulture);
            }
            if (request.ProgressVolumes is { } volumes)
            {
                form["num_volumes_read"] = volumes.ToString(CultureInfo.InvariantCulture);
            }
            if (form.Count == 0)
            {
                throw new InvalidOperationException("MyAnimeList manga progress requires a chapter or volume count.");
            }
        }

        return await UpdateListStatusAsync(userId, identity, form, cancellationToken);
    }

    public async Task<MediaProviderMutationResult> UpdateStatusAsync(
        int userId,
        MediaStatusUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var identity = ParseProviderMediaId(request.ProviderMediaId);
        var isAnime = identity.MediaKind == MediaKinds.Anime;
        var form = new Dictionary<string, string?>
        {
            ["status"] = ToMyAnimeListStatus(request.Status, isAnime),
            [isAnime ? "is_rewatching" : "is_rereading"] = request.Status == MediaLibraryStatuses.Repeating
                ? "true"
                : "false"
        };

        return await UpdateListStatusAsync(userId, identity, form, cancellationToken);
    }

    public async Task<MediaProviderMutationResult> UpdateScoreAsync(
        int userId,
        MediaScoreUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var identity = ParseProviderMediaId(request.ProviderMediaId);
        var score = request.Score is null
            ? 0
            : Math.Clamp(
                decimal.ToInt32(decimal.Round(request.Score.Value / 10m, 0, MidpointRounding.AwayFromZero)),
                1,
                10);
        return await UpdateListStatusAsync(
            userId,
            identity,
            new Dictionary<string, string?>
            {
                ["score"] = score.ToString(CultureInfo.InvariantCulture)
            },
            cancellationToken);
    }

    public async Task<MediaProviderMutationResult> SyncLibraryStateAsync(
        int userId,
        MediaLibraryStateSyncRequest request,
        CancellationToken cancellationToken)
    {
        var identity = ParseProviderMediaId(request.ProviderMediaId);
        var isAnime = identity.MediaKind == MediaKinds.Anime;
        var score = request.Score is null
            ? 0
            : Math.Clamp(
                decimal.ToInt32(decimal.Round(request.Score.Value / 10m, 0, MidpointRounding.AwayFromZero)),
                1,
                10);
        var form = new Dictionary<string, string?>
        {
            ["status"] = ToMyAnimeListStatus(request.Status, isAnime),
            [isAnime ? "is_rewatching" : "is_rereading"] = request.Status == MediaLibraryStatuses.Repeating
                ? "true"
                : "false",
            ["score"] = score.ToString(CultureInfo.InvariantCulture)
        };

        if (isAnime && request.ProgressEpisodes is { } episodes)
        {
            form["num_watched_episodes"] = episodes.ToString(CultureInfo.InvariantCulture);
        }
        else if (!isAnime)
        {
            if (request.ProgressChapters is { } chapters)
            {
                form["num_chapters_read"] = chapters.ToString(CultureInfo.InvariantCulture);
            }
            if (request.ProgressVolumes is { } volumes)
            {
                form["num_volumes_read"] = volumes.ToString(CultureInfo.InvariantCulture);
            }
        }

        return await UpdateListStatusAsync(userId, identity, form, cancellationToken);
    }

    public async Task<MediaReleaseMetadata?> GetReleaseMetadataAsync(
        int userId,
        string providerMediaId,
        CancellationToken cancellationToken)
    {
        var details = await GetTitleDetailsAsync(userId, providerMediaId, cancellationToken);
        return details is null
            ? null
            : new MediaReleaseMetadata
            {
                ProviderId = ProviderName,
                ProviderMediaId = providerMediaId,
                ReleaseStatusDimension = details.ReleaseStatusDimension,
                ReleasedCount = details.ReleasedCount,
                TotalKnownCount = details.TotalKnownCount,
                NextReleaseAt = details.NextReleaseAt,
                NextReleaseLabel = details.NextReleaseLabel
            };
    }

    private async Task<MediaProviderMutationResult> UpdateListStatusAsync(
        int userId,
        MyAnimeListIdentity identity,
        IReadOnlyDictionary<string, string?> form,
        CancellationToken cancellationToken)
    {
        var account = await RequireConnectedAccountAsync(userId, cancellationToken);
        var accessToken = await ResolveAccessTokenAsync(account, cancellationToken);
        DateTimeOffset? updatedAt;
        if (identity.MediaKind == MediaKinds.Anime)
        {
            var result = await _apiClient.PutFormAsync<MyAnimeListAnimeListStatus>(
                $"/anime/{identity.Id}/my_list_status",
                accessToken,
                form,
                cancellationToken);
            updatedAt = result.UpdatedAt;
        }
        else
        {
            var result = await _apiClient.PutFormAsync<MyAnimeListMangaListStatus>(
                $"/manga/{identity.Id}/my_list_status",
                accessToken,
                form,
                cancellationToken);
            updatedAt = result.UpdatedAt;
        }

        return new MediaProviderMutationResult
        {
            ProviderId = ProviderName,
            ProviderMediaId = identity.ProviderMediaId,
            AppliedAt = DateTimeOffset.UtcNow,
            LastRemoteUpdateAt = updatedAt
        };
    }

    private async Task<IReadOnlyList<T>> ReadAllPagesAsync<T>(
        string path,
        string accessToken,
        string fields,
        CancellationToken cancellationToken)
    {
        var items = new List<T>();
        string? next = path;
        IReadOnlyDictionary<string, string?>? query = new Dictionary<string, string?>
        {
            ["limit"] = "1000",
            ["fields"] = fields,
            ["sort"] = "list_updated_at"
        };
        while (!string.IsNullOrWhiteSpace(next))
        {
            var page = await _apiClient.GetAsync<MyAnimeListPage<T>>(
                next,
                accessToken,
                query,
                cancellationToken);
            items.AddRange(page.Data);
            next = page.Paging?.Next;
            query = null;
        }

        return items;
    }

    private async Task<ConnectedServiceAccount> RequireConnectedAccountAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        return await GetConnectedAccountAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException("MyAnimeList account not connected.");
    }

    private async Task<string> ResolveAccessTokenAsync(
        ConnectedServiceAccount account,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(account.EncryptedAccessToken)
            && (account.TokenExpiresAt is null || account.TokenExpiresAt > now.Add(AccessTokenRefreshMargin)))
        {
            return _tokenEncryptionService.Decrypt(account.EncryptedAccessToken);
        }

        using var refreshLease = await _tokenRefreshGate.AcquireAsync(account.Id, cancellationToken);
        await _dbContext.Entry(account).ReloadAsync(cancellationToken);
        now = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(account.EncryptedAccessToken)
            && (account.TokenExpiresAt is null || account.TokenExpiresAt > now.Add(AccessTokenRefreshMargin)))
        {
            return _tokenEncryptionService.Decrypt(account.EncryptedAccessToken);
        }

        if (string.IsNullOrWhiteSpace(account.EncryptedRefreshToken)
            || account.RefreshTokenExpiresAt is { } refreshExpiresAt && refreshExpiresAt <= now)
        {
            throw new InvalidOperationException("MyAnimeList access expired. Reconnect your MyAnimeList account.");
        }

        var refreshToken = _tokenEncryptionService.Decrypt(account.EncryptedRefreshToken);
        var token = await _apiClient.RefreshAccessTokenAsync(refreshToken, cancellationToken);
        account.EncryptedAccessToken = _tokenEncryptionService.Encrypt(token.AccessToken);
        if (!string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            account.EncryptedRefreshToken = _tokenEncryptionService.Encrypt(token.RefreshToken);
            account.RefreshTokenExpiresAt = now.AddMonths(1);
        }
        account.TokenExpiresAt = now.AddSeconds(token.ExpiresIn);
        account.TokenVersion++;
        account.UpdatedAt = now;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Refreshed MyAnimeList access token for connected account {AccountId}.", account.Id);
        return token.AccessToken;
    }

    private static MediaProviderLibraryItem? MapAnimeLibraryItem(MyAnimeListAnimeListEdge edge)
    {
        if (edge.Node is not { Id: > 0 } media || edge.ListStatus is null)
        {
            return null;
        }

        return MapLibraryItem(
            media,
            MediaKinds.Anime,
            edge.ListStatus.IsRewatching
                ? MediaLibraryStatuses.Repeating
                : MapStatus(edge.ListStatus.Status, isAnime: true),
            edge.ListStatus.Score,
            edge.ListStatus.NumEpisodesWatched,
            progressChapters: null,
            progressVolumes: null,
            edge.ListStatus.UpdatedAt);
    }

    private static MediaProviderLibraryItem? MapMangaLibraryItem(MyAnimeListMangaListEdge edge)
    {
        if (edge.Node is not { Id: > 0 } media || edge.ListStatus is null)
        {
            return null;
        }

        return MapLibraryItem(
            media,
            MediaKinds.Manga,
            edge.ListStatus.IsRereading
                ? MediaLibraryStatuses.Repeating
                : MapStatus(edge.ListStatus.Status, isAnime: false),
            edge.ListStatus.Score,
            progressEpisodes: null,
            edge.ListStatus.NumChaptersRead,
            edge.ListStatus.NumVolumesRead,
            edge.ListStatus.UpdatedAt);
    }

    private static MediaProviderLibraryItem MapLibraryItem(
        MyAnimeListMedia media,
        string mediaKind,
        string status,
        int score,
        int? progressEpisodes,
        int? progressChapters,
        int? progressVolumes,
        DateTimeOffset? updatedAt)
    {
        var metadata = GetReleaseMetadata(media, mediaKind);
        return new MediaProviderLibraryItem
        {
            ProviderMediaId = FormatProviderMediaId(mediaKind, media.Id),
            Title = SelectCanonicalTitle(media),
            NativeTitle = NullIfWhiteSpace(media.AlternativeTitles?.Japanese),
            OriginalTitle = NullIfWhiteSpace(media.AlternativeTitles?.Japanese),
            Synonyms = NormalizeSynonyms(media),
            MediaKind = mediaKind,
            Synopsis = media.Synopsis,
            Format = MapFormat(media.MediaType),
            PosterUrl = media.MainPicture?.Large ?? media.MainPicture?.Medium,
            ExternalUrl = BuildExternalUrl(mediaKind, media.Id),
            StartYear = ParseStartYear(media.StartDate),
            EpisodeCount = mediaKind == MediaKinds.Anime ? NullIfZero(media.NumEpisodes) : null,
            ChapterCount = mediaKind == MediaKinds.Manga ? NullIfZero(media.NumChapters) : null,
            VolumeCount = mediaKind == MediaKinds.Manga ? NullIfZero(media.NumVolumes) : null,
            ReleasedCount = metadata.ReleasedCount,
            TotalKnownCount = metadata.TotalKnownCount,
            Status = status,
            Score = score is > 0 and <= 10 ? score * 10m : null,
            ProgressEpisodes = progressEpisodes,
            ProgressChapters = progressChapters,
            ProgressVolumes = progressVolumes,
            PrimaryProgressDimension = mediaKind == MediaKinds.Anime
                ? MediaProgressDimensions.Episode
                : MediaProgressDimensions.Chapter,
            ReleaseStatusDimension = mediaKind == MediaKinds.Anime
                ? MediaProgressDimensions.Episode
                : MediaProgressDimensions.Chapter,
            LastRemoteUpdateAt = updatedAt
        };
    }

    private static MediaProviderSearchResult MapSearchResult(MyAnimeListMedia media, string mediaKind)
    {
        return new MediaProviderSearchResult
        {
            ProviderId = ProviderName,
            ProviderMediaId = FormatProviderMediaId(mediaKind, media.Id),
            Title = SelectCanonicalTitle(media),
            NativeTitle = NullIfWhiteSpace(media.AlternativeTitles?.Japanese),
            Synonyms = NormalizeSynonyms(media),
            MediaKind = mediaKind,
            Synopsis = media.Synopsis,
            PosterUrl = media.MainPicture?.Large ?? media.MainPicture?.Medium,
            StartYear = ParseStartYear(media.StartDate),
            EpisodeCount = mediaKind == MediaKinds.Anime ? NullIfZero(media.NumEpisodes) : null,
            ChapterCount = mediaKind == MediaKinds.Manga ? NullIfZero(media.NumChapters) : null,
            VolumeCount = mediaKind == MediaKinds.Manga ? NullIfZero(media.NumVolumes) : null,
            PrimaryProgressDimension = mediaKind == MediaKinds.Anime
                ? MediaProgressDimensions.Episode
                : MediaProgressDimensions.Chapter,
            ReleaseStatusDimension = mediaKind == MediaKinds.Anime
                ? MediaProgressDimensions.Episode
                : MediaProgressDimensions.Chapter
        };
    }

    private static MediaProviderTitleDetails MapTitleDetails(MyAnimeListMedia media, string mediaKind)
    {
        var metadata = GetReleaseMetadata(media, mediaKind);
        return new MediaProviderTitleDetails
        {
            ProviderId = ProviderName,
            ProviderMediaId = FormatProviderMediaId(mediaKind, media.Id),
            Title = SelectCanonicalTitle(media),
            NativeTitle = NullIfWhiteSpace(media.AlternativeTitles?.Japanese),
            Synonyms = NormalizeSynonyms(media),
            MediaKind = mediaKind,
            Synopsis = media.Synopsis,
            Format = MapFormat(media.MediaType),
            PosterUrl = media.MainPicture?.Large ?? media.MainPicture?.Medium,
            StartYear = ParseStartYear(media.StartDate),
            EpisodeCount = mediaKind == MediaKinds.Anime ? NullIfZero(media.NumEpisodes) : null,
            ChapterCount = mediaKind == MediaKinds.Manga ? NullIfZero(media.NumChapters) : null,
            VolumeCount = mediaKind == MediaKinds.Manga ? NullIfZero(media.NumVolumes) : null,
            ReleasedCount = metadata.ReleasedCount,
            TotalKnownCount = metadata.TotalKnownCount,
            PrimaryProgressDimension = mediaKind == MediaKinds.Anime
                ? MediaProgressDimensions.Episode
                : MediaProgressDimensions.Chapter,
            ReleaseStatusDimension = mediaKind == MediaKinds.Anime
                ? MediaProgressDimensions.Episode
                : MediaProgressDimensions.Chapter
        };
    }

    private static (int? ReleasedCount, int? TotalKnownCount) GetReleaseMetadata(
        MyAnimeListMedia media,
        string mediaKind)
    {
        var totalKnownCount = mediaKind == MediaKinds.Anime
            ? NullIfZero(media.NumEpisodes)
            : NullIfZero(media.NumChapters);
        var isFinished = mediaKind == MediaKinds.Anime
            ? string.Equals(media.Status, "finished_airing", StringComparison.OrdinalIgnoreCase)
            : string.Equals(media.Status, "finished", StringComparison.OrdinalIgnoreCase);
        return (isFinished ? totalKnownCount : null, totalKnownCount);
    }

    private static string MapStatus(string? status, bool isAnime)
    {
        return status?.ToLowerInvariant() switch
        {
            "watching" or "reading" => MediaLibraryStatuses.Current,
            "plan_to_watch" or "plan_to_read" => MediaLibraryStatuses.Planned,
            "on_hold" => MediaLibraryStatuses.Paused,
            "completed" => MediaLibraryStatuses.Completed,
            "dropped" => MediaLibraryStatuses.Dropped,
            _ => MediaLibraryStatuses.Unknown
        };
    }

    private static string ToMyAnimeListStatus(string status, bool isAnime)
    {
        return status switch
        {
            MediaLibraryStatuses.Current => isAnime ? "watching" : "reading",
            MediaLibraryStatuses.Planned => isAnime ? "plan_to_watch" : "plan_to_read",
            MediaLibraryStatuses.Paused => "on_hold",
            MediaLibraryStatuses.Completed => "completed",
            MediaLibraryStatuses.Dropped => "dropped",
            MediaLibraryStatuses.Repeating => isAnime ? "watching" : "reading",
            _ => throw new InvalidOperationException(
                $"Media status '{status}' cannot be written to MyAnimeList.")
        };
    }

    private static MyAnimeListIdentity ParseProviderMediaId(string providerMediaId)
    {
        var parts = providerMediaId.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || parts[0] is not ("anime" or "manga")
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            || id <= 0)
        {
            throw new InvalidOperationException(
                $"MyAnimeList media id '{providerMediaId}' is invalid; expected 'anime:<id>' or 'manga:<id>'.");
        }

        return new MyAnimeListIdentity(
            parts[0] == "anime" ? MediaKinds.Anime : MediaKinds.Manga,
            parts[0],
            id);
    }

    private static string FormatProviderMediaId(string mediaKind, int id)
        => $"{(mediaKind == MediaKinds.Manga ? "manga" : "anime")}:{id}";

    private static string BuildExternalUrl(string mediaKind, int id)
        => $"https://myanimelist.net/{(mediaKind == MediaKinds.Manga ? "manga" : "anime")}/{id}";

    private static string SelectCanonicalTitle(MyAnimeListMedia media)
        => NullIfWhiteSpace(media.AlternativeTitles?.English)
            ?? NullIfWhiteSpace(media.Title)
            ?? NullIfWhiteSpace(media.AlternativeTitles?.Japanese)
            ?? "Unknown title";

    private static IReadOnlyList<string> NormalizeSynonyms(MyAnimeListMedia media)
    {
        var canonical = SelectCanonicalTitle(media);
        return (media.AlternativeTitles?.Synonyms ?? [])
            .Append(media.Title)
            .Append(media.AlternativeTitles?.Japanese)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Where(value => !string.Equals(value, canonical, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? MapFormat(string? mediaType)
    {
        return mediaType?.ToLowerInvariant() switch
        {
            "tv" => MediaFormats.Tv,
            "ova" => MediaFormats.Ova,
            "movie" => MediaFormats.Movie,
            "special" => MediaFormats.Special,
            "ona" => MediaFormats.Ona,
            "music" => MediaFormats.Music,
            "manga" => MediaFormats.Manga,
            "novel" => MediaFormats.Novel,
            "one_shot" => MediaFormats.OneShot,
            "unknown" or null or "" => null,
            var value => value
        };
    }

    private static int? ParseStartYear(string? startDate)
    {
        if (string.IsNullOrWhiteSpace(startDate) || startDate.Length < 4)
        {
            return null;
        }

        return int.TryParse(startDate.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            ? year
            : null;
    }

    private static int? NullIfZero(int value) => value > 0 ? value : null;

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record MyAnimeListIdentity(string MediaKind, string PathKind, int Id)
    {
        public string ProviderMediaId => $"{PathKind}:{Id}";
    }
}
