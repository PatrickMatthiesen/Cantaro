import { useCallback, useEffect, useState } from 'react';
import { platformManager } from '../../platforms';
import type { PlatformAccountStatus, PlatformPlaylist, PlatformSong } from '../../platforms';

function getYouTubeErrorMessage(error: unknown, fallbackMessage: string): string {
  return error instanceof Error ? error.message : fallbackMessage;
}

export function useYouTubePlaylistsState() {
  const [status, setStatus] = useState<PlatformAccountStatus | null>(null);
  const [playlists, setPlaylists] = useState<PlatformPlaylist[]>([]);
  const [selectedPlaylist, setSelectedPlaylist] = useState<PlatformPlaylist | null>(null);
  const [playlistItems, setPlaylistItems] = useState<PlatformSong[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isLoadingItems, setIsLoadingItems] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const youtubePlatform = platformManager.getClient('youtube');

  const loadStatus = useCallback(async () => {
    try {
      const statusData = await youtubePlatform.status();
      setStatus(statusData);
      return statusData;
    } catch (loadError) {
      console.error('Failed to load YouTube status:', loadError);
      setError('Failed to check YouTube connection status');
      return null;
    }
  }, [youtubePlatform]);

  const loadPlaylists = useCallback(async () => {
    try {
      setPlaylists(await youtubePlatform.playlists(false));
      setError(null);
    } catch (loadError) {
      console.error('Failed to load playlists:', loadError);
      setError(getYouTubeErrorMessage(loadError, 'Failed to load playlists'));
    }
  }, [youtubePlatform]);

  useEffect(() => {
    const init = async () => {
      setIsLoading(true);
      const statusData = await loadStatus();
      if (statusData?.isConnected) {
        await loadPlaylists();
      }
      setIsLoading(false);
    };

    void init();
  }, [loadPlaylists, loadStatus]);

  const connect = useCallback(() => {
    void platformManager.connect('youtube', {
      route: '/music/platforms/sync?source=youtube',
      trigger: 'youtube-page-connect',
    });
  }, []);

  const disconnect = useCallback(async () => {
    try {
      await platformManager.disconnect('youtube', {
        onSuccess: () => {
          setStatus({ platformId: 'youtube', isConnected: false });
          setPlaylists([]);
          setSelectedPlaylist(null);
          setPlaylistItems([]);
        },
      });
      await loadStatus();
    } catch {
      setError('Failed to disconnect YouTube account');
    }
  }, [loadStatus]);

  const selectPlaylist = useCallback(async (playlist: PlatformPlaylist) => {
    setSelectedPlaylist(playlist);
    setIsLoadingItems(true);

    try {
      setPlaylistItems(await playlist.songs());
      setError(null);
    } catch (loadError) {
      setError(getYouTubeErrorMessage(loadError, 'Failed to load playlist items'));
      setPlaylistItems([]);
    } finally {
      setIsLoadingItems(false);
    }
  }, []);

  const clearSelectedPlaylist = useCallback(() => {
    setSelectedPlaylist(null);
    setPlaylistItems([]);
  }, []);

  const refreshPlaylists = useCallback(async () => {
    try {
      setIsLoading(true);
      setPlaylists(await platformManager.refreshPlaylists('youtube', false));
      clearSelectedPlaylist();
      setError(null);
    } catch (loadError) {
      setError(getYouTubeErrorMessage(loadError, 'Failed to refresh playlists'));
    } finally {
      setIsLoading(false);
    }
  }, [clearSelectedPlaylist]);

  return {
    status,
    playlists,
    selectedPlaylist,
    playlistItems,
    isLoading,
    isLoadingItems,
    error,
    connect,
    disconnect,
    selectPlaylist,
    clearSelectedPlaylist,
    refreshPlaylists,
  };
}
