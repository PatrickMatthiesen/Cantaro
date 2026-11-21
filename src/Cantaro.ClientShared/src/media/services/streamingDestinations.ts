import type {
  MediaEpisodeCatalogDto,
  MediaProviderAvailabilityLinkDto,
  MediaStreamingDestinationDto,
} from './mediaApi.types';
import {
  isStreamingDestinationUrl,
  resolveStreamingServiceId,
  STREAMING_SERVICES,
  type StreamingServiceDefinition,
  type StreamingServiceId,
} from './streamingServices';

export type StreamingDestinationKind = 'series' | 'episode';

export interface StreamingDestination {
  serviceId: StreamingServiceId;
  url: string;
  kind: StreamingDestinationKind;
  displayName: string;
  audioLocale?: string;
  releaseTrack?: string;
  availableAudioLanguageCodes?: string[];
  availableSubtitleLanguageCodes?: string[];
}

export interface EpisodeStreamingDestinations {
  episodeNumber: number;
  seasonNumber?: number;
  seasonEpisodeNumber?: number;
  title?: string;
  availableAudioLanguageCodes: string[];
  availableSubtitleLanguageCodes: string[];
  destinations: StreamingDestination[];
}

export interface MediaStreamingDestinations {
  seriesDestinations: StreamingDestination[];
  episodes: EpisodeStreamingDestinations[];
}

export function preferEpisodeDestinationsByService(
  episodeDestinations: readonly StreamingDestination[],
  seriesDestinations: readonly StreamingDestination[],
): StreamingDestination[] {
  const serviceIds = new Set([
    ...episodeDestinations.map(destination => destination.serviceId),
    ...seriesDestinations.map(destination => destination.serviceId),
  ]);
  return [...serviceIds]
    .map(serviceId => episodeDestinations.find(destination => destination.serviceId === serviceId)
      ?? seriesDestinations.find(destination => destination.serviceId === serviceId))
    .filter((destination): destination is StreamingDestination => destination !== undefined);
}

type DestinationSource = Partial<MediaStreamingDestinationDto> & {
  serviceId: string;
  url?: string;
};

export function resolveStreamingDestinations(
  availabilityLinks: readonly MediaProviderAvailabilityLinkDto[],
  episodeCatalog?: MediaEpisodeCatalogDto | null,
  preferredServiceId?: StreamingServiceId | null,
): MediaStreamingDestinations {
  return {
    seriesDestinations: resolveSeriesDestinations(availabilityLinks, episodeCatalog, preferredServiceId),
    episodes: resolveEpisodeDestinations(episodeCatalog, preferredServiceId),
  };
}

function resolveSeriesDestinations(
  availabilityLinks: readonly MediaProviderAvailabilityLinkDto[],
  catalog: MediaEpisodeCatalogDto | null | undefined,
  preferredServiceId: StreamingServiceId | null | undefined,
): StreamingDestination[] {
  const availability = availabilityLinks
    .filter(link => link.availabilityKind.toLowerCase() === 'streaming')
    .map(link => destination(
      { serviceId: link.serviceId || link.displayName, url: link.url },
      'series',
    ));
  const observed = (catalog?.seriesDestinations ?? []).map(item => destination(item, 'series'));
  const standalone = compact((catalog?.standaloneDestinations ?? []).map(item => destination(item, 'episode')));
  const standaloneServices = new Set(standalone.map(item => item.serviceId));
  const series = compact([...observed, ...availability]).filter(item => !standaloneServices.has(item.serviceId));
  return normalizeAndOrder([...standalone, ...series], preferredServiceId);
}

function resolveEpisodeDestinations(
  catalog: MediaEpisodeCatalogDto | null | undefined,
  preferredServiceId: StreamingServiceId | null | undefined,
): EpisodeStreamingDestinations[] {
  return (catalog?.episodes ?? []).map(episode => ({
    episodeNumber: episode.episodeNumber,
    seasonNumber: episode.seasonNumber ?? undefined,
    seasonEpisodeNumber: episode.seasonEpisodeNumber ?? undefined,
    title: episode.title,
    availableAudioLanguageCodes: episode.availableAudioLanguageCodes ?? [],
    availableSubtitleLanguageCodes: episode.availableSubtitleLanguageCodes ?? [],
    // Episode actions select one URL per service after applying the audio filter.
    destinations: orderDestinations(
      compact(episode.destinations.map(item => destination(item, 'episode'))),
      preferredServiceId,
    ),
  })).sort((left, right) => left.episodeNumber - right.episodeNumber);
}

export function normalizeAndOrder(
  destinations: readonly StreamingDestination[],
  preferredServiceId?: StreamingServiceId | null,
): StreamingDestination[] {
  const seen = new Set<string>();
  return orderDestinations(destinations, preferredServiceId).filter(item => {
    const key = item.serviceId;
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}

function orderDestinations(
  destinations: readonly StreamingDestination[],
  preferredServiceId?: StreamingServiceId | null,
): StreamingDestination[] {
  return destinations
    .filter(item => isStreamingDestinationUrl(item.serviceId, item.url, item.kind))
    .sort((left, right) => compareDestinations(left, right, preferredServiceId));
}

function compareDestinations(
  left: StreamingDestination,
  right: StreamingDestination,
  preferredServiceId?: StreamingServiceId | null,
): number {
  return preferenceRank(left.serviceId, preferredServiceId) - preferenceRank(right.serviceId, preferredServiceId)
    || left.displayName.localeCompare(right.displayName);
}

function preferenceRank(serviceId: StreamingServiceId, preferredServiceId?: StreamingServiceId | null): number {
  return serviceId === preferredServiceId ? 0 : 1;
}

function destination(
  source: DestinationSource,
  kind: StreamingDestinationKind,
): StreamingDestination | null {
  if (!source.url) return null;
  const serviceId = resolveStreamingServiceId(source.serviceId);
  if (!serviceId) return null;
  const url = normalizeProviderUrl(serviceId, source.url);
  if (!url) return null;
  if (!isStreamingDestinationUrl(serviceId, url, kind)) return null;
  const definition = STREAMING_SERVICES[serviceId];
  if (!supportsDestinationKind(definition, kind)) return null;

  return {
    serviceId,
    url: normalizeUrl(url),
    kind,
    displayName: definition.displayName,
    audioLocale: source.audioLocale ?? undefined,
    releaseTrack: source.releaseTrack ?? undefined,
    availableAudioLanguageCodes: source.availableAudioLanguageCodes,
    availableSubtitleLanguageCodes: source.availableSubtitleLanguageCodes,
  };
}

function supportsDestinationKind(
  definition: StreamingServiceDefinition,
  kind: StreamingDestinationKind,
): boolean {
  return kind === 'series'
    ? definition.capabilities.seriesDestinations
    : definition.capabilities.episodeDestinations;
}

function normalizeProviderUrl(serviceId: StreamingServiceId, value: string): string | null {
  try {
    const url = new URL(value);
    const hostname = url.hostname.toLowerCase();
    const isOwnedHost = STREAMING_SERVICES[serviceId].allowedHosts.some(
      allowed => hostname === allowed || hostname.endsWith(`.${allowed}`),
    );
    if (url.protocol === 'http:' && !url.port && isOwnedHost) {
      url.protocol = 'https:';
    }
    return url.href;
  } catch {
    return null;
  }
}

function compact<T>(values: readonly (T | null)[]): T[] {
  return values.filter((value): value is T => value !== null);
}

function normalizeUrl(value: string): string {
  const url = new URL(value);
  url.hash = '';
  return url.href;
}
