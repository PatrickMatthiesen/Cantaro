using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;
using System.Text.Json;

namespace Cantaro.Api.Services;

/// <summary>
/// Service for syncing YouTube playlists to Cantaro's canonical playlist system
/// </summary>
public class YouTubePlaylistSyncService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly YouTubeService _youtubeService;
    private readonly TrackMatchingService _trackMatchingService;
    private readonly ILogger<YouTubePlaylistSyncService> _logger;

    private const string ServiceName = "youtube";

    public YouTubePlaylistSyncService(
        ApplicationDbContext dbContext,
        YouTubeService youtubeService,
        TrackMatchingService trackMatchingService,
        ILogger<YouTubePlaylistSyncService> logger)
    {
        _dbContext = dbContext;
        _youtubeService = youtubeService;
        _trackMatchingService = trackMatchingService;
        _logger = logger;
    }

    /// <summary>
    /// Syncs a YouTube playlist to Cantaro's database
    /// </summary>
    /// <param name="userId">The Cantaro user ID</param>
    /// <param name="youtubePlaylistId">The YouTube playlist ID to sync</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The Cantaro Playlist ID (Guid)</returns>
    public async Task<Guid> SyncYouTubePlaylistAsync(
        int userId,
        string youtubePlaylistId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting sync of YouTube playlist {PlaylistId} for user {UserId}", youtubePlaylistId, userId);

        // Use execution strategy to handle retries with transactions
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        
        return await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            
            try
            {
                // Step 1: Fetch YouTube playlist metadata
                var youtubePlaylists = await _youtubeService.GetPlaylistsAsync(userId);
                var youtubePlaylist = youtubePlaylists.FirstOrDefault(p => p.Id == youtubePlaylistId)
                    ?? throw new InvalidOperationException($"YouTube playlist {youtubePlaylistId} not found or not accessible");

                // Step 2: Fetch all playlist items (videos)
                var playlistItems = await _youtubeService.GetPlaylistItemsAsync(userId, youtubePlaylistId);
                _logger.LogInformation("Fetched {ItemCount} items from YouTube playlist {PlaylistId}", playlistItems.Count, youtubePlaylistId);

                // Step 3: Check if this playlist is already mapped to a Cantaro playlist
                var existingMapping = await _dbContext.ServicePlaylistMappings
                    .Include(m => m.Playlist)
                    .FirstOrDefaultAsync(
                        m => m.Service == ServiceName && m.ServicePlaylistId == youtubePlaylistId,
                        cancellationToken);

                Playlist playlist;

                if (existingMapping != null)
                {
                    // Update existing playlist
                    playlist = existingMapping.Playlist 
                        ?? throw new InvalidOperationException("ServicePlaylistMapping has no associated Playlist");
                    
                    playlist.Name = youtubePlaylist.Title;
                    playlist.Description = youtubePlaylist.Description;
                    playlist.UpdatedAt = DateTimeOffset.UtcNow;

                    _logger.LogInformation("Updating existing Cantaro playlist {PlaylistId} for YouTube playlist {YouTubePlaylistId}", 
                        playlist.Id, youtubePlaylistId);

                    // Remove existing playlist entries using bulk delete
                    await _dbContext.PlaylistEntries
                        .Where(e => e.PlaylistId == playlist.Id)
                        .ExecuteDeleteAsync(cancellationToken);
                }
                else
                {
                    // Create new playlist
                    playlist = new Playlist
                    {
                        Id = Guid.NewGuid(),
                        UserId = userId,
                        Name = youtubePlaylist.Title,
                        Description = youtubePlaylist.Description,
                        CreatedAt = DateTimeOffset.UtcNow,
                        UpdatedAt = DateTimeOffset.UtcNow
                    };
                    _dbContext.Playlists.Add(playlist);

                    _logger.LogInformation("Creating new Cantaro playlist {PlaylistId} for YouTube playlist {YouTubePlaylistId}", 
                        playlist.Id, youtubePlaylistId);
                }

                // Step 4: Process each video into an observation and playlist entry
                int failedTracks = 0;
                var playlistEntries = new List<PlaylistEntry>();
                var observationIdsToProcess = new HashSet<Guid>();

                for (int i = 0; i < playlistItems.Count; i++)
                {
                    var item = playlistItems[i];
                    
                    try
                    {
                        var observation = await GetOrCreateObservationForVideoAsync(item, cancellationToken);
                        observationIdsToProcess.Add(observation.Id);
                        
                        var entry = new PlaylistEntry
                        {
                            Id = Guid.NewGuid(),
                            PlaylistId = playlist.Id,
                            TrackObservationId = observation.Id,
                            TrackId = observation.TrackId,
                            Position = i,
                            AddedAt = item.PublishedAt ?? DateTimeOffset.UtcNow,
                            SourceService = ServiceName
                        };
                        playlistEntries.Add(entry);
                    }
                    catch (Exception ex) when (!IsDatabaseException(ex))
                    {
                        _logger.LogWarning(ex, "Failed to process video {VideoId} in playlist {PlaylistId}", 
                            item.VideoId, youtubePlaylistId);
                        failedTracks++;
                    }
                }

                _dbContext.PlaylistEntries.AddRange(playlistEntries);
                _logger.LogInformation("Created {EntryCount} playlist entries for playlist {PlaylistId}", 
                    playlistEntries.Count, playlist.Id);

                await _dbContext.SaveChangesAsync(cancellationToken);

                foreach (var observationId in observationIdsToProcess)
                {
                    await _trackMatchingService.ProcessObservationAsync(observationId, cancellationToken);
                }

                // Step 5: Create or update ServicePlaylistMapping
                if (existingMapping != null)
                {
                    existingMapping.LastSyncedAt = DateTimeOffset.UtcNow;
                    existingMapping.LastSyncStatus = failedTracks > 0 ? "partial_failure" : "success";
                }
                else
                {
                    var mapping = new ServicePlaylistMapping
                    {
                        Id = Guid.NewGuid(),
                        PlaylistId = playlist.Id,
                        Service = ServiceName,
                        ServicePlaylistId = youtubePlaylistId,
                        SyncMode = "import_only",
                        LastSyncedAt = DateTimeOffset.UtcNow,
                        LastSyncStatus = failedTracks > 0 ? "partial_failure" : "success"
                    };
                    _dbContext.ServicePlaylistMappings.Add(mapping);
                }

                // Save all changes
                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                _logger.LogInformation(
                    "Successfully synced YouTube playlist {YouTubePlaylistId} to Cantaro playlist {PlaylistId}. " +
                    "Total items: {TotalItems}, Failed: {FailedItems}",
                    youtubePlaylistId, playlist.Id, playlistItems.Count, failedTracks);

                return playlist.Id;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                _logger.LogError(ex, "Failed to sync YouTube playlist {PlaylistId} for user {UserId}", 
                    youtubePlaylistId, userId);
                throw;
            }
        });
    }

    /// <summary>
    /// Gets or creates a TrackObservation for a YouTube video and refreshes the captured metadata.
    /// </summary>
    private async Task<TrackObservation> GetOrCreateObservationForVideoAsync(
        YouTubePlaylistItemDto video,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var rawMetadata = JsonSerializer.Serialize(new TrackObservationMetadata
        {
            SourceType = ServiceName,
            ExternalId = video.VideoId,
            Title = video.Title,
            Artist = video.ChannelTitle,
            Description = video.Description,
            ThumbnailUrl = video.ThumbnailUrl,
            DurationSeconds = video.DurationSeconds,
            PublishedAt = video.PublishedAt
        });

        var trackedObservation = _dbContext.TrackObservations.Local
            .FirstOrDefault(observation => observation.SourceType == ServiceName && observation.ExternalId == video.VideoId);

        if (trackedObservation != null)
        {
            UpdateObservation(trackedObservation, video, rawMetadata, now);
            return trackedObservation;
        }

        var existingObservation = await _dbContext.TrackObservations
            .FirstOrDefaultAsync(
                observation => observation.SourceType == ServiceName && observation.ExternalId == video.VideoId,
                cancellationToken);

        if (existingObservation != null)
        {
            UpdateObservation(existingObservation, video, rawMetadata, now);
            return existingObservation;
        }

        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(),
            SourceType = ServiceName,
            ExternalId = video.VideoId,
            Title = video.Title,
            Artist = video.ChannelTitle,
            ThumbnailUrl = video.ThumbnailUrl,
            RawMetadata = rawMetadata,
            NormalizedTitle = TrackTextNormalizer.Normalize(video.Title),
            NormalizedArtist = TrackTextNormalizer.Normalize(video.ChannelTitle),
            DurationSeconds = video.DurationSeconds,
            MatchStatus = TrackMatchingStatuses.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };

        _dbContext.TrackObservations.Add(observation);
        return observation;
    }

    private static void UpdateObservation(
        TrackObservation observation,
        YouTubePlaylistItemDto video,
        string rawMetadata,
        DateTimeOffset now)
    {
        observation.Title = video.Title;
        observation.Artist = video.ChannelTitle;
        observation.ThumbnailUrl = video.ThumbnailUrl;
        observation.RawMetadata = rawMetadata;
        observation.NormalizedTitle = TrackTextNormalizer.Normalize(video.Title);
        observation.NormalizedArtist = TrackTextNormalizer.Normalize(video.ChannelTitle);
        observation.DurationSeconds = video.DurationSeconds;
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
        if (ex is DbUpdateException)
        {
            return true;
        }

        return ex.GetBaseException() is DbException;
    }
}
