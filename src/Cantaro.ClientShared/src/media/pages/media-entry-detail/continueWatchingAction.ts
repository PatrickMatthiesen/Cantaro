import {
  preferEpisodeDestinationsByService,
  type StreamingDestination,
} from '../../services/streamingDestinations';
import type { MediaContinueWatchingDto } from '../../services/mediaApi';
import {
  STREAMING_SERVICE_IDS,
  STREAMING_SERVICES,
  isStreamingDestinationUrl,
  resolveStreamingServiceId,
  type StreamingServiceId,
} from '../../services/streamingServices';
import type { ContinueWatchingState } from './mediaEntryDetailTypes';

export interface ContinueLinkAction {
  serviceId: StreamingServiceId;
  url: string;
  label: string;
  kind: 'episode' | 'series' | 'search';
}

function destinationAction(destination: StreamingDestination): ContinueLinkAction {
  const service = STREAMING_SERVICES[destination.serviceId];
  return {
    serviceId: destination.serviceId,
    url: destination.url,
    label: destination.kind === 'episode'
      ? `Continue on ${service.displayName}`
      : `Open on ${service.displayName}`,
    kind: destination.kind,
  };
}

function resolvedDestination(
  provider: string | undefined,
  url: string | undefined,
  kind: 'series' | 'episode',
): StreamingDestination | null {
  const serviceId = provider ? resolveStreamingServiceId(provider) : null;
  if (!serviceId || !url || !isStreamingDestinationUrl(serviceId, url, kind)) return null;
  return {
    serviceId,
    url,
    kind,
    displayName: STREAMING_SERVICES[serviceId].displayName,
  };
}

function searchFallback(
  canonicalTitle: string,
  preferredServiceId: StreamingServiceId | null,
  disabledProviderIds: readonly string[],
): ContinueLinkAction | null {
  const serviceId = preferredServiceId && !disabledProviderIds.includes(preferredServiceId) && STREAMING_SERVICES[preferredServiceId].searchUrl
    ? preferredServiceId
    : STREAMING_SERVICE_IDS.find(id => !disabledProviderIds.includes(id) && STREAMING_SERVICES[id].searchUrl);
  if (!serviceId) return null;
  const service = STREAMING_SERVICES[serviceId];
  return {
    serviceId,
    url: service.searchUrl!(canonicalTitle),
    label: `Search ${service.displayName}`,
    kind: 'search',
  };
}

function resolvedKind(outcome: MediaContinueWatchingDto['outcome']): 'series' | 'episode' | null {
  if (outcome === 'direct') return 'episode';
  if (outcome === 'series_fallback') return 'series';
  return null;
}

function destinationCandidates(
  value: MediaContinueWatchingDto,
  seriesDestinations: readonly StreamingDestination[],
  episodeDestinations: readonly StreamingDestination[],
): { series: readonly StreamingDestination[]; episodes: readonly StreamingDestination[] } {
  const kind = resolvedKind(value.outcome);
  const serverDestination = kind ? resolvedDestination(value.provider, value.url, kind) : null;
  const episodes = includesNextEpisode(value.outcome) ? episodeDestinations : [];
  return {
    series: prependDestination(serverDestination, 'series', seriesDestinations),
    episodes: prependDestination(serverDestination, 'episode', episodes),
  };
}

function includesNextEpisode(outcome: MediaContinueWatchingDto['outcome']): boolean {
  return outcome === 'direct'
    || outcome === 'series_fallback'
    || outcome === 'unavailable';
}

function prependDestination(
  candidate: StreamingDestination | null,
  kind: StreamingDestination['kind'],
  destinations: readonly StreamingDestination[],
): readonly StreamingDestination[] {
  return candidate?.kind === kind ? [candidate, ...destinations] : destinations;
}

function orderDestinations(
  destinations: readonly StreamingDestination[],
  preferredServiceId: StreamingServiceId | null,
): StreamingDestination[] {
  return [...destinations].sort((left, right) => {
    if (left.serviceId === preferredServiceId) return -1;
    if (right.serviceId === preferredServiceId) return 1;
    return STREAMING_SERVICES[left.serviceId].displayName.localeCompare(
      STREAMING_SERVICES[right.serviceId].displayName,
    );
  });
}

export function getContinueLinkActions(
  state: ContinueWatchingState,
  seriesDestinations: readonly StreamingDestination[],
  episodeDestinations: readonly StreamingDestination[],
  preferredServiceId: StreamingServiceId | null,
  canonicalTitle: string,
  mediaKind: string,
  disabledProviderIds: readonly string[] = [],
): ContinueLinkAction[] {
  if (state.status !== 'loaded') return [];
  const candidates = destinationCandidates(state.value, seriesDestinations, episodeDestinations);
  const destinations = preferEpisodeDestinationsByService(candidates.episodes, candidates.series);
  const actions = orderDestinations(destinations, preferredServiceId)
    .filter(destination => !disabledProviderIds.includes(destination.serviceId))
    .map(destinationAction);
  const fallback = mediaKind.toLowerCase() === 'anime'
    ? searchFallback(canonicalTitle, preferredServiceId, disabledProviderIds)
    : null;
  return actions.length > 0 || !fallback ? actions : [fallback];
}
