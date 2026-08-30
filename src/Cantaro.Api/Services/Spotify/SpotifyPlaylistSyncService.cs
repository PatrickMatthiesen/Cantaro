using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services.Spotify;

public sealed class SpotifyPlaylistSyncService(
    ApplicationDbContext dbContext,
    SpotifyService spotifyService,
    SpotifyTrackResolver trackResolver,
    PlaylistCanonicalReconciliationService playlistReconciler,
    TimeProvider timeProvider,
    ILogger<SpotifyPlaylistSyncService> logger)
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly SpotifyService _spotifyService = spotifyService;
    private readonly SpotifyTrackResolver _trackResolver = trackResolver;
    private readonly PlaylistCanonicalReconciliationService _playlistReconciler = playlistReconciler;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<SpotifyPlaylistSyncService> _logger = logger;

    public async Task<Guid> SyncPlaylistAsync(
        PlatformAccountContext account,
        string spotifyPlaylistId,
        CancellationToken cancellationToken)
    {
        // Spotify network calls complete before any database transaction starts.
        var snapshot = await _spotifyService.GetPlaylistImportSnapshotAsync(
            account,
            spotifyPlaylistId,
            cancellationToken);
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        var importResult = await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var now = _timeProvider.GetUtcNow();
                var mapping = await _dbContext.ServicePlaylistMappings
                    .Include(candidate => candidate.Playlist)
                    .SingleOrDefaultAsync(
                        candidate => candidate.Service == SpotifyService.ServiceName
                            && candidate.ConnectedServiceAccountId == account.ConnectedServiceAccountId
                            && candidate.ServicePlaylistId == spotifyPlaylistId,
                        cancellationToken);

                Playlist playlist;
                if (mapping is null)
                {
                    playlist = new Playlist
                    {
                        Id = Guid.NewGuid(),
                        UserId = account.UserId,
                        Name = snapshot.Playlist.Name,
                        Description = snapshot.Playlist.Description,
                        ImportedFromService = SpotifyService.ServiceName,
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    mapping = new ServicePlaylistMapping
                    {
                        Id = Guid.NewGuid(),
                        PlaylistId = playlist.Id,
                        ConnectedServiceAccountId = account.ConnectedServiceAccountId,
                        Service = SpotifyService.ServiceName,
                        ServicePlaylistId = spotifyPlaylistId,
                        SyncMode = "import_only",
                        LastSyncedAt = now,
                        LastSyncStatus = "success"
                    };
                    _dbContext.Playlists.Add(playlist);
                    _dbContext.ServicePlaylistMappings.Add(mapping);
                }
                else
                {
                    playlist = mapping.Playlist
                        ?? throw new InvalidOperationException("Spotify playlist mapping has no playlist.");
                    playlist.Name = snapshot.Playlist.Name;
                    playlist.Description = snapshot.Playlist.Description;
                    playlist.ImportedFromService = SpotifyService.ServiceName;
                    playlist.UpdatedAt = now;
                    mapping.ConnectedServiceAccountId = account.ConnectedServiceAccountId;
                    mapping.LastSyncedAt = now;
                    mapping.LastSyncStatus = "success";

                    await _dbContext.PlaylistEntries
                        .Where(entry => entry.PlaylistId == playlist.Id)
                        .ExecuteDeleteAsync(cancellationToken);
                }

                playlist.Metadata = JsonSerializer.Serialize(new
                {
                    provider = SpotifyService.ServiceName,
                    externalUrl = snapshot.Playlist.ExternalUrl,
                    snapshotId = snapshot.Playlist.SnapshotId,
                    refreshedAt = now
                });

                var uniqueTracks = snapshot.Tracks
                    .GroupBy(track => track.Id, StringComparer.Ordinal)
                    .Select(group => group.First())
                    .ToList();
                var entries = new List<PlaylistEntry>(uniqueTracks.Count);
                foreach (var track in uniqueTracks)
                {
                    var observation = await UpsertObservationAsync(track, now, cancellationToken);
                    var canonicalTrack = await _trackResolver.ResolveAsync(track, now, cancellationToken);
                    observation.TrackId = canonicalTrack.Id;
                    observation.MatchStatus = TrackMatchingStatuses.Matched;
                    observation.AcceptedCandidateId = null;
                    observation.LastMatchError = null;
                    observation.ResolutionNotes = "Resolved directly from authoritative Spotify catalog metadata.";
                    observation.UpdatedAt = now;

                    entries.Add(new PlaylistEntry
                    {
                        Id = Guid.NewGuid(),
                        PlaylistId = playlist.Id,
                        TrackObservationId = observation.Id,
                        TrackId = canonicalTrack.Id,
                        Position = track.Position,
                        AddedAt = track.AddedAt ?? now,
                        SourceService = SpotifyService.ServiceName
                    });
                }

                var reconciledEntries = _playlistReconciler.ReconcileImportedEntries(entries);
                _dbContext.PlaylistEntries.AddRange(reconciledEntries);
                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return (PlaylistId: playlist.Id, TrackCount: reconciledEntries.Count);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });

        _logger.LogInformation(
            "Imported Spotify playlist {SpotifyPlaylistId} into Cantaro playlist {PlaylistId} with {TrackCount} tracks.",
            spotifyPlaylistId,
            importResult.PlaylistId,
            importResult.TrackCount);
        return importResult.PlaylistId;
    }

    private async Task<TrackObservation> UpsertObservationAsync(
        SpotifyTrackSnapshot track,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var observation = _dbContext.TrackObservations.Local.FirstOrDefault(
                candidate => candidate.SourceType == SpotifyService.ServiceName && candidate.ExternalId == track.Id)
            ?? await _dbContext.TrackObservations.SingleOrDefaultAsync(
                candidate => candidate.SourceType == SpotifyService.ServiceName && candidate.ExternalId == track.Id,
                cancellationToken);
        var rawMetadata = JsonSerializer.Serialize(new TrackObservationMetadata
        {
            SourceType = SpotifyService.ServiceName,
            ExternalId = track.Id,
            SourceUrl = track.ExternalUrl,
            Title = track.Name,
            Artist = track.Artist,
            Album = track.AlbumName,
            Isrc = track.Isrc,
            OriginalTitle = track.Name,
            OriginalArtist = track.Artist,
            SearchTitle = track.Name,
            SearchArtist = track.Artist,
            ThumbnailUrl = track.ImageUrl,
            DurationSeconds = track.DurationSeconds
        });

        if (observation is null)
        {
            observation = new TrackObservation
            {
                Id = Guid.NewGuid(),
                SourceType = SpotifyService.ServiceName,
                ExternalId = track.Id,
                Title = track.Name,
                Artist = track.Artist,
                ThumbnailUrl = track.ImageUrl,
                RawMetadata = rawMetadata,
                NormalizedTitle = TrackTextNormalizer.Normalize(track.Name),
                NormalizedArtist = TrackTextNormalizer.Normalize(track.Artist),
                DurationSeconds = track.DurationSeconds,
                MatchStatus = TrackMatchingStatuses.Matched,
                CreatedAt = now,
                UpdatedAt = now
            };
            _dbContext.TrackObservations.Add(observation);
            return observation;
        }

        observation.Title = track.Name;
        observation.Artist = track.Artist;
        observation.ThumbnailUrl = track.ImageUrl;
        observation.RawMetadata = rawMetadata;
        observation.NormalizedTitle = TrackTextNormalizer.Normalize(track.Name);
        observation.NormalizedArtist = TrackTextNormalizer.Normalize(track.Artist);
        observation.DurationSeconds = track.DurationSeconds;
        observation.UpdatedAt = now;

        return observation;
    }
}
