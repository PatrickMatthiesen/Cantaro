using Cantaro.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

/// <summary>Uses persisted due times for daily runs and bounded per-link retries.</summary>
public sealed class PlaylistSyncScheduler(IServiceScopeFactory scopes, IConfiguration configuration,
    TimeProvider clock, ILogger<PlaylistSyncScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await ProcessNextAsync(stoppingToken)) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning(ex, "Playlist scheduling could not complete a run"); }
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }

    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        if (!configuration.GetValue("MusicPlaylistSync:Enabled", true)) return false;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var coordinator = scope.ServiceProvider.GetRequiredService<PlaylistSyncCoordinator>();
        var now = clock.GetUtcNow();
        var dueLink = await db.ServicePlaylistMappings.AsNoTracking().Where(link =>
            (link.State == "active" || (link.State == "paused" && link.InitialMode != null
                && (link.LastSyncStatus == "rate_limited" || link.LastSyncStatus == "quota_limited")))
            && link.NextAttemptAt != null && link.NextAttemptAt <= now
            && (link.Playlist!.SyncLeaseExpiresAt == null || link.Playlist.SyncLeaseExpiresAt <= now)
            && link.ConnectedServiceAccount!.ConnectionState == "connected"
            && link.ConnectedServiceAccount.ExternalAccountId == link.ExternalAccountId)
            .OrderBy(link => link.NextAttemptAt)
            .Select(link => new { link.Id, link.UserId, link.PlaylistId, link.Service, link.InitialMode }).FirstOrDefaultAsync(ct);
        if (dueLink is not null)
        {
            try
            {
                if (dueLink.InitialMode is { } mode)
                    await coordinator.InitializeLinkAsync(dueLink.UserId, dueLink.PlaylistId, dueLink.Id, mode, ct);
                await coordinator.RunAsync(dueLink.UserId, dueLink.PlaylistId, dueLink.Service, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                db.ChangeTracker.Clear();
                var link = await db.ServicePlaylistMappings.FindAsync([dueLink.Id], ct);
                if (link is not null)
                {
                    if (ex is PlatformApiException { StatusCode: 429 } rateLimit)
                    {
                        link.LastSyncStatus = IsSpotifyQuotaException(rateLimit) ? "quota_limited" : "rate_limited";
                        link.LastError = null;
                        link.NextAttemptAt = clock.GetUtcNow().Add(rateLimit.RetryAfter is { } retryAfter && retryAfter > TimeSpan.Zero
                            ? retryAfter : link.Service == "spotify"
                                ? (IsSpotifyQuotaException(rateLimit) ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(30))
                                : TimeSpan.FromMinutes(1));
                    }
                    else
                    {
                        link.AttemptCount++;
                        link.LastSyncStatus = "error";
                        var failure = MusicSyncFailureClassifier.Classify(ex);
                        link.LastError = failure.Message;
                        var delay = TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, link.AttemptCount)));
                        if (ex is PlatformApiException { RetryAfter: { } providerDelay } && providerDelay > delay) delay = providerDelay;
                        var retryable = failure.Retryable || failure.Code is "remote_changed" or "sync_in_progress";
                        link.NextAttemptAt = retryable && link.AttemptCount < 5 ? clock.GetUtcNow().Add(delay) : null;
                    }
                    await db.SaveChangesAsync(ct);
                }
            }
            return true;
        }
        if (!configuration.GetValue("MusicPlaylistSync:AutomaticEnabled", true)) return false;
        var playlist = await db.Playlists.AsNoTracking().Where(item => item.SyncEnabled && item.NextSyncAt != null
            && item.NextSyncAt <= now && (item.SyncLeaseExpiresAt == null || item.SyncLeaseExpiresAt <= now)
            && !item.ServiceMappings.Any(link => link.State == "active" && link.NextAttemptAt > now))
            .OrderBy(item => item.NextSyncAt).Select(item => new { item.Id, item.UserId }).FirstOrDefaultAsync(ct);
        if (playlist is null) return false;
        try { await coordinator.RunAsync(playlist.UserId, playlist.Id, null, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Daily sync failed for playlist {PlaylistId}", playlist.Id);
            db.ChangeTracker.Clear();
            var failed = await db.Playlists.FindAsync([playlist.Id], ct);
            if (failed is not null)
            {
                failed.NextSyncAt = clock.GetUtcNow().AddDays(1);
                await db.SaveChangesAsync(ct);
            }
        }
        return true;
    }

    private static bool IsSpotifyQuotaException(PlatformApiException exception)
        => exception.Code == "spotify_quota_exceeded"
            || exception.InnerException is PlatformApiException { Code: "spotify_quota_exceeded" };
}
