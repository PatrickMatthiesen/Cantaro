using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Cantaro.Api.Controllers;

public sealed record AttachPlaylistRequest(string Service, string ServicePlaylistId);
public sealed record ConfirmAttachPlaylistRequest(
    string Service, string ServicePlaylistId, string SyncMode, string InitialMode, string PreviewToken);
public sealed record RunPlaylistSyncRequest(string? Service);
public sealed record CreatePlaylistLinkRequest(string Service, string? PreviewToken = null);
public sealed record SetPlaylistSyncEnabledRequest(bool SyncEnabled);
public sealed record SetPlaylistDuplicatesRequest(bool AllowDuplicateTracks);
public sealed record PlaylistRenameRequest(string Name);
public sealed record ConfirmPlaylistRenameRequest(string Name, string PreviewToken);
public sealed record PlaylistRenameProposalDecisionRequest(bool Accept, string PreviewToken);
public sealed record DeletePlaylistLinkRequest(bool DeleteRemote, bool DeleteCanonicalIfLast);
public sealed record RetryPlaylistInitializationRequest(string PreviewToken);
public sealed record ResolvePlaylistOrderRequest(string Use);
public sealed record SearchPlaylistMatchRequest(string? Query);
public sealed record ConfirmPlaylistMatchRequest(string CandidateToken);

