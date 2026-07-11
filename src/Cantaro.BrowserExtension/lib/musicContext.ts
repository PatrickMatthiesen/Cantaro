import type { MusicLibrarySong } from '@cantaro/client-shared/music';

export type MusicSite = 'youtube' | 'youtube_music';

export interface BrowserMusicContext {
  site: MusicSite;
  externalId: string;
  title?: string;
  artist?: string;
  url: string;
  observedAt: string;
}

export type MusicContextResolution =
  | { status: 'matched'; song: MusicLibrarySong }
  | { status: 'ambiguous'; songs: MusicLibrarySong[] }
  | { status: 'unmatched' };

const normalize = (value?: string) => (value ?? '')
  .normalize('NFKD')
  .replace(/[\u0300-\u036f]/g, '')
  .replace(/\([^)]*(official|audio|video|lyrics)[^)]*\)/gi, '')
  .replace(/[^a-z0-9]+/gi, ' ')
  .trim()
  .toLowerCase();

export function parseYouTubeMusicContext(urlValue: string, title?: string, artist?: string): BrowserMusicContext | null {
  const url = new URL(urlValue);
  if (url.hostname !== 'www.youtube.com' && url.hostname !== 'youtube.com' && url.hostname !== 'music.youtube.com') return null;
  const externalId = url.searchParams.get('v')?.trim();
  if (!externalId || !/^[-_A-Za-z0-9]{6,}$/.test(externalId)) return null;
  return {
    site: url.hostname === 'music.youtube.com' ? 'youtube_music' : 'youtube',
    externalId,
    title: title?.trim() || undefined,
    artist: artist?.trim() || undefined,
    url: url.href,
    observedAt: new Date().toISOString(),
  };
}

export function resolveMusicContext(context: BrowserMusicContext, songs: MusicLibrarySong[]): MusicContextResolution {
  const idMatches = songs.filter((song) => song.sourceIdentities.some((identity) =>
    (identity.source.toLowerCase().includes('youtube')) && identity.externalId === context.externalId));
  if (idMatches.length === 1) return { status: 'matched', song: idMatches[0] };
  if (idMatches.length > 1) return { status: 'ambiguous', songs: idMatches };

  const title = normalize(context.title);
  if (!title) return { status: 'unmatched' };
  const metadataMatches = songs.filter((song) => normalize(song.title) === title
    && (!context.artist || normalize(song.artist) === normalize(context.artist)));
  if (metadataMatches.length === 1) return { status: 'matched', song: metadataMatches[0] };
  if (metadataMatches.length > 1) return { status: 'ambiguous', songs: metadataMatches };
  return { status: 'unmatched' };
}

export const MUSIC_CONTEXT_STORAGE_KEY = 'latestMusicContext';

export async function readActiveTabMusicContext(): Promise<BrowserMusicContext | null> {
  const [tab] = await browser.tabs.query({ active: true, currentWindow: true });
  if (!tab?.url) return null;
  const pageTitle = tab.title?.replace(/\s+-\s+YouTube$/, '').trim();
  return parseYouTubeMusicContext(tab.url, pageTitle);
}
