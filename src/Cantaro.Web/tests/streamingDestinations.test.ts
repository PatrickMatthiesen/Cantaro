import { describe, expect, it } from 'bun:test';
import {
  isAllowedStreamingUrl,
  resolveStreamingDestinations,
  resolveStreamingServiceId,
  STREAMING_SERVICES,
} from '@cantaro/client-shared/media';

describe('streaming service definitions', () => {
  it('resolves provider aliases into the closed service set', () => {
    expect(resolveStreamingServiceId('Crunchyroll')).toBe('crunchyroll');
    expect(resolveStreamingServiceId('Disney+')).toBe('disney-plus');
    expect(resolveStreamingServiceId('Amazon Prime')).toBe('prime-video');
    expect(resolveStreamingServiceId('unknown')).toBeNull();
    expect(STREAMING_SERVICES.crunchyroll.capabilities.episodeDestinations).toBeTrue();
  });

  it('allows only HTTPS destinations on a service-owned host', () => {
    expect(isAllowedStreamingUrl('crunchyroll', 'https://www.crunchyroll.com/watch/EP1')).toBeTrue();
    expect(isAllowedStreamingUrl('crunchyroll', 'https://crunchyroll.com.evil.test/watch/EP1')).toBeFalse();
    expect(isAllowedStreamingUrl('crunchyroll', 'http://crunchyroll.com/watch/EP1')).toBeFalse();
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

  it('does not promote an episode-shaped availability link to a series destination', () => {
    const result = resolveStreamingDestinations([{
      serviceId: 'crunchyroll',
      displayName: 'Crunchyroll',
      url: 'https://www.crunchyroll.com/watch/EP1/one',
      availabilityKind: 'streaming',
    }]);

    expect(result.seriesDestinations).toEqual([]);
  });

  it('keeps the strongest observed identity first for one service', () => {
    const result = resolveStreamingDestinations([], {
      seriesDestinations: [],
      episodes: [{
        episodeNumber: 1,
        seenCount: 4,
        hasConflict: false,
        destinations: [
          { serviceId: 'crunchyroll', url: 'https://www.crunchyroll.com/watch/AAA/one', seenCount: 1, firstSeenAt: '2026-08-01', lastSeenAt: '2026-08-09' },
          { serviceId: 'crunchyroll', url: 'https://www.crunchyroll.com/watch/ZZZ/one', seenCount: 3, firstSeenAt: '2026-08-01', lastSeenAt: '2026-08-08' },
        ],
      }],
    });

    expect(result.episodes[0]?.destinations[0]?.url).toContain('/watch/ZZZ/');
  });

  it('preserves provider-neutral destination arrays under each episode', () => {
    const result = resolveStreamingDestinations([], {
      seriesDestinations: [{
        serviceId: 'crunchyroll',
        url: 'https://www.crunchyroll.com/series/SERIES1/show',
        seenCount: 2,
        firstSeenAt: '2026-08-08T00:00:00Z',
        lastSeenAt: '2026-08-09T00:00:00Z',
      }],
      episodes: [
        {
          episodeNumber: 2,
          seenCount: 1,
          hasConflict: false,
          destinations: [],
        },
        {
          episodeNumber: 1,
          title: 'One',
          seenCount: 2,
          hasConflict: false,
          destinations: [
            {
              serviceId: 'crunchyroll',
              url: 'https://www.crunchyroll.com/watch/EP1/one',
              seenCount: 2,
              firstSeenAt: '2026-08-08T00:00:00Z',
              lastSeenAt: '2026-08-09T00:00:00Z',
            },
          ],
        },
      ],
    }, 'crunchyroll');

    expect(result.seriesDestinations).toHaveLength(1);
    expect(result.episodes.map(item => item.episodeNumber)).toEqual([1, 2]);
    expect(result.episodes[0]?.destinations.map(item => item.serviceId)).toEqual(['crunchyroll']);
    expect(result.episodes[0]?.destinations[0]?.seenCount).toBe(2);
  });
});
