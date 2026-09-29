using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

public sealed class OutboundPlaylistSyncService(
    ApplicationDbContext dbContext,
    IEnumerable<IPlaylistWriter> writers,
    PlaylistSyncCoordinator? coordinator = null)
{
    private const string PendingDestinationPrefix = "pending:";

    public async Task<string?> ValidateAsync(
        PlatformAccountContext account, string service, Guid playlistId, string? requestedDestinationId,
        CancellationToken cancellationToken)
    {
        if (coordinator is not null)
        {
            var linked = await PrepareCoordinatorAsync(account, service, playlistId,
                requestedDestinationId, cancellationToken);
            ThrowIfCooldown(linked, service);
            var coordinatorWriter = GetWriter(service);
            if (linked is null) await coordinatorWriter.ValidateCreationAsync(account, cancellationToken);
            else await coordinatorWriter.ValidateDestinationAsync(account, linked.ServicePlaylistId, cancellationToken);
            return linked?.ServicePlaylistId;
        }
        var prepared = await PrepareAsync(account, service, playlistId, requestedDestinationId, cancellationToken);
        ThrowIfCooldown(prepared.Mapping, service);
        var writer = GetWriter(service);
        if (prepared.Mapping is { } mapping)
        {
            await writer.ValidateDestinationAsync(account, mapping.ServicePlaylistId, cancellationToken);
            return mapping.ServicePlaylistId;
        }

        await writer.ValidateCreationAsync(account, cancellationToken);
        return null;
    }

    public async Task<Guid> SyncAsync(
        PlatformAccountContext account, string service, Guid playlistId, string? requestedDestinationId,
        CancellationToken cancellationToken)
    {
        if (coordinator is not null)
        {
            var linked = await PrepareCoordinatorAsync(account, service, playlistId,
                requestedDestinationId, cancellationToken);
            ThrowIfCooldown(linked, service);
            if (linked is null)
                await coordinator.CreateLinkAsync(account.UserId, playlistId, service, cancellationToken);
            await coordinator.QueueAsync(account.UserId, playlistId, service, cancellationToken);
            await coordinator.RunAsync(account.UserId, playlistId, service, cancellationToken);
            linked = await dbContext.ServicePlaylistMappings.AsNoTracking().SingleOrDefaultAsync(item =>
                item.PlaylistId == playlistId && item.UserId == account.UserId
                && item.Service == service && item.ConnectedServiceAccountId == account.ConnectedServiceAccountId
                && item.ExternalAccountId == account.ExpectedExternalAccountId && item.State == "active",
                cancellationToken);
            if (linked is null)
                throw new PlatformApiException("playlist_sync_failed",
                    "The linked playlist is unavailable. Review its connection before retrying.", 409);
            ThrowIfCooldown(linked, service);
            if (linked.LastSyncStatus == "partial")
                throw new PlatformApiException("unmatched_tracks",
                    $"{linked.UnresolvedCount} tracks remain unmatched. Matched tracks were synced; resolve the remaining matches.", 422);
            if (linked.LastSyncStatus != "success")
                throw new PlatformApiException("playlist_sync_failed",
                    linked.LastError ?? "The linked playlist did not finish syncing. Review its status before retrying.", 409);
            return playlistId;
        }
        var prepared = await PrepareAsync(account, service, playlistId, requestedDestinationId, cancellationToken);
        var writer = GetWriter(service);
        var mapping = prepared.Mapping;
        ThrowIfCooldown(mapping, service);
        if (mapping is null)
        {
            await writer.ValidateCreationAsync(account, cancellationToken);
            var pendingId = $"{PendingDestinationPrefix}{Guid.NewGuid():N}";
            mapping = new ServicePlaylistMapping
            {
                Id = Guid.NewGuid(),
                PlaylistId = playlistId,
                ConnectedServiceAccountId = account.ConnectedServiceAccountId,
                UserId = account.UserId,
                ExternalAccountId = account.ExpectedExternalAccountId ?? await dbContext.ConnectedServiceAccounts
                    .Where(item => item.Id == account.ConnectedServiceAccountId).Select(item => item.ExternalAccountId).SingleAsync(cancellationToken),
                Service = service,
                ServicePlaylistId = pendingId,
                SyncMode = "from_cantaro",
                LastSyncStatus = "creating"
            };
            dbContext.ServicePlaylistMappings.Add(mapping);
            await dbContext.SaveChangesAsync(cancellationToken);

            try
            {
                var createdId = await writer.CreatePlaylistAsync(account, prepared.Playlist.Name, cancellationToken);
                if (string.IsNullOrWhiteSpace(createdId))
                {
                    throw new InvalidOperationException("The provider did not return a playlist ID.");
                }

                mapping.ServicePlaylistId = createdId;
                mapping.LastSyncStatus = "running";
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (PlatformApiException ex) when (ex.Code is "youtube_playlist_creation_rejected"
                or "spotify_playlist_creation_rejected" or "spotify_quota_exceeded")
            {
                dbContext.ServicePlaylistMappings.Remove(mapping);
                await dbContext.SaveChangesAsync(CancellationToken.None);
                throw;
            }
            catch (Exception ex)
            {
                // A lost response or interrupted process might leave a remote
                // playlist behind. The durable marker prevents duplicate creates.
                mapping.ServicePlaylistId = pendingId;
                mapping.LastSyncStatus = "creation_uncertain";
                try { await dbContext.SaveChangesAsync(CancellationToken.None); }
                catch { /* A persisted creating marker also blocks a retry. */ }
                throw new PlatformApiException(
                    "destination_creation_uncertain",
                    $"Cantaro could not confirm whether {service} created the playlist. Check the provider before trying again.",
                    StatusCodes.Status409Conflict,
                    innerException: ex);
            }
        }
        else
        {
            try
            {
                await writer.ValidateDestinationAsync(account, mapping.ServicePlaylistId, cancellationToken);
            }
            catch (PlatformApiException ex) when (ex.StatusCode == StatusCodes.Status429TooManyRequests)
            {
                await RecordProviderCooldownAsync(mapping, service, ex);
                throw;
            }
            mapping.LastSyncStatus = "running";
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        try
        {
            await writer.ReconcileAsync(account, mapping.ServicePlaylistId, prepared.TrackIds, cancellationToken);
            mapping.LastSyncedAt = DateTimeOffset.UtcNow;
            mapping.LastSyncStatus = "success";
            mapping.NextAttemptAt = null;
            mapping.LastError = null;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (PlatformApiException ex) when (ex.StatusCode == StatusCodes.Status429TooManyRequests)
        {
            await RecordProviderCooldownAsync(mapping, service, ex);
            throw;
        }
        catch (Exception ex)
        {
            mapping.LastSyncStatus = "error";
            mapping.LastError = MusicSyncFailureClassifier.Classify(ex).Message;
            await dbContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        return playlistId;
    }

    private async Task<PreparedExport> PrepareAsync(
        PlatformAccountContext account, string service, Guid playlistId, string? requestedDestinationId,
        CancellationToken cancellationToken)
    {
        var playlist = await dbContext.Playlists.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == playlistId && item.UserId == account.UserId, cancellationToken)
            ?? throw new PlatformApiException("playlist_not_found", "Cantaro playlist not found.", 404);

        var connected = await dbContext.ConnectedServiceAccounts.AsNoTracking().AnyAsync(
            item => item.Id == account.ConnectedServiceAccountId && item.UserId == account.UserId
                && item.Service == service && item.ConnectionState == "connected"
                && (account.ExpectedExternalAccountId == null || item.ExternalAccountId == account.ExpectedExternalAccountId), cancellationToken);
        if (!connected)
        {
            throw new PlatformApiException("account_changed", "Reconnect the original destination account before syncing.", 409);
        }

        var mapping = await dbContext.ServicePlaylistMappings.SingleOrDefaultAsync(
            item => item.PlaylistId == playlistId
                && item.ConnectedServiceAccountId == account.ConnectedServiceAccountId
                && item.Service == service,
            cancellationToken);
        if (mapping is not null && (mapping.ServicePlaylistId.StartsWith(PendingDestinationPrefix, StringComparison.Ordinal)
            || mapping.LastSyncStatus is "creating" or "creation_uncertain"))
        {
            throw new PlatformApiException(
                "destination_creation_uncertain",
                $"Cantaro could not confirm whether {service} created this playlist. Check the provider before trying again.",
                StatusCodes.Status409Conflict);
        }

        if (mapping is { SyncMode: not "from_cantaro" }
            || (mapping is null && playlist.ImportedFromService == service))
        {
            throw new PlatformApiException(
                "import_only_source",
                $"This playlist came from {service}. Refresh it from {service}; use a Cantaro playlist to sync changes out.",
                StatusCodes.Status409Conflict);
        }

        if (!string.IsNullOrWhiteSpace(requestedDestinationId)
            && (mapping is null || !string.Equals(mapping.ServicePlaylistId, requestedDestinationId, StringComparison.Ordinal)))
        {
            throw new PlatformApiException(
                "unlinked_destination",
                "Cantaro can sync only to the playlist it created and linked for this source.",
                StatusCodes.Status409Conflict);
        }

        var entries = await dbContext.PlaylistEntries.AsNoTracking()
            .Where(item => item.PlaylistId == playlistId)
            .Include(item => item.Track).ThenInclude(track => track!.SourceIds)
            .Include(item => item.TrackObservation)
            .OrderBy(item => item.Position).ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);
        var trackIds = new List<string>(entries.Count);
        var unmatched = new List<string>();
        foreach (var entry in entries)
        {
            var observation = entry.TrackObservation;
            var providerId = observation?.SourceType == service
                && (entry.TrackId is null || observation.TrackId == entry.TrackId)
                ? observation.ExternalId
                : entry.Track?.SourceIds
                    .Where(source => source.SourceType == service && !string.IsNullOrWhiteSpace(source.ExternalId))
                    .OrderByDescending(source => source.Confidence)
                    .ThenByDescending(source => source.LastVerifiedAt)
                    .ThenBy(source => source.ExternalId, StringComparer.Ordinal)
                    .Select(source => source.ExternalId).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(providerId))
            {
                unmatched.Add(entry.Track?.SearchTitle ?? observation?.Title ?? $"Track {entry.Position + 1}");
            }
            else
            {
                trackIds.Add(providerId);
            }
        }

        if (unmatched.Count > 0)
        {
            var names = string.Join(", ", unmatched.Take(10).Select(name => name.Length > 100 ? name[..100] : name));
            throw new PlatformApiException("unmatched_tracks",
                $"{unmatched.Count} {(unmatched.Count == 1 ? "track has" : "tracks have")} no {service} match: {names}{(unmatched.Count > 10 ? ", ..." : "")}. Resolve their matches, then retry. The destination was not changed.",
                StatusCodes.Status422UnprocessableEntity);
        }

        return new PreparedExport(playlist, mapping, trackIds);
    }

    private IPlaylistWriter GetWriter(string service) => writers.SingleOrDefault(writer => writer.PlatformId == service)
        ?? throw new PlatformApiException("unsupported_destination", "This platform does not support outbound sync.", 400);

    private static bool IsSpotifyQuotaExceeded(PlatformApiException exception)
        => exception.Code == "spotify_quota_exceeded"
            || exception.InnerException is PlatformApiException { Code: "spotify_quota_exceeded" };

    private static void ThrowIfCooldown(ServicePlaylistMapping? mapping, string service)
    {
        if (mapping?.LastSyncStatus is not ("rate_limited" or "quota_limited")
            || mapping.NextAttemptAt is not { } nextAttemptAt
            || nextAttemptAt <= DateTimeOffset.UtcNow)
            return;
        var retryAfter = nextAttemptAt - DateTimeOffset.UtcNow;
        var quotaExceeded = mapping.LastSyncStatus == "quota_limited";
        throw new PlatformApiException(
            quotaExceeded ? "spotify_quota_exceeded" : $"{service}_rate_limited",
            quotaExceeded ? "Spotify's development quota is temporarily exhausted. Try again later."
                : $"{service} is rate limiting requests. Try again later.",
            StatusCodes.Status429TooManyRequests,
            retryAfter);
    }

    private async Task RecordProviderCooldownAsync(ServicePlaylistMapping mapping, string service,
        PlatformApiException exception)
    {
        var quotaExceeded = IsSpotifyQuotaExceeded(exception);
        mapping.LastSyncStatus = quotaExceeded ? "quota_limited" : "rate_limited";
        mapping.LastError = null;
        mapping.NextAttemptAt = DateTimeOffset.UtcNow.Add(exception.RetryAfter is { } retryAfter && retryAfter > TimeSpan.Zero
            ? retryAfter : service == "spotify"
                ? (quotaExceeded ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(30))
                : TimeSpan.FromMinutes(1));
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<ServicePlaylistMapping?> PrepareCoordinatorAsync(
        PlatformAccountContext account, string service, Guid playlistId, string? requestedDestinationId,
        CancellationToken ct)
    {
        var playlist = await dbContext.Playlists.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == playlistId && item.UserId == account.UserId, ct)
            ?? throw new PlatformApiException("playlist_not_found", "Cantaro playlist not found.", 404);
        var connected = await dbContext.ConnectedServiceAccounts.AsNoTracking().AnyAsync(item =>
            item.Id == account.ConnectedServiceAccountId && item.UserId == account.UserId
            && item.Service == service && item.ConnectionState == "connected"
            && account.ExpectedExternalAccountId != null
            && item.ExternalAccountId == account.ExpectedExternalAccountId, ct);
        if (!connected)
            throw new PlatformApiException("account_changed",
                "Reconnect the original destination account before syncing.", 409);

        var links = await dbContext.ServicePlaylistMappings.AsNoTracking().Where(item =>
            item.PlaylistId == playlistId && item.UserId == account.UserId && item.Service == service
            && item.ExternalAccountId == account.ExpectedExternalAccountId && item.State != "unlinked").ToListAsync(ct);
        if (links.Count > 1)
            throw new PlatformApiException("playlist_link_conflict", "Review this playlist's platform links before syncing.", 409);
        var mapping = links.SingleOrDefault();
        if (mapping is { SyncMode: "import_only" } || (mapping is null && playlist.ImportedFromService == service))
            throw new PlatformApiException("import_only_source",
                $"This playlist came from {service}. Refresh it from {service}; use a Cantaro playlist to sync changes out.", 409);
        if (mapping is not null && (mapping.State != "active"
            || mapping.ConnectedServiceAccountId != account.ConnectedServiceAccountId
            || mapping.ServicePlaylistId.StartsWith(PendingDestinationPrefix, StringComparison.Ordinal)))
            throw new PlatformApiException("playlist_link_unavailable",
                "Review this platform link before syncing it.", 409);
        if (!string.IsNullOrWhiteSpace(requestedDestinationId)
            && (mapping is null || !string.Equals(mapping.ServicePlaylistId, requestedDestinationId, StringComparison.Ordinal)))
            throw new PlatformApiException("unlinked_destination",
                "Cantaro can sync only to the playlist already linked to this source.", 409);
        return mapping;
    }

    private sealed record PreparedExport(Playlist Playlist, ServicePlaylistMapping? Mapping, IReadOnlyList<string> TrackIds);
}
