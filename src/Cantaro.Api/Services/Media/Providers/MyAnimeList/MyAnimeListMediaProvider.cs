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

    public bool SupportsMediaKind(string mediaKind) => mediaKind is MediaKinds.Anime or MediaKinds.Manga;

    // MAL currently supports only plain PKCE, so the challenge is the verifier itself.
    public string BuildCodeChallenge(string codeVerifier) => codeVerifier;

    public string GetAuthorizationUrl(string redirectUri, string state, string codeChallenge)
        => _apiClient.BuildAuthorizationUrl(redirectUri, state, codeChallenge);

    public async Task<ConnectedServiceAccount?> GetConnectedAccountAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        var account = await _dbContext.ConnectedServiceAccounts.FirstOrDefaultAsync(
            account => account.UserId == userId && account.Service == ProviderName,
            cancellationToken);
        if (account is { ConnectionState: "connected", RefreshTokenExpiresAt: { } expires } && expires <= DateTime.UtcNow)
        {
            await ProviderReconnectState.MarkAsync(_dbContext, account, "refresh_token_expired", cancellationToken);
            await _dbContext.Entry(account).ReloadAsync(cancellationToken);
        }
        return account;
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
        if (account is not null && account.ExternalAccountId != viewer.Id.ToString(CultureInfo.InvariantCulture))
            throw new PlatformApiException("platform_account_changed",
                "Reconnect the same MyAnimeList account to resume pending work.", 409);
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

        if (string.IsNullOrWhiteSpace(token.RefreshToken)
            && (account.ConnectionState != "connected" || string.IsNullOrWhiteSpace(account.EncryptedRefreshToken)
                || account.RefreshTokenExpiresAt <= now
                || account.ExternalAccountId != viewer.Id.ToString(CultureInfo.InvariantCulture)))
            throw new PlatformApiException("myanimelist_refresh_token_missing",
                "MyAnimeList did not provide a refresh token. Reconnect MyAnimeList to continue.", 409);

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
        var expectedVersion = account.TokenVersion;
        account.UpdatedAt = now;
        if (_dbContext.Entry(account).State == EntityState.Added)
        {
            account.TokenVersion++;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        else
        {
            var updated = await _dbContext.ConnectedServiceAccounts.Where(item => item.Id == account.Id
                && (!string.IsNullOrWhiteSpace(token.RefreshToken)
                    || (item.TokenVersion == expectedVersion && item.ConnectionState == "connected")))
                .ExecuteUpdateAsync(set => set
                    .SetProperty(item => item.ExternalAccountId, account.ExternalAccountId)
                    .SetProperty(item => item.DisplayName, account.DisplayName)
                    .SetProperty(item => item.EncryptedAccessToken, account.EncryptedAccessToken)
                    .SetProperty(item => item.EncryptedRefreshToken, account.EncryptedRefreshToken)
                    .SetProperty(item => item.RefreshTokenExpiresAt, account.RefreshTokenExpiresAt)
                    .SetProperty(item => item.TokenExpiresAt, account.TokenExpiresAt)
                    .SetProperty(item => item.Scopes, account.Scopes)
                    .SetProperty(item => item.ConnectionState, "connected")
                    .SetProperty(item => item.ReconnectRequiredAt, (DateTime?)null)
                    .SetProperty(item => item.ReconnectReason, (string?)null)
                    .SetProperty(item => item.TokenVersion, item => item.TokenVersion + 1)
                    .SetProperty(item => item.UpdatedAt, now), cancellationToken);
            if (updated != 1)
                throw new PlatformReconnectRequiredException("MyAnimeList did not provide a replacement refresh token. Reconnect MyAnimeList to continue.");
            await _dbContext.Entry(account).ReloadAsync(cancellationToken);
        }
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
        return await WithAuthorizationAsync(account, async accessToken =>
        {
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
        }, cancellationToken);
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
        var form = new Dictionary<string, string?>();
        if (request.UpdateStatus)
        {
            form["status"] = ToMyAnimeListStatus(request.Status, isAnime);
            form[isAnime ? "is_rewatching" : "is_rereading"] = request.Status == MediaLibraryStatuses.Repeating
                ? "true"
                : "false";
        }
        if (request.UpdateScore)
        {
            form["score"] = score.ToString(CultureInfo.InvariantCulture);
        }

        if (request.UpdateProgress && isAnime && request.ProgressEpisodes is { } episodes)
        {
            form["num_watched_episodes"] = episodes.ToString(CultureInfo.InvariantCulture);
        }
        else if (request.UpdateProgress && !isAnime)
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

        return await UpdateListStatusAsync(userId, identity, form, cancellationToken, request);
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
        CancellationToken cancellationToken,
        MediaLibraryStateSyncRequest? expectedState = null)
    {
        var account = await RequireConnectedAccountAsync(userId, cancellationToken);
        return await WithAuthorizationAsync(account, async accessToken =>
        {
            if (expectedState is not null
                && (expectedState.LastKnownRemoteUpdateAt.HasValue || expectedState.ExpectMissingRemote))
            {
                var current = await _apiClient.GetAsync<MyAnimeListMedia>(
                    $"/{identity.PathKind}/{identity.Id}",
                    accessToken,
                    new Dictionary<string, string?> { ["fields"] = "my_list_status" },
                    cancellationToken);
                if (expectedState.ExpectMissingRemote ? current.MyListStatus is not null
                    : current.MyListStatus?.UpdatedAt != expectedState.LastKnownRemoteUpdateAt)
                {
                    throw new PlatformApiException(
                        "platform_sync_conflict", "MyAnimeList changed after the sync preview. Create a new preview.", 409);
                }
            }

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
        }, cancellationToken, retryRead: false);
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

    private async Task<T> WithAuthorizationAsync<T>(ConnectedServiceAccount account,
        Func<string, Task<T>> operation, CancellationToken ct, bool retryRead = true)
    {
        var expectedExternalId = account.ExternalAccountId;
        async Task<T> ExecuteAsync(string token)
        {
            var current = await _dbContext.ConnectedServiceAccounts.AsNoTracking().SingleAsync(item => item.Id == account.Id, ct);
            if (current.ExternalAccountId != expectedExternalId)
                throw new PlatformApiException("platform_account_changed", "The connected MyAnimeList account changed. Retry from the current account.", 409);
            ProviderReconnectState.RequireUsable(current);
            var result = await operation(token);
            if (!await _dbContext.ConnectedServiceAccounts.AsNoTracking().AnyAsync(item => item.Id == account.Id
                && item.ExternalAccountId == expectedExternalId, ct))
                throw new PlatformApiException("platform_account_changed", "The connected MyAnimeList account changed. Retry from the current account.", 409);
            return result;
        }
        var access = await ResolveAccessTokenAsync(account, ct);
        var version = account.TokenVersion;
        try { return await ExecuteAsync(access); }
        catch (MyAnimeListRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            await _dbContext.Entry(account).ReloadAsync(ct);
            access = await ResolveAccessTokenAsync(account, ct, forceRefresh: account.TokenVersion == version);
            if (!retryRead)
                throw new PlatformApiException("provider_authorization_refreshed",
                    "MyAnimeList authorization was refreshed. Retry the operation.", 503,
                    retryAfter: TimeSpan.FromSeconds(1), innerException: ex);
            try { return await ExecuteAsync(access); }
            catch (MyAnimeListRequestException rejected) when (rejected.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                if (await ProviderReconnectState.MarkOrAlreadyRequiredAsync(_dbContext, account, "access_token_rejected", ct))
                    throw new PlatformReconnectRequiredException("Reconnect MyAnimeList to continue.", rejected);
                throw;
            }
        }
    }

    private async Task<string> ResolveAccessTokenAsync(
        ConnectedServiceAccount account,
        CancellationToken cancellationToken, bool forceRefresh = false)
    {
        ProviderReconnectState.RequireUsable(account);
        var now = DateTime.UtcNow;
        if (!forceRefresh && !string.IsNullOrWhiteSpace(account.EncryptedAccessToken)
            && (account.TokenExpiresAt is null || account.TokenExpiresAt > now.Add(AccessTokenRefreshMargin)))
        {
            return _tokenEncryptionService.Decrypt(account.EncryptedAccessToken);
        }

        using var refreshLease = await _tokenRefreshGate.AcquireAsync(account.Id, cancellationToken);
        await _dbContext.Entry(account).ReloadAsync(cancellationToken);
        ProviderReconnectState.RequireUsable(account);
        now = DateTime.UtcNow;
        if (!forceRefresh && !string.IsNullOrWhiteSpace(account.EncryptedAccessToken)
            && (account.TokenExpiresAt is null || account.TokenExpiresAt > now.Add(AccessTokenRefreshMargin)))
        {
            return _tokenEncryptionService.Decrypt(account.EncryptedAccessToken);
        }

        if (string.IsNullOrWhiteSpace(account.EncryptedRefreshToken)
            || account.RefreshTokenExpiresAt is { } refreshExpiresAt && refreshExpiresAt <= now)
        {
            if (await ProviderReconnectState.MarkAsync(_dbContext, account, "refresh_token_expired", cancellationToken))
                throw new PlatformReconnectRequiredException("Reconnect MyAnimeList to continue.");
            await _dbContext.Entry(account).ReloadAsync(cancellationToken);
            ProviderReconnectState.RequireUsable(account);
            return _tokenEncryptionService.Decrypt(account.EncryptedAccessToken!);
        }

        var refreshToken = _tokenEncryptionService.Decrypt(account.EncryptedRefreshToken);
        var version = account.TokenVersion;
        MyAnimeListTokenResponse token;
        try { token = await _apiClient.RefreshAccessTokenAsync(refreshToken, cancellationToken); }
        catch (MyAnimeListRequestException ex) when ((ex.StatusCode == System.Net.HttpStatusCode.BadRequest && ex.ErrorCode == "invalid_grant")
            || ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            if (await ProviderReconnectState.MarkAsync(_dbContext, account, "refresh_token_invalid", cancellationToken))
                throw new PlatformReconnectRequiredException("Reconnect MyAnimeList to continue.", ex);
            await _dbContext.Entry(account).ReloadAsync(cancellationToken);
            // The refresh gate is already held. A newer OAuth result has a usable token.
            ProviderReconnectState.RequireUsable(account);
            return _tokenEncryptionService.Decrypt(account.EncryptedAccessToken!);
        }
        await using var tokenDb = PlaylistReadIngestion.CreateContext(_dbContext);
        var encryptedAccess = _tokenEncryptionService.Encrypt(token.AccessToken);
        var encryptedRefresh = string.IsNullOrWhiteSpace(token.RefreshToken)
            ? account.EncryptedRefreshToken : _tokenEncryptionService.Encrypt(token.RefreshToken);
        var refreshExpiry = string.IsNullOrWhiteSpace(token.RefreshToken) ? account.RefreshTokenExpiresAt : now.AddMonths(1);
        var saved = await tokenDb.ConnectedServiceAccounts.Where(item => item.Id == account.Id
            && item.TokenVersion == version && item.ExternalAccountId == account.ExternalAccountId)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.EncryptedAccessToken, encryptedAccess)
                .SetProperty(item => item.EncryptedRefreshToken, encryptedRefresh)
                .SetProperty(item => item.RefreshTokenExpiresAt, refreshExpiry)
                .SetProperty(item => item.TokenExpiresAt, now.AddSeconds(token.ExpiresIn))
                .SetProperty(item => item.TokenVersion, version + 1)
                .SetProperty(item => item.UpdatedAt, now), cancellationToken);
        await _dbContext.Entry(account).ReloadAsync(cancellationToken);
        ProviderReconnectState.RequireUsable(account);
        if (saved == 0) return _tokenEncryptionService.Decrypt(account.EncryptedAccessToken!);
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
            ExternalUrl = BuildExternalUrl(mediaKind, media.Id),
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
            ExternalUrl = BuildExternalUrl(mediaKind, media.Id),
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
