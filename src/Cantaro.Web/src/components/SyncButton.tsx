import { useState, useEffect, useCallback } from 'react';
import { syncApi, youtubeApi } from '../services';
import type { SyncStatusResponse, BatchSyncResponse } from '../services/syncApi';
import type { YouTubePlaylist } from '../services/youtubeApi';

export function SyncButton() {
  const [syncStatus, setSyncStatus] = useState<SyncStatusResponse | null>(null);
  const [availablePlaylists, setAvailablePlaylists] = useState<YouTubePlaylist[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isSyncing, setIsSyncing] = useState(false);
  const [showPlaylistSelector, setShowPlaylistSelector] = useState(false);
  const [selectedPlaylists, setSelectedPlaylists] = useState<Set<string>>(new Set());
  const [syncResult, setSyncResult] = useState<BatchSyncResponse | null>(null);
  const [error, setError] = useState<string | null>(null);

  const loadSyncStatus = useCallback(async () => {
    try {
      const status = await syncApi.getSyncStatus();
      setSyncStatus(status);
      setError(null);
      return status;
    } catch (err) {
      console.error('Failed to load sync status:', err);
      setError('Failed to load sync status');
      return null;
    }
  }, []);

  const loadAvailablePlaylists = useCallback(async () => {
    try {
      const playlists = await youtubeApi.getPlaylists();
      setAvailablePlaylists(playlists);
    } catch (err) {
      console.error('Failed to load playlists:', err);
    }
  }, []);

  useEffect(() => {
    const init = async () => {
      setIsLoading(true);
      const status = await loadSyncStatus();
      await loadAvailablePlaylists();
      setIsLoading(false);

      // Auto-sync if needed and allowed
      if (status?.overall.needsAutoSync && status?.overall.canSyncNow) {
        console.log('Auto-triggering sync (>24h since last sync)');
        handleSync(null); // null = sync all
      }
    };
    init();
  }, [loadSyncStatus, loadAvailablePlaylists]);

  const handleSync = async (playlistIds: string[] | null) => {
    if (!syncStatus?.overall.canSyncNow) {
      return;
    }

    setIsSyncing(true);
    setSyncResult(null);
    setError(null);

    try {
      const result = await syncApi.batchSync({
        service: 'youtube',
        servicePlaylistIds: playlistIds,
      });

      setSyncResult(result);
      await loadSyncStatus(); // Refresh status after sync
      setShowPlaylistSelector(false);
    } catch (err) {
      console.error('Sync failed:', err);
      setError(err instanceof Error ? err.message : 'Sync failed');
    } finally {
      setIsSyncing(false);
    }
  };

  const handleSyncAllClick = () => {
    handleSync(null);
  };

  const handleSyncSelectedClick = () => {
    if (selectedPlaylists.size === 0) {
      setError('Please select at least one playlist');
      return;
    }
    handleSync(Array.from(selectedPlaylists));
  };

  const togglePlaylistSelection = (playlistId: string) => {
    const newSet = new Set(selectedPlaylists);
    if (newSet.has(playlistId)) {
      newSet.delete(playlistId);
    } else {
      newSet.add(playlistId);
    }
    setSelectedPlaylists(newSet);
  };

  const formatLastSync = (lastSyncedAt: string | null) => {
    if (!lastSyncedAt) return 'Never';
    
    const date = new Date(lastSyncedAt);
    const now = new Date();
    const diffMs = now.getTime() - date.getTime();
    const diffMins = Math.floor(diffMs / 60000);
    const diffHours = Math.floor(diffMins / 60);
    const diffDays = Math.floor(diffHours / 24);

    if (diffMins < 1) return 'Just now';
    if (diffMins < 60) return `${diffMins}m ago`;
    if (diffHours < 24) return `${diffHours}h ago`;
    if (diffDays === 1) return 'Yesterday';
    if (diffDays < 7) return `${diffDays}d ago`;
    
    return date.toLocaleDateString();
  };

  if (isLoading) {
    return (
      <div className="glass-panel p-6">
        <div className="flex items-center gap-3">
          <span className="h-2 w-2 animate-pulse rounded-full bg-emerald-400" />
          <p className="text-sm text-slate-300">Loading sync status...</p>
        </div>
      </div>
    );
  }

  const canSync = syncStatus?.overall.canSyncNow && !isSyncing;

  return (
    <div className="glass-panel p-6">
      <div className="flex items-center justify-between">
        <div>
          <p className="text-xs uppercase tracking-[0.5em] text-slate-400">Playlist sync</p>
          <p className="mt-2 text-lg font-semibold text-white">
            Last sync: {formatLastSync(syncStatus?.overall.lastSyncedAt || null)}
          </p>
          {syncStatus?.overall.message && (
            <p className="mt-1 text-sm text-slate-400">{syncStatus.overall.message}</p>
          )}
          {syncStatus?.playlists && syncStatus.playlists.length > 0 && (
            <p className="mt-1 text-sm text-slate-400">
              {syncStatus.playlists.length} playlist{syncStatus.playlists.length !== 1 ? 's' : ''} tracked
            </p>
          )}
        </div>

        <div className="flex gap-2">
          <button
            onClick={handleSyncAllClick}
            disabled={!canSync}
            className="inline-flex items-center gap-2 rounded-xl border border-white/15 bg-white/5 px-4 py-2 text-sm font-semibold text-white transition hover:border-white/30 hover:bg-white/10 disabled:cursor-not-allowed disabled:opacity-40"
          >
            {isSyncing ? (
              <>
                <span className="h-2 w-2 animate-pulse rounded-full bg-emerald-400" />
                Syncing...
              </>
            ) : (
              <>
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                  <path d="M21.5 2v6h-6M2.5 22v-6h6M2 11.5a10 10 0 0 1 18.8-4.3M22 12.5a10 10 0 0 1-18.8 4.2" />
                </svg>
                Sync All
              </>
            )}
          </button>

          <button
            onClick={() => setShowPlaylistSelector(!showPlaylistSelector)}
            disabled={!canSync}
            className="inline-flex items-center gap-2 rounded-xl border border-white/15 bg-white/5 px-4 py-2 text-sm font-semibold text-white transition hover:border-white/30 hover:bg-white/10 disabled:cursor-not-allowed disabled:opacity-40"
          >
            Select Playlists
          </button>
        </div>
      </div>

      {error && (
        <div className="mt-4 rounded-xl border border-red-500/30 bg-red-500/10 px-4 py-3 text-sm text-red-300">
          {error}
        </div>
      )}

      {syncResult && (
        <div className="mt-4 rounded-xl border border-emerald-500/30 bg-emerald-500/10 px-4 py-3">
          <p className="text-sm font-semibold text-emerald-300">
            Sync completed: {syncResult.successCount} succeeded, {syncResult.failureCount} failed
          </p>
          {syncResult.results.filter(r => !r.success).length > 0 && (
            <ul className="mt-2 space-y-1 text-xs text-slate-300">
              {syncResult.results.filter(r => !r.success).map(r => (
                <li key={r.servicePlaylistId}>
                  ✗ {r.playlistName}: {r.error}
                </li>
              ))}
            </ul>
          )}
        </div>
      )}

      {showPlaylistSelector && availablePlaylists.length > 0 && (
        <div className="mt-4 rounded-xl border border-white/10 bg-slate-900/50 p-4">
          <p className="mb-3 text-sm font-semibold text-white">Select playlists to sync:</p>
          <div className="max-h-64 space-y-2 overflow-y-auto">
            {availablePlaylists.map(playlist => (
              <label
                key={playlist.id}
                className="flex cursor-pointer items-center gap-3 rounded-lg border border-white/10 bg-slate-950/50 p-3 transition hover:border-white/20 hover:bg-slate-950/70"
              >
                <input
                  type="checkbox"
                  checked={selectedPlaylists.has(playlist.id)}
                  onChange={() => togglePlaylistSelection(playlist.id)}
                  className="h-4 w-4 rounded border-white/20 bg-white/10 text-emerald-500 focus:ring-2 focus:ring-emerald-500"
                />
                <div className="flex-1">
                  <p className="text-sm font-medium text-white">{playlist.title}</p>
                  <p className="text-xs text-slate-400">{playlist.itemCount} tracks</p>
                </div>
              </label>
            ))}
          </div>
          <div className="mt-4 flex gap-2">
            <button
              onClick={handleSyncSelectedClick}
              disabled={selectedPlaylists.size === 0 || isSyncing}
              className="flex-1 rounded-xl border border-emerald-500/30 bg-emerald-500/20 px-4 py-2 text-sm font-semibold text-emerald-300 transition hover:bg-emerald-500/30 disabled:cursor-not-allowed disabled:opacity-40"
            >
              Sync {selectedPlaylists.size} selected
            </button>
            <button
              onClick={() => setShowPlaylistSelector(false)}
              className="rounded-xl border border-white/15 bg-white/5 px-4 py-2 text-sm font-semibold text-white transition hover:border-white/30 hover:bg-white/10"
            >
              Cancel
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
