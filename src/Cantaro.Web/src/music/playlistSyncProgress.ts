import type { MusicSyncJobResponse, PlatformId } from '@cantaro/client-shared/music';

const playlistSyncFocusKey = 'cantaro.playlistSyncFocusJob.v1';

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
}

function progressPhase(status: MusicSyncJobResponse['status']): PlaylistSyncProgressPhase {
  if (status === 'completed') return 'completed';
  if (status === 'failed') return 'failed';
  return 'syncing';
}

function optionalText(value: string | null | undefined): string | undefined {
  return value ?? undefined;
}

function syncErrorMessage(job: MusicSyncJobResponse): string | undefined {
  const failedResult = job.results.find((result) => !result.success);
  return optionalText(failedResult?.errorMessage ?? job.errorMessage);
}

export function progressFromSyncJob(job: MusicSyncJobResponse): PlaylistSyncProgress {
  return {
    jobId: job.id,
    phase: progressPhase(job.status),
    sourcePlatformId: job.service as PlatformId,
    playlistCount: job.playlistCount,
    songCount: job.songCount,
    targetCount: 0,
    playlistNames: job.playlistNames,
    startedAt: optionalText(job.startedAt) ?? job.createdAt,
    updatedAt: job.updatedAt,
    successCount: job.successCount,
    failureCount: job.failureCount,
    errorMessage: syncErrorMessage(job),
    processedPlaylistCount: job.processedPlaylistCount,
    processedSongCount: job.processedSongCount,
    currentPlaylistName: optionalText(job.currentPlaylistName),
    currentSongName: optionalText(job.currentSongName),
  };
}

export function writePlaylistSyncActivityFocus(jobId: string): void {
  window.sessionStorage.setItem(playlistSyncFocusKey, jobId);
}

export function consumePlaylistSyncActivityFocus(jobIds: ReadonlySet<string>): boolean {
  try {
    const jobId = window.sessionStorage.getItem(playlistSyncFocusKey);
    if (!jobId || !jobIds.has(jobId)) return false;
    window.sessionStorage.removeItem(playlistSyncFocusKey);
    return true;
  } catch {
    return false;
  }
}

export function requestPlaylistSyncDataRefresh(): void {
  window.dispatchEvent(new CustomEvent(playlistSyncDataRefreshEventName));
}
