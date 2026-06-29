import { Link, useNavigate } from '@tanstack/react-router';
import {
  MusicPlatformIcon,
  MusicUiIcon,
  platformCatalog,
  platformManager,
  syncApi,
  type BatchSyncResponse,
  type MusicLibraryResponse,
  type PlatformId,
  type PlatformPlaylist,
  type SyncStatusResponse,
} from '@cantaro/client-shared/music';
import { GlassCard, StatusBadge } from '@cantaro/client-shared/ui';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { MusicLibraryPanel } from './MusicLibraryPanel';
import { MusicPageShell } from './MusicPageShell';
import { progressFromSyncJob, writePlaylistSyncProgress } from './playlistSyncProgress';
import { useConnectedMusicPlatforms } from './useConnectedMusicPlatforms';
import { useAuth } from '../contexts/AuthContext';

const platformById = new Map(platformCatalog.map((platform) => [platform.id, platform]));

function getSyncErrorMessage(error: unknown, fallback: string): string {
  return error instanceof Error ? error.message : fallback;
}

function playlistArtwork(playlist: PlatformPlaylist): string | null {
  return playlist.thumbnailUrl || null;
}

function toggleSetValue(previous: Set<string>, value: string): Set<string> {
  const next = new Set(previous);
  if (next.has(value)) {
    next.delete(value);
  } else {
    next.add(value);
  }

  return next;
}

function syncUnavailableMessage(status: SyncStatusResponse | null): string {
  return status?.overall.message ?? 'Cantaro could not confirm that playlist sync can run right now. Please try again.';
}

function selectSourcePlatform(
  requestedPlatformId: PlatformId | undefined,
  connectedPlatformIds: PlatformId[],
  fallbackPlatformId: PlatformId,
): PlatformId {
  if (requestedPlatformId && connectedPlatformIds.includes(requestedPlatformId)) {
    return requestedPlatformId;
  }

  return fallbackPlatformId;
}

function selectInitialSourcePlatform(requestedPlatformId: PlatformId | undefined, fallbackPlatformId: PlatformId): PlatformId {
  return requestedPlatformId ?? fallbackPlatformId;
}

function ToggleRow({
  title,
  detail,
  enabled,
  onToggle,
}: {
  title: string;
  detail: string;
  enabled: boolean;
  onToggle: () => void;
}) {
  return (
    <button
      type="button"
      className="flex w-full items-center justify-between gap-4 border-b border-slate-200/70 px-4 py-3 text-left last:border-b-0"
      onClick={onToggle}
    >
      <span className="min-w-0">
        <span className="block text-sm font-black text-slate-800">{title}</span>
        <span className="block text-xs font-semibold text-slate-500">{detail}</span>
      </span>
      <span className={`relative h-6 w-11 shrink-0 rounded-full transition ${enabled ? 'bg-violet-600' : 'bg-slate-300'}`}>
        <span className={`absolute top-1 h-4 w-4 rounded-full bg-white shadow-sm transition ${enabled ? 'left-6' : 'left-1'}`} />
      </span>
    </button>
  );
}

