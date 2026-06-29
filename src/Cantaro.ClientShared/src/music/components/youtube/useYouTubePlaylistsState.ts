import { useCallback, useEffect, useState } from 'react';
import { platformManager } from '../../platforms';
import { isYouTubeReconnectRequiredError } from '../../platforms/clients/youtubePlatformClient';
import type { PlatformAccountStatus, PlatformPlaylist, PlatformSong } from '../../platforms';

function getYouTubeErrorMessage(error: unknown, fallbackMessage: string): string {
  return error instanceof Error ? error.message : fallbackMessage;
}

function useYouTubeBrowserState() {
  const [status, setStatus] = useState<PlatformAccountStatus | null>(null);
  const [playlists, setPlaylists] = useState<PlatformPlaylist[]>([]);
  const [selectedPlaylist, setSelectedPlaylist] = useState<PlatformPlaylist | null>(null);
  const [playlistItems, setPlaylistItems] = useState<PlatformSong[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isLoadingItems, setIsLoadingItems] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [needsReconnect, setNeedsReconnect] = useState(false);

  const clearPlaylistState = useCallback(() => {
    setPlaylists([]);
    setSelectedPlaylist(null);
    setPlaylistItems([]);
  }, []);

  return {
    status,
    setStatus,
    playlists,
    setPlaylists,
    selectedPlaylist,
    setSelectedPlaylist,
    playlistItems,
    setPlaylistItems,
    isLoading,
    setIsLoading,
    isLoadingItems,
    setIsLoadingItems,
    error,
    setError,
    needsReconnect,
    setNeedsReconnect,
    clearPlaylistState,
  };
}

function useYouTubeLoaders(state: ReturnType<typeof useYouTubeBrowserState>) {
  const youtubePlatform = platformManager.getClient('youtube');
  const {
    setStatus,
    setPlaylists,
    setSelectedPlaylist,
    setPlaylistItems,
    setIsLoading,
    setIsLoadingItems,
    setError,
    setNeedsReconnect,
    clearPlaylistState,
  } = state;

  const handleLoadError = useCallback((loadError: unknown, fallbackMessage: string) => {
    if (isYouTubeReconnectRequiredError(loadError)) {
      setNeedsReconnect(true);
      clearPlaylistState();
      setError(null);
      return;
    }

    setError(getYouTubeErrorMessage(loadError, fallbackMessage));
  }, [clearPlaylistState, setError, setNeedsReconnect]);

  const loadStatus = useCallback(async () => {
    try {
      const statusData = await youtubePlatform.status();
      setStatus(statusData);
      setNeedsReconnect(false);
      return statusData;
    } catch (loadError) {
      console.error('Failed to load YouTube status:', loadError);
      setError('Failed to check YouTube connection status');
      return null;
    }
  }, [setError, setNeedsReconnect, setStatus, youtubePlatform]);

  const loadPlaylists = useCallback(async () => {
    try {
      setPlaylists(await youtubePlatform.playlists(false));
      setNeedsReconnect(false);
      setError(null);
    } catch (loadError) {
      console.error('Failed to load playlists:', loadError);
      handleLoadError(loadError, 'Failed to load playlists');
    }
  }, [handleLoadError, setError, setNeedsReconnect, setPlaylists, youtubePlatform]);

  const connect = useCallback(() => {
    void platformManager.connect('youtube', {
      route: '/music/platforms/youtube',
      trigger: 'youtube-page-connect',
    });
  }, []);

  const selectPlaylist = useCallback(async (playlist: PlatformPlaylist) => {
    setSelectedPlaylist(playlist);
    setIsLoadingItems(true);

    try {
      setPlaylistItems(await playlist.songs());
      setNeedsReconnect(false);
      setError(null);
    } catch (loadError) {
      handleLoadError(loadError, 'Failed to load playlist items');
      setPlaylistItems([]);
    } finally {
      setIsLoadingItems(false);
    }
  }, [handleLoadError, setError, setIsLoadingItems, setNeedsReconnect, setPlaylistItems, setSelectedPlaylist]);

  const clearSelectedPlaylist = useCallback(() => {
    setSelectedPlaylist(null);
    setPlaylistItems([]);
  }, [setPlaylistItems, setSelectedPlaylist]);

  const refreshPlaylists = useCallback(async () => {
    try {
      setIsLoading(true);
      setPlaylists(await platformManager.refreshPlaylists('youtube', false));
      clearSelectedPlaylist();
      setNeedsReconnect(false);
      setError(null);
    } catch (loadError) {
      handleLoadError(loadError, 'Failed to refresh playlists');
    } finally {
      setIsLoading(false);
    }
  }, [clearSelectedPlaylist, handleLoadError, setError, setIsLoading, setNeedsReconnect, setPlaylists]);

  return {
    loadStatus,
    loadPlaylists,
    handleLoadError,
    connect,
    selectPlaylist,
    clearSelectedPlaylist,
    refreshPlaylists,
  };
}

function useYouTubeConnectionActions(
  state: ReturnType<typeof useYouTubeBrowserState>,
  loadStatus: () => Promise<PlatformAccountStatus | null>,
) {
  const disconnect = useCallback(async () => {
    try {
      await platformManager.disconnect('youtube', {
        onSuccess: () => {
          state.setStatus({ platformId: 'youtube', isConnected: false });
          state.clearPlaylistState();
          state.setNeedsReconnect(false);
        },
      });
      await loadStatus();
    } catch {
      state.setError('Failed to disconnect YouTube account');
    }
  }, [loadStatus, state]);

  return { disconnect };
}

function useInitialYouTubeLoad(
  setIsLoading: (isLoading: boolean) => void,
  loadStatus: () => Promise<PlatformAccountStatus | null>,
  loadPlaylists: () => Promise<void>,
) {
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
  }, [loadPlaylists, loadStatus, setIsLoading]);
}

export function useYouTubePlaylistsState() {
  const state = useYouTubeBrowserState();
  const loaders = useYouTubeLoaders(state);
  const { disconnect } = useYouTubeConnectionActions(state, loaders.loadStatus);
  useInitialYouTubeLoad(state.setIsLoading, loaders.loadStatus, loaders.loadPlaylists);

  return {
    status: state.status,
    playlists: state.playlists,
    selectedPlaylist: state.selectedPlaylist,
    playlistItems: state.playlistItems,
    isLoading: state.isLoading,
    isLoadingItems: state.isLoadingItems,
    error: state.error,
    needsReconnect: state.needsReconnect,
    connect: loaders.connect,
    disconnect,
    selectPlaylist: loaders.selectPlaylist,
    clearSelectedPlaylist: loaders.clearSelectedPlaylist,
    refreshPlaylists: loaders.refreshPlaylists,
  };
}
