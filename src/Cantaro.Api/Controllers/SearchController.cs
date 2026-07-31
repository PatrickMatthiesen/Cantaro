using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Cantaro.Api.Controllers;

[ApiController]
[Route("api/search")]
[Authorize]
public sealed class SearchController(
    CantaroSearchService searchService,
    UserManager<User> userManager) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SearchResponseDto>> Search(
        [FromQuery] string? q,
        [FromQuery] int limitPerGroup = CantaroSearchService.DefaultLimitPerGroup,
        [FromQuery] bool includeDiscovery = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return BadRequest(new { error = "A non-empty q query parameter is required." });
        }

        if (q.Trim().Length > CantaroSearchService.MaximumQueryLength)
        {
            return BadRequest(new
            {
                error = $"The q query parameter cannot exceed {CantaroSearchService.MaximumQueryLength} characters."
            });
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        return Ok(await searchService.SearchAsync(
            user.Id,
            q,
            limitPerGroup,
            includeDiscovery,
            cancellationToken));
    }
}