function SourcePlatformSelector({
  sourcePlatformId,
  connectedPlatformIds,
  selectedPlaylists,
  targetPlatformIds,
  onChange,
}: {
  sourcePlatformId: PlatformId;
  connectedPlatformIds: PlatformId[];
  selectedPlaylists: PlatformPlaylist[];
  targetPlatformIds: PlatformId[];
  onChange: (platformId: PlatformId) => void;
}) {
  const connectedSourcePlatforms = platformCatalog.filter((platform) => platform.implemented && connectedPlatformIds.includes(platform.id));

  return (
    <GlassCard className="p-4">
      <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_340px]">
        <div>
          <p className="text-xs font-black tracking-[0.18em] text-slate-500 uppercase">Source platform</p>
          <div className="mt-3 grid gap-2 sm:grid-cols-2 2xl:grid-cols-4">
            {connectedSourcePlatforms.map((platform) => {
              const isSelected = sourcePlatformId === platform.id;
              return (
                <button
                  key={platform.id}
                  type="button"
                  className={`flex items-center gap-3 rounded-2xl border px-3 py-3 text-left transition ${
                    isSelected ? 'border-violet-300 bg-white shadow-[0_14px_34px_rgba(88,74,150,0.12)]' : 'border-white/70 bg-white/55 hover:bg-white'
                  }`}
                  onClick={() => onChange(platform.id)}
                >
                  <span className={`flex h-10 w-10 items-center justify-center rounded-xl bg-linear-to-br ${platform.gradient} text-white`}>
                    <MusicPlatformIcon platformId={platform.iconId} className="h-5 w-5" />
                  </span>
                  <span className="min-w-0 flex-1">
                    <span className="block truncate text-sm font-black text-slate-900">{platform.name}</span>
                    <span className="block text-xs font-semibold text-emerald-600">Connected</span>
                  </span>
                  {isSelected ? <MusicUiIcon name="squareCheck" className="h-5 w-5 text-violet-600" /> : null}
                </button>
              );
            })}
          </div>
        </div>
        <div className="border-t border-[#e3def8] pt-4 xl:border-t-0 xl:border-l xl:pt-0 xl:pl-4">
          <SyncSetupMetrics selectedPlaylists={selectedPlaylists} targetPlatformIds={targetPlatformIds} />
        </div>
      </div>
      {connectedSourcePlatforms.length === 0 ? (
        <div className="mt-3 rounded-2xl bg-white/70 p-4 text-sm font-semibold text-slate-500">
          Connect YouTube Music first, then return here to choose source playlists.
        </div>
      ) : null}
    </GlassCard>
  );
}

function SyncSetupMetric({
  icon,
  value,
  label,
}: {
  icon: 'refresh' | 'cable' | 'music' | 'clock';
  value: string;
  label: string;
}) {
  return (
    <div className="flex min-w-0 items-center gap-2 rounded-2xl bg-white/64 px-3 py-2.5 shadow-[0_10px_28px_rgba(88,74,150,0.06)]">
      <MusicUiIcon name={icon} className="h-4 w-4 shrink-0 text-violet-600" />
      <div className="min-w-0">
        <p className="truncate text-sm font-black text-slate-900">{value}</p>
        <p className="truncate text-[11px] font-semibold text-slate-500">{label}</p>
      </div>
    </div>
  );
}

function SyncSetupMetrics({
  selectedPlaylists,
  targetPlatformIds,
}: {
  selectedPlaylists: PlatformPlaylist[];
  targetPlatformIds: PlatformId[];
}) {
  const totalSongs = selectedPlaylists.reduce((sum, playlist) => sum + playlist.itemCount, 0);

  return (
    <div>
      <p className="text-xs font-black tracking-[0.18em] text-slate-500 uppercase">Sync summary</p>
      <div className="mt-3 grid grid-cols-2 gap-2">
        <SyncSetupMetric icon="refresh" value={selectedPlaylists.length.toLocaleString()} label="Source playlists" />
        <SyncSetupMetric icon="cable" value={targetPlatformIds.length.toLocaleString()} label="Other platforms" />
        <SyncSetupMetric icon="music" value={totalSongs.toLocaleString()} label="Total songs" />
        <SyncSetupMetric icon="clock" value="~2 min" label="Estimated time" />
      </div>
    </div>
  );
}

function PlaylistPickerHeader({
  sourcePlatformId,
  playlists,
  selectedPlaylistIds,
  isLoading,
  onSelectAll,
}: {
  sourcePlatformId: PlatformId;
  playlists: PlatformPlaylist[];
  selectedPlaylistIds: Set<string>;
  isLoading: boolean;
  onSelectAll: () => void;
}) {
  const hasSelectedEveryPlaylist = playlists.length > 0 && selectedPlaylistIds.size === playlists.length;

  return (
    <div className="flex items-start justify-between gap-2">
      <div className="min-w-0 flex-1">
        <h2 className="text-lg font-black text-slate-950">1. Source playlists</h2>
        <p className="mt-1 text-sm font-semibold text-slate-500">Choose playlists to sync from {platformById.get(sourcePlatformId)?.name}.</p>
      </div>
      <button
        type="button"
        className="shrink-0 rounded-2xl bg-white px-4 py-2 text-sm font-black whitespace-nowrap text-violet-700 transition hover:bg-violet-50 disabled:cursor-not-allowed disabled:text-slate-400"
        disabled={isLoading || playlists.length === 0}
        onClick={onSelectAll}
      >
        {hasSelectedEveryPlaylist ? 'Clear' : 'Select all'}
      </button>
    </div>
  );
}

