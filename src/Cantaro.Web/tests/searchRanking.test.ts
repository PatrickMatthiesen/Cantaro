import { describe, expect, test } from 'bun:test';
import type { SearchResponse, SearchResultItem } from '../src/search/searchApi';
import { rankSearchResults } from '../src/search/searchRanking';

function item(entityType: SearchResultItem['entityType'], id: string, title: string): SearchResultItem {
  return {
    entityType,
    id,
    title,
    canonicalRoute: entityType === 'media'
      ? `/media/catalog/anilist/${id}`
      : `/music/songs/${id}`,
  };
}

function response(overrides: Partial<SearchResponse['groups']>): SearchResponse {
  const group = { status: 'ok' as const, items: [], hasMore: false };
  return {
    query: 'that time i',
    groups: {
      songs: group,
      artists: { ...group, status: 'unavailable' },
      playlists: group,
      media: group,
      ...overrides,
    },
  };
}

describe('mixed search result ranking', () => {
  test('surfaces the strongest title match regardless of result type', () => {
    const searchResponse = response({
      songs: {
        status: 'ok',
        items: [item('song', 'song-one', 'A Song from That Time')],
        hasMore: false,
      },
      media: {
        status: 'ok',
        items: [item('media', 'media-one', 'That Time I Got Reincarnated as a Slime')],
        hasMore: false,
      },
    });

    expect(rankSearchResults(searchResponse, ['songs', 'artists', 'playlists', 'media'], 'that time i'))
      .toEqual([
        { groupId: 'media', item: searchResponse.groups.media.items[0] },
        { groupId: 'songs', item: searchResponse.groups.songs.items[0] },
      ]);
  });

  test('keeps backend ordering when matches have equal relevance', () => {
    const searchResponse = response({
      media: {
        status: 'ok',
        items: [
          item('media', 'first', 'That Time I First'),
          item('media', 'second', 'That Time I Second'),
        ],
        hasMore: false,
      },
    });

    expect(rankSearchResults(searchResponse, ['media'], 'that time i').map(({ item: result }) => result.id))
      .toEqual(['first', 'second']);
  });

  test('lets one result type fill the shared preview budget', () => {
    const searchResponse = response({
      media: {
        status: 'ok',
        items: Array.from({ length: 8 }, (_, index) => (
          item('media', `season-${index + 1}`, `That Time I Season ${index + 1}`)
        )),
        hasMore: true,
      },
    });

    expect(rankSearchResults(
      searchResponse,
      ['songs', 'artists', 'playlists', 'media'],
      'that time i',
      8,
    )).toHaveLength(8);
  });

  test('recognizes query terms separated by words in the title', () => {
    const seasonThree = item('media', 'season-3', 'That Time I Got Reincarnated as a Slime Season 3');
    const searchResponse = response({
      media: { status: 'ok', items: [seasonThree], hasMore: false },
    });

    expect(rankSearchResults(searchResponse, ['media'], 'that time i 3')[0]?.item)
      .toEqual(seasonThree);
  });
});
