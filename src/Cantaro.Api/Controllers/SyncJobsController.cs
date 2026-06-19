using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Controllers;

public sealed class MusicSyncJobResponse
{
    public Guid Id { get; init; }
    public required string Status { get; init; }
    public required string Service { get; init; }
    public int PlaylistCount { get; init; }
    public int SongCount { get; init; }
    public int ProcessedPlaylistCount { get; init; }
    public int ProcessedSongCount { get; init; }
    public int SuccessCount { get; init; }
    public int FailureCount { get; init; }
    public required IReadOnlyList<string> PlaylistNames { get; init; }
    public string? CurrentPlaylistName { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}

[ApiController]
[Route("api/sync/jobs")]
[Authorize]
public sealed class SyncJobsController(
    ApplicationDbContext dbContext,
    UserManager<User> userManager,
    IPlatformRegistry platformRegistry,
    MusicSyncThrottleService throttleService,
    IServiceScopeFactory serviceScopeFactory) : ControllerBase
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly UserManager<User> _userManager = userManager;
    private readonly IPlatformRegistry _platformRegistry = platformRegistry;
    private readonly MusicSyncThrottleService _throttleService = throttleService;
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;

    [HttpPost]
    public async Task<ActionResult<MusicSyncJobResponse>> Create(
        [FromBody] BatchSyncRequest request,
        CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserIdAsync();
        if (string.IsNullOrWhiteSpace(request.Service))
        {
            return BadRequest(new { error = "Service is required" });
        }

        var service = request.Service.ToLowerInvariant();
        if (!_platformRegistry.IsSupported(service))
        {
            return BadRequest(new { error = $"Service '{request.Service}' not supported yet" });
        }

        var activeJob = await _dbContext.MusicSyncJobs
            .AsNoTracking()
            .Where(item => item.UserId == userId
                && item.Service == service
                && (item.Status == MusicSyncJobStatuses.Queued || item.Status == MusicSyncJobStatuses.Running))
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (activeJob is not null)
        {
            return Conflict(new { error = "A sync is already running for this platform.", jobId = activeJob.Id });
        }

        var platform = _platformRegistry.GetRequired(service);
        var available = await platform.GetPlaylistsAsync(userId);
        var requestedIds = request.ServicePlaylistIds is { Count: > 0 }
            ? request.ServicePlaylistIds.Distinct(StringComparer.Ordinal).ToList()
            : available.Select(item => item.Id).ToList();
        var requestedIdSet = requestedIds.ToHashSet(StringComparer.Ordinal);
        var selected = available.Where(item => requestedIdSet.Contains(item.Id)).ToList();
        if (selected.Count != requestedIds.Count)
        {
            return BadRequest(new { error = "One or more selected playlists are unavailable." });
        }

        var existingMappings = await _dbContext.ServicePlaylistMappings
            .Where(mapping => mapping.Service == service
                && mapping.Playlist!.UserId == userId
                && requestedIdSet.Contains(mapping.ServicePlaylistId))
            .Select(mapping => new { mapping.ServicePlaylistId, mapping.PlaylistId })
            .ToListAsync(cancellationToken);
        var mappedPlaylistIds = existingMappings.Select(item => item.PlaylistId).Distinct().ToList();
        var existingCounts = mappedPlaylistIds.Count == 0
            ? []
            : await _dbContext.PlaylistEntries
                .Where(entry => mappedPlaylistIds.Contains(entry.PlaylistId))
                .GroupBy(entry => entry.PlaylistId)
                .Select(group => new { PlaylistId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(item => item.PlaylistId, item => item.Count, cancellationToken);
        var existingByServiceId = existingMappings.ToDictionary(
            item => item.ServicePlaylistId,
            item => existingCounts.TryGetValue(item.PlaylistId, out var count) ? count : 0);
        var estimatedNewSongs = selected.Sum(item => Math.Max(
            0,
            Math.Max(0, item.ItemCount) - (existingByServiceId.TryGetValue(item.Id, out var count) ? count : 0)));

        var throttle = _throttleService.GetStatus(userId, service, DateTimeOffset.UtcNow);
        if (throttle.Usage > 0 && estimatedNewSongs > throttle.Remaining)
        {
            return BadRequest(new
            {
                error = $"This sync would process approximately {estimatedNewSongs} new songs, but only {throttle.Remaining} songs remain in the current {MusicSyncThrottleService.SyncWindowMinutes}-minute window."
            });
        }

        var playlists = selected
            .Select(item => new MusicSyncJobPlaylist(item.Id, item.Title, Math.Max(0, item.ItemCount)))
            .ToList();
        var now = DateTimeOffset.UtcNow;
        var job = new MusicSyncJob
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Service = service,
            PlaylistsJson = JsonSerializer.Serialize(playlists),
            Status = MusicSyncJobStatuses.Queued,
            PlaylistCount = playlists.Count,
            SongCount = playlists.Sum(item => item.SongCount),
            EstimatedNewSongCount = estimatedNewSongs,
            CreatedAt = now,
            UpdatedAt = now
        };
        _dbContext.MusicSyncJobs.Add(job);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return AcceptedAtAction(nameof(Get), new { id = job.Id }, ToResponse(job));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MusicSyncJobResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserIdAsync();
        var job = await _dbContext.MusicSyncJobs
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id && item.UserId == userId, cancellationToken);
        return job is null ? NotFound() : Ok(ToResponse(job));
    }

    [HttpGet("{id:guid}/events")]
    public async Task<IResult> Events(Guid id, CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserIdAsync();
        var exists = await _dbContext.MusicSyncJobs
            .AsNoTracking()
            .AnyAsync(item => item.Id == id && item.UserId == userId, cancellationToken);
        return exists
            ? TypedResults.ServerSentEvents(StreamEvents(id, userId, cancellationToken))
            : TypedResults.NotFound();
    }

    private async IAsyncEnumerable<SseItem<MusicSyncJobResponse>> StreamEvents(
        Guid id,
        int userId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        DateTimeOffset? lastUpdate = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var job = await dbContext.MusicSyncJobs
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == id && item.UserId == userId, cancellationToken);
            if (job is null) yield break;

            if (lastUpdate != job.UpdatedAt)
            {
                lastUpdate = job.UpdatedAt;
                yield return new SseItem<MusicSyncJobResponse>(ToResponse(job));
            }

            if (MusicSyncJobStatuses.IsTerminal(job.Status)) yield break;
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
    }

    private async Task<int> GetCurrentUserIdAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        return user?.Id ?? throw new UnauthorizedAccessException("User not authenticated");
    }

    internal static MusicSyncJobResponse ToResponse(MusicSyncJob job)
    {
        var playlists = JsonSerializer.Deserialize<List<MusicSyncJobPlaylist>>(job.PlaylistsJson) ?? [];
        return new MusicSyncJobResponse
        {
            Id = job.Id,
            Status = job.Status,
            Service = job.Service,
            PlaylistCount = job.PlaylistCount,
            SongCount = job.SongCount,
            ProcessedPlaylistCount = job.ProcessedPlaylistCount,
            ProcessedSongCount = job.ProcessedSongCount,
            SuccessCount = job.SuccessCount,
            FailureCount = job.FailureCount,
            PlaylistNames = playlists.Select(item => item.Name).Take(3).ToList(),
            CurrentPlaylistName = job.CurrentPlaylistName,
            ErrorMessage = job.ErrorMessage,
            CreatedAt = job.CreatedAt,
            UpdatedAt = job.UpdatedAt,
            StartedAt = job.StartedAt,
            CompletedAt = job.CompletedAt
        };
    }
}
