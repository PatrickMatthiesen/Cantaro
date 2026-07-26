using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services.Spotify;

public sealed record SpotifyAccessToken(string Value, long Version);

public sealed class SpotifyTokenManager
{
    private static readonly TimeSpan AccessTokenSkew = TimeSpan.FromMinutes(1);
    private const int RefreshLeaseSeconds = 30;
    private static readonly TimeSpan RefreshLeaseHeartbeatInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RefreshLeasePollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RefreshLeaseCleanupTimeout = TimeSpan.FromSeconds(5);
    private const string ServiceName = "spotify";

    private readonly ApplicationDbContext _dbContext;
    private readonly SpotifyApiClient _apiClient;
    private readonly TokenEncryptionService _encryptionService;
    private readonly TimeProvider _timeProvider;
    private readonly IServiceScopeFactory _scopeFactory;

    public SpotifyTokenManager(
        ApplicationDbContext dbContext,
        SpotifyApiClient apiClient,
        TokenEncryptionService encryptionService,
        TimeProvider timeProvider,
        IServiceScopeFactory scopeFactory)
    {
        _dbContext = dbContext;
        _apiClient = apiClient;
        _encryptionService = encryptionService;
        _timeProvider = timeProvider;
        _scopeFactory = scopeFactory;
    }

    public async Task<string> GetAccessTokenAsync(
        int userId,
        bool forceRefresh,
        CancellationToken cancellationToken)
        => (await GetAccessTokenSnapshotAsync(userId, forceRefresh, cancellationToken)).Value;

    public async Task<SpotifyAccessToken> GetAccessTokenSnapshotAsync(
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
            return ToAccessToken(account);
        }

        var requestedTokenVersion = account.TokenVersion;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _dbContext.Entry(account).ReloadAsync(cancellationToken);

            if (IsReconnectRequired(account))
            {
                throw ReconnectRequired();
            }

            // A different process may have completed the requested refresh while this
            // process was waiting for the distributed lease.
            if ((!forceRefresh || account.TokenVersion != requestedTokenVersion)
                && CanReuseAccessToken(account))
            {
                return ToAccessToken(account);
            }

            var leaseId = Guid.NewGuid();
            var acquired = await TryAcquireLeaseAsync(account.Id, leaseId, cancellationToken);
            if (!acquired)
            {
                await Task.Delay(RefreshLeasePollInterval, cancellationToken);
                continue;
            }

            await _dbContext.Entry(account).ReloadAsync(cancellationToken);
            if (account.TokenRefreshLeaseId != leaseId)
            {
                continue;
            }

            var tokenVersion = account.TokenVersion;
            var leaseReleased = false;
            try
            {
                var now = _timeProvider.GetUtcNow().UtcDateTime;
                if (account.RefreshTokenExpiresAt is { } refreshExpiry && refreshExpiry <= now)
                {
                    if (await TryMarkReconnectRequiredAsync(
                            account.Id,
                            leaseId,
                            tokenVersion,
                            "refresh_token_expired",
                            cancellationToken))
                    {
                        leaseReleased = true;
                        throw ReconnectRequired();
                    }

                    continue;
                }

                if (string.IsNullOrWhiteSpace(account.EncryptedRefreshToken))
                {
                    if (await TryMarkReconnectRequiredAsync(
                            account.Id,
                            leaseId,
                            tokenVersion,
                            "refresh_token_missing",
                            cancellationToken))
                    {
                        leaseReleased = true;
                        throw ReconnectRequired();
                    }

                    continue;
                }

                SpotifyTokenResponse refreshed;
                using var heartbeatStop = new CancellationTokenSource();
                using var ownershipLost = new CancellationTokenSource();
                using var providerCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    ownershipLost.Token);
                var heartbeat = MaintainLeaseAsync(
                    account.Id,
                    leaseId,
                    tokenVersion,
                    heartbeatStop.Token,
                    ownershipLost);
                try
                {
                    refreshed = await _apiClient.RefreshTokenAsync(
                        _encryptionService.Decrypt(account.EncryptedRefreshToken),
                        providerCancellation.Token);
                }
                catch (OperationCanceledException) when (
                    !cancellationToken.IsCancellationRequested
                    && ownershipLost.IsCancellationRequested)
                {
                    continue;
                }
                catch (PlatformApiException ex) when (
                    string.Equals(ex.Code, "invalid_grant", StringComparison.OrdinalIgnoreCase))
                {
                    if (await TryMarkReconnectRequiredAsync(
                            account.Id,
                            leaseId,
                            tokenVersion,
                            "refresh_token_invalid",
                            cancellationToken))
                    {
                        leaseReleased = true;
                        throw new PlatformReconnectRequiredException(
                            "Your Spotify connection expired. Reconnect Spotify to continue.",
                            ex);
                    }

                    // Reauthorization or a newer refresh won while this request was
                    // in flight. Reload instead of invalidating those credentials.
                    continue;
                }
                finally
                {
                    heartbeatStop.Cancel();
                    await heartbeat;
                }

