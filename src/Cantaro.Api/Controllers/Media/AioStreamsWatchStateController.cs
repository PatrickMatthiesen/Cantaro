using System.Text.Json.Serialization;
using Cantaro.Api.Configuration;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cantaro.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route(AioStreamsWatchStateOptions.Route)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AioStreamsWatchStateController(
    ApplicationDbContext db,
    IOptionsMonitor<AioStreamsWatchStateOptions> options,
    MediaLibraryEventHub eventHub,
    TimeProvider clock) : ControllerBase
{
    [HttpGet("manifest.json")]
    public async Task<IActionResult> Manifest(CancellationToken cancellationToken)
    {
        var binding = options.CurrentValue;
        if (CheckAccess(binding) is { } error) return error;
        if (await FindEpisodeAsync(binding, cancellationToken) is null) return NotFound();

        return Ok(new
        {
            id = "com.cantaro.tracker",
            version = "1.0.0",
            name = "Cantaro",
            types = new[] { "series" },
            resources = new[] { new { name = "watch_state", types = new[] { "series" }, idPrefixes = new[] { binding.VideoId } } },
            watchState = new
            {
                version = 2,
                push = new { events = new[] { "pause", "stop", "played" } },
                pull = new { items = true, watched = true, ttlSeconds = 30 }
            }
        });
    }

    [HttpPost("watch_state/push/series/{videoId}.json")]
    [RequestSizeLimit(16_384)]
    public async Task<IActionResult> Push(string videoId, PlaybackEvent payload, CancellationToken cancellationToken)
    {
        var binding = options.CurrentValue;
        if (CheckAccess(binding) is { } error) return error;
        if (videoId != binding.VideoId || payload.VideoId != videoId || payload.MetaId != binding.MetaId
            || payload.Season != binding.Season || payload.Episode != binding.Episode) return NotFound();

        if (payload.Scope is not (null or "episode") || payload.Event is not ("pause" or "stop" or "played")
            || string.IsNullOrWhiteSpace(payload.Id) || payload.Id.Length > 512 || payload.Id.Contains('\0')
            || payload.At <= 0 || payload.PositionMs is < 0 || payload.DurationMs is <= 0
            || payload.PositionMs > payload.DurationMs) return BadRequest();

        var episode = await FindEpisodeAsync(binding, cancellationToken);
        if (episode is null) return NotFound();

        var played = payload.Event == "played" || payload.Event == "stop" && payload.Played == true;
        var progressChanged = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "AioStreamsWatchEvents"
                    ("MediaLibraryEntryId", "MediaEpisodeId", "EventId", "At", "PositionMs", "DurationMs", "Played")
                VALUES ({episode.EntryId}, {binding.MediaEpisodeId}, {payload.Id}, {payload.At},
                    {payload.PositionMs}, {payload.DurationMs}, {played})
                ON CONFLICT ("MediaLibraryEntryId", "MediaEpisodeId", "EventId") DO NOTHING
                """, cancellationToken);

            var updated = 0;
            if (inserted > 0 && played)
            {
                var now = clock.GetUtcNow();
                // A single played episode cannot fill gaps in Cantaro's consecutive episode count.
                updated = await db.MediaLibraryEntries
                    .Where(entry => entry.Id == episode.EntryId && (entry.ProgressEpisodes ?? 0) == episode.Number - 1)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(entry => entry.ProgressEpisodes, episode.Number)
                        .SetProperty(entry => entry.LastLocalEditAt, now)
                        .SetProperty(entry => entry.UpdatedAt, now)
                        .SetProperty(entry => entry.LastMutationSource, MediaMutationSources.AioStreamsWatchState),
                        cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return updated > 0;
        });

        if (progressChanged) eventHub.Publish(binding.UserId);
        return NoContent();
    }

    [HttpGet("watch_state/pull.json")]
    public async Task<IActionResult> Pull([FromQuery] string? since, CancellationToken cancellationToken)
    {
        var binding = options.CurrentValue;
        if (CheckAccess(binding) is { } error) return error;
        var episode = await FindEpisodeAsync(binding, cancellationToken);
        if (episode is null) return NotFound();

        var events = db.AioStreamsWatchEvents.Where(e =>
            e.MediaLibraryEntryId == episode.EntryId && e.MediaEpisodeId == binding.MediaEpisodeId);
        var played = episode.WatchedThrough >= episode.Number || await events.AnyAsync(e => e.Played, cancellationToken);
        var latest = played ? null : await events
            .Where(e => e.PositionMs != null)
            .OrderByDescending(e => e.At).ThenByDescending(e => e.Id)
            .FirstOrDefaultAsync(cancellationToken);
        ResumeItem[] items = latest is { PositionMs: > 0 } ?
            [new(binding.MetaId!, binding.VideoId!, binding.Season, binding.Episode,
                latest.At, latest.PositionMs.Value, latest.DurationMs)] : [];

        var version = AioStreamsWatchStateOptions.HashToken($"{binding.MediaEpisodeId}:{binding.VideoId}:{played}");
        return since == version
            ? Ok(new { version, items })
            : Ok(new { version, items, watched = new { movies = Array.Empty<string>(),
                episodes = played ? new[] { binding.VideoId } : [] } });
    }

    private IActionResult? CheckAccess(AioStreamsWatchStateOptions binding)
    {
        if (!binding.IsConfigured) return NotFound();
        var token = Request.Query["token"];
        if (token.Count != 1 || !binding.AcceptsToken(token[0])) return Unauthorized();
        return Request.Query.ContainsKey("viewer") ? NotFound() : null;
    }

    private Task<BoundEpisode?> FindEpisodeAsync(AioStreamsWatchStateOptions binding, CancellationToken cancellationToken)
        => (from episode in db.MediaEpisodes
            join entry in db.MediaLibraryEntries on episode.MediaTitleId equals entry.MediaTitleId
            where episode.Id == binding.MediaEpisodeId && entry.UserId == binding.UserId
                && episode.MediaTitle!.PrimaryProgressDimension == MediaProgressDimensions.Episode
            select new BoundEpisode(entry.Id, episode.EpisodeNumber, entry.ProgressEpisodes))
            .SingleOrDefaultAsync(cancellationToken);

    private sealed record BoundEpisode(Guid EntryId, int Number, int? WatchedThrough);

    private sealed record ResumeItem(
        string MetaId, string VideoId, int? Season, int Episode, long At, long PositionMs,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? DurationMs)
    {
        public string Type => "series";
        public bool Played => false;
    }

    public sealed record PlaybackEvent(
        string? Id, string? Event, string? Scope, long At, string? MetaId, string? VideoId,
        int? Season, int? Episode, long? PositionMs, long? DurationMs, bool? Played);
}
