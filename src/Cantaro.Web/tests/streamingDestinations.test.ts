import { describe, expect, it } from 'bun:test';
import {
  isAllowedStreamingUrl,
  resolveStreamingDestinations,
  resolveStreamingServiceId,
  STREAMING_SERVICES,
} from '@cantaro/client-shared/media';
import { getContinueLinkActions } from '../../Cantaro.ClientShared/src/media/pages/media-entry-detail/continueWatchingAction';
import { preferEpisodeDestinationsByService } from '../../Cantaro.ClientShared/src/media/services/streamingDestinations';

describe('streaming service definitions', () => {
  it('resolves provider aliases into the closed service set', () => {
    expect(resolveStreamingServiceId('Crunchyroll')).toBe('crunchyroll');
    expect(resolveStreamingServiceId('Disney+')).toBe('disney-plus');
    expect(resolveStreamingServiceId('Amazon Prime')).toBe('prime-video');
    expect(resolveStreamingServiceId('Stremio')).toBe('stremio');
    expect(resolveStreamingServiceId('unknown')).toBeNull();
    expect(STREAMING_SERVICES.crunchyroll.capabilities.episodeDestinations).toBeTrue();
    expect(STREAMING_SERVICES.stremio.capabilities.episodeDestinations).toBeTrue();
  });

  it('allows only HTTPS destinations on a service-owned host', () => {
    expect(isAllowedStreamingUrl('crunchyroll', 'https://www.crunchyroll.com/watch/EP1')).toBeTrue();
    expect(isAllowedStreamingUrl('crunchyroll', 'https://crunchyroll.com.evil.test/watch/EP1')).toBeFalse();
    expect(isAllowedStreamingUrl('crunchyroll', 'http://crunchyroll.com/watch/EP1')).toBeFalse();
  });

  it('does not accept imported URLs for the generated-only Stremio service', () => {
    expect(STREAMING_SERVICES.stremio.allowedHosts).toEqual([]);
    expect(isAllowedStreamingUrl('stremio', 'stremio:///detail/series/tt0108778')).toBeFalse();
    expect(isAllowedStreamingUrl('stremio', 'https://web.stremio.com/#/detail/series/tt0108778')).toBeFalse();
  });
});

