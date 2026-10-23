using System.Text.Json;
using Cantaro.Api.Configuration;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cantaro.Api.Services;

public sealed class YouTubePlaylistSyncProvider(
    ApplicationDbContext dbContext,
    YouTubeService youtubeService,
    TrackMatchQueue matchQueue,
    TrackMatchingService matchingService,
    PlaylistCanonicalReconciliationService playlistReconciler,
    IOptions<TrackMatchingOptions> matchingOptions) : IPlaylistSyncProvider
{
    public string PlatformId => "youtube";

    public async Task<IReadOnlyList<PlaylistRemoteCatalogItem>> ListPlaylistsAsync(
        PlatformAccountContext account, CancellationToken cancellationToken)
        => (await youtubeService.GetPlaylistsAsync(account, cancellationToken))
            .Select(item => new PlaylistRemoteCatalogItem(item.Id, item.Title)).ToList();

    public async Task<PlaylistRemoteSnapshot> ReadAsync(
        PlatformAccountContext account, string playlistId, CancellationToken cancellationToken)
    {
        var raw = await youtubeService.GetSyncReadSnapshotAsync(account, playlistId, cancellationToken);
        if (!raw.IsComplete)
            throw new PlatformApiException("incomplete_playlist", "The platform returned an incomplete playlist. Its missing items were not treated as removals.", 503);
        await youtubeService.ValidateSyncAccountAsync(account, cancellationToken);
        Func<Task>? validateProvenance = null;
        return await PlaylistReadIngestion.RunAsync(dbContext, raw.Account, async readDb =>
        {
            var bound = await PlaylistDestinationReadBindings.BindReadAsync(readDb, account, PlatformId, playlistId,
                raw.Items.Select(item => new PlaylistRemoteTrack(item.ExternalId, null, null, item.Title,
                    item.Position, item.IsAvailable)).ToArray(), cancellationToken);
            validateProvenance = await PlaylistExportAttemptStore.GuardReadAsync(readDb, raw.Account, playlistId, bound, cancellationToken);
            var tracks = new List<PlaylistRemoteTrack>(raw.Items.Count);
            var cache = new Dictionary<string, TrackObservation>(StringComparer.Ordinal);
            var unavailableIdentityCache = new Dictionary<string, (Guid? TrackId, Guid? ObservationId)>(StringComparer.Ordinal);
            var enqueue = new HashSet<Guid>();
            var now = DateTimeOffset.UtcNow;
            foreach (var item in raw.Items)
            {
                if (bound[item.Position].ReadScope is not null)
                {
                    tracks.Add(bound[item.Position]);
                    continue;
                }
                if (!item.IsAvailable)
                {
                    var existing = unavailableIdentityCache.GetValueOrDefault(item.ExternalId);
                    if (!unavailableIdentityCache.ContainsKey(item.ExternalId))
                    {
                        existing = await FindExistingUnavailableIdentityAsync(readDb, item.ExternalId, cancellationToken);
                        unavailableIdentityCache.Add(item.ExternalId, existing);
                    }
                    tracks.Add(MapUnavailableItem(item, existing.TrackId, existing.ObservationId));
                    continue;
                }
                if (!cache.TryGetValue(item.ExternalId, out var observation))
                {
                    observation = readDb.TrackObservations.Local.FirstOrDefault(existing =>
                        existing.SourceType == PlatformId && existing.ExternalId == item.ExternalId)
                        ?? await readDb.TrackObservations.SingleOrDefaultAsync(existing =>
                            existing.SourceType == PlatformId && existing.ExternalId == item.ExternalId,
                            cancellationToken);
                    var parsed = TrackMetadataParser.Parse(item.Title, item.Artist);
                    var displayArtist = parsed.DisplayArtist ?? item.Artist;
                    observation ??= new TrackObservation
                    {
                        Id = Guid.NewGuid(), SourceType = PlatformId, ExternalId = item.ExternalId,
                        Title = parsed.DisplayTitle, MatchStatus = TrackMatchingStatuses.Pending,
                        CreatedAt = now
                    };
                    if (readDb.Entry(observation).State == EntityState.Detached)
                        readDb.TrackObservations.Add(observation);
                    var preservedDescription = GetPreservedDescription(item.Description, observation.RawMetadata);
                    observation.Title = parsed.DisplayTitle;
                    observation.Artist = displayArtist;
                    observation.ThumbnailUrl = item.ThumbnailUrl;
                    observation.DurationSeconds = item.DurationSeconds;
                    observation.NormalizedTitle = TrackTextNormalizer.Normalize(parsed.DisplayTitle);
                    observation.NormalizedArtist = TrackTextNormalizer.Normalize(displayArtist);
                    observation.RawMetadata = JsonSerializer.Serialize(new TrackObservationMetadata
                    {
                        SourceType = PlatformId, ExternalId = item.ExternalId,
                        Title = parsed.DisplayTitle, Artist = displayArtist,
                        OriginalTitle = item.Title, OriginalArtist = item.Artist,
                        ChannelTitle = item.Artist, SearchTitle = parsed.SearchTitle,
                        SearchArtist = parsed.SearchArtist, ThumbnailUrl = item.ThumbnailUrl,
                        DurationSeconds = item.DurationSeconds,
                        Description = preservedDescription
                    });
                    observation.UpdatedAt = now;
                    if (observation.TrackId is null)
                    {
                        var known = await readDb.TrackSourceIds.AsNoTracking().SingleOrDefaultAsync(source =>
                            source.SourceType == PlatformId && source.ExternalId == item.ExternalId,
                            cancellationToken);
                        if (known is not null)
                        {
                            observation.TrackId = known.TrackId;
                            observation.MatchStatus = TrackMatchingStatuses.Matched;
                        }
                        else
                        {
                            observation.MatchStatus = TrackMatchingStatuses.Pending;
                            enqueue.Add(observation.Id);
                        }
                    }
                    cache.Add(item.ExternalId, observation);
                }
                tracks.Add(new PlaylistRemoteTrack(item.ExternalId, observation.TrackId,
                    observation.Id, item.Title, item.Position));
            }
            await youtubeService.ValidateSyncAccountAsync(account, cancellationToken);
            await readDb.SaveChangesAsync(cancellationToken);
            var readQueue = matchQueue.ForContext(readDb);
            foreach (var observationId in enqueue)
                await readQueue.EnqueueAsync(observationId, cancellationToken);
            return new PlaylistRemoteSnapshot(raw.Id, raw.Name, tracks, raw.Revision, raw.IsComplete,
                raw.UnavailableItemCount);
        }, cancellationToken, () => validateProvenance!());
    }

    private async Task<(Guid? TrackId, Guid? ObservationId)> FindExistingUnavailableIdentityAsync(
        ApplicationDbContext readDb, string externalId, CancellationToken cancellationToken)
    {
        if (externalId.StartsWith("!unavailable:", StringComparison.Ordinal)) return (null, null);
        var observation = readDb.TrackObservations.Local.FirstOrDefault(existing =>
                existing.SourceType == PlatformId && existing.ExternalId == externalId)
            ?? await readDb.TrackObservations.AsNoTracking().SingleOrDefaultAsync(existing =>
                existing.SourceType == PlatformId && existing.ExternalId == externalId,
                cancellationToken);
        if (observation is not null) return (observation.TrackId, observation.Id);

        var sourceId = await readDb.TrackSourceIds.AsNoTracking().SingleOrDefaultAsync(source =>
            source.SourceType == PlatformId && source.ExternalId == externalId, cancellationToken);
        return (sourceId?.TrackId, null);
    }

    internal static PlaylistRemoteTrack MapUnavailableItem(
        YouTubeService.SyncReadItem item, Guid? existingTrackId, Guid? existingObservationId)
        => new(item.ExternalId, existingTrackId, existingObservationId, item.Title, item.Position,
            IsAvailable: false);

    internal static string? GetPreservedDescription(string? currentDescription, string? previousRawMetadata)
    {
        if (!string.IsNullOrWhiteSpace(currentDescription))
            return currentDescription;
        if (string.IsNullOrWhiteSpace(previousRawMetadata))
            return null;

        try
        {
            return JsonSerializer.Deserialize<TrackObservationMetadata>(previousRawMetadata)?.Description;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<string?> ResolveAsync(
        PlatformAccountContext account, PlaylistEntry entry, CancellationToken cancellationToken)
    {
        return await PlaylistDestinationIdentityResolver.ResolveAsync(dbContext, matchingOptions.Value,
            async (observationId, candidate, ct) => (await matchingService.CreateTrackFromMatchAsync(observationId, candidate, ct)).TrackId
                ?? throw new InvalidOperationException("Matching did not create a canonical track."),
            (observationId, trackId, ct) => playlistReconciler.ReconcileObservationAsync(observationId, trackId, ct),
            account, PlatformId, entry,
            async (observation, ct) =>
            {
                await youtubeService.ValidateSyncAccountAsync(account, ct);
                return await youtubeService.SearchVideoCandidatesAsync(account, observation, ct);
            },
            cancellationToken);
    }

    public Task RenameAsync(
        PlatformAccountContext account, string playlistId, string name, CancellationToken cancellationToken)
        => youtubeService.RenamePlaylistForSyncAsync(account, playlistId, name, cancellationToken);

    public Task DeleteAsync(
        PlatformAccountContext account, string playlistId, CancellationToken cancellationToken)
        => youtubeService.DeletePlaylistForSyncAsync(account, playlistId, cancellationToken);
}
