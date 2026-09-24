import { describe, expect, it, vi } from 'vitest';
import type { MusicLibrarySong } from '@cantaro/client-shared/music';
import { createYouTubeLyricsHandler } from './youtubeLyricsHandler';
import type { YouTubeLyricsRequest } from './youtubeLyrics';
import type { LyricsResult } from '../../../app/music/musicLyrics';

const request: YouTubeLyricsRequest = { type: 'music.youtube.lyrics', correlationId: 'test', payload: { videoId: 'qmSiJxEOVGs' } };
const sender = { tab: { id: 1 }, frameId: 0, url: 'https://www.youtube.com/watch?v=qmSiJxEOVGs' } as Browser.runtime.MessageSender;
const song = { id: 'track:known-track', title: 'Known song', artist: 'Known artist', sourceIdentities: [{ source: 'youtube', externalId: 'qmSiJxEOVGs' }] } as MusicLibrarySong;

function setup(songs = [song]) {
  const lyrics: LyricsResult = { state: 'available', matchStatus: 'fallback', plainLyrics: 'Synthetic words', provider: 'lrclib', attribution: 'LRCLIB' };
  const api = { request: vi.fn().mockResolvedValueOnce({ songs }) };
  const consent = vi.fn(async () => 'https://cantaro.test');
  const fetchLyrics = vi.fn(async () => lyrics);
  const getTab = vi.fn(async (_id: number): Promise<{ url?: string }> => ({ url: sender.url }));
  return { api, consent, fetchLyrics, getTab, handler: createYouTubeLyricsHandler(api, consent, fetchLyrics, getTab), lyrics };
}

describe('YouTube lyrics background boundary', () => {
  it('returns only the exact identified song and its lyrics', async () => {
    const { api, handler, fetchLyrics, lyrics } = setup();
    expect(await handler(request, sender)).toEqual({ ok: true, correlationId: 'test', value: { song: { id: song.id, title: song.title, artist: song.artist }, lyrics } });
    expect(api.request.mock.calls).toEqual([['/api/music/library']]);
    expect(fetchLyrics).toHaveBeenCalledWith(song);
  });

  it('searches LRCLIB from user-entered metadata without requesting the Cantaro library', async () => {
    const { api, handler, fetchLyrics, lyrics } = setup();
    const manualRequest: YouTubeLyricsRequest = {
      ...request,
      payload: { ...request.payload, search: { title: '  Unlinked song  ', artist: '  Unlinked artist  ' } },
    };
    expect(await handler(manualRequest, sender)).toEqual({ ok: true, correlationId: 'test', value: {
      song: { id: 'manual', title: 'Unlinked song', artist: 'Unlinked artist' }, lyrics,
    } });
    expect(api.request).not.toHaveBeenCalled();
    expect(fetchLyrics).toHaveBeenCalledWith(expect.objectContaining({
      id: 'manual', title: 'Unlinked song', artist: 'Unlinked artist', albums: [], sourceIdentities: [],
    }));
  });

  it('discards manual search results when consent is revoked during LRCLIB lookup', async () => {
    const { handler, consent, fetchLyrics, api } = setup();
    fetchLyrics.mockImplementationOnce(async () => {
      consent.mockRejectedValueOnce(new Error('Revoked'));
      return { state: 'available', matchStatus: 'fallback', plainLyrics: 'Synthetic words', provider: 'lrclib', attribution: 'LRCLIB' };
    });
    const manualRequest: YouTubeLyricsRequest = {
      ...request,
      payload: { ...request.payload, search: { title: 'Unlinked song', artist: 'Unlinked artist' } },
    };
    expect(await handler(manualRequest, sender)).toMatchObject({ ok: false, error: { code: 'delivery_failed' } });
    expect(api.request).not.toHaveBeenCalled();
  });

  it.each([
    { ...sender, url: 'https://evil.test/watch?v=qmSiJxEOVGs' },
    { ...sender, tab: {} as Browser.tabs.Tab },
    { ...sender, frameId: 1 },
  ])('rejects a mismatched sender before account access', async untrusted => {
    const { handler, api, consent } = setup();
    expect(await handler(request, untrusted)).toMatchObject({ ok: false, error: { code: 'invalid_request' } });
    expect(consent).not.toHaveBeenCalled();
    expect(api.request).not.toHaveBeenCalled();
  });

  it.each(['https://www.youtube.com/watch?v=abcdefghijk', 'https://www.youtube.com/'])('uses the current tab URL after navigation from %s', async oldUrl => {
    const { handler, getTab } = setup();
    expect(await handler(request, { ...sender, url: oldUrl })).toMatchObject({ ok: true });
    expect(getTab).toHaveBeenCalledWith(1);
  });

  it.each(['https://www.youtube.com/watch?v=abcdefghijk', 'https://evil.test/watch?v=qmSiJxEOVGs', undefined])('rejects a current tab URL that does not match: %s', async url => {
    const { handler, getTab, api } = setup();
    getTab.mockResolvedValueOnce({ url });
    expect(await handler(request, sender)).toMatchObject({ ok: false, error: { code: 'invalid_request' } });
    expect(api.request).not.toHaveBeenCalled();
  });

  it('rejects a tab that has closed without accessing account data', async () => {
    const { handler, getTab, api } = setup();
    getTab.mockRejectedValueOnce(new Error('No tab'));
    expect(await handler(request, sender)).toMatchObject({ ok: false });
    expect(api.request).not.toHaveBeenCalled();
  });

  it('does not access the library without consent', async () => {
    const { handler, consent, api } = setup();
    consent.mockRejectedValueOnce(new Error('Denied'));
    expect(await handler(request, sender)).toMatchObject({ ok: false });
    expect(api.request).not.toHaveBeenCalled();
  });

  it('does not request lyrics after consent is revoked during library loading', async () => {
    const { handler, consent, api } = setup();
    consent.mockResolvedValueOnce('https://cantaro.test').mockRejectedValueOnce(new Error('Revoked'));
    expect(await handler(request, sender)).toMatchObject({ ok: false });
    expect(api.request).toHaveBeenCalledTimes(1);
  });

  it('discards results after a server change', async () => {
    const { handler, consent, fetchLyrics } = setup();
    consent.mockResolvedValueOnce('https://cantaro.test').mockResolvedValueOnce('https://other.test');
    expect(await handler(request, sender)).toMatchObject({ ok: false });
    expect(fetchLyrics).not.toHaveBeenCalled();
  });

  it('does not return fetched lyrics after consent is revoked while LRCLIB loads', async () => {
    const { handler, consent } = setup();
    consent.mockResolvedValueOnce('https://cantaro.test').mockResolvedValueOnce('https://cantaro.test').mockRejectedValueOnce(new Error('Revoked'));
    expect(await handler(request, sender)).toMatchObject({ ok: false });
  });

  it.each([{ songs: [] }, { songs: [song, { ...song, id: 'track:other' }] }])('does not guess between missing or conflicting song identities', async ({ songs }) => {
    const { handler, api } = setup(songs);
    expect(await handler(request, sender)).toMatchObject({ ok: true, value: { song: null, lyrics: null } });
    expect(api.request).toHaveBeenCalledTimes(1);
  });
});
