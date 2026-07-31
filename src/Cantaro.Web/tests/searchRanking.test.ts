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
});
