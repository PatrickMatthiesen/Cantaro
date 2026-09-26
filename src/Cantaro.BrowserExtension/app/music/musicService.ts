import type { MusicLibraryResponse, MusicLibrarySong } from '@cantaro/client-shared/music';
import { cantaroApiClient } from '../../platform/api/cantaroApiClient';
import { browserSettingsRepository } from '../../platform/settings/settingsRepository';
import type { LyricsResult } from './musicLyrics';

import { getLocalLyrics } from '../../features/music/musicLyricsProvider';

function playlistMutationQuery(youtubeVideoId?: string, entryId?: string) {
  const query = new URLSearchParams();
  if (youtubeVideoId) query.set('youtubeVideoId', youtubeVideoId);
  if (entryId) query.set('entryId', entryId);
  return query.size > 0 ? `?${query.toString()}` : '';
}

export async function loadMusicLibrary(): Promise<MusicLibraryResponse> {
  return cantaroApiClient.request('/api/music/library');
}

export async function loadCanonicalSong(trackId: string): Promise<MusicLibrarySong> {
  return cantaroApiClient.request(`/api/music/library/songs/${encodeURIComponent(trackId)}`);
}

export async function addSongToPlaylist(trackId: string, playlistId: string, youtubeVideoId?: string) {
  await cantaroApiClient.request(`/api/music/library/playlists/${encodeURIComponent(playlistId)}/songs/${encodeURIComponent(trackId)}${playlistMutationQuery(youtubeVideoId)}`, {
    method: 'POST',
  });
}

export async function removeSongFromPlaylist(trackId: string, playlistId: string, youtubeVideoId?: string, entryId?: string) {
  await cantaroApiClient.request(`/api/music/library/playlists/${encodeURIComponent(playlistId)}/songs/${encodeURIComponent(trackId)}${playlistMutationQuery(youtubeVideoId, entryId)}`, {
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
