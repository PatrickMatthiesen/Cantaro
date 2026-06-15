import { useCallback, useEffect, useMemo, useState } from 'react';
import { platformManager } from '../platforms';
import { platformCatalog } from '../platforms/catalog';
import type { PlatformAccountStatus, PlatformId, PlatformPlaylist, PlatformSong } from '../platforms';

function getPlatformName(platformId: PlatformId): string {
  return platformCatalog.find((platform) => platform.id === platformId)?.name ?? platformId;
}

function getPlatformErrorMessage(error: unknown, fallbackMessage: string): string {
  return error instanceof Error ? error.message : fallbackMessage;
}

export function usePlatformPlaylistsState(platformId: PlatformId) {
  const [status, setStatus] = useState<PlatformAccountStatus | null>(null);
  const [playlists, setPlaylists] = useState<PlatformPlaylist[]>([]);
  const [selectedPlaylist, setSelectedPlaylist] = useState<PlatformPlaylist | null>(null);
  const [playlistItems, setPlaylistItems] = useState<PlatformSong[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isLoadingItems, setIsLoadingItems] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const platform = useMemo(() => platformManager.getClient(platformId), [platformId]);
  const platformName = getPlatformName(platformId);

  const loadStatus = useCallback(async () => {
    try {
      const statusData = await platform.status();
      setStatus(statusData);
      return statusData;
    } catch (loadError) {
      console.error(`Failed to load ${platformName} status:`, loadError);
      setError(`Failed to check ${platformName} connection status`);
      return null;
    }
  }, [platform, platformName]);

  const loadPlaylists = useCallback(async () => {
    try {
      setPlaylists(await platform.playlists(false));
      setError(null);
    } catch (loadError) {
      console.error(`Failed to load ${platformName} playlists:`, loadError);
      setError(getPlatformErrorMessage(loadError, 'Failed to load playlists'));
    }
  }, [platform, platformName]);

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
    void platformManager.connect(platformId, {
      route: window.location.pathname,
      trigger: `${platformId}-page-connect`,
    });
  }, [platformId]);

  const disconnect = useCallback(async () => {
    try {
      await platformManager.disconnect(platformId, {
        onSuccess: () => {
          setStatus({ platformId, isConnected: false });
          setPlaylists([]);
          setSelectedPlaylist(null);
          setPlaylistItems([]);
        },
      });
      await loadStatus();
    } catch {
      setError(`Failed to disconnect ${platformName} account`);
    }
  }, [loadStatus, platformId, platformName]);

  const selectPlaylist = useCallback(async (playlist: PlatformPlaylist) => {
    setSelectedPlaylist(playlist);
    setIsLoadingItems(true);

    try {
      setPlaylistItems(await playlist.songs());
      setError(null);
    } catch (loadError) {
      setError(getPlatformErrorMessage(loadError, 'Failed to load playlist items'));
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
      setPlaylists(await platformManager.refreshPlaylists(platformId, false));
      clearSelectedPlaylist();
      setError(null);
    } catch (loadError) {
      setError(getPlatformErrorMessage(loadError, 'Failed to refresh playlists'));
    } finally {
      setIsLoading(false);
    }
  }, [clearSelectedPlaylist, platformId]);

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
