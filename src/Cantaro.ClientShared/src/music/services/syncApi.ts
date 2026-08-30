export interface SyncStatusInfo {
  lastSyncedAt: string | null;
  needsAutoSync: boolean;
  canSyncNow: boolean;
  songsSyncedInWindow: number;
  remainingSongsInWindow: number;
  songSyncLimit: number;
  windowMinutes: number;
  message?: string;
}

export interface PlaylistSyncInfo {
  playlistId: string;
  name: string;
  service: string;
  servicePlaylistId: string;
  lastSyncedAt: string | null;
  lastSyncStatus: string | null;
}

export interface SyncStatusResponse {
  overall: SyncStatusInfo;
  playlists: PlaylistSyncInfo[];
}

interface BatchSyncRequest {
  service: string;
  servicePlaylistIds?: string[] | null; // null or empty = sync all
}

export type MusicSyncJobStatus = 'queued' | 'running' | 'completed' | 'failed';

export interface MusicSyncJobPlaylistResult {
  servicePlaylistId: string;
  playlistName: string;
  success: boolean;
  cantaroPlaylistId?: string | null;
  errorCode?: string | null;
  errorMessage?: string | null;
  retryable: boolean;
}

export interface MusicSyncJobResponse {
  id: string;
  status: MusicSyncJobStatus;
  service: string;
  playlistCount: number;
  songCount: number;
  processedPlaylistCount: number;
  processedSongCount: number;
  successCount: number;
  failureCount: number;
  playlistNames: string[];
  results: MusicSyncJobPlaylistResult[];
  currentPlaylistName?: string | null;
  currentSongName?: string | null;
  errorMessage?: string | null;
  createdAt: string;
  updatedAt: string;
  startedAt?: string | null;
  completedAt?: string | null;
}

export interface MusicSyncJobListResponse {
  items: MusicSyncJobResponse[];
  nextCursor: string | null;
}

export interface MusicSyncJobListOptions {
  service?: string;
  status?: MusicSyncJobStatus;
  cursor?: string;
  limit?: number;
}

export const syncApi = {
  async getSyncStatus(service?: string): Promise<SyncStatusResponse> {
    const query = service ? `?service=${encodeURIComponent(service)}` : '';
    const response = await fetch(`/api/sync/status${query}`, {
      credentials: 'include',
    });

    if (!response.ok) {
      if (response.status === 401) {
        throw new Error('Not authenticated');
      }
      throw new Error(`Failed to fetch sync status: ${response.statusText}`);
    }

    return response.json();
  },

  async createSyncJob(request: BatchSyncRequest): Promise<MusicSyncJobResponse> {
    const response = await fetch('/api/sync/jobs', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      credentials: 'include',
      body: JSON.stringify(request),
    });

    if (!response.ok) {
      const error = await response.json().catch(() => ({ error: 'Failed to start playlist sync' }));
      throw new Error(error.error || 'Failed to start playlist sync');
    }

    return response.json();
  },

  async getSyncJob(jobId: string): Promise<MusicSyncJobResponse> {
    const response = await fetch(`/api/sync/jobs/${encodeURIComponent(jobId)}`, {
      credentials: 'include',
    });
    if (!response.ok) {
      throw new Error(response.status === 404 ? 'Sync job not found' : 'Failed to fetch sync job');
    }
    return response.json();
  },

  async listSyncJobs(options: MusicSyncJobListOptions = {}): Promise<MusicSyncJobListResponse> {
    const query = new URLSearchParams();
    if (options.service) query.set('service', options.service);
    if (options.status) query.set('status', options.status);
    if (options.cursor) query.set('cursor', options.cursor);
    if (options.limit !== undefined) query.set('limit', options.limit.toString());
    const suffix = query.size > 0 ? `?${query.toString()}` : '';
    const response = await fetch(`/api/sync/jobs${suffix}`, { credentials: 'include' });
    if (!response.ok) {
      throw new Error(response.status === 401 ? 'Not authenticated' : 'Failed to fetch sync history');
    }
    return response.json();
  },
};
