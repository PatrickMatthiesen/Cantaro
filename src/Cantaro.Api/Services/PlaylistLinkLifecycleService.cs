using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

public sealed record PlaylistLinkStatus(
    Guid MappingId, string Service, string ServicePlaylistId, string ExternalAccountId,
    string SyncMode, string State, string? LastSyncStatus, DateTimeOffset? LastSyncedAt,
    DateTimeOffset? NextAttemptAt, int UnresolvedCount, int ResolvedCount, int TotalCount,
    string? LastError, string? PendingName, bool CanDeleteRemote, bool ReconnectRequired = false,
    int MatchingProcessedCount = 0, int MatchingTotalCount = 0);

public sealed record PlaylistSyncStatus(
    Guid PlaylistId, string Name, bool SyncEnabled, bool AllowDuplicateTracks,
    DateTimeOffset? NextSyncAt, IReadOnlyList<PlaylistLinkStatus> Links);

public sealed record PlaylistAttachPreview(
    string Service, string ServicePlaylistId, string RemoteName, int RemoteTrackCount,
    int CantaroTrackCount, int Additions, int Removals, string PreviewToken, int UnavailableItemCount = 0);

public sealed record PlaylistCreateCandidate(
    string ServicePlaylistId, string Name, int RemoteTrackCount,
    int? SharedTrackCount, Guid? LinkedPlaylistId, string? ComparisonError);

public sealed record PlaylistCreatePreview(IReadOnlyList<PlaylistCreateCandidate> Candidates, string PreviewToken);

public sealed record PlaylistRenamePreview(
    string CurrentName, string ProposedName, IReadOnlyList<PlaylistRenameLinkPreview> Links,
    string PreviewToken);

public sealed record PlaylistRenameLinkPreview(Guid MappingId, string Service, string CurrentName, bool WillRename);

public sealed record PlaylistRenameProposalPreview(
    string CurrentName, string PendingName, string ProviderName, string PreviewToken);

public sealed record PlaylistDisconnectPreview(
    string PlatformId, string ExternalAccountId, IReadOnlyList<PlaylistDisconnectLinkPreview> Links);

public sealed record PlaylistDisconnectLinkPreview(
    Guid MappingId, Guid PlaylistId, string PlaylistName, string ServicePlaylistId,
    bool CanDeleteRemote, bool CanDeleteCanonicalIfLast);

public sealed record PlaylistDisconnectChoice(Guid MappingId, bool DeleteRemote, bool DeleteCanonicalIfLast);

