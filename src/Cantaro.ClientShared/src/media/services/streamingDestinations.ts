import type {
  MediaEpisodeCatalogDto,
  MediaProviderAvailabilityLinkDto,
  MediaStreamingDestinationDto,
} from './mediaApi.types';
import {
  isStreamingDestinationUrl,
  resolveStreamingServiceId,
  STREAMING_SERVICES,
  type StreamingServiceId,
} from './streamingServices';

export type StreamingDestinationKind = 'series' | 'episode';

export interface StreamingDestination {
  serviceId: StreamingServiceId;
  url: string;
  kind: StreamingDestinationKind;
  displayName: string;
  seenCount?: number;
  firstSeenAt?: string;
  lastSeenAt?: string;
}

export interface EpisodeStreamingDestinations {
  episodeNumber: number;
  title?: string;
  destinations: StreamingDestination[];
}

export interface MediaStreamingDestinations {
  seriesDestinations: StreamingDestination[];
  episodes: EpisodeStreamingDestinations[];
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
  return normalizeAndOrder(compact([...availability, ...observed]), preferredServiceId);
}

function resolveEpisodeDestinations(
  catalog: MediaEpisodeCatalogDto | null | undefined,
  preferredServiceId: StreamingServiceId | null | undefined,
): EpisodeStreamingDestinations[] {
  return (catalog?.episodes ?? []).map(episode => ({
    episodeNumber: episode.episodeNumber,
    title: episode.title,
    destinations: normalizeAndOrder(
      compact(episode.destinations.map(item => destination(item, 'episode'))),
      preferredServiceId,
    ),
  })).sort((left, right) => left.episodeNumber - right.episodeNumber);
}

export function normalizeAndOrder(
  destinations: readonly StreamingDestination[],
  preferredServiceId?: StreamingServiceId | null,
): StreamingDestination[] {
  const ordered = destinations
    .filter(item => isStreamingDestinationUrl(item.serviceId, item.url, item.kind))
    .sort((left, right) => compareDestinations(left, right, preferredServiceId));
  const seen = new Set<string>();
  return ordered.filter(item => {
    const key = `${item.serviceId}:${normalizeUrl(item.url)}`;
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}

function compareDestinations(
  left: StreamingDestination,
  right: StreamingDestination,
  preferredServiceId?: StreamingServiceId | null,
): number {
  const comparisons = [
    preferenceRank(left.serviceId, preferredServiceId) - preferenceRank(right.serviceId, preferredServiceId),
    left.displayName.localeCompare(right.displayName),
    (right.seenCount ?? 0) - (left.seenCount ?? 0),
    compareTimestampDescending(left.lastSeenAt, right.lastSeenAt),
    left.url.localeCompare(right.url),
  ];
  return comparisons.find(value => value !== 0) ?? 0;
}

function preferenceRank(serviceId: StreamingServiceId, preferredServiceId?: StreamingServiceId | null): number {
  return serviceId === preferredServiceId ? 0 : 1;
}

function compareTimestampDescending(left?: string, right?: string): number {
  return (right ? Date.parse(right) : 0) - (left ? Date.parse(left) : 0);
}

function destination(
  source: DestinationSource,
  kind: StreamingDestinationKind,
): StreamingDestination | null {
  if (!source.url) return null;
  const serviceId = resolveStreamingServiceId(source.serviceId);
  if (!serviceId || !isStreamingDestinationUrl(serviceId, source.url, kind)) return null;
  const definition = STREAMING_SERVICES[serviceId];
  const supported = kind === 'series'
    ? definition.capabilities.seriesDestinations
    : definition.capabilities.episodeDestinations;
  return supported ? {
    serviceId,
    url: normalizeUrl(source.url),
    kind,
    displayName: definition.displayName,
    seenCount: source.seenCount,
    firstSeenAt: source.firstSeenAt,
    lastSeenAt: source.lastSeenAt,
  } : null;
}

function compact<T>(values: readonly (T | null)[]): T[] {
  return values.filter((value): value is T => value !== null);
}

function normalizeUrl(value: string): string {
  const url = new URL(value);
  url.hash = '';
  return url.href;
}
