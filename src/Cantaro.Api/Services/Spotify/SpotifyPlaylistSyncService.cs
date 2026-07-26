using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services.Spotify;

public sealed class SpotifyPlaylistSyncService(
    ApplicationDbContext dbContext,
    SpotifyService spotifyService,
    TrackMatchingQueue matchingQueue,
    TimeProvider timeProvider,
    ILogger<SpotifyPlaylistSyncService> logger)
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly SpotifyService _spotifyService = spotifyService;
    private readonly TrackMatchingQueue _matchingQueue = matchingQueue;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<SpotifyPlaylistSyncService> _logger = logger;

    public async Task<Guid> SyncPlaylistAsync(
        int userId,
        string spotifyPlaylistId,
        CancellationToken cancellationToken)
    {
        // Spotify network calls complete before any database transaction starts.
        var snapshot = await _spotifyService.GetPlaylistImportSnapshotAsync(
            userId,
            spotifyPlaylistId,
            cancellationToken);
        var accountId = await _dbContext.ConnectedServiceAccounts
            .Where(account => account.UserId == userId && account.Service == SpotifyService.ServiceName)
            .Select(account => account.Id)
            .SingleAsync(cancellationToken);
        var observationIds = new HashSet<Guid>();
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        var playlistId = await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var now = _timeProvider.GetUtcNow();
                var mapping = await _dbContext.ServicePlaylistMappings
                    .Include(candidate => candidate.Playlist)
                    .SingleOrDefaultAsync(
                        candidate => candidate.Service == SpotifyService.ServiceName
                            && candidate.ServicePlaylistId == spotifyPlaylistId
                            && candidate.Playlist!.UserId == userId,
                        cancellationToken);

                Playlist playlist;
                if (mapping is null)
                {
                    playlist = new Playlist
                    {
                        Id = Guid.NewGuid(),
                        UserId = userId,
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
                        ConnectedServiceAccountId = accountId,
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
                    mapping.ConnectedServiceAccountId = accountId;
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

                for (var position = 0; position < uniqueTracks.Count; position++)
                {
                    var track = uniqueTracks[position];
                    var observation = await UpsertObservationAsync(track, now, cancellationToken);
                    observationIds.Add(observation.Id);
                    entries.Add(new PlaylistEntry
                    {
                        Id = Guid.NewGuid(),
                        PlaylistId = playlist.Id,
                        TrackObservationId = observation.Id,
                        TrackId = observation.TrackId,
                        Position = position,
                        AddedAt = track.AddedAt ?? now,
                        SourceService = SpotifyService.ServiceName
                    });
                }

                _dbContext.PlaylistEntries.AddRange(entries);
                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return playlist.Id;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });

        // Matching can perform network work and is intentionally queued only after commit.
        foreach (var observationId in observationIds)
        {
            _matchingQueue.Enqueue(observationId);
        }

        _logger.LogInformation(
            "Imported Spotify playlist {SpotifyPlaylistId} into Cantaro playlist {PlaylistId} with {TrackCount} tracks.",
            spotifyPlaylistId,
            playlistId,
            observationIds.Count);
        return playlistId;
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
            Title = track.Name,
            Artist = track.Artist,
            OriginalTitle = track.Name,
            OriginalArtist = track.Artist,
            SearchTitle = track.Name,
            SearchArtist = track.Artist,
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
                // Spotify artwork URLs are temporary and are not persisted in the canonical cache.
                ThumbnailUrl = null,
                RawMetadata = rawMetadata,
                NormalizedTitle = TrackTextNormalizer.Normalize(track.Name),
                NormalizedArtist = TrackTextNormalizer.Normalize(track.Artist),
                DurationSeconds = track.DurationSeconds,
                MatchStatus = TrackMatchingStatuses.Pending,
                CreatedAt = now,
                UpdatedAt = now
            };
            _dbContext.TrackObservations.Add(observation);
            return observation;
        }

        observation.Title = track.Name;
        observation.Artist = track.Artist;
        observation.ThumbnailUrl = null;
        observation.RawMetadata = rawMetadata;
        observation.NormalizedTitle = TrackTextNormalizer.Normalize(track.Name);
        observation.NormalizedArtist = TrackTextNormalizer.Normalize(track.Artist);
        observation.DurationSeconds = track.DurationSeconds;
        observation.UpdatedAt = now;
        if (observation.MatchStatus != TrackMatchingStatuses.Matched)
        {
            observation.MatchStatus = TrackMatchingStatuses.Pending;
            observation.ResolutionNotes = null;
            observation.AcceptedCandidateId = null;
        }

        return observation;
    }
}
