import { describe, expect, test } from 'bun:test';
import {
  createGlobalSearchState,
  getSearchResultLimit,
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
});