function LoadingPlaylistsState() {
  return (
    <div className="flex min-h-56 items-center justify-center rounded-2xl bg-white/60">
      <div className="flex items-center gap-2 text-sm font-semibold text-slate-500">
        <MusicUiIcon name="loader" className="h-4 w-4 animate-spin" />
        Loading playlists
      </div>
    </div>
  );
}

function EmptyPlaylistsState() {
  return (
    <div className="flex min-h-56 flex-col items-center justify-center rounded-2xl bg-white/60 text-center">
      <MusicUiIcon name="listMusic" className="h-9 w-9 text-slate-300" />
      <p className="mt-2 text-sm font-black text-slate-800">No playlists found</p>
      <p className="mt-1 text-xs font-semibold text-slate-500">Refresh the platform connection and try again.</p>
    </div>
  );
}

function PlaylistArtworkThumbnail({ playlist, sourcePlatformId }: { playlist: PlatformPlaylist; sourcePlatformId: PlatformId }) {
  const artwork = playlistArtwork(playlist);

  if (artwork) {
    return <img src={artwork} alt="" className="h-11 w-11 rounded-xl object-cover" />;
  }

  return (
    <span className="flex h-11 w-11 items-center justify-center rounded-xl bg-white shadow-sm">
      <MusicPlatformIcon platformId={sourcePlatformId} className="h-6 w-6" />
    </span>
  );
}

function PlaylistPickerRow({
  playlist,
  sourcePlatformId,
  isSelected,
  onTogglePlaylist,
}: {
  playlist: PlatformPlaylist;
  sourcePlatformId: PlatformId;
  isSelected: boolean;
  onTogglePlaylist: (playlistId: string) => void;
}) {
  return (
    <button
      type="button"
      className={`grid w-full grid-cols-[auto_44px_1fr] items-center gap-3 rounded-2xl border px-3 py-2.5 text-left transition ${
        isSelected ? 'border-violet-300 bg-white shadow-[0_12px_30px_rgba(88,74,150,0.1)]' : 'border-white/70 bg-white/58 hover:bg-white'
      }`}
      onClick={() => onTogglePlaylist(playlist.id)}
    >
      <span className={`flex h-5 w-5 items-center justify-center rounded-md border ${isSelected ? 'border-violet-600 bg-violet-600 text-white' : 'border-slate-300 bg-white text-transparent'}`}>
        <MusicUiIcon name="squareCheck" className="h-3.5 w-3.5" />
      </span>
      <PlaylistArtworkThumbnail playlist={playlist} sourcePlatformId={sourcePlatformId} />
      <span className="min-w-0">
        <span className="block truncate text-sm font-black text-slate-900">{playlist.title}</span>
        <span className="block text-xs font-semibold text-slate-500">{playlist.itemCount.toLocaleString()} songs</span>
      </span>
    </button>
  );
}

function PlaylistPicker({
  sourcePlatformId,
  playlists,
  selectedPlaylistIds,
  isLoading,
  onTogglePlaylist,
  onSelectAll,
}: {
  sourcePlatformId: PlatformId;
  playlists: PlatformPlaylist[];
  selectedPlaylistIds: Set<string>;
  isLoading: boolean;
  onTogglePlaylist: (playlistId: string) => void;
  onSelectAll: () => void;
}) {
  return (
    <GlassCard className="p-4">
      <PlaylistPickerHeader
        sourcePlatformId={sourcePlatformId}
        playlists={playlists}
        selectedPlaylistIds={selectedPlaylistIds}
        isLoading={isLoading}
        onSelectAll={onSelectAll}
      />

      <div className="mt-4 max-h-[560px] space-y-2 overflow-y-auto pr-1">
        {isLoading ? (
          <LoadingPlaylistsState />
        ) : playlists.length > 0 ? playlists.map((playlist) => {
          const isSelected = selectedPlaylistIds.has(playlist.id);

          return (
            <PlaylistPickerRow
              key={playlist.id}
              playlist={playlist}
              sourcePlatformId={sourcePlatformId}
              isSelected={isSelected}
              onTogglePlaylist={onTogglePlaylist}
            />
          );
        }) : (
          <EmptyPlaylistsState />
        )}
      </div>
    </GlassCard>
  );
}

