import type { MusicLibraryResponse, MusicLibrarySong } from '@cantaro/client-shared/music';
import { cantaroApiClient } from '../../platform/api/cantaroApiClient';
import { browserSettingsRepository } from '../../platform/settings/settingsRepository';
import type { LyricsResult } from './musicLyrics';

import { getLocalLyrics } from '../../features/music/musicLyricsProvider';

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

export async function loadSongLyrics(song: MusicLibrarySong, signal?: AbortSignal): Promise<LyricsResult> {
  return getLocalLyrics(song, signal);
}

export async function openCantaroPage(path: string) {
  const settings = await browserSettingsRepository.read();
  await browser.tabs.create({ url: `${settings.baseUrl}${path}` });
}
