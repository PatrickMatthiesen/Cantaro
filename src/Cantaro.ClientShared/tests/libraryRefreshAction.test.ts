import { expect, test } from 'bun:test';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { LibraryRefreshAction } from '../src/media/components/media-library/LibraryFilterControls';

const actions = { onRefresh: async () => undefined, onReconnect: () => undefined };

test('the library shows pending providers before showing a reconnect warning', () => {
  const result = renderToStaticMarkup(createElement(LibraryRefreshAction, {
    ...actions, canRefresh: true, syncingProviderIds: ['anilist', 'simkl'], reconnectProviderId: 'myanimelist',
  }));

  expect(result).toContain('Syncing AniList…');
  expect(result).toContain('Syncing AniList, SIMKL');
  expect(result).not.toContain('Reconnect MyAnimeList');
});

test('the reconnect warning replaces Reload once provider sync finishes', () => {
  const result = renderToStaticMarkup(createElement(LibraryRefreshAction, {
    ...actions, canRefresh: true, syncingProviderIds: [], reconnectProviderId: 'myanimelist',
  }));

  expect(result).toContain('aria-label="Reconnect MyAnimeList"');
  expect(result).not.toContain('Reload');
});
