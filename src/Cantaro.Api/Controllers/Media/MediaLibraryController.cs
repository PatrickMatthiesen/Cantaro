using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Cantaro.Api.Controllers;

[ApiController]
[Route("api/media/library")]
[Authorize]
public class MediaLibraryController(
    MediaLibraryQueryService queryService,
    UserManager<User> userManager) : ControllerBase
{
    private readonly MediaLibraryQueryService _queryService = queryService;
    private readonly UserManager<User> _userManager = userManager;

    [HttpGet]
    public async Task<ActionResult<MediaLibraryPageDto>> GetLibrary(
        [FromQuery] string? status,
        [FromQuery] string? mediaKind,
        [FromQuery] string? provider,
        [FromQuery] string? providerListName,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? query = null,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.GetUserAsync(User)
            ?? throw new UnauthorizedAccessException("User not authenticated");
        var result = await _queryService.GetLibraryAsync(
            user.Id,
            new MediaLibraryQueryOptions
            {
                Query = query,
                Status = status,
                MediaKind = mediaKind,
                Provider = provider,
                ProviderListName = providerListName,
                SortBy = string.IsNullOrWhiteSpace(sortBy) ? "updatedAt" : sortBy,
                SortDir = string.IsNullOrWhiteSpace(sortDir) ? "desc" : sortDir,
                Page = page,
                PageSize = pageSize
            },
            cancellationToken);
        return Ok(result);
    }
}
