import { useCallback, useEffect, useState, type ReactNode } from 'react';
import { musicLibraryApi, type MusicLibraryResponse } from '@cantaro/client-shared/music';
import { GlassCard } from '@cantaro/client-shared/ui';
import { MusicPageShell } from './MusicPageShell';
import { playlistSyncDataRefreshEventName } from './playlistSyncProgress';

function useMusicLibrary() {
  const [library, setLibrary] = useState<MusicLibraryResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  const loadLibrary = useCallback(async () => {
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
    void loadLibrary();
  }, [loadLibrary]);

  useEffect(() => {
    const handlePlaylistSyncDataRefresh = () => {
      void loadLibrary();
    };

    window.addEventListener(playlistSyncDataRefreshEventName, handlePlaylistSyncDataRefresh);
    return () => window.removeEventListener(playlistSyncDataRefreshEventName, handlePlaylistSyncDataRefresh);
  }, [loadLibrary]);

  return { library, error, isLoading, loadLibrary };
}

export function MusicLibraryPanel({ children }: { children: (library: MusicLibraryResponse) => ReactNode }) {
  const { library, error, isLoading, loadLibrary } = useMusicLibrary();

  if (isLoading) {
    return (
      <MusicPageShell>
        <div className="flex min-h-[360px] items-center justify-center rounded-3xl border border-white/80 bg-white/65 shadow-[0_24px_80px_rgba(82,70,140,0.08)] backdrop-blur">
          <div className="flex items-center gap-3 text-sm font-semibold text-slate-600">
            <span className="h-2.5 w-2.5 animate-pulse rounded-full bg-violet-500" aria-hidden />
            Loading your library
          </div>
        </div>
      </MusicPageShell>
    );
  }

  if (error) {
    return (
      <MusicPageShell>
        <GlassCard className="border-rose-200 bg-rose-50 p-5 text-sm text-rose-700">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <span>{error}</span>
            <button type="button" className="font-semibold text-rose-800 underline" onClick={() => void loadLibrary()}>
              Retry
            </button>
          </div>
        </GlassCard>
      </MusicPageShell>
    );
  }

  return <>{library ? children(library) : null}</>;
}
