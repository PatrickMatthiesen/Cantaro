import { expect, test } from 'bun:test';
import type { PlaylistSyncInfo } from '../../Cantaro.ClientShared/src/music/services/syncApi';
import { playlistPlatformAction } from '../src/music/playlistPlatformActions';

function mapping(syncMode?: string, servicePlaylistId = 'original-playlist'): PlaylistSyncInfo {
  return { playlistId: 'cantaro-playlist', name: 'Løb', service: 'youtube', servicePlaylistId, syncMode, lastSyncedAt: null, lastSyncStatus: null };
}

test('an imported source refreshes Cantaro instead of overwriting the source', () => {
  expect(playlistPlatformAction(mapping('import_only'))).toBe('refresh');
  expect(playlistPlatformAction(mapping())).toBe('refresh');
});

test('only a Cantaro-created counterpart receives outgoing changes', () => {
  expect(playlistPlatformAction(mapping('from_cantaro'))).toBe('update');
});

test('an unlinked platform creates a new counterpart', () => {
  expect(playlistPlatformAction(undefined)).toBe('create');
});

test('uncertain creation cannot offer another create or a link to a fake ID', () => {
  expect(playlistPlatformAction(mapping('from_cantaro', 'pending:reservation'))).toBe('review');
});
