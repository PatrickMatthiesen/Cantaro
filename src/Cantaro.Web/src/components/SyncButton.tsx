import { useState, useEffect, useCallback, useRef } from 'react';
import { syncApi } from '../services';
import type { SyncStatusResponse, BatchSyncResponse } from '../services/syncApi';
import { platformManager } from '../platforms';
import type { PlatformPlaylist } from '../platforms';

export function SyncButton() {
  const [syncStatus, setSyncStatus] = useState<SyncStatusResponse | null>(null);
  const [availablePlaylists, setAvailablePlaylists] = useState<PlatformPlaylist[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isSyncing, setIsSyncing] = useState(false);
  const [showPlaylistSelector, setShowPlaylistSelector] = useState(false);
  const [selectedPlaylists, setSelectedPlaylists] = useState<Set<string>>(new Set());
  const [syncResult, setSyncResult] = useState<BatchSyncResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [syncProgress, setSyncProgress] = useState(0);
  const [statusUpdates, setStatusUpdates] = useState<string[]>([]);
  const [showStatusDrawer, setShowStatusDrawer] = useState(false);

  const progressIntervalRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const milestoneRef = useRef(0);

  const appendStatus = useCallback((message: string) => {
    setStatusUpdates((previous) => [...previous, message]);
  }, []);

  const stopProgressSimulation = useCallback(() => {
    if (progressIntervalRef.current) {
      clearInterval(progressIntervalRef.current);
      progressIntervalRef.current = null;
    }
  }, []);

  const startProgressSimulation = useCallback(
    (playlistCount: number) => {
      stopProgressSimulation();
      milestoneRef.current = 0;
      setSyncProgress(5);
      setStatusUpdates([
        `Preparing sync for ${playlistCount === 0 ? 'all available' : playlistCount.toString()} playlist(s).`,
      ]);
      setShowStatusDrawer(true);

      const milestones = [
        { progress: 15, message: 'Checking sync permissions and current rate limit window.' },
        { progress: 35, message: 'Fetching latest playlist metadata from YouTube.' },
        { progress: 55, message: 'Matching tracks to canonical TrackIDs.' },
        { progress: 75, message: 'Writing playlist updates and finalizing results.' },
      ];

      progressIntervalRef.current = setInterval(() => {
        setSyncProgress((previous) => {
          const next = Math.min(previous + 4, 90);

          while (
            milestoneRef.current < milestones.length &&
            next >= milestones[milestoneRef.current].progress
          ) {
            appendStatus(milestones[milestoneRef.current].message);
            milestoneRef.current += 1;
          }

          return next;
        });
      }, 500);
    },
    [appendStatus, stopProgressSimulation],
  );

  const loadSyncStatus = useCallback(async () => {
    try {
      const status = await syncApi.getSyncStatus();
      setSyncStatus(status);
      setError(null);
      return status;
    } catch (err) {
      console.error('Failed to load sync status:', err);
      if (err instanceof Error && err.message.includes('Not authenticated')) {
        return null;
      }
      setError(err instanceof Error ? err.message : 'Failed to load sync status');
      return null;
    }
  }, []);

  const loadAvailablePlaylists = useCallback(async () => {
    try {
      const playlists = await platformManager.playlists('youtube', false);
      setAvailablePlaylists(playlists);
    } catch (err) {
      console.error('Failed to load playlists:', err);
    }
  }, []);

  useEffect(() => {
    const init = async () => {
      setIsLoading(true);
      await loadSyncStatus();
      await loadAvailablePlaylists();
      setIsLoading(false);
    };
    init();

    return () => stopProgressSimulation();
  }, [loadAvailablePlaylists, loadSyncStatus, stopProgressSimulation]);

  useEffect(() => {
    const intervalId = setInterval(() => {
      if (!isSyncing) {
        void loadSyncStatus();
      }
    }, 15000);

    return () => clearInterval(intervalId);
  }, [isSyncing, loadSyncStatus]);

  const handleSync = async (playlistIds: string[] | null) => {
    const latestStatus = await loadSyncStatus();
    if (!latestStatus?.overall.canSyncNow) {
      const message =
        latestStatus?.overall.message ??
        `Song sync limit reached. Please wait for the ${latestStatus?.overall.windowMinutes ?? 10}-minute window to reset.`;
      setError(message);
      appendStatus(`Sync blocked: ${message}`);
      setShowStatusDrawer(true);
      return;
    }

    setIsSyncing(true);
    setSyncResult(null);
    setError(null);
    startProgressSimulation(playlistIds?.length ?? 0);

    try {
      const result = await syncApi.batchSync({
        service: 'youtube',
        servicePlaylistIds: playlistIds,
      });

      stopProgressSimulation();
      setSyncProgress(100);
      appendStatus(
        `Sync completed. ${result.successCount} playlist(s) succeeded, ${result.failureCount} failed.`,
      );
      appendStatus(
        `Processed ${result.songsSynced}/${result.songsRequested} requested songs in this run.`,
      );

      setSyncResult(result);
      await loadSyncStatus();
      setShowPlaylistSelector(false);
    } catch (err) {
      stopProgressSimulation();
      setSyncProgress(0);
      const message = err instanceof Error ? err.message : 'Sync failed';
      setError(message);
      appendStatus(`Sync failed: ${message}`);
      await loadSyncStatus();
    } finally {
      setIsSyncing(false);
    }
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

  if (isLoading) {
    return (
      <section className="rounded-3xl border border-white/80 bg-white/70 p-6 text-gray-700 shadow-[0_8px_32px_rgba(0,0,0,0.06),inset_0_1px_0_rgba(255,255,255,0.9)] backdrop-blur-[20px]">
        <p className="text-sm">Loading sync controls…</p>
      </section>
    );
  }

  if (!syncStatus && !error) {
    return (
      <section className="rounded-3xl border border-white/80 bg-white/70 p-6 text-gray-900 shadow-[0_8px_32px_rgba(0,0,0,0.06),inset_0_1px_0_rgba(255,255,255,0.9)] backdrop-blur-[20px]">
        <p className="text-xs uppercase tracking-[0.24em] text-gray-500">Playlist sync</p>
        <h3 className="mt-2 text-xl font-semibold">Ready when you are</h3>
        <p className="mt-1 text-sm text-gray-600">Connect a service workspace to start your first sync run.</p>
      </section>
    );
  }

  const songsSyncedInWindow = syncStatus?.overall.songsSyncedInWindow ?? 0;
  const songSyncLimit = syncStatus?.overall.songSyncLimit ?? 2000;
  const windowMinutes = syncStatus?.overall.windowMinutes ?? 10;
  const remainingSongs = syncStatus?.overall.remainingSongsInWindow ?? 0;
  const windowUsagePercent = Math.min(
    100,
    Math.round((songsSyncedInWindow / Math.max(1, songSyncLimit)) * 100),
  );
  const canSync = Boolean(syncStatus?.overall.canSyncNow) && !isSyncing;

  return (
    <section className="rounded-3xl border border-white/80 bg-white/70 p-6 text-gray-900 shadow-[0_8px_32px_rgba(0,0,0,0.06),inset_0_1px_0_rgba(255,255,255,0.9)] backdrop-blur-[20px]">
      <p className="text-xs uppercase tracking-[0.24em] text-gray-500">Playlist sync</p>
      <h3 className="mt-2 text-xl font-semibold">Keep your playlists aligned</h3>
      <p className="mt-1 text-sm text-gray-600">
        Limit: {songSyncLimit.toLocaleString()} songs per {windowMinutes} minutes.
      </p>

      <div className="mt-4 rounded-2xl bg-white/75 p-4">
        <div className="mb-1 flex items-center justify-between text-xs text-gray-600">
          <span>Current window usage</span>
          <span>
            {songsSyncedInWindow.toLocaleString()} / {songSyncLimit.toLocaleString()} songs
          </span>
        </div>
        <div className="h-2 overflow-hidden rounded-full bg-gray-200">
          <div
            className="h-full bg-gradient-to-r from-indigo-500 to-purple-500"
            style={{ width: `${windowUsagePercent}%` }}
          />
        </div>
        <p className="mt-2 text-xs text-gray-500">{remainingSongs.toLocaleString()} songs remaining in current window.</p>
        {syncStatus?.overall.message ? (
          <p className="mt-2 text-xs font-medium text-amber-700">{syncStatus.overall.message}</p>
        ) : null}
      </div>

      <div className="mt-4 flex flex-wrap gap-2">
        <button
          onClick={() => handleSync(null)}
          disabled={!canSync}
          className="rounded-xl bg-gradient-to-r from-blue-500 to-cyan-500 px-4 py-2 text-sm font-semibold text-white transition hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-40"
        >
          {isSyncing ? `Syncing… ${syncProgress}%` : 'Sync everything'}
        </button>
        <button
          onClick={() => setShowPlaylistSelector(!showPlaylistSelector)}
          disabled={!canSync}
          className="rounded-xl bg-gray-900 px-4 py-2 text-sm font-semibold text-white transition hover:bg-gray-700 disabled:cursor-not-allowed disabled:opacity-40"
        >
          Choose playlists
        </button>
        <button
          onClick={() => setShowStatusDrawer((previous) => !previous)}
          className="rounded-xl bg-white px-4 py-2 text-sm font-semibold text-gray-700 transition hover:bg-gray-100"
        >
          {showStatusDrawer ? 'Hide status' : 'Show status'}
        </button>
      </div>

      {isSyncing || syncProgress > 0 ? (
        <div className="mt-4">
          <div className="mb-1 flex items-center justify-between text-xs text-gray-600">
            <span>Sync progress</span>
            <span>{syncProgress}%</span>
          </div>
          <div className="h-3 overflow-hidden rounded-full bg-gray-200">
            <div
              className="h-full bg-gradient-to-r from-blue-500 via-indigo-500 to-purple-500 transition-all duration-300"
              style={{ width: `${syncProgress}%` }}
            />
          </div>
        </div>
      ) : null}

      {showStatusDrawer && statusUpdates.length > 0 ? (
        <div className="mt-4 rounded-2xl border border-white/80 bg-white/80 p-4">
          <p className="text-xs uppercase tracking-[0.22em] text-gray-500">Sync status updates</p>
          <ul className="mt-3 max-h-44 space-y-2 overflow-y-auto text-sm text-gray-700">
            {statusUpdates.map((update, index) => (
              <li key={`${update}-${index}`} className="rounded-xl bg-white px-3 py-2">
                {update}
              </li>
            ))}
          </ul>
        </div>
      ) : null}

      {error ? (
        <div className="mt-4 rounded-xl border border-rose-300 bg-rose-50 px-4 py-3 text-sm text-rose-700">
          {error}
        </div>
      ) : null}

      {syncResult ? (
        <div className="mt-4 rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-700">
          Processed {syncResult.songsSynced}/{syncResult.songsRequested} requested songs.
          {syncResult.results.some((result) => !result.success) ? (
            <ul className="mt-2 list-disc space-y-1 pl-4">
              {syncResult.results
                .filter((result) => !result.success)
                .map((result) => (
                  <li key={result.servicePlaylistId}>
                    {result.playlistName}: {result.error}
                  </li>
                ))}
            </ul>
          ) : null}
        </div>
      ) : null}

      {showPlaylistSelector && availablePlaylists.length > 0 ? (
        <div className="mt-4 rounded-2xl border border-white/80 bg-white/80 p-4">
          <p className="text-sm font-semibold text-gray-800">Choose playlists</p>
          <div className="mt-3 max-h-64 space-y-2 overflow-y-auto">
            {availablePlaylists.map((playlist) => (
              <label
                key={playlist.id}
                className="flex cursor-pointer items-center gap-3 rounded-lg border border-gray-200 bg-white p-3 transition hover:border-gray-300"
              >
                <input
                  type="checkbox"
                  checked={selectedPlaylists.has(playlist.id)}
                  onChange={() => togglePlaylistSelection(playlist.id)}
                  className="h-4 w-4 rounded border-gray-300 text-indigo-600 focus:ring-2 focus:ring-indigo-500"
                />
                <div className="flex-1">
                  <p className="text-sm font-medium text-gray-800">{playlist.title}</p>
                  <p className="text-xs text-gray-500">{playlist.itemCount} songs</p>
                </div>
              </label>
            ))}
          </div>
          <div className="mt-4 flex gap-2">
            <button
              onClick={() => handleSync(Array.from(selectedPlaylists))}
              disabled={selectedPlaylists.size === 0 || isSyncing}
              className="flex-1 rounded-xl bg-gradient-to-r from-indigo-500 to-purple-500 px-4 py-2 text-sm font-semibold text-white transition hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-40"
            >
              Sync selected
            </button>
            <button
              onClick={() => setShowPlaylistSelector(false)}
              className="rounded-xl bg-gray-100 px-4 py-2 text-sm font-semibold text-gray-700 transition hover:bg-gray-200"
            >
              Cancel
            </button>
          </div>
        </div>
      ) : null}
    </section>
  );
}
