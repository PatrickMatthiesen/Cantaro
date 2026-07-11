import type { MusicLibraryResponse, MusicLibrarySong } from '@cantaro/client-shared/music';
import { ensureExtensionAccessToken } from './cantaroAuthSession';
import { readExtensionConfig } from './extensionRuntimeConfig';
import { readActiveTabMusicContext, type BrowserMusicContext } from './musicContext';

export interface MusicRecognitionResult {
  classification: 'music' | 'not_music' | 'uncertain';
  status: string;
  trackId?: string;
  title?: string;
  artist?: string;
  inUserLibrary: boolean;
  song?: MusicLibrarySong;
}

export async function addCanonicalSongToPlaylist(trackId: string, playlistId: string, youtubeVideoId?: string): Promise<void> {
  const config = await readExtensionConfig();
  const token = await ensureExtensionAccessToken(config.apiBaseUrl);
  if (!token) throw new Error('Sign in to add songs to playlists.');
  const query = youtubeVideoId ? `?youtubeVideoId=${encodeURIComponent(youtubeVideoId)}` : '';
  const response = await fetch(`${config.apiBaseUrl}/api/music/library/playlists/${playlistId}/songs/${trackId}${query}`, {
    method: 'POST', headers: { Authorization: `Bearer ${token}` },
  });
  if (!response.ok) { const body = await response.json().catch(() => null) as { error?: string } | null; throw new Error(body?.error ?? 'Could not add this song to the playlist.'); }
}

export async function removeCanonicalSongFromPlaylist(trackId: string, playlistId: string, youtubeVideoId?: string): Promise<void> {
  const config = await readExtensionConfig();
  const token = await ensureExtensionAccessToken(config.apiBaseUrl);
  if (!token) throw new Error('Sign in to edit playlists.');
  const query = youtubeVideoId ? `?youtubeVideoId=${encodeURIComponent(youtubeVideoId)}` : '';
  const response = await fetch(`${config.apiBaseUrl}/api/music/library/playlists/${playlistId}/songs/${trackId}${query}`, {
    method: 'DELETE', headers: { Authorization: `Bearer ${token}` },
  });
  if (!response.ok) { const body = await response.json().catch(() => null) as { error?: string } | null; throw new Error(body?.error ?? 'Could not remove this song from the playlist.'); }
}

export async function recognizeActiveYouTube(context?: BrowserMusicContext | null, signal?: AbortSignal): Promise<MusicRecognitionResult | null> {
  const activeContext = context ?? await readActiveTabMusicContext();
  if (!activeContext) return null;
  const config = await readExtensionConfig();
  const token = await ensureExtensionAccessToken(config.apiBaseUrl);
  if (!token) return null;
  const response = await fetch(`${config.apiBaseUrl}/api/music/recognition/youtube`, {
    method: 'POST', headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
    body: JSON.stringify({ videoId: activeContext.externalId, site: activeContext.site, pageTitle: activeContext.title }),
    signal,
  });
  if (!response.ok) throw new Error('Could not identify the current YouTube page.');
  return response.json() as Promise<MusicRecognitionResult>;
}

export async function loadExtensionMusicLibrary(): Promise<MusicLibraryResponse> {
  const config = await readExtensionConfig();
  const token = await ensureExtensionAccessToken(config.apiBaseUrl);
  if (!token) throw new Error('Sign in to Cantaro to browse your music library.');
  const response = await fetch(`${config.apiBaseUrl}/api/music/library`, {
    headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
  });
  if (!response.ok) throw new Error(response.status === 401 ? 'Your Cantaro session expired. Sign in again.' : 'Could not load your music library.');
  return response.json() as Promise<MusicLibraryResponse>;
}
