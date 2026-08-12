using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

public class MediaEpisodeIdentityService(
    ApplicationDbContext dbContext,
    ILogger<MediaEpisodeIdentityService> logger)
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly ILogger<MediaEpisodeIdentityService> _logger = logger;
    private static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task RecordObservationAsync(
        MediaObservation observation,
        CancellationToken cancellationToken)
    {
        if (observation.MatchStatus != MediaObservationStatuses.Matched
            || observation.MediaTitleId is null
            || !string.Equals(
                observation.SiteIdentifier,
                MediaObservationSiteIdentifiers.Crunchyroll,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var payload = DeserializePayload(observation.RawPayload);
        var catalogPayload = payload is null
            ? DeserializeCatalogPayload(observation.RawPayload)
            : null;
        var now = DateTimeOffset.UtcNow;
        var recordedDestinationCount = payload is not null
            ? await RecordRenderedEpisodesAsync(
                observation,
                payload.ProviderSeriesId,
                payload.ProviderSeasonId,
                payload.SeasonNumber,
                payload.ObservedEpisodes,
                now,
                cancellationToken)
            : catalogPayload is not null
                ? await RecordCatalogEpisodesAsync(observation, catalogPayload, now, cancellationToken)
                : 0;

        if (observation.ResolvedProgress is > 0)
        {
            var recordedWatchDestination = await RecordWatchedEpisodeAsync(
                observation,
                payload,
                now,
                cancellationToken);
            recordedDestinationCount += recordedWatchDestination ? 1 : 0;
        }

        if (recordedDestinationCount > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<int> RecordCatalogObservationAsync(
        MediaObservation observation,
        SubmitMediaCatalogObservationRequest payload,
        CancellationToken cancellationToken)
    {
        if (observation.MatchStatus != MediaObservationStatuses.Matched
            || observation.MediaTitleId is null
            || !string.Equals(observation.SiteIdentifier, payload.Provider, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var recordedCount = await RecordCatalogEpisodesAsync(
            observation,
            payload,
            DateTimeOffset.UtcNow,
            cancellationToken);
        if (recordedCount > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return recordedCount;
    }

    private async Task<bool> RecordWatchedEpisodeAsync(
        MediaObservation observation,
        SubmitMediaObservationRequest? payload,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var providerEpisodeId = FirstNonBlank(payload?.SiteMediaId, observation.SiteMediaId);
        if (providerEpisodeId is null
            || !MediaDestinationUrlPolicy.TryNormalizePath(
                observation.SiteIdentifier,
                observation.ObservedUrl,
                providerEpisodeId,
                out var providerUrlPath))
        {
            _logger.LogWarning(
                "Ignored unsafe episode destination for observation {ObservationId}.",
                observation.Id);
            return false;
        }

        var canonicalEpisodeNumber = observation.ResolvedProgress!.Value;
        await UpsertIdentityAsync(
            observation.MediaTitleId!.Value,
            canonicalEpisodeNumber,
            payload?.EpisodeTitle,
            observation.SiteIdentifier,
            providerEpisodeId,
            providerUrlPath,
            payload?.ProviderSeriesId,
            payload?.ProviderSeasonId,
            payload?.SeasonNumber,
            payload?.EpisodeNumber,
            payload?.ProviderSequenceNumber,
            now,
            cancellationToken);

        if (payload?.NextEpisodeProviderId is { Length: > 0 } nextEpisodeId
            && !string.Equals(nextEpisodeId, providerEpisodeId, StringComparison.OrdinalIgnoreCase)
            && MediaDestinationUrlPolicy.TryNormalizePath(
                observation.SiteIdentifier,
                payload.NextEpisodeUrl,
                nextEpisodeId,
                out var nextProviderUrlPath)
            && await CanRecordNextEpisodeAsync(
                observation.MediaTitleId.Value,
                canonicalEpisodeNumber,
                cancellationToken))
        {
            await UpsertIdentityAsync(
                observation.MediaTitleId.Value,
                canonicalEpisodeNumber + 1,
                payload.NextEpisodeTitle,
                observation.SiteIdentifier,
                nextEpisodeId,
                nextProviderUrlPath,
                payload.ProviderSeriesId,
                null,
                null,
                payload.NextEpisodeNumber,
                null,
                now,
                cancellationToken);
        }

        return true;
    }

    private Task<int> RecordCatalogEpisodesAsync(
        MediaObservation observation,
        SubmitMediaCatalogObservationRequest payload,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var episodes = payload.Episodes.Select(item => new ObservedProviderEpisodeDto
        {
            ProviderEpisodeId = item.ProviderEpisodeId,
            ProviderUrl = item.ProviderUrl,
            EpisodeNumber = item.EpisodeNumber,
            EpisodeTitle = item.EpisodeTitle,
            AvailableSubtitleLanguageCodes = item.AvailableSubtitleLanguageCodes,
            AvailableAudioLanguageCodes = item.AvailableAudioLanguageCodes
        }).ToList();

        return RecordRenderedEpisodesAsync(
            observation,
            payload.ProviderSeriesId,
            payload.ProviderSeasonId,
            payload.SeasonNumber,
            episodes,
            now,
            cancellationToken,
            allowIdentityRemap: true);
    }

    private async Task<int> RecordRenderedEpisodesAsync(
        MediaObservation observation,
        string? providerSeriesId,
        string? providerSeasonId,
        int? seasonNumber,
        IReadOnlyCollection<ObservedProviderEpisodeDto> observedEpisodes,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        bool allowIdentityRemap = false)
    {
        if (observation.MediaTitleId is not { } mediaTitleId
            || observedEpisodes.Count == 0)
        {
            return 0;
        }

        var episodeCount = await _dbContext.MediaTitles
            .Where(title => title.Id == mediaTitleId)
            .Select(title => title.EpisodeCount)
            .SingleAsync(cancellationToken);
        var episodeOffset = observation.EpisodeOffset ?? 0;
        var recordedDestinationCount = 0;

        foreach (var renderedEpisode in observedEpisodes
                     .Where(item => item is not null && !string.IsNullOrWhiteSpace(item.ProviderEpisodeId))
                     .DistinctBy(item => item.ProviderEpisodeId.Trim(), StringComparer.OrdinalIgnoreCase)
                     .Take(MediaCatalogObservationLimits.MaximumEpisodesPerObservation))
        {
            var canonicalEpisodeNumber = renderedEpisode.EpisodeNumber + episodeOffset;
            if (canonicalEpisodeNumber <= 0
                || (episodeCount is not null && canonicalEpisodeNumber > episodeCount)
                || !MediaDestinationUrlPolicy.TryNormalizePath(
                    observation.SiteIdentifier,
                    renderedEpisode.ProviderUrl,
                    renderedEpisode.ProviderEpisodeId,
                    out var providerUrlPath))
            {
                _logger.LogWarning(
                    "Ignored invalid rendered episode destination {ProviderEpisodeId} for observation {ObservationId}.",
                    renderedEpisode.ProviderEpisodeId,
                    observation.Id);
                continue;
            }

            await UpsertIdentityAsync(
                mediaTitleId,
                canonicalEpisodeNumber,
                renderedEpisode.EpisodeTitle,
                observation.SiteIdentifier,
                renderedEpisode.ProviderEpisodeId,
                providerUrlPath,
                providerSeriesId,
                providerSeasonId,
                seasonNumber,
                renderedEpisode.EpisodeNumber,
                null,
                now,
                cancellationToken,
                allowIdentityRemap,
                renderedEpisode.AvailableSubtitleLanguageCodes,
                renderedEpisode.AvailableAudioLanguageCodes);
            recordedDestinationCount++;
        }

        return recordedDestinationCount;
    }

    public async Task<MediaEpisodeCatalogDto?> GetEpisodeCatalogAsync(
        Guid mediaTitleId,
        CancellationToken cancellationToken)
    {
        var title = await _dbContext.MediaTitles
            .AsNoTracking()
            .Where(item => item.Id == mediaTitleId)
            .Select(item => new { item.Id, item.ReleasedCount })
            .SingleOrDefaultAsync(cancellationToken);
        if (title is null)
        {
            return null;
        }

        var episodes = await _dbContext.MediaEpisodes
            .AsNoTracking()
            .Where(episode => episode.MediaTitleId == title.Id)
            .Include(episode => episode.ProviderIdentities)
            .OrderBy(episode => episode.EpisodeNumber)
            .ToListAsync(cancellationToken);
        var seriesDestinations = SelectSeriesDestinations(
            episodes.SelectMany(episode => episode.ProviderIdentities));

        return new MediaEpisodeCatalogDto
        {
            SeriesDestinations = seriesDestinations,
            Episodes = episodes.Select(MapEpisodeDestination).ToList(),
            ReleaseAvailability = BuildReleaseAvailability(
                episodes,
                title.ReleasedCount)
        };
    }

    private static MediaReleaseAvailabilityDto BuildReleaseAvailability(
        IReadOnlyCollection<MediaEpisode> episodes,
        int? maxReleasedEpisodes)
    {
        var languageCodes = episodes
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
                SubReleasedEpisodes = episodes
                    .Where(episode => episode.AvailableSubtitleLanguageCodes.Contains(languageCode, StringComparer.OrdinalIgnoreCase))
                    .Select(episode => (int?)episode.EpisodeNumber)
                    .Max(),
                DubReleasedEpisodes = episodes
                    .Where(episode => episode.AvailableAudioLanguageCodes.Contains(languageCode, StringComparer.OrdinalIgnoreCase))
                    .Select(episode => (int?)episode.EpisodeNumber)
                    .Max()
            }).ToList()
        };
    }

    private static MediaEpisodeDestinationDto MapEpisodeDestination(MediaEpisode episode)
    {
        var identities = episode.ProviderIdentities
            .OrderBy(identity => identity.HasConflict)
            .ThenByDescending(identity => identity.SeenCount)
            .ThenByDescending(identity => identity.LastSeenAt)
            .ToList();
        var destinations = identities
            .Where(identity => !identity.HasConflict)
            .Select(MapEpisodeProviderDestination)
            .OfType<MediaStreamingDestinationDto>()
            .ToList();

        return new MediaEpisodeDestinationDto
        {
            EpisodeNumber = episode.EpisodeNumber,
            Title = episode.Title,
            AvailableSubtitleLanguageCodes = episode.AvailableSubtitleLanguageCodes,
            AvailableAudioLanguageCodes = episode.AvailableAudioLanguageCodes,
            Destinations = destinations,
            SeenCount = identities.Select(identity => identity.SeenCount).DefaultIfEmpty(0).Max(),
            HasConflict = identities.Any(identity => identity.HasConflict)
        };
    }

    private static MediaStreamingDestinationDto? MapEpisodeProviderDestination(
        MediaEpisodeProviderIdentity identity)
    {
        var url = MediaDestinationUrlPolicy.BuildUrl(identity.Provider, identity.ProviderUrlPath);
        return url is null
            ? null
            : new MediaStreamingDestinationDto
            {
                ServiceId = identity.Provider,
                Url = url,
                SeenCount = identity.SeenCount,
                FirstSeenAt = identity.FirstSeenAt,
                LastSeenAt = identity.LastSeenAt
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
                item.MediaTitleId,
                item.ProgressEpisodes,
                item.Status,
                item.MediaTitle!.EpisodeCount,
                item.MediaTitle.SupportsEpisodeProgress
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (entry is null)
        {
            return null;
        }

        var nextEpisodeNumber = (entry.ProgressEpisodes ?? 0) + 1;
        if (!entry.SupportsEpisodeProgress)
        {
            return new MediaContinueWatchingDto { Outcome = "unavailable" };
        }

        if (entry.Status == MediaLibraryStatuses.Completed
            || (entry.EpisodeCount is not null && nextEpisodeNumber > entry.EpisodeCount))
        {
            return new MediaContinueWatchingDto
            {
                Outcome = "completed",
                EpisodeNumber = entry.ProgressEpisodes
            };
        }

        var identities = await _dbContext.MediaEpisodes
            .AsNoTracking()
            .Where(episode =>
                episode.MediaTitleId == entry.MediaTitleId
                && episode.EpisodeNumber == nextEpisodeNumber)
            .SelectMany(episode => episode.ProviderIdentities)
            .ToListAsync(cancellationToken);

        identities = identities
            .OrderBy(identity => identity.HasConflict)
            .ThenByDescending(identity => identity.SeenCount)
            .ThenByDescending(identity => identity.LastSeenAt)
            .ToList();

        foreach (var identity in identities.Where(identity => !identity.HasConflict))
        {
            var url = MediaDestinationUrlPolicy.BuildUrl(identity.Provider, identity.ProviderUrlPath);
            if (url is not null)
            {
                return new MediaContinueWatchingDto
                {
                    Outcome = "direct",
                    EpisodeNumber = nextEpisodeNumber,
                    Provider = identity.Provider,
                    Url = url
                };
            }
        }

        if (identities.Any(identity => identity.HasConflict))
        {
            return new MediaContinueWatchingDto
            {
                Outcome = "conflict",
                EpisodeNumber = nextEpisodeNumber
            };
        }

        var seriesDestination = await FindSeriesDestinationAsync(
            entry.MediaTitleId,
            cancellationToken);
        if (seriesDestination is not null)
        {
            return new MediaContinueWatchingDto
            {
                Outcome = "series_fallback",
                EpisodeNumber = nextEpisodeNumber,
                Provider = seriesDestination.Value.Provider,
                Url = seriesDestination.Value.Url
            };
        }

        return new MediaContinueWatchingDto
        {
            Outcome = "unavailable",
            EpisodeNumber = nextEpisodeNumber
        };
    }

    private async Task<(string Provider, string Url)?> FindSeriesDestinationAsync(
        Guid mediaTitleId,
        CancellationToken cancellationToken)
    {
        var identities = await _dbContext.MediaEpisodes
            .AsNoTracking()
            .Where(episode => episode.MediaTitleId == mediaTitleId)
            .SelectMany(episode => episode.ProviderIdentities)
            .Where(identity => !identity.HasConflict && identity.ProviderSeriesId != null)
            .ToListAsync(cancellationToken);

        var destination = SelectSeriesDestinations(identities).FirstOrDefault();
        return destination is null ? null : (destination.ServiceId, destination.Url);
    }

    private static IReadOnlyList<MediaStreamingDestinationDto> SelectSeriesDestinations(
        IEnumerable<MediaEpisodeProviderIdentity> identities)
    {
        return identities
            .Where(identity => !identity.HasConflict && !string.IsNullOrWhiteSpace(identity.ProviderSeriesId))
            .GroupBy(identity => new { identity.Provider, identity.ProviderSeriesId })
            .Select(group =>
            {
                var url = MediaDestinationUrlPolicy.BuildSeriesUrl(
                    group.Key.Provider,
                    group.Key.ProviderSeriesId);
                return url is null
                    ? null
                    : new MediaStreamingDestinationDto
                    {
                        ServiceId = group.Key.Provider,
                        Url = url,
                        SeenCount = group.Sum(identity => identity.SeenCount),
                        FirstSeenAt = group.Min(identity => identity.FirstSeenAt),
                        LastSeenAt = group.Max(identity => identity.LastSeenAt)
                    };
            })
            .OfType<MediaStreamingDestinationDto>()
            .GroupBy(destination => destination.ServiceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(destination => destination.SeenCount)
                .ThenByDescending(destination => destination.LastSeenAt)
                .First())
            .OrderByDescending(destination => destination.SeenCount)
            .ThenBy(destination => destination.ServiceId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<bool> CanRecordNextEpisodeAsync(
        Guid mediaTitleId,
        int currentEpisodeNumber,
        CancellationToken cancellationToken)
    {
        var episodeCount = await _dbContext.MediaTitles
            .Where(title => title.Id == mediaTitleId)
            .Select(title => title.EpisodeCount)
            .SingleAsync(cancellationToken);

        return episodeCount is null || currentEpisodeNumber < episodeCount;
    }

    private async Task UpsertIdentityAsync(
        Guid mediaTitleId,
        int canonicalEpisodeNumber,
        string? episodeTitle,
        string provider,
        string providerEpisodeId,
        string providerUrlPath,
        string? providerSeriesId,
        string? providerSeasonId,
        int? providerSeasonNumber,
        int? providerEpisodeNumber,
        int? providerSequenceNumber,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        bool allowIdentityRemap = false,
        IReadOnlyCollection<string>? subtitleLanguageCodes = null,
        IReadOnlyCollection<string>? audioLanguageCodes = null)
    {
        var episode = await _dbContext.MediaEpisodes
            .FirstOrDefaultAsync(item =>
                item.MediaTitleId == mediaTitleId
                && item.EpisodeNumber == canonicalEpisodeNumber,
                cancellationToken);
        if (episode is null)
        {
            episode = new MediaEpisode
            {
                Id = Guid.NewGuid(),
                MediaTitleId = mediaTitleId,
                EpisodeNumber = canonicalEpisodeNumber,
                Title = TrimToNull(episodeTitle),
                AvailableSubtitleLanguageCodes = NormalizeLanguageCodes(subtitleLanguageCodes),
                AvailableAudioLanguageCodes = NormalizeLanguageCodes(audioLanguageCodes),
                CreatedAt = now,
                UpdatedAt = now
            };
            _dbContext.MediaEpisodes.Add(episode);
        }
        else if (episode.Title is null && !string.IsNullOrWhiteSpace(episodeTitle))
        {
            episode.Title = episodeTitle.Trim();
            episode.UpdatedAt = now;
        }


        var mergedSubtitleLanguageCodes = MergeLanguageCodes(
            episode.AvailableSubtitleLanguageCodes,
            subtitleLanguageCodes);
        var mergedAudioLanguageCodes = MergeLanguageCodes(
            episode.AvailableAudioLanguageCodes,
            audioLanguageCodes);
        if (!episode.AvailableSubtitleLanguageCodes.SequenceEqual(mergedSubtitleLanguageCodes)
            || !episode.AvailableAudioLanguageCodes.SequenceEqual(mergedAudioLanguageCodes))
        {
            episode.AvailableSubtitleLanguageCodes = mergedSubtitleLanguageCodes;
            episode.AvailableAudioLanguageCodes = mergedAudioLanguageCodes;
            episode.UpdatedAt = now;
        }

        var normalizedProvider = provider.Trim().ToLowerInvariant();
        var normalizedEpisodeId = string.Equals(
            normalizedProvider,
            MediaObservationSiteIdentifiers.Crunchyroll,
            StringComparison.Ordinal)
            ? providerEpisodeId.Trim().ToUpperInvariant()
            : providerEpisodeId.Trim();
        var identity = await _dbContext.MediaEpisodeProviderIdentities
            .FirstOrDefaultAsync(item =>
                item.Provider == normalizedProvider
                && item.ProviderEpisodeId == normalizedEpisodeId,
                cancellationToken);

        if (identity is null)
        {
            _dbContext.MediaEpisodeProviderIdentities.Add(new MediaEpisodeProviderIdentity
            {
                Id = Guid.NewGuid(),
                MediaEpisodeId = episode.Id,
                Provider = normalizedProvider,
                ProviderSeriesId = TrimToNull(providerSeriesId),
                ProviderSeasonId = TrimToNull(providerSeasonId),
                ProviderEpisodeId = normalizedEpisodeId,
                ProviderSeasonNumber = providerSeasonNumber,
                ProviderEpisodeNumber = providerEpisodeNumber,
                ProviderSequenceNumber = providerSequenceNumber,
                ProviderUrlPath = providerUrlPath,
                SeenCount = 1,
                FirstSeenAt = now,
                LastSeenAt = now
            });
            return;
        }

        identity.SeenCount += 1;
        identity.LastSeenAt = now;
        if (identity.MediaEpisodeId != episode.Id)
        {
            if (!allowIdentityRemap)
            {
                identity.HasConflict = true;
                _logger.LogWarning(
                    "Provider episode {Provider}/{ProviderEpisodeId} conflicted between canonical episodes {ExistingEpisodeId} and {ObservedEpisodeId}.",
                    normalizedProvider,
                    normalizedEpisodeId,
                    identity.MediaEpisodeId,
                    episode.Id);
                return;
            }

            _logger.LogInformation(
                "Remapped catalog episode {Provider}/{ProviderEpisodeId} from canonical episode {ExistingEpisodeId} to {ObservedEpisodeId}.",
                normalizedProvider,
                normalizedEpisodeId,
                identity.MediaEpisodeId,
                episode.Id);
            identity.MediaEpisodeId = episode.Id;
            identity.HasConflict = false;
        }

        identity.ProviderSeriesId = FirstNonBlank(providerSeriesId, identity.ProviderSeriesId);
        identity.ProviderSeasonId = FirstNonBlank(providerSeasonId, identity.ProviderSeasonId);
        identity.ProviderSeasonNumber = providerSeasonNumber ?? identity.ProviderSeasonNumber;
        identity.ProviderEpisodeNumber = providerEpisodeNumber ?? identity.ProviderEpisodeNumber;
        identity.ProviderSequenceNumber = providerSequenceNumber ?? identity.ProviderSequenceNumber;
        identity.ProviderUrlPath = providerUrlPath;
    }

    private static SubmitMediaObservationRequest? DeserializePayload(string? rawPayload)
    {
        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SubmitMediaObservationRequest>(rawPayload, PayloadJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static SubmitMediaCatalogObservationRequest? DeserializeCatalogPayload(string? rawPayload)
    {
        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SubmitMediaCatalogObservationRequest>(rawPayload, PayloadJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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

    private static string[] MergeLanguageCodes(
        IEnumerable<string> existing,
        IEnumerable<string>? observed) =>
        NormalizeLanguageCodes(existing.Concat(observed ?? []));
}
