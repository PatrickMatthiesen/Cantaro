import { afterEach, describe, expect, it } from 'bun:test';
import { configureMediaApi, mediaApi } from '../../Cantaro.ClientShared/src/media/services/mediaApi';

const originalFetch = globalThis.fetch;

afterEach(() => {
  globalThis.fetch = originalFetch;
  configureMediaApi(() => ({ baseUrl: '', includeCredentials: true }));
});

describe('franchise graph API', () => {
  it('loads the graph from the library entry franchise endpoint', async () => {
    let requestedUrl = '';
    globalThis.fetch = (input) => {
      requestedUrl = String(input);
      return Promise.resolve(Response.json({
        currentMediaTitleId: 'current',
        sourceProvider: 'anilist',
        nodes: [],
        relations: [],
        continuity: {
          orderedMediaTitleIds: [],
          episodeOffsetByMediaTitleId: {},
          isComplete: false,
        },
      }));
    };
    configureMediaApi(() => ({ baseUrl: 'https://cantaro.test/', includeCredentials: true }));

    await mediaApi.getFranchiseGraph('entry / 1');

    expect(requestedUrl).toBe('https://cantaro.test/api/media/library/entry%20%2F%201/franchise');
  });
});

