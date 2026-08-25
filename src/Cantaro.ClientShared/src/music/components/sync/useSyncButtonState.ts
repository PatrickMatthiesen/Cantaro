import { useCallback, useEffect, useRef, useState } from 'react';
import { syncApi } from '../../services';
import type { BatchSyncResponse, SyncStatusResponse } from '../../services/syncApi';
import { platformManager } from '../../platforms';
import type { PlatformId, PlatformPlaylist } from '../../platforms';

function getSyncErrorMessage(error: unknown, fallbackMessage: string): string {
  return error instanceof Error ? error.message : fallbackMessage;
}

function getSyncBlockedMessage(status: SyncStatusResponse | null): string {
  return status?.overall.message
    ?? `Song sync limit reached. Please wait for the ${status?.overall.windowMinutes ?? 10}-minute window to reset.`;
}

interface SyncActionOptions {
  platformId: PlatformId;
  isSyncing: boolean;
  setIsSyncing: (isSyncing: boolean) => void;
  loadSyncStatus: () => Promise<SyncStatusResponse | null>;
  setError: (message: string | null) => void;
  appendStatus: (message: string) => void;
  startProgressSimulation: (playlistCount: number) => void;
  stopProgressSimulation: () => void;
  setSyncProgress: (progress: number) => void;
  setShowStatusDrawer: (visible: boolean | ((previous: boolean) => boolean)) => void;
}

function toggleSelectedPlaylist(previous: Set<string>, playlistId: string): Set<string> {
  const next = new Set(previous);
  if (next.has(playlistId)) {
    next.delete(playlistId);
  } else {
    next.add(playlistId);
  }

  return next;
}

function useSyncSimulation(platformName: string) {
  const [syncProgress, setSyncProgress] = useState(0);
  const [statusUpdates, setStatusUpdates] = useState<string[]>([]);
  const [showStatusDrawer, setShowStatusDrawer] = useState(false);
  const progressIntervalRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const milestoneRef = useRef(0);

  const appendStatus = useCallback((message: string) => {
    setStatusUpdates((previous) => [...previous, message]);
  }, []);

  const stopProgressSimulation = useCallback(() => {
    if (progressIntervalRef.current) {
      clearInterval(progressIntervalRef.current);
      progressIntervalRef.current = null;
    }
  }, []);

  const startProgressSimulation = useCallback((playlistCount: number) => {
    stopProgressSimulation();
    milestoneRef.current = 0;
    setSyncProgress(5);
    setStatusUpdates([
      `Preparing sync for ${playlistCount === 0 ? 'all available' : playlistCount.toString()} playlist(s).`,
    ]);
    setShowStatusDrawer(true);

    const milestones = [
      { progress: 15, message: 'Checking sync permissions and current rate limit window.' },
      { progress: 35, message: `Fetching latest playlist metadata from ${platformName}.` },
      { progress: 55, message: 'Matching tracks to canonical TrackIDs.' },
      { progress: 75, message: 'Writing playlist updates and finalizing results.' },
    ];

    progressIntervalRef.current = setInterval(() => {
      setSyncProgress((previous) => {
        const next = Math.min(previous + 4, 90);

        while (milestoneRef.current < milestones.length) {
          const milestone = milestones[milestoneRef.current];
          if (!milestone || next < milestone.progress) break;
          appendStatus(milestone.message);
          milestoneRef.current += 1;
        }

        return next;
      });
    }, 500);
  }, [appendStatus, platformName, stopProgressSimulation]);

  return {
    syncProgress,
    setSyncProgress,
    statusUpdates,
    showStatusDrawer,
    setShowStatusDrawer,
    appendStatus,
    stopProgressSimulation,
    startProgressSimulation,
  };
}

