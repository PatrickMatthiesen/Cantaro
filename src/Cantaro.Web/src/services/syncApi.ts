export interface SyncStatusInfo {
  lastSyncedAt: string | null;
  needsAutoSync: boolean;
  canSyncNow: boolean;
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

export interface BatchSyncRequest {
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
}

export const syncApi = {
  async getSyncStatus(): Promise<SyncStatusResponse> {
    const response = await fetch('/api/sync/status', {
      credentials: 'include',
    });

    if (!response.ok) {
      throw new Error('Failed to fetch sync status');
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
};
