import { playlistSyncApi, type PlaylistSyncDetails } from '@cantaro/client-shared/music';
import { useCallback, useEffect, useRef, useState } from 'react';
import { requestPlaylistSyncDataRefresh } from './playlistSyncProgress';

function message(error: unknown) {
  return error instanceof Error ? error.message : 'Playlist sync failed.';
}

export function usePlaylistSyncDetails(playlistId: string) {
  const [details, setDetails] = useState<PlaylistSyncDetails | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const generation = useRef(0);
  const locked = useRef(false);

  const reload = useCallback(async () => {
    const current = ++generation.current;
    setIsLoading(true);
    try {
      const response = await playlistSyncApi.get(playlistId);
      if (current !== generation.current) return;
      setDetails(response);
      setError(null);
    } catch (loadError) {
      if (current === generation.current) setError(message(loadError));
    } finally {
      if (current === generation.current) setIsLoading(false);
    }
  }, [playlistId]);

  useEffect(() => {
    void reload();
    const generationRef = generation;
    return () => { generationRef.current++; };
  }, [reload]);

  const isPending = details?.links.some((link) => link.state === 'creating' || ['pending', 'running', 'rate_limited', 'quota_limited'].includes(link.lastSyncStatus ?? '')) ?? false;
  useEffect(() => {
    if (!isPending) return;
    const timer = window.setInterval(() => {
      if (document.visibilityState === 'visible') void reload();
    }, 5000);
    return () => window.clearInterval(timer);
  }, [isPending, reload]);

  const perform = useCallback(async (label: string, operation: () => Promise<PlaylistSyncDetails | void>) => {
    if (locked.current) return false;
    locked.current = true;
    setBusy(label);
    setError(null);
    try {
      const updated = await operation();
      if (updated) setDetails(updated);
      else await reload();
      requestPlaylistSyncDataRefresh();
      return true;
    } catch (operationError) {
      const failureMessage = message(operationError);
      try {
        // Mutations can persist a per-link failure before returning an error.
        // Refresh that state and report it once, beside its recovery action.
        const refreshed = await playlistSyncApi.get(playlistId);
        setDetails(refreshed);
        setError(refreshed.links.some((link) => link.lastError === failureMessage) ? null : failureMessage);
      } catch {
        setError(failureMessage);
      }
      return false;
    } finally {
      locked.current = false;
      setBusy(null);
    }
  }, [playlistId, reload]);

  return { details: details?.playlistId === playlistId ? details : null, isLoading, busy, error, setError, reload, perform };
}
