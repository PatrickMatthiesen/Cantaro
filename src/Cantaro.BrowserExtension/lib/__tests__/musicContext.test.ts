import { describe, expect, it } from 'vitest';
import { parseYouTubeMusicContext, resolveMusicContext } from '../musicContext';
import type { MusicLibrarySong } from '@cantaro/client-shared/music';

const song = (id: string, externalId: string, title = 'The Song'): MusicLibrarySong => ({ id, title, artist: 'Artist', albums: [], sourcePlatforms: ['youtube'], sourceIdentities: [{ source: 'youtube', externalId }], platformLinks: [], playlists: [] });

describe('YouTube music context', () => {
  it('reads stable video ids from supported routes', () => expect(parseYouTubeMusicContext('https://music.youtube.com/watch?v=abcDEF12345', 'Song')?.externalId).toBe('abcDEF12345'));
  it('ignores unsupported and non-watch pages', () => expect(parseYouTubeMusicContext('https://youtube.com/feed/music')).toBeNull());
  it('prefers an exact source identity', () => expect(resolveMusicContext(parseYouTubeMusicContext('https://youtube.com/watch?v=abcDEF12345')!, [song('1', 'abcDEF12345')]).status).toBe('matched'));
  it('does not choose among duplicate identities', () => expect(resolveMusicContext(parseYouTubeMusicContext('https://youtube.com/watch?v=abcDEF12345')!, [song('1', 'abcDEF12345'), song('2', 'abcDEF12345')]).status).toBe('ambiguous'));
  it('falls back to normalized title and artist metadata', () => expect(resolveMusicContext(parseYouTubeMusicContext('https://youtube.com/watch?v=unknown1', 'The Song (Official Audio)', 'Artist')!, [song('1', 'different')]).status).toBe('matched'));
});
