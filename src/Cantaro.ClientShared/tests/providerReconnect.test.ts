import { expect, test } from 'bun:test';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { ProviderPanelActions, ProviderPanelHeader } from '../src/media/components/media-providers/ProviderPanelSections';

test('a retained unusable account offers reconnect without disconnect or import actions', () => {
  const html = renderToStaticMarkup(createElement(ProviderPanelActions, {
    name: 'MyAnimeList', isLoadingStatus: false, hasStatus: true, isConnected: true, needsReconnect: true,
    onConnect: () => {}, onRetryStatus: async () => {},
    connectedActions: { isImporting: false, isDisconnecting: false, isInitialSyncActive: false,
      onImport: async () => null, onPreviewInitialSync: async () => null, onDisconnect: async () => {} },
  }));
  expect(html).toContain('Reconnect');
  expect(html).not.toContain('Disconnect');
  expect(html).not.toContain('Refresh');
});

test('the provider header reports reconnect rather than connected for a retained account', () => {
  const html = renderToStaticMarkup(createElement(ProviderPanelHeader, {
    name: 'MyAnimeList', icon: null, description: '', isLoadingStatus: false,
    status: { providerId: 'myanimelist', isConnected: true, needsReconnect: true, connectionState: 'reconnect_required' },
  }));
  expect(html).toContain('Reconnect required');
  expect(html).not.toContain('> Connected');
});
