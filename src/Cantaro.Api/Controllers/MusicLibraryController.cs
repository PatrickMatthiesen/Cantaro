using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Google;

namespace Cantaro.Api.Controllers;

[ApiController]
[Route("api/music/library")]
[Authorize]
public class MusicLibraryController(
    MusicLibraryQueryService musicLibraryQueryService,
    ApplicationDbContext dbContext,
    YouTubeService youtubeService,
    UserManager<User> userManager) : ControllerBase
{
    private readonly MusicLibraryQueryService _musicLibraryQueryService = musicLibraryQueryService;
    private readonly UserManager<User> _userManager = userManager;

    [HttpGet]
    public async Task<ActionResult<MusicLibraryResponse>> GetLibrary(CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserIdAsync();
        return Ok(await _musicLibraryQueryService.GetLibraryAsync(userId, cancellationToken));
    }

    [HttpGet("songs/{trackId:guid}")]
    public async Task<ActionResult<MusicLibrarySongDto>> GetSong(Guid trackId, CancellationToken cancellationToken)
    {
        var song = await _musicLibraryQueryService.GetCanonicalSongAsync(trackId, await GetCurrentUserIdAsync(), cancellationToken);
        return song is null ? NotFound() : Ok(song);
    }

    [HttpPost("playlists/{playlistId:guid}/songs/{trackId:guid}")]
    public async Task<ActionResult> AddSong(Guid playlistId, Guid trackId, [FromQuery] string? youtubeVideoId, CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserIdAsync();
        var playlist = await dbContext.Playlists.AsNoTracking().Include(x => x.Entries).Include(x => x.ServiceMappings)
            .FirstOrDefaultAsync(x => x.Id == playlistId && x.UserId == userId, cancellationToken);
        if (playlist is null || !await dbContext.Tracks.AnyAsync(x => x.Id == trackId, cancellationToken)) return NotFound();
        if (playlist.Entries.Any(x => x.TrackId == trackId)) return NoContent();
        var videoId = await GetPreferredYouTubeIdAsync(trackId, playlistId, youtubeVideoId, cancellationToken);
        var mappings = playlist.ServiceMappings.Where(IsOutboundYouTubeMapping).Select(x => x.ServicePlaylistId).Distinct().ToList();
        if (mappings.Count > 0 && string.IsNullOrWhiteSpace(videoId)) return Conflict(new { error = "This track has no YouTube source to sync." });
        var changedMappings = new List<string>();
        try { foreach (var mapping in mappings) if (await youtubeService.AddVideoToPlaylistAsync(userId, mapping, videoId!, cancellationToken)) changedMappings.Add(mapping); }
        catch (YouTubePlaylistReconciliationException ex) { return Problem(statusCode: 502, title: "Playlist update needs reconciliation", detail: ex.Message); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
        catch (GoogleApiException) { return Problem(statusCode: StatusCodes.Status502BadGateway, title: "YouTube playlist update failed", detail: "Cantaro was not changed. Try again after the provider is available."); }
        try
        {
            return await _musicLibraryQueryService.AddCanonicalSongToPlaylistAsync(trackId, playlistId, userId, cancellationToken) ? NoContent() : NotFound();
        }
        catch (DbUpdateException)
        {
            try { foreach (var mapping in changedMappings) await youtubeService.RemoveVideoFromPlaylistAsync(userId, mapping, videoId!, CancellationToken.None); }
            catch { return Problem(statusCode: 502, title: "Playlist update needs reconciliation", detail: "YouTube changed but Cantaro could not save or roll back the change."); }
            if (await dbContext.PlaylistEntries.AsNoTracking().AnyAsync(x => x.PlaylistId == playlistId && x.TrackId == trackId, cancellationToken)) return NoContent();
            throw;
        }
    }

    [HttpDelete("playlists/{playlistId:guid}/songs/{trackId:guid}")]
    public async Task<ActionResult> RemoveSong(Guid playlistId, Guid trackId, [FromQuery] string? youtubeVideoId, CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserIdAsync();
        var playlist = await dbContext.Playlists.AsNoTracking().Include(x => x.Entries).Include(x => x.ServiceMappings)
            .FirstOrDefaultAsync(x => x.Id == playlistId && x.UserId == userId, cancellationToken);
        if (playlist is null) return NotFound();
        if (!playlist.Entries.Any(x => x.TrackId == trackId)) return NoContent();
        var videoId = await GetPreferredYouTubeIdAsync(trackId, playlistId, youtubeVideoId, cancellationToken);
        var mappings = playlist.ServiceMappings.Where(IsOutboundYouTubeMapping).Select(x => x.ServicePlaylistId).Distinct().ToList();
        if (mappings.Count > 0 && string.IsNullOrWhiteSpace(videoId)) return Conflict(new { error = "This track has no YouTube source to sync." });
        var changedMappings = new List<(string PlaylistId, IReadOnlyList<long?> Positions)>();
        try
        {
            foreach (var mapping in mappings)
            {
                var removal = await youtubeService.RemoveVideoFromPlaylistAsync(userId, mapping, videoId!, cancellationToken);
                if (removal.Changed) changedMappings.Add((mapping, removal.Positions));
            }
        }
        catch (YouTubePlaylistReconciliationException ex) { return Problem(statusCode: 502, title: "Playlist update needs reconciliation", detail: ex.Message); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
        catch (GoogleApiException) { return Problem(statusCode: StatusCodes.Status502BadGateway, title: "YouTube playlist update failed", detail: "Cantaro was not changed. Try again after the provider is available."); }
        try
        {
            return await _musicLibraryQueryService.RemoveCanonicalSongFromPlaylistAsync(trackId, playlistId, userId, cancellationToken) ? NoContent() : NotFound();
        }
        catch (DbUpdateException)
        {
            try
            {
                foreach (var mapping in changedMappings)
                    foreach (var position in mapping.Positions)
                        await youtubeService.RestoreVideoToPlaylistAsync(userId, mapping.PlaylistId, videoId!, position, CancellationToken.None);
            }
            catch { return Problem(statusCode: 502, title: "Playlist update needs reconciliation", detail: "YouTube changed but Cantaro could not save or roll back the change."); }
            throw;
        }
    }

    private async Task<string?> GetPreferredYouTubeIdAsync(Guid trackId, Guid playlistId, string? requestedVideoId, CancellationToken cancellationToken)
    {
        // Prefer the exact YouTube version already observed in this playlist. A canonical
        // recording can have several uploads (official video, lyric video, live version).
        var observed = await dbContext.PlaylistEntries.AsNoTracking()
            .Where(x => x.PlaylistId == playlistId && x.TrackId == trackId && x.TrackObservation != null && x.TrackObservation.SourceType == "youtube")
            .OrderBy(x => x.Position).Select(x => x.TrackObservation!.ExternalId).FirstOrDefaultAsync(cancellationToken);
        if (observed is not null) return observed;
        var sourceIds = await dbContext.TrackSourceIds.AsNoTracking()
            .Where(x => x.TrackId == trackId && x.SourceType == "youtube")
            .OrderBy(x => x.ExternalId).Select(x => x.ExternalId).ToListAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(requestedVideoId))
            return sourceIds.Contains(requestedVideoId, StringComparer.Ordinal) ? requestedVideoId : null;
        return sourceIds.Count == 1 ? sourceIds[0] : null;
    }

    private static bool IsOutboundYouTubeMapping(ServicePlaylistMapping mapping) =>
        mapping.Service.Equals("youtube", StringComparison.OrdinalIgnoreCase)
        && (mapping.SyncMode.Equals("from_cantaro", StringComparison.OrdinalIgnoreCase)
            || mapping.SyncMode.Equals("bidirectional", StringComparison.OrdinalIgnoreCase));

    private async Task<int> GetCurrentUserIdAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            throw new UnauthorizedAccessException("User not authenticated");
        }

        return user.Id;
    }
}
