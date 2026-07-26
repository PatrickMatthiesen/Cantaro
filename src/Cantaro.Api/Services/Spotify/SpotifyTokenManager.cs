using System.Collections.Concurrent;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services.Spotify;

public sealed class SpotifyTokenRefreshCoordinator
{
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _locks = new();

    public SemaphoreSlim ForAccount(int accountId) => _locks.GetOrAdd(accountId, _ => new SemaphoreSlim(1, 1));
}

public sealed class SpotifyTokenManager
{
    private static readonly TimeSpan AccessTokenSkew = TimeSpan.FromMinutes(1);
    private const string ServiceName = "spotify";

    private readonly ApplicationDbContext _dbContext;
    private readonly SpotifyApiClient _apiClient;
    private readonly TokenEncryptionService _encryptionService;
    private readonly SpotifyTokenRefreshCoordinator _coordinator;
    private readonly TimeProvider _timeProvider;

    public SpotifyTokenManager(
        ApplicationDbContext dbContext,
        SpotifyApiClient apiClient,
        TokenEncryptionService encryptionService,
        SpotifyTokenRefreshCoordinator coordinator,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _apiClient = apiClient;
        _encryptionService = encryptionService;
        _coordinator = coordinator;
        _timeProvider = timeProvider;
    }

    public async Task<string> GetAccessTokenAsync(
        int userId,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var account = await RequireAccountAsync(userId, cancellationToken);
        if (IsReconnectRequired(account))
        {
            throw ReconnectRequired();
        }

        if (!forceRefresh && CanReuseAccessToken(account))
        {
            return _encryptionService.Decrypt(account.EncryptedAccessToken!);
        }

        var refreshLock = _coordinator.ForAccount(account.Id);
        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            await _dbContext.Entry(account).ReloadAsync(cancellationToken);
            if (IsReconnectRequired(account))
            {
                throw ReconnectRequired();
            }

            if (!forceRefresh && CanReuseAccessToken(account))
            {
                return _encryptionService.Decrypt(account.EncryptedAccessToken!);
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            if (account.RefreshTokenExpiresAt is { } refreshExpiry && refreshExpiry <= now)
            {
                await MarkReconnectRequiredAsync(account, "refresh_token_expired", cancellationToken);
                throw ReconnectRequired();
            }

            if (string.IsNullOrWhiteSpace(account.EncryptedRefreshToken))
            {
                await MarkReconnectRequiredAsync(account, "refresh_token_missing", cancellationToken);
                throw ReconnectRequired();
            }

            SpotifyTokenResponse refreshed;
            try
            {
                refreshed = await _apiClient.RefreshTokenAsync(
                    _encryptionService.Decrypt(account.EncryptedRefreshToken),
                    cancellationToken);
            }
            catch (PlatformApiException ex) when (
                string.Equals(ex.Code, "invalid_grant", StringComparison.OrdinalIgnoreCase))
            {
                await MarkReconnectRequiredAsync(account, "refresh_token_invalid", cancellationToken);
                throw new PlatformReconnectRequiredException(
                    "Your Spotify connection expired. Reconnect Spotify to continue.",
                    ex);
            }

            account.EncryptedAccessToken = _encryptionService.Encrypt(refreshed.AccessToken);
            account.TokenExpiresAt = now.AddSeconds(Math.Max(1, refreshed.ExpiresIn));
            if (!string.IsNullOrWhiteSpace(refreshed.RefreshToken))
            {
                account.EncryptedRefreshToken = _encryptionService.Encrypt(refreshed.RefreshToken);
            }

            if (!string.IsNullOrWhiteSpace(refreshed.Scope))
            {
                account.Scopes = refreshed.Scope;
            }

            account.ConnectionState = "connected";
            account.ReconnectRequiredAt = null;
            account.ReconnectReason = null;
            account.UpdatedAt = now;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return refreshed.AccessToken;
        }
        finally
        {
            refreshLock.Release();
        }
    }

    public async Task MarkReconnectRequiredAsync(
        int userId,
        string reason,
        CancellationToken cancellationToken)
    {
        var account = await RequireAccountAsync(userId, cancellationToken);
        await MarkReconnectRequiredAsync(account, reason, cancellationToken);
    }

    private async Task<ConnectedServiceAccount> RequireAccountAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.ConnectedServiceAccounts.SingleOrDefaultAsync(
                candidate => candidate.UserId == userId && candidate.Service == ServiceName,
                cancellationToken)
            ?? throw new PlatformApiException(
                "spotify_not_connected",
                "Connect Spotify before requesting playlists.",
                StatusCodes.Status403Forbidden);
    }

    private bool CanReuseAccessToken(ConnectedServiceAccount account)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        return !string.IsNullOrWhiteSpace(account.EncryptedAccessToken)
            && account.TokenExpiresAt is { } expiresAt
            && expiresAt > now.Add(AccessTokenSkew);
    }

    private static bool IsReconnectRequired(ConnectedServiceAccount account)
        => string.Equals(account.ConnectionState, "reconnect_required", StringComparison.Ordinal);

    private async Task MarkReconnectRequiredAsync(
        ConnectedServiceAccount account,
        string reason,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        account.EncryptedAccessToken = null;
        account.EncryptedRefreshToken = null;
        account.TokenExpiresAt = null;
        account.RefreshTokenExpiresAt = null;
        account.ConnectionState = "reconnect_required";
        account.ReconnectRequiredAt = now;
        account.ReconnectReason = reason;
        account.UpdatedAt = now;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static PlatformReconnectRequiredException ReconnectRequired()
        => new("Your Spotify connection expired. Reconnect Spotify to continue.");
}
