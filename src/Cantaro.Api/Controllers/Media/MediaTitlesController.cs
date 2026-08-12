using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Controllers;

[ApiController]
[Route("api/media/titles")]
public class MediaTitlesController(
    ApplicationDbContext dbContext,
    MediaLibraryQueryService queryService,
    MediaEpisodeIdentityService episodeIdentityService,
    MediaLibraryLinkService linkService,
    UserManager<User> userManager) : ControllerBase
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly MediaLibraryQueryService _queryService = queryService;
    private readonly MediaEpisodeIdentityService _episodeIdentityService = episodeIdentityService;
    private readonly MediaLibraryLinkService _linkService = linkService;
    private readonly UserManager<User> _userManager = userManager;

    [HttpGet("{mediaTitleId:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<MediaTitleDetailDto>> GetTitle(
        Guid mediaTitleId,
        CancellationToken cancellationToken)
    {
        var title = await _queryService.GetMediaTitleAsync(mediaTitleId, cancellationToken);
        return title is null
            ? NotFound(new { error = "Media title not found." })
            : Ok(title);
    }

    [HttpGet("{mediaTitleId:guid}/viewer")]
    [AllowAnonymous]
    public async Task<ActionResult<MediaViewerStateDto?>> GetViewerState(
        Guid mediaTitleId,
        CancellationToken cancellationToken)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return new JsonResult(null) { StatusCode = StatusCodes.Status200OK };
        }

        var state = await _queryService.GetViewerStateAsync(user.Id, mediaTitleId, cancellationToken);
        return new JsonResult(state) { StatusCode = StatusCodes.Status200OK };
    }

    [HttpPost("{mediaTitleId:guid}/viewer")]
    [Authorize]
    public async Task<ActionResult<MediaViewerStateDto>> AddToLibrary(
        Guid mediaTitleId,
        [FromBody] MediaViewerStateCreateDto request,
        CancellationToken cancellationToken)
    {
        if (!WritableStatuses.Contains(request.Status))
        {
            return BadRequest(new { error = "Unsupported media status." });
        }

        var user = await _userManager.GetUserAsync(User)
            ?? throw new UnauthorizedAccessException("User not authenticated");
        var existing = await _queryService.GetViewerStateAsync(user.Id, mediaTitleId, cancellationToken);
        if (existing is not null)
        {
            return Ok(existing);
        }

        var title = await _dbContext.MediaTitles
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == mediaTitleId, cancellationToken);
        if (title is null)
        {
            return NotFound(new { error = "Media title not found." });
        }

        var now = DateTimeOffset.UtcNow;
        _dbContext.MediaLibraryEntries.Add(new MediaLibraryEntry
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            MediaTitleId = title.Id,
            Status = request.Status,
            ProgressEpisodes = title.PrimaryProgressDimension == MediaProgressDimensions.Episode ? 0 : null,
            ProgressChapters = title.PrimaryProgressDimension == MediaProgressDimensions.Chapter ? 0 : null,
            ProgressVolumes = title.PrimaryProgressDimension == MediaProgressDimensions.Volume ? 0 : null,
            LastLocalEditAt = now,
            LastMutationSource = MediaMutationSources.UserStatusUpdate,
            CreatedAt = now,
            UpdatedAt = now
        });
        await _dbContext.SaveChangesAsync(cancellationToken);

        var created = await _queryService.GetViewerStateAsync(user.Id, mediaTitleId, cancellationToken)
            ?? throw new InvalidOperationException("Created viewer state could not be loaded.");
        return CreatedAtAction(nameof(GetViewerState), new { mediaTitleId }, created);
    }

    [HttpGet("{mediaTitleId:guid}/episodes")]
    [AllowAnonymous]
    public async Task<ActionResult<MediaEpisodeCatalogDto>> GetEpisodes(
        Guid mediaTitleId,
        CancellationToken cancellationToken)
    {
        var result = await _episodeIdentityService.GetEpisodeCatalogAsync(mediaTitleId, cancellationToken);
        return result is null
            ? NotFound(new { error = "Media title not found." })
            : Ok(result);
    }

    [HttpGet("{mediaTitleId:guid}/viewer/continue-watching")]
    [Authorize]
    public async Task<ActionResult<MediaContinueWatchingDto>> GetContinueWatching(
        Guid mediaTitleId,
        CancellationToken cancellationToken)
    {
        var user = await _userManager.GetUserAsync(User)
            ?? throw new UnauthorizedAccessException("User not authenticated");
        var result = await _episodeIdentityService.ResolveContinueWatchingAsync(
            user.Id,
            mediaTitleId,
            cancellationToken);
        return result is null
            ? NotFound(new { error = "This title is not in your library." })
            : Ok(result);
    }

    [HttpPost("{mediaTitleId:guid}/links")]
    [Authorize]
    public async Task<ActionResult> LinkProvider(
        Guid mediaTitleId,
        [FromBody] MediaLinkRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ProviderId)
            || string.IsNullOrWhiteSpace(request.ProviderMediaId))
        {
            return BadRequest(new { error = "ProviderId and ProviderMediaId are required." });
        }

        var user = await _userManager.GetUserAsync(User)
            ?? throw new UnauthorizedAccessException("User not authenticated");
        var result = await _linkService.LinkProviderAsync(
            user.Id,
            mediaTitleId,
            request.ProviderId,
            request.ProviderMediaId,
            request.ConfirmReplacement,
            cancellationToken);
        return MapLinkResult(result);
    }

    [HttpDelete("{mediaTitleId:guid}/links/{providerId}")]
    [Authorize]
    public async Task<ActionResult> UnlinkProvider(
        Guid mediaTitleId,
        string providerId,
        CancellationToken cancellationToken)
    {
        var user = await _userManager.GetUserAsync(User)
            ?? throw new UnauthorizedAccessException("User not authenticated");
        return MapLinkResult(await _linkService.UnlinkProviderAsync(
            user.Id,
            mediaTitleId,
            providerId,
            cancellationToken));
    }

    private ActionResult MapLinkResult(MediaLinkResult result)
    {
        return result.Kind switch
        {
            MediaLinkResultKind.Success or MediaLinkResultKind.AlreadyLinked => NoContent(),
            MediaLinkResultKind.EntryNotFound => NotFound(new { error = "This title is not in your library." }),
            MediaLinkResultKind.ProviderLinkNotFound => NotFound(new { error = "No link found for this provider." }),
            MediaLinkResultKind.ConflictingTitle => Conflict(new MediaLinkConflictDto
            {
                Error = "That provider title belongs to another Cantaro title.",
                Code = "provider_identity_owned_by_another_title",
                ConflictingMediaTitleId = result.ConflictingMediaTitleId,
                ConflictingCanonicalTitle = result.ConflictingCanonicalTitle
            }),
            MediaLinkResultKind.ReplacementConfirmationRequired => Conflict(new MediaLinkConflictDto
            {
                Error = "Confirm replacement of the current provider identity.",
                Code = "replacement_confirmation_required",
                CurrentProviderMediaId = result.CurrentProviderMediaId
            }),
            MediaLinkResultKind.LinkInUse => Conflict(new MediaLinkConflictDto
            {
                Error = "This provider identity is used by synchronized library state.",
                Code = "provider_identity_in_use",
                CurrentProviderMediaId = result.CurrentProviderMediaId
            }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    private static readonly HashSet<string> WritableStatuses =
    [
        MediaLibraryStatuses.Current,
        MediaLibraryStatuses.Planned,
        MediaLibraryStatuses.Paused,
        MediaLibraryStatuses.Completed,
        MediaLibraryStatuses.Dropped,
        MediaLibraryStatuses.Repeating
    ];
}
