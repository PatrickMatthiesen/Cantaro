import { afterEach, describe, expect, it, vi } from 'vitest';
import type { MusicLibrarySong } from '@cantaro/client-shared/music';
import { getLocalLyrics, rankLrclibCandidates } from './musicLyricsProvider';

const song = {
  id: 'track:maybe-idk', title: 'Maybe IDK', artist: 'Jon Bellion', albums: ['The Human Condition'], durationSeconds: 233,
} as MusicLibrarySong;

const records = [
  { id: 41, trackName: 'Maybe IDK', artistName: 'Jon Bellion', albumName: 'The Human Condition', duration: 237, plainLyrics: 'another synthetic version' },
  { id: 57, trackName: 'Maybe IDK', artistName: 'Jon Bellion', albumName: 'The Human Condition', duration: 233, syncedLyrics: '[00:01.00]same synthetic words' },
  { id: 12, trackName: 'Maybe IDK', artistName: 'Jon Bellion', albumName: 'The Human Condition', duration: 233, plainLyrics: 'different synthetic words' },
  { id: 90, trackName: 'Maybe IDK Live', artistName: 'Jon Bellion', albumName: 'Live', duration: 233, plainLyrics: 'different synthetic words' },
  { id: 91, trackName: 'Maybe IDK', artistName: 'Another Artist', albumName: 'The Human Condition', duration: 233, plainLyrics: 'different synthetic words' },
  { id: 92, trackName: 'Maybe IDK', artistName: 'Jon Bellion', albumName: 'The Human Condition', duration: 233 },
];

afterEach(() => vi.unstubAllGlobals());

describe('local LRCLIB lyrics lookup', () => {
  it('searches without an artist and preserves distinguishable artist choices', async () => {
    const fetchMock = vi.fn(async (_input: RequestInfo | URL) => new Response(JSON.stringify(records), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);
    const result = await getLocalLyrics({ ...song, artist: '', albums: [], durationSeconds: undefined });
    expect(new URL(String(fetchMock.mock.calls[0]?.[0])).searchParams.has('artist_name')).toBe(false);
    expect(result.candidates?.some(candidate => candidate.label.startsWith('Another Artist '))).toBe(true);
    expect(result.candidates?.some(candidate => candidate.label.startsWith('Jon Bellion '))).toBe(true);
    expect(result.candidates?.some(candidate => candidate.providerRecordId === '90')).toBe(false);
  });

  it('ranks the best match first and keeps other acceptable versions selectable', () => {
    const candidates = rankLrclibCandidates(song, records);
    expect(candidates.map(candidate => candidate.providerRecordId)).toEqual(['57', '12', '41']);
    expect(candidates[0]?.syncedLyrics).toContain('same synthetic words');
    expect(candidates.every(candidate => candidate.state === 'available')).toBe(true);
  });

  it('keeps only the best ranked record for lyric text that differs by timestamps, case, punctuation, or spacing', () => {
    const candidates = rankLrclibCandidates(song, [
      ...records,
      { id: 61, trackName: 'Maybe IDK', artistName: 'Jon Bellion', albumName: 'Compilation', duration: 233, plainLyrics: ' SAME, synthetic—words! ' },
    ]);
    expect(candidates.map(candidate => candidate.providerRecordId)).toEqual(['57', '12', '41']);
  });

  it('keeps different lyric word order and repetitions as separate versions', () => {
    const candidates = rankLrclibCandidates(song, [
      ...records,
      { id: 62, trackName: 'Maybe IDK', artistName: 'Jon Bellion', albumName: 'Compilation', duration: 233, plainLyrics: 'synthetic same words' },
      { id: 63, trackName: 'Maybe IDK', artistName: 'Jon Bellion', albumName: 'Compilation', duration: 233, plainLyrics: 'same synthetic words words' },
    ]);
    expect(candidates.map(candidate => candidate.providerRecordId)).toEqual(['57', '12', '41', '62', '63']);
  });

  it('deduplicates instrumental records separately from lyric versions', () => {
    const candidates = rankLrclibCandidates(song, [
      ...records,
      { id: 101, trackName: 'Maybe IDK', artistName: 'Jon Bellion', albumName: 'Instrumental', duration: 233, instrumental: true },
      { id: 102, trackName: 'Maybe IDK', artistName: 'Jon Bellion', albumName: 'Instrumental alt', duration: 233, instrumental: true },
    ]);
    expect(candidates.map(candidate => candidate.providerRecordId)).toEqual(['57', '12', '41', '101']);
    expect(candidates[3]?.state).toBe('instrumental');
  });

  it('queries LRCLIB with only song metadata and returns the ranked default plus alternatives', async () => {
    const fetchMock = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => new Response(JSON.stringify(records), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);
    const result = await getLocalLyrics(song);
    const url = new URL(String(fetchMock.mock.calls[0]?.[0]));
    expect(url.origin).toBe('https://lrclib.net');
    expect(url.pathname).toBe('/api/search');
    expect(url.searchParams.get('track_name')).toBe(song.title);
    expect(url.searchParams.get('artist_name')).toBe(song.artist);
    expect(url.searchParams.has('album_name')).toBe(false);
    expect(url.searchParams.has('duration')).toBe(false);
    expect(url.search).not.toContain(song.id);
    expect(result.providerRecordId).toBe('57');
    expect(result.candidates?.map(candidate => candidate.providerRecordId)).toEqual(['57', '12', '41']);
  });

  it('forwards cancellation to the provider request', async () => {
    const fetchMock = vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
      controller.abort();
      expect(init?.signal?.aborted).toBe(true);
      return new Response('[]', { status: 200 });
    });
    vi.stubGlobal('fetch', fetchMock);
    const controller = new AbortController();
    await getLocalLyrics(song, controller.signal);
    expect(fetchMock).toHaveBeenCalledOnce();
  });
});
