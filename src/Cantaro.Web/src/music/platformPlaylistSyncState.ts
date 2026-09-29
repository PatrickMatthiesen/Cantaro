import type { MusicSyncJobResponse, PlaylistSyncInfo, SyncStatusResponse } from '@cantaro/client-shared/music';

export type SyncService = 'youtube' | 'spotify';

export function isActive(job: MusicSyncJobResponse): boolean {
  return job.status === 'queued' || job.status === 'running';
}

export function syncMappings(status: SyncStatusResponse | null, service: SyncService): Map<string, PlaylistSyncInfo> {
  return new Map(status?.playlists
    .filter((mapping) => mapping.service === service)
    .map((mapping) => [mapping.servicePlaylistId, mapping]) ?? []);
}

export function importBlockReason(status: SyncStatusResponse | null, loadError: string | null, mapping: PlaylistSyncInfo | undefined, isLoading = false): string | null {
  if (isLoading) return 'Wait for playlist sync status to load.';
  if (!status) return loadError ?? 'Load sync status before importing this playlist.';
  if (mapping?.syncMode === 'from_cantaro') return 'This playlist is a Cantaro export and cannot be imported.';
  return null;
}

export function isPlaylistBusy(playlistId: string, submitting: Set<string>, jobs: Map<string, MusicSyncJobResponse>): boolean {
  const job = jobs.get(playlistId);
  return submitting.has(playlistId) || Boolean(job && isActive(job));
}

export function pendingPlaylistIds(submitting: Set<string>, jobs: Map<string, MusicSyncJobResponse>): Set<string> {
  const pending = new Set(submitting);
  for (const [playlistId, job] of jobs) {
    if (isActive(job)) pending.add(playlistId);
  }
  return pending;
}

export function jobFailureMessage(job: MusicSyncJobResponse, playlistId: string): string | null {
  const failedResult = job.results.find((result) => result.servicePlaylistId === playlistId && !result.success);
  if (job.status !== 'failed' && !failedResult && job.failureCount === 0) return null;
  return failedResult?.errorMessage ?? job.errorMessage ?? 'Playlist import failed.';
}
