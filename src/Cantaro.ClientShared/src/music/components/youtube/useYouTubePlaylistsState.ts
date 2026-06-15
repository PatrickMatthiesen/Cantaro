import { usePlatformPlaylistsState } from '../usePlatformPlaylistsState';

export function useYouTubePlaylistsState() {
  return usePlatformPlaylistsState('youtube');
}
