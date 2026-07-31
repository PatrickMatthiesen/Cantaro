import { afterEach, describe, expect, test } from 'bun:test';
import { searchCantaro } from '../src/search/searchApi';

const originalFetch = globalThis.fetch;

afterEach(() => {
  globalThis.fetch = originalFetch;
});

function successfulSearchResponse(): Response {
  return Response.json({
    query: 'teardrop',
    groups: {
      songs: { status: 'ok', items: [], hasMore: false },
      artists: { status: 'unavailable', items: [], hasMore: false },
      playlists: { status: 'ok', items: [], hasMore: false },
      media: { status: 'ok', items: [], hasMore: false },
    },
  });
}

describe('search API parameters', () => {
  test('preview searches omit provider discovery', async () => {
    let requestedUrl = '';
    globalThis.fetch = (input) => {
      requestedUrl = String(input);
      return Promise.resolve(successfulSearchResponse());
    };

    await searchCantaro('teardrop', { limitPerGroup: 3 });

    expect(requestedUrl).toContain('limitPerGroup=3');
    expect(requestedUrl).not.toContain('includeDiscovery');
  });

  test('expanded searches explicitly request provider discovery', async () => {
    let requestedUrl = '';
    globalThis.fetch = (input) => {
      requestedUrl = String(input);
      return Promise.resolve(successfulSearchResponse());
    };

    await searchCantaro('teardrop', { includeDiscovery: true, limitPerGroup: 20 });

    expect(requestedUrl).toContain('limitPerGroup=20');
    expect(requestedUrl).toContain('includeDiscovery=true');
  });
});
