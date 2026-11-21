using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Cantaro.Api.Services;

public class MediaEpisodeIdentityService(
    ApplicationDbContext dbContext,
    MediaProviderSeasonMappingService seasonMappingService,
    ILogger<MediaEpisodeIdentityService> logger)
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly MediaProviderSeasonMappingService _seasonMappingService = seasonMappingService;

    public async Task RecordObservationAsync(MediaObservation observation, CancellationToken cancellationToken, bool countSeen = true)
    {
        await RecordCatalogObservationAsync(observation, cancellationToken, countSeen);
        if (observation.IsCatalogObservation || !MediaProviderSeasonMappingService.IsSupported(observation.SiteIdentifier))
        {
            return;
        }

        await _seasonMappingService.WithWriteAsync(async () =>
        {
            var watched = await RecordEpisodeAsync(observation, observation.SiteMediaId, observation.ObservedUrl,
                observation.EpisodeNumber, observation.EpisodeTitle, observation.ReleaseTrack, [], [],
                observation.ProviderSequenceNumber, false, countSeen, cancellationToken);
            if (watched && await _seasonMappingService.GetSuccessorCoordinatesAsync(observation, cancellationToken)
                is { } successor)
            {
                await RecordEpisodeAsync(observation, observation.NextEpisodeProviderId, observation.NextEpisodeUrl,
                    observation.NextEpisodeNumber, observation.NextEpisodeTitle, observation.NextEpisodeReleaseTrack,
                    [], [], null, false, countSeen, cancellationToken, successor.Inferred);
            }

            return watched;
        }, cancellationToken);
    }

    public async Task<int> RecordCatalogObservationAsync(
        MediaObservation observation, CancellationToken cancellationToken, bool countSeen = true)
    {
        if (observation.Episodes.Count == 0 || !MediaProviderSeasonMappingService.IsSupported(observation.SiteIdentifier))
        {
            return 0;
        }

        return await _seasonMappingService.WithWriteAsync(async () =>
        {
            var count = 0;
            foreach (var episode in observation.Episodes
                         .Where(item => item.EpisodeNumber > 0)
                         .DistinctBy(item => item.ProviderEpisodeId.Trim(), StringComparer.OrdinalIgnoreCase)
                         .Take(MediaCatalogObservationLimits.MaximumEpisodesPerObservation))
            {
                if (await RecordEpisodeAsync(observation, episode.ProviderEpisodeId, episode.ProviderUrl,
                    episode.EpisodeNumber, episode.EpisodeTitle, episode.ReleaseTrack,
                    episode.AvailableSubtitleLanguageCodes, episode.AvailableAudioLanguageCodes,
                    null, true, countSeen, cancellationToken))
                {
                    count++;
                }
            }

            return count;
        }, cancellationToken);
    }

    private async Task<bool> RecordEpisodeAsync(MediaObservation observation, string? id, string? url,
        int? episodeNumber, string? title, string? releaseTrack, IEnumerable<string> subtitles,
        IEnumerable<string> audio, int? sequence, bool catalog, bool countSeen, CancellationToken cancellationToken,
        ProviderEpisodeCoordinates? coordinates = null)
    {
        var provider = observation.SiteIdentifier;
        var episodeId = MediaProviderSeasonMappingService.NormalizeId(id);
        if (!MediaDestinationUrlPolicy.TryNormalizePath(provider, url, episodeId, out var path))
        {
            logger.LogDebug("Skipped unsafe provider episode destination.");
            return false;
        }

        var variant = MediaProviderVariantIdentities.Parse(provider, episodeId!);
        var series = coordinates is null
            ? MediaProviderSeasonMappingService.NormalizeId(observation.ProviderSeriesId) : coordinates.SeriesId;
        if (MediaDestinationUrlPolicy.BuildSeriesUrl(provider, series) is null)
        {
            series = null;
        }

        var season = coordinates is null
            ? MediaProviderSeasonMappingService.NormalizeId(observation.ProviderSeasonId) : coordinates.SeasonId;
        var seasonNumber = coordinates is null ? observation.SeasonNumber : coordinates.SeasonNumber;
        var seasonTitle = coordinates is null
            ? MediaProviderSeasonMappingService.NormalizeSeasonTitle(observation.SeasonTitle) : coordinates.SeasonTitle;
        var normalizedTrack = MediaReleaseTrackPreferences.TryNormalize(releaseTrack, out var track) ? track : null;
        if (!catalog)
        {
            if (normalizedTrack?.StartsWith("sub:", StringComparison.Ordinal) == true)
            {
                subtitles = subtitles.Append(normalizedTrack[4..]);
            }
            else if (normalizedTrack?.StartsWith("dub:", StringComparison.Ordinal) == true)
            {
                audio = audio.Append(normalizedTrack[4..]);
            }
            else if (variant.AudioLocale is { } locale)
            {
                audio = audio.Append(locale.Split('-', 2)[0]);
            }
        }
        var subtitleJson = JsonSerializer.Serialize(NormalizeLanguageCodes(subtitles));
        var audioJson = JsonSerializer.Serialize(NormalizeLanguageCodes(audio));
        var observedAt = observation.ObservedAt.ToUniversalTime();
        // Capture coordinates for existing direct mappings before replacing provider facts.
        var directCoordinates = countSeen && coordinates is null
            && (series is not null || season is not null || seasonNumber.HasValue)
            ? await _seasonMappingService.GetAutomaticDirectCoordinatesAsync(
                provider, episodeId!, variant.ContentKey, observedAt, cancellationToken)
            : [];

        // PostgreSQL stores primitive collections as arrays. SQLite stores them as JSON.
        FormattableString insert = _dbContext.Database.IsNpgsql()
            ? (FormattableString)$"""
              VALUES ({provider}, {episodeId}, {series}, {season}, {seasonNumber}, {seasonTitle}, {episodeNumber}, {sequence},
                  {path}, {variant.ContentKey}, {variant.AudioLocale}, {normalizedTrack}, {title},
                  ARRAY(SELECT jsonb_array_elements_text(CAST({subtitleJson} AS jsonb))),
                  ARRAY(SELECT jsonb_array_elements_text(CAST({audioJson} AS jsonb))), 1, {observedAt}, {observedAt})
              """
            : $"""
              VALUES ({provider}, {episodeId}, {series}, {season}, {seasonNumber}, {seasonTitle}, {episodeNumber}, {sequence},
                  {path}, {variant.ContentKey}, {variant.AudioLocale}, {normalizedTrack}, {title},
                  {subtitleJson}, {audioJson}, 1, {observedAt}, {observedAt})
              """;
        string MergeLanguages(string column) => _dbContext.Database.IsNpgsql()
            ? $"""ARRAY(SELECT DISTINCT unnest("MediaProviderEpisodes"."{column}" || excluded."{column}") ORDER BY 1)"""
            : $"""
              (SELECT json_group_array(value) FROM (
                  SELECT value FROM json_each("MediaProviderEpisodes"."{column}")
                  UNION SELECT value FROM json_each(excluded."{column}") ORDER BY value))
              """;
        const string incomingIsCurrent = """excluded."LastSeenAt" >= "MediaProviderEpisodes"."LastSeenAt" """;
        string KeepFacts(string column) => countSeen
            ? $"""
              CASE WHEN {incomingIsCurrent}
                   THEN COALESCE(excluded."{column}", "MediaProviderEpisodes"."{column}")
                   ELSE COALESCE("MediaProviderEpisodes"."{column}", excluded."{column}") END
              """
            : $"""COALESCE("MediaProviderEpisodes"."{column}", excluded."{column}")""";
        const string seriesChanged = """excluded."ProviderSeriesId" <> "MediaProviderEpisodes"."ProviderSeriesId" """;
        const string seasonNumberChangedWithoutId = """
            excluded."ProviderSeasonId" IS NULL
            AND excluded."ProviderSeasonNumber" <> "MediaProviderEpisodes"."ProviderSeasonNumber"
            """;
        const string seasonIdChangedWithoutNumber = """
            excluded."ProviderSeasonNumber" IS NULL
            AND excluded."ProviderSeasonId" <> "MediaProviderEpisodes"."ProviderSeasonId"
            """;
        string Contradicts(string column) => $"""
            (excluded."{column}" IS NOT NULL AND "MediaProviderEpisodes"."{column}" IS NOT NULL
                AND excluded."{column}" <> "MediaProviderEpisodes"."{column}")
            """;
        var coordinatesConflict = string.Join(" OR ",
            Contradicts("ProviderSeriesId"), Contradicts("ProviderSeasonId"), Contradicts("ProviderSeasonNumber"));
        string AddsOrChanges(string column) => $"""
            (excluded."{column}" IS NOT NULL AND excluded."{column}" IS DISTINCT FROM "MediaProviderEpisodes"."{column}")
            """;
        var coordinatesChanged = string.Join(" OR ",
            AddsOrChanges("ProviderSeriesId"), AddsOrChanges("ProviderSeasonId"), AddsOrChanges("ProviderSeasonNumber"));
        string KeepSeasonFact(string column, string correction)
        {
            var staleValue = $"""
                CASE WHEN {coordinatesConflict} THEN "MediaProviderEpisodes"."{column}"
                     ELSE COALESCE("MediaProviderEpisodes"."{column}", excluded."{column}") END
                """;
            return countSeen
                ? $"""
                  CASE WHEN {incomingIsCurrent}
                       THEN CASE WHEN {correction} THEN excluded."{column}"
                                 ELSE COALESCE(excluded."{column}", "MediaProviderEpisodes"."{column}") END
                       ELSE {staleValue} END
                  """
                : staleValue;
        }

        var increment = countSeen ? 1 : 0;
        var lastSeen = countSeen
            ? """
              CASE WHEN excluded."LastSeenAt" >= "MediaProviderEpisodes"."LastSeenAt"
                   THEN excluded."LastSeenAt" ELSE "MediaProviderEpisodes"."LastSeenAt" END
              """
            : "\"MediaProviderEpisodes\".\"LastSeenAt\"";
        var firstSeen = countSeen
            ? """
              CASE WHEN excluded."FirstSeenAt" < "MediaProviderEpisodes"."FirstSeenAt"
                   THEN excluded."FirstSeenAt" ELSE "MediaProviderEpisodes"."FirstSeenAt" END
              """
            : "\"MediaProviderEpisodes\".\"FirstSeenAt\"";
        var sql = """
            INSERT INTO "MediaProviderEpisodes"
                ("Provider", "ProviderEpisodeId", "ProviderSeriesId", "ProviderSeasonId", "ProviderSeasonNumber", "NormalizedSeasonTitle",
                 "ProviderEpisodeNumber", "ProviderSequenceNumber", "ProviderUrlPath", "ContentKey", "AudioLocale",
                 "ReleaseTrack", "EpisodeTitle", "AvailableSubtitleLanguageCodes", "AvailableAudioLanguageCodes",
                 "SeenCount", "FirstSeenAt", "LastSeenAt")
            """ + Environment.NewLine + insert.Format + Environment.NewLine + $"""
            ON CONFLICT ("Provider", "ProviderEpisodeId") DO UPDATE SET
                "ProviderSeriesId" = {KeepSeasonFact("ProviderSeriesId", "FALSE")},
                "ProviderSeasonId" = {KeepSeasonFact("ProviderSeasonId", $"({seriesChanged}) OR ({seasonNumberChangedWithoutId})")},
                "ProviderSeasonNumber" = {KeepSeasonFact("ProviderSeasonNumber", $"({seriesChanged}) OR ({seasonIdChangedWithoutNumber})")},
                "NormalizedSeasonTitle" = {KeepSeasonFact("NormalizedSeasonTitle", coordinatesChanged)},
                "ProviderEpisodeNumber" = {KeepSeasonFact("ProviderEpisodeNumber", coordinatesConflict)},
                "ProviderSequenceNumber" = {KeepFacts("ProviderSequenceNumber")},
                "ProviderUrlPath" = {KeepFacts("ProviderUrlPath")},
                "EpisodeTitle" = {KeepFacts("EpisodeTitle")},
                "ContentKey" = {KeepFacts("ContentKey")},
                "AudioLocale" = {KeepFacts("AudioLocale")},
                "ReleaseTrack" = {KeepFacts("ReleaseTrack")},
                "AvailableSubtitleLanguageCodes" = {MergeLanguages("AvailableSubtitleLanguageCodes")},
                "AvailableAudioLanguageCodes" = {MergeLanguages("AvailableAudioLanguageCodes")},
                "SeenCount" = "MediaProviderEpisodes"."SeenCount" + {increment},
                "FirstSeenAt" = {firstSeen},
                "LastSeenAt" = {lastSeen}
            """;
        await _dbContext.Database.ExecuteSqlInterpolatedAsync(System.Runtime.CompilerServices.FormattableStringFactory.Create(sql, insert.GetArguments()), cancellationToken);
        if (directCoordinates.Count > 0)
        {
            await _seasonMappingService.RemoveContradictedDirectMappingsAsync(
                provider, directCoordinates, cancellationToken);
        }

        return true;
    }

    public async Task<MediaEpisodeCatalogDto?> GetEpisodeCatalogAsync(
        Guid mediaTitleId, CancellationToken cancellationToken, string? preferredReleaseTrack = null)
    {
        var title = await _dbContext.MediaTitles.AsNoTracking().Include(item => item.ProviderLinks)
            .SingleOrDefaultAsync(item => item.Id == mediaTitleId, cancellationToken);
        if (title is null)
        {
            return null;
        }

        var canonical = await _dbContext.MediaEpisodes.AsNoTracking()
            .Where(episode => episode.MediaTitleId == mediaTitleId)
            .ToDictionaryAsync(episode => episode.EpisodeNumber, cancellationToken);
        var simklCoordinates = await QueryVerifiedSimklEpisodeCoordinates(_dbContext, mediaTitleId)
            .ToDictionaryAsync(item => item.MediaEpisodeId, cancellationToken);
        var mapped = await MediaProviderSeasonMappingService.GetMappedEpisodesAsync(_dbContext, [mediaTitleId], cancellationToken);
        var variants = mapped.Where(item => item.EpisodeNumber is not null).ToLookup(item => item.EpisodeNumber!.Value);
        var preferredTrack = MediaReleaseTrackPreferences.TryNormalize(preferredReleaseTrack, out var track)
            ? track : MediaReleaseTrackPreferences.Default;
        var episodes = canonical.Keys.Union(variants.Select(group => group.Key)).Order()
            .Select(number =>
            {
                var episode = canonical.GetValueOrDefault(number);
                var coordinates = episode is null ? null : simklCoordinates.GetValueOrDefault(episode.Id);
                return MapEpisodeDestination(number, episode, coordinates,
                    variants[number].Select(item => item.Episode), preferredTrack);
            })
            .ToList();
        var standalone = OrderVariants(mapped.Where(item => item.EpisodeNumber is null)
                .Select(item => item.Episode), preferredTrack)
            .Select(MapDestination).OfType<MediaStreamingDestinationDto>().ToList();
        return new MediaEpisodeCatalogDto
        {
            SeriesDestinations = await SelectSeriesDestinationsAsync(title, mapped, cancellationToken),
            Episodes = episodes,
            StandaloneDestinations = standalone,
            Specials = title.ProviderLinks
                .Where(link => link.Provider == MediaObservationSiteIdentifiers.Simkl && link.SpecialEpisodeCatalogSnapshot is not null)
                .SelectMany(link => DeserializeSpecialEpisodeCatalog(link.SpecialEpisodeCatalogSnapshot!))
                .OrderBy(item => item.SpecialEpisodeNumber).ToList(),
            ReleaseAvailability = BuildReleaseAvailability(episodes, standalone, title.ReleasedCount)
        };
    }

    internal sealed record SimklEpisodeCoordinates
    {
        public Guid MediaEpisodeId { get; init; }
        public int SeasonNumber { get; init; }
        public int SeasonEpisodeNumber { get; init; }
    }

    internal static IQueryable<SimklEpisodeCoordinates> QueryVerifiedSimklEpisodeCoordinates(
        ApplicationDbContext db, Guid mediaTitleId)
    {
        var candidates = from episode in db.MediaEpisodes
                         from content in episode.ProviderContents
                         where episode.MediaTitleId == mediaTitleId
                             && content.Provider == MediaObservationSiteIdentifiers.Simkl
                             && content.ProviderSeasonNumber > 0 && content.ProviderEpisodeNumber > 0
                             && content.ProviderSequenceNumber == episode.EpisodeNumber
                             && episode.MediaTitle!.ProviderLinks.Any(link =>
                                 link.Provider == MediaObservationSiteIdentifiers.Simkl
                                 && link.LastVerifiedAt != null && link.ExternalId == content.ProviderSeriesId)
                         select new
                         {
                             MediaEpisodeId = episode.Id,
                             SeasonNumber = content.ProviderSeasonNumber!.Value,
                             SeasonEpisodeNumber = content.ProviderEpisodeNumber!.Value
                         };

        // A playable coordinate must identify exactly one canonical episode and vice versa.
        return candidates.Where(candidate => !candidates.Any(other =>
                (other.MediaEpisodeId == candidate.MediaEpisodeId
                    && (other.SeasonNumber != candidate.SeasonNumber
                        || other.SeasonEpisodeNumber != candidate.SeasonEpisodeNumber))
                || (other.MediaEpisodeId != candidate.MediaEpisodeId
                    && other.SeasonNumber == candidate.SeasonNumber
                    && other.SeasonEpisodeNumber == candidate.SeasonEpisodeNumber)))
            .Select(candidate => new SimklEpisodeCoordinates
            {
                MediaEpisodeId = candidate.MediaEpisodeId,
                SeasonNumber = candidate.SeasonNumber,
                SeasonEpisodeNumber = candidate.SeasonEpisodeNumber
            })
            .Distinct();
    }

    public Task CacheProviderEpisodeCatalogAsync(
        Guid mediaTitleId,
        string provider,
        string providerSeriesId,
        IReadOnlyList<MediaProviderWatchedEpisode> catalog,
        IReadOnlyList<MediaProviderWatchedEpisode> specials,
        int? expectedEpisodeCount,
        CancellationToken cancellationToken) =>
        CacheProviderEpisodeCatalogAsync(_dbContext, mediaTitleId, provider, providerSeriesId,
            catalog, specials, expectedEpisodeCount, cancellationToken);

    internal async Task CacheProviderEpisodeCatalogAsync(
        ApplicationDbContext db,
        Guid mediaTitleId,
        string provider,
        string providerSeriesId,
        IReadOnlyList<MediaProviderWatchedEpisode> catalog,
        IReadOnlyList<MediaProviderWatchedEpisode> specials,
        int? expectedEpisodeCount,
        CancellationToken cancellationToken)
    {
        var normalizedProvider = provider.Trim().ToLowerInvariant();
        var normalizedSeriesId = providerSeriesId.Trim();
        if (normalizedProvider != MediaObservationSiteIdentifiers.Simkl || normalizedSeriesId.Length == 0)
        {
            return;
        }

        var orderedRegularEpisodes = catalog
            .Where(item => item.SeasonNumber is > 0 && item.EpisodeNumber > 0)
            .OrderBy(item => item.SeasonNumber)
            .ThenBy(item => item.EpisodeNumber)
            .DistinctBy(item => (item.SeasonNumber, item.EpisodeNumber))
            .ToList();
        var contiguousEpisodes = TakeContiguousCatalogPrefix(orderedRegularEpisodes);

        var now = DateTimeOffset.UtcNow;
        var providerLink = db.MediaProviderLinks.Local.FirstOrDefault(link =>
                link.MediaTitleId == mediaTitleId
                && link.Provider == normalizedProvider
                && link.ExternalId == normalizedSeriesId)
            ?? await db.MediaProviderLinks.FirstOrDefaultAsync(link =>
                link.MediaTitleId == mediaTitleId
                && link.Provider == normalizedProvider
                && link.ExternalId == normalizedSeriesId,
                cancellationToken);
        if (providerLink is not null)
        {
            providerLink.SpecialEpisodeCatalogSnapshot = JsonSerializer.Serialize(
                specials
                    .Where(item => item.EpisodeNumber > 0)
                    .OrderBy(item => item.EpisodeNumber)
                    .Select(item => new MediaSpecialEpisodeDestinationDto
                    {
                        SpecialEpisodeNumber = item.EpisodeNumber,
                        Title = item.Title
                    }));
            providerLink.EpisodeCatalogLastVerifiedAt = now;
            providerLink.UpdatedAt = now;
        }

        var episodes = await db.MediaEpisodes
            .Where(episode => episode.MediaTitleId == mediaTitleId)
            .Include(episode => episode.ProviderContents)
            .OrderBy(episode => episode.EpisodeNumber)
            .ToListAsync(cancellationToken);
        var episodesByNumber = episodes.ToDictionary(episode => episode.EpisodeNumber);
        var existingContents = episodes
            .SelectMany(episode => episode.ProviderContents)
            .Where(content => content.Provider == normalizedProvider
                && content.ProviderSeriesId == normalizedSeriesId)
            .ToList();
        var existingByKey = existingContents
            .Where(content => content.ProviderContentKey is not null)
            .GroupBy(content => content.ProviderContentKey!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var existingSequence = existingContents
            .Where(content => content.ProviderSequenceNumber is > 0)
            .OrderBy(content => content.ProviderSequenceNumber)
            .ToList();
        var incomingKeys = orderedRegularEpisodes
            .Select(item => GetProviderContentKey(normalizedSeriesId, item))
            .ToList();
        var changesExistingSequence = existingSequence.Any(content =>
            content.ProviderSequenceNumber is { } sequence
            && sequence <= incomingKeys.Count
            && content.ProviderContentKey is { } existingKey
            && existingKey != incomingKeys[sequence - 1]);
        var isShorterThanExistingCache = orderedRegularEpisodes.Count < existingSequence.Count;
        var hasTrustedCompleteCatalog = contiguousEpisodes.Count == orderedRegularEpisodes.Count
            && expectedEpisodeCount is > 0
            && orderedRegularEpisodes.Count == expectedEpisodeCount
            && !changesExistingSequence
            && !isShorterThanExistingCache;
        IReadOnlyList<MediaProviderWatchedEpisode> regularEpisodes;
        if (changesExistingSequence || isShorterThanExistingCache)
        {
            regularEpisodes = [];
        }
        else if (hasTrustedCompleteCatalog)
        {
            regularEpisodes = contiguousEpisodes;
        }
        else
        {
            regularEpisodes = contiguousEpisodes
                .TakeWhile(item => item.SeasonNumber == 1)
                .ToList();
        }
        var retainedContentIds = new HashSet<Guid>();

        for (var index = 0; index < regularEpisodes.Count; index++)
        {
            var providerEpisode = regularEpisodes[index];
            var canonicalEpisodeNumber = index + 1;
            if (!episodesByNumber.TryGetValue(canonicalEpisodeNumber, out var episode))
            {
                episode = new MediaEpisode
                {
                    Id = Guid.NewGuid(),
                    MediaTitleId = mediaTitleId,
                    EpisodeNumber = canonicalEpisodeNumber,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                episodesByNumber[canonicalEpisodeNumber] = episode;
                db.MediaEpisodes.Add(episode);
            }

            // SIMKL supplies episode titles with the extended catalog response.
            // Keep a title already learned from another provider authoritative,
            // but fill an empty canonical title from this metadata-only refresh.
            if (string.IsNullOrWhiteSpace(episode.Title)
                && !string.IsNullOrWhiteSpace(providerEpisode.Title))
            {
                episode.Title = providerEpisode.Title.Trim();
                episode.UpdatedAt = now;
            }

            var contentKey = GetProviderContentKey(normalizedSeriesId, providerEpisode);
            if (!existingByKey.TryGetValue(contentKey, out var content))
            {
                content = new MediaEpisodeProviderContent
                {
                    Id = Guid.NewGuid(),
                    MediaEpisodeId = episode.Id,
                    Provider = normalizedProvider,
                    ProviderContentKey = contentKey
                };
                db.MediaEpisodeProviderContents.Add(content);
                existingByKey[contentKey] = content;
            }

            content.MediaEpisodeId = episode.Id;
            content.MediaEpisode = episode;
            content.ProviderSeriesId = normalizedSeriesId;
            content.ProviderSeasonNumber = providerEpisode.SeasonNumber;
            content.ProviderEpisodeNumber = providerEpisode.EpisodeNumber;
            content.ProviderSequenceNumber = canonicalEpisodeNumber;
            retainedContentIds.Add(content.Id);
        }

        var staleContents = existingContents
            .Where(content => hasTrustedCompleteCatalog
                && !retainedContentIds.Contains(content.Id))
            .ToList();
        db.MediaEpisodeProviderContents.RemoveRange(staleContents);
    }

    private static string GetProviderContentKey(
        string providerSeriesId,
        MediaProviderWatchedEpisode episode)
        => !string.IsNullOrWhiteSpace(episode.ProviderEpisodeId)
            ? episode.ProviderEpisodeId.Trim()
            : $"{providerSeriesId}:s{episode.SeasonNumber}:e{episode.EpisodeNumber}";

    private static IReadOnlyList<MediaProviderWatchedEpisode> TakeContiguousCatalogPrefix(
        IReadOnlyList<MediaProviderWatchedEpisode> episodes)
    {
        var prefix = new List<MediaProviderWatchedEpisode>();
        var expectedSeason = 1;
        var expectedEpisode = 1;
        foreach (var episode in episodes)
        {
            if (episode.SeasonNumber != expectedSeason || episode.EpisodeNumber != expectedEpisode)
            {
                if (episode.SeasonNumber == expectedSeason + 1 && episode.EpisodeNumber == 1)
                {
                    expectedSeason++;
                    expectedEpisode = 1;
                }
                else
                {
                    break;
                }
            }

            prefix.Add(episode);
            expectedEpisode++;
        }
        return prefix;
    }

    private static IReadOnlyList<MediaSpecialEpisodeDestinationDto> DeserializeSpecialEpisodeCatalog(string snapshot)
    {
        try
        {
            return JsonSerializer.Deserialize<List<MediaSpecialEpisodeDestinationDto>>(snapshot) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static MediaReleaseAvailabilityDto BuildReleaseAvailability(
        IReadOnlyCollection<MediaEpisodeDestinationDto> episodes,
        IReadOnlyCollection<MediaStreamingDestinationDto> standalone,
        int? maxReleasedEpisodes)
    {
        var releases = episodes.Select(episode => (Number: episode.EpisodeNumber,
                episode.AvailableSubtitleLanguageCodes, episode.AvailableAudioLanguageCodes))
            .Concat(standalone.Select(destination => (Number: 1,
                destination.AvailableSubtitleLanguageCodes, destination.AvailableAudioLanguageCodes)))
            .ToList();
        var languageCodes = releases
            .SelectMany(episode => episode.AvailableSubtitleLanguageCodes
                .Concat(episode.AvailableAudioLanguageCodes))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new MediaReleaseAvailabilityDto
        {
            MaxReleasedEpisodes = maxReleasedEpisodes,
            Languages = languageCodes.Select(languageCode => new MediaReleaseLanguageAvailabilityDto
            {
                LanguageCode = languageCode,
                SubReleasedEpisodes = releases
                    .Where(episode => episode.AvailableSubtitleLanguageCodes.Contains(languageCode, StringComparer.OrdinalIgnoreCase))
                    .Select(episode => (int?)episode.Number)
                    .Max(),
                DubReleasedEpisodes = releases
                    .Where(episode => episode.AvailableAudioLanguageCodes.Contains(languageCode, StringComparer.OrdinalIgnoreCase))
                    .Select(episode => (int?)episode.Number)
                    .Max()
            }).ToList()
        };
    }


    private static MediaEpisodeDestinationDto MapEpisodeDestination(int number, MediaEpisode? episode,
        SimklEpisodeCoordinates? simkl, IEnumerable<MediaProviderEpisode> variants, string preferredTrack)
    {
        var ordered = OrderVariants(variants, preferredTrack).ToList();
        return new MediaEpisodeDestinationDto
        {
            EpisodeNumber = number,
            SeasonNumber = simkl?.SeasonNumber,
            SeasonEpisodeNumber = simkl?.SeasonEpisodeNumber,
            Title = episode?.Title ?? ordered.Select(item => item.EpisodeTitle).FirstOrDefault(title => title is not null),
            AvailableSubtitleLanguageCodes = NormalizeLanguageCodes(
                (episode?.AvailableSubtitleLanguageCodes ?? []).Concat(ordered.SelectMany(item => item.AvailableSubtitleLanguageCodes))),
            AvailableAudioLanguageCodes = NormalizeLanguageCodes(
                (episode?.AvailableAudioLanguageCodes ?? []).Concat(ordered.SelectMany(item => item.AvailableAudioLanguageCodes))),
            Destinations = ordered.Select(MapDestination).OfType<MediaStreamingDestinationDto>().ToList()
        };
    }

    private static MediaStreamingDestinationDto? MapDestination(MediaProviderEpisode episode)
    {
        var url = MediaDestinationUrlPolicy.BuildUrl(episode.Provider, episode.ProviderUrlPath);
        return url is null ? null : new MediaStreamingDestinationDto
        {
            ServiceId = episode.Provider, Url = url, AudioLocale = episode.AudioLocale,
            Title = episode.EpisodeTitle,
            AvailableSubtitleLanguageCodes = episode.AvailableSubtitleLanguageCodes,
            AvailableAudioLanguageCodes = episode.AvailableAudioLanguageCodes,
            ReleaseTrack = episode.ReleaseTrack
        };
    }

    public async Task<MediaContinueWatchingDto?> ResolveContinueWatchingAsync(
        int userId,
        Guid mediaTitleId,
        CancellationToken cancellationToken)
    {
        var entry = await _dbContext.MediaLibraryEntries
            .AsNoTracking()
            .Where(item => item.MediaTitleId == mediaTitleId && item.UserId == userId)
            .Select(item => new
            {
                Title = item.MediaTitle!,
                item.ProgressEpisodes,
                item.Status,
                PreferredReleaseTrack = item.User!.Settings != null
                    ? item.User.Settings.PreferredMediaReleaseTrack
                    : null
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (entry is null)
        {
            return null;
        }

        var nextEpisodeNumber = (entry.ProgressEpisodes ?? 0) + 1;
        if (entry.Status == MediaLibraryStatuses.Completed
            || (entry.Title.EpisodeCount is not null && nextEpisodeNumber > entry.Title.EpisodeCount))
        {
            return new MediaContinueWatchingDto
            {
                Outcome = "completed",
                EpisodeNumber = entry.ProgressEpisodes
            };
        }


        var mapped = await MediaProviderSeasonMappingService.GetMappedEpisodesAsync(_dbContext, [mediaTitleId], cancellationToken);
        var preferredTrack = MediaReleaseTrackPreferences.TryNormalize(entry.PreferredReleaseTrack, out var track)
            ? track : MediaReleaseTrackPreferences.Default;
        var destination = OrderVariants(mapped.Where(item => entry.Title.SupportsEpisodeProgress
                    ? item.EpisodeNumber == nextEpisodeNumber : item.EpisodeNumber is null)
                .Select(item => item.Episode), preferredTrack)
            .Select(MapDestination).OfType<MediaStreamingDestinationDto>().FirstOrDefault();
        if (destination is not null)
        {
            return new MediaContinueWatchingDto
            {
                Outcome = "direct", EpisodeNumber = entry.Title.SupportsEpisodeProgress ? nextEpisodeNumber : null,
                Provider = destination.ServiceId, Url = destination.Url
            };
        }

        destination = (await SelectSeriesDestinationsAsync(entry.Title, mapped, cancellationToken)).FirstOrDefault();
        return new MediaContinueWatchingDto
        {
            Outcome = destination is null ? "unavailable" : "series_fallback",
            EpisodeNumber = entry.Title.SupportsEpisodeProgress ? nextEpisodeNumber : null,
            Provider = destination?.ServiceId, Url = destination?.Url
        };
    }

    private async Task<List<MediaStreamingDestinationDto>> SelectSeriesDestinationsAsync(
        MediaTitle title, IReadOnlyCollection<MappedProviderEpisode> mapped, CancellationToken cancellationToken)
    {
        // Completion-only videos can use a series page without a numbered episode mapping.
        var seasons = MediaProviderSeasonMappingService.IsCompatibleTarget(title, numbered: !title.IsCompletionOnly)
            ? await _dbContext.MediaProviderSeasonMappings.AsNoTracking()
                .Where(item => item.MediaTitleId == title.Id).ToListAsync(cancellationToken)
            : [];
        return mapped.Select(item => item.Episode)
            .Select(item => (item.Provider, Series: item.ProviderSeriesId, item.SeenCount, item.LastSeenAt))
            .Concat(seasons.Select(item => (item.Provider, Series: (string?)item.ProviderSeriesId, SeenCount: 0, LastSeenAt: item.LastVerifiedAt)))
            .GroupBy(item => (item.Provider, item.Series))
            .OrderByDescending(group => group.Sum(item => item.SeenCount))
            .ThenByDescending(group => group.Max(item => item.LastSeenAt))
            .ThenBy(group => group.Key.Series, StringComparer.Ordinal)
            .Select(group =>
            {
                var url = MediaDestinationUrlPolicy.BuildSeriesUrl(group.Key.Provider, group.Key.Series);
                return url is null ? null : new MediaStreamingDestinationDto { ServiceId = group.Key.Provider, Url = url };
            })
            .OfType<MediaStreamingDestinationDto>()
            .DistinctBy(item => item.ServiceId).ToList();
    }

    private static IOrderedEnumerable<MediaProviderEpisode> OrderVariants(
        IEnumerable<MediaProviderEpisode> variants, string preferredTrack)
    {
        var parts = preferredTrack.Split(':', 2);
        var isDub = parts[0] == "dub";
        var language = parts[1];
        var audioLanguage = isDub ? language.Split('-', 2)[0] : "ja";
        return variants
            .OrderBy(item => string.Equals(item.ReleaseTrack, preferredTrack, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenByDescending(item => string.Equals(item.AudioLocale?.Split('-', 2)[0],
                audioLanguage, StringComparison.OrdinalIgnoreCase))
            .ThenBy(item => PreferredLanguageAvailabilityRank(
                isDub ? item.AvailableAudioLanguageCodes : item.AvailableSubtitleLanguageCodes, language))
            .ThenBy(item => !isDub && item.AudioLocale is not null)
            .ThenByDescending(item => item.SeenCount).ThenByDescending(item => item.LastSeenAt)
            .ThenBy(item => item.ProviderEpisodeId, StringComparer.Ordinal);
    }

    private static int PreferredLanguageAvailabilityRank(string[] availableLanguages, string language)
    {
        if (availableLanguages.Any(code => string.Equals(code, language, StringComparison.OrdinalIgnoreCase)
            || code.StartsWith(language + "-", StringComparison.OrdinalIgnoreCase)
            || language.StartsWith(code + "-", StringComparison.OrdinalIgnoreCase)))
        {
            return 0;
        }

        return availableLanguages.Length == 0 ? 1 : 2;
    }

    private static string[] NormalizeLanguageCodes(IEnumerable<string>? values) =>
        values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Where(value => MediaReleaseTrackPreferences.TryNormalize($"sub:{value}", out _))
            .Select(value =>
            {
                MediaReleaseTrackPreferences.TryNormalize($"sub:{value}", out var normalized);
                return normalized[4..];
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

}
