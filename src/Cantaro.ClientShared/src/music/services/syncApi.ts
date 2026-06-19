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

export interface BatchSyncResult {
  servicePlaylistId: string;
  playlistName: string;
  success: boolean;
  error?: string;
  cantaroPlaylistId?: string;
}

export interface BatchSyncResponse {
  results: BatchSyncResult[];
  successCount: number;
  failureCount: number;
  songsRequested: number;
  songsSynced: number;
}

export type MusicSyncJobStatus = 'queued' | 'running' | 'completed' | 'failed';

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
  currentPlaylistName?: string | null;
  errorMessage?: string | null;
  createdAt: string;
  updatedAt: string;
  startedAt?: string | null;
  completedAt?: string | null;
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

  async batchSync(request: BatchSyncRequest): Promise<BatchSyncResponse> {
    const response = await fetch('/api/sync/batch', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      credentials: 'include',
      body: JSON.stringify(request),
    });

    if (!response.ok) {
      const error = await response.json().catch(() => ({ error: 'Failed to sync playlists' }));
      throw new Error(error.error || 'Failed to sync playlists');
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
};
