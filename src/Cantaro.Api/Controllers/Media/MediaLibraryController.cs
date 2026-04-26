using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Controllers;

[ApiController]
[Route("api/media/library")]
[Authorize]
public class MediaLibraryController(
    ApplicationDbContext dbContext,
    MediaLibraryQueryService queryService,
    MediaLibraryLinkService linkService,
    UserManager<User> userManager,
    ILogger<MediaLibraryController> logger) : ControllerBase
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly MediaLibraryQueryService _queryService = queryService;
    private readonly MediaLibraryLinkService _linkService = linkService;
    private readonly UserManager<User> _userManager = userManager;
    private readonly ILogger<MediaLibraryController> _logger = logger;

    /// <summary>
    /// Browse the authenticated user's media library with optional filtering, sorting, and pagination.
    /// </summary>
    /// <param name="status">Filter by normalized status (current, planned, paused, completed, dropped).</param>
    /// <param name="mediaKind">Filter by media kind (anime, manga, movie, series).</param>
    /// <param name="provider">Filter by provider ID (e.g. anilist).</param>
    /// <param name="sortBy">Sort field: title, updatedAt, status, progress. Defaults to updatedAt.</param>
    /// <param name="sortDir">Sort direction: asc or desc. Defaults to desc.</param>
    /// <param name="page">1-based page number. Defaults to 1.</param>
    /// <param name="pageSize">Items per page (1–100). Defaults to 25.</param>
    [HttpGet]
    public async Task<ActionResult<MediaLibraryPageDto>> GetLibrary(
        [FromQuery] string? status,
        [FromQuery] string? mediaKind,
        [FromQuery] string? provider,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var userId = await GetCurrentUserIdAsync();

        var options = new MediaLibraryQueryOptions
        {
            Status = status,
            MediaKind = mediaKind,
            Provider = provider,
            SortBy = string.IsNullOrWhiteSpace(sortBy) ? "updatedAt" : sortBy,
            SortDir = string.IsNullOrWhiteSpace(sortDir) ? "desc" : sortDir,
            Page = page,
            PageSize = pageSize
        };

        var result = await _queryService.GetLibraryAsync(userId, options, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Get a single library entry with full detail: canonical title metadata, progress, and provider links.
    /// </summary>
    [HttpGet("{libraryEntryId:guid}")]
    public async Task<ActionResult<MediaLibraryEntryDetailDto>> GetEntry(
        Guid libraryEntryId,
        CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserIdAsync();
        var detail = await _queryService.GetLibraryEntryDetailAsync(userId, libraryEntryId, cancellationToken);

        return detail is null
            ? NotFound(new { error = "Media library entry not found." })
            : Ok(detail);
    }

    /// <summary>
    /// Manually link a library entry's canonical title to a provider catalog entry.
    /// If the provider/external-ID pair is already associated with a different title a 409 is returned;
    /// set forceRelink=true in the request body to override (no silent reassignment by default).
    /// </summary>
    [HttpPost("{libraryEntryId:guid}/link")]
    public async Task<ActionResult> LinkProvider(
        Guid libraryEntryId,
        [FromBody] MediaLinkRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ProviderId))
        {
            return BadRequest(new { error = "ProviderId is required." });
        }

        if (string.IsNullOrWhiteSpace(request.ProviderMediaId))
        {
            return BadRequest(new { error = "ProviderMediaId is required." });
        }

        var userId = await GetCurrentUserIdAsync();

        var result = await _linkService.LinkProviderAsync(
            userId,
            libraryEntryId,
            request.ProviderId,
            request.ProviderMediaId,
            request.ForceRelink,
            cancellationToken);

        return result.Kind switch
        {
            MediaLinkResultKind.Success => NoContent(),
            MediaLinkResultKind.AlreadyLinked => NoContent(),
            MediaLinkResultKind.EntryNotFound => NotFound(new { error = "Media library entry not found." }),
            MediaLinkResultKind.ConflictingTitle => Conflict(new MediaLinkConflictDto
            {
                Error = "The requested provider/external-ID is already linked to a different title. " +
                        "Set forceRelink=true to override.",
                ConflictingMediaTitleId = result.ConflictingMediaTitleId!.Value,
                ConflictingCanonicalTitle = result.ConflictingCanonicalTitle!
            }),
            _ => StatusCode(StatusCodes.Status500InternalServerError, new { error = "Unexpected link result." })
        };
    }

    /// <summary>
    /// Remove the provider link between this library entry's canonical title and the given provider.
    /// Returns 204 on success, 404 when the entry or link does not exist.
    /// </summary>
    [HttpDelete("{libraryEntryId:guid}/link/{providerId}")]
    public async Task<ActionResult> UnlinkProvider(
        Guid libraryEntryId,
        string providerId,
        CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserIdAsync();

        var result = await _linkService.UnlinkProviderAsync(
            userId,
            libraryEntryId,
            providerId,
            cancellationToken);

        return result.Kind switch
        {
            MediaLinkResultKind.Success => NoContent(),
            MediaLinkResultKind.EntryNotFound => NotFound(new { error = "Media library entry not found." }),
            MediaLinkResultKind.ProviderLinkNotFound => NotFound(new { error = "No link found for this provider." }),
            _ => StatusCode(StatusCodes.Status500InternalServerError, new { error = "Unexpected unlink result." })
        };
    }

    /// <summary>
    /// Update the automatic-progress opt-in flag for a library entry.
    /// When enabled the backend will automatically advance progress counters
    /// from matched observations (subject to monotonic and sync-metadata guards).
    /// PATCH /api/media/library/{id}/auto-progress
    /// </summary>
    [HttpPatch("{libraryEntryId:guid}/auto-progress")]
    public async Task<ActionResult> UpdateAutoProgress(
        Guid libraryEntryId,
        [FromBody] MediaAutoProgressRequest request,
        CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserIdAsync();

        var entry = await _dbContext.MediaLibraryEntries
            .FirstOrDefaultAsync(e => e.Id == libraryEntryId && e.UserId == userId, cancellationToken);

        if (entry is null)
        {
            return NotFound(new { error = "Media library entry not found." });
        }

        entry.AutoProgressFromObservations = request.Enabled;
        entry.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User {UserId} set AutoProgressFromObservations={Enabled} for entry {EntryId}.",
            userId, request.Enabled, libraryEntryId);

        return NoContent();
    }

    private async Task<int> GetCurrentUserIdAsync()
    {
        var email = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new UnauthorizedAccessException("User not authenticated");
        }

        var user = await _userManager.FindByEmailAsync(email);
        if (user is null)
        {
            throw new UnauthorizedAccessException("User not found");
        }

        return user.Id;
    }
}
