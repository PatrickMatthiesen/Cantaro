import { describe, expect, test } from 'bun:test';
import { trustedCanonicalRoute } from '../src/search/searchRouting';

describe('search result routes', () => {
  test('allows exact song, playlist, canonical media, and discovery routes', () => {
    const guid = '11890a79-1337-4ab6-8cc7-bec27efb4218';
    expect(trustedCanonicalRoute(`/music/songs/track%3A${guid}`)).toBe(`/music/songs/track%3A${guid}`);
    expect(trustedCanonicalRoute(`/music/songs/observation%3A${guid}`)).toBe(`/music/songs/observation%3A${guid}`);
    expect(trustedCanonicalRoute(`/music/songs/entry%3A${guid}`)).toBe(`/music/songs/entry%3A${guid}`);
    expect(trustedCanonicalRoute(`/music/songs/${guid}`)).toBe(`/music/songs/${guid}`);
    expect(trustedCanonicalRoute(`/music/playlists/${guid}`)).toBe(`/music/playlists/${guid}`);
    expect(trustedCanonicalRoute(`/media/${guid}`)).toBe(`/media/${guid}`);
    expect(trustedCanonicalRoute('/media/catalog/anilist/tv%3A16498')).toBe('/media/catalog/anilist/tv%3A16498');
  });

  test('rejects external, unrelated, and inexact routes', () => {
    expect(trustedCanonicalRoute('https://example.com/media/library/42')).toBeNull();
    expect(trustedCanonicalRoute('//example.com/media/library/42')).toBeNull();
    expect(trustedCanonicalRoute('/settings')).toBeNull();
    expect(trustedCanonicalRoute('/music/artists/11890a79-1337-4ab6-8cc7-bec27efb4218')).toBeNull();
    expect(trustedCanonicalRoute('/music/playlists/not-a-number')).toBeNull();
    expect(trustedCanonicalRoute('/media/library/42')).toBeNull();
    expect(trustedCanonicalRoute(`/media/library/11890a79-1337-4ab6-8cc7-bec27efb4218`)).toBeNull();
    expect(trustedCanonicalRoute('/media/catalog/anilist')).toBeNull();
    expect(trustedCanonicalRoute('/media/catalog/anilist/16498/extra')).toBeNull();
    expect(trustedCanonicalRoute('/media/11890a79-1337-4ab6-8cc7-bec27efb4218?tab=activity')).toBeNull();
  });

  test('rejects dot segments, encoded separators, and double encoding', () => {
    expect(trustedCanonicalRoute('/media/catalog/../16498')).toBeNull();
    expect(trustedCanonicalRoute('/media/catalog/%2e%2e/16498')).toBeNull();
    expect(trustedCanonicalRoute('/media/catalog/anilist%2fadmin/16498')).toBeNull();
    expect(trustedCanonicalRoute('/media/catalog/anilist%5cadmin/16498')).toBeNull();
    expect(trustedCanonicalRoute('/media/catalog/anilist/%252fadmin')).toBeNull();
  });
});