describe('streaming destination resolution', () => {
  it('normalizes, deduplicates, validates, and puts the preferred service first', () => {
    const result = resolveStreamingDestinations([
      { serviceId: 'netflix', displayName: 'Netflix', url: 'https://www.netflix.com/title/1', availabilityKind: 'streaming' },
      { serviceId: 'crunchyroll', displayName: 'Crunchyroll', url: 'https://www.crunchyroll.com/series/SERIES1/show', availabilityKind: 'streaming' },
      { serviceId: 'Crunchyroll', displayName: 'Crunchyroll', url: 'https://www.crunchyroll.com/series/SERIES1/show#episodes', availabilityKind: 'streaming' },
      { serviceId: 'crunchyroll', displayName: 'Crunchyroll', url: 'https://example.com/series/SERIES1', availabilityKind: 'streaming' },
      { serviceId: 'unknown', displayName: 'Unknown', url: 'https://unknown.test/show', availabilityKind: 'streaming' },
    ], null, 'netflix');

    expect(result.seriesDestinations.map(item => item.serviceId)).toEqual(['netflix', 'crunchyroll']);
  });

  it('prefers the catalog series destination for each service', () => {
    const result = resolveStreamingDestinations([
      { serviceId: 'crunchyroll', displayName: 'Crunchyroll', url: 'https://www.crunchyroll.com/series/OLDER/show', availabilityKind: 'streaming' },
    ], {
      seriesDestinations: [{
        serviceId: 'crunchyroll',
        url: 'https://www.crunchyroll.com/series/STRONGER/show',
      }],
      episodes: [],
      specials: [],
    });

    expect(result.seriesDestinations).toHaveLength(1);
    expect(result.seriesDestinations[0]?.url).toContain('/series/STRONGER/');
  });

  it('does not promote an episode-shaped availability link to a series destination', () => {
    const result = resolveStreamingDestinations([{
      serviceId: 'crunchyroll',
      displayName: 'Crunchyroll',
      url: 'https://www.crunchyroll.com/watch/EP1/one',
      availabilityKind: 'streaming',
    }]);

    expect(result.seriesDestinations).toEqual([]);
  });

  it('prefers a standalone movie watch link over a series fallback from the same service', () => {
    const url = 'https://www.crunchyroll.com/watch/MOVIESUB/title';
    const result = resolveStreamingDestinations([
      { serviceId: 'netflix', displayName: 'Netflix', url: 'https://www.netflix.com/title/1', availabilityKind: 'streaming' },
    ], {
      standaloneDestinations: [
        { serviceId: 'crunchyroll', url, releaseTrack: 'sub:en' },
        { serviceId: 'crunchyroll', url: 'https://www.crunchyroll.com/watch/MOVIEDUB/title', releaseTrack: 'dub:en' },
      ],
      seriesDestinations: [{
        serviceId: 'crunchyroll',
        url: 'https://www.crunchyroll.com/series/SERIES1/title',
      }],
      episodes: [],
      specials: [],
    }, 'crunchyroll');

    expect(result.episodes).toEqual([]);
    expect(result.seriesDestinations.map(item => item.url)).toEqual([url, 'https://www.netflix.com/title/1']);
    expect(getContinueLinkActions(
      { status: 'loaded', value: { outcome: 'unavailable' } },
      result.seriesDestinations,
      [],
      'crunchyroll',
      'Title',
      'movie',
    )[0]).toMatchObject({ kind: 'episode', url });
  });

  it('upgrades a trusted legacy Crunchyroll series link to HTTPS', () => {
    const result = resolveStreamingDestinations([{
      serviceId: 'Crunchyroll',
      displayName: 'Crunchyroll',
      url: 'http://www.crunchyroll.com/one-piece',
      availabilityKind: 'streaming',
    }]);

    expect(result.seriesDestinations).toHaveLength(1);
    expect(result.seriesDestinations[0]?.url).toBe('https://www.crunchyroll.com/one-piece');
  });

  it('keeps the API language ranking for opaque episode and movie destinations', () => {
    const preferredUrl = 'https://www.crunchyroll.com/watch/G31UXQKKG';
    const destinations = [
      { url: preferredUrl, availableAudioLanguageCodes: ['en'], availableSubtitleLanguageCodes: ['de'] },
      { url: 'https://www.crunchyroll.com/watch/GQ8BRAPFQ', availableAudioLanguageCodes: [], availableSubtitleLanguageCodes: [] },
      { url: 'https://www.crunchyroll.com/watch/GRQW9GW7R', availableAudioLanguageCodes: ['ja'], availableSubtitleLanguageCodes: ['en'] },
    ].map(item => ({
      ...item,
      serviceId: 'crunchyroll',
      audioLocale: null,
      releaseTrack: null,
    }));
    const result = resolveStreamingDestinations([], {
      seriesDestinations: [],
      standaloneDestinations: destinations,
      specials: [],
      episodes: [{
        episodeNumber: 1,
        availableSubtitleLanguageCodes: ['en'],
        availableAudioLanguageCodes: ['ja', 'en'],
        destinations,
      }],
    });

    expect(preferEpisodeDestinationsByService(
      result.episodes[0]?.destinations ?? [],
      result.seriesDestinations,
    ).map(action => action.url)).toEqual([preferredUrl]);
    expect(getContinueLinkActions(
      { status: 'loaded', value: { outcome: 'unavailable' } },
      result.seriesDestinations,
      [],
      'crunchyroll',
      'Title',
      'movie',
    ).map(action => action.url)).toEqual([preferredUrl]);
    expect(result.episodes[0]?.availableSubtitleLanguageCodes).toEqual(['en']);
    expect(result.episodes[0]?.availableAudioLanguageCodes).toEqual(['ja', 'en']);
  });

  it('preserves provider-neutral destination arrays under each episode', () => {
    const result = resolveStreamingDestinations([], {
      seriesDestinations: [{
        serviceId: 'crunchyroll',
        url: 'https://www.crunchyroll.com/series/SERIES1/show',
      }],
      episodes: [
        {
          episodeNumber: 2,
          destinations: [],
        },
        {
          episodeNumber: 1,
          title: 'One',
          availableSubtitleLanguageCodes: ['en'],
          availableAudioLanguageCodes: ['en'],
          destinations: [
            {
              serviceId: 'crunchyroll',
              url: 'https://www.crunchyroll.com/watch/EP1/one',
            },
          ],
        },
      ],
      specials: [],
    }, 'crunchyroll');

    expect(result.seriesDestinations).toHaveLength(1);
    expect(result.episodes.map(item => item.episodeNumber)).toEqual([1, 2]);
    expect(result.episodes[0]?.destinations.map(item => item.serviceId)).toEqual(['crunchyroll']);
    expect(result.episodes[0]?.availableSubtitleLanguageCodes).toEqual(['en']);
    expect(result.episodes[0]?.availableAudioLanguageCodes).toEqual(['en']);
  });
});
