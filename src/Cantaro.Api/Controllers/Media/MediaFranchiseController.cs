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
public sealed class MediaFranchiseController(
    ApplicationDbContext dbContext,
    MediaFranchiseGraphService graphService,
    MediaRelationGraphRefreshQueue relationGraphRefreshQueue,
    MediaTitleRelationSyncService relationSyncService,
    IMediaProviderRegistry mediaProviderRegistry,
    UserManager<User> userManager,
    ILogger<MediaFranchiseController> logger) : ControllerBase
{
    private const string AniListProvider = "anilist";
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly MediaFranchiseGraphService _graphService = graphService;
    private readonly MediaRelationGraphRefreshQueue _relationGraphRefreshQueue = relationGraphRefreshQueue;
    private readonly MediaTitleRelationSyncService _relationSyncService = relationSyncService;
    private readonly IMediaProviderRegistry _mediaProviderRegistry = mediaProviderRegistry;
    private readonly UserManager<User> _userManager = userManager;
    private readonly ILogger<MediaFranchiseController> _logger = logger;

    [HttpGet("{mediaTitleId:guid}/franchise")]
    [AllowAnonymous]
    public async Task<ActionResult<MediaFranchiseGraphDto>> GetFranchise(
        Guid mediaTitleId,
        CancellationToken cancellationToken)
    {
        var user = await _userManager.GetUserAsync(User);
        var root = await _dbContext.MediaTitles
            .AsNoTracking()
            .Where(title => title.Id == mediaTitleId)
            .SelectMany(title => title.ProviderLinks)
            .Where(link => link.Provider == AniListProvider)
            .Select(link => new { link.MediaTitleId, link.ExternalId, link.RelationsLastVerifiedAt })
            .SingleOrDefaultAsync(cancellationToken);

        if (root is null)
        {
            var existing = await _graphService.GetAsync(user?.Id, mediaTitleId, cancellationToken);
            return existing is null
                ? NotFound(new { error = "Media title not found." })
                : Ok(existing);
        }

        if (root.RelationsLastVerifiedAt is null)
        {
            if (user is null)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    error = "Franchise connections have not been synchronized yet. Sign in and retry."
                });
            }

            try
            {
                if (_mediaProviderRegistry.GetRequired(AniListProvider) is not IMediaRelationGraphProvider provider)
                {
                    return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                    {
                        error = "AniList franchise synchronization is unavailable."
                    });
                }

                await _relationSyncService.SyncAsync(
                    user.Id,
                    provider,
                    root.ExternalId,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Initial franchise synchronization failed for MediaTitle {MediaTitleId}.",
                    mediaTitleId);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    error = "Could not synchronize franchise connections from AniList."
                });
            }
        }

        var freshnessCutoff = DateTimeOffset.UtcNow.AddHours(-6);
        var hasFreshGraph = root.RelationsLastVerifiedAt >= freshnessCutoff;
        var graph = await _graphService.GetAsync(user?.Id, mediaTitleId, cancellationToken);
        if (graph is null)
        {
            return NotFound(new { error = "Media title not found." });
        }

        if (user is not null
            && root.RelationsLastVerifiedAt is not null
            && (!hasFreshGraph || !graph.Continuity.IsComplete))
        {
            _relationGraphRefreshQueue.Enqueue(user.Id, AniListProvider, root.ExternalId);
        }

        return Ok(graph);
    }
}
