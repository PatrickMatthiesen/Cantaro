using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services.Spotify;

public sealed class SpotifyPlaylistWriter(
    ApplicationDbContext dbContext,
    SpotifyApiClient apiClient,
    SpotifyTokenManager tokenManager) : IPlaylistWriter
{
    private const int WriteBatchSize = 100;

    public string PlatformId => SpotifyService.ServiceName;

    public Task ValidateCreationAsync(
        PlatformAccountContext account, CancellationToken cancellationToken)
        => WithAuthorizedRetryAsync(
            account,
            ValidateCreationAccessAsync,
            cancellationToken);

    public async Task<string> CreatePlaylistAsync(
        PlatformAccountContext account, string name, CancellationToken cancellationToken)
    {
        SpotifyAccessToken token;
        ConnectedServiceAccount connectedAccount;
        SpotifyProfileResponse profile;
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            await ValidateCreationAsync(account, cancellationToken);
            token = await tokenManager.GetAccessTokenSnapshotAsync(account, false, cancellationToken);
            connectedAccount = await ReadActiveAccountAsync(account, cancellationToken);
            RequirePrivateWriteScope(connectedAccount);
            profile = await apiClient.GetProfileAsync(token.Value, cancellationToken);
            if (!string.Equals(profile.AccountId ?? profile.Id, connectedAccount.ExternalAccountId, StringComparison.Ordinal))
                throw new PlatformApiException("spotify_account_changed",
                    "The connected Spotify account changed. Reconnect Spotify before creating a playlist.", 409);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (ArgumentException ex)
        {
            throw new PlatformApiException("spotify_playlist_creation_rejected",
                "The Cantaro playlist needs a name before Spotify can create it.",
                400, innerException: ex);
        }
        catch (PlatformApiException ex)
        {
            // No create request has been sent, so orchestration may release its reservation.
            throw CreationRejected(ex);
        }
        catch (HttpRequestException ex)
        {
            throw CreationRejected(new PlatformApiException("spotify_unavailable",
                "Spotify is unavailable. Retry synchronization later.",
                StatusCodes.Status503ServiceUnavailable, innerException: ex));
        }
        catch (OperationCanceledException ex)
        {
            throw CreationRejected(new PlatformApiException("spotify_creation_cancelled",
                "Spotify playlist creation was cancelled before the request was sent.",
                409, innerException: ex));
        }

        try
        {
            var created = await apiClient.CreateUnlistedPlaylistAsync(token.Value, name, cancellationToken);
            if (string.IsNullOrWhiteSpace(created.Id)
                || created.Id.Length > 100
                || !created.Id.All(char.IsLetterOrDigit)
                || string.IsNullOrWhiteSpace(profile.Id)
                || !string.Equals(created.OwnerId, profile.Id, StringComparison.Ordinal))
            {
                throw CreationOutcomeUnknown();
            }

            // Spotify's public flag describes profile publication, not access privacy.
            // A differing flag must not discard the ID of a successfully created copy.
            return created.Id;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw CreationOutcomeUnknown(ex);
        }
        catch (HttpRequestException ex)
        {
            throw CreationOutcomeUnknown(ex);
        }
        catch (PlatformApiException ex) when (ex.StatusCode >= 500 || ex.StatusCode == 408)
        {
            throw CreationOutcomeUnknown(ex);
        }
        catch (PlatformApiException ex) when (ex.Code != "spotify_playlist_creation_unknown"
            && ex.StatusCode is >= 400 and < 500)
        {
            throw CreationRejected(ex);
        }
    }

    public Task ValidateDestinationAsync(
        PlatformAccountContext account,
        string playlistId,
        CancellationToken cancellationToken)
        => WithAuthorizedRetryAsync(
            account,
            (accessToken, connectedAccount, ct) => ValidateAsync(
                accessToken, connectedAccount, playlistId, ct),
            cancellationToken);

    public Task ReconcileAsync(
        PlatformAccountContext account,
        string playlistId,
        IReadOnlyList<string> trackIds,
        CancellationToken cancellationToken)
    {
        ValidateId(playlistId, "playlist");
        foreach (var trackId in trackIds)
        {
            ValidateId(trackId, "track");
        }

        return WithAuthorizedRetryAsync(
            account,
            async (accessToken, connectedAccount, ct) =>
            {
                var current = await ValidateAsync(accessToken, connectedAccount, playlistId, ct);
                if (current.TrackIds.SequenceEqual(trackIds, StringComparer.Ordinal))
                {
                    return;
                }

                // Replace resets order, removals, and duplicates in one deterministic step.
                // If a later append fails, the queued retry re-reads and starts again.
                await apiClient.ReplacePlaylistItemsAsync(
                    accessToken,
                    playlistId,
                    trackIds.Take(WriteBatchSize).ToArray(),
                    ct);
                foreach (var batch in trackIds.Skip(WriteBatchSize).Chunk(WriteBatchSize))
                {
                    await apiClient.AddPlaylistItemsAsync(accessToken, playlistId, batch, ct);
                }

                var verified = await ValidateAsync(accessToken, connectedAccount, playlistId, ct);
                if (!verified.TrackIds.SequenceEqual(trackIds, StringComparer.Ordinal))
                {
                    throw new PlatformApiException(
                        "spotify_playlist_verification_failed",
                        "Spotify did not retain the requested playlist order. Retry the sync.",
                        StatusCodes.Status502BadGateway);
                }
            },
            cancellationToken);
    }

    private async Task<SpotifyPlaylistWriteSnapshot> ValidateAsync(
        string accessToken,
        ConnectedServiceAccount account,
        string playlistId,
        CancellationToken cancellationToken)
    {
        ValidateId(playlistId, "playlist");
        var profile = await apiClient.GetProfileAsync(accessToken, cancellationToken);
        var profileId = profile.AccountId ?? profile.Id;
        if (!string.Equals(profileId, account.ExternalAccountId, StringComparison.Ordinal))
        {
            throw new PlatformApiException(
                "spotify_account_changed",
                "The connected Spotify account changed. Reconnect Spotify before syncing playlists.",
                StatusCodes.Status409Conflict);
        }

        var playlist = await apiClient.GetPlaylistWriteSnapshotAsync(
            accessToken,
            playlistId,
            cancellationToken);
        if ((string.IsNullOrWhiteSpace(profile.Id) || !string.Equals(playlist.OwnerId, profile.Id, StringComparison.Ordinal))
            && !playlist.IsCollaborative)
        {
            throw new PlatformApiException(
                "spotify_playlist_not_writable",
                "The linked Spotify playlist is no longer writable by the connected account.",
                StatusCodes.Status403Forbidden);
        }

        var requiredScope = playlist.IsPublic == true
            ? "playlist-modify-public"
            : "playlist-modify-private";
        var scopes = (account.Scopes ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (!scopes.Contains(requiredScope, StringComparer.Ordinal))
        {
            throw new PlatformApiException(
                "spotify_write_scope_required",
                $"Reconnect Spotify and grant {requiredScope} to sync this playlist from Cantaro.",
                StatusCodes.Status409Conflict);
        }

        return playlist;
    }

    private async Task ValidateCreationAccessAsync(
        string accessToken, ConnectedServiceAccount account, CancellationToken cancellationToken)
    {
        RequirePrivateWriteScope(account);
        var profile = await apiClient.GetProfileAsync(accessToken, cancellationToken);
        if (!string.Equals(profile.AccountId ?? profile.Id, account.ExternalAccountId, StringComparison.Ordinal))
        {
            throw new PlatformApiException(
                "spotify_account_changed",
                "The connected Spotify account changed. Reconnect Spotify before creating a playlist.",
                StatusCodes.Status409Conflict);
        }
    }

    private static void RequirePrivateWriteScope(ConnectedServiceAccount account)
    {
        var scopes = (account.Scopes ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (!scopes.Contains("playlist-modify-private", StringComparer.Ordinal)
            || !scopes.Contains("playlist-read-private", StringComparer.Ordinal))
        {
            throw new PlatformApiException(
                "spotify_write_scope_required",
                "Reconnect Spotify to allow Cantaro to create and sync playlists.",
                StatusCodes.Status409Conflict);
        }
    }

    private static PlatformApiException CreationOutcomeUnknown(Exception? exception = null) => new(
        "spotify_playlist_creation_unknown",
        "Spotify may have created the playlist, but Cantaro did not receive its ID. Check Spotify before trying again.",
        StatusCodes.Status409Conflict,
        innerException: exception);

    private static PlatformApiException CreationRejected(PlatformApiException ex) => new(
        ex.Code == "spotify_quota_exceeded" ? ex.Code : "spotify_playlist_creation_rejected",
        ex.Message, ex.StatusCode, ex.RetryAfter, ex);

    private async Task WithAuthorizedRetryAsync(
        PlatformAccountContext context,
        Func<string, ConnectedServiceAccount, CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        var token = await tokenManager.GetAccessTokenSnapshotAsync(context, false, cancellationToken);
        var account = await ReadActiveAccountAsync(context, cancellationToken);
        try
        {
            await operation(token.Value, account, cancellationToken);
        }
        catch (PlatformApiException ex) when (ex.StatusCode == StatusCodes.Status401Unauthorized)
        {
            token = await tokenManager.GetAccessTokenSnapshotAsync(context, true, cancellationToken);
            account = await ReadActiveAccountAsync(context, cancellationToken);
            try
            {
                // A write may have completed before its response failed. The operation
                // always re-reads provider state before another mutation.
                await operation(token.Value, account, cancellationToken);
            }
            catch (PlatformApiException retryException) when (
                retryException.StatusCode == StatusCodes.Status401Unauthorized)
            {
                var invalidated = await tokenManager.MarkReconnectRequiredAsync(
                    context,
                    token.Version,
                    "access_token_rejected",
                    cancellationToken);
                if (invalidated)
                {
                    throw new PlatformReconnectRequiredException(
                        "Spotify rejected the refreshed connection. Reconnect Spotify to continue.",
                        retryException);
                }

                // A concurrent refresh or reconnection replaced the rejected token.
                // Give that generation one attempt after reading provider state again.
                var newest = await tokenManager.GetAccessTokenSnapshotAsync(
                    context,
                    false,
                    cancellationToken);
                var latestAccount = await ReadActiveAccountAsync(context, cancellationToken);
                await operation(newest.Value, latestAccount, cancellationToken);
            }
        }
    }

    private async Task<ConnectedServiceAccount> ReadActiveAccountAsync(
        PlatformAccountContext context,
        CancellationToken cancellationToken)
    {
        var account = await dbContext.ConnectedServiceAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate =>
                candidate.Id == context.ConnectedServiceAccountId
                && candidate.UserId == context.UserId
                && candidate.Service == SpotifyService.ServiceName,
                cancellationToken);
        if (account is null || account.ConnectionState != "connected")
        {
            throw new PlatformApiException(
                "spotify_not_connected",
                "The selected Spotify account is no longer connected. Reconnect Spotify to continue.",
                StatusCodes.Status409Conflict);
        }
        if (context.ExpectedExternalAccountId is { } expectedAccountId
            && !string.Equals(account.ExternalAccountId, expectedAccountId, StringComparison.Ordinal))
        {
            throw new PlatformApiException(
                "spotify_account_changed",
                "The destination account changed. Reconnect the original Spotify account before syncing.",
                StatusCodes.Status409Conflict);
        }
        return account;
    }

    private static void ValidateId(string? id, string kind)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100 || !id.All(char.IsLetterOrDigit))
        {
            throw new PlatformApiException(
                "spotify_invalid_id",
                $"Invalid Spotify {kind} ID format.",
                StatusCodes.Status400BadRequest);
        }
    }
}
