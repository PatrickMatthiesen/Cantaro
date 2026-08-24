using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Cantaro.Api.Controllers;

[ApiController]
[Route("api/media/providers/{providerId}/initial-sync")]
[Authorize]
public sealed class MediaProviderInitialSyncController(
    IMediaProviderRegistry mediaProviderRegistry,
    MediaProviderInitialSyncService initialSyncService,
    UserManager<User> userManager) : ControllerBase
{
    private readonly IMediaProviderRegistry _mediaProviderRegistry = mediaProviderRegistry;
    private readonly MediaProviderInitialSyncService _initialSyncService = initialSyncService;
    private readonly UserManager<User> _userManager = userManager;

    [HttpPost("preview")]
    public async Task<ActionResult<MediaProviderInitialSyncPreviewDto>> Preview(
        string providerId,
        CancellationToken cancellationToken)
    {
        if (!_mediaProviderRegistry.IsSupported(providerId))
        {
            return NotFound(new { error = $"Media provider '{providerId}' is not implemented" });
        }

        try
        {
            return Ok(await _initialSyncService.PreviewAsync(
                await GetCurrentUserIdAsync(),
                providerId,
                cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpPost("apply")]
    public async Task<ActionResult<MediaProviderInitialSyncResultDto>> Apply(
        string providerId,
        [FromBody] MediaProviderInitialSyncApplyDto request,
        CancellationToken cancellationToken)
    {
        if (!_mediaProviderRegistry.IsSupported(providerId))
        {
            return NotFound(new { error = $"Media provider '{providerId}' is not implemented" });
        }

        if (string.IsNullOrWhiteSpace(request.Fingerprint))
        {
            return BadRequest(new { error = "A current initial-sync preview is required." });
        }

        try
        {
            var result = await _initialSyncService.ApplyAsync(
                await GetCurrentUserIdAsync(),
                providerId,
                request.Fingerprint,
                cancellationToken);
            return result.QueuedOperations > 0 ? Accepted(result) : Ok(result);
        }
        catch (MediaProviderInitialSyncChangedException exception)
        {
            return Conflict(new { error = exception.Message, code = "preview_changed" });
        }
        catch (MediaProviderInitialSyncNotReadyException exception)
        {
            return Conflict(new { error = exception.Message, code = "providers_not_settled" });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpGet("batches/{batchId:guid}")]
    public async Task<ActionResult<MediaProviderInitialSyncProgressDto>> GetProgress(
        string providerId,
        Guid batchId,
        CancellationToken cancellationToken)
    {
        if (!_mediaProviderRegistry.IsSupported(providerId))
        {
            return NotFound(new { error = $"Media provider '{providerId}' is not implemented" });
        }

        return Ok(await _initialSyncService.GetProgressAsync(
            await GetCurrentUserIdAsync(),
            providerId,
            batchId,
            cancellationToken));
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
