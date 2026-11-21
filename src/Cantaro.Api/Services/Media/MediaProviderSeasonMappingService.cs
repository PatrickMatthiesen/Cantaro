using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Cantaro.Api.Services;

public sealed class MediaProviderSeasonMappingService(
    ApplicationDbContext dbContext,
    ILogger<MediaProviderSeasonMappingService> logger)
{
    internal static bool IsSupported(string provider) => provider == MediaObservationSiteIdentifiers.Crunchyroll;

    internal static bool IsCompatibleTarget(MediaTitle title, bool numbered) =>
        MediaObservationMatchingService.IsStreamingCompatibleTitle(title)
        && (numbered
            ? title.SupportsEpisodeProgress && !title.IsCompletionOnly
                && string.Equals(title.PrimaryProgressDimension, MediaProgressDimensions.Episode, StringComparison.OrdinalIgnoreCase)
            : title.IsCompletionOnly);

    private static bool IsCompatibleSeasonTarget(MediaProviderSeasonMapping mapping) =>
        mapping.MediaTitle is { } title && IsCompatibleTarget(title, numbered: true);

    private static MediaProviderEpisodeMapping? GetCompatibleDirectMapping(MediaProviderEpisodeMapping? mapping) =>
        mapping?.MediaTitle is { } title && IsCompatibleTarget(title, mapping.CanonicalEpisodeNumber.HasValue)
            && (mapping.CanonicalEpisodeNumber is null
                || MediaObservationMatchingService.IsCanonicalEpisodeNumber(title, mapping.CanonicalEpisodeNumber))
            ? mapping : null;

    internal static string? NormalizeId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static string? GetSingleEpisodeId(MediaObservation observation) =>
        NormalizeId(observation.IsCatalogObservation
            ? observation.Episodes.Count == 1 ? observation.Episodes.Single().ProviderEpisodeId : null
            : observation.SiteMediaId);

    internal static string? SeasonKey(string? id, int? number) =>
        NormalizeId(id) is { } normalized ? $"id:{normalized}" :
        number is > 0 ? FormattableString.Invariant($"number:{number}") : null;

    internal static string? NormalizeSeasonTitle(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : string.Join(' ',
            value.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    internal Task<T> WithWriteAsync<T>(Func<Task<T>> write, CancellationToken cancellationToken) =>
        WithWriteAsync(dbContext, write, cancellationToken);

    internal static Task<T> WithWriteAsync<T>(
        ApplicationDbContext db, Func<Task<T>> write, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not null || !db.Database.IsRelational())
        {
            return write();
        }

        return db.Database.CreateExecutionStrategy().ExecuteAsync<T>(async _ =>
        {
            // PostgreSQL serializes mapping edits with the progress transactions that consume them.
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            if (db.Database.IsNpgsql())
            {
                await db.Database.ExecuteSqlRawAsync(
                    "SELECT pg_advisory_xact_lock(2174315046981611)", cancellationToken);
            }

            var result = await write();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }, cancellationToken);
    }

    public Task<MediaProviderSeasonMapping?> FindAsync(
        string provider, string? providerSeriesId, string? providerSeasonId,
        int? providerSeasonNumber, CancellationToken cancellationToken) =>
        FindAsync(provider, new ProviderEpisodeCoordinates(
            NormalizeId(providerSeriesId), NormalizeId(providerSeasonId), providerSeasonNumber, null, null),
            cancellationToken);

    internal Task<MediaProviderSeasonMapping?> FindAsync(
        string provider, ProviderEpisodeCoordinates coordinates,
        CancellationToken cancellationToken) => FindSeasonAsync(provider, coordinates, null, cancellationToken);

    internal Task<MediaProviderSeasonMapping?> ResolveSeasonAsync(
        string provider, ProviderEpisodeCoordinates coordinates, CancellationToken cancellationToken) =>
        FindSeasonAsync(provider, coordinates, row => CanonicalNumber(row, coordinates.EpisodeNumber).HasValue,
            cancellationToken);

    private async Task<MediaProviderSeasonMapping?> FindSeasonAsync(
        string provider, ProviderEpisodeCoordinates coordinates, Func<MediaProviderSeasonMapping, bool>? validEpisodes,
        CancellationToken cancellationToken)
    {
        if (coordinates.SeriesId is null)
        {
            return null;
        }

        var rows = await dbContext.MediaProviderSeasonMappings.AsNoTracking()
            .Include(mapping => mapping.MediaTitle)
            .Where(mapping => mapping.Provider == provider && mapping.ProviderSeriesId == coordinates.SeriesId)
            .ToListAsync(cancellationToken);
        var mapping = SelectSeason(rows, coordinates, allowSingleSeasonFallback: false, validEpisodes);
        if (mapping is not null || !CanUseSingleSeasonFallback(coordinates) || rows.Count == 0)
        {
            return mapping;
        }

        var knownSeasons = await dbContext.MediaProviderEpisodes.AsNoTracking()
            .Where(row => row.Provider == provider && row.ProviderSeriesId == coordinates.SeriesId
                && (row.ProviderSeasonId != null || row.ProviderSeasonNumber > 0))
            .Select(row => new ObservedSeason(row.ProviderSeasonId, row.ProviderSeasonNumber))
            .Distinct().ToListAsync(cancellationToken);
        return SelectSeason(rows, coordinates, allowSingleSeasonFallback: !HasMultipleObservedSeasons(knownSeasons),
            validEpisodes);
    }

    private sealed record ObservedSeason(string? Id, int? Number);

    private static bool HasMultipleObservedSeasons(IEnumerable<ObservedSeason> seasons) =>
        seasons.Select(row => row.Id).OfType<string>().Distinct().Skip(1).Any()
        || seasons.Select(row => row.Number).Where(number => number > 0).Distinct().Skip(1).Any();

    private static bool CanUseSingleSeasonFallback(ProviderEpisodeCoordinates coordinates) =>
        coordinates.SeasonId is null && coordinates.SeasonNumber is null;

    private static MediaProviderSeasonMapping? SelectSeason(
        IEnumerable<MediaProviderSeasonMapping> rows, ProviderEpisodeCoordinates coordinates,
        bool allowSingleSeasonFallback, Func<MediaProviderSeasonMapping, bool>? validEpisodes)
    {
        var compatible = rows.Where(IsCompatibleSeasonTarget).ToList();
        var applicable = compatible.Where(row =>
            ((row.ProviderEpisodeStart is null && row.ProviderEpisodeEnd is null)
                || (coordinates.EpisodeNumber is { } number
                    && number >= (row.ProviderEpisodeStart ?? 1)
                    && number <= (row.ProviderEpisodeEnd ?? int.MaxValue)))
            && (validEpisodes is null || validEpisodes(row))).ToList();

        var key = SeasonKey(coordinates.SeasonId, coordinates.SeasonNumber);
        var exact = applicable.Where(row => row.ProviderSeasonKey == key).ToList();
        var ordinal = applicable.Where(row => coordinates.SeasonNumber is > 0
            && row.ProviderSeasonNumber == coordinates.SeasonNumber
            && (coordinates.SeasonId is null || row.ProviderSeasonId is null
                || row.ProviderSeasonId == coordinates.SeasonId)).ToList();
        var eligible = applicable.Where(row => (coordinates.SeasonId is null || row.ProviderSeasonId == coordinates.SeasonId)
            && (coordinates.SeasonNumber is null || row.ProviderSeasonNumber is null
                || row.ProviderSeasonNumber == coordinates.SeasonNumber)
            && (coordinates.SeasonTitle is null || row.NormalizedSeasonTitle == coordinates.SeasonTitle)).ToList();
        var named = eligible.Where(row => coordinates.SeasonTitle is not null
            && row.NormalizedSeasonTitle == coordinates.SeasonTitle).ToList();

        static MediaProviderSeasonMapping? Unique(IEnumerable<MediaProviderSeasonMapping> candidates, string? source = null)
        {
            var rows = candidates.Where(row => source is null || row.MappingSource == source).Take(2).ToList();
            return rows.Count == 1 ? rows[0] : null;
        }

        return Unique(exact, MediaProviderSeasonMappingSources.Manual)
            ?? Unique(ordinal, MediaProviderSeasonMappingSources.Manual)
            ?? Unique(named, MediaProviderSeasonMappingSources.Manual)
            ?? Unique(exact)
            ?? Unique(ordinal)
            ?? Unique(named)
            ?? (allowSingleSeasonFallback && CanUseSingleSeasonFallback(coordinates)
                && !compatible.Select(row => row.ProviderSeasonKey).Distinct().Skip(1).Any() ? Unique(eligible) : null);
    }

    internal static ProviderEpisodeCoordinates GetObservedCoordinates(MediaObservation observation) =>
        new(NormalizeId(observation.ProviderSeriesId),
            NormalizeId(observation.ProviderSeasonId), observation.SeasonNumber,
            observation.EpisodeNumber ?? (observation.IsCatalogObservation
                ? observation.Episodes.Min(item => (int?)item.EpisodeNumber) : null),
            NormalizeSeasonTitle(observation.SeasonTitle));

    internal async Task<ProviderEpisodeCoordinates> GetCoordinatesAsync(
        MediaObservation observation, CancellationToken cancellationToken)
    {
        var observed = GetObservedCoordinates(observation);
        var provider = observation.SiteIdentifier;
        if (!IsSupported(provider))
        {
            return observed;
        }

        var episodeId = GetSingleEpisodeId(observation);
        var episode = await dbContext.MediaProviderEpisodes.AsNoTracking().FirstOrDefaultAsync(
            row => row.Provider == provider && row.ProviderEpisodeId == episodeId, cancellationToken);
        return await CompleteCoordinatesAsync(provider, episodeId, episode, observed, cancellationToken);
    }

    internal async Task<ProviderEpisodeCoordinates> CompleteCoordinatesAsync(
        string provider, string? episodeId, MediaProviderEpisode? episode, ProviderEpisodeCoordinates observed,
        CancellationToken cancellationToken)
    {
        var coordinates = episode is null ? observed : FillMissing(Coordinates(episode), observed);
        var contentKey = episode?.ContentKey ?? (episodeId is null ? null :
            MediaProviderVariantIdentities.Parse(provider, episodeId).ContentKey);
        if (contentKey is null || !NeedsSibling(coordinates))
        {
            return coordinates;
        }

        var siblings = await dbContext.MediaProviderEpisodes.AsNoTracking()
            .Where(row => row.Provider == provider && row.ContentKey == contentKey)
            .ToListAsync(cancellationToken);
        return CompleteFromSibling(coordinates, episodeId, siblings);
    }

    private static ProviderEpisodeCoordinates Coordinates(MediaProviderEpisode row) =>
        new(row.ProviderSeriesId, row.ProviderSeasonId, row.ProviderSeasonNumber, row.ProviderEpisodeNumber, row.NormalizedSeasonTitle);

    private static bool NeedsSibling(ProviderEpisodeCoordinates coordinates) =>
        coordinates.SeriesId is null || coordinates.SeasonId is null || coordinates.EpisodeNumber is null;

    private static ProviderEpisodeCoordinates FillMissing(ProviderEpisodeCoordinates primary, ProviderEpisodeCoordinates fallback)
    {
        if (Disagree(primary.SeriesId, fallback.SeriesId)
            || Disagree(primary.SeasonId, fallback.SeasonId)
            || Disagree(primary.SeasonNumber, fallback.SeasonNumber))
        {
            return primary;
        }

        return new(primary.SeriesId ?? fallback.SeriesId, primary.SeasonId ?? fallback.SeasonId,
            primary.SeasonNumber ?? fallback.SeasonNumber, primary.EpisodeNumber ?? fallback.EpisodeNumber,
            primary.SeasonTitle ?? fallback.SeasonTitle);

        static bool Disagree<T>(T left, T right) =>
            left is not null && right is not null && !EqualityComparer<T>.Default.Equals(left, right);
    }

    private static ProviderEpisodeCoordinates CompleteFromSibling(ProviderEpisodeCoordinates coordinates, string? episodeId,
        IEnumerable<MediaProviderEpisode> siblings)
    {
        var candidates = siblings.Where(row => row.ProviderEpisodeId != episodeId
                && (row.ProviderSeasonId != null || (row.ProviderSeriesId != null && row.ProviderSeasonNumber > 0))
                && (coordinates.SeriesId == null || row.ProviderSeriesId == coordinates.SeriesId)
                && (coordinates.SeasonId == null || row.ProviderSeasonId == null || row.ProviderSeasonId == coordinates.SeasonId)
                && (coordinates.SeasonNumber == null || row.ProviderSeasonNumber == null || row.ProviderSeasonNumber == coordinates.SeasonNumber)
                && (coordinates.EpisodeNumber == null || row.ProviderEpisodeNumber == null || row.ProviderEpisodeNumber == coordinates.EpisodeNumber))
            .GroupBy(row => (row.ProviderSeriesId, row.ProviderSeasonId, row.ProviderSeasonNumber))
            .Take(2).ToList();
        if (candidates.Count != 1)
        {
            return coordinates;
        }

        var candidate = candidates[0];
        return FillMissing(coordinates, new(candidate.Key.ProviderSeriesId, candidate.Key.ProviderSeasonId,
            candidate.Key.ProviderSeasonNumber, Consensus(candidate.Select(row => row.ProviderEpisodeNumber)),
            Consensus(candidate.Select(row => row.NormalizedSeasonTitle))));

        static T? Consensus<T>(IEnumerable<T?> values)
        {
            var distinct = values.Where(value => value is not null).Distinct().Take(2).ToList();
            return distinct.Count == 1 ? distinct[0] : default;
        }
    }

    internal async Task<ProviderEpisodeResolution?> ResolveAsync(
        MediaObservation observation, CancellationToken cancellationToken)
    {
        var provider = observation.SiteIdentifier;
        if (!IsSupported(provider))
        {
            return null;
        }

        var episodeId = GetSingleEpisodeId(observation);
        var direct = await dbContext.MediaProviderEpisodeMappings.AsNoTracking().Include(row => row.MediaTitle)
            .FirstOrDefaultAsync(row => row.Provider == provider && row.ProviderEpisodeId == episodeId,
                cancellationToken);
        direct = GetCompatibleDirectMapping(direct);
        if (direct?.MappingSource == MediaProviderSeasonMappingSources.Manual)
        {
            return DirectResolution(direct);
        }

        var coordinates = await GetCoordinatesAsync(observation, cancellationToken);
        var season = observation.IsCatalogObservation
            ? await FindSeasonAsync(provider, coordinates, row => observation.Episodes.Count > 0
                && observation.Episodes.All(episode => CanonicalNumber(row, episode.EpisodeNumber).HasValue), cancellationToken)
            : await ResolveSeasonAsync(provider, coordinates, cancellationToken);
        if (WinsOverSeason(direct, season is not null))
        {
            return DirectResolution(direct!);
        }

        if (season is null)
        {
            return null;
        }

        return new(season.MediaTitle!, observation.IsCatalogObservation ? null :
            CanonicalNumber(season, coordinates.EpisodeNumber), season.EpisodeOffset, season.MappingSource, false);
    }

    private static ProviderEpisodeResolution DirectResolution(MediaProviderEpisodeMapping direct) =>
        new(direct.MediaTitle!, direct.CanonicalEpisodeNumber, null, direct.MappingSource, true);

    private static bool WinsOverSeason(MediaProviderEpisodeMapping? direct, bool seasonHasValidEpisode) =>
        direct is not null && (!seasonHasValidEpisode || direct.MappingSource == MediaProviderSeasonMappingSources.Manual);

    internal static int? CanonicalNumber(MediaProviderSeasonMapping season, int? providerNumber)
    {
        if (providerNumber is null || providerNumber < (season.ProviderEpisodeStart ?? 1)
            || providerNumber > (season.ProviderEpisodeEnd ?? int.MaxValue))
        {
            return null;
        }

        var number = (long?)providerNumber + season.EpisodeOffset;
        var episodeCount = season.MediaTitle?.EpisodeCount;
        return number is > 0 and <= int.MaxValue
            && (episodeCount is not > 0 || number <= episodeCount)
            ? (int)number : null;
    }

    public async Task<MediaProviderSeasonMapping?> EstablishAsync(
        string provider, string? providerSeriesId, string? providerSeasonId, int? providerSeasonNumber,
        Guid mediaTitleId, int episodeOffset, string mappingSource, decimal confidence,
        CancellationToken cancellationToken, string? seasonTitle = null,
        int? episodeStart = null, int? episodeEnd = null, DateTimeOffset? observedAt = null)
    {
        ValidateSource(mappingSource);
        var automatic = mappingSource == MediaProviderSeasonMappingSources.Automatic;
        var evidenceAt = automatic ? observedAt?.ToUniversalTime() : null;
        provider = provider.Trim().ToLowerInvariant();
        var series = NormalizeId(providerSeriesId);
        var seasonId = NormalizeId(providerSeasonId);
        var key = SeasonKey(seasonId, providerSeasonNumber);
        if (!IsSupported(provider) || series is null || key is null
            || MediaDestinationUrlPolicy.BuildSeriesUrl(provider, series) is null)
        {
            return null;
        }

        if (episodeStart is <= 0 || episodeEnd is <= 0 || episodeEnd < episodeStart)
        {
            throw new ArgumentException("Episode bounds must be positive and ordered.");
        }

        return await WithWriteAsync<MediaProviderSeasonMapping?>(async () =>
        {
            var title = await GetValidTargetAsync(mediaTitleId, numbered: true, cancellationToken);
            if (mappingSource == MediaProviderSeasonMappingSources.Manual)
            {
                var maximum = title.EpisodeCount is > 0 ? title.EpisodeCount.Value : int.MaxValue;
                var canonicalStart = (long)(episodeStart ?? 1) + episodeOffset;
                var canonicalEnd = (long)(episodeEnd ?? int.MaxValue) + episodeOffset;
                if (canonicalEnd < 1 || canonicalStart > maximum
                    || (episodeStart is not null && canonicalStart < 1)
                    || (episodeEnd is not null && canonicalEnd > maximum))
                {
                    throw new ArgumentException("Episode bounds and offset must resolve within the title's episode range.");
                }
            }

            var rows = dbContext.MediaProviderSeasonMappings.Where(row =>
                row.Provider == provider && row.ProviderSeriesId == series);
            var (existing, narrowings, automaticOverlaps, hasOverlap) = await PlanSeasonWriteAsync(provider, series, key,
                seasonId, providerSeasonNumber, episodeStart, episodeEnd, automatic, evidenceAt, cancellationToken);

            if (automatic && existing is not null
                && (existing.MappingSource == MediaProviderSeasonMappingSources.Manual
                    || HasNewerEvidence(existing.LastObservedAt, evidenceAt)))
            {
                return null;
            }

            if (hasOverlap)
            {
                if (mappingSource == MediaProviderSeasonMappingSources.Manual)
                {
                    throw new ArgumentException("Episode bounds overlap an existing mapping for this season.");
                }

                logger.LogDebug("Skipped overlapping automatic mapping for {Provider}/{Series}/{Season}.", provider, series, key);
                return null;
            }

            foreach (var (id, end) in narrowings)
            {
                await rows.Where(row => row.Id == id).ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.ProviderEpisodeEnd, end)
                    .SetProperty(row => row.LastObservedAt, row => evidenceAt ?? row.LastObservedAt), cancellationToken);
            }

            foreach (var row in automaticOverlaps)
            {
                await CarveAutomaticRangeAsync(row, episodeStart ?? 1, episodeEnd ?? int.MaxValue, cancellationToken);
            }

            var now = DateTimeOffset.UtcNow;
            var normalizedTitle = NormalizeSeasonTitle(seasonTitle);
            if (existing is null)
            {
                existing = new MediaProviderSeasonMapping
                {
                    Id = Guid.NewGuid(), Provider = provider, ProviderSeriesId = series,
                    ProviderSeasonId = seasonId, ProviderSeasonKey = key, ProviderSeasonNumber = providerSeasonNumber,
                    NormalizedSeasonTitle = normalizedTitle, ProviderEpisodeStart = episodeStart,
                    ProviderEpisodeEnd = episodeEnd, MediaTitleId = mediaTitleId, EpisodeOffset = episodeOffset,
                    MappingSource = mappingSource, Confidence = confidence, FirstSeenAt = now, LastVerifiedAt = now,
                    LastObservedAt = evidenceAt
                };
                await InsertSeasonAsync(existing, cancellationToken);
            }
            else
            {
                await rows.Where(row => row.Id == existing.Id
                        && (mappingSource == MediaProviderSeasonMappingSources.Manual
                            || row.MappingSource == MediaProviderSeasonMappingSources.Automatic))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(row => row.ProviderSeasonId, row => seasonId ?? row.ProviderSeasonId)
                        .SetProperty(row => row.ProviderSeasonKey, row => seasonId != null ? key : row.ProviderSeasonKey)
                        .SetProperty(row => row.MediaTitleId, mediaTitleId)
                        .SetProperty(row => row.EpisodeOffset, episodeOffset)
                        .SetProperty(row => row.MappingSource, mappingSource)
                        .SetProperty(row => row.Confidence, confidence)
                        .SetProperty(row => row.ProviderSeasonNumber, row => providerSeasonNumber ?? row.ProviderSeasonNumber)
                        .SetProperty(row => row.NormalizedSeasonTitle, row => normalizedTitle ?? row.NormalizedSeasonTitle)
                        .SetProperty(row => row.LastVerifiedAt, now)
                        .SetProperty(row => row.LastObservedAt, evidenceAt), cancellationToken);
                existing = await rows.AsNoTracking().SingleAsync(row => row.Id == existing.Id, cancellationToken);
            }

            return existing;
        }, cancellationToken);
    }

    private Task<int> InsertSeasonAsync(MediaProviderSeasonMapping row, CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "MediaProviderSeasonMappings"
                ("Id", "Provider", "ProviderSeriesId", "ProviderSeasonId", "ProviderSeasonKey",
                 "ProviderSeasonNumber", "NormalizedSeasonTitle", "ProviderEpisodeStart", "ProviderEpisodeEnd",
                 "MediaTitleId", "EpisodeOffset", "MappingSource", "Confidence", "FirstSeenAt", "LastVerifiedAt",
                 "LastObservedAt")
            VALUES ({row.Id}, {row.Provider}, {row.ProviderSeriesId}, {row.ProviderSeasonId}, {row.ProviderSeasonKey},
                    {row.ProviderSeasonNumber}, {row.NormalizedSeasonTitle}, {row.ProviderEpisodeStart}, {row.ProviderEpisodeEnd},
                    {row.MediaTitleId}, {row.EpisodeOffset}, {row.MappingSource}, {row.Confidence},
                    {row.FirstSeenAt}, {row.LastVerifiedAt}, {row.LastObservedAt})
            """, cancellationToken);

    private async Task CarveAutomaticRangeAsync(MediaProviderSeasonMapping row, int manualStart, int manualEnd,
        CancellationToken cancellationToken)
    {
        var query = dbContext.MediaProviderSeasonMappings.Where(mapping => mapping.Id == row.Id);
        var maximum = row.MediaTitle?.EpisodeCount is > 0 ? row.MediaTitle.EpisodeCount.Value : int.MaxValue;
        var effectiveStart = Math.Max(row.ProviderEpisodeStart ?? 1, 1L - row.EpisodeOffset);
        var effectiveEnd = Math.Min(row.ProviderEpisodeEnd ?? int.MaxValue, (long)maximum - row.EpisodeOffset);
        var leftEnd = Math.Min(effectiveEnd, (long)manualStart - 1);
        var rightStart = Math.Max(effectiveStart, (long)manualEnd + 1);
        var keepLeft = effectiveStart <= leftEnd;
        var keepRight = rightStart <= effectiveEnd;
        var end = keepRight && effectiveEnd < int.MaxValue ? (int?)effectiveEnd : row.ProviderEpisodeEnd;
        if (keepLeft)
        {
            var start = effectiveStart == 1 ? row.ProviderEpisodeStart : (int?)effectiveStart;
            await query.ExecuteUpdateAsync(setters => setters
                .SetProperty(mapping => mapping.ProviderEpisodeStart, start)
                .SetProperty(mapping => mapping.ProviderEpisodeEnd, (int)leftEnd), cancellationToken);
            if (keepRight)
            {
                row.Id = Guid.NewGuid();
                row.ProviderEpisodeStart = (int)rightStart;
                row.ProviderEpisodeEnd = end;
                await InsertSeasonAsync(row, cancellationToken);
            }
        }
        else if (keepRight)
        {
            await query.ExecuteUpdateAsync(setters => setters
                .SetProperty(mapping => mapping.ProviderEpisodeStart, (int)rightStart)
                .SetProperty(mapping => mapping.ProviderEpisodeEnd, end), cancellationToken);
        }
        else
        {
            await query.ExecuteDeleteAsync(cancellationToken);
        }
    }

    private async Task<(MediaProviderSeasonMapping? Existing, List<(Guid Id, int End)> Narrowings,
        List<MediaProviderSeasonMapping> AutomaticOverlaps, bool HasOverlap)>
        PlanSeasonWriteAsync(string provider, string series, string key, string? seasonId, int? seasonNumber,
            int? episodeStart, int? episodeEnd, bool automatic, DateTimeOffset? evidenceAt,
            CancellationToken cancellationToken)
    {
        var overlaps = await dbContext.MediaProviderSeasonMappings.AsNoTracking().Include(row => row.MediaTitle)
            .Where(row => row.Provider == provider && row.ProviderSeriesId == series
                && (row.ProviderSeasonKey == key || (seasonNumber > 0
                    && row.ProviderSeasonNumber == seasonNumber
                    && (seasonId == null || row.ProviderSeasonId == null)))
                && (row.ProviderEpisodeStart ?? 1) <= (episodeEnd ?? int.MaxValue)
                && (row.ProviderEpisodeEnd ?? int.MaxValue) >= (episodeStart ?? 1)).ToListAsync(cancellationToken);
        var existing = overlaps.OrderByDescending(row => row.MappingSource == MediaProviderSeasonMappingSources.Manual)
            .FirstOrDefault(row => row.ProviderEpisodeStart == episodeStart && row.ProviderEpisodeEnd == episodeEnd);
        var narrowings = new List<(Guid Id, int End)>();
        var automaticOverlaps = new List<MediaProviderSeasonMapping>();
        foreach (var row in overlaps.Where(row => row.Id != existing?.Id))
        {
            var impliedEnd = (long?)row.MediaTitle?.EpisodeCount - row.EpisodeOffset;
            if (row is { ProviderEpisodeStart: null, ProviderEpisodeEnd: null,
                    MappingSource: MediaProviderSeasonMappingSources.Automatic }
                && row.MediaTitle?.EpisodeCount is > 0
                && (!automatic || !HasNewerEvidence(row.LastObservedAt, evidenceAt))
                && impliedEnd is >= 1 && impliedEnd < episodeStart)
            {
                narrowings.Add((row.Id, (int)impliedEnd.Value));
                continue;
            }
            if (!automatic && row.MappingSource == MediaProviderSeasonMappingSources.Automatic)
            {
                automaticOverlaps.Add(row);
                continue;
            }
            return (existing, narrowings, automaticOverlaps, true);
        }
        return (existing, narrowings, automaticOverlaps, false);
    }

    private static bool HasNewerEvidence(DateTimeOffset? existing, DateTimeOffset? incoming) =>
        existing is { } recordedAt && (incoming is null || incoming < recordedAt);

    internal Task EstablishCatalogDirectMappingsAsync(MediaObservation observation, MediaTitle title, int offset,
        string source, CancellationToken cancellationToken) => WithWriteAsync(async () =>
    {
        var provider = observation.SiteIdentifier;
        var episodes = observation.Episodes.Select(episode => (Episode: episode, Id: NormalizeId(episode.ProviderEpisodeId)))
            .Where(item => item.Id is not null).ToArray();
        var ids = episodes.Select(item => item.Id).ToArray();
        var currentEpisodes = await dbContext.MediaProviderEpisodes.AsNoTracking()
            .Where(row => row.Provider == provider && ids.Contains(row.ProviderEpisodeId))
            .ToDictionaryAsync(row => row.ProviderEpisodeId, cancellationToken);

        var seriesId = NormalizeId(observation.ProviderSeriesId);
        var seasonId = NormalizeId(observation.ProviderSeasonId);
        foreach (var (episode, id) in episodes)
        {
            var number = (long)episode.EpisodeNumber + offset;
            if ((!title.IsCompletionOnly && !MediaObservationMatchingService.IsCanonicalEpisodeNumber(title, number))
                || !MediaDestinationUrlPolicy.TryNormalizePath(provider, episode.ProviderUrl, id, out _))
            {
                continue;
            }

            if (currentEpisodes.TryGetValue(id!, out var current) && current.LastSeenAt > observation.ObservedAt
                && ((current.ProviderEpisodeNumber is { } currentNumber && currentNumber != episode.EpisodeNumber)
                    || (seriesId is not null && current.ProviderSeriesId is { } currentSeries && currentSeries != seriesId)
                    || (seasonId is not null && current.ProviderSeasonId is { } currentSeason && currentSeason != seasonId)
                    || (observation.SeasonNumber is { } ordinal && current.ProviderSeasonNumber is { } currentOrdinal
                        && currentOrdinal != ordinal)))
            {
                continue;
            }

            await EstablishDirectAsync(provider, id!, title.Id, title.IsCompletionOnly ? null : (int)number,
                source, cancellationToken, observation.ObservedAt);
        }

        return true;
    }, cancellationToken);

    internal Task<bool> EstablishManualDirectAsync(string provider, string providerEpisodeId, Guid titleId,
        int? episodeNumber, CancellationToken cancellationToken) => WithWriteAsync(async () =>
    {
        var title = await dbContext.MediaTitles.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == titleId, cancellationToken);
        var id = NormalizeId(providerEpisodeId);
        var episode = await dbContext.MediaProviderEpisodes.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Provider == provider && row.ProviderEpisodeId == id, cancellationToken);
        if (title is null || episode is null)
        {
            return false;
        }

        if (!IsCompatibleTarget(title, episodeNumber.HasValue) || episodeNumber > title.EpisodeCount)
        {
            throw new ArgumentException("The canonical episode number must match the title's progress type and episode count.");
        }

        if (!title.IsCompletionOnly
            && !string.Equals(title.Format, MediaFormats.Ova, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(title.Format, MediaFormats.Movie, StringComparison.OrdinalIgnoreCase)
            && episode.ProviderSeriesId is not null && episode.ProviderEpisodeNumber is > 0
            && SeasonKey(episode.ProviderSeasonId, episode.ProviderSeasonNumber) is not null)
        {
            throw new ArgumentException("Use a season mapping for this provider episode.");
        }

        return await WriteDirectAsync(provider, episode.ProviderEpisodeId, titleId, episodeNumber,
            MediaProviderSeasonMappingSources.Manual, null, cancellationToken) > 0;
    }, cancellationToken);

    internal async Task<bool> EstablishDirectAsync(string provider, string episodeId, Guid titleId,
        int? episodeNumber, string source, CancellationToken cancellationToken, DateTimeOffset? observedAt = null)
    {
        ValidateSource(source);
        var evidenceAt = source == MediaProviderSeasonMappingSources.Automatic ? observedAt?.ToUniversalTime() : null;
        var changed = await WithWriteAsync(async () =>
        {
            if (IsSupported(provider))
            {
                await GetValidTargetAsync(titleId, episodeNumber.HasValue, cancellationToken);
            }

            return await WriteDirectAsync(provider, episodeId, titleId, episodeNumber, source, evidenceAt, cancellationToken);
        }, cancellationToken);
        return changed > 0;
    }

    private Task<int> WriteDirectAsync(string provider, string episodeId, Guid titleId, int? episodeNumber,
        string source, DateTimeOffset? evidenceAt, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        return dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "MediaProviderEpisodeMappings"
                ("Provider", "ProviderEpisodeId", "MediaTitleId", "CanonicalEpisodeNumber", "MappingSource",
                 "CreatedAt", "UpdatedAt", "LastObservedAt")
            VALUES ({provider}, {episodeId}, {titleId}, {episodeNumber}, {source}, {now}, {now}, {evidenceAt})
            ON CONFLICT ("Provider", "ProviderEpisodeId") DO UPDATE SET
                "MediaTitleId" = excluded."MediaTitleId",
                "CanonicalEpisodeNumber" = excluded."CanonicalEpisodeNumber",
                "MappingSource" = excluded."MappingSource",
                "UpdatedAt" = excluded."UpdatedAt",
                "LastObservedAt" = excluded."LastObservedAt"
            WHERE excluded."MappingSource" = 'manual'
                OR ("MediaProviderEpisodeMappings"."MappingSource" = 'automatic'
                    AND ("MediaProviderEpisodeMappings"."LastObservedAt" IS NULL
                        OR excluded."LastObservedAt" >= "MediaProviderEpisodeMappings"."LastObservedAt"))
            """, cancellationToken);
    }

    private async Task<MediaTitle> GetValidTargetAsync(Guid titleId, bool numbered, CancellationToken cancellationToken)
    {
        var title = await dbContext.MediaTitles.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == titleId, cancellationToken);
        if (title is null || !IsCompatibleTarget(title, numbered))
        {
            throw new ArgumentException(numbered
                ? "A numbered Crunchyroll mapping requires a video title with episode progress."
                : "An unnumbered Crunchyroll mapping requires a completion-only video title.");
        }

        return title;
    }

    private static void ValidateSource(string source)
    {
        if (source is not (MediaProviderSeasonMappingSources.Automatic or MediaProviderSeasonMappingSources.Manual))
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }
    }

    internal async Task RemoveCoveredDirectMappingsAsync(MediaProviderSeasonMapping mapping,
        IReadOnlyCollection<string> episodeIds, CancellationToken cancellationToken)
    {
        var directs = dbContext.MediaProviderEpisodeMappings.Where(row => row.Provider == mapping.Provider
            && episodeIds.Contains(row.ProviderEpisodeId));
        var episodes = await dbContext.MediaProviderEpisodes.AsNoTracking()
            .Where(row => row.Provider == mapping.Provider
                && directs.Any(direct => direct.ProviderEpisodeId == row.ProviderEpisodeId))
            .ToListAsync(cancellationToken);
        var destinations = await GetSeasonDestinationsAsync(dbContext, mapping.Provider, episodes, cancellationToken);
        var coveredIds = destinations.Where(pair => pair.Value.Mapping.Id == mapping.Id)
            .Select(pair => pair.Key).ToArray();
        if (coveredIds.Length > 0)
        {
            await directs.Where(row => coveredIds.Contains(row.ProviderEpisodeId)).ExecuteDeleteAsync(cancellationToken);
        }
    }

    internal static async Task<List<MappedProviderEpisode>> GetMappedEpisodesAsync(
        ApplicationDbContext dbContext, IReadOnlyCollection<Guid> titleIds, CancellationToken cancellationToken)
    {
        const string provider = MediaObservationSiteIdentifiers.Crunchyroll;
        var seriesIds = await dbContext.MediaProviderSeasonMappings.AsNoTracking()
            .Where(row => row.Provider == provider && titleIds.Contains(row.MediaTitleId))
            .Select(row => row.ProviderSeriesId).Distinct().ToListAsync(cancellationToken);
        var directIds = await dbContext.MediaProviderEpisodeMappings.AsNoTracking()
            .Where(row => row.Provider == provider && titleIds.Contains(row.MediaTitleId))
            .Select(row => row.ProviderEpisodeId).ToListAsync(cancellationToken);
        if (seriesIds.Count == 0 && directIds.Count == 0)
        {
            return [];
        }

        var raw = dbContext.MediaProviderEpisodes.AsNoTracking().Where(row => row.Provider == provider);
        var candidates = raw.Where(row => seriesIds.Contains(row.ProviderSeriesId!) || directIds.Contains(row.ProviderEpisodeId));
        var contentKeys = candidates.Where(row => row.ContentKey != null).Select(row => row.ContentKey);
        var episodes = await raw.Where(row => candidates.Any(candidate => candidate.ProviderEpisodeId == row.ProviderEpisodeId)
                || (row.ProviderSeriesId == null && row.ContentKey != null && contentKeys.Contains(row.ContentKey)))
            .ToListAsync(cancellationToken);
        var mapped = await ResolveMappedEpisodesAsync(dbContext, episodes, cancellationToken);
        return mapped.Where(item => titleIds.Contains(item.MediaTitleId)).ToList();
    }

    internal static async Task<List<MappedProviderEpisode>?> GetCatalogCoverageAsync(
        ApplicationDbContext dbContext, MediaObservation observation, CancellationToken cancellationToken)
    {
        var provider = observation.SiteIdentifier;
        if (!observation.IsCatalogObservation || observation.Episodes.Count == 0 || !IsSupported(provider))
        {
            return null;
        }

        var ids = observation.Episodes.Select(episode => NormalizeId(episode.ProviderEpisodeId)).Distinct().ToArray();
        var episodes = await dbContext.MediaProviderEpisodes.AsNoTracking()
            .Where(row => row.Provider == provider && ids.Contains(row.ProviderEpisodeId))
            .ToListAsync(cancellationToken);
        if (episodes.Count != ids.Length)
        {
            return null;
        }

        var mapped = await ResolveMappedEpisodesAsync(dbContext, episodes, cancellationToken);
        return mapped.Count == ids.Length ? mapped : null;
    }

    private static async Task<List<MappedProviderEpisode>> ResolveMappedEpisodesAsync(
        ApplicationDbContext dbContext, List<MediaProviderEpisode> episodes, CancellationToken cancellationToken)
    {
        const string provider = MediaObservationSiteIdentifiers.Crunchyroll;
        var candidateIds = episodes.Select(row => row.ProviderEpisodeId).ToArray();
        var allDirects = await dbContext.MediaProviderEpisodeMappings.AsNoTracking().Include(row => row.MediaTitle)
            .Where(row => row.Provider == provider && candidateIds.Contains(row.ProviderEpisodeId))
            .ToDictionaryAsync(row => row.ProviderEpisodeId, cancellationToken);
        var destinations = await GetSeasonDestinationsAsync(dbContext, provider, episodes, cancellationToken);
        var result = new List<MappedProviderEpisode>();
        foreach (var episode in episodes)
        {
            var season = destinations.GetValueOrDefault(episode.ProviderEpisodeId);
            var direct = GetCompatibleDirectMapping(allDirects.GetValueOrDefault(episode.ProviderEpisodeId));
            if (WinsOverSeason(direct, season is not null))
            {
                result.Add(new(direct!.MediaTitleId, direct.CanonicalEpisodeNumber, episode));
                continue;
            }

            if (season is not null)
            {
                result.Add(new(season.Mapping.MediaTitleId, season.EpisodeNumber, episode));
            }
        }

        return result;
    }

    private sealed record SeasonDestination(MediaProviderSeasonMapping Mapping, int EpisodeNumber);

    private static async Task<Dictionary<string, SeasonDestination>> GetSeasonDestinationsAsync(
        ApplicationDbContext dbContext, string provider, IReadOnlyCollection<MediaProviderEpisode> episodes,
        CancellationToken cancellationToken)
    {
        if (episodes.Count == 0)
        {
            return [];
        }

        var raw = dbContext.MediaProviderEpisodes.AsNoTracking().Where(row => row.Provider == provider);
        var siblingKeys = episodes.Select(row => row.ContentKey).OfType<string>().Distinct().ToArray();
        var siblingRows = await raw.Where(row => row.ContentKey != null && siblingKeys.Contains(row.ContentKey)
                && (row.ProviderSeasonId != null || (row.ProviderSeriesId != null && row.ProviderSeasonNumber > 0)))
            .ToListAsync(cancellationToken);
        var relatedSeries = episodes.Concat(siblingRows).Select(row => row.ProviderSeriesId).OfType<string>().Distinct().ToArray();
        var allSeasons = await dbContext.MediaProviderSeasonMappings.AsNoTracking().Include(row => row.MediaTitle)
            .Where(row => row.Provider == provider && relatedSeries.Contains(row.ProviderSeriesId))
            .ToListAsync(cancellationToken);
        var seasonsBySeries = allSeasons.ToLookup(row => row.ProviderSeriesId);
        var knownSeasons = await raw.Where(row => (row.ProviderSeasonId != null || row.ProviderSeasonNumber > 0)
                && relatedSeries.Contains(row.ProviderSeriesId!))
            .Select(row => new { row.ProviderSeriesId, row.ProviderSeasonId, row.ProviderSeasonNumber }).Distinct()
            .ToListAsync(cancellationToken);
        var multiSeasonSeries = knownSeasons.GroupBy(row => row.ProviderSeriesId)
            .Where(group => HasMultipleObservedSeasons(group.Select(row => new ObservedSeason(row.ProviderSeasonId, row.ProviderSeasonNumber))))
            .Select(group => group.Key).ToHashSet();
        var siblingsByKey = siblingRows.ToLookup(row => row.ContentKey!);
        var destinations = new Dictionary<string, SeasonDestination>();
        foreach (var episode in episodes)
        {
            var coordinates = Coordinates(episode);
            if (episode.ContentKey is { } key && NeedsSibling(coordinates))
            {
                coordinates = CompleteFromSibling(coordinates, episode.ProviderEpisodeId, siblingsByKey[key]);
            }

            var season = SelectSeason(seasonsBySeries[coordinates.SeriesId ?? string.Empty], coordinates,
                allowSingleSeasonFallback: !multiSeasonSeries.Contains(coordinates.SeriesId),
                row => CanonicalNumber(row, coordinates.EpisodeNumber).HasValue);
            if (season is not null && CanonicalNumber(season, coordinates.EpisodeNumber) is { } canonicalNumber)
            {
                destinations.Add(episode.ProviderEpisodeId, new(season, canonicalNumber));
            }
        }

        return destinations;
    }
}

internal sealed record ProviderEpisodeCoordinates(
    string? SeriesId, string? SeasonId, int? SeasonNumber, int? EpisodeNumber, string? SeasonTitle);

internal sealed record ProviderEpisodeResolution(
    MediaTitle Title, int? EpisodeNumber, int? EpisodeOffset, string Source, bool Direct);

internal sealed record MappedProviderEpisode(Guid MediaTitleId, int? EpisodeNumber, MediaProviderEpisode Episode);
