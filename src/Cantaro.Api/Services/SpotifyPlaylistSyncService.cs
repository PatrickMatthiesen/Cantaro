using System.Data.Common;
using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

public sealed class SpotifyPlaylistSyncService
{
    private const string ServiceName = "spotify";

    private readonly ApplicationDbContext _dbContext;
    private readonly SpotifyService _spotifyService;
    private readonly TrackMatchingService _trackMatchingService;
    private readonly ILogger<SpotifyPlaylistSyncService> _logger;

    public SpotifyPlaylistSyncService(
        ApplicationDbContext dbContext,
        SpotifyService spotifyService,
        TrackMatchingService trackMatchingService,
        ILogger<SpotifyPlaylistSyncService> logger)
    {
        _dbContext = dbContext;
        _spotifyService = spotifyService;
        _trackMatchingService = trackMatchingService;
        _logger = logger;
    }

    public async Task<Guid> SyncSpotifyPlaylistAsync(
        int userId,
        string spotifyPlaylistId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting sync of Spotify playlist {PlaylistId} for user {UserId}", spotifyPlaylistId, userId);

        var strategy = _dbContext.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                var spotifyPlaylists = await _spotifyService.GetPlaylistsAsync(userId, cancellationToken);
                var spotifyPlaylist = spotifyPlaylists.FirstOrDefault(p => p.Id == spotifyPlaylistId)
                    ?? throw new InvalidOperationException($"Spotify playlist {spotifyPlaylistId} not found or not accessible");

                var playlistItems = await _spotifyService.GetPlaylistItemsAsync(userId, spotifyPlaylistId, cancellationToken);
                _logger.LogInformation("Fetched {ItemCount} items from Spotify playlist {PlaylistId}", playlistItems.Count, spotifyPlaylistId);

                var existingMapping = await _dbContext.ServicePlaylistMappings
                    .Include(m => m.Playlist)
                    .FirstOrDefaultAsync(
                        m => m.Service == ServiceName
                            && m.ServicePlaylistId == spotifyPlaylistId
                            && m.Playlist!.UserId == userId,
                        cancellationToken);

                Playlist playlist;
                if (existingMapping is not null)
                {
                    playlist = existingMapping.Playlist
                        ?? throw new InvalidOperationException("ServicePlaylistMapping has no associated Playlist");

                    playlist.Name = spotifyPlaylist.Title;
                    playlist.Description = spotifyPlaylist.Description;
                    playlist.UpdatedAt = DateTimeOffset.UtcNow;

                    await _dbContext.PlaylistEntries
                        .Where(e => e.PlaylistId == playlist.Id)
                        .ExecuteDeleteAsync(cancellationToken);
                }
                else
                {
                    playlist = new Playlist
                    {
                        Id = Guid.NewGuid(),
                        UserId = userId,
                        Name = spotifyPlaylist.Title,
                        Description = spotifyPlaylist.Description,
                        CreatedAt = DateTimeOffset.UtcNow,
                        UpdatedAt = DateTimeOffset.UtcNow
                    };
                    _dbContext.Playlists.Add(playlist);
                }

                var failedTracks = 0;
                var playlistEntries = new List<PlaylistEntry>();
                var observationIdsToProcess = new HashSet<Guid>();

                for (var i = 0; i < playlistItems.Count; i++)
                {
                    var item = playlistItems[i];

                    try
                    {
                        var observation = await GetOrCreateObservationForTrackAsync(item, cancellationToken);
                        observationIdsToProcess.Add(observation.Id);

                        playlistEntries.Add(new PlaylistEntry
                        {
                            Id = Guid.NewGuid(),
                            PlaylistId = playlist.Id,
                            TrackObservationId = observation.Id,
                            TrackId = observation.TrackId,
                            Position = i,
                            AddedAt = item.AddedAt ?? DateTimeOffset.UtcNow,
                            SourceService = ServiceName
                        });
                    }
                    catch (Exception ex) when (!IsDatabaseException(ex))
                    {
                        _logger.LogWarning(ex, "Failed to process Spotify track {TrackId} in playlist {PlaylistId}", item.TrackId, spotifyPlaylistId);
                        failedTracks++;
                    }
                }

                _dbContext.PlaylistEntries.AddRange(playlistEntries);
                await _dbContext.SaveChangesAsync(cancellationToken);

                foreach (var observationId in observationIdsToProcess)
                {
                    await _trackMatchingService.ProcessObservationAsync(observationId, cancellationToken);
                }

                if (existingMapping is not null)
                {
                    existingMapping.LastSyncedAt = DateTimeOffset.UtcNow;
                    existingMapping.LastSyncStatus = failedTracks > 0 ? "partial_failure" : "success";
                }
                else
                {
                    _dbContext.ServicePlaylistMappings.Add(new ServicePlaylistMapping
                    {
                        Id = Guid.NewGuid(),
                        PlaylistId = playlist.Id,
                        Service = ServiceName,
                        ServicePlaylistId = spotifyPlaylistId,
                        SyncMode = "import_only",
                        LastSyncedAt = DateTimeOffset.UtcNow,
                        LastSyncStatus = failedTracks > 0 ? "partial_failure" : "success"
                    });
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                _logger.LogInformation(
                    "Successfully synced Spotify playlist {SpotifyPlaylistId} to Cantaro playlist {PlaylistId}. Total items: {TotalItems}, Failed: {FailedItems}",
                    spotifyPlaylistId,
                    playlist.Id,
                    playlistItems.Count,
                    failedTracks);

                return playlist.Id;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                _logger.LogError(ex, "Failed to sync Spotify playlist {PlaylistId} for user {UserId}", spotifyPlaylistId, userId);
                throw;
            }
        });
    }

    private async Task<TrackObservation> GetOrCreateObservationForTrackAsync(
        SpotifyPlaylistItemDto track,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var artist = string.IsNullOrWhiteSpace(track.ArtistName) ? "Spotify" : track.ArtistName;
        var rawMetadata = JsonSerializer.Serialize(new TrackObservationMetadata
        {
            SourceType = ServiceName,
            ExternalId = track.TrackId,
            Title = track.Title,
            Artist = artist,
            OriginalTitle = track.Title,
            OriginalArtist = track.ArtistName,
            SearchTitle = track.Title,
            SearchArtist = artist,
            Description = track.AlbumName,
            ThumbnailUrl = track.ThumbnailUrl,
            DurationSeconds = track.DurationSeconds,
            PublishedAt = track.AddedAt
        });

        var trackedObservation = _dbContext.TrackObservations.Local
            .FirstOrDefault(observation => observation.SourceType == ServiceName && observation.ExternalId == track.TrackId);

        if (trackedObservation is not null)
        {
            UpdateObservation(trackedObservation, track, artist, rawMetadata, now);
            return trackedObservation;
        }

        var existingObservation = await _dbContext.TrackObservations
            .FirstOrDefaultAsync(
                observation => observation.SourceType == ServiceName && observation.ExternalId == track.TrackId,
                cancellationToken);

        if (existingObservation is not null)
        {
            UpdateObservation(existingObservation, track, artist, rawMetadata, now);
            return existingObservation;
        }

        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(),
            SourceType = ServiceName,
            ExternalId = track.TrackId,
            Title = track.Title,
            Artist = artist,
            ThumbnailUrl = track.ThumbnailUrl,
            RawMetadata = rawMetadata,
            NormalizedTitle = TrackTextNormalizer.Normalize(track.Title),
            NormalizedArtist = TrackTextNormalizer.Normalize(artist),
            DurationSeconds = track.DurationSeconds,
            MatchStatus = TrackMatchingStatuses.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };

        _dbContext.TrackObservations.Add(observation);
        return observation;
    }

    private static void UpdateObservation(
        TrackObservation observation,
        SpotifyPlaylistItemDto track,
        string artist,
        string rawMetadata,
        DateTimeOffset now)
    {
        observation.Title = track.Title;
        observation.Artist = artist;
        observation.ThumbnailUrl = track.ThumbnailUrl;
        observation.RawMetadata = rawMetadata;
        observation.NormalizedTitle = TrackTextNormalizer.Normalize(track.Title);
        observation.NormalizedArtist = TrackTextNormalizer.Normalize(artist);
        observation.DurationSeconds = track.DurationSeconds;
        observation.UpdatedAt = now;

        if (observation.MatchStatus != TrackMatchingStatuses.Matched)
        {
            observation.MatchStatus = TrackMatchingStatuses.Pending;
            observation.ResolutionNotes = null;
            observation.AcceptedCandidateId = null;
        }
    }

    private static bool IsDatabaseException(Exception ex)
    {
        return ex is DbUpdateException || ex.GetBaseException() is DbException;
    }
}