[ApiController]
[Route("api/music/playlists/{playlistId:guid}/sync")]
[Authorize]
public sealed class PlaylistSyncController(
    PlaylistLinkLifecycleService lifecycle,
    PlaylistUnmatchedReviewService unmatchedReview,
    UserManager<User> userManager) : ControllerBase
{
    [HttpGet]
    public Task<ActionResult<PlaylistSyncStatus>> Get(Guid playlistId, CancellationToken ct)
        => ExecuteAsync(() => lifecycle.GetStatusAsync(UserId(), playlistId, ct));

    [HttpPost("attach/preview")]
    public Task<ActionResult<PlaylistAttachPreview>> AttachPreview(
        Guid playlistId, [FromBody] AttachPlaylistRequest request, CancellationToken ct)
        => ExecuteAsync(() => lifecycle.PreviewAttachAsync(UserId(), playlistId,
            request.Service, request.ServicePlaylistId, ct));

    [HttpPost("attach")]
    public Task<ActionResult<PlaylistSyncStatus>> Attach(
        Guid playlistId, [FromBody] ConfirmAttachPlaylistRequest request, CancellationToken ct)
        => ExecuteAsync(() => lifecycle.AttachAsync(UserId(), playlistId,
            request.Service, request.ServicePlaylistId, request.SyncMode, request.InitialMode,
            request.PreviewToken, ct));

    [HttpPost("run")]
    public Task<ActionResult<PlaylistSyncStatus>> Run(
        Guid playlistId, [FromBody] RunPlaylistSyncRequest request, CancellationToken ct)
        => ExecuteAsync(() => lifecycle.RunAsync(UserId(), playlistId, request.Service, ct));

    [HttpGet("links/{mappingId:guid}/unmatched")]
    public Task<ActionResult<IReadOnlyList<PlaylistUnmatchedEntry>>> Unmatched(
        Guid playlistId, Guid mappingId, CancellationToken ct)
        => ExecuteAsync(() => unmatchedReview.ListAsync(UserId(), playlistId, mappingId, ct));

    [HttpPost("links/{mappingId:guid}/unmatched/{entryId:guid}/search")]
    public Task<ActionResult<PlaylistMatchSearch>> SearchUnmatched(
        Guid playlistId, Guid mappingId, Guid entryId,
        [FromBody] SearchPlaylistMatchRequest request, CancellationToken ct)
        => ExecuteAsync(() => unmatchedReview.SearchAsync(UserId(), playlistId, mappingId,
            entryId, request.Query, ct));

    [HttpPost("links/{mappingId:guid}/unmatched/{entryId:guid}/confirm")]
    public async Task<IActionResult> ConfirmUnmatched(
        Guid playlistId, Guid mappingId, Guid entryId,
        [FromBody] ConfirmPlaylistMatchRequest request, CancellationToken ct)
    {
        try
        {
            await unmatchedReview.ConfirmAsync(UserId(), playlistId, mappingId,
                entryId, request.CandidateToken, ct);
            return NoContent();
        }
        catch (PlatformApiException ex) { return Error(ex); }
    }

    [HttpPost("links/{mappingId:guid}/initialization/preview")]
    public Task<ActionResult<PlaylistAttachPreview>> InitializationPreview(
        Guid playlistId, Guid mappingId, CancellationToken ct)
        => ExecuteAsync(() => lifecycle.PreviewPendingInitializationAsync(UserId(), playlistId, mappingId, ct));

    [HttpPost("links/{mappingId:guid}/initialization/confirm")]
    public Task<ActionResult<PlaylistSyncStatus>> InitializationConfirm(
        Guid playlistId, Guid mappingId, [FromBody] RetryPlaylistInitializationRequest request, CancellationToken ct)
        => ExecuteAsync(() => lifecycle.RetryPendingInitializationAsync(
            UserId(), playlistId, mappingId, request.PreviewToken, ct));

    [HttpPost("links/{mappingId:guid}/order")]
    public Task<ActionResult<PlaylistSyncStatus>> ResolveOrder(
        Guid playlistId, Guid mappingId, [FromBody] ResolvePlaylistOrderRequest request, CancellationToken ct)
        => ExecuteAsync(() => lifecycle.ResolveOrderAsync(UserId(), playlistId, mappingId, request.Use, ct));

    [HttpPost("links/create")]
    public Task<ActionResult<PlaylistSyncStatus>> CreateLink(
        Guid playlistId, [FromBody] CreatePlaylistLinkRequest request, CancellationToken ct)
        => ExecuteAsync(() => lifecycle.CreateLinkAsync(UserId(), playlistId,
            request.Service, request.PreviewToken, ct));

    [HttpPost("links/create/preview")]
    public Task<ActionResult<PlaylistCreatePreview>> CreatePreview(
        Guid playlistId, [FromBody] CreatePlaylistLinkRequest request, CancellationToken ct)
        => ExecuteAsync(() => lifecycle.PreviewCreateAsync(UserId(), playlistId, request.Service, ct));

    [HttpPut("enabled")]
    public Task<ActionResult<PlaylistSyncStatus>> SetEnabled(
        Guid playlistId, [FromBody] SetPlaylistSyncEnabledRequest request, CancellationToken ct)
        => ExecuteAsync(() => lifecycle.SetEnabledAsync(UserId(), playlistId, request.SyncEnabled, ct));

    [HttpPut("duplicates")]
    public Task<ActionResult<PlaylistSyncStatus>> SetDuplicates(
        Guid playlistId, [FromBody] SetPlaylistDuplicatesRequest request, CancellationToken ct)
        => ExecuteAsync(() => lifecycle.SetDuplicatesAsync(UserId(), playlistId, request.AllowDuplicateTracks, ct));

    [HttpPost("rename/preview")]
    public Task<ActionResult<PlaylistRenamePreview>> RenamePreview(
        Guid playlistId, [FromBody] PlaylistRenameRequest request, CancellationToken ct)
        => ExecuteAsync(() => lifecycle.PreviewRenameAsync(UserId(), playlistId, request.Name, ct));

    [HttpPost("rename/confirm")]
    public Task<ActionResult<PlaylistSyncStatus>> RenameConfirm(
        Guid playlistId, [FromBody] ConfirmPlaylistRenameRequest request, CancellationToken ct)
        => ExecuteAsync(() => lifecycle.ConfirmRenameAsync(UserId(), playlistId,
            request.Name, request.PreviewToken, ct));

    [HttpPost("rename/proposals/{mappingId:guid}/preview")]
    public Task<ActionResult<PlaylistRenameProposalPreview>> RenameProposalPreview(
        Guid playlistId, Guid mappingId, CancellationToken ct)
        => ExecuteAsync(() => lifecycle.PreviewRenameProposalAsync(UserId(), playlistId, mappingId, ct));

    [HttpPost("rename/proposals/{mappingId:guid}/decision")]
    public Task<ActionResult<PlaylistSyncStatus>> RenameProposalDecision(
        Guid playlistId, Guid mappingId, [FromBody] PlaylistRenameProposalDecisionRequest request, CancellationToken ct)
        => ExecuteAsync(() => lifecycle.DecideRenameProposalAsync(UserId(), playlistId,
            mappingId, request.Accept, request.PreviewToken, ct));

    [HttpDelete("links/{mappingId:guid}")]
    public async Task<ActionResult> DeleteLink(Guid playlistId, Guid mappingId,
        [FromBody] DeletePlaylistLinkRequest request, CancellationToken ct)
    {
        try
        {
            await lifecycle.UnlinkAsync(UserId(), playlistId, mappingId,
                request.DeleteRemote, request.DeleteCanonicalIfLast, ct);
            return NoContent();
        }
        catch (PlatformApiException ex) { return Error(ex); }
    }

    private int UserId()
    {
        var value = userManager.GetUserId(User);
        return int.TryParse(value, out var userId) ? userId : throw new UnauthorizedAccessException();
    }

    private async Task<ActionResult<T>> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (PlatformApiException ex) { return Error(ex); }
    }

    private ObjectResult Error(PlatformApiException ex)
    {
        if (ex.RetryAfter is { } retryAfter)
            Response.Headers.RetryAfter = Math.Max(0, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        return StatusCode(ex.StatusCode, new { code = ex.Code, error = ex.Message });
    }
}
