import { afterEach, describe, expect, test } from 'bun:test';
import { musicLibraryApi, type LyricsResult } from '@cantaro/client-shared/music';
import { lyricsForDisplay } from '../src/music/musicLyrics';

const originalFetch = globalThis.fetch;

afterEach(() => {
  globalThis.fetch = originalFetch;
});

const availableLyrics: LyricsResult = {
  state: 'available',
  matchStatus: 'exact',
  provider: 'lrclib',
  syncedLyrics: '[00:01.20]First line\n[00:04.00][00:05.00]Second line',
  plainLyrics: 'First line\nSecond line\n\nThird line\nFourth line',
  confidence: 0.98,
  attribution: 'Lyrics provided by LRCLIB',
};

describe('song details lyrics', () => {
  test('loads lyrics for canonical track identifiers using the signed-in web session', async () => {
    let requestedUrl = '';
    let requestedInit: RequestInit | undefined;
    globalThis.fetch = (input, init) => {
      requestedUrl = String(input);
      requestedInit = init;
      return Promise.resolve(Response.json(availableLyrics));
    };

    await expect(musicLibraryApi.getLyrics('track:abc-123')).resolves.toEqual(availableLyrics);

    expect(requestedUrl).toBe('/api/music/tracks/abc-123/lyrics');
    expect(requestedInit?.credentials).toBe('include');
  });

  test('reports a missing canonical song without hiding it as a provider failure', async () => {
    globalThis.fetch = () => Promise.resolve(new Response(null, { status: 404 }));

    await expect(musicLibraryApi.getLyrics('missing-song')).rejects.toThrow('This song is not available in Cantaro.');
  });

  test('prefers plain lyrics so the song page preserves authored stanza breaks', () => {
    expect(lyricsForDisplay(availableLyrics)).toEqual({
      synchronized: false,
      text: 'First line\nSecond line\n\nThird line\nFourth line',
    });
  });

  test('falls back to timed lyrics without consuming structural blank lines', () => {
    expect(lyricsForDisplay({
      ...availableLyrics,
      plainLyrics: undefined,
      syncedLyrics: '[00:01.20]First line\n[00:04.00]\n[00:05.00]Second line',
    })).toEqual({
      synchronized: true,
      text: 'First line\n\nSecond line',
    });
  });

  test('handles empty provider results', () => {
    expect(lyricsForDisplay({ ...availableLyrics, syncedLyrics: ' ', plainLyrics: undefined })).toBeNull();
  });
});
