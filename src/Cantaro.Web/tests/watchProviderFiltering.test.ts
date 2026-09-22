import { describe, expect, it } from 'bun:test';
import { filterWatchProviders } from '../../Cantaro.ClientShared/src/media/services/watchProviderVisibility';
import { getContinueLinkActions } from '../../Cantaro.ClientShared/src/media/pages/media-entry-detail/continueWatchingAction';
import type { MediaStreamingDestinations } from '../../Cantaro.ClientShared/src/media/services/streamingDestinations';
import { STREAMING_SERVICE_IDS } from '@cantaro/client-shared/media';

const destinations: MediaStreamingDestinations = {
  seriesDestinations: [
    { serviceId: 'netflix', displayName: 'Netflix', kind: 'series', url: 'https://www.netflix.com/title/1' },
    { serviceId: 'crunchyroll', displayName: 'Crunchyroll', kind: 'series', url: 'https://www.crunchyroll.com/series/ONE' },
  ],
  episodes: [{
    episodeNumber: 11, seasonNumber: 2, seasonEpisodeNumber: 1, title: 'The return',
    availableAudioLanguageCodes: ['ja'], availableSubtitleLanguageCodes: ['en'],
    destinations: [{ serviceId: 'crunchyroll', displayName: 'Crunchyroll', kind: 'episode', url: 'https://www.crunchyroll.com/watch/ONE' }],
  }],
};

describe('watch provider filtering', () => {
  it('shows all providers until disabled', () => {
    expect(filterWatchProviders(destinations)).toEqual(destinations);
  });

  it('hides title and episode links without removing episode identity or release data', () => {
    const filtered = filterWatchProviders(destinations, ['crunchyroll']);
    expect(filtered.seriesDestinations.map(item => item.serviceId)).toEqual(['netflix']);
    expect(filtered.episodes).toEqual([{ ...destinations.episodes[0], destinations: [] }]);
    expect(destinations.episodes[0].destinations).toHaveLength(1);
  });

  it('does not resurrect disabled providers through server continue-watching results', () => {
    const actions = getContinueLinkActions(
      { status: 'loaded', value: { outcome: 'direct', provider: 'crunchyroll', url: 'https://www.crunchyroll.com/watch/ONE' } },
      destinations.seriesDestinations, destinations.episodes[0].destinations,
      'crunchyroll', 'Example', 'anime', ['crunchyroll'],
    );
    expect(actions.map(item => item.serviceId)).toEqual(['netflix']);
  });

  it('does not resurrect disabled providers through search fallbacks', () => {
    expect(getContinueLinkActions(
      { status: 'loaded', value: { outcome: 'unavailable' } }, [], [],
      'crunchyroll', 'Example', 'anime', STREAMING_SERVICE_IDS,
    )).toEqual([]);
  });
});
