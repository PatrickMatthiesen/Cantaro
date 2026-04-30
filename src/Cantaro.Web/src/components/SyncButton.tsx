import {
  SyncButtonPanel,
  SyncEmptyState,
  SyncLoadingState,
} from './sync/SyncButtonPanel';
import { useSyncButtonState } from './sync/useSyncButtonState';
import type { PlatformId } from '../platforms';

interface SyncButtonProps {
  platformId: PlatformId;
  platformName: string;
}

export function SyncButton({ platformId, platformName }: SyncButtonProps) {
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
