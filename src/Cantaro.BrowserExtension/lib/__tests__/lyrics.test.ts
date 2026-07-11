import { beforeEach, describe, expect, it, vi } from 'vitest';

const { ensureExtensionAccessToken, readExtensionConfig } = vi.hoisted(() => ({
  ensureExtensionAccessToken: vi.fn(),
  readExtensionConfig: vi.fn(),
}));

vi.mock('../cantaroAuthSession', () => ({ ensureExtensionAccessToken }));
vi.mock('../extensionRuntimeConfig', () => ({ readExtensionConfig }));

import {
  clearLyricsSessionCache,
  displayLyricsText,
  loadExtensionLyrics,
  type LyricsResult,
} from '../lyrics';

const availableLyrics: LyricsResult = {
  state: 'available',
  matchStatus: 'exact',
  provider: 'LRCLIB',
  plainLyrics: 'Plain line',
  syncedLyrics: '[00:01.00]Timed line\n[00:03.10]Another line',
  attribution: 'Lyrics provided by LRCLIB',
};

describe('extension lyrics client', () => {
  beforeEach(() => {
    clearLyricsSessionCache();
    vi.restoreAllMocks();
    ensureExtensionAccessToken.mockResolvedValue('access-token');
    readExtensionConfig.mockResolvedValue({ apiBaseUrl: 'https://cantaro.test', sessionEmail: 'test@cantaro.local' });
  });

  it('prefers synchronized lyrics and removes LRC timestamps for the popup', () => {
    expect(displayLyricsText(availableLyrics)).toEqual({ text: 'Timed line\nAnother line', synchronized: true });
  });

  it('uses a short-lived in-memory result for the same signed-in user', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(availableLyrics), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    await loadExtensionLyrics('track-id');
    await loadExtensionLyrics('track-id');

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledWith(
      'https://cantaro.test/api/music/tracks/track-id/lyrics',
      expect.objectContaining({ headers: { Authorization: 'Bearer access-token' } }),
    );
  });

  it('does not cache provider failures', async () => {
    const unavailable = { ...availableLyrics, state: 'provider_error', matchStatus: 'provider_error' };
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(new Response(JSON.stringify(unavailable), { status: 200 })));
    vi.stubGlobal('fetch', fetchMock);

    await loadExtensionLyrics('track-id');
    await loadExtensionLyrics('track-id');

    expect(fetchMock).toHaveBeenCalledTimes(2);
  });
});
