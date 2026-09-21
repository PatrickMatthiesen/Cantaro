import { describe, expect, it } from 'bun:test';
import {
  buildStremioDetailUrl,
  findStremioDetailUrl,
} from '@cantaro/client-shared/media';

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
});