                var encryptedAccessToken = _encryptionService.Encrypt(refreshed.AccessToken);
                var encryptedRefreshToken = string.IsNullOrWhiteSpace(refreshed.RefreshToken)
                    ? account.EncryptedRefreshToken
                    : _encryptionService.Encrypt(refreshed.RefreshToken);
                var scopes = string.IsNullOrWhiteSpace(refreshed.Scope)
                    ? account.Scopes
                    : refreshed.Scope;
                var refreshedAt = _timeProvider.GetUtcNow().UtcDateTime;
                var persisted = await ExecuteWithFreshContextAsync(dbContext =>
                    dbContext.ConnectedServiceAccounts
                        .Where(candidate =>
                            candidate.Id == account.Id
                            && candidate.TokenRefreshLeaseId == leaseId
                            && candidate.TokenVersion == tokenVersion)
                        .ExecuteUpdateAsync(
                            setters => setters
                                .SetProperty(candidate => candidate.EncryptedAccessToken, encryptedAccessToken)
                                .SetProperty(candidate => candidate.EncryptedRefreshToken, encryptedRefreshToken)
                                .SetProperty(candidate => candidate.TokenExpiresAt, refreshedAt.AddSeconds(Math.Max(1, refreshed.ExpiresIn)))
                                .SetProperty(candidate => candidate.Scopes, scopes)
                                .SetProperty(candidate => candidate.ConnectionState, "connected")
                                .SetProperty(candidate => candidate.ReconnectRequiredAt, (DateTime?)null)
                                .SetProperty(candidate => candidate.ReconnectReason, (string?)null)
                                .SetProperty(candidate => candidate.TokenVersion, tokenVersion + 1)
                                .SetProperty(candidate => candidate.TokenRefreshLeaseId, (Guid?)null)
                                .SetProperty(candidate => candidate.TokenRefreshLeaseExpiresAt, (DateTime?)null)
                                .SetProperty(candidate => candidate.UpdatedAt, refreshedAt),
                            cancellationToken));

                if (persisted == 1)
                {
                    leaseReleased = true;
                    return new SpotifyAccessToken(refreshed.AccessToken, tokenVersion + 1);
                }

