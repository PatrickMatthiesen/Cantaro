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
