import type { MusicLibraryResponse, MusicLibrarySong } from '@cantaro/client-shared/music';
import { ApiError } from '../../platform/api/apiError';
import { cantaroApiClient } from '../../platform/api/cantaroApiClient';
import { browserSettingsRepository } from '../../platform/settings/settingsRepository';
import type { LyricsResult } from './musicLyrics';

const lyricsCache = new Map<string, LyricsResult>();

function youtubeQuery(youtubeVideoId?: string) {
  return youtubeVideoId ? `?youtubeVideoId=${encodeURIComponent(youtubeVideoId)}` : '';
}

export async function loadMusicLibrary(): Promise<MusicLibraryResponse> {
  return cantaroApiClient.request('/api/music/library');
}

export async function addSongToPlaylist(trackId: string, playlistId: string, youtubeVideoId?: string) {
  await cantaroApiClient.request(`/api/music/library/playlists/${encodeURIComponent(playlistId)}/songs/${encodeURIComponent(trackId)}${youtubeQuery(youtubeVideoId)}`, {
    method: 'POST',
  });
}

export async function removeSongFromPlaylist(trackId: string, playlistId: string, youtubeVideoId?: string) {
  await cantaroApiClient.request(`/api/music/library/playlists/${encodeURIComponent(playlistId)}/songs/${encodeURIComponent(trackId)}${youtubeQuery(youtubeVideoId)}`, {
    method: 'DELETE',
  });
}

export async function loadSongLyrics(trackId: string, signal?: AbortSignal): Promise<LyricsResult> {
  const cached = lyricsCache.get(trackId);
  if (cached) return cached;
  const result = await requestSongLyrics(trackId, signal);
  if (result.state !== 'provider_error') lyricsCache.set(trackId, result);
  return result;
}

async function requestSongLyrics(
  trackId: string,
  signal?: AbortSignal,
): Promise<LyricsResult> {
  try {
    return await cantaroApiClient.request<LyricsResult>(
      `/api/music/tracks/${encodeURIComponent(trackId)}/lyrics`,
      { signal },
    );
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) return unavailableLyrics();
    throw abortAwareError(error, signal);
  }
}

function unavailableLyrics(): LyricsResult {
  return {
    state: 'unavailable',
    matchStatus: 'unavailable',
    provider: 'Cantaro',
    attribution: 'Lyrics lookup by Cantaro',
    explanation: 'Lyrics are not available for this song yet.',
  };
}

function abortAwareError(error: unknown, signal?: AbortSignal): unknown {
  return signal?.aborted
    ? new DOMException('The request was aborted.', 'AbortError')
    : error;
}

export async function openCantaroPage(path: string) {
  const settings = await browserSettingsRepository.read();
  await browser.tabs.create({ url: `${settings.baseUrl}${path}` });
}