function RulesPanel({
  keepOrder,
  keepMetadata,
  hideUnavailable,
  scheduledSync,
  onToggleKeepOrder,
  onToggleKeepMetadata,
  onToggleHideUnavailable,
  onToggleScheduledSync,
}: {
  keepOrder: boolean;
  keepMetadata: boolean;
  hideUnavailable: boolean;
  scheduledSync: boolean;
  onToggleKeepOrder: () => void;
  onToggleKeepMetadata: () => void;
  onToggleHideUnavailable: () => void;
  onToggleScheduledSync: () => void;
}) {
  return (
    <GlassCard className="p-4">
      <h2 className="text-lg font-black text-slate-950">2. Sync rules</h2>
      <p className="mt-1 text-sm font-semibold text-slate-500">This run imports the selected source playlists into Cantaro. These rules describe how Cantaro should preserve them for later platform updates.</p>

      <div className="mt-4 grid gap-2 sm:grid-cols-2">
        <div className="rounded-2xl border border-violet-300 bg-white p-4 text-left shadow-[0_12px_30px_rgba(88,74,150,0.08)]">
          <div className="flex items-center justify-between gap-3">
            <MusicUiIcon name="repeat" className="h-5 w-5 text-violet-600" />
            <MusicUiIcon name="squareCheck" className="h-5 w-5 text-violet-600" />
          </div>
          <p className="mt-3 text-sm font-black text-slate-900">Mirror sync</p>
          <p className="mt-1 text-xs leading-5 font-semibold text-slate-500">Keep selected playlists aligned through Cantaro.</p>
        </div>
        <div className="rounded-2xl border border-white/80 bg-white/52 p-4 text-left opacity-65">
          <div className="flex items-center justify-between gap-3">
            <MusicUiIcon name="library" className="h-5 w-5 text-slate-500" />
            <span className="text-xs font-black text-slate-400">Later</span>
          </div>
          <p className="mt-3 text-sm font-black text-slate-900">Update library</p>
          <p className="mt-1 text-xs leading-5 font-semibold text-slate-500">Add tracks without removing existing entries.</p>
        </div>
      </div>

      <div className="mt-4 overflow-hidden rounded-2xl bg-white/70">
        <ToggleRow title="Keep song order" detail="Preserve source playlist order in Cantaro." enabled={keepOrder} onToggle={onToggleKeepOrder} />
        <ToggleRow title="Keep playlist metadata" detail="Sync title, description, and artwork where available." enabled={keepMetadata} onToggle={onToggleKeepMetadata} />
        <ToggleRow title="Hide unavailable tracks" detail="Exclude missing or region-blocked songs from previews." enabled={hideUnavailable} onToggle={onToggleHideUnavailable} />
        <ToggleRow title="Scheduled sync" detail="Keep this sync ready for automatic runs later." enabled={scheduledSync} onToggle={onToggleScheduledSync} />
      </div>
    </GlassCard>
  );
}

function PreviewTargets({ targetPlatformIds }: { targetPlatformIds: PlatformId[] }) {
  if (targetPlatformIds.length === 0) {
    return <span className="rounded-full bg-amber-50 px-3 py-1.5 text-amber-700">Connect another platform for cross-platform sync</span>;
  }

  return targetPlatformIds.map((platformId) => (
    <span key={platformId} className="inline-flex items-center gap-2 rounded-full bg-white px-3 py-1.5 text-slate-700 shadow-sm">
      <MusicPlatformIcon platformId={platformId} className="h-4 w-4" />
      {platformById.get(platformId)?.name}
    </span>
  ));
}

