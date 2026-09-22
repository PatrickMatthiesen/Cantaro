import type { MediaStreamingDestinations } from './streamingDestinations';

export function filterWatchProviders(
  destinations: MediaStreamingDestinations,
  disabledProviderIds: readonly string[] = [],
): MediaStreamingDestinations {
  const visible = (destination: { serviceId: string }) => !disabledProviderIds.includes(destination.serviceId);
  return {
    seriesDestinations: destinations.seriesDestinations.filter(visible),
    episodes: destinations.episodes.map(episode => ({
      ...episode,
      destinations: episode.destinations.filter(visible),
    })),
  };
}
