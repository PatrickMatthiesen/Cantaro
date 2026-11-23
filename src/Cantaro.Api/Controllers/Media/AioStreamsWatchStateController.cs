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
            var imdbId = identity.Imdb?.Id ?? mappings.ImdbId;
            var usesImdbParent = payload.MetaId != kitsuParent;
            if (usesImdbParent)
            {
                var imdbMapping = AioStreamsAnimeEnricher.GetImdbEpisodeMapping(identity.Imdb);
                var matchesNumbering = imdbMapping is not null && payload.Season == imdbMapping.SeasonNumber
                    ? MapImdbEpisode(payload.Season, payload.Episode ?? kitsuNumber, identity.Imdb) == kitsuNumber
                    : matchesKitsuNumbering;
                if (payload.MetaId != imdbId || !matchesNumbering) return NotFound();
            }
            var route = new AnimeEpisodeRoute(kitsuNumber, mappings, imdbId, HasMixedImdbParent: usesImdbParent);
            episode = await FindEpisodeAsync(userId, route, cancellationToken);
        }
        else
        {
            (episode, var unavailable) = await FindImdbMediaAsync(userId, type, payload, cancellationToken);
            if (unavailable) return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        if (episode is null) return NotFound();

        var played = payload.Event == "played" || payload.Event == "stop" && payload.Played == true;
        var hash = ReadTokenHash()!;
        var result = episode.MissingAnimeEpisode is not null
            ? await MediaProviderSeasonMappingService.WithWriteAsync(db, WriteAsync, cancellationToken)
            : await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                var written = await WriteAsync();
                await transaction.CommitAsync(cancellationToken);
                return written;
            });

        if (!result.Authorized) return Unauthorized();
        if (result.ProgressChanged) eventHub.Publish(userId);
        return result.Accepted ? NoContent() : NotFound();

        async Task<(bool Accepted, bool ProgressChanged, bool Authorized)> WriteAsync()
        {
            // Rotation and disconnect update this row, so neither can finish before an authorized write commits.
            var pairing = await db.Set<AioStreamsPairing>().FromSqlInterpolated($"""
                SELECT * FROM "AioStreamsPairings" WHERE "UserId" = {userId} AND "TokenHash" = {hash} FOR UPDATE
                """).AsNoTracking().ToListAsync(cancellationToken);
            if (pairing.Count == 0) return (false, false, false);

            if ((episode.ImdbTvTitleId != null || episode.MissingAnimeEpisode != null)
                && await ReceiptMatchesAsync(userId, type, payload, cancellationToken) is { } existing)
                return (existing, false, true);

            var bound = episode;
            if (episode.ImdbTvTitleId is { } titleId)
            {
                // Catalog refreshes write the SIMKL link with the coordinates. Share locks also
                // serialize with link replacement or unlink while allowing foreign key checks.
                var lockedLinks = await db.Database.SqlQuery<Guid>($"""
                    SELECT "Id" AS "Value" FROM "MediaProviderLinks"
                    WHERE "MediaTitleId" = {titleId} AND "Provider" IN ('imdb', 'simkl')
                    ORDER BY "Id" FOR SHARE
                    """).ToArrayAsync(cancellationToken);
                if (lockedLinks.Length != 2
                    || await FindVerifiedImdbTvEpisodeIdAsync(userId, episode.EntryId, titleId, payload, cancellationToken)
                        != episode.Id)
                    return (false, false, true);
            }
            else if (episode.MissingAnimeEpisode is { } route)
            {
                // Recheck the binding under the mapping lock before creating an episode for this receipt.
                bound = await FindEpisodeAsync(userId, route, cancellationToken, materializeForEntry: episode.EntryId);
                if (bound is null) return (false, false, true);
            }

            // Serialize played events with library progress updates so contiguous progress cannot lose a write.
            if (played)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    SELECT "Id" FROM "MediaLibraryEntries" WHERE "Id" = {bound.EntryId} FOR NO KEY UPDATE
                    """, cancellationToken);
            }

            var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "AioStreamsWatchEvents"
                    ("MediaLibraryEntryId", "MediaEpisodeId", "EventId", "UserId",
                     "Type", "MetaId", "VideoId", "Season", "Episode", "At", "PositionMs", "DurationMs", "Played")
                VALUES ({bound.EntryId}, {bound.Id}, {payload.Id}, {userId},
                    {type}, {payload.MetaId}, {videoId}, {payload.Season}, {payload.Episode}, {payload.At},
                    {payload.PositionMs}, {payload.DurationMs}, {played})
                ON CONFLICT DO NOTHING
                """, cancellationToken);

            var accepted = inserted > 0;
            if (!accepted)
                accepted = await ReceiptMatchesAsync(userId, type, payload, cancellationToken)
                    ?? await AdoptLegacyReceiptAsync(db.Database.CurrentTransaction!, bound, userId, type, payload, cancellationToken);

            var progressChanged = inserted > 0 && played
                && (movie ? await CompleteMovieAsync(bound.EntryId, cancellationToken)
                    : await AdvanceProgressAsync(bound.EntryId, cancellationToken));
            return (accepted, progressChanged, true);
        }
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
                          where entry.UserId == userId
                              && watch.UserId == userId && watch.VideoId != null
                              && (episode.MediaTitleId == entry.MediaTitleId
                                  || watch.MediaEpisodeId == null && watch.Type == "movie")
                          group new { Watch = watch, EpisodeNumber = (int?)episode.EpisodeNumber }
                          by new { watch.MediaLibraryEntryId, watch.MediaEpisodeId, watch.VideoId,
                              ImdbTvTitleId = entry.MediaTitle!.MediaKind == MediaKinds.Series
                                  && watch.Type == "series" && watch.VideoId!.StartsWith("tt")
                                      ? (Guid?)entry.MediaTitleId : null,
                              EpisodeNumber = (int?)episode.EpisodeNumber, entry.ProgressEpisodes, entry.Status }
                          into events
                          select new
                          {
                              events.Key.MediaEpisodeId,
                              events.Key.MediaLibraryEntryId,
                              events.Key.VideoId,
                              events.Key.ImdbTvTitleId,
                              LibraryPlayed = events.Key.MediaEpisodeId == null
                                  ? events.Key.Status == MediaLibraryStatuses.Completed
                                  : events.Key.ProgressEpisodes >= events.Key.EpisodeNumber,
                              LastPlayedAt = events.Where(e => e.Watch.Played).Max(e => (long?)e.Watch.At),
                              Latest = events.Where(e => e.Watch.Played || e.Watch.PositionMs != null)
                                  .OrderByDescending(e => e.Watch.At).ThenByDescending(e => e.Watch.Played)
                                  .ThenByDescending(e => e.Watch.Id)
                                  .Select(e => new
                                  {
                                      Event = e.Watch,
                                      ExternallyCompleted = db.AioStreamsExternalCompletions.Any(completion =>
                                          completion.MediaLibraryEntryId == e.Watch.MediaLibraryEntryId
                                          && (e.Watch.MediaEpisodeId == null
                                              ? completion.FirstEpisode == null && completion.LastEpisode == null
                                              : completion.FirstEpisode <= e.EpisodeNumber
                                                  && e.EpisodeNumber <= completion.LastEpisode)
                                          // The wire timestamp has second precision. Receipt order cannot resolve a tie.
                                          && completion.At >= e.Watch.At)
                                  }).FirstOrDefault()
                          }).ToListAsync(cancellationToken);
        var tvTitleIds = rows.Select(row => row.ImdbTvTitleId).OfType<Guid>().Distinct().ToArray();
        Dictionary<Guid, string> tvRoutes = tvTitleIds.Length == 0 ? [] : await QueryVerifiedImdbTvRoutes(tvTitleIds)
            .ToDictionaryAsync(route => route.MediaEpisodeId,
                route => FormattableString.Invariant($"{route.ImdbId}:{route.Season}:{route.Episode}"), cancellationToken);
        var media = rows.ToLookup(row => (row.MediaLibraryEntryId, row.MediaEpisodeId));
        // A stale route can still have completed its canonical episode.
        var watched = media.Where(item => item.Any(row => row.LibraryPlayed || row.LastPlayedAt != null))
            .SelectMany(item => item.Where(row => IsCurrentRoute(row.ImdbTvTitleId, row.MediaEpisodeId, row.VideoId))
                .Select(row => row.VideoId!))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var items = media.Select(item => new
            {
                LastPlayedAt = item.Max(row => row.LastPlayedAt),
                Latest = item.Where(row => IsCurrentRoute(row.ImdbTvTitleId, row.MediaEpisodeId, row.VideoId))
                    .OrderByDescending(row => row.Latest?.Event.At).ThenByDescending(row => row.Latest?.Event.Played)
                    .ThenByDescending(row => row.Latest?.Event.Id).FirstOrDefault()?.Latest
            })
            .Where(item => item.Latest is { ExternallyCompleted: false, Event: { Played: false, PositionMs: > 0 } } latest
                && !(item.LastPlayedAt >= latest.Event.At))
            .Select(item => item.Latest!.Event)
            .OrderByDescending(e => e.At).ThenByDescending(e => e.Id).Take(50)
            .Select(e => new ResumeItem(e.Type!, e.MetaId!, e.VideoId!, e.Season, e.Episode,
                e.At, e.PositionMs!.Value, e.DurationMs)).ToArray();

        var version = AioStreamsWatchState.HashToken(string.Join('\n', watched));
        return since == version
            ? Ok(new { version, items })
            : Ok(new { version, items, watched = new {
                movies = watched.Where(id => !id.Contains(':')).ToArray(),
                episodes = watched.Where(id => id.Contains(':')).ToArray() } });

        bool IsCurrentRoute(Guid? tvTitleId, Guid? episodeId, string? videoId) => tvTitleId is null
            || (episodeId is { } id && tvRoutes.GetValueOrDefault(id) == videoId);
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

    private static int? MapImdbEpisode(int? season, int episode, AioStreamsAnimeEnricher.AioStreamsImdbMapping? imdb)
    {
        if (AioStreamsAnimeEnricher.GetImdbEpisodeMapping(imdb) is not { } mapping || season != mapping.SeasonNumber)
            return null;

        var mappedEpisode = episode - mapping.EpisodeOffset;
        return mappedEpisode > 0 && imdb?.NonImdbEpisodes?.Any(number => number <= mappedEpisode) != true
            ? mappedEpisode : null;
    }

    private static bool MatchesImdbRoute(string type, string videoId, PlaybackEvent payload)
    {
        if (payload.MetaId is not { Length: > 2 and <= 32 } id
            || !id.StartsWith("tt", StringComparison.Ordinal)
            || !id[2..].All(char.IsAsciiDigit)) return false;
        return type switch
        {
            "movie" => videoId == id && payload.Season is null && payload.Episode is null,
            "anime" or "series" => payload.Season is > 0 && payload.Episode is > 0
                && videoId == FormattableString.Invariant($"{id}:{payload.Season}:{payload.Episode}"),
            _ => false
        };
    }

    private async Task<(BoundEpisode? Episode, bool Unavailable)> FindImdbMediaAsync(int userId, string type, PlaybackEvent payload,
        CancellationToken cancellationToken)
    {
        var entries = from link in db.MediaProviderLinks
                      join entry in db.MediaLibraryEntries on link.MediaTitleId equals entry.MediaTitleId
                      where link.Provider == "imdb" && link.ExternalId == payload.MetaId && link.LastVerifiedAt != null
                          && entry.UserId == userId
                      select entry;
        var linked = await entries.Select(entry => new { entry.Id, entry.MediaTitleId, entry.MediaTitle!.MediaKind,
                entry.MediaTitle.Format })
            .SingleOrDefaultAsync(cancellationToken);
        if (linked is null) return (null, false);
        if (type == "movie")
        {
            var isMovie = linked.MediaKind == MediaKinds.Movie
                || linked is { MediaKind: MediaKinds.Anime, Format: MediaFormats.Movie };
            return (isMovie ? new BoundEpisode(linked.Id, null) : null, false);
        }
        if (linked.MediaKind == MediaKinds.Anime)
            return await FindImdbAnimeEpisodeAsync(userId, linked.Id, linked.MediaTitleId, payload, cancellationToken);
        if (type != "series" || linked.MediaKind != MediaKinds.Series) return (null, false);

        var episodeId = await FindVerifiedImdbTvEpisodeIdAsync(userId, linked.Id, linked.MediaTitleId, payload, cancellationToken);
        return (episodeId is { } id ? new BoundEpisode(linked.Id, id, ImdbTvTitleId: linked.MediaTitleId) : null, false);
    }

    private Task<Guid?> FindVerifiedImdbTvEpisodeIdAsync(int userId, Guid entryId, Guid titleId, PlaybackEvent payload,
        CancellationToken cancellationToken) =>
        QueryVerifiedImdbTvRoutes([titleId])
            .Where(route => route.ImdbId == payload.MetaId && route.Season == payload.Season && route.Episode == payload.Episode
                && db.MediaLibraryEntries.Any(entry => entry.Id == entryId && entry.UserId == userId
                    && entry.MediaTitleId == titleId))
            .Select(route => (Guid?)route.MediaEpisodeId).SingleOrDefaultAsync(cancellationToken);

    private IQueryable<ImdbTvRoute> QueryVerifiedImdbTvRoutes(Guid[] titleIds) =>
        from coordinates in MediaEpisodeIdentityService.QueryVerifiedSimklEpisodeCoordinates(db, titleIds)
        join link in db.MediaProviderLinks on coordinates.MediaTitleId equals link.MediaTitleId
        where link.Provider == "imdb" && link.LastVerifiedAt != null && link.MediaTitle!.MediaKind == MediaKinds.Series
        select new ImdbTvRoute
        {
            MediaEpisodeId = coordinates.MediaEpisodeId, ImdbId = link.ExternalId,
            Season = coordinates.SeasonNumber, Episode = coordinates.SeasonEpisodeNumber
        };

    private async Task<(BoundEpisode? Episode, bool Unavailable)> FindImdbAnimeEpisodeAsync(int userId,
        Guid entryId, Guid titleId, PlaybackEvent payload, CancellationToken cancellationToken)
    {
        var link = await db.MediaProviderLinks.Where(link => link.MediaTitleId == titleId && link.LastVerifiedAt != null
                && (link.Provider == "simkl" || link.Provider == "anilist" || link.Provider == "myanimelist"))
            .OrderBy(link => link.Provider != "simkl").ThenBy(link => link.Provider)
            .Select(link => new { link.Provider, link.ExternalId }).FirstOrDefaultAsync(cancellationToken);
        if (link is null) return (null, false);

        var (identity, unavailable) = await anime.LookupLinkedAnimeAsync(link.Provider, link.ExternalId, cancellationToken);
        if (identity?.Mappings is not { } mappings || identity.Type is "SPECIAL" or "MOVIE"
            || payload.MetaId != (identity.Imdb?.Id ?? mappings.ImdbId)
            || identity.Imdb?.NonImdbEpisodes is null
            || MapImdbEpisode(payload.Season, payload.Episode!.Value, identity.Imdb) is not { } number)
            return (null, unavailable);

        var route = new AnimeEpisodeRoute(number, mappings, payload.MetaId, entryId);
        return (await FindEpisodeAsync(userId, route, cancellationToken), false);
    }

    private async Task<bool> CompleteMovieAsync(Guid entryId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        return await db.MediaLibraryEntries.Where(entry => entry.Id == entryId && entry.Status != MediaLibraryStatuses.Completed)
            .ExecuteUpdateAsync(setters => setters.SetProperty(entry => entry.Status, MediaLibraryStatuses.Completed)
                .SetProperty(entry => entry.LastLocalEditAt, now).SetProperty(entry => entry.UpdatedAt, now)
                .SetProperty(entry => entry.LastMutationSource, MediaMutationSources.AioStreamsWatchState), cancellationToken) > 0;
    }

    private async Task<BoundEpisode?> FindEpisodeAsync(int userId, AnimeEpisodeRoute route,
        CancellationToken cancellationToken, Guid? materializeForEntry = null)
    {
        var (number, identity, imdb, imdbEntryId, hasMixedImdbParent) = route;
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

        if (hasMixedImdbParent && await db.MediaProviderLinks.AnyAsync(link =>
                link.Provider == "imdb" && link.ExternalId == imdb && link.LastVerifiedAt != null
                && link.MediaTitleId != titleId, cancellationToken))
            return null;

        // Local links for the same providers must agree with the anime entry.
        if (await db.MediaProviderLinks.AnyAsync(link => link.MediaTitleId == titleId
            && ((simkl != null && link.Provider == "simkl" && link.ExternalId != simkl)
                || (anilist != null && link.Provider == "anilist" && link.ExternalId != anilist)
                || (mal != null && link.Provider == "myanimelist" && link.ExternalId != mal)
                || (imdb != null && link.Provider == "imdb" && link.ExternalId != imdb)), cancellationToken))
            return null;

        var entry = await db.MediaLibraryEntries
            .Where(entry => entry.MediaTitleId == titleId && entry.UserId == userId
                && entry.MediaTitle!.MediaKind == MediaKinds.Anime
                && entry.MediaTitle.PrimaryProgressDimension == MediaProgressDimensions.Episode
                && entry.MediaTitle.Format != MediaFormats.Movie && entry.MediaTitle.Format != MediaFormats.Special
                && (imdbEntryId == null || (entry.Id == imdbEntryId && entry.MediaTitle.ProviderLinks.Any(link =>
                    link.Provider == "imdb" && link.ExternalId == imdb && link.LastVerifiedAt != null))))
            .Select(entry => new { entry.Id, EpisodeId = entry.MediaTitle!.Episodes
                .Where(episode => episode.EpisodeNumber == number).Select(episode => (Guid?)episode.Id).FirstOrDefault() })
            .SingleOrDefaultAsync(cancellationToken);
        if (entry is null || (materializeForEntry is { } expected && entry.Id != expected)) return null;
        if (entry.EpisodeId is { } episodeId) return new BoundEpisode(entry.Id, episodeId);
        if (materializeForEntry is null) return new BoundEpisode(entry.Id, null, route);

        var mapped = await MediaProviderSeasonMappingService.GetMappedEpisodesAsync(db, [titleId], cancellationToken);
        if (!mapped.Any(episode => episode.EpisodeNumber == number)) return null;

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "MediaEpisodes" ("Id", "MediaTitleId", "EpisodeNumber")
            VALUES ({Guid.NewGuid()}, {titleId}, {number})
            ON CONFLICT ("MediaTitleId", "EpisodeNumber") DO NOTHING
            """, cancellationToken);
        var canonicalId = await db.MediaEpisodes.Where(episode => episode.MediaTitleId == titleId
                && episode.EpisodeNumber == number)
            .Select(episode => episode.Id).SingleAsync(cancellationToken);
        return new BoundEpisode(entry.Id, canonicalId);
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

    private sealed record AnimeEpisodeRoute(int Number, AioStreamsAnimeEnricher.AioStreamsMappings Identity,
        string? Imdb, Guid? ImdbEntryId = null, bool HasMixedImdbParent = false);

    private sealed record BoundEpisode(Guid EntryId, Guid? Id, AnimeEpisodeRoute? MissingAnimeEpisode = null,
        Guid? ImdbTvTitleId = null);

    private sealed class ImdbTvRoute
    {
        public Guid MediaEpisodeId { get; init; }
        public required string ImdbId { get; init; }
        public int Season { get; init; }
        public int Episode { get; init; }
    }

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
