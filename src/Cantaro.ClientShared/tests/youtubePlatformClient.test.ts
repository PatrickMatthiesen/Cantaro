import { afterEach, expect, test } from 'bun:test';
import { youtubePlatformClient, isYouTubeReconnectRequiredError } from '../src/music/platforms/clients/youtubePlatformClient';
import { createPlatformApiError, isPlatformReconnectRequiredError } from '../src/music/platforms/clients/platformApiError';

const originalFetch = globalThis.fetch;

afterEach(() => {
  globalThis.fetch = originalFetch;
  youtubePlatformClient.clearCache();
});

function reconnectResponse(code: string) {
  return new Response(JSON.stringify({ code, error: 'Reconnect YouTube to continue.' }), {
    status: 409,
    headers: { 'Content-Type': 'application/json' },
  });
}

test.each(['platform_reconnect_required', 'youtube_reconnect_required'])(
  'playlist request recognizes %s', async (code) => {
    globalThis.fetch = (async () => reconnectResponse(code)) as typeof fetch;

    try {
      await youtubePlatformClient.refreshPlaylists();
      throw new Error('Expected reconnect error');
    } catch (error) {
      expect(isYouTubeReconnectRequiredError(error)).toBe(true);
      expect(isPlatformReconnectRequiredError(error)).toBe(true);
      expect((error as Error).message).toBe('Reconnect YouTube to continue.');
    }
  },
);

test('playlist songs recognize the generic reconnect code', async () => {
  globalThis.fetch = (async () => reconnectResponse('platform_reconnect_required')) as typeof fetch;

  try {
    await youtubePlatformClient.songs('playlist-1');
    throw new Error('Expected reconnect error');
  } catch (error) {
    expect(isYouTubeReconnectRequiredError(error)).toBe(true);
  }
});

test('a reconnect code for another platform stays an ordinary API error', async () => {
  const error = await createPlatformApiError('youtube', reconnectResponse('spotify_reconnect_required'), 'Fallback');

  expect(isYouTubeReconnectRequiredError(error)).toBe(false);
  expect(error.code).toBe('spotify_reconnect_required');
});