function PreviewPlaylistList({ selectedPlaylists }: { selectedPlaylists: PlatformPlaylist[] }) {
  if (selectedPlaylists.length === 0) {
    return <div className="rounded-2xl bg-white/60 p-4 text-sm font-semibold text-slate-500">Pick at least one playlist to preview the sync.</div>;
  }

  return (
    <>
      {selectedPlaylists.slice(0, 4).map((playlist) => (
        <div key={playlist.id} className="flex items-center justify-between gap-3 rounded-2xl bg-white/60 px-3 py-2">
          <span className="min-w-0">
            <span className="block truncate text-sm font-black text-slate-800">{playlist.title}</span>
            <span className="block text-xs font-semibold text-slate-500">{playlist.itemCount.toLocaleString()} songs</span>
          </span>
          <span className="rounded-full bg-violet-100 px-3 py-1 text-xs font-black text-violet-700">Add</span>
        </div>
      ))}
      {selectedPlaylists.length > 4 ? (
        <p className="text-center text-xs font-black text-violet-600">+ {selectedPlaylists.length - 4} more playlists</p>
      ) : null}
    </>
  );
}

function SyncPreview({
  sourcePlatformId,
  selectedPlaylists,
  targetPlatformIds,
  syncStatus,
  syncResult,
}: {
  sourcePlatformId: PlatformId;
  selectedPlaylists: PlatformPlaylist[];
  targetPlatformIds: PlatformId[];
  syncStatus: SyncStatusResponse | null;
  syncResult: BatchSyncResponse | null;
}) {
  const totalSongs = selectedPlaylists.reduce((sum, playlist) => sum + playlist.itemCount, 0);

  return (
    <GlassCard className="p-4">
      <div className="flex items-center justify-between gap-3">
        <div>
          <h2 className="text-lg font-black text-slate-950">3. Sync preview</h2>
          <p className="mt-1 text-sm font-semibold text-slate-500">Review what Cantaro will import now and which connected platforms can receive this playlist later.</p>
        </div>
        <MusicUiIcon name="cloudSync" className="h-5 w-5 text-violet-500" />
      </div>

      <div className="mt-4 rounded-2xl bg-white/72 p-4">
        <div className="flex items-center gap-3">
          <span className="flex h-10 w-10 items-center justify-center rounded-xl bg-white shadow-sm">
            <MusicPlatformIcon platformId={sourcePlatformId} className="h-5 w-5" />
          </span>
          <div className="min-w-0 flex-1">
            <p className="text-sm font-black text-slate-900">{platformById.get(sourcePlatformId)?.name} -&gt; Cantaro</p>
            <p className="text-xs font-semibold text-slate-500">{selectedPlaylists.length.toLocaleString()} playlists · {totalSongs.toLocaleString()} songs selected</p>
          </div>
        </div>

        <div className="mt-4 flex flex-wrap items-center gap-2 text-xs font-black text-slate-500">
          <span>Cantaro can map to</span>
          <PreviewTargets targetPlatformIds={targetPlatformIds} />
        </div>
      </div>

      <div className="mt-4 grid gap-2">
        <PreviewPlaylistList selectedPlaylists={selectedPlaylists} />
      </div>

      {syncStatus?.overall.message ? (
        <div className="mt-4 rounded-2xl bg-amber-50 px-4 py-3 text-sm font-semibold text-amber-700">{syncStatus.overall.message}</div>
      ) : null}

      {syncResult ? (
        <div className="mt-4 rounded-2xl bg-emerald-50 px-4 py-3 text-sm font-semibold text-emerald-700">
          Sync completed: {syncResult.successCount} succeeded, {syncResult.failureCount} failed, {syncResult.songsSynced.toLocaleString()} songs processed.
        </div>
      ) : null}
    </GlassCard>
  );
}

interface SyncRuleDefaults {
  keepPlaylistOrder?: boolean;
  keepPlaylistMetadata?: boolean;
  hideUnavailableTracks?: boolean;
  scheduledSync?: boolean;
}

const defaultSyncRuleDefaults: Required<SyncRuleDefaults> = {
  keepPlaylistOrder: true,
  keepPlaylistMetadata: true,
  hideUnavailableTracks: true,
  scheduledSync: true,
};