                // A reconnect or newer lease owner changed the fencing version while
                // Spotify was responding. The stale response must never be published.
            }
            finally
            {
                if (!leaseReleased)
                {
                    await ReleaseLeaseBestEffortAsync(account.Id, leaseId, tokenVersion);
                }
            }
        }
    }

    public async Task<bool> MarkReconnectRequiredAsync(
        int userId,
        long expectedTokenVersion,
        string reason,
        CancellationToken cancellationToken)
    {
        var account = await RequireAccountAsync(userId, cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var updated = await ExecuteWithFreshContextAsync(dbContext =>
            dbContext.ConnectedServiceAccounts
                .Where(candidate =>
                    candidate.Id == account.Id
                    && candidate.TokenVersion == expectedTokenVersion)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(candidate => candidate.EncryptedAccessToken, (string?)null)
                        .SetProperty(candidate => candidate.EncryptedRefreshToken, (string?)null)
                        .SetProperty(candidate => candidate.TokenExpiresAt, (DateTime?)null)
                        .SetProperty(candidate => candidate.RefreshTokenExpiresAt, (DateTime?)null)
                        .SetProperty(candidate => candidate.ConnectionState, "reconnect_required")
                        .SetProperty(candidate => candidate.ReconnectRequiredAt, now)
                        .SetProperty(candidate => candidate.ReconnectReason, reason)
                        .SetProperty(candidate => candidate.TokenVersion, expectedTokenVersion + 1)
                        .SetProperty(candidate => candidate.TokenRefreshLeaseId, (Guid?)null)
                        .SetProperty(candidate => candidate.TokenRefreshLeaseExpiresAt, (DateTime?)null)
                        .SetProperty(candidate => candidate.UpdatedAt, now),
                    cancellationToken));
        return updated == 1;
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

    private SpotifyAccessToken ToAccessToken(ConnectedServiceAccount account)
        => new(_encryptionService.Decrypt(account.EncryptedAccessToken!), account.TokenVersion);

    private static bool IsReconnectRequired(ConnectedServiceAccount account)
        => string.Equals(account.ConnectionState, "reconnect_required", StringComparison.Ordinal);

    private Task<bool> TryAcquireLeaseAsync(
        int accountId,
        Guid leaseId,
        CancellationToken cancellationToken)
        => ExecuteWithFreshContextAsync(async dbContext =>
            await dbContext.ConnectedServiceAccounts
                .Where(candidate =>
                    candidate.Id == accountId
                    && (candidate.TokenRefreshLeaseExpiresAt == null
                        || candidate.TokenRefreshLeaseExpiresAt <= DateTime.UtcNow))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(candidate => candidate.TokenRefreshLeaseId, leaseId)
                        .SetProperty(
                            candidate => candidate.TokenRefreshLeaseExpiresAt,
                            candidate => DateTime.UtcNow.AddSeconds(RefreshLeaseSeconds)),
                    cancellationToken) == 1);

    private Task<bool> RenewLeaseAsync(
        int accountId,
        Guid leaseId,
        long tokenVersion,
        CancellationToken cancellationToken)
        => ExecuteWithFreshContextAsync(async dbContext =>
            await dbContext.ConnectedServiceAccounts
                .Where(candidate =>
                    candidate.Id == accountId
                    && candidate.TokenRefreshLeaseId == leaseId
                    && candidate.TokenVersion == tokenVersion)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        candidate => candidate.TokenRefreshLeaseExpiresAt,
                        candidate => DateTime.UtcNow.AddSeconds(RefreshLeaseSeconds)),
                    cancellationToken) == 1);

    private async Task MaintainLeaseAsync(
        int accountId,
        Guid leaseId,
        long tokenVersion,
        CancellationToken stoppingToken,
        CancellationTokenSource ownershipLost)
    {
        try
        {
            while (true)
            {
                await Task.Delay(RefreshLeaseHeartbeatInterval, stoppingToken);
                if (!await RenewLeaseAsync(accountId, leaseId, tokenVersion, stoppingToken))
                {
                    ownershipLost.Cancel();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch
        {
            // If the lease cannot be renewed, stop using the provider token. The
            // next caller can safely recover after the last persisted lease expires.
            ownershipLost.Cancel();
        }
    }

    private async Task<bool> TryMarkReconnectRequiredAsync(
        int accountId,
        Guid leaseId,
        long tokenVersion,
        string reason,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var updated = await ExecuteWithFreshContextAsync(dbContext =>
            dbContext.ConnectedServiceAccounts
                .Where(candidate =>
                    candidate.Id == accountId
                    && candidate.TokenRefreshLeaseId == leaseId
                    && candidate.TokenVersion == tokenVersion)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(candidate => candidate.EncryptedAccessToken, (string?)null)
                        .SetProperty(candidate => candidate.EncryptedRefreshToken, (string?)null)
                        .SetProperty(candidate => candidate.TokenExpiresAt, (DateTime?)null)
                        .SetProperty(candidate => candidate.RefreshTokenExpiresAt, (DateTime?)null)
                        .SetProperty(candidate => candidate.ConnectionState, "reconnect_required")
                        .SetProperty(candidate => candidate.ReconnectRequiredAt, now)
                        .SetProperty(candidate => candidate.ReconnectReason, reason)
                        .SetProperty(candidate => candidate.TokenVersion, tokenVersion + 1)
                        .SetProperty(candidate => candidate.TokenRefreshLeaseId, (Guid?)null)
                        .SetProperty(candidate => candidate.TokenRefreshLeaseExpiresAt, (DateTime?)null)
                        .SetProperty(candidate => candidate.UpdatedAt, now),
                    cancellationToken));
        return updated == 1;
    }

    private async Task ReleaseLeaseBestEffortAsync(int accountId, Guid leaseId, long tokenVersion)
    {
        try
        {
            using var cleanup = new CancellationTokenSource(RefreshLeaseCleanupTimeout);
            await ExecuteWithFreshContextAsync(dbContext =>
                dbContext.ConnectedServiceAccounts
                    .Where(candidate =>
                        candidate.Id == accountId
                        && candidate.TokenRefreshLeaseId == leaseId
                        && candidate.TokenVersion == tokenVersion)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(candidate => candidate.TokenRefreshLeaseId, (Guid?)null)
                            .SetProperty(candidate => candidate.TokenRefreshLeaseExpiresAt, (DateTime?)null),
                        cleanup.Token));
        }
        catch
        {
            // The 30-second lease expiry is the crash-safe fallback. Cleanup must
            // never replace the original provider or cancellation exception.
        }
    }

    private async Task<TResult> ExecuteWithFreshContextAsync<TResult>(
        Func<ApplicationDbContext, Task<TResult>> operation)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await operation(dbContext);
    }

    private static PlatformReconnectRequiredException ReconnectRequired()
        => new("Your Spotify connection expired. Reconnect Spotify to continue.");
}
