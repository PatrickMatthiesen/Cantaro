using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
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
    MediaLibraryEventHub eventHub,
    UserManager<User> userManager) : ControllerBase
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MaximumStreamDuration = TimeSpan.FromMinutes(5);
    private readonly MediaLibraryQueryService _queryService = queryService;
    private readonly MediaLibraryEventHub _eventHub = eventHub;
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

    [HttpGet("events")]
    public async Task<IResult> Events(CancellationToken cancellationToken)
    {
        var user = await _userManager.GetUserAsync(User)
            ?? throw new UnauthorizedAccessException("User not authenticated");
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
        return TypedResults.ServerSentEvents(ReadEvents(user.Id, cancellationToken));
    }

    internal async IAsyncEnumerable<SseItem<MediaLibraryChangedEvent>> ReadEvents(
        int userId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var reader = _eventHub.Subscribe(userId, out var subscriptionId);
        using var streamCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        streamCancellation.CancelAfter(MaximumStreamDuration);
        using var heartbeatTimer = new PeriodicTimer(HeartbeatInterval);

        try
        {
            yield return new SseItem<MediaLibraryChangedEvent>(new(), "library-changed");

            var waitForEvent = WaitToReadAsync(reader, streamCancellation.Token);
            var waitForHeartbeat = WaitForHeartbeatAsync(heartbeatTimer, streamCancellation.Token);
            while (!streamCancellation.IsCancellationRequested)
            {
                var completed = await Task.WhenAny(waitForEvent, waitForHeartbeat);
                if (completed == waitForEvent)
                {
                    if (!await waitForEvent)
                    {
                        yield break;
                    }

                    while (reader.TryRead(out var libraryEvent))
                    {
                        yield return new SseItem<MediaLibraryChangedEvent>(libraryEvent, "library-changed");
                    }

                    waitForEvent = WaitToReadAsync(reader, streamCancellation.Token);
                    continue;
                }

                if (!await waitForHeartbeat)
                {
                    yield break;
                }

                yield return new SseItem<MediaLibraryChangedEvent>(new(), "heartbeat");
                waitForHeartbeat = WaitForHeartbeatAsync(heartbeatTimer, streamCancellation.Token);
            }
        }
        finally
        {
            await streamCancellation.CancelAsync();
            _eventHub.Unsubscribe(userId, subscriptionId);
        }
    }

    private static async Task<bool> WaitToReadAsync(
        ChannelReader<MediaLibraryChangedEvent> reader,
        CancellationToken cancellationToken)
    {
        try
        {
            return await reader.WaitToReadAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static async Task<bool> WaitForHeartbeatAsync(
        PeriodicTimer timer,
        CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
