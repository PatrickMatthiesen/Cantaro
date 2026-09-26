import { afterEach, expect, test } from 'bun:test';
import { musicLibraryApi } from '../src/music/services/musicLibraryApi';

const originalFetch = globalThis.fetch;
afterEach(() => { globalThis.fetch = originalFetch; });

test('removes only the selected duplicate playlist occurrence', async () => {
  let requestedUrl = '';
  globalThis.fetch = (async (input) => {
    requestedUrl = String(input);
    return new Response(null, { status: 204 });
  }) as typeof fetch;

  await musicLibraryApi.removeSongFromPlaylist('track:canonical', 'playlist', 'video', 'entry-two');

  expect(requestedUrl).toBe('/api/music/library/playlists/playlist/songs/canonical?youtubeVideoId=video&entryId=entry-two');
});