public sealed class PlaylistLinkLifecycleService(
    ApplicationDbContext db,
    IPlatformRegistry platforms,
    IEnumerable<IPlaylistSyncProvider> providers,
    PlaylistSyncCoordinator coordinator,
    IDataProtectionProvider dataProtectionProvider)
{
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("Cantaro.PlaylistSync.Preview.v1");
    private readonly IReadOnlyDictionary<string, IPlaylistSyncProvider> _providers =
        providers.ToDictionary(provider => provider.PlatformId, StringComparer.Ordinal);

    public async Task<PlaylistSyncStatus> GetStatusAsync(int userId, Guid playlistId, CancellationToken ct)
    {
        var playlist = await RequirePlaylistAsync(userId, playlistId, ct);
        var count = await db.PlaylistEntries.CountAsync(item => item.PlaylistId == playlistId, ct);
        var mappings = await db.ServicePlaylistMappings.AsNoTracking()
            .Include(link => link.ConnectedServiceAccount)
            .Where(link => link.UserId == userId && link.PlaylistId == playlistId && link.State != "unlinked")
            .OrderBy(link => link.Service).ThenBy(link => link.ServicePlaylistId)
            .ToListAsync(ct);
        var links = mappings.Select(link => new PlaylistLinkStatus(
                link.Id, link.Service, link.ServicePlaylistId, link.ExternalAccountId,
                link.SyncMode, link.State,
                link.LastSyncStatus == "running" && (playlist.SyncLeaseId is null
                    || playlist.SyncLeaseExpiresAt is null
                    || playlist.SyncLeaseExpiresAt <= DateTimeOffset.UtcNow)
                    ? "pending"
                    : link.BaselineJson == null && link.LastSyncStatus == "success"
                        ? "awaiting_first_sync" : link.LastSyncStatus,
                link.BaselineJson == null ? null : link.LastSyncedAt,
                link.NextAttemptAt, link.UnresolvedCount,
                link.BaselineJson != null && link.LastSyncedAt != null
                    ? Math.Max(0, count - link.UnresolvedCount) : 0,
                count,
                link.LastError, link.PendingName,
                link.Service == "youtube" && link.ConnectedServiceAccount != null
                    && link.ConnectedServiceAccount.ConnectionState == "connected"
                    && link.ConnectedServiceAccount.ExternalAccountId == link.ExternalAccountId,
                NeedsReconnect(link), link.MatchingProcessedCount, link.MatchingTotalCount))
            .ToList();
        return new PlaylistSyncStatus(playlist.Id, playlist.Name, playlist.SyncEnabled,
            playlist.AllowDuplicateTracks, playlist.NextSyncAt, links);
    }

    private static bool NeedsReconnect(ServicePlaylistMapping link)
    {
        var account = link.ConnectedServiceAccount;
        if (link.State == "reconnect_required" || account is null
            || account.ConnectionState != "connected"
            || account.ExternalAccountId != link.ExternalAccountId)
            return true;

        // A failed Cantaro-created Spotify playlist has no provider ID yet. It can
        // only be retried when the account has the private-playlist read/write scopes.
        // Existing public playlists have service-specific write requirements, so do
        // not infer missing scopes from their stored scope set here.
        if (link.Service == "spotify" && link.State == "creation_failed"
            && link.ServicePlaylistId.StartsWith("pending:", StringComparison.Ordinal)
            && link.SyncMode != "import_only")
        {
            var scopes = (account.Scopes ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return !scopes.Contains("playlist-read-private", StringComparer.Ordinal)
                || !scopes.Contains("playlist-modify-private", StringComparer.Ordinal);
        }

        return false;
    }

    public async Task<PlaylistAttachPreview> PreviewAttachAsync(
        int userId, Guid playlistId, string service, string servicePlaylistId, CancellationToken ct)
    {
        var prepared = await PrepareAttachAsync(userId, playlistId, service, servicePlaylistId, ct);
        var token = Protect(new PreviewProof(userId, playlistId, "attach", service,
            servicePlaylistId, null, prepared.Account.ExternalAccountId,
            prepared.Fingerprint, null, DateTimeOffset.UtcNow));
        return new PlaylistAttachPreview(service, servicePlaylistId, prepared.Remote.Name,
            prepared.Remote.Tracks.Count, prepared.Canonical.Count, prepared.Additions,
            prepared.Removals, token, prepared.Remote.UnavailableItemCount);
    }

    public async Task<PlaylistSyncStatus> AttachAsync(
        int userId, Guid playlistId, string service, string servicePlaylistId,
        string syncMode, string initialMode, string previewToken, CancellationToken ct)
    {
        if (syncMode is not ("bidirectional" or "import_only" or "from_cantaro")
            || initialMode is not ("combine" or "platform" or "cantaro"))
            throw new PlatformApiException("invalid_link_options", "Choose a sync direction and initial contents.", 400);

        var prepared = await PrepareAttachAsync(userId, playlistId, service, servicePlaylistId, ct);
        Verify(previewToken, userId, playlistId, "attach", service, servicePlaylistId,
            null, prepared.Account.ExternalAccountId, prepared.Fingerprint, null);
        await EnsureNoActiveSyncAsync(userId, playlistId, ct);

        var linked = await db.ServicePlaylistMappings.SingleOrDefaultAsync(link =>
            link.UserId == userId && link.Service == service
            && link.ExternalAccountId == prepared.Account.ExternalAccountId
            && link.ServicePlaylistId == servicePlaylistId && link.State != "unlinked", ct);
        if (linked is not null && linked.PlaylistId != playlistId)
            throw new PlatformApiException("playlist_already_linked", "That provider playlist belongs to another Cantaro playlist.", 409);
        if (linked is not null && linked.InitialMode is null)
            throw new PlatformApiException("playlist_already_linked", "That provider playlist is already linked.", 409);

        var unfinishedCreation = await db.ServicePlaylistMappings.SingleOrDefaultAsync(link =>
            link.UserId == userId && link.PlaylistId == playlistId && link.Service == service
            && link.ExternalAccountId == prepared.Account.ExternalAccountId
            && (link.State == "creation_uncertain" || link.State == "creation_failed"
                || link.State == "creating") && link.ServicePlaylistId.StartsWith("pending:"), ct);
        if (unfinishedCreation?.State == "creating"
            && prepared.Playlist.SyncLeaseExpiresAt > DateTimeOffset.UtcNow)
            throw new PlatformApiException("creation_in_progress",
                "Wait for the current playlist creation to finish before attaching its result.", 409);
        var otherLinked = await db.ServicePlaylistMappings.AnyAsync(link =>
            link.UserId == userId && link.PlaylistId == playlistId && link.Service == service
            && link.ExternalAccountId == prepared.Account.ExternalAccountId
            && link.ServicePlaylistId != servicePlaylistId && link.State != "unlinked"
            && link.Id != (unfinishedCreation == null ? Guid.Empty : unfinishedCreation.Id), ct);
        if (otherLinked)
            throw new PlatformApiException("playlist_already_linked",
                "Unlink the current provider playlist before linking another one.", 409);

        var existing = linked ?? unfinishedCreation ?? await db.ServicePlaylistMappings
            .Where(link => link.UserId == userId && link.PlaylistId == playlistId
                && link.Service == service && link.ExternalAccountId == prepared.Account.ExternalAccountId
                && link.ServicePlaylistId == servicePlaylistId && link.State == "unlinked")
            .OrderBy(link => link.Id).FirstOrDefaultAsync(ct);

        var obsoleteTombstones = await db.ServicePlaylistMappings.Where(link =>
            link.UserId == userId && link.PlaylistId == playlistId && link.Service == service
            && link.ExternalAccountId == prepared.Account.ExternalAccountId
            && link.State == "unlinked" && link.ServicePlaylistId != servicePlaylistId).ToListAsync(ct);
        db.ServicePlaylistMappings.RemoveRange(obsoleteTombstones);

        var link = existing ?? new ServicePlaylistMapping
        {
            Id = Guid.NewGuid(), UserId = userId, PlaylistId = playlistId,
            ConnectedServiceAccountId = prepared.Account.Id,
            ExternalAccountId = prepared.Account.ExternalAccountId,
            Service = service, ServicePlaylistId = servicePlaylistId,
            SyncMode = syncMode, State = "paused", InitialMode = initialMode,
            LastSyncStatus = "initializing"
        };
        if (existing is { State: "unlinked" or "creation_uncertain" or "creation_failed" or "creating" })
        {
            link.ServicePlaylistId = servicePlaylistId;
            link.State = "paused";
            link.InitialMode = initialMode;
            link.SyncMode = syncMode;
            link.ConnectedServiceAccountId = prepared.Account.Id;
            link.BaselineJson = null;
            link.BaselineName = null;
            link.PendingName = null;
            link.RejectedName = null;
            link.DesiredName = null;
            link.LastError = null;
            link.LastSyncStatus = "initializing";
            link.PendingWriteJson = null;
            link.MatchingProgressJson = null;
            link.MatchingProcessedCount = 0;
            link.MatchingTotalCount = 0;
            link.NextAttemptAt = null;
            link.AttemptCount = 0;
            await db.SaveChangesAsync(ct);
        }
        else if (existing is null)
        {
            db.ServicePlaylistMappings.Add(link);
            await db.SaveChangesAsync(ct);
        }
        else if (link.InitialMode != initialMode || link.SyncMode != syncMode)
        {
            throw new PlatformApiException("initialization_pending",
                "Retry this link with its original direction and initial contents.", 409);
        }

        try
        {
            await coordinator.InitializeLinkAsync(userId, playlistId, link.Id, initialMode, ct);
        }
        catch (Exception ex)
        {
            // Initialization may have rolled back a relational transaction. Do not save
            // tracked canonical changes from that failed transaction along with the error.
            db.ChangeTracker.Clear();
            var pending = await db.ServicePlaylistMappings.SingleOrDefaultAsync(item => item.Id == link.Id,
                CancellationToken.None);
            if (pending is { InitialMode: not null })
            {
                if (ex is PlatformApiException { StatusCode: 429 } rateLimit)
                {
                    pending.LastSyncStatus = IsSpotifyQuotaExceeded(rateLimit) ? "quota_limited" : "rate_limited";
                    pending.LastError = null;
                    pending.NextAttemptAt = DateTimeOffset.UtcNow.Add(rateLimit.RetryAfter is { } retryAfter && retryAfter > TimeSpan.Zero
                        ? retryAfter : pending.Service == "spotify"
                            ? (IsSpotifyQuotaExceeded(rateLimit) ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(30))
                            : TimeSpan.FromMinutes(1));
                }
                else
                {
                    pending.LastSyncStatus = "initialization_pending";
                    pending.LastError = "Initialization did not finish. Refresh the preview and retry the same choices.";
                }
                await db.SaveChangesAsync(CancellationToken.None);
            }
            throw;
        }

        return await GetStatusAsync(userId, playlistId, ct);
    }

    public async Task<PlaylistAttachPreview> PreviewPendingInitializationAsync(
        int userId, Guid playlistId, Guid mappingId, CancellationToken ct)
    {
        var link = await RequirePendingInitializationAsync(userId, playlistId, mappingId, ct);
        return await PreviewAttachAsync(userId, playlistId, link.Service, link.ServicePlaylistId, ct);
    }

    public async Task<PlaylistSyncStatus> RetryPendingInitializationAsync(
        int userId, Guid playlistId, Guid mappingId, string previewToken, CancellationToken ct)
    {
        var link = await RequirePendingInitializationAsync(userId, playlistId, mappingId, ct);
        return await AttachAsync(userId, playlistId, link.Service, link.ServicePlaylistId,
            link.SyncMode, link.InitialMode!, previewToken, ct);
    }

    public async Task<PlaylistSyncStatus> RunAsync(int userId, Guid playlistId, string? service, CancellationToken ct)
    {
        await coordinator.QueueAsync(userId, playlistId, service, ct);
        return await GetStatusAsync(userId, playlistId, ct);
    }

    public async Task<PlaylistCreatePreview> PreviewCreateAsync(
        int userId, Guid playlistId, string service, CancellationToken ct)
    {
        var prepared = await PrepareCreateAsync(userId, playlistId, service, ct);
        var token = Protect(new PreviewProof(userId, playlistId, "create", service, null, null,
            prepared.Account.ExternalAccountId, prepared.Fingerprint, null, DateTimeOffset.UtcNow));
        return new PlaylistCreatePreview(prepared.Candidates, token);
    }

    public async Task<PlaylistSyncStatus> CreateLinkAsync(
        int userId, Guid playlistId, string service, string? previewToken, CancellationToken ct)
    {
        await RequirePlaylistAsync(userId, playlistId, ct);
        await coordinator.CreateLinkAsync(userId, playlistId, service, async token =>
        {
            var prepared = await PrepareCreateAsync(userId, playlistId, service, token);
            if (string.IsNullOrEmpty(previewToken))
            {
                if (prepared.Candidates.Count > 0)
                    throw new PlatformApiException("create_preview_required",
                        "Review the existing playlists with this name before creating another one.", 409);
            }
            else Verify(previewToken, userId, playlistId, "create", service, null, null,
                prepared.Account.ExternalAccountId, prepared.Fingerprint, null);
        }, ct);
        return await GetStatusAsync(userId, playlistId, ct);
    }

    private async Task<PreparedCreate> PrepareCreateAsync(
        int userId, Guid playlistId, string service, CancellationToken ct)
    {
        var playlist = await RequirePlaylistAsync(userId, playlistId, ct);
        if (service is not ("youtube" or "spotify") || !platforms.IsSupported(service)
            || !_providers.ContainsKey(service))
            throw new PlatformApiException("invalid_platform", "Choose YouTube or Spotify.", 400);
        var account = await RequireAccountAsync(userId, service, ct);
        var platform = platforms.GetRequired(service);
        var available = await platform.GetPlaylistsAsync(
            new PlatformAccountContext(userId, account.Id, account.ExternalAccountId), ct);
        var sameName = available.Where(remote => NormalizePlaylistName(remote.Title) == NormalizePlaylistName(playlist.Name))
            .OrderBy(remote => remote.Id, StringComparer.Ordinal).ToList();
        var ids = sameName.Select(remote => remote.Id).ToList();
        var linked = await db.ServicePlaylistMappings.AsNoTracking()
            .Where(link => link.UserId == userId && link.Service == service
                && link.ExternalAccountId == account.ExternalAccountId
                && ids.Contains(link.ServicePlaylistId) && link.State != "unlinked")
            .ToListAsync(ct);
        var comparisons = new List<(PlatformPlaylistDto Playlist, Guid? LinkedPlaylistId,
            PlaylistRemoteSnapshot? Snapshot, string? Error)>(sameName.Count);
        foreach (var remote in sameName)
        {
            var mapping = linked.FirstOrDefault(link => link.ServicePlaylistId == remote.Id);
            try
            {
                var snapshot = await Provider(service).ReadAsync(
                    new(userId, account.Id, account.ExternalAccountId), remote.Id, ct);
                if (!snapshot.IsComplete || snapshot.Tracks.Any(track =>
                        track.IsAvailable && track.TrackId is null && track.ObservationId is null))
                    throw new PlatformApiException("incomplete_playlist", "Could not compare all tracks.", 503);
                comparisons.Add((remote, mapping?.PlaylistId, snapshot, null));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                comparisons.Add((remote, mapping?.PlaylistId, null,
                    ex is PlatformApiException ? ex.Message : "Could not compare tracks. Open this playlist to check its contents."));
            }
        }
        // Provider reads can import or reconcile observations. Count local identities only
        // after every candidate has been read, as in the attach preview.
        var canonical = await db.PlaylistEntries.AsNoTracking()
            .Include(entry => entry.TrackObservation)
            .Where(entry => entry.PlaylistId == playlistId)
            .OrderBy(entry => entry.Position).ThenBy(entry => entry.Id).ToListAsync(ct);
        var localCounts = CountKeys(canonical.Select(entry => LocalKey(entry, service)));
        var candidates = new List<PlaylistCreateCandidate>(sameName.Count);
        var revisions = new List<object>(sameName.Count);
        foreach (var comparison in comparisons)
        {
            var remote = comparison.Playlist;
            if (comparison.Snapshot is { } snapshot)
            {
                var remoteCounts = CountKeys(snapshot.Tracks
                    .Where(track => track.TrackId is not null || track.ObservationId is not null)
                    .Select(track => RemoteKey(track, service)));
                var shared = remoteCounts.Sum(pair => Math.Min(pair.Value, localCounts.GetValueOrDefault(pair.Key)));
                candidates.Add(new(remote.Id, remote.Title, snapshot.Tracks.Count,
                    shared, comparison.LinkedPlaylistId, null));
                revisions.Add(new { remote.Id, snapshot.Name, Snapshot = (object)snapshot,
                    comparison.LinkedPlaylistId });
            }
            else
            {
                candidates.Add(new(remote.Id, remote.Title, remote.ItemCount,
                    null, comparison.LinkedPlaylistId, comparison.Error));
                revisions.Add(new { remote.Id, remote.Title, remote.ItemCount,
                    comparison.LinkedPlaylistId, ComparisonError = comparison.Error });
            }
        }
        var fingerprint = Fingerprint(JsonSerializer.Serialize(new
        {
            playlist.Id, playlist.Name, playlist.SyncRevision, playlist.AllowDuplicateTracks,
            AccountId = account.Id, account.ExternalAccountId,
            Local = canonical.Select(entry => new { entry.Id, entry.Position, entry.TrackId, entry.TrackObservationId }),
            Candidates = revisions
        }));
        return new PreparedCreate(account, candidates, fingerprint);
    }

    internal static string NormalizePlaylistName(string name)
        => name.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();

    public async Task<PlaylistSyncStatus> ResolveOrderAsync(
        int userId, Guid playlistId, Guid mappingId, string use, CancellationToken ct)
    {
        await coordinator.ResolveOrderAsync(userId, playlistId, mappingId, use, ct);
        return await GetStatusAsync(userId, playlistId, ct);
    }

    public async Task<PlaylistSyncStatus> SetEnabledAsync(int userId, Guid playlistId, bool enabled, CancellationToken ct)
    {
        if (enabled) await coordinator.EnableAsync(userId, playlistId, ct);
        else
        {
            var playlist = await RequirePlaylistAsync(userId, playlistId, ct);
            await EnsureNoActiveSyncAsync(userId, playlistId, ct);
            playlist.SyncEnabled = false;
            playlist.NextSyncAt = null;
            await db.SaveChangesAsync(ct);
        }
        return await GetStatusAsync(userId, playlistId, ct);
    }

    public async Task<PlaylistSyncStatus> SetDuplicatesAsync(int userId, Guid playlistId, bool allowed, CancellationToken ct)
    {
        var playlist = await RequirePlaylistAsync(userId, playlistId, ct);
        await EnsureNoActiveSyncAsync(userId, playlistId, ct);
        if (playlist.AllowDuplicateTracks != allowed)
        {
            playlist.AllowDuplicateTracks = allowed;
            playlist.SyncRevision++;
            if (playlist.SyncEnabled) playlist.NextSyncAt ??= DateTimeOffset.UtcNow.AddDays(1);
            await db.SaveChangesAsync(ct);
        }
        return await GetStatusAsync(userId, playlistId, ct);
    }

    public async Task<PlaylistRenamePreview> PreviewRenameAsync(
        int userId, Guid playlistId, string name, CancellationToken ct)
    {
        name = RequireName(name);
        var prepared = await PrepareRenameAsync(userId, playlistId, name, ct);
        var token = Protect(new PreviewProof(userId, playlistId, "rename", null, null, null,
            null, prepared.Fingerprint, name, DateTimeOffset.UtcNow));
        return new PlaylistRenamePreview(prepared.Playlist.Name, name,
            prepared.Links.Select(link => new PlaylistRenameLinkPreview(
                link.Mapping.Id, link.Mapping.Service, link.RemoteName,
                link.Mapping.SyncMode != "import_only" && link.RemoteName != name)).ToArray(), token);
    }

    public async Task<PlaylistSyncStatus> ConfirmRenameAsync(
        int userId, Guid playlistId, string name, string previewToken, CancellationToken ct)
    {
        name = RequireName(name);
        var prepared = await PrepareRenameAsync(userId, playlistId, name, ct);
        Verify(previewToken, userId, playlistId, "rename", null, null, null,
            null, prepared.Fingerprint, name);
        await EnsureNoActiveSyncAsync(userId, playlistId, ct);
        var now = DateTimeOffset.UtcNow;
        if (prepared.Playlist.Name != name)
        {
            prepared.Playlist.Name = name;
            prepared.Playlist.SyncRevision++;
            prepared.Playlist.UpdatedAt = now;
        }
        foreach (var link in prepared.Links.Where(item => item.Mapping.SyncMode != "import_only"))
        {
            link.Mapping.DesiredName = name;
            link.Mapping.NextAttemptAt = now;
            link.Mapping.PendingName = null;
            link.Mapping.RejectedName = null;
        }
        if (prepared.Playlist.SyncEnabled) prepared.Playlist.NextSyncAt = now;
        await db.SaveChangesAsync(ct);
        return await GetStatusAsync(userId, playlistId, ct);
    }

    public async Task<PlaylistRenameProposalPreview> PreviewRenameProposalAsync(
        int userId, Guid playlistId, Guid mappingId, CancellationToken ct)
    {
        var prepared = await PrepareProposalAsync(userId, playlistId, mappingId, ct);
        var token = Protect(new PreviewProof(userId, playlistId, "rename_proposal",
            prepared.Mapping.Service, prepared.Mapping.ServicePlaylistId, mappingId,
            prepared.Mapping.ExternalAccountId, prepared.Fingerprint,
            prepared.Mapping.PendingName, DateTimeOffset.UtcNow));
        return new PlaylistRenameProposalPreview(prepared.Playlist.Name,
            prepared.Mapping.PendingName!, prepared.RemoteName, token);
    }

    public async Task<PlaylistSyncStatus> DecideRenameProposalAsync(
        int userId, Guid playlistId, Guid mappingId, bool accept, string previewToken, CancellationToken ct)
    {
        var prepared = await PrepareProposalAsync(userId, playlistId, mappingId, ct);
        Verify(previewToken, userId, playlistId, "rename_proposal", prepared.Mapping.Service,
            prepared.Mapping.ServicePlaylistId, mappingId, prepared.Mapping.ExternalAccountId,
            prepared.Fingerprint, prepared.Mapping.PendingName);
        await EnsureNoActiveSyncAsync(userId, playlistId, ct);
        var proposedName = prepared.Mapping.PendingName!;
        prepared.Mapping.PendingName = null;
        prepared.Mapping.BaselineName = prepared.RemoteName;
        if (accept)
        {
            var now = DateTimeOffset.UtcNow;
            prepared.Playlist.Name = proposedName;
            prepared.Playlist.SyncRevision++;
            prepared.Playlist.UpdatedAt = now;
            prepared.Mapping.RejectedName = null;
            var writable = await db.ServicePlaylistMappings.Where(link =>
                link.PlaylistId == playlistId && link.UserId == userId
                && link.State == "active" && link.SyncMode != "import_only").ToListAsync(ct);
            foreach (var link in writable)
            {
                link.DesiredName = proposedName;
                link.NextAttemptAt = now;
            }
            if (prepared.Playlist.SyncEnabled) prepared.Playlist.NextSyncAt = now;
        }
        else
        {
            prepared.Mapping.RejectedName = proposedName;
        }
        await db.SaveChangesAsync(ct);
        return await GetStatusAsync(userId, playlistId, ct);
    }

    public Task UnlinkAsync(int userId, Guid playlistId, Guid mappingId,
        bool deleteRemote, bool deleteCanonicalIfLast, CancellationToken ct)
        => coordinator.WithPlaylistLeaseAsync(userId, playlistId,
            token => UnlinkUnderLeaseAsync(userId, playlistId, mappingId,
                deleteRemote, deleteCanonicalIfLast, token), ct);

    private async Task UnlinkUnderLeaseAsync(int userId, Guid playlistId, Guid mappingId,
        bool deleteRemote, bool deleteCanonicalIfLast, CancellationToken ct)
    {
        var playlist = await RequirePlaylistAsync(userId, playlistId, ct);
        var link = await db.ServicePlaylistMappings.SingleOrDefaultAsync(item =>
            item.Id == mappingId && item.UserId == userId && item.PlaylistId == playlistId
            && item.State != "unlinked", ct)
            ?? throw new PlatformApiException("link_not_found", "Playlist link not found.", 404);
        if (deleteCanonicalIfLast && await HasOtherConnectedLinkAsync(playlistId, mappingId, ct))
            throw new PlatformApiException("other_platforms_remain",
                "This Cantaro playlist is still linked to another connected platform.", 409);
        if (deleteRemote)
        {
            if (link.Service != "youtube")
                throw new PlatformApiException("remote_delete_unsupported",
                    "Spotify does not offer playlist deletion through its current API.", 409);
            var account = await RequireAccountAsync(userId, link.Service, ct);
            if (link.ConnectedServiceAccountId != account.Id || link.ExternalAccountId != account.ExternalAccountId)
                throw new PlatformApiException("account_changed", "Reconnect the original linked account before deleting its playlist.", 409);
            await Provider(link.Service).DeleteAsync(new(userId, account.Id, account.ExternalAccountId),
                link.ServicePlaylistId, ct);
        }

        link.State = "unlinked";
        link.NextAttemptAt = null;
        link.InitialMode = null;
        await db.SaveChangesAsync(ct);
        if (deleteCanonicalIfLast)
        {
            await DeleteCanonicalIfLastAsync(playlist, ct);
        }
    }

    public async Task<PlaylistDisconnectPreview> PreviewDisconnectAsync(
        int userId, string service, CancellationToken ct)
    {
        if (!platforms.IsSupported(service))
            throw new PlatformApiException("unsupported_platform", "Platform not found.", 404);
        var account = await db.ConnectedServiceAccounts.AsNoTracking().SingleOrDefaultAsync(item =>
            item.UserId == userId && item.Service == service && item.ConnectionState != "disconnected", ct)
            ?? throw new PlatformApiException("account_not_connected", "This platform is not connected.", 409);
        var links = await db.ServicePlaylistMappings.AsNoTracking()
            .Include(link => link.Playlist)
            .Where(link => link.UserId == userId && link.ConnectedServiceAccountId == account.Id
                && link.Service == service && link.State != "unlinked")
            .OrderBy(link => link.Playlist!.Name).ThenBy(link => link.Id)
            .ToListAsync(ct);
        var accountMappingIds = links.Select(link => link.Id).ToHashSet();
        var previews = new List<PlaylistDisconnectLinkPreview>(links.Count);
        foreach (var link in links)
        {
            var otherConnected = await db.ServicePlaylistMappings.AsNoTracking().AnyAsync(other =>
                other.PlaylistId == link.PlaylistId && !accountMappingIds.Contains(other.Id)
                && other.State != "unlinked" && other.ConnectedServiceAccount != null
                && other.ConnectedServiceAccount.ConnectionState != "disconnected"
                && other.ExternalAccountId == other.ConnectedServiceAccount.ExternalAccountId, ct);
            previews.Add(new PlaylistDisconnectLinkPreview(link.Id, link.PlaylistId,
                link.Playlist?.Name ?? "Playlist", link.ServicePlaylistId,
                service == "youtube" && account.ConnectionState == "connected"
                    && link.ExternalAccountId == account.ExternalAccountId, !otherConnected));
        }
        return new PlaylistDisconnectPreview(service, account.ExternalAccountId, previews);
    }

    public async Task DisconnectAsync(int userId, string service,
        IReadOnlyList<PlaylistDisconnectChoice> choices, CancellationToken ct)
    {
        var preview = await PreviewDisconnectAsync(userId, service, ct);
        var playlistIds = preview.Links.Select(link => link.PlaylistId).Distinct().OrderBy(id => id).ToArray();
        await WithPlaylistLeasesAsync(userId, playlistIds, 0,
            token => DisconnectUnderLeasesAsync(userId, service, choices, token), ct);
    }

    private Task WithPlaylistLeasesAsync(int userId, IReadOnlyList<Guid> playlistIds, int index,
        Func<CancellationToken, Task> action, CancellationToken ct)
        => index == playlistIds.Count ? action(ct)
            : coordinator.WithPlaylistLeaseAsync(userId, playlistIds[index],
                token => WithPlaylistLeasesAsync(userId, playlistIds, index + 1, action, token), ct);

    private async Task DisconnectUnderLeasesAsync(int userId, string service,
        IReadOnlyList<PlaylistDisconnectChoice> choices, CancellationToken ct)
    {
        // A queued sync or another review can change the link set between preview
        // and lease acquisition. Recheck every choice while all affected leases are held.
        var preview = await PreviewDisconnectAsync(userId, service, ct);
        if (choices.Count != preview.Links.Count
            || choices.Select(choice => choice.MappingId).Distinct().Count() != choices.Count
            || choices.Any(choice => preview.Links.All(link => link.MappingId != choice.MappingId)))
            throw new PlatformApiException("disconnect_review_stale",
                "The linked playlists changed. Review the disconnect choices again.", 409);
        var previewById = preview.Links.ToDictionary(link => link.MappingId);
        foreach (var choice in choices)
        {
            var link = previewById[choice.MappingId];
            if (choice.DeleteRemote && !link.CanDeleteRemote)
                throw new PlatformApiException("remote_delete_unsupported",
                    "This platform cannot delete that playlist through its API.", 409);
            if (choice.DeleteCanonicalIfLast && !link.CanDeleteCanonicalIfLast)
                throw new PlatformApiException("other_platforms_remain",
                    "This Cantaro playlist is still linked to another connected platform.", 409);
        }

        var account = await db.ConnectedServiceAccounts.AsNoTracking().SingleOrDefaultAsync(item =>
            item.UserId == userId && item.Service == service && item.ConnectionState != "disconnected", ct)
            ?? throw new PlatformApiException("account_not_connected", "This platform is not connected.", 409);
        if (account.ExternalAccountId != preview.ExternalAccountId)
            throw new PlatformApiException("disconnect_review_stale",
                "The connected account changed. Review the disconnect choices again.", 409);
        foreach (var choice in choices.Where(choice => choice.DeleteRemote))
        {
            var link = await db.ServicePlaylistMappings.SingleAsync(item => item.Id == choice.MappingId, ct);
            if (link.ConnectedServiceAccountId != account.Id || link.ExternalAccountId != account.ExternalAccountId)
                throw new PlatformApiException("account_changed",
                    "Reconnect the original linked account before deleting its playlist.", 409);
            await Provider(service).DeleteAsync(new(userId, account.Id, account.ExternalAccountId),
                link.ServicePlaylistId, ct);
            link.State = "unlinked";
            link.NextAttemptAt = null;
            await db.SaveChangesAsync(ct);
        }

        await platforms.GetRequired(service).DisconnectAsync(userId, ct);

        var deletePlaylistIds = choices.Where(choice => choice.DeleteCanonicalIfLast)
            .Select(choice => previewById[choice.MappingId].PlaylistId).Distinct().ToArray();
        foreach (var playlistId in deletePlaylistIds)
        {
            var playlist = await db.Playlists.SingleOrDefaultAsync(item =>
                item.Id == playlistId && item.UserId == userId, ct);
            if (playlist is not null) await DeleteCanonicalIfLastAsync(playlist, ct);
        }
    }

    private async Task<PreparedAttach> PrepareAttachAsync(
        int userId, Guid playlistId, string service, string servicePlaylistId, CancellationToken ct)
    {
        var playlist = await RequirePlaylistAsync(userId, playlistId, ct);
        if (!platforms.IsSupported(service) || !_providers.ContainsKey(service)
            || !platforms.GetRequired(service).TryValidatePlaylistId(servicePlaylistId, out _))
            throw new PlatformApiException("invalid_provider_playlist", "Choose a valid provider playlist.", 400);
        var account = await RequireAccountAsync(userId, service, ct);
        var remote = await Provider(service).ReadAsync(new(userId, account.Id, account.ExternalAccountId),
            servicePlaylistId, ct);
        if (!remote.IsComplete)
            throw new PlatformApiException("incomplete_playlist",
                "Cantaro couldn't verify the full playlist. Refresh it and try again.", 503);
        if (remote.Tracks.Any(track => track.IsAvailable && track.TrackId is null && track.ObservationId is null))
            throw new PlatformApiException("playlist_items_not_imported",
                "Cantaro can't map every item in this playlist to its library yet. Refresh the platform library and try linking again.", 409);
        var canonical = await db.PlaylistEntries.AsNoTracking()
            .Include(entry => entry.TrackObservation)
            .Where(entry => entry.PlaylistId == playlistId)
            .OrderBy(entry => entry.Position).ThenBy(entry => entry.Id)
            .ToListAsync(ct);

        var localCounts = CountKeys(canonical.Select(entry => LocalKey(entry, service)));
        var remoteCounts = CountKeys(remote.Tracks.Where(track => track.TrackId is not null || track.ObservationId is not null).OrderBy(track => track.Position).Select(track => RemoteKey(track, service)));
        var additions = remoteCounts.Sum(pair => Math.Max(0, pair.Value - localCounts.GetValueOrDefault(pair.Key)));
        var removals = localCounts.Sum(pair => Math.Max(0, pair.Value - remoteCounts.GetValueOrDefault(pair.Key)));
        var fingerprint = Fingerprint(JsonSerializer.Serialize(new
        {
            playlist.Id, playlist.Name, playlist.SyncRevision, playlist.AllowDuplicateTracks,
            AccountId = account.Id, account.ExternalAccountId,
            Local = canonical.Select(entry => new { entry.Id, entry.Position, entry.TrackId, entry.TrackObservationId }).ToArray(),
            Remote = remote
        }));
        return new PreparedAttach(playlist, account, remote, canonical, additions, removals, fingerprint);
    }

    private async Task<PreparedRename> PrepareRenameAsync(
        int userId, Guid playlistId, string name, CancellationToken ct)
    {
        await RequireAllLinksReadyForRenameAsync(userId, playlistId, ct);
        await coordinator.RunAsync(userId, playlistId, null, ct, readOnly: true);
        var playlist = await RequirePlaylistAsync(userId, playlistId, ct);
        var links = await db.ServicePlaylistMappings
            .Where(link => link.PlaylistId == playlistId && link.UserId == userId && link.State == "active")
            .OrderBy(link => link.Service).ThenBy(link => link.Id).ToListAsync(ct);
        if (links.Any(link => link.LastSyncStatus == "order_conflict"))
            throw new PlatformApiException("order_conflict",
                "Resolve the playlist order conflict before renaming linked copies.", 409);
        var names = new List<LinkName>(links.Count);
        foreach (var link in links)
        {
            var account = await RequireAccountAsync(userId, link.Service, ct);
            if (account.Id != link.ConnectedServiceAccountId || account.ExternalAccountId != link.ExternalAccountId)
                throw new PlatformApiException("account_changed", "Reconnect the original linked account before renaming.", 409);
            var remote = await Provider(link.Service).ReadAsync(new(userId, account.Id, account.ExternalAccountId),
                link.ServicePlaylistId, ct);
            if (!remote.IsComplete) throw StalePreview();
            names.Add(new LinkName(link, remote.Name));
        }
        var fingerprint = Fingerprint(JsonSerializer.Serialize(new
        {
            playlist.Name, playlist.SyncRevision, ProposedName = name,
            Links = names.Select(item => new
            {
                item.Mapping.Id, item.Mapping.SyncMode, item.Mapping.State,
                item.Mapping.ExternalAccountId, item.Mapping.PendingName,
                item.Mapping.RejectedName, item.Mapping.BaselineJson, item.RemoteName
            }).ToArray()
        }));
        return new PreparedRename(playlist, names, fingerprint);
    }

    private async Task<PreparedProposal> PrepareProposalAsync(
        int userId, Guid playlistId, Guid mappingId, CancellationToken ct)
    {
        await RequireAllLinksReadyForRenameAsync(userId, playlistId, ct);
        await coordinator.RunAsync(userId, playlistId, null, ct, readOnly: true);
        var playlist = await RequirePlaylistAsync(userId, playlistId, ct);
        var mapping = await db.ServicePlaylistMappings.SingleOrDefaultAsync(link =>
            link.Id == mappingId && link.PlaylistId == playlistId && link.UserId == userId
            && link.State == "active", ct)
            ?? throw new PlatformApiException("link_not_found", "Playlist link not found.", 404);
        if (string.IsNullOrWhiteSpace(mapping.PendingName))
            throw new PlatformApiException("rename_proposal_missing", "There is no provider rename to review.", 409);
        var account = await RequireAccountAsync(userId, mapping.Service, ct);
        if (account.Id != mapping.ConnectedServiceAccountId || account.ExternalAccountId != mapping.ExternalAccountId)
            throw new PlatformApiException("account_changed", "Reconnect the original linked account before reviewing its rename.", 409);
        var remote = await Provider(mapping.Service).ReadAsync(new(userId, account.Id, account.ExternalAccountId),
            mapping.ServicePlaylistId, ct);
        if (!remote.IsComplete || remote.Name != mapping.PendingName) throw StalePreview();
        var fingerprint = Fingerprint(JsonSerializer.Serialize(new
        {
            playlist.Name, playlist.SyncRevision, mapping.Id, mapping.PendingName,
            mapping.BaselineName, mapping.RejectedName, mapping.ExternalAccountId,
            RemoteName = remote.Name, remote.Revision
        }));
        return new PreparedProposal(playlist, mapping, remote.Name, fingerprint);
    }

    private async Task<Playlist> RequirePlaylistAsync(int userId, Guid playlistId, CancellationToken ct)
        => await db.Playlists.SingleOrDefaultAsync(playlist => playlist.Id == playlistId && playlist.UserId == userId, ct)
            ?? throw new PlatformApiException("playlist_not_found", "Playlist not found.", 404);

    private async Task EnsureNoActiveSyncAsync(int userId, Guid playlistId, CancellationToken ct)
    {
        var leaseExpiresAt = await db.Playlists.AsNoTracking()
            .Where(playlist => playlist.Id == playlistId && playlist.UserId == userId)
            .Select(playlist => playlist.SyncLeaseExpiresAt).SingleAsync(ct);
        if (leaseExpiresAt > DateTimeOffset.UtcNow)
            throw new PlatformApiException("sync_in_progress", "Wait for this playlist sync to finish.", 409);
    }

    private async Task RequireAllLinksReadyForRenameAsync(int userId, Guid playlistId, CancellationToken ct)
    {
        var unavailable = await db.ServicePlaylistMappings.AsNoTracking().AnyAsync(link =>
            link.UserId == userId && link.PlaylistId == playlistId
            && link.State != "unlinked" && link.State != "active", ct);
        if (unavailable)
            throw new PlatformApiException("rename_links_unavailable",
                "Reconnect, resolve, or unlink every unavailable platform copy before renaming this playlist.", 409);
    }

    private async Task<ServicePlaylistMapping> RequirePendingInitializationAsync(
        int userId, Guid playlistId, Guid mappingId, CancellationToken ct)
        => await db.ServicePlaylistMappings.SingleOrDefaultAsync(link =>
            link.Id == mappingId && link.UserId == userId && link.PlaylistId == playlistId
            && link.State == "paused" && link.InitialMode != null, ct)
            ?? throw new PlatformApiException("initialization_not_pending",
                "This playlist link has no pending initialization to retry.", 409);

    private async Task<ConnectedServiceAccount> RequireAccountAsync(int userId, string service, CancellationToken ct)
        => await db.ConnectedServiceAccounts.AsNoTracking().SingleOrDefaultAsync(account =>
            account.UserId == userId && account.Service == service && account.ConnectionState == "connected", ct)
            ?? throw new PlatformApiException("account_not_connected", "Connect the platform account before linking a playlist.", 409);

    private IPlaylistSyncProvider Provider(string service) => _providers.TryGetValue(service, out var provider)
        ? provider : throw new PlatformApiException("unsupported_platform", "This platform cannot sync playlists.", 400);

    private async Task DeleteCanonicalIfLastAsync(Playlist playlist, CancellationToken ct)
    {
        var remaining = await db.ServicePlaylistMappings.AsNoTracking().AnyAsync(link =>
            link.PlaylistId == playlist.Id && link.State != "unlinked"
            && link.ConnectedServiceAccount != null
            && link.ConnectedServiceAccount.ConnectionState != "disconnected"
            && link.ExternalAccountId == link.ConnectedServiceAccount.ExternalAccountId, ct);
        if (remaining)
            throw new PlatformApiException("other_platforms_remain",
                "This Cantaro playlist is still linked to another connected platform.", 409);
        db.Playlists.Remove(playlist);
        await db.SaveChangesAsync(ct);
    }

    private Task<bool> HasOtherConnectedLinkAsync(Guid playlistId, Guid excludedMappingId, CancellationToken ct)
        => db.ServicePlaylistMappings.AsNoTracking().AnyAsync(link =>
            link.PlaylistId == playlistId && link.Id != excludedMappingId && link.State != "unlinked"
            && link.ConnectedServiceAccount != null
            && link.ConnectedServiceAccount.ConnectionState != "disconnected"
            && link.ExternalAccountId == link.ConnectedServiceAccount.ExternalAccountId, ct);

    private static Dictionary<string, int> CountKeys(IEnumerable<string> keys)
        => keys.GroupBy(key => key, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

    private static string LocalKey(PlaylistEntry entry, string service)
        => entry.TrackId is { } trackId ? $"track:{trackId}"
            : entry.TrackObservationId is { } observationId ? $"observation:{observationId}"
            : entry.TrackObservation is { } observation && observation.SourceType == service
                ? $"external:{service}:{observation.ExternalId}"
                : $"entry:{entry.Id}";

    private static string RemoteKey(PlaylistRemoteTrack track, string service)
        => track.TrackId is { } trackId ? $"track:{trackId}"
            : track.ObservationId is { } observationId ? $"observation:{observationId}"
            : $"external:{service}:{track.ExternalId}";

    private string Protect(PreviewProof proof) => _protector.Protect(JsonSerializer.Serialize(proof));

    private void Verify(string token, int userId, Guid playlistId, string operation, string? service,
        string? remoteId, Guid? mappingId, string? externalAccountId, string fingerprint, string? name)
    {
        PreviewProof? proof;
        try { proof = JsonSerializer.Deserialize<PreviewProof>(_protector.Unprotect(token)); }
        catch (Exception ex) when (ex is CryptographicException or JsonException or ArgumentException)
        {
            throw StalePreview();
        }
        if (proof is null || proof.UserId != userId || proof.PlaylistId != playlistId
            || proof.Operation != operation || proof.Service != service || proof.RemoteId != remoteId
            || proof.MappingId != mappingId || proof.ExternalAccountId != externalAccountId
            || proof.Fingerprint != fingerprint || proof.Name != name
            || DateTimeOffset.UtcNow - proof.IssuedAt > TimeSpan.FromMinutes(10))
            throw StalePreview();
    }

    private static string Fingerprint(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static PlatformApiException StalePreview() => new("stale_preview",
        "The playlist changed after the preview. Refresh the preview before confirming.", 409);

    private static bool IsSpotifyQuotaExceeded(PlatformApiException exception)
        => exception.Code == "spotify_quota_exceeded"
            || exception.InnerException is PlatformApiException { Code: "spotify_quota_exceeded" };

    private sealed record PreparedAttach(Playlist Playlist, ConnectedServiceAccount Account,
        PlaylistRemoteSnapshot Remote, IReadOnlyList<PlaylistEntry> Canonical,
        int Additions, int Removals, string Fingerprint);

    private sealed record PreparedCreate(ConnectedServiceAccount Account,
        IReadOnlyList<PlaylistCreateCandidate> Candidates, string Fingerprint);

    private sealed record LinkName(ServicePlaylistMapping Mapping, string RemoteName);
    private sealed record PreparedRename(Playlist Playlist, IReadOnlyList<LinkName> Links, string Fingerprint);
    private sealed record PreparedProposal(Playlist Playlist, ServicePlaylistMapping Mapping,
        string RemoteName, string Fingerprint);

    private static string RequireName(string? name)
    {
        var result = name?.Trim();
        if (string.IsNullOrWhiteSpace(result) || result.Length > 150)
            throw new PlatformApiException("invalid_playlist_name", "Enter a playlist name up to 150 characters.", 400);
        return result;
    }

    private sealed record PreviewProof(int UserId, Guid PlaylistId, string Operation,
        string? Service, string? RemoteId, Guid? MappingId, string? ExternalAccountId,
        string Fingerprint, string? Name, DateTimeOffset IssuedAt);
}