function syncRuleDefaults(syncDefaults: SyncRuleDefaults | undefined) {
  return { ...defaultSyncRuleDefaults, ...syncDefaults };
}

function useSourcePlatformSelection(
  initialSourcePlatformId: PlatformId | undefined,
  connectedPlatformIds: PlatformId[],
  isCheckingConnectedAccounts: boolean,
) {
  const firstConnectedSource = platformCatalog.find((platform) => platform.implemented && connectedPlatformIds.includes(platform.id))?.id ?? 'youtube';
  const preferredSource = selectSourcePlatform(initialSourcePlatformId, connectedPlatformIds, firstConnectedSource);
  const [sourcePlatformId, setSourcePlatformId] = useState<PlatformId>(() => selectInitialSourcePlatform(initialSourcePlatformId, firstConnectedSource));

  useEffect(() => {
    if (!isCheckingConnectedAccounts) {
      setSourcePlatformId(preferredSource);
    }
  }, [isCheckingConnectedAccounts, preferredSource]);

  return { sourcePlatformId, setSourcePlatformId };
}

function useSyncRules(syncDefaults: SyncRuleDefaults | undefined) {
  const defaults = syncRuleDefaults(syncDefaults);
  const [keepOrder, setKeepOrder] = useState(defaults.keepPlaylistOrder);
  const [keepMetadata, setKeepMetadata] = useState(defaults.keepPlaylistMetadata);
  const [hideUnavailable, setHideUnavailable] = useState(defaults.hideUnavailableTracks);
  const [scheduledSync, setScheduledSync] = useState(defaults.scheduledSync);

  return { keepOrder, keepMetadata, hideUnavailable, scheduledSync, setKeepOrder, setKeepMetadata, setHideUnavailable, setScheduledSync };
}

function useSyncSetupData(sourcePlatformId: PlatformId) {
  const [playlists, setPlaylists] = useState<PlatformPlaylist[]>([]);
  const [selectedPlaylistIds, setSelectedPlaylistIds] = useState<Set<string>>(new Set());
  const [syncStatus, setSyncStatus] = useState<SyncStatusResponse | null>(null);
  const [syncResult, setSyncResult] = useState<BatchSyncResponse | null>(null);
  const [isLoadingPlaylists, setIsLoadingPlaylists] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let isMounted = true;

    async function loadSetupData() {
      setIsLoadingPlaylists(true);
      setError(null);
      setSelectedPlaylistIds(new Set());
      setSyncResult(null);

      try {
        const [status, platformPlaylists] = await Promise.all([
          syncApi.getSyncStatus(sourcePlatformId).catch(() => null),
          platformManager.playlists(sourcePlatformId, false),
        ]);

        if (!isMounted) return;
        setSyncStatus(status);
        setPlaylists(platformPlaylists);
      } catch (loadError) {
        if (!isMounted) return;
        setPlaylists([]);
        setError(getSyncErrorMessage(loadError, 'Failed to load playlists'));
      } finally {
        if (isMounted) setIsLoadingPlaylists(false);
      }
    }

    void loadSetupData();

    return () => {
      isMounted = false;
    };
  }, [sourcePlatformId]);

  return { playlists, selectedPlaylistIds, syncStatus, syncResult, isLoadingPlaylists, error, setError, setSelectedPlaylistIds, setSyncResult, setSyncStatus };
}