function useSyncSetup(platformId: PlatformId, isSyncing: boolean, stopProgressSimulation: () => void) {
  const [syncStatus, setSyncStatus] = useState<SyncStatusResponse | null>(null);
  const [availablePlaylists, setAvailablePlaylists] = useState<PlatformPlaylist[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const loadSyncStatus = useCallback(async () => {
    try {
      const status = await syncApi.getSyncStatus(platformId);
      setSyncStatus(status);
      setError(null);
      return status;
    } catch (loadError) {
      console.error('Failed to load sync status:', loadError);
      if (loadError instanceof Error && loadError.message.includes('Not authenticated')) {
        return null;
      }

      setError(getSyncErrorMessage(loadError, 'Failed to load sync status'));
      return null;
    }
  }, [platformId]);

  const loadAvailablePlaylists = useCallback(async () => {
    try {
      setAvailablePlaylists(await platformManager.playlists(platformId, false));
    } catch (loadError) {
      console.error('Failed to load playlists:', loadError);
    }
  }, [platformId]);

  useEffect(() => {
    const init = async () => {
      setIsLoading(true);
      await loadSyncStatus();
      await loadAvailablePlaylists();
      setIsLoading(false);
    };

    void init();

    return () => stopProgressSimulation();
  }, [loadAvailablePlaylists, loadSyncStatus, stopProgressSimulation]);

  useEffect(() => {
    const intervalId = setInterval(() => {
      if (!isSyncing) {
        void loadSyncStatus();
      }
    }, 15000);

    return () => clearInterval(intervalId);
  }, [isSyncing, loadSyncStatus]);

  return {
    syncStatus,
    availablePlaylists,
    isLoading,
    error,
    setError,
    loadSyncStatus,
  };
}

function syncWasBlocked(
  latestStatus: SyncStatusResponse | null,
  setError: (message: string | null) => void,
  appendStatus: (message: string) => void,
  setShowStatusDrawer: (visible: boolean | ((previous: boolean) => boolean)) => void,
) {
  if (latestStatus?.overall.canSyncNow) {
    return false;
  }

  const message = getSyncBlockedMessage(latestStatus);
  setError(message);
  appendStatus(`Sync blocked: ${message}`);
  setShowStatusDrawer(true);
  return true;
}

async function handleSyncSuccess(
  result: BatchSyncResponse,
  stopProgressSimulation: () => void,
  setSyncProgress: (progress: number) => void,
  appendStatus: (message: string) => void,
  setSyncResult: (result: BatchSyncResponse | null) => void,
  loadSyncStatus: () => Promise<SyncStatusResponse | null>,
  setShowPlaylistSelector: (visible: boolean) => void,
) {
  stopProgressSimulation();
  setSyncProgress(100);
  appendStatus(`Sync completed. ${result.successCount} playlist(s) succeeded, ${result.failureCount} failed.`);
  appendStatus(`Processed ${result.songsSynced}/${result.songsRequested} requested songs in this run.`);
  setSyncResult(result);
  await loadSyncStatus();
  setShowPlaylistSelector(false);
}

async function handleSyncFailure(
  syncError: unknown,
  stopProgressSimulation: () => void,
  setSyncProgress: (progress: number) => void,
  setError: (message: string | null) => void,
  appendStatus: (message: string) => void,
  loadSyncStatus: () => Promise<SyncStatusResponse | null>,
) {
  stopProgressSimulation();
  setSyncProgress(0);
  const message = getSyncErrorMessage(syncError, 'Sync failed');
  setError(message);
  appendStatus(`Sync failed: ${message}`);
  await loadSyncStatus();
}

function useSyncActions(
  {
    platformId,
    isSyncing,
    setIsSyncing,
    loadSyncStatus,
    setError,
    appendStatus,
    startProgressSimulation,
    stopProgressSimulation,
    setSyncProgress,
    setShowStatusDrawer,
  }: SyncActionOptions,
) {
  const [showPlaylistSelector, setShowPlaylistSelector] = useState(false);
  const [selectedPlaylists, setSelectedPlaylists] = useState<Set<string>>(new Set());
  const [syncResult, setSyncResult] = useState<BatchSyncResponse | null>(null);

  const handleSync = useCallback(async (playlistIds: string[] | null) => {
    const latestStatus = await loadSyncStatus();
    if (syncWasBlocked(latestStatus, setError, appendStatus, setShowStatusDrawer)) {
      return;
    }

    setIsSyncing(true);
    setSyncResult(null);
    setError(null);
    startProgressSimulation(playlistIds?.length ?? 0);

    try {
      const result = await syncApi.batchSync({
        service: platformId,
        servicePlaylistIds: playlistIds,
      });

      await handleSyncSuccess(
        result,
        stopProgressSimulation,
        setSyncProgress,
        appendStatus,
        setSyncResult,
        loadSyncStatus,
        setShowPlaylistSelector,
      );
    } catch (syncError) {
      await handleSyncFailure(
        syncError,
        stopProgressSimulation,
        setSyncProgress,
        setError,
        appendStatus,
        loadSyncStatus,
      );
    } finally {
      setIsSyncing(false);
    }
  }, [appendStatus, loadSyncStatus, platformId, setError, setIsSyncing, setShowStatusDrawer, setSyncProgress, startProgressSimulation, stopProgressSimulation]);

  const togglePlaylistSelection = useCallback((playlistId: string) => {
    setSelectedPlaylists((previous) => toggleSelectedPlaylist(previous, playlistId));
  }, []);

  return {
    isSyncing,
    showPlaylistSelector,
    setShowPlaylistSelector,
    selectedPlaylists,
    syncResult,
    handleSync,
    togglePlaylistSelection,
  };
}

function getSyncUsageSummary(syncStatus: SyncStatusResponse | null, isSyncing: boolean) {
  const songsSyncedInWindow = syncStatus?.overall.songsSyncedInWindow ?? 0;
  const songSyncLimit = syncStatus?.overall.songSyncLimit ?? 2000;
  const windowMinutes = syncStatus?.overall.windowMinutes ?? 10;
  const remainingSongs = syncStatus?.overall.remainingSongsInWindow ?? 0;

  return {
    songsSyncedInWindow,
    songSyncLimit,
    windowMinutes,
    remainingSongs,
    windowUsagePercent: Math.min(100, Math.round((songsSyncedInWindow / Math.max(1, songSyncLimit)) * 100)),
    canSync: Boolean(syncStatus?.overall.canSyncNow) && !isSyncing,
  };
}

export function useSyncButtonState(platformId: PlatformId, platformName: string) {
  const [isSyncing, setIsSyncing] = useState(false);
  const {
    syncProgress,
    setSyncProgress,
    statusUpdates,
    showStatusDrawer,
    setShowStatusDrawer,
    appendStatus,
    stopProgressSimulation,
    startProgressSimulation,
  } = useSyncSimulation(platformName);
  const {
    syncStatus,
    availablePlaylists,
    isLoading,
    error,
    setError,
    loadSyncStatus,
  } = useSyncSetup(platformId, isSyncing, stopProgressSimulation);
  const {
    showPlaylistSelector,
    setShowPlaylistSelector,
    selectedPlaylists,
    syncResult,
    handleSync,
    togglePlaylistSelection,
  } = useSyncActions({
    platformId,
    isSyncing,
    setIsSyncing,
    loadSyncStatus,
    setError,
    appendStatus,
    startProgressSimulation,
    stopProgressSimulation,
    setSyncProgress,
    setShowStatusDrawer,
  });
  const usageSummary = getSyncUsageSummary(syncStatus, isSyncing);

  return {
    syncStatus,
    availablePlaylists,
    isLoading,
    error,
    isSyncing,
    syncProgress,
    statusUpdates,
    showStatusDrawer,
    showPlaylistSelector,
    selectedPlaylists,
    syncResult,
    ...usageSummary,
    setShowStatusDrawer,
    setShowPlaylistSelector,
    handleSync,
    togglePlaylistSelection,
  };
}
