using System.Globalization;
using System.Net;
using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

public sealed class SimklMediaProvider(
    ApplicationDbContext db,
    SimklApiClient api,
    TokenEncryptionService encryption,
    SimklTokenRefreshGate refreshGate,
    SimklImportGate importGate,
    IAniListAvailabilityEnricher aniListAvailabilityEnricher) : IMediaProvider
{
    private const string ProviderName = MediaObservationSiteIdentifiers.Simkl;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public string ProviderId => ProviderName;
    public bool SupportsMediaKind(string mediaKind) => mediaKind is MediaKinds.Anime or MediaKinds.Movie or MediaKinds.Series;

    public string GetAuthorizationUrl(string redirectUri, string state, string codeChallenge)
        => api.BuildAuthorizationUrl(redirectUri, state, codeChallenge);

    public Task<ConnectedServiceAccount?> GetConnectedAccountAsync(int userId, CancellationToken cancellationToken = default)
        => db.ConnectedServiceAccounts.FirstOrDefaultAsync(x => x.UserId == userId && x.Service == ProviderName, cancellationToken);

    public async Task<ConnectedServiceAccount> ExchangeCodeAndSaveAsync(int userId, string authorizationCode,
        string redirectUri, string codeVerifier, CancellationToken cancellationToken)
    {
        var token = await api.ExchangeCodeAsync(authorizationCode, redirectUri, codeVerifier, cancellationToken);
        if (string.IsNullOrWhiteSpace(token.RefreshToken)) throw new InvalidOperationException("SIMKL did not return a refresh token.");
        EnsureWriteScope(token);
        using var settings = await api.GetAsync("/users/settings", token.AccessToken, null, cancellationToken);
        var profile = settings.RootElement;
        var accountId = SimklJson.Int(SimklJson.Property(profile, "account"), "id");
        if (accountId is null or <= 0) throw new InvalidOperationException("SIMKL did not return an account ID.");
        var name = SimklJson.String(SimklJson.Property(profile, "user"), "name") ?? accountId.Value.ToString(CultureInfo.InvariantCulture);
        var now = DateTime.UtcNow;
        var account = await GetConnectedAccountAsync(userId, cancellationToken);
        if (account is null)
        {
            account = new ConnectedServiceAccount { UserId = userId, Service = ProviderName,
                ExternalAccountId = accountId.Value.ToString(CultureInfo.InvariantCulture), CreatedAt = now };
            db.ConnectedServiceAccounts.Add(account);
        }
        if (account.Id > 0 && account.ExternalAccountId != accountId.Value.ToString(CultureInfo.InvariantCulture))
        {
            if (account.EncryptedRefreshToken is { } oldRefresh)
                await api.RevokeAsync(encryption.Decrypt(oldRefresh), cancellationToken);
            var oldSnapshot = await db.MediaProviderLibrarySnapshots.FirstOrDefaultAsync(x => x.ConnectedServiceAccountId == account.Id, cancellationToken);
            if (oldSnapshot is not null) db.MediaProviderLibrarySnapshots.Remove(oldSnapshot);
            var oldBindings = await db.MediaLibraryProviderBindings
                .Include(x => x.MediaLibraryEntry).Include(x => x.MediaProviderLink)
                .Where(x => x.ConnectedServiceAccountId == account.Id && x.MediaProviderLink!.Provider == ProviderName)
                .ToListAsync(cancellationToken);
            var bindingIds = oldBindings.Select(x => x.Id).ToArray();
            var oldOperations = await db.MediaProviderOperations
                .Where(x => bindingIds.Contains(x.MediaLibraryProviderBindingId)).ToListAsync(cancellationToken);
            db.MediaProviderOperations.RemoveRange(oldOperations);
            foreach (var binding in oldBindings)
            {
                binding.ConnectedServiceAccountId = null;
                binding.UpdatedAt = DateTimeOffset.UtcNow;
                if (binding.MediaLibraryEntry is { } entry)
                {
                    entry.LastMutationSource = MediaMutationSources.ProviderDisconnect;
                    entry.UpdatedAt = DateTimeOffset.UtcNow;
                }
            }
        }
        account.ExternalAccountId = accountId.Value.ToString(CultureInfo.InvariantCulture);
        account.DisplayName = name;
        account.EncryptedAccessToken = encryption.Encrypt(token.AccessToken);
        account.EncryptedRefreshToken = encryption.Encrypt(token.RefreshToken);
        account.TokenExpiresAt = now.AddSeconds(token.ExpiresIn);
        account.RefreshTokenExpiresAt = now.AddDays(180);
        account.Scopes = token.Scope ?? "media:read media:write";
        account.ConnectionState = "connected";
        account.ReconnectRequiredAt = null;
        account.ReconnectReason = null;
        account.TokenVersion++;
        account.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return account;
    }

    public async Task DisconnectAsync(int userId, CancellationToken cancellationToken)
    {
        var account = await GetConnectedAccountAsync(userId, cancellationToken);
        if (account is null) return;
        if (account.EncryptedRefreshToken is { } refreshToken)
            await api.RevokeAsync(encryption.Decrypt(refreshToken), cancellationToken);
        else if (account.EncryptedAccessToken is { } accessToken)
            await api.RevokeAsync(encryption.Decrypt(accessToken), cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var bindings = await db.MediaLibraryProviderBindings
            .Include(x => x.MediaLibraryEntry).Include(x => x.MediaProviderLink)
            .Where(x => x.MediaLibraryEntry!.UserId == userId && x.MediaProviderLink!.Provider == ProviderName
                && x.ConnectedServiceAccountId == account.Id).ToListAsync(cancellationToken);
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
        var snapshot = await db.MediaProviderLibrarySnapshots.FindAsync([account.Id], cancellationToken);
        if (snapshot is not null) db.MediaProviderLibrarySnapshots.Remove(snapshot);
        db.ConnectedServiceAccounts.Remove(account);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<MediaProviderLibraryImportResult> ImportLibraryAsync(int userId, CancellationToken cancellationToken)
    {
        using var lease = await importGate.AcquireAsync(userId, cancellationToken);
        return await ImportLibraryCoreAsync(userId, cancellationToken);
    }

    private async Task<MediaProviderLibraryImportResult> ImportLibraryCoreAsync(int userId, CancellationToken cancellationToken)
    {
        var account = await RequireAccountAsync(userId, cancellationToken);
        using var activities = await GetAuthenticatedAsync(userId, "/sync/activities", null, cancellationToken);
        var activity = activities.RootElement;
        var activityAll = SimklJson.String(activity, "all");
        if (string.IsNullOrWhiteSpace(activityAll)) throw new InvalidOperationException("SIMKL activities omitted the sync cursor.");
        var removed = new[] { "tv_shows", "movies", "anime" }
            .ToDictionary(x => x, x => SimklJson.String(SimklJson.Property(activity, x), "removed_from_list"));
        var snapshot = await db.MediaProviderLibrarySnapshots.FirstOrDefaultAsync(x => x.ConnectedServiceAccountId == account.Id, cancellationToken);
        SimklCursor? cursor = null;
        if (snapshot?.Cursor is { } rawCursor)
        {
            try { cursor = JsonSerializer.Deserialize<SimklCursor>(rawCursor, JsonOptions); }
            catch (JsonException) { /* Rebuild an unreadable snapshot. */ }
        }
        if (cursor?.FormatVersion != 1) cursor = null;
        var items = cursor is null ? new Dictionary<string, MediaProviderLibraryItem>()
            : JsonSerializer.Deserialize<Dictionary<string, MediaProviderLibraryItem>>(snapshot!.ItemsJson, JsonOptions) ?? [];
        if (cursor?.All != activityAll || snapshot is null)
        {
            var query = new Dictionary<string, string?>
            {
                ["extended"] = "full", ["episode_watched_at"] = "yes", ["include_all_episodes"] = "yes",
                ["language"] = "en"
            };
            if (snapshot is null || cursor is null)
            {
                foreach (var type in new[] { "shows", "movies", "anime" })
                {
                    using var page = await GetAuthenticatedAsync(userId, $"/sync/all-items/{type}", query, cancellationToken);
                    foreach (var item in SimklJson.MapLibraryItems(page.RootElement))
                    {
                        await ApplyEpisodeCatalogAsync(userId, item, cancellationToken);
                        items[item.ProviderMediaId] = item;
                    }
                }
            }
            else
            {
                query["date_from"] = cursor.All;
                using var page = await GetAuthenticatedAsync(userId, "/sync/all-items", query, cancellationToken);
                foreach (var item in SimklJson.MapLibraryItems(page.RootElement))
                {
                    await ApplyEpisodeCatalogAsync(userId, item, cancellationToken);
                    items[item.ProviderMediaId] = item;
                }
                if (removed.Any(x => !string.Equals(x.Value, cursor.Removed.GetValueOrDefault(x.Key), StringComparison.Ordinal)))
                {
                    using var ids = await GetAuthenticatedAsync(userId, "/sync/all-items",
                        new Dictionary<string, string?> { ["extended"] = "simkl_ids_only" }, cancellationToken);
                    var live = SimklJson.MapIds(ids.RootElement);
                    foreach (var stale in items.Keys.Where(x => !live.Contains(x)).ToArray()) items.Remove(stale);
                }
            }
            snapshot ??= new MediaProviderLibrarySnapshot { ConnectedServiceAccountId = account.Id };
            if (db.Entry(snapshot).State == EntityState.Detached) db.MediaProviderLibrarySnapshots.Add(snapshot);
            snapshot.ItemsJson = JsonSerializer.Serialize(items, JsonOptions);
            snapshot.Cursor = JsonSerializer.Serialize(new SimklCursor { FormatVersion = 1, All = activityAll, Removed = removed }, JsonOptions);
            snapshot.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
        return new MediaProviderLibraryImportResult
        {
            ProviderId = ProviderName,
            ImportedAt = DateTimeOffset.UtcNow,
            Items = items.Values.ToList()
        };
    }

    public async Task<IReadOnlyList<MediaProviderSearchResult>> SearchAsync(int userId, MediaCatalogSearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Query);
        var kinds = request.MediaKinds.Count == 0
            ? new[] { MediaKinds.Series, MediaKinds.Movie, MediaKinds.Anime }
            : request.MediaKinds.Where(x => x is MediaKinds.Series or MediaKinds.Movie or MediaKinds.Anime).Distinct().ToArray();
        var results = new List<MediaProviderSearchResult>();
        foreach (var kind in kinds)
        {
            var type = kind switch { MediaKinds.Series => "tv", MediaKinds.Movie => "movie", _ => "anime" };
            using var page = await GetAuthenticatedAsync(userId, $"/search/{type}",
                new Dictionary<string, string?> { ["q"] = request.Query, ["limit"] = Math.Min(50, request.Limit).ToString(CultureInfo.InvariantCulture), ["extended"] = "full" }, cancellationToken);
            foreach (var item in SimklJson.Array(page.RootElement))
            {
                var mapped = SimklJson.MapSearchResult(item, kind);
                if (mapped is not null) results.Add(mapped);
            }
        }
        return results.Take(request.Limit).ToList();
    }

    public Task<MediaProviderTitleDetails?> GetTitleDetailsAsync(
        int userId,
        string providerMediaId,
        CancellationToken cancellationToken)
        => GetTitleDetailsCoreAsync(userId, providerMediaId, includeAvailability: true, cancellationToken);

    private async Task<MediaProviderTitleDetails?> GetTitleDetailsCoreAsync(
        int userId,
        string providerMediaId,
        bool includeAvailability,
        CancellationToken cancellationToken)
    {
        var id = SimklJson.ParseId(providerMediaId);
        var path = id.Type switch { "tv" => "tv", "movie" => "movies", _ => "anime" };
        try
        {
            using var result = await GetAuthenticatedAsync(
                userId,
                $"/{path}/{id.Id}",
                new Dictionary<string, string?> { ["extended"] = "full" },
                cancellationToken);
            var details = SimklJson.MapDetails(result.RootElement, id.Type, id.Id);
            if (id.Type == "anime" && includeAvailability)
            {
                await ApplyAnimeAvailabilityAsync(details, cancellationToken);
            }
            if (id.Type == "tv")
            {
                try
                {
                    var catalog = await ReadEpisodeCatalogSnapshotAsync(userId, id, cancellationToken);
                    details.EpisodeCatalog = catalog.RegularEpisodes;
                    details.SpecialEpisodeCatalog = catalog.Specials;
                }
                catch (Exception ex) when (
                    !cancellationToken.IsCancellationRequested
                    && ex is SimklRequestException or HttpRequestException or JsonException or OperationCanceledException)
                {
                    // Keep title details usable and retain the last local catalog
                    // when SIMKL's episode endpoint is temporarily unavailable.
                }
            }
            return details;
        }
        catch (SimklRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return null; }
    }

    private async Task ApplyAnimeAvailabilityAsync(
        MediaProviderTitleDetails details,
        CancellationToken cancellationToken)
    {
        try
        {
            var availability = await aniListAvailabilityEnricher.GetAvailabilityAsync(
                details.CrossReferences,
                cancellationToken);
            if (availability is not null)
            {
                details.AvailabilityLinks = availability;
                details.AvailabilityRefreshSucceeded = true;
                return;
            }
        }
        catch (Exception ex) when (
            !cancellationToken.IsCancellationRequested
            && ex is AniListRequestException or HttpRequestException or JsonException or InvalidOperationException or OperationCanceledException)
        {
            // SIMKL details remain usable when optional AniList availability is unavailable.
        }

        var snapshot = await db.MediaProviderLinks
            .AsNoTracking()
            .Where(link => link.Provider == ProviderName && link.ExternalId == details.ProviderMediaId)
            .Select(link => link.AvailabilitySnapshot)
            .FirstOrDefaultAsync(cancellationToken);
        details.AvailabilityLinks = MediaProviderAvailabilitySnapshotCodec.Deserialize(snapshot);
        details.AvailabilityRefreshSucceeded = false;
    }

    public async Task<MediaReleaseMetadata?> GetReleaseMetadataAsync(int userId, string providerMediaId, CancellationToken cancellationToken)
    {
        var details = await GetTitleDetailsCoreAsync(
            userId,
            providerMediaId,
            includeAvailability: false,
            cancellationToken);
        return details is null ? null : new MediaReleaseMetadata
        {
            ProviderId = ProviderName, ProviderMediaId = providerMediaId,
            ReleaseStatusDimension = details.ReleaseStatusDimension,
            ReleasedCount = details.ReleasedCount, TotalKnownCount = details.TotalKnownCount,
            NextReleaseAt = details.NextReleaseAt, NextReleaseLabel = details.NextReleaseLabel
        };
    }

    public async Task<MediaProviderMutationResult> UpdateStatusAsync(int userId, MediaStatusUpdateRequest request, CancellationToken cancellationToken)
        => await WriteStatusAsync(userId, request.ProviderMediaId, request.Status, cancellationToken);

    public async Task<MediaProviderMutationResult> UpdateScoreAsync(int userId, MediaScoreUpdateRequest request, CancellationToken cancellationToken)
        => await WriteScoreAsync(userId, request.ProviderMediaId, request.Score, cancellationToken);

    public async Task<MediaProviderMutationResult> UpdateProgressAsync(int userId, MediaProgressUpdateRequest request, CancellationToken cancellationToken)
    {
        if (request.ProgressEpisodes is not { } count) throw new InvalidOperationException("SIMKL progress requires an episode count.");
        return await WriteProgressAsync(userId, request.ProviderMediaId, count, null, cancellationToken);
    }

    public async Task<MediaProviderMutationResult> SyncLibraryStateAsync(int userId, MediaLibraryStateSyncRequest request, CancellationToken cancellationToken)
    {
        MediaProviderMutationResult result;
        if (request.UpdateProgress && (request.WatchedEpisodes is not null || request.ProgressEpisodes is not null))
            result = await WriteProgressAsync(userId, request.ProviderMediaId, request.ProgressEpisodes, request.WatchedEpisodes, cancellationToken);
        else
            result = Result(request.ProviderMediaId);
        var historyResolvedCompletedToCurrent = request.Status == MediaLibraryStatuses.Completed
            && result.AppliedStatus == MediaLibraryStatuses.Current;
        if (request.UpdateStatus && !historyResolvedCompletedToCurrent
            && result.AppliedStatus != request.Status
            && (request.UpdateProgress || !await StatusMatchesSnapshotAsync(userId, request.ProviderMediaId, request.Status, cancellationToken)))
            result = await WriteStatusAsync(userId, request.ProviderMediaId, request.Status, cancellationToken);
        await WriteScoreAsync(userId, request.ProviderMediaId, request.Score, cancellationToken);
        return result;
    }

    private async Task<MediaProviderMutationResult> WriteStatusAsync(int userId, string providerMediaId, string status, CancellationToken cancellationToken)
    {
        var id = SimklJson.ParseId(providerMediaId);
        using var response = await PostAuthenticatedAsync(userId, "/sync/add-to-list",
            SimklJson.Envelope(id.Type, new { ids = new { simkl = id.Id }, to = SimklJson.ToStatus(status, id.Type) }), cancellationToken);
        SimklJson.ThrowIfNotFound(response.RootElement, id.Type);
        var resolved = SimklJson.ResolvedListStatus(response.RootElement, id.Type) ?? SimklJson.ToStatus(status, id.Type);
        return Result(providerMediaId, SimklJson.MapStatus(resolved));
    }

    private async Task<MediaProviderMutationResult> WriteScoreAsync(int userId, string providerMediaId, decimal? score, CancellationToken cancellationToken)
    {
        var id = SimklJson.ParseId(providerMediaId);
        var path = score is null ? "/sync/ratings/remove" : "/sync/ratings";
        if (score is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(score));
        var body = score is null
            ? SimklJson.Envelope(id.Type, new { ids = new { simkl = id.Id } })
            : SimklJson.Envelope(id.Type, new { ids = new { simkl = id.Id }, rating = Math.Clamp(decimal.ToInt32(decimal.Round(score.Value / 10m, MidpointRounding.AwayFromZero)), 1, 10) });
        using var response = await PostAuthenticatedAsync(userId, path, body, cancellationToken);
        SimklJson.ThrowIfNotFound(response.RootElement, id.Type);
        return Result(providerMediaId);
    }

    private async Task<MediaProviderMutationResult> WriteProgressAsync(int userId, string providerMediaId, int? progress,
        IReadOnlyList<MediaProviderWatchedEpisode>? exactEpisodes, CancellationToken cancellationToken)
    {
        var id = SimklJson.ParseId(providerMediaId);
        if (id.Type == "movie")
        {
            if (progress is > 0)
            {
                using var response = await PostAuthenticatedAsync(userId, "/sync/history",
                    SimklJson.Envelope(id.Type, new { ids = new { simkl = id.Id } }), cancellationToken);
                SimklJson.ThrowIfNotFound(response.RootElement, id.Type);
                return Result(providerMediaId, MediaLibraryStatuses.Completed);
            }
            return Result(providerMediaId);
        }
        var current = await ReadWatchedEpisodesAsync(userId, id, cancellationToken);
        IReadOnlyList<MediaProviderWatchedEpisode> target;
        if (exactEpisodes is not null) target = exactEpisodes;
        else
        {
            if (progress is null or < 0) throw new InvalidOperationException("SIMKL progress requires a nonnegative episode count.");
            var catalog = await ReadEpisodeCatalogAsync(userId, id, cancellationToken);
            if (progress > catalog.Count) throw new InvalidOperationException("SIMKL episode count exceeds its regular episode catalog.");
            var oldPrefix = 0;
            var currentKeys = current.Select(SimklJson.EpisodeKey).ToHashSet();
            while (oldPrefix < catalog.Count && currentKeys.Contains(SimklJson.EpisodeKey(catalog[oldPrefix]))) oldPrefix++;
            var prefixKeys = catalog.Take(oldPrefix).Select(SimklJson.EpisodeKey).ToHashSet();
            target = current.Where(x => !prefixKeys.Contains(SimklJson.EpisodeKey(x)))
                .Concat(catalog.Take(progress.Value)).DistinctBy(SimklJson.EpisodeKey).ToArray();
        }
        var existing = current.Select(SimklJson.EpisodeKey).ToHashSet();
        var desired = target.Select(SimklJson.EpisodeKey).ToHashSet();
        var toRemove = current.Where(x => !desired.Contains(SimklJson.EpisodeKey(x))).ToArray();
        var toAdd = target.Where(x => !existing.Contains(SimklJson.EpisodeKey(x))).ToArray();
        if (toRemove.Length > 0)
        {
            using var response = await PostAuthenticatedAsync(userId, "/sync/history/remove",
                SimklJson.EpisodeEnvelope(id, toRemove), cancellationToken);
            SimklJson.ThrowIfNotFound(response.RootElement, id.Type);
        }
        if (toAdd.Length > 0)
        {
            using var response = await PostAuthenticatedAsync(userId, "/sync/history?skip_auto_watching=yes",
                SimklJson.EpisodeEnvelope(id, toAdd), cancellationToken);
            SimklJson.ThrowIfNotFound(response.RootElement, id.Type);
            return Result(providerMediaId, SimklJson.ResolvedHistoryStatus(response.RootElement), progress);
        }
        return Result(providerMediaId, appliedProgress: progress);
    }

    private async Task<IReadOnlyList<MediaProviderWatchedEpisode>> ReadWatchedEpisodesAsync(int userId, SimklIdentity id, CancellationToken cancellationToken)
    {
        var library = await ImportLibraryAsync(userId, cancellationToken);
        var item = library.Items.FirstOrDefault(x => x.ProviderMediaId == id.ProviderMediaId);
        return item?.WatchedEpisodes ?? [];
    }

    private async Task ApplyEpisodeCatalogAsync(int userId, MediaProviderLibraryItem item, CancellationToken cancellationToken)
    {
        if (item.MediaKind != MediaKinds.Series || item.WatchedEpisodes is null) return;
        var id = SimklJson.ParseId(item.ProviderMediaId);
        var catalog = await ReadEpisodeCatalogAsync(userId, id, cancellationToken);
        SimklJson.ApplyCatalog(item, catalog);
    }

    private async Task<bool> StatusMatchesSnapshotAsync(int userId, string providerMediaId, string requested, CancellationToken cancellationToken)
    {
        var account = await RequireAccountAsync(userId, cancellationToken);
        var snapshot = await db.MediaProviderLibrarySnapshots.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ConnectedServiceAccountId == account.Id, cancellationToken);
        if (snapshot is null) return false;
        var items = JsonSerializer.Deserialize<Dictionary<string, MediaProviderLibraryItem>>(snapshot.ItemsJson, JsonOptions);
        if (items is null || !items.TryGetValue(providerMediaId, out var item)) return false;
        return item.Status == requested;
    }

    private async Task<IReadOnlyList<MediaProviderWatchedEpisode>> ReadEpisodeCatalogAsync(int userId, SimklIdentity id, CancellationToken cancellationToken)
        => (await ReadEpisodeCatalogSnapshotAsync(userId, id, cancellationToken)).RegularEpisodes;

    private async Task<MediaProviderEpisodeCatalogSnapshot> ReadEpisodeCatalogSnapshotAsync(
        int userId,
        SimklIdentity id,
        CancellationToken cancellationToken)
    {
        using var page = await GetAuthenticatedAsync(userId,
            $"/{(id.Type == "tv" ? "tv" : "anime")}/episodes/{id.Id}",
            new Dictionary<string, string?> { ["extended"] = "full" },
            cancellationToken);
        return SimklJson.MapEpisodeCatalogSnapshot(page.RootElement, id.Type);
    }

    private static MediaProviderMutationResult Result(string id, string? appliedStatus = null, int? appliedProgress = null)
        => new() { ProviderId = ProviderName, ProviderMediaId = id, AppliedAt = DateTimeOffset.UtcNow,
            AppliedStatus = appliedStatus, AppliedProgressEpisodes = appliedProgress };

    private Task<JsonDocument> GetAuthenticatedAsync(int userId, string path,
        IReadOnlyDictionary<string, string?>? query, CancellationToken cancellationToken)
        => AuthenticatedAsync(userId, token => api.GetAsync(path, token, query, cancellationToken), cancellationToken);

    private Task<JsonDocument> PostAuthenticatedAsync(int userId, string path, object body, CancellationToken cancellationToken)
        => AuthenticatedAsync(userId, token => api.PostAsync(path, token, body, cancellationToken), cancellationToken);

    private async Task<JsonDocument> AuthenticatedAsync(int userId, Func<string, Task<JsonDocument>> request,
        CancellationToken cancellationToken)
    {
        var account = await RequireAccountAsync(userId, cancellationToken);
        var token = await ResolveAccessTokenAsync(account, cancellationToken);
        try { return await request(token); }
        catch (SimklRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            var refreshed = await ResolveAccessTokenAsync(account, cancellationToken, token);
            return await request(refreshed);
        }
    }

    private async Task<ConnectedServiceAccount> RequireAccountAsync(int userId, CancellationToken cancellationToken)
        => await GetConnectedAccountAsync(userId, cancellationToken) ?? throw new InvalidOperationException("SIMKL account is not connected.");

    private async Task<string> ResolveAccessTokenAsync(ConnectedServiceAccount account,
        CancellationToken cancellationToken, string? rejectedToken = null)
    {
        var now = DateTime.UtcNow;
        if (account.ConnectionState != "connected")
            throw new InvalidOperationException("SIMKL access expired. Reconnect your SIMKL account.");
        if (account.ConnectionState == "connected" && account.EncryptedAccessToken is { } encrypted
            && (account.TokenExpiresAt is null || account.TokenExpiresAt > now.AddMinutes(1)))
        {
            var candidate = encryption.Decrypt(encrypted);
            if (rejectedToken is null || candidate != rejectedToken) return candidate;
        }
        using var lease = await refreshGate.AcquireAsync(account.Id, cancellationToken);
        await db.Entry(account).ReloadAsync(cancellationToken);
        now = DateTime.UtcNow;
        if (account.ConnectionState != "connected")
            throw new InvalidOperationException("SIMKL access expired. Reconnect your SIMKL account.");
        if (account.ConnectionState == "connected" && account.EncryptedAccessToken is { } valid
            && (account.TokenExpiresAt is null || account.TokenExpiresAt > now.AddMinutes(1)))
        {
            var candidate = encryption.Decrypt(valid);
            if (rejectedToken is null || candidate != rejectedToken) return candidate;
        }
        if (account.EncryptedRefreshToken is null || account.RefreshTokenExpiresAt is { } expires && expires <= now)
            throw new InvalidOperationException("SIMKL token expired. Reconnect your SIMKL account.");
        SimklTokenResponse token;
        try
        {
            token = await api.RefreshAsync(encryption.Decrypt(account.EncryptedRefreshToken), cancellationToken);
            EnsureWriteScope(token);
        }
        catch (SimklRequestException exception) when (exception.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            account.ConnectionState = "reconnect_required";
            account.ReconnectRequiredAt = now;
            account.ReconnectReason = "token_refresh_rejected";
            account.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("SIMKL access expired. Reconnect your SIMKL account.", exception);
        }
        account.EncryptedAccessToken = encryption.Encrypt(token.AccessToken);
        if (token.RefreshToken is { Length: > 0 }) account.EncryptedRefreshToken = encryption.Encrypt(token.RefreshToken);
        account.TokenExpiresAt = now.AddSeconds(token.ExpiresIn);
        account.RefreshTokenExpiresAt = now.AddDays(180);
        account.TokenVersion++;
        account.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return token.AccessToken;
    }

    private static void EnsureWriteScope(SimklTokenResponse token)
    {
        if (token.Scope is null || !token.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("media:write", StringComparer.Ordinal))
            throw new InvalidOperationException("SIMKL did not grant media:write. Reconnect and allow library changes.");
    }

    private sealed class SimklCursor
    {
        public int FormatVersion { get; set; }
        public required string All { get; set; }
        public Dictionary<string, string?> Removed { get; set; } = [];
    }
}
