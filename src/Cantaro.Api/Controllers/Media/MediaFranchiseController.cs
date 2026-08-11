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
    IMediaProviderRegistry mediaProviderRegistry,
    MediaTitleRelationSyncService relationSyncService,
    MediaFranchiseGraphService graphService,
    UserManager<User> userManager,
    ILogger<MediaFranchiseController> logger) : ControllerBase
{
    private const string AniListProvider = "anilist";
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly IMediaProviderRegistry _mediaProviderRegistry = mediaProviderRegistry;
    private readonly MediaTitleRelationSyncService _relationSyncService = relationSyncService;
    private readonly MediaFranchiseGraphService _graphService = graphService;
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
            .Select(link => new { link.MediaTitleId, link.ExternalId })
            .SingleOrDefaultAsync(cancellationToken);

        if (root is null)
        {
            var existing = await _graphService.GetAsync(user?.Id, mediaTitleId, cancellationToken);
            return existing is null
                ? NotFound(new { error = "Media title not found." })
                : Ok(existing);
        }

        var freshnessCutoff = DateTimeOffset.UtcNow.AddHours(-6);
        var hasFreshGraph = await _dbContext.MediaTitleRelations
            .AsNoTracking()
            .AnyAsync(relation => relation.MediaTitleId == root.MediaTitleId
                && relation.SourceProvider == AniListProvider
                && relation.LastVerifiedAt >= freshnessCutoff,
                cancellationToken);
        if (user is not null
            && !hasFreshGraph
            && _mediaProviderRegistry.GetRequired(AniListProvider) is IMediaRelationGraphProvider relationProvider)
        {
            try
            {
                await _relationSyncService.SyncAsync(user.Id, relationProvider, root.ExternalId, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(
                    exception,
                    "AniList franchise refresh failed for media title {MediaTitleId}; serving the last persisted graph.",
                    mediaTitleId);
            }
        }

        var graph = await _graphService.GetAsync(user?.Id, mediaTitleId, cancellationToken);
        return graph is null
            ? NotFound(new { error = "Media title not found." })
            : Ok(graph);
    }
}
