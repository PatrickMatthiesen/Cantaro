using Cantaro.Api.Configuration;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.WebUtilities;

namespace Cantaro.Api.Services.Spotify;

public sealed class SpotifyService
{
    public const string ServiceName = "spotify";
    public const string AuthorizationScopes = "playlist-read-private playlist-read-collaborative user-read-private";

    private readonly ApplicationDbContext _dbContext;
    private readonly SpotifyApiClient _apiClient;
    private readonly SpotifyTokenManager _tokenManager;
    private readonly TokenEncryptionService _encryptionService;
    private readonly SpotifyOptions _options;
    private readonly TimeProvider _timeProvider;

    public SpotifyService(
        ApplicationDbContext dbContext,
        SpotifyApiClient apiClient,
        SpotifyTokenManager tokenManager,
        TokenEncryptionService encryptionService,
        IOptions<SpotifyOptions> options,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _apiClient = apiClient;
        _tokenManager = tokenManager;
        _encryptionService = encryptionService;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public static string ResolveRedirectUri(CallbackUrlCandidates callbackUrls)
    {
        var preferredUri = new Uri(callbackUrls.Preferred);
        if (!IsBareLocalhost(preferredUri))
        {
            return callbackUrls.Preferred;
        }

        var loopbackUri = callbackUrls.Http is null
            ? preferredUri
            : new Uri(callbackUrls.Http);
        var builder = new UriBuilder(loopbackUri)
        {
            Host = "127.0.0.1"
        };
        return builder.Uri.AbsoluteUri;
    }

    public string GetAuthorizationUrl(string redirectUri, string state)
    {
        EnsureConfigured();

        var query = new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = _options.ClientId,
            ["scope"] = AuthorizationScopes,
            ["redirect_uri"] = redirectUri,
            ["state"] = state
        };

        return QueryHelpers.AddQueryString("https://accounts.spotify.com/authorize", query);
    }

