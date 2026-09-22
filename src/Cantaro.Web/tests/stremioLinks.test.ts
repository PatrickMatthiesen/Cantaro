import { describe, expect, it } from 'bun:test';
import {
  addStremioDestinations,
  buildStremioDetailUrl,
  findStremioDetailUrl,
} from '@cantaro/client-shared/media';
import type { MediaStreamingDestinations } from '@cantaro/client-shared/media';

describe('Stremio title links', () => {
  it('builds the Stremio movie route with its required repeated IMDb id', () => {
    expect(buildStremioDetailUrl({ type: 'movie', id: 'tt1254207' }))
      .toBe('stremio:///detail/movie/tt1254207/tt1254207');
  });

  it('builds a title-level series route and preserves a Kitsu namespace', () => {
    expect(buildStremioDetailUrl({ type: 'series', id: 'kitsu:1' }))
      .toBe('stremio:///detail/series/kitsu:1');
  });

  it('builds a title-level IMDb series route without guessing an episode', () => {
    expect(buildStremioDetailUrl({ type: 'series', id: 'tt0108778' }))
      .toBe('stremio:///detail/series/tt0108778');
  });

  it('rejects malformed targets before they reach the custom URL scheme', () => {
    const malformedTargets = [
      null,
      { type: 'episode', id: 'tt0108778' },
      { type: 'movie', id: 'https://example.com/title' },
      { type: 'movie', id: 'tt1254207?autoplay=1' },
      { type: 'series', id: 'kitsu:1/../../play' },
      { type: 'series', id: 'kitsu:0' },
      { type: 'series', id: 'tt0108778:1:1' },
    ];

    for (const target of malformedTargets) {
      expect(buildStremioDetailUrl(target)).toBeNull();
    }
  });

  it('selects the first valid target when earlier provider data is unusable', () => {
    expect(findStremioDetailUrl([
      null,
      { type: 'series', id: 'kitsu:0' },
      { type: 'series', id: 'kitsu:12' },
      { type: 'movie', id: 'tt1254207' },
    ])).toBe('stremio:///detail/series/kitsu:12');
  });

  it('returns no action URL when provider data has no valid target', () => {
    expect(findStremioDetailUrl([
      undefined,
      { type: 'series', id: 'tt0108778:1:1' },
    ])).toBeNull();
  });

  it('appends title and exact IMDb episode destinations after existing services', () => {
    const destinations = catalogDestinations();

    const result = addStremioDestinations(
      destinations,
      [null, { type: 'series', id: 'tt0108778' }],
      'series',
    );

    expect(result.seriesDestinations.map(item => item.serviceId)).toEqual([
      'crunchyroll',
      'stremio',
    ]);
    expect(result.seriesDestinations[1]?.url)
      .toBe('stremio:///detail/series/tt0108778');
    expect(result.episodes[0]?.destinations.at(-1)).toEqual({
      serviceId: 'stremio',
      displayName: 'Stremio',
      kind: 'episode',
      url: 'stremio:///detail/series/tt0108778/tt0108778:2:3',
    });
    expect(result.episodes[1]?.destinations).toEqual([]);
    expect(destinations.seriesDestinations).toHaveLength(1);
    expect(destinations.episodes[0]?.destinations).toHaveLength(1);
  });

  it('uses the Kitsu title route without guessing overall episode numbers', () => {
    const result = addStremioDestinations(
      catalogDestinations(),
      [{ type: 'series', id: 'kitsu:1' }],
      'anime',
    );

    expect(result.seriesDestinations.at(-1)?.url)
      .toBe('stremio:///detail/series/kitsu:1');
    expect(result.episodes.every(episode =>
      episode.destinations.every(destination => destination.serviceId !== 'stremio'),
    )).toBeTrue();
  });

  it('keeps the repeated movie id and does not add episode routes', () => {
    const result = addStremioDestinations(
      catalogDestinations(),
      [{ type: 'movie', id: 'tt1254207' }],
      'movie',
    );

    expect(result.seriesDestinations.at(-1)?.url)
      .toBe('stremio:///detail/movie/tt1254207/tt1254207');
    expect(result.episodes.every(episode =>
      episode.destinations.every(destination => destination.serviceId !== 'stremio'),
    )).toBeTrue();
  });

  it('uses a mapped IMDb episode even when Kitsu is the first title fallback', () => {
    const result = addStremioDestinations(catalogDestinations(), [
      { type: 'series', id: 'kitsu:6448' },
      { type: 'series', id: 'tt2098220', episodeMapping: { seasonNumber: 1, episodeOffset: 12 } },
    ], 'anime', [1, 1, 59]);

    expect(result.seriesDestinations.at(-1)?.url).toBe('stremio:///detail/series/kitsu:6448');
    expect(result.episodes.find(item => item.episodeNumber === 59)?.destinations.at(-1)?.url)
      .toBe('stremio:///detail/series/tt2098220/tt2098220:1:71');
    expect(result.episodes.filter(item => item.episodeNumber === 1)).toHaveLength(1);
    expect(result.episodes.find(item => item.episodeNumber === 15)?.destinations[0]?.serviceId)
      .toBe('crunchyroll');
  });

  it('uses a Kitsu entry mapping for synthetic anime episodes', () => {
    const result = addStremioDestinations({ seriesDestinations: [], episodes: [] }, [
      { type: 'series', id: 'kitsu:6448', episodeMapping: { seasonNumber: null, episodeOffset: 0 } },
    ], 'anime', [59, -1, 0, 1.5, Number.MAX_SAFE_INTEGER + 1]);

    expect(result.episodes).toHaveLength(1);
    expect(result.episodes[0]?.destinations[0]?.url)
      .toBe('stremio:///detail/series/kitsu:6448/kitsu:6448:59');
    expect(result.episodes[0]?.seasonNumber).toBeUndefined();
  });

  it('does not assume that an anime catalog season uses IMDb numbering', () => {
    const result = addStremioDestinations(catalogDestinations(), [
      { type: 'series', id: 'tt2098220' },
    ], 'anime', [59]);
    expect(result.episodes).toHaveLength(2);
    expect(result.episodes[0]?.destinations).toHaveLength(1);
  });

  it('keeps title fallbacks for invalid or incompatible episode mappings', () => {
    for (const target of [
      { type: 'series', id: 'tt2098220', episodeMapping: { seasonNumber: null, episodeOffset: 0 } },
      { type: 'series', id: 'tt2098220', episodeMapping: { seasonNumber: 1, episodeOffset: -1 } },
      { type: 'series', id: 'kitsu:6448', episodeMapping: { seasonNumber: 1, episodeOffset: 0 } },
      { type: 'series', id: 'kitsu:6448', episodeMapping: { seasonNumber: null, episodeOffset: Number.MAX_SAFE_INTEGER } },
    ]) {
      const result = addStremioDestinations({ seriesDestinations: [], episodes: [] }, [target], 'anime', [1]);
      expect(result.seriesDestinations).toHaveLength(1);
      expect(result.episodes).toHaveLength(0);
    }
  });

  it('rejects malformed targets and media kinds that cannot be watched', () => {
    const destinations = catalogDestinations();

    expect(addStremioDestinations(
      destinations,
      [{ type: 'series', id: 'tt0108778:1:1' }],
      'series',
    )).toBe(destinations);
    expect(addStremioDestinations(
      destinations,
      [{ type: 'series', id: 'tt0108778' }],
      'manga',
    )).toBe(destinations);
  });
});

function catalogDestinations(): MediaStreamingDestinations {
  return {
    seriesDestinations: [{
      serviceId: 'crunchyroll',
      displayName: 'Crunchyroll',
      kind: 'series',
      url: 'https://www.crunchyroll.com/series/SERIES1/show',
    }],
    episodes: [
      {
        episodeNumber: 15,
        seasonNumber: 2,
        seasonEpisodeNumber: 3,
        availableAudioLanguageCodes: [],
        availableSubtitleLanguageCodes: [],
        destinations: [{
          serviceId: 'crunchyroll',
          displayName: 'Crunchyroll',
          kind: 'episode',
          url: 'https://www.crunchyroll.com/watch/EP15/title',
        }],
      },
      {
        episodeNumber: 16,
        availableAudioLanguageCodes: [],
        availableSubtitleLanguageCodes: [],
        destinations: [],
      },
    ],
  };
}
