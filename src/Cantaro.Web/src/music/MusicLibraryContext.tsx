import { useCallback, useEffect, useState, type ReactNode } from 'react';
import { musicLibraryApi, type MusicLibraryResponse } from '@cantaro/client-shared/music';
import { playlistSyncDataRefreshEventName } from './playlistSyncProgress';
import { MusicLibraryStateContext } from './MusicLibraryStateContext';

export function MusicLibraryProvider({ children }: { children: ReactNode }) {
  const [library, setLibrary] = useState<MusicLibraryResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  const reload = useCallback(async () => {
    setIsLoading(true);
    setError(null);

    try {
      setLibrary(await musicLibraryApi.getLibrary());
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : 'Failed to load music library');
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    void reload();
  }, [reload]);

  useEffect(() => {
    const handlePlaylistSyncDataRefresh = () => {
      void reload();
    };

    window.addEventListener(playlistSyncDataRefreshEventName, handlePlaylistSyncDataRefresh);
    return () => window.removeEventListener(playlistSyncDataRefreshEventName, handlePlaylistSyncDataRefresh);
  }, [reload]);

  return (
    <MusicLibraryStateContext.Provider value={{ library, error, isLoading, reload }}>
      {children}
    </MusicLibraryStateContext.Provider>
  );
}
