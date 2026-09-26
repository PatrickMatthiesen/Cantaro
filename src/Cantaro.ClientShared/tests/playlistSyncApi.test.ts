import { afterEach, describe, expect, test } from 'bun:test';
import { playlistSyncApi } from '../src/music/services/playlistSyncApi';

const originalFetch = globalThis.fetch;
afterEach(() => { globalThis.fetch = originalFetch; });

describe('playlist sync requests', () => {
  test('previews same-name playlists before creating with the returned token', async () => {
    const calls: Array<{ url: string; method: string; body: unknown }> = [];
    globalThis.fetch = (async (input, init) => {
      calls.push({ url: String(input), method: init?.method ?? 'GET', body: JSON.parse(String(init?.body)) });
      return Response.json(calls.length === 1
        ? { candidates: [{ servicePlaylistId: 'remote', name: 'Coding', remoteTrackCount: 12, sharedTrackCount: 4, linkedPlaylistId: null, comparisonError: null }], previewToken: 'reviewed-create' }
        : { playlistId: 'cantaro', links: [] });
    }) as typeof fetch;

    const preview = await playlistSyncApi.previewCreateLink('cantaro', 'spotify');
    expect(preview.candidates[0].sharedTrackCount).toBe(4);
    await playlistSyncApi.createLink('cantaro', 'spotify', preview.previewToken);
    expect(calls).toEqual([
      { url: '/api/music/playlists/cantaro/sync/links/create/preview', method: 'POST', body: { service: 'spotify' } },
      { url: '/api/music/playlists/cantaro/sync/links/create', method: 'POST', body: { service: 'spotify', previewToken: 'reviewed-create' } },
    ]);
  });

  test('previews an existing playlist without submitting an attachment', async () => {
    const calls: Array<{ url: string; method: string; body: unknown }> = [];
    globalThis.fetch = (async (input, init) => {
      calls.push({ url: String(input), method: init?.method ?? 'GET', body: JSON.parse(String(init?.body)) });
      return Response.json({ service: 'spotify', servicePlaylistId: 'remote', additions: 3, removals: 2, previewToken: 'reviewed' });
    }) as typeof fetch;

    const preview = await playlistSyncApi.previewAttach('cantaro', 'spotify', 'remote');

    expect(calls).toEqual([{ url: '/api/music/playlists/cantaro/sync/attach/preview', method: 'POST', body: { service: 'spotify', servicePlaylistId: 'remote' } }]);
    expect(preview.previewToken).toBe('reviewed');
  });

  test('passes both initial and ongoing choices with the preview token', async () => {
    let body: unknown;
    globalThis.fetch = (async (_input, init) => {
      body = JSON.parse(String(init?.body));
      return Response.json({ playlistId: 'cantaro', links: [] });
    }) as typeof fetch;

    await playlistSyncApi.attach('cantaro', {
      service: 'youtube', servicePlaylistId: 'remote', syncMode: 'import_only', initialMode: 'combine', previewToken: 'reviewed',
    });

    expect(body).toEqual({ service: 'youtube', servicePlaylistId: 'remote', syncMode: 'import_only', initialMode: 'combine', previewToken: 'reviewed' });
  });

  test('sends each disconnect choice in one request and surfaces stale review errors', async () => {
    let body: unknown;
    globalThis.fetch = (async (_input, init) => {
      body = JSON.parse(String(init?.body));
      return Response.json({ error: 'Linked playlists changed. Review again.' }, { status: 409 });
    }) as typeof fetch;

    await expect(playlistSyncApi.disconnect('youtube', [
      { mappingId: 'one', deleteRemote: false, deleteCanonicalIfLast: false },
      { mappingId: 'two', deleteRemote: true, deleteCanonicalIfLast: true },
    ])).rejects.toThrow('Linked playlists changed. Review again.');

    expect(body).toEqual({ links: [
      { mappingId: 'one', deleteRemote: false, deleteCanonicalIfLast: false },
      { mappingId: 'two', deleteRemote: true, deleteCanonicalIfLast: true },
    ] });
  });
});
