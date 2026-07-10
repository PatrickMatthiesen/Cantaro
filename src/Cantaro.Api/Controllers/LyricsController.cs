using Cantaro.Api.Models;
using Cantaro.Api.Services.Lyrics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Cantaro.Api.Controllers;

[ApiController]
[Route("api/music/tracks")]
[Authorize]
public sealed class LyricsController(LyricsService lyricsService, UserManager<User> userManager) : ControllerBase
{
    [HttpGet("{trackId:guid}/lyrics")]
    public async Task<ActionResult<LyricsResult>> GetLyrics(Guid trackId, CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        var result = await lyricsService.GetLyricsAsync(user.Id, trackId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }
}
