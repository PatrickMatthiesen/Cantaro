using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

public class MediaEpisodeIdentityService(
    ApplicationDbContext dbContext,
    MediaProviderSeasonMappingService seasonMappingService,
    ILogger<MediaEpisodeIdentityService> logger)
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly MediaProviderSeasonMappingService _seasonMappingService = seasonMappingService;
    private readonly ILogger<MediaEpisodeIdentityService> _logger = logger;
    public async Task RecordObservationAsync(
        MediaObservation observation,
        CancellationToken cancellationToken,
        bool isUserConfirmed = false,
        bool allowTrustedIdentityRemap = false)
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

        var now = DateTimeOffset.UtcNow;
        var hasIdentityConflict = false;
        var canTrustIdentities = isUserConfirmed
            || await WasMatchedByTrustedCandidateAsync(observation, cancellationToken);
        var recordedDestinationCount = observation.IsCatalogObservation
            ? await RecordCatalogEpisodesAsync(
                observation,
                now,
                cancellationToken,
                canTrustIdentities,
                allowTrustedIdentityRemap,
                onConflict: () => hasIdentityConflict = true)
            : observation.Episodes.Count > 0
                ? await RecordRenderedEpisodesAsync(
                    observation,
                    observation.ProviderSeriesId,
                    observation.ProviderSeasonId,
                    observation.SeasonNumber,
                    observation.Episodes.Select(ToObservedProviderEpisode).ToList(),
                    now,
                    cancellationToken,
                    allowIdentityRemap: true,
                    trustIdentities: canTrustIdentities,
                    allowTrustedIdentityRemap: allowTrustedIdentityRemap,
                    onConflict: () => hasIdentityConflict = true)
                : 0;

        if (observation.ResolvedProgress is > 0)
        {
            var recordedWatchDestination = await RecordWatchedEpisodeAsync(
                observation,
                now,
                cancellationToken,
                canTrustIdentities,
                allowTrustedIdentityRemap,
                onConflict: () => hasIdentityConflict = true);
            recordedDestinationCount += recordedWatchDestination ? 1 : 0;
        }

        if (hasIdentityConflict)
        {
            MarkObservationIdentityConflict(observation);
        }

        if (recordedDestinationCount > 0 || hasIdentityConflict)
        {
            if (await WasMatchedByExactProviderEpisodeIdentityAsync(observation, cancellationToken))
            {
                await _seasonMappingService.EstablishAsync(
                    observation.SiteIdentifier,
                    observation.ProviderSeriesId,
                    observation.ProviderSeasonId,
                    observation.SeasonNumber,
                    observation.MediaTitleId.Value,
                    observation.EpisodeOffset ?? 0,
                    MediaProviderSeasonMappingSources.ProviderEpisodeIdentity,
                    1m,
                    cancellationToken);
            }
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<int> RecordCatalogObservationAsync(
        MediaObservation observation,
        CancellationToken cancellationToken)
    {
        if (observation.MatchStatus != MediaObservationStatuses.Matched
            || observation.MediaTitleId is null
            || !observation.IsCatalogObservation)
        {
            return 0;
        }

        var hasIdentityConflict = false;
        var recordedCount = await RecordCatalogEpisodesAsync(
            observation,
            DateTimeOffset.UtcNow,
            cancellationToken,
            await WasMatchedByTrustedCandidateAsync(observation, cancellationToken),
            allowTrustedIdentityRemap: false,
            onConflict: () =>
            {
                hasIdentityConflict = true;
                MarkObservationIdentityConflict(observation);
            });
        if (recordedCount > 0 || hasIdentityConflict)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        return recordedCount;
    }

    private async Task<bool> WasMatchedByExactProviderEpisodeIdentityAsync(
        MediaObservation observation,
        CancellationToken cancellationToken)
    {
        if (observation.AcceptedCandidateId is not { } candidateId)
        {
            return false;
        }

        return await _dbContext.MediaObservationCandidates
            .AnyAsync(candidate => candidate.Id == candidateId
                && candidate.MediaObservationId == observation.Id
                && candidate.IsAccepted
                && candidate.CandidateSource == MediaObservationCandidateSources.ProviderEpisodeIdentityExact,
                cancellationToken);
    }

    private async Task<bool> WasMatchedByTrustedCandidateAsync(
        MediaObservation observation,
        CancellationToken cancellationToken)
    {
        if (observation.AcceptedCandidateId is not { } candidateId)
        {
            return false;
        }

        return await _dbContext.MediaObservationCandidates
            .AnyAsync(candidate => candidate.Id == candidateId
                && candidate.MediaObservationId == observation.Id
                && candidate.IsAccepted
                && (candidate.CandidateSource == MediaObservationCandidateSources.ProviderEpisodeIdentityExact
                    || candidate.CandidateSource == MediaObservationCandidateSources.ProviderSeasonMappingExact),
                cancellationToken);
    }

    private async Task<bool> RecordWatchedEpisodeAsync(
        MediaObservation observation,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        bool trustIdentities,
        bool allowTrustedIdentityRemap,
        Action? onConflict = null)
    {
        var hasConflict = false;
        var providerEpisodeId = FirstNonBlank(observation.SiteMediaId);
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
            observation.EpisodeTitle,
            observation.SiteIdentifier,
            providerEpisodeId,
            providerUrlPath,
            observation.ProviderSeriesId,
            observation.ProviderSeasonId,
            observation.SeasonNumber,
            observation.EpisodeNumber,
            observation.ProviderSequenceNumber,
            now,
            cancellationToken,
            allowIdentityRemap: true,
            isTrusted: trustIdentities,
            allowTrustedIdentityRemap: allowTrustedIdentityRemap,
            onConflict: () =>
            {
                hasConflict = true;
                onConflict?.Invoke();
            },
            releaseTrack: observation.ReleaseTrack);

        if (observation.NextEpisodeProviderId is { Length: > 0 } nextEpisodeId
            && !string.Equals(nextEpisodeId, providerEpisodeId, StringComparison.OrdinalIgnoreCase)
            && MediaDestinationUrlPolicy.TryNormalizePath(
                observation.SiteIdentifier,
                observation.NextEpisodeUrl,
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
                observation.NextEpisodeTitle,
                observation.SiteIdentifier,
                nextEpisodeId,
                nextProviderUrlPath,
                observation.ProviderSeriesId,
                null,
                null,
                observation.NextEpisodeNumber,
                null,
                now,
                cancellationToken,
                allowIdentityRemap: true,
                isTrusted: trustIdentities,
                allowTrustedIdentityRemap: allowTrustedIdentityRemap,
                onConflict: () =>
                {
                    hasConflict = true;
                    onConflict?.Invoke();
                },
                releaseTrack: observation.NextEpisodeReleaseTrack);
        }

        return !hasConflict;
    }

    private Task<int> RecordCatalogEpisodesAsync(
        MediaObservation observation,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        bool trustIdentities,
        bool allowTrustedIdentityRemap,
        Action? onConflict = null)
    {
        var episodes = observation.Episodes.Select(ToObservedProviderEpisode).ToList();

        return RecordRenderedEpisodesAsync(
            observation,
            observation.ProviderSeriesId,
            observation.ProviderSeasonId,
            observation.SeasonNumber,
            episodes,
            now,
            cancellationToken,
            allowIdentityRemap: true,
            trustIdentities: trustIdentities,
            allowTrustedIdentityRemap: allowTrustedIdentityRemap,
            onConflict: onConflict);
    }

    private async Task<int> RecordRenderedEpisodesAsync(
        MediaObservation observation,
        string? providerSeriesId,
        string? providerSeasonId,
        int? seasonNumber,
        IReadOnlyCollection<ObservedProviderEpisodeDto> observedEpisodes,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        bool allowIdentityRemap = false,
        bool trustIdentities = false,
        bool allowTrustedIdentityRemap = false,
        Action? onConflict = null)
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

            var hasConflict = false;
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
                trustIdentities,
                allowTrustedIdentityRemap,
                renderedEpisode.AvailableSubtitleLanguageCodes,
                renderedEpisode.AvailableAudioLanguageCodes,
                renderedEpisode.ReleaseTrack,
                onConflict: () =>
                {
                    hasConflict = true;
                    onConflict?.Invoke();
                });
            if (!hasConflict)
            {
                recordedDestinationCount++;
            }
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
            .Include(episode => episode.ProviderContents)
                .ThenInclude(content => content.Variants)
            .OrderBy(episode => episode.EpisodeNumber)
            .ToListAsync(cancellationToken);
        var seriesDestinations = SelectSeriesDestinations(
            episodes.SelectMany(episode => episode.ProviderContents));

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
        var identities = episode.ProviderContents
            .SelectMany(content => content.Variants.Select(variant => (Content: content, Variant: variant)))
            .OrderBy(item => item.Variant.HasConflict)
            .ThenByDescending(item => item.Variant.SeenCount)
            .ThenByDescending(item => item.Variant.LastSeenAt)
            .ToList();
        var destinations = identities
            .Where(item => !item.Variant.HasConflict)
            .Select(item => MapEpisodeProviderDestination(item.Variant))
            .OfType<MediaStreamingDestinationDto>()
            .ToList();

        return new MediaEpisodeDestinationDto
        {
            EpisodeNumber = episode.EpisodeNumber,
            Title = episode.Title,
            AvailableSubtitleLanguageCodes = episode.AvailableSubtitleLanguageCodes,
            AvailableAudioLanguageCodes = episode.AvailableAudioLanguageCodes,
            Destinations = destinations,
            SeenCount = identities.Select(item => item.Variant.SeenCount).DefaultIfEmpty(0).Max(),
            HasConflict = identities.Any(item => item.Variant.HasConflict)
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
                AudioLocale = identity.AudioLocale,
                ReleaseTrack = identity.ReleaseTrack,
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
                item.MediaTitle.SupportsEpisodeProgress,
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
            .SelectMany(episode => episode.ProviderContents)
            .SelectMany(content => content.Variants)
            .ToListAsync(cancellationToken);
        var preferredTrack = MediaReleaseTrackPreferences.TryNormalize(
            entry.PreferredReleaseTrack,
            out var normalizedTrack)
            ? normalizedTrack
            : MediaReleaseTrackPreferences.Default;

        foreach (var identity in OrderIdentitiesForPreferredTrack(identities, preferredTrack)
                     .Where(identity => !identity.HasConflict))
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
        var identities = await _dbContext.MediaEpisodeProviderContents
            .AsNoTracking()
            .Where(content => content.MediaEpisode!.MediaTitleId == mediaTitleId)
            .Where(content => content.Variants.Any(identity => !identity.HasConflict)
                && content.ProviderSeriesId != null)
            .Include(content => content.Variants)
            .ToListAsync(cancellationToken);

        var destination = SelectSeriesDestinations(identities).FirstOrDefault();
        return destination is null ? null : (destination.ServiceId, destination.Url);
    }

    private static IEnumerable<MediaEpisodeProviderIdentity> OrderIdentitiesForPreferredTrack(
        IEnumerable<MediaEpisodeProviderIdentity> identities,
        string preferredTrack)
    {
        MediaReleaseTrackPreferences.TryNormalize(preferredTrack, out var normalizedTrack);
        var parts = normalizedTrack.Split(':', 2);
        var presentation = parts[0];
        var language = parts[1].Split('-', 2)[0];
        return identities
            .OrderBy(identity => identity.HasConflict)
            .ThenBy(identity => string.Equals(identity.ReleaseTrack, normalizedTrack, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(identity => PreferredTrackRank(identity.AudioLocale, presentation, language))
            .ThenByDescending(identity => identity.SeenCount)
            .ThenByDescending(identity => identity.LastSeenAt);
    }

    private static int PreferredTrackRank(string? audioLocale, string presentation, string language)
    {
        var audioLanguage = audioLocale?.Split('-', 2)[0];
        if (presentation == "dub")
        {
            return string.Equals(audioLanguage, language, StringComparison.OrdinalIgnoreCase) ? 0 : 1;
        }

        if (string.Equals(audioLanguage, "ja", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }
        return audioLanguage is null ? 1 : 2;
    }

    private static IReadOnlyList<MediaStreamingDestinationDto> SelectSeriesDestinations(
        IEnumerable<MediaEpisodeProviderContent> contents)
    {
        return contents
            .Where(content => !string.IsNullOrWhiteSpace(content.ProviderSeriesId))
            .GroupBy(content => new { content.Provider, content.ProviderSeriesId })
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
                        SeenCount = group.SelectMany(content => content.Variants)
                            .Where(variant => !variant.HasConflict)
                            .Sum(variant => variant.SeenCount),
                        FirstSeenAt = group.SelectMany(content => content.Variants)
                            .Where(variant => !variant.HasConflict)
                            .Min(variant => variant.FirstSeenAt),
                        LastSeenAt = group.SelectMany(content => content.Variants)
                            .Where(variant => !variant.HasConflict)
                            .Max(variant => variant.LastSeenAt)
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
        bool isTrusted = false,
        bool allowTrustedIdentityRemap = false,
        IReadOnlyCollection<string>? subtitleLanguageCodes = null,
        IReadOnlyCollection<string>? audioLanguageCodes = null,
        string? releaseTrack = null,
        Action? onConflict = null)
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
        var variantIdentity = MediaProviderVariantIdentities.Parse(normalizedProvider, normalizedEpisodeId);
        var normalizedReleaseTrack = MediaReleaseTrackPreferences.TryNormalize(releaseTrack, out var parsedReleaseTrack)
            ? parsedReleaseTrack
            : null;
        var identity = await _dbContext.MediaEpisodeProviderIdentities
            .Include(item => item.Content)
            .FirstOrDefaultAsync(item =>
                item.Provider == normalizedProvider
                && item.ProviderEpisodeId == normalizedEpisodeId,
                cancellationToken);

        if (identity is null)
        {
            var providerContent = await FindOrCreateProviderContentAsync(
                episode,
                normalizedProvider,
                variantIdentity.ContentKey,
                providerSeriesId,
                providerSeasonId,
                providerSeasonNumber,
                providerEpisodeNumber,
                providerSequenceNumber,
                cancellationToken);
            _dbContext.MediaEpisodeProviderIdentities.Add(new MediaEpisodeProviderIdentity
            {
                Id = Guid.NewGuid(),
                MediaEpisodeProviderContentId = providerContent.Id,
                Provider = normalizedProvider,
                ProviderEpisodeId = normalizedEpisodeId,
                AudioLocale = variantIdentity.AudioLocale,
                ReleaseTrack = normalizedReleaseTrack,
                ProviderUrlPath = providerUrlPath,
                SeenCount = 1,
                FirstSeenAt = now,
                LastSeenAt = now,
                IsTrusted = isTrusted
            });
            return;
        }

        identity.SeenCount += 1;
        identity.LastSeenAt = now;
        var content = identity.Content
            ?? throw new InvalidOperationException("Provider variant content was not loaded.");
        if (identity.HasConflict
            && identity.IsTrusted
            && content.MediaEpisodeId == episode.Id
            && !allowTrustedIdentityRemap)
        {
            onConflict?.Invoke();
            return;
        }

        if (content.MediaEpisodeId != episode.Id)
        {
            // Inferred identities may be repaired when newer observation evidence
            // arrives. User-confirmed or authoritative identities remain fixed
            // during automatic ingestion; surface disagreement as a conflict.
            if (!allowIdentityRemap || identity.IsTrusted && !allowTrustedIdentityRemap)
            {
                identity.HasConflict = true;
                onConflict?.Invoke();
                _logger.LogWarning(
                    "Provider episode {Provider}/{ProviderEpisodeId} conflicted between canonical episodes {ExistingEpisodeId} and {ObservedEpisodeId}.",
                    normalizedProvider,
                    normalizedEpisodeId,
                    content.MediaEpisodeId,
                    episode.Id);
                return;
            }

            _logger.LogInformation(
                "Remapped catalog episode {Provider}/{ProviderEpisodeId} from canonical episode {ExistingEpisodeId} to {ObservedEpisodeId}.",
                normalizedProvider,
                normalizedEpisodeId,
                content.MediaEpisodeId,
                episode.Id);
            content.MediaEpisodeId = episode.Id;
            identity.HasConflict = false;
        }

        // New evidence can clear a conflict on an inferred identity. A trusted
        // identity requires an explicit confirmation before its conflict clears.
        if (content.MediaEpisodeId == episode.Id
            && (allowTrustedIdentityRemap || !identity.IsTrusted))
        {
            identity.HasConflict = false;
        }

        identity.IsTrusted |= isTrusted;

        content.ProviderSeriesId = FirstNonBlank(providerSeriesId, content.ProviderSeriesId);
        content.ProviderSeasonId = FirstNonBlank(providerSeasonId, content.ProviderSeasonId);
        content.ProviderSeasonNumber = providerSeasonNumber ?? content.ProviderSeasonNumber;
        content.ProviderEpisodeNumber = providerEpisodeNumber ?? content.ProviderEpisodeNumber;
        content.ProviderSequenceNumber = providerSequenceNumber ?? content.ProviderSequenceNumber;
        identity.AudioLocale ??= variantIdentity.AudioLocale;
        identity.ReleaseTrack ??= normalizedReleaseTrack;
        identity.ProviderUrlPath = providerUrlPath;
    }

    private static void MarkObservationIdentityConflict(MediaObservation observation)
    {
        foreach (var candidate in observation.Candidates)
        {
            candidate.IsAccepted = false;
        }

        observation.AcceptedCandidateId = null;
        observation.MatchStatus = MediaObservationStatuses.Ambiguous;
        observation.EpisodeOffset = null;
        observation.ResolvedProgress = null;
        observation.ResolutionNotes =
            "A trusted provider episode identity conflicts with this match. Review the observation before accepting it.";
    }

    private async Task<MediaEpisodeProviderContent> FindOrCreateProviderContentAsync(
        MediaEpisode episode,
        string provider,
        string? providerContentKey,
        string? providerSeriesId,
        string? providerSeasonId,
        int? providerSeasonNumber,
        int? providerEpisodeNumber,
        int? providerSequenceNumber,
        CancellationToken cancellationToken)
    {
        var content = await _dbContext.MediaEpisodeProviderContents.FirstOrDefaultAsync(item =>
            item.MediaEpisodeId == episode.Id
            && item.Provider == provider
            && item.ProviderContentKey == providerContentKey,
            cancellationToken);
        if (content is not null)
        {
            return content;
        }

        content = new MediaEpisodeProviderContent
        {
            Id = Guid.NewGuid(),
            MediaEpisodeId = episode.Id,
            Provider = provider,
            ProviderContentKey = providerContentKey,
            ProviderSeriesId = TrimToNull(providerSeriesId),
            ProviderSeasonId = TrimToNull(providerSeasonId),
            ProviderSeasonNumber = providerSeasonNumber,
            ProviderEpisodeNumber = providerEpisodeNumber,
            ProviderSequenceNumber = providerSequenceNumber
        };
        _dbContext.MediaEpisodeProviderContents.Add(content);
        return content;
    }

    private static ObservedProviderEpisodeDto ToObservedProviderEpisode(MediaObservationEpisode episode) => new()
    {
        ProviderEpisodeId = episode.ProviderEpisodeId,
        ProviderUrl = episode.ProviderUrl,
        EpisodeNumber = episode.EpisodeNumber,
        EpisodeTitle = episode.EpisodeTitle,
        ReleaseTrack = episode.ReleaseTrack,
        AvailableSubtitleLanguageCodes = episode.AvailableSubtitleLanguageCodes,
        AvailableAudioLanguageCodes = episode.AvailableAudioLanguageCodes
    };

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
