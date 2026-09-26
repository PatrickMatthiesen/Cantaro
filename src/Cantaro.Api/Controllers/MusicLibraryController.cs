using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Cantaro.Api.Controllers;

[ApiController]
[Route("api/music/library")]
[Authorize]
public class MusicLibraryController(
    MusicLibraryQueryService musicLibraryQueryService,
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
        => await _musicLibraryQueryService.AddCanonicalSongToPlaylistAsync(
            trackId, playlistId, await GetCurrentUserIdAsync(), cancellationToken)
            ? NoContent() : NotFound();

    [HttpDelete("playlists/{playlistId:guid}/songs/{trackId:guid}")]
    public async Task<ActionResult> RemoveSong(Guid playlistId, Guid trackId,
        [FromQuery] string? youtubeVideoId, CancellationToken cancellationToken, [FromQuery] Guid? entryId = null)
        => await _musicLibraryQueryService.RemoveCanonicalSongFromPlaylistAsync(
            trackId, playlistId, await GetCurrentUserIdAsync(), cancellationToken, entryId)
            ? NoContent() : NotFound();

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
