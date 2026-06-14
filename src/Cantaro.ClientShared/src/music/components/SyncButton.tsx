import {
  SyncButtonPanel,
  SyncEmptyState,
  SyncLoadingState,
} from './sync/SyncButtonPanel';
import { useEffect, useRef } from 'react';
import { useSyncButtonState } from './sync/useSyncButtonState';
import type { PlatformId } from '../platforms';

interface SyncButtonProps {
  platformId: PlatformId;
  platformName: string;
  initiallyShowPlaylistSelector?: boolean;
}

export function SyncButton({ platformId, platformName, initiallyShowPlaylistSelector = false }: SyncButtonProps) {
  const hasAppliedInitialPlaylistSelector = useRef(false);
  const {
    syncStatus,
    availablePlaylists,
    isLoading,
    error,
    isSyncing,
    syncProgress,
    statusUpdates,
    showStatusDrawer,
    setShowStatusDrawer,
    showPlaylistSelector,
    setShowPlaylistSelector,
    selectedPlaylists,
    syncResult,
    songsSyncedInWindow,
    songSyncLimit,
    windowMinutes,
    remainingSongs,
    windowUsagePercent,
    canSync,
    handleSync,
    togglePlaylistSelection,
  } = useSyncButtonState(platformId, platformName);

  useEffect(() => {
    if (
      initiallyShowPlaylistSelector
      && !hasAppliedInitialPlaylistSelector.current
      && !showPlaylistSelector
      && availablePlaylists.length > 0
      && canSync
    ) {
      hasAppliedInitialPlaylistSelector.current = true;
      setShowPlaylistSelector(true);
    }
  }, [availablePlaylists.length, canSync, initiallyShowPlaylistSelector, setShowPlaylistSelector, showPlaylistSelector]);

  if (isLoading) {
    return <SyncLoadingState />;
  }

  if (!syncStatus && !error) {
    return <SyncEmptyState />;
  }

  return (
    <SyncButtonPanel
      platformName={platformName}
      syncStatus={syncStatus!}
      availablePlaylists={availablePlaylists}
      isSyncing={isSyncing}
      syncProgress={syncProgress}
      statusUpdates={statusUpdates}
      showStatusDrawer={showStatusDrawer}
      showPlaylistSelector={showPlaylistSelector}
      selectedPlaylists={selectedPlaylists}
      syncResult={syncResult}
      error={error}
      songsSyncedInWindow={songsSyncedInWindow}
      songSyncLimit={songSyncLimit}
      windowMinutes={windowMinutes}
      remainingSongs={remainingSongs}
      windowUsagePercent={windowUsagePercent}
      canSync={canSync}
      onSyncAll={() => void handleSync(null)}
      onTogglePlaylistSelector={() => setShowPlaylistSelector((previous) => !previous)}
      onToggleStatusDrawer={() => setShowStatusDrawer((previous) => !previous)}
      onTogglePlaylistSelection={togglePlaylistSelection}
      onSyncSelected={() => void handleSync(Array.from(selectedPlaylists))}
      onClosePlaylistSelector={() => setShowPlaylistSelector(false)}
    />
  );
}
