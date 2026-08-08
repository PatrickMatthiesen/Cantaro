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
            EpisodeTitle = item.EpisodeTitle
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
                allowIdentityRemap);
            recordedDestinationCount++;
        }

        return recordedDestinationCount;
    }

    public async Task<MediaEpisodeCatalogDto?> GetEpisodeCatalogAsync(
        int userId,
        Guid libraryEntryId,
        CancellationToken cancellationToken)
    {
        var mediaTitleId = await _dbContext.MediaLibraryEntries
            .AsNoTracking()
            .Where(item => item.Id == libraryEntryId && item.UserId == userId)
            .Select(item => (Guid?)item.MediaTitleId)
            .SingleOrDefaultAsync(cancellationToken);
        if (mediaTitleId is null)
        {
            return null;
        }

        var episodes = await _dbContext.MediaEpisodes
            .AsNoTracking()
            .Where(episode => episode.MediaTitleId == mediaTitleId.Value)
            .Include(episode => episode.ProviderIdentities)
            .OrderBy(episode => episode.EpisodeNumber)
            .ToListAsync(cancellationToken);
        var seriesDestination = SelectSeriesDestination(
            episodes.SelectMany(episode => episode.ProviderIdentities));

        return new MediaEpisodeCatalogDto
        {
            SeriesProvider = seriesDestination?.Provider,
            SeriesUrl = seriesDestination?.Url,
            Episodes = episodes.Select(MapEpisodeDestination).ToList()
        };
    }

    private static MediaEpisodeDestinationDto MapEpisodeDestination(MediaEpisode episode)
    {
        var identities = episode.ProviderIdentities
            .OrderBy(identity => identity.HasConflict)
            .ThenByDescending(identity => identity.SeenCount)
            .ThenByDescending(identity => identity.LastSeenAt)
            .ToList();
        var directIdentity = identities.FirstOrDefault(identity =>
            !identity.HasConflict
            && MediaDestinationUrlPolicy.BuildUrl(identity.Provider, identity.ProviderUrlPath) is not null);

        return new MediaEpisodeDestinationDto
        {
            EpisodeNumber = episode.EpisodeNumber,
            Title = episode.Title,
            Provider = directIdentity?.Provider,
            Url = directIdentity is null
                ? null
                : MediaDestinationUrlPolicy.BuildUrl(directIdentity.Provider, directIdentity.ProviderUrlPath),
            SeenCount = directIdentity?.SeenCount ?? identities.Select(identity => identity.SeenCount).DefaultIfEmpty(0).Max(),
            HasConflict = identities.Any(identity => identity.HasConflict)
        };
    }

    public async Task<MediaContinueWatchingDto?> ResolveContinueWatchingAsync(
        int userId,
        Guid libraryEntryId,
        CancellationToken cancellationToken)
    {
        var entry = await _dbContext.MediaLibraryEntries
            .AsNoTracking()
            .Where(item => item.Id == libraryEntryId && item.UserId == userId)
            .Select(item => new
            {
                item.MediaTitleId,
                item.ProgressEpisodes,
                item.NormalizedStatus,
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

        if (entry.NormalizedStatus == MediaLibraryStatuses.Completed
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

        return SelectSeriesDestination(identities);
    }

    private static (string Provider, string Url)? SelectSeriesDestination(
        IEnumerable<MediaEpisodeProviderIdentity> identities)
    {
        foreach (var candidate in identities
                     .Where(identity => !identity.HasConflict && !string.IsNullOrWhiteSpace(identity.ProviderSeriesId))
                     .GroupBy(identity => new { identity.Provider, identity.ProviderSeriesId })
                     .OrderByDescending(group => group.Sum(identity => identity.SeenCount)))
        {
            var url = MediaDestinationUrlPolicy.BuildSeriesUrl(
                candidate.Key.Provider,
                candidate.Key.ProviderSeriesId);
            if (url is not null)
            {
                return (candidate.Key.Provider, url);
            }
        }

        return null;
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
        bool allowIdentityRemap = false)
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
}