    public Task<ConnectedServiceAccount?> GetConnectedAccountAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        return _dbContext.ConnectedServiceAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.UserId == userId && candidate.Service == ServiceName,
                cancellationToken);
    }

    public async Task<ConnectedServiceAccount> ExchangeCodeAndSaveAsync(
        int userId,
        string authorizationCode,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var token = await _apiClient.ExchangeCodeAsync(authorizationCode, redirectUri, cancellationToken);
        var profile = await _apiClient.GetProfileAsync(token.AccessToken, cancellationToken);
        var externalAccountId = profile.AccountId ?? profile.Id;
        if (string.IsNullOrWhiteSpace(externalAccountId))
        {
            throw new PlatformApiException(
                "spotify_profile_missing_account_id",
                "Spotify did not return a stable account identifier.",
                StatusCodes.Status502BadGateway);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var displayName = string.IsNullOrWhiteSpace(profile.DisplayName)
            ? "Spotify account"
            : profile.DisplayName;
        var encryptedAccessToken = _encryptionService.Encrypt(token.AccessToken);
        var encryptedRefreshToken = string.IsNullOrWhiteSpace(token.RefreshToken)
            ? null
            : _encryptionService.Encrypt(token.RefreshToken);
        var tokenExpiresAt = now.AddSeconds(Math.Max(1, token.ExpiresIn));
        var refreshTokenExpiresAt = now.AddMonths(6);
        var scopes = string.IsNullOrWhiteSpace(token.Scope) ? AuthorizationScopes : token.Scope;

        var replaced = await ReplaceExistingAccountAsync();
        if (replaced == 0)
        {
            var newAccount = new ConnectedServiceAccount
            {
                UserId = userId,
                Service = ServiceName,
                ExternalAccountId = externalAccountId,
                DisplayName = displayName,
                EncryptedAccessToken = encryptedAccessToken,
                EncryptedRefreshToken = encryptedRefreshToken,
                TokenExpiresAt = tokenExpiresAt,
                RefreshTokenExpiresAt = refreshTokenExpiresAt,
                Scopes = scopes,
                ConnectionState = "connected",
                TokenVersion = 1,
                CreatedAt = now,
                UpdatedAt = now
            };
            _dbContext.ConnectedServiceAccounts.Add(newAccount);
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // A concurrent callback may have inserted the unique user/service
                // row after our initial update. Fence that row instead of allowing
                // either callback to persist tracked stale state.
                _dbContext.Entry(newAccount).State = EntityState.Detached;
                if (await ReplaceExistingAccountAsync() == 0)
                {
                    throw;
                }
            }
        }

        return await _dbContext.ConnectedServiceAccounts
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.UserId == userId && candidate.Service == ServiceName,
                cancellationToken);

        Task<int> ReplaceExistingAccountAsync()
        {
            var query = _dbContext.ConnectedServiceAccounts
                .Where(candidate => candidate.UserId == userId && candidate.Service == ServiceName);
            return encryptedRefreshToken is null
                ? query.ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(candidate => candidate.ExternalAccountId, externalAccountId)
                        .SetProperty(candidate => candidate.DisplayName, displayName)
                        .SetProperty(candidate => candidate.EncryptedAccessToken, encryptedAccessToken)
                        .SetProperty(candidate => candidate.TokenExpiresAt, tokenExpiresAt)
                        .SetProperty(candidate => candidate.RefreshTokenExpiresAt, refreshTokenExpiresAt)
                        .SetProperty(candidate => candidate.Scopes, scopes)
                        .SetProperty(candidate => candidate.ConnectionState, "connected")
                        .SetProperty(candidate => candidate.ReconnectRequiredAt, (DateTime?)null)
                        .SetProperty(candidate => candidate.ReconnectReason, (string?)null)
                        .SetProperty(candidate => candidate.TokenVersion, candidate => candidate.TokenVersion + 1)
                        .SetProperty(candidate => candidate.TokenRefreshLeaseId, (Guid?)null)
                        .SetProperty(candidate => candidate.TokenRefreshLeaseExpiresAt, (DateTime?)null)
                        .SetProperty(candidate => candidate.UpdatedAt, now),
                    cancellationToken)
                : query.ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(candidate => candidate.ExternalAccountId, externalAccountId)
                        .SetProperty(candidate => candidate.DisplayName, displayName)
                        .SetProperty(candidate => candidate.EncryptedAccessToken, encryptedAccessToken)
                        .SetProperty(candidate => candidate.EncryptedRefreshToken, encryptedRefreshToken)
                        .SetProperty(candidate => candidate.TokenExpiresAt, tokenExpiresAt)
                        .SetProperty(candidate => candidate.RefreshTokenExpiresAt, refreshTokenExpiresAt)
                        .SetProperty(candidate => candidate.Scopes, scopes)
                        .SetProperty(candidate => candidate.ConnectionState, "connected")
                        .SetProperty(candidate => candidate.ReconnectRequiredAt, (DateTime?)null)
                        .SetProperty(candidate => candidate.ReconnectReason, (string?)null)
                        .SetProperty(candidate => candidate.TokenVersion, candidate => candidate.TokenVersion + 1)
                        .SetProperty(candidate => candidate.TokenRefreshLeaseId, (Guid?)null)
                        .SetProperty(candidate => candidate.TokenRefreshLeaseExpiresAt, (DateTime?)null)
                        .SetProperty(candidate => candidate.UpdatedAt, now),
                    cancellationToken);
        }
    }

    public async Task<IReadOnlyList<SpotifyPlaylistSnapshot>> GetPlaylistsAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        return await WithAuthorizedRetryAsync(
            userId,
            (accessToken, ct) => _apiClient.GetPlaylistsAsync(accessToken, ct),
            cancellationToken);
    }

    public async Task<IReadOnlyList<SpotifyTrackSnapshot>> GetPlaylistItemsAsync(
        int userId,
        string playlistId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await WithAuthorizedRetryAsync(
                userId,
                (accessToken, ct) => _apiClient.GetPlaylistItemsAsync(accessToken, playlistId, ct),
                cancellationToken);
        }
        catch (PlatformApiException ex) when (
            ex.StatusCode == StatusCodes.Status403Forbidden
            && string.Equals(ex.Code, "spotify_forbidden", StringComparison.Ordinal))
        {
            throw new PlatformApiException(
                "spotify_playlist_items_unavailable",
                "Spotify denied access to this playlist's items. This can happen when the connected account follows a playlist but does not own or collaborate on it.",
                StatusCodes.Status403Forbidden,
                innerException: ex);
        }
    }

    public async Task<SpotifyPlaylistImportSnapshot> GetPlaylistImportSnapshotAsync(
        int userId,
        string playlistId,
        CancellationToken cancellationToken)
    {
        var playlists = await GetPlaylistsAsync(userId, cancellationToken);
        var playlist = playlists.SingleOrDefault(candidate => candidate.Id == playlistId)
            ?? throw new PlatformApiException(
                "spotify_playlist_not_found",
                "Spotify did not return that playlist for the connected account.",
                StatusCodes.Status404NotFound);
        var tracks = await GetPlaylistItemsAsync(userId, playlistId, cancellationToken);
        return new SpotifyPlaylistImportSnapshot(playlist, tracks);
    }

    public async Task DisconnectAsync(int userId, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var account = await _dbContext.ConnectedServiceAccounts.SingleOrDefaultAsync(
            candidate => candidate.UserId == userId && candidate.Service == ServiceName,
            cancellationToken);
        if (account is null)
        {
            return;
        }

        var importedPlaylistIds = await _dbContext.ServicePlaylistMappings
            .Where(mapping => mapping.ConnectedServiceAccountId == account.Id
                && mapping.Service == ServiceName
                && mapping.Playlist!.UserId == userId)
            .Select(mapping => mapping.PlaylistId)
            .ToListAsync(cancellationToken);

        var importedPlaylists = await _dbContext.Playlists
            .Where(playlist => importedPlaylistIds.Contains(playlist.Id)
                && playlist.UserId == userId
                && playlist.ImportedFromService == ServiceName)
            .ToListAsync(cancellationToken);

        await _dbContext.MusicSyncJobs
            .Where(job => job.UserId == userId && job.Service == ServiceName)
            .ExecuteDeleteAsync(cancellationToken);
        _dbContext.Playlists.RemoveRange(importedPlaylists);
        _dbContext.ConnectedServiceAccounts.Remove(account);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var orphanObservations = await _dbContext.TrackObservations
            .Where(observation => observation.SourceType == ServiceName
                && !observation.PlaylistEntries.Any())
            .Select(observation => new { observation.Id, observation.ExternalId })
            .ToListAsync(cancellationToken);
        var orphanObservationIds = orphanObservations.Select(observation => observation.Id).ToList();

        if (orphanObservationIds.Count > 0)
        {
            await _dbContext.TrackResolutionCandidates
                .Where(candidate => orphanObservationIds.Contains(candidate.TrackObservationId))
                .ExecuteDeleteAsync(cancellationToken);
            await _dbContext.TrackObservations
                .Where(observation => orphanObservationIds.Contains(observation.Id))
                .ExecuteDeleteAsync(cancellationToken);

            var orphanExternalIds = orphanObservations
                .Select(observation => observation.ExternalId)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            await _dbContext.TrackSourceIds
                .Where(sourceId => sourceId.SourceType == ServiceName
                    && orphanExternalIds.Contains(sourceId.ExternalId)
                    && !_dbContext.TrackObservations.Any(observation =>
                        observation.SourceType == ServiceName
                        && observation.ExternalId == sourceId.ExternalId))
                .ExecuteDeleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<T> WithAuthorizedRetryAsync<T>(
        int userId,
        Func<string, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var accessToken = await _tokenManager.GetAccessTokenSnapshotAsync(userId, false, cancellationToken);
        try
        {
            return await operation(accessToken.Value, cancellationToken);
        }
        catch (PlatformApiException ex) when (ex.StatusCode == StatusCodes.Status401Unauthorized)
        {
            accessToken = await _tokenManager.GetAccessTokenSnapshotAsync(userId, true, cancellationToken);
            try
            {
                return await operation(accessToken.Value, cancellationToken);
            }
            catch (PlatformApiException retryException) when (
                retryException.StatusCode == StatusCodes.Status401Unauthorized)
            {
                var invalidated = await _tokenManager.MarkReconnectRequiredAsync(
                    userId,
                    accessToken.Version,
                    "access_token_rejected",
                    cancellationToken);
                if (invalidated)
                {
                    throw new PlatformReconnectRequiredException(
                        "Spotify rejected the refreshed connection. Reconnect Spotify to continue.",
                        retryException);
                }

                // Reauthorization or another refresh replaced the rejected token
                // before it could be invalidated. Give that newer generation one
                // bounded attempt instead of deleting it.
                var newest = await _tokenManager.GetAccessTokenSnapshotAsync(
                    userId,
                    false,
                    cancellationToken);
                return await operation(newest.Value, cancellationToken);
            }
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            throw new PlatformApiException(
                "spotify_not_configured",
                "Spotify OAuth is not configured.",
                StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static bool IsBareLocalhost(Uri uri)
    {
        var host = uri.Host.TrimEnd('.');
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase);
    }
}
