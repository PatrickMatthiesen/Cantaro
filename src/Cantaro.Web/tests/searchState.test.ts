import { describe, expect, test } from 'bun:test';
import {
  createGlobalSearchState,
  getMobileSearchCloseAction,
  getSearchResultLimit,
  getSearchSurfaceKind,
  readSearchRouteState,
  searchMaxQueryLength,
} from '../src/search/searchState';

describe('search URL state', () => {
  test('top-bar submissions trim the query and return to the all-results group', () => {
    expect(createGlobalSearchState('  massive attack  ')).toEqual({
      q: 'massive attack',
      group: 'all',
    });
  });

  test('route state preserves supported groups and rejects unknown groups', () => {
    expect(readSearchRouteState({ q: 'teardrop', group: 'songs' })).toEqual({
      q: 'teardrop',
      group: 'songs',
    });
    expect(readSearchRouteState({ q: 'teardrop', group: 'unknown' })).toEqual({
      q: 'teardrop',
      group: 'all',
    });
  });

  test('queries are safely limited to the API maximum', () => {
    const query = 'a'.repeat(searchMaxQueryLength + 40);
    expect(readSearchRouteState({ q: query, group: 'all' }).q).toHaveLength(searchMaxQueryLength);
  });

  test('selected groups request the expanded result limit', () => {
    expect(getSearchResultLimit('all')).toBe(6);
    expect(getSearchResultLimit('media')).toBe(20);
  });

  test('mobile search becomes the expanded surface only on the search route', () => {
    expect(getSearchSurfaceKind(false, '/music')).toBe('desktop-preview');
    expect(getSearchSurfaceKind(true, '/music')).toBe('mobile-preview');
    expect(getSearchSurfaceKind(true, '/search')).toBe('mobile-expanded');
  });

  test('mobile preview state is explicit and close uses route history or a safe fallback', () => {
    expect(readSearchRouteState({ q: 'teardrop', group: 'all', preview: 'true' }).preview).toBe(true);
    expect(readSearchRouteState({ q: 'teardrop', group: 'all', preview: 'false' }).preview).toBeUndefined();
    expect(getMobileSearchCloseAction('/search', true)).toBe('back');
    expect(getMobileSearchCloseAction('/search', false)).toBe('fallback');
    expect(getMobileSearchCloseAction('/music', false)).toBe('hide');
  });
});