function useSyncJob({
  isSyncing,
  selectedPlaylistIds,
  sourcePlatformId,
  setError,
  setIsSyncing,
  setSyncResult,
  setSyncStatus,
}: {
  isSyncing: boolean;
  selectedPlaylistIds: Set<string>;
  sourcePlatformId: PlatformId;
  setError: (message: string | null) => void;
  setIsSyncing: (isSyncing: boolean) => void;
  setSyncResult: (result: BatchSyncResponse | null) => void;
  setSyncStatus: (status: SyncStatusResponse | null) => void;
}) {
  const navigate = useNavigate();

  return useCallback(async () => {
    if (selectedPlaylistIds.size === 0 || isSyncing) return;

    setIsSyncing(true);
    setError(null);
    setSyncResult(null);

    const latestStatus = await syncApi.getSyncStatus(sourcePlatformId).catch(() => null);
    setSyncStatus(latestStatus);

    if (!latestStatus?.overall.canSyncNow) {
      setError(syncUnavailableMessage(latestStatus));
      setIsSyncing(false);
      return;
    }

    try {
      const job = await syncApi.createSyncJob({
        service: sourcePlatformId,
        servicePlaylistIds: Array.from(selectedPlaylistIds),
      });
      writePlaylistSyncProgress(progressFromSyncJob(job, true));
      void navigate({ to: '/music/platforms' });
    } catch (syncError) {
      const errorMessage = getSyncErrorMessage(syncError, 'Failed to sync playlists');
      setError(errorMessage);
    } finally {
      setIsSyncing(false);
    }
  }, [isSyncing, navigate, selectedPlaylistIds, setError, setIsSyncing, setSyncResult, setSyncStatus, sourcePlatformId]);
}

function useSyncSetupController(initialSourcePlatformId?: PlatformId) {
  const { user } = useAuth();
  const { connectedPlatformIds, isCheckingConnectedAccounts } = useConnectedMusicPlatforms();
  const sourceSelection = useSourcePlatformSelection(initialSourcePlatformId, connectedPlatformIds, isCheckingConnectedAccounts);
  const setupData = useSyncSetupData(sourceSelection.sourcePlatformId);
  const rules = useSyncRules(user?.preferences);
  const [isSyncing, setIsSyncing] = useState(false);
  const selectedPlaylists = useMemo(
    () => setupData.playlists.filter((playlist) => setupData.selectedPlaylistIds.has(playlist.id)),
    [setupData.playlists, setupData.selectedPlaylistIds],
  );
  const targetPlatformIds = connectedPlatformIds.filter((platformId) => platformId !== sourceSelection.sourcePlatformId);
  const canSync = setupData.selectedPlaylistIds.size > 0 && !isSyncing && Boolean(setupData.syncStatus?.overall.canSyncNow);
  const handleSelectAll = useCallback(() => {
    setupData.setSelectedPlaylistIds((previous) => previous.size === setupData.playlists.length ? new Set() : new Set(setupData.playlists.map((playlist) => playlist.id)));
  }, [setupData]);
  const handleSync = useSyncJob({
    isSyncing,
    selectedPlaylistIds: setupData.selectedPlaylistIds,
    sourcePlatformId: sourceSelection.sourcePlatformId,
    setError: setupData.setError,
    setIsSyncing,
    setSyncResult: setupData.setSyncResult,
    setSyncStatus: setupData.setSyncStatus,
  });

  return {
    connectedPlatformIds,
    isCheckingConnectedAccounts,
    ...sourceSelection,
    ...setupData,
    selectedPlaylists,
    targetPlatformIds,
    isSyncing,
    canSync,
    handleSelectAll,
    handleSync,
    ...rules,
  };
}

type SyncSetupController = ReturnType<typeof useSyncSetupController>;

function SyncSetupHeader({
  canSync,
  isSyncing,
  onSync,
}: {
  canSync: boolean;
  isSyncing: boolean;
  onSync: () => void;
}) {
  return (
    <header className="flex flex-wrap items-start justify-between gap-4">
      <div>
        <Link to="/music/platforms" className="inline-flex items-center gap-2 text-sm font-black text-violet-700 transition hover:text-violet-500">
          <MusicUiIcon name="arrowRight" className="h-5 w-5 rotate-180" />
          Back to sync overview
        </Link>
        <h1 className="mt-3 text-4xl font-black text-slate-950">Create playlist sync</h1>
        <p className="mt-2 max-w-3xl text-sm leading-6 font-semibold text-slate-500">
          Pick a source platform and the playlists Cantaro should import into the canonical archive. Other connected platforms become available as mapped destinations after Cantaro knows the songs.
        </p>
      </div>
      <div className="flex flex-wrap items-center gap-3">
        <button
          type="button"
          disabled={!canSync}
          onClick={onSync}
          className="inline-flex h-12 items-center gap-2 rounded-2xl bg-slate-950 px-5 text-sm font-black text-white transition hover:bg-violet-700 disabled:cursor-not-allowed disabled:bg-slate-200 disabled:text-slate-500"
        >
          <MusicUiIcon name={isSyncing ? 'loader' : 'refresh'} className={`h-5 w-5 ${isSyncing ? 'animate-spin' : ''}`} />
          {isSyncing ? 'Syncing' : 'Sync playlists'}
        </button>
      </div>
    </header>
  );
}

