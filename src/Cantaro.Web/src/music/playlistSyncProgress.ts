import type { MusicSyncJobResponse, PlatformId } from '@cantaro/client-shared/music';

const playlistSyncProgressKey = 'cantaro.playlistSyncProgress.v1';
const maxProgressAgeMs = 10 * 60 * 1000;

export const playlistSyncProgressEventName = 'cantaro-playlist-sync-progress';
export const playlistSyncDataRefreshEventName = 'cantaro-playlist-sync-data-refresh';

export type PlaylistSyncProgressPhase = 'syncing' | 'completed' | 'failed';

export interface PlaylistSyncProgress {
  jobId: string;
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
  processedPlaylistCount?: number;
  processedSongCount?: number;
  currentPlaylistName?: string;
  currentSongName?: string;
  focusActivity?: boolean;
}

function progressPhase(status: MusicSyncJobResponse['status']): PlaylistSyncProgressPhase {
  if (status === 'completed') return 'completed';
  if (status === 'failed') return 'failed';
  return 'syncing';
}

export function progressFromSyncJob(job: MusicSyncJobResponse, focusActivity = false): PlaylistSyncProgress {
  return {
    jobId: job.id,
    phase: progressPhase(job.status),
    sourcePlatformId: job.service as PlatformId,
    playlistCount: job.playlistCount,
    songCount: job.songCount,
    targetCount: 0,
    playlistNames: job.playlistNames,
    startedAt: job.startedAt ?? job.createdAt,
    updatedAt: job.updatedAt,
    successCount: job.successCount,
    failureCount: job.failureCount,
    errorMessage: job.errorMessage ?? undefined,
    processedPlaylistCount: job.processedPlaylistCount,
    processedSongCount: job.processedSongCount,
    currentPlaylistName: job.currentPlaylistName ?? undefined,
    currentSongName: job.currentSongName ?? undefined,
    focusActivity,
  };
}

function isProgressFresh(progress: PlaylistSyncProgress): boolean {
  return progress.phase === 'syncing'
    || Date.now() - new Date(progress.updatedAt).getTime() < maxProgressAgeMs;
}

export function readPlaylistSyncProgress(): PlaylistSyncProgress | null {
  try {
    const rawProgress = window.sessionStorage.getItem(playlistSyncProgressKey);
    if (!rawProgress) return null;

    const progress = JSON.parse(rawProgress) as PlaylistSyncProgress;
    if (!progress.jobId) {
      window.sessionStorage.removeItem(playlistSyncProgressKey);
      return null;
    }
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
