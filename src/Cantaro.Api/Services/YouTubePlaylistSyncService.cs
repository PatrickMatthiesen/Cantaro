using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Cantaro.Api.Services;

/// <summary>
/// DTO for track canonical metadata
/// </summary>
public class TrackMetadata
{
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
}

/// <summary>
/// DTO for track origin metadata
/// </summary>
public class TrackOriginMetadata
{
    public string? ChannelTitle { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}

/// <summary>
/// Service for syncing YouTube playlists to Cantaro's canonical playlist system
/// </summary>
public class YouTubePlaylistSyncService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly YouTubeService _youtubeService;
    private readonly ILogger<YouTubePlaylistSyncService> _logger;

    private const string ServiceName = "youtube";

    public YouTubePlaylistSyncService(
        ApplicationDbContext dbContext,
        YouTubeService youtubeService,
        ILogger<YouTubePlaylistSyncService> logger)
    {
        _dbContext = dbContext;
        _youtubeService = youtubeService;
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

                // Step 4: Process each video and create PlaylistEntry records
                int failedTracks = 0;
                var playlistEntries = new List<PlaylistEntry>();

                for (int i = 0; i < playlistItems.Count; i++)
                {
                    var item = playlistItems[i];
                    
                    try
                    {
                        var trackId = await GetOrCreateTrackForVideoAsync(item, cancellationToken);
                        
                        var entry = new PlaylistEntry
                        {
                            Id = Guid.NewGuid(),
                            PlaylistId = playlist.Id,
                            TrackId = trackId,
                            Position = i,
                            AddedAt = item.PublishedAt ?? DateTimeOffset.UtcNow,
                            SourceService = ServiceName
                        };
                        playlistEntries.Add(entry);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to process video {VideoId} in playlist {PlaylistId}", 
                            item.VideoId, youtubePlaylistId);
                        failedTracks++;
                    }
                }

                _dbContext.PlaylistEntries.AddRange(playlistEntries);
                _logger.LogInformation("Created {EntryCount} playlist entries for playlist {PlaylistId}", 
                    playlistEntries.Count, playlist.Id);

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
    /// Gets or creates a Track entity for a YouTube video using atomic UPSERT pattern
    /// to handle concurrent requests safely
    /// </summary>
    private async Task<Guid> GetOrCreateTrackForVideoAsync(
        YouTubePlaylistItemDto video,
        CancellationToken cancellationToken)
    {
        // Create Track and TrackSourceId entities
        var trackId = Guid.NewGuid();
        var sourceIdGuid = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        
        var canonicalMetadata = JsonSerializer.Serialize(new TrackMetadata
        {
            Title = video.Title,
            Artist = video.ChannelTitle,
            Description = video.Description,
            ThumbnailUrl = video.ThumbnailUrl
        });
        
        var originMetadata = JsonSerializer.Serialize(new TrackOriginMetadata
        {
            ChannelTitle = video.ChannelTitle,
            PublishedAt = video.PublishedAt
        });

        // Attempt atomic insert using PostgreSQL's ON CONFLICT DO NOTHING
        // This handles race conditions where multiple transactions try to insert the same (SourceType, ExternalId)
        var insertedRows = await _dbContext.Database.ExecuteSqlRawAsync(
            @"WITH inserted_track AS (
                INSERT INTO ""Tracks"" (""Id"", ""CanonicalMetadata"", ""MbidRecording"", ""Isrc"", ""CreatedAt"", ""UpdatedAt"")
                VALUES ({0}, {1}, NULL, NULL, {2}, {3})
                ON CONFLICT DO NOTHING
                RETURNING ""Id""
            )
            INSERT INTO ""TrackSourceIds"" (""Id"", ""TrackId"", ""SourceType"", ""ExternalId"", ""Confidence"", ""OriginMetadata"", ""LastVerifiedAt"")
            SELECT {4}, {0}, {5}, {6}, NULL, {7}, {8}
            FROM inserted_track
            ON CONFLICT (""SourceType"", ""ExternalId"") DO NOTHING",
            trackId,
            canonicalMetadata,
            now,
            now,
            sourceIdGuid,
            ServiceName,
            video.VideoId,
            originMetadata,
            now);

        if (insertedRows > 0)
        {
            // Successfully inserted new Track and TrackSourceId
            _logger.LogDebug("Created new Track {TrackId} for YouTube video {VideoId}", trackId, video.VideoId);
            return trackId;
        }

        // Conflict occurred - another transaction already created this mapping
        // Query for the existing TrackSourceId and update LastVerifiedAt
        var existingSourceId = await _dbContext.TrackSourceIds
            .FirstOrDefaultAsync(
                s => s.SourceType == ServiceName && s.ExternalId == video.VideoId,
                cancellationToken);

        if (existingSourceId == null)
        {
            // This should never happen, but handle defensively
            _logger.LogError("Failed to find existing TrackSourceId after conflict for YouTube video {VideoId}", video.VideoId);
            throw new InvalidOperationException($"Failed to resolve TrackSourceId for YouTube video {video.VideoId}");
        }

        // Update last verified timestamp
        existingSourceId.LastVerifiedAt = now;
        _logger.LogDebug("Found existing Track {TrackId} for YouTube video {VideoId} (concurrent insert)", 
            existingSourceId.TrackId, video.VideoId);

        return existingSourceId.TrackId;
    }
}