function SyncSetupGrid({ controller }: { controller: SyncSetupController }) {
  return (
    <main className="grid gap-5 2xl:grid-cols-[minmax(420px,1.15fr)_minmax(320px,0.75fr)_minmax(420px,1fr)]">
      <PlaylistPicker
        sourcePlatformId={controller.sourcePlatformId}
        playlists={controller.playlists}
        selectedPlaylistIds={controller.selectedPlaylistIds}
        isLoading={controller.isLoadingPlaylists}
        onTogglePlaylist={(playlistId) => controller.setSelectedPlaylistIds((previous) => toggleSetValue(previous, playlistId))}
        onSelectAll={controller.handleSelectAll}
      />
      <RulesPanel
        keepOrder={controller.keepOrder}
        keepMetadata={controller.keepMetadata}
        hideUnavailable={controller.hideUnavailable}
        scheduledSync={controller.scheduledSync}
        onToggleKeepOrder={() => controller.setKeepOrder((previous) => !previous)}
        onToggleKeepMetadata={() => controller.setKeepMetadata((previous) => !previous)}
        onToggleHideUnavailable={() => controller.setHideUnavailable((previous) => !previous)}
        onToggleScheduledSync={() => controller.setScheduledSync((previous) => !previous)}
      />
      <SyncPreview
        sourcePlatformId={controller.sourcePlatformId}
        selectedPlaylists={controller.selectedPlaylists}
        targetPlatformIds={controller.targetPlatformIds}
        syncStatus={controller.syncStatus}
        syncResult={controller.syncResult}
      />
    </main>
  );
}

function ConnectFirstNotice({
  connectedPlatformIds,
  isCheckingConnectedAccounts,
}: {
  connectedPlatformIds: PlatformId[];
  isCheckingConnectedAccounts: boolean;
}) {
  if (isCheckingConnectedAccounts || connectedPlatformIds.length > 0) return null;

  return (
    <GlassCard className="p-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 className="text-lg font-black text-slate-950">Connect a platform first</h2>
          <p className="mt-1 text-sm font-semibold text-slate-500">Playlist sync needs at least one connected source service.</p>
        </div>
        <StatusBadge status="warning" />
      </div>
    </GlassCard>
  );
}

function SyncSetupSurface({ library, controller }: { library: MusicLibraryResponse; controller: SyncSetupController }) {
  return (
    <MusicPageShell library={library}>
      <div className="space-y-6">
        <SyncSetupHeader canSync={controller.canSync} isSyncing={controller.isSyncing} onSync={() => void controller.handleSync()} />
        <SourcePlatformSelector
          sourcePlatformId={controller.sourcePlatformId}
          connectedPlatformIds={controller.connectedPlatformIds}
          selectedPlaylists={controller.selectedPlaylists}
          targetPlatformIds={controller.targetPlatformIds}
          onChange={controller.setSourcePlatformId}
        />
        {controller.error ? (
          <GlassCard className="border-rose-200 bg-rose-50 p-4 text-sm font-semibold text-rose-700">
            {controller.error}
          </GlassCard>
        ) : null}
        <SyncSetupGrid controller={controller} />
        <ConnectFirstNotice connectedPlatformIds={controller.connectedPlatformIds} isCheckingConnectedAccounts={controller.isCheckingConnectedAccounts} />
      </div>
    </MusicPageShell>
  );
}

export function MusicPlaylistSyncSetupPage({ initialSourcePlatformId }: { initialSourcePlatformId?: PlatformId }) {
  const controller = useSyncSetupController(initialSourcePlatformId);

  return (
    <MusicLibraryPanel>
      {(library) => <SyncSetupSurface library={library} controller={controller} />}
    </MusicLibraryPanel>
  );
}
