using System.Globalization;
using System.Text.Json.Serialization;
using Cantaro.Api.Configuration;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Cantaro.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route(AioStreamsWatchState.Route)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AioStreamsWatchStateController(
    ApplicationDbContext db,
    AioStreamsAnimeEnricher anime,
    MediaLibraryEventHub eventHub,
    TimeProvider clock) : ControllerBase
{
    private static readonly string[] Types = ["anime", "series", "movie"];

    [HttpGet("manifest.json")]
    [EnableCors(AioStreamsWatchState.ManifestCorsPolicy)]
    public async Task<IActionResult> Manifest(CancellationToken cancellationToken)
    {
        if (Request.Query.ContainsKey("viewer")) return NotFound();
        if (await ResolveUserAsync(cancellationToken) is not { } userId) return Unauthorized();

        return Ok(new
        {
            id = "com.cantaro.tracker",
            version = "1.0.0",
            name = "Cantaro",
            description = "Sync resume positions and watched history with your Cantaro account.",
            logo = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/icon-192.png",
            types = Types,
            resources = new[] { new { name = "watch_state", types = Types, idPrefixes = new[] { "kitsu:", "tt" } } },
            watchState = new
            {
                version = 2,
                push = new { events = new[] { "pause", "stop", "played" } },
                pull = new { items = true, watched = true, ttlSeconds = 30 }
            }
        });
    }

    [HttpPost("watch_state/push/{type}/{videoId}.json")]
    [RequestSizeLimit(16_384)]
    public async Task<IActionResult> Push(string type, string videoId, PlaybackEvent payload, CancellationToken cancellationToken)
    {
        if (Request.Query.ContainsKey("viewer")) return NotFound();
        if (await ResolveUserAsync(cancellationToken) is not { } userId) return Unauthorized();
        if (payload.VideoId != videoId) return NotFound();
        var movie = type == "movie";
        var kitsu = TryParseKitsuEpisode(videoId, out var kitsuId, out var kitsuNumber);
        if (kitsu ? type is not ("anime" or "series")
            : !MatchesImdbRoute(type, videoId, payload)) return NotFound();

        if ((payload.Scope is not null && payload.Scope != (movie ? "movie" : "episode"))
            || payload.Event is not ("pause" or "stop" or "played")
            || string.IsNullOrWhiteSpace(payload.Id) || payload.Id.Length > 512 || payload.Id.Contains('\0')
            || payload.MetaId is not { Length: > 0 and <= 256 } || payload.MetaId.Contains('\0')
            || payload.Season is <= 0 || payload.Episode is <= 0
            || payload.At <= 0 || payload.PositionMs is < 0 || payload.DurationMs is <= 0
            || payload.PositionMs > payload.DurationMs) return BadRequest();

        var kitsuParent = kitsu ? FormattableString.Invariant($"kitsu:{kitsuId}") : null;
        var matchesKitsuNumbering = payload.Season is null or 1 && (payload.Episode ?? kitsuNumber) == kitsuNumber;
        if (kitsu && payload.MetaId == kitsuParent && !matchesKitsuNumbering) return NotFound();

        // An acknowledged retry can be delivered while the metadata API is offline.
        if (await ReceiptMatchesAsync(userId, type, payload, cancellationToken) is { } matches)
            return matches ? NoContent() : NotFound();

        BoundEpisode? episode;
        if (kitsu)
        {
            var (identity, unavailable) = await anime.LookupKitsuAsync(kitsuId, cancellationToken);
            if (unavailable) return StatusCode(StatusCodes.Status503ServiceUnavailable);
            if (identity?.Mappings is not { } mappings || identity.Type is "SPECIAL" or "MOVIE") return NotFound();
            if (payload.MetaId != kitsuParent
                && (payload.MetaId != (identity.Imdb?.Id ?? mappings.ImdbId)
                    || !matchesKitsuNumbering && !MatchesImdbNumbering(payload, kitsuNumber, identity.Imdb))) return NotFound();
            episode = await FindEpisodeAsync(userId, kitsuNumber, mappings, cancellationToken);
        }
        else
        {
            episode = await FindImdbMediaAsync(userId, type, payload, cancellationToken);
        }
        if (episode is null) return NotFound();

        var played = payload.Event == "played" || payload.Event == "stop" && payload.Played == true;
        var hash = ReadTokenHash()!;
        var (accepted, progressChanged, authorized) = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            // Rotation and disconnect update this row, so neither can finish before an authorized write commits.
            var pairing = await db.Set<AioStreamsPairing>().FromSqlInterpolated($"""
                SELECT * FROM "AioStreamsPairings" WHERE "UserId" = {userId} AND "TokenHash" = {hash} FOR UPDATE
                """).AsNoTracking().ToListAsync(cancellationToken);
            if (pairing.Count == 0) return (false, false, false);
            // Serialize played events for a title so a simultaneous completion cannot leave a gap.
            if (played)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    SELECT "Id" FROM "MediaLibraryEntries" WHERE "Id" = {episode.EntryId} FOR NO KEY UPDATE
                    """, cancellationToken);
            }

            var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "AioStreamsWatchEvents"
                    ("MediaLibraryEntryId", "MediaEpisodeId", "EventId", "UserId",
                     "Type", "MetaId", "VideoId", "Season", "Episode", "At", "PositionMs", "DurationMs", "Played")
                VALUES ({episode.EntryId}, {episode.Id}, {payload.Id}, {userId},
                    {type}, {payload.MetaId}, {videoId}, {payload.Season}, {payload.Episode}, {payload.At},
                    {payload.PositionMs}, {payload.DurationMs}, {played})
                ON CONFLICT DO NOTHING
                """, cancellationToken);

            var accepted = inserted > 0;
            if (!accepted)
                accepted = await ReceiptMatchesAsync(userId, type, payload, cancellationToken)
                    ?? await AdoptLegacyReceiptAsync(transaction, episode, userId, type, payload, cancellationToken);

            var progressChanged = inserted > 0 && played
                && (movie ? await CompleteMovieAsync(episode.EntryId, cancellationToken)
                    : await AdvanceProgressAsync(episode.EntryId, cancellationToken));
            await transaction.CommitAsync(cancellationToken);
            return (accepted, progressChanged, true);
        });

        if (!authorized) return Unauthorized();
        if (progressChanged) eventHub.Publish(userId);
        return accepted ? NoContent() : NotFound();
    }

    [HttpGet("watch_state/pull.json")]
    public async Task<IActionResult> Pull([FromQuery] string? since, CancellationToken cancellationToken)
    {
        if (Request.Query.ContainsKey("viewer")) return NotFound();
        if (await ResolveUserAsync(cancellationToken) is not { } userId) return Unauthorized();
        var rows = await (from watch in db.AioStreamsWatchEvents.AsNoTracking()
                          join entry in db.MediaLibraryEntries on watch.MediaLibraryEntryId equals entry.Id
                          join episode in db.MediaEpisodes on watch.MediaEpisodeId equals (Guid?)episode.Id into episodeRows
                          from episode in episodeRows.DefaultIfEmpty()
                          where watch.UserId == userId && entry.UserId == userId
                              && watch.VideoId != null && (episode.MediaTitleId == entry.MediaTitleId
                                  || watch.MediaEpisodeId == null && watch.Type == "movie")
                          group watch by new { watch.MediaLibraryEntryId, watch.MediaEpisodeId, watch.VideoId,
                              EpisodeNumber = (int?)episode.EpisodeNumber, entry.ProgressEpisodes, entry.Status }
                          into events
                          select new
                          {
                              events.Key.MediaEpisodeId,
                              events.Key.MediaLibraryEntryId,
                              VideoId = events.Key.VideoId!,
                              Played = (events.Key.MediaEpisodeId == null
                                  ? events.Key.Status == MediaLibraryStatuses.Completed
                                  : events.Key.ProgressEpisodes >= events.Key.EpisodeNumber) || events.Any(e => e.Played),
                              Latest = events.Where(e => e.Played || e.PositionMs != null)
                                  .OrderByDescending(e => e.At).ThenByDescending(e => e.Id).FirstOrDefault()
                          }).ToListAsync(cancellationToken);
        var media = rows.ToLookup(row => (row.MediaLibraryEntryId, row.MediaEpisodeId));
        var watched = media.Where(item => item.Any(row => row.Played))
            .SelectMany(item => item.Select(row => row.VideoId))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var items = media.Select(item => item.Select(row => row.Latest).OfType<AioStreamsWatchEvent>()
                .OrderByDescending(e => e.At).ThenByDescending(e => e.Id).FirstOrDefault())
            .OfType<AioStreamsWatchEvent>().Where(e => !e.Played && e.PositionMs > 0)
            .OrderByDescending(e => e.At).ThenByDescending(e => e.Id).Take(50)
            .Select(e => new ResumeItem(e.Type!, e.MetaId!, e.VideoId!, e.Season, e.Episode,
                e.At, e.PositionMs!.Value, e.DurationMs)).ToArray();

        var version = AioStreamsWatchState.HashToken(string.Join('\n', watched));
        return since == version
            ? Ok(new { version, items })
            : Ok(new { version, items, watched = new {
                movies = watched.Where(id => !id.Contains(':')).ToArray(),
                episodes = watched.Where(id => id.Contains(':')).ToArray() } });
    }

    private Task<int?> ResolveUserAsync(CancellationToken cancellationToken)
    {
        var hash = ReadTokenHash();
        return hash is null ? Task.FromResult<int?>(null) : db.Set<AioStreamsPairing>().Where(p => p.TokenHash == hash)
            .Select(p => (int?)p.UserId).SingleOrDefaultAsync(cancellationToken);
    }

    private string? ReadTokenHash()
    {
        var token = Request.Query["token"];
        return token.Count == 1 && token[0] is { Length: 43 } value ? AioStreamsWatchState.HashToken(value) : null;
    }

    private Task<bool?> ReceiptMatchesAsync(int userId, string type, PlaybackEvent payload,
        CancellationToken cancellationToken)
        => db.AioStreamsWatchEvents.Where(e => e.UserId == userId && e.EventId == payload.Id)
            .Select(e => (bool?)(e.Type == type && e.MetaId == payload.MetaId && e.VideoId == payload.VideoId
                && e.Season == payload.Season && e.Episode == payload.Episode))
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<bool> AdoptLegacyReceiptAsync(IDbContextTransaction transaction, BoundEpisode episode,
        int userId, string type, PlaybackEvent payload, CancellationToken cancellationToken)
    {
        // A replay supplies routing for a legacy receipt without reapplying its progress.
        const string savepoint = "before_watch_receipt_upgrade";
        await transaction.CreateSavepointAsync(savepoint, cancellationToken);
        try
        {
            var updated = await db.AioStreamsWatchEvents.Where(e => e.MediaLibraryEntryId == episode.EntryId
                    && e.MediaEpisodeId == episode.Id && e.EventId == payload.Id && e.UserId == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(e => e.UserId, userId).SetProperty(e => e.Type, type)
                    .SetProperty(e => e.MetaId, payload.MetaId).SetProperty(e => e.VideoId, payload.VideoId)
                    .SetProperty(e => e.Season, payload.Season).SetProperty(e => e.Episode, payload.Episode),
                    cancellationToken);
            if (updated > 0) return true;
        }
        catch (PostgresException exception) when (exception is
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_AioStreamsWatchEvents_UserId_EventId" })
        {
            await transaction.RollbackToSavepointAsync(savepoint, cancellationToken);
        }

        return await ReceiptMatchesAsync(userId, type, payload, cancellationToken) == true;
    }

    private static bool MatchesImdbNumbering(PlaybackEvent payload, int kitsuNumber,
        AioStreamsAnimeEnricher.AioStreamsImdbMapping? imdb)
        => AioStreamsAnimeEnricher.GetImdbEpisodeMapping(imdb) is { } mapping
            && (payload.Season is null || payload.Season == mapping.SeasonNumber)
            && (payload.Episode ?? kitsuNumber) == (long)kitsuNumber + mapping.EpisodeOffset;

    private static bool MatchesImdbRoute(string type, string videoId, PlaybackEvent payload)
    {
        if (payload.MetaId is not { Length: > 2 and <= 32 } id
            || !id.StartsWith("tt", StringComparison.Ordinal)
            || !id[2..].All(char.IsAsciiDigit)) return false;
        return type switch
        {
            "movie" => videoId == id && payload.Season is null && payload.Episode is null,
            "series" => payload.Season is > 0 && payload.Episode is > 0
                && videoId == FormattableString.Invariant($"{id}:{payload.Season}:{payload.Episode}"),
            _ => false
        };
    }

    private async Task<BoundEpisode?> FindImdbMediaAsync(int userId, string type, PlaybackEvent payload,
        CancellationToken cancellationToken)
    {
        var kind = type == "movie" ? MediaKinds.Movie : MediaKinds.Series;
        var entries = from link in db.MediaProviderLinks
                      join entry in db.MediaLibraryEntries on link.MediaTitleId equals entry.MediaTitleId
                      where link.Provider == "imdb" && link.ExternalId == payload.MetaId && link.LastVerifiedAt != null
                          && entry.UserId == userId && entry.MediaTitle!.MediaKind == kind
                      select entry;
        if (type == "movie") return await entries.Select(entry => new BoundEpisode(entry.Id, null))
            .SingleOrDefaultAsync(cancellationToken);

        var matches = await (from entry in entries
                             join episode in db.MediaEpisodes on entry.MediaTitleId equals episode.MediaTitleId
                             from content in episode.ProviderContents
                             where content.Provider == "simkl" && content.ProviderSeasonNumber == payload.Season
                                 && content.ProviderEpisodeNumber == payload.Episode
                                 && entry.MediaTitle!.ProviderLinks.Any(link => link.Provider == "simkl"
                                     && link.LastVerifiedAt != null && link.ExternalId == content.ProviderSeriesId)
                             select new BoundEpisode(entry.Id, episode.Id)).Distinct().Take(2).ToListAsync(cancellationToken);
        return matches.Count == 1 ? matches[0] : null;
    }

    private async Task<bool> CompleteMovieAsync(Guid entryId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        return await db.MediaLibraryEntries.Where(entry => entry.Id == entryId && entry.Status != MediaLibraryStatuses.Completed)
            .ExecuteUpdateAsync(setters => setters.SetProperty(entry => entry.Status, MediaLibraryStatuses.Completed)
                .SetProperty(entry => entry.LastLocalEditAt, now).SetProperty(entry => entry.UpdatedAt, now)
                .SetProperty(entry => entry.LastMutationSource, MediaMutationSources.AioStreamsWatchState), cancellationToken) > 0;
    }

    private async Task<BoundEpisode?> FindEpisodeAsync(int userId, int number,
        AioStreamsAnimeEnricher.AioStreamsMappings identity, CancellationToken cancellationToken)
    {
        var simkl = identity.SimklId is > 0 ? $"anime:{identity.SimklId.Value}" : null;
        var anilist = identity.AniListId is > 0 ? identity.AniListId.Value.ToString(CultureInfo.InvariantCulture) : null;
        var mal = identity.MalId is > 0 ? $"anime:{identity.MalId.Value}" : null;
        var matches = db.MediaProviderLinks.Where(link =>
            (simkl != null && link.Provider == "simkl" && link.ExternalId == simkl)
            || (anilist != null && link.Provider == "anilist" && link.ExternalId == anilist)
            || (mal != null && link.Provider == "myanimelist" && link.ExternalId == mal));
        var titleIds = await matches.Select(link => link.MediaTitleId).Distinct().Take(2).ToListAsync(cancellationToken);
        if (titleIds.Count != 1 || !await matches.AnyAsync(link => link.LastVerifiedAt != null, cancellationToken)) return null;
        var titleId = titleIds[0];

        // Local links for the same providers must agree with the anime entry.
        if (await db.MediaProviderLinks.AnyAsync(link => link.MediaTitleId == titleId
            && ((simkl != null && link.Provider == "simkl" && link.ExternalId != simkl)
                || (anilist != null && link.Provider == "anilist" && link.ExternalId != anilist)
                || (mal != null && link.Provider == "myanimelist" && link.ExternalId != mal)), cancellationToken))
            return null;

        return await (from episode in db.MediaEpisodes
                      join entry in db.MediaLibraryEntries on episode.MediaTitleId equals entry.MediaTitleId
                      where episode.MediaTitleId == titleId && episode.EpisodeNumber == number
                          && entry.UserId == userId && episode.MediaTitle!.MediaKind == MediaKinds.Anime
                          && episode.MediaTitle.PrimaryProgressDimension == MediaProgressDimensions.Episode
                          && episode.MediaTitle.Format != MediaFormats.Movie && episode.MediaTitle.Format != MediaFormats.Special
                      select new BoundEpisode(entry.Id, episode.Id)).SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<bool> AdvanceProgressAsync(Guid entryId, CancellationToken cancellationToken)
    {
        var current = await db.MediaLibraryEntries.Where(entry => entry.Id == entryId)
            .Select(entry => entry.ProgressEpisodes ?? 0).SingleAsync(cancellationToken);
        var completed = await (from watch in db.AioStreamsWatchEvents
                               join episode in db.MediaEpisodes on watch.MediaEpisodeId equals episode.Id
                               where watch.MediaLibraryEntryId == entryId && watch.Played && episode.EpisodeNumber > current
                               select episode.EpisodeNumber).Distinct().Order().ToListAsync(cancellationToken);
        var next = current;
        foreach (var number in completed)
        {
            if (number != next + 1) break;
            next = number;
        }

        if (next == current) return false;
        var now = clock.GetUtcNow();
        return await db.MediaLibraryEntries.Where(entry => entry.Id == entryId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(entry => entry.ProgressEpisodes, next)
                .SetProperty(entry => entry.LastLocalEditAt, now)
                .SetProperty(entry => entry.UpdatedAt, now)
                .SetProperty(entry => entry.LastMutationSource, MediaMutationSources.AioStreamsWatchState),
                cancellationToken) > 0;
    }

    private static bool TryParseKitsuEpisode(string videoId, out int entry, out int episode)
    {
        entry = episode = 0;
        var parts = videoId.Split(':');
        return parts is ["kitsu", var title, var number]
            && int.TryParse(title, NumberStyles.None, CultureInfo.InvariantCulture, out entry) && entry > 0
            && int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out episode) && episode > 0
            && videoId == FormattableString.Invariant($"kitsu:{entry}:{episode}");
    }

    private sealed record BoundEpisode(Guid EntryId, Guid? Id);

    private sealed record ResumeItem(
        string Type, string MetaId, string VideoId,
        // AIOStreams falls back to the video ID for omitted coordinates.
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Season,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Episode,
        long At, long PositionMs,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? DurationMs)
    {
        public bool Played => false;
    }

    public sealed record PlaybackEvent(
        string? Id, string? Event, string? Scope, long At, string? MetaId, string? VideoId,
        int? Season, int? Episode, long? PositionMs, long? DurationMs, bool? Played);
}
