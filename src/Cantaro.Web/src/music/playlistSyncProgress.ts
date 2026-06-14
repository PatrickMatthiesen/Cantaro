import type { PlatformId } from '@cantaro/client-shared/music';

const playlistSyncProgressKey = 'cantaro.playlistSyncProgress.v1';
const maxProgressAgeMs = 10 * 60 * 1000;

export const playlistSyncProgressEventName = 'cantaro-playlist-sync-progress';
export const playlistSyncDataRefreshEventName = 'cantaro-playlist-sync-data-refresh';

export type PlaylistSyncProgressPhase = 'syncing' | 'completed' | 'failed';

export interface PlaylistSyncProgress {
  phase: PlaylistSyncProgressPhase;
  sourcePlatformId: PlatformId;
  playlistCount: number;
  songCount: number;
  targetCount: number;
  playlistNames: string[];
  startedAt: string;
  updatedAt: string;
  successCount?: number;
  failureCount?: number;
  errorMessage?: string;
  focusActivity?: boolean;
}

function isProgressFresh(progress: PlaylistSyncProgress): boolean {
  return Date.now() - new Date(progress.updatedAt).getTime() < maxProgressAgeMs;
}

export function readPlaylistSyncProgress(): PlaylistSyncProgress | null {
  try {
    const rawProgress = window.sessionStorage.getItem(playlistSyncProgressKey);
    if (!rawProgress) return null;

    const progress = JSON.parse(rawProgress) as PlaylistSyncProgress;
    if (!isProgressFresh(progress)) {
      window.sessionStorage.removeItem(playlistSyncProgressKey);
      return null;
    }

    return progress;
  } catch {
    window.sessionStorage.removeItem(playlistSyncProgressKey);
    return null;
  }
}

export function writePlaylistSyncProgress(progress: PlaylistSyncProgress): void {
  window.sessionStorage.setItem(playlistSyncProgressKey, JSON.stringify(progress));
  window.dispatchEvent(new CustomEvent(playlistSyncProgressEventName));
}

export function requestPlaylistSyncDataRefresh(): void {
  window.dispatchEvent(new CustomEvent(playlistSyncDataRefreshEventName));
}

export function consumePlaylistSyncActivityFocus(): boolean {
  const progress = readPlaylistSyncProgress();
  if (!progress?.focusActivity) return false;

  writePlaylistSyncProgress({
    ...progress,
    focusActivity: false,
  });
  return true;
}
