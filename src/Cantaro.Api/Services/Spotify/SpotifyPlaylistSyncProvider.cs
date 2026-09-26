using System.Text.Json;
using Cantaro.Api.Configuration;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cantaro.Api.Services.Spotify;

public sealed class SpotifyPlaylistSyncProvider(
    ApplicationDbContext dbContext,
    SpotifyApiClient apiClient,
    SpotifyTokenManager tokenManager,
    SpotifyTrackResolver trackResolver,
    IEnumerable<ITrackMetadataSearchProvider> searchProviders,
    TrackMatchingService matchingService,
    PlaylistCanonicalReconciliationService playlistReconciler,
    IOptions<TrackMatchingOptions> matchingOptions) : IPlaylistSyncProvider
{
    public string PlatformId => SpotifyService.ServiceName;

    public async Task<IReadOnlyList<PlaylistRemoteCatalogItem>> ListPlaylistsAsync(
        PlatformAccountContext account, CancellationToken cancellationToken)
    {
        var accessToken = await GetPinnedTokenAsync(account, cancellationToken);
        return (await apiClient.GetPlaylistsAsync(accessToken, cancellationToken))
            .Select(item => new PlaylistRemoteCatalogItem(item.Id, item.Name)).ToList();
    }

    public async Task<PlaylistRemoteSnapshot> ReadAsync(
        PlatformAccountContext account, string playlistId, CancellationToken cancellationToken)
    {
        var accessToken = await GetPinnedTokenAsync(account, cancellationToken);
        var raw = await apiClient.GetPlaylistSyncReadSnapshotAsync(accessToken, playlistId, cancellationToken);
        var tracks = new List<PlaylistRemoteTrack>(raw.Items.Count);
        var cache = new Dictionary<string, (Guid TrackId, Guid ObservationId)>(StringComparer.Ordinal);
        var now = DateTimeOffset.UtcNow;
        foreach (var item in raw.Items)
        {
            if (item.Track is not { } snapshot)
            {
                tracks.Add(new PlaylistRemoteTrack($"!unavailable:{item.Position}", null, null,
                    "Unavailable Spotify item", item.Position));
                continue;
            }

            if (!cache.TryGetValue(snapshot.Id, out var ids))
            {
                var observation = dbContext.TrackObservations.Local.FirstOrDefault(existing =>
                    existing.SourceType == PlatformId && existing.ExternalId == snapshot.Id)
                    ?? await dbContext.TrackObservations.SingleOrDefaultAsync(existing =>
                        existing.SourceType == PlatformId && existing.ExternalId == snapshot.Id, cancellationToken);
                observation ??= new TrackObservation
                {
                    Id = Guid.NewGuid(), SourceType = PlatformId, ExternalId = snapshot.Id,
                    Title = snapshot.Name, MatchStatus = TrackMatchingStatuses.Matched,
                    CreatedAt = now
                };
                if (dbContext.Entry(observation).State == EntityState.Detached)
                    dbContext.TrackObservations.Add(observation);
                observation.Title = snapshot.Name;
                observation.Artist = snapshot.Artist;
                observation.DurationSeconds = snapshot.DurationSeconds;
                observation.ThumbnailUrl = snapshot.ImageUrl;
                observation.NormalizedTitle = TrackTextNormalizer.Normalize(snapshot.Name);
                observation.NormalizedArtist = TrackTextNormalizer.Normalize(snapshot.Artist);
                observation.RawMetadata = JsonSerializer.Serialize(new TrackObservationMetadata
                {
                    SourceType = PlatformId, ExternalId = snapshot.Id, SourceUrl = snapshot.ExternalUrl,
                    Title = snapshot.Name, Artist = snapshot.Artist, Album = snapshot.AlbumName,
                    Isrc = snapshot.Isrc, OriginalTitle = snapshot.Name, OriginalArtist = snapshot.Artist,
                    SearchTitle = snapshot.Name, SearchArtist = snapshot.Artist,
                    ThumbnailUrl = snapshot.ImageUrl, DurationSeconds = snapshot.DurationSeconds
                });
                var canonical = await trackResolver.ResolveAsync(snapshot, now, cancellationToken);
                observation.TrackId = canonical.Id;
                observation.MatchStatus = TrackMatchingStatuses.Matched;
                observation.UpdatedAt = now;
                ids = (canonical.Id, observation.Id);
                cache.Add(snapshot.Id, ids);
            }
            tracks.Add(new PlaylistRemoteTrack(snapshot.Id, ids.TrackId, ids.ObservationId,
                snapshot.Name, item.Position));
        }
        _ = await RequireAccountAsync(account, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new PlaylistRemoteSnapshot(raw.Id, raw.Name, tracks, raw.SnapshotId, raw.IsComplete);
    }

    public async Task<string?> ResolveAsync(
        PlatformAccountContext account, PlaylistEntry entry, CancellationToken cancellationToken)
    {
        var searchProvider = searchProviders.OfType<SpotifySearchProvider>().Single();
        return await PlaylistDestinationIdentityResolver.ResolveAsync(dbContext, matchingOptions.Value,
            async (observationId, candidate, ct) => (await matchingService.CreateTrackFromMatchAsync(observationId, candidate, ct)).TrackId
                ?? throw new InvalidOperationException("Matching did not create a canonical track."),
            (observationId, trackId, ct) => playlistReconciler.ReconcileObservationAsync(observationId, trackId, ct),
            account, PlatformId, entry, async (observation, ct) =>
            {
                _ = await GetPinnedTokenAsync(account, ct);
                return await searchProvider.SearchAsync(observation, ct);
            }, cancellationToken);
    }

    public async Task RenameAsync(
        PlatformAccountContext account, string playlistId, string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var accessToken = await GetPinnedTokenAsync(account, cancellationToken);
        var linkedAccount = await RequireAccountAsync(account, cancellationToken);
        var profile = await apiClient.GetProfileAsync(accessToken, cancellationToken);
        var playlist = await apiClient.GetPlaylistSyncReadSnapshotAsync(accessToken, playlistId, cancellationToken);
        if (profile.AccountId != linkedAccount.ExternalAccountId && profile.Id != linkedAccount.ExternalAccountId)
            throw new PlatformApiException("spotify_account_changed", "The connected Spotify account changed.", 409);
        if (string.IsNullOrWhiteSpace(profile.Id) || playlist.OwnerId != profile.Id)
            throw new PlatformApiException("spotify_playlist_not_owned", "Only the playlist owner can rename it.", 403);
        if (!playlist.IsComplete)
            throw new PlatformApiException("spotify_playlist_incomplete_response",
                "Spotify playlist changed while Cantaro refreshed it. Retry the rename.", 409);
        var requiredScope = playlist.IsPublic == true ? "playlist-modify-public" : "playlist-modify-private";
        if (!(linkedAccount.Scopes ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains(requiredScope, StringComparer.Ordinal))
            throw new PlatformApiException("spotify_write_scope_required", "Reconnect Spotify with playlist write access.", 409);
        await apiClient.RenamePlaylistAsync(accessToken, playlistId, name, cancellationToken);
    }

    public Task DeleteAsync(PlatformAccountContext account, string playlistId, CancellationToken cancellationToken)
        => throw new PlatformApiException("spotify_remote_delete_unsupported",
            "Spotify does not provide a playlist deletion API. Delete this playlist in Spotify, then confirm unlinking in Cantaro.", 409);

    private async Task<string> GetPinnedTokenAsync(PlatformAccountContext account, CancellationToken cancellationToken)
    {
        var linkedAccount = await RequireAccountAsync(account, cancellationToken);
        var token = await tokenManager.GetAccessTokenSnapshotAsync(account, false, cancellationToken);
        var profile = await apiClient.GetProfileAsync(token.Value, cancellationToken);
        if (!string.Equals(profile.AccountId ?? profile.Id, linkedAccount.ExternalAccountId, StringComparison.Ordinal))
            throw new PlatformApiException("spotify_account_changed",
                "The connected Spotify account changed. Reconnect the original account before syncing.", 409);
        return token.Value;
    }

    private async Task<ConnectedServiceAccount> RequireAccountAsync(
        PlatformAccountContext context, CancellationToken cancellationToken)
    {
        var linked = await dbContext.ConnectedServiceAccounts.AsNoTracking().SingleOrDefaultAsync(candidate =>
            candidate.Id == context.ConnectedServiceAccountId && candidate.UserId == context.UserId
            && candidate.Service == PlatformId && candidate.ConnectionState == "connected"
            && (context.ExpectedExternalAccountId == null
                || candidate.ExternalAccountId == context.ExpectedExternalAccountId), cancellationToken);
        return linked ?? throw new PlatformApiException("spotify_account_changed",
            "Reconnect the original Spotify account before syncing this playlist.", 409);
    }
}
