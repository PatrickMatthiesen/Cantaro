import { Link } from '@tanstack/react-router';
import {
  MusicPlatformIcon,
  MusicUiIcon,
  platformCatalog,
  platformManager,
  syncApi,
  type MusicLibraryResponse,
  type PlatformId,
  type SyncStatusResponse,
} from '@cantaro/client-shared/music';
import { GlassCard, SegmentedSwitch, StatusBadge, type SegmentedSwitchOption } from '@cantaro/client-shared/ui';
import { useCallback, useEffect, useMemo, useRef, useState, type RefObject } from 'react';
import { MusicLibraryPanel } from './MusicLibraryPanel';
import { MusicPageShell } from './MusicPageShell';
import { formatRelativeTime, isPlatformId, latestTimestamp, platformName } from './musicPresentation';
import {
  consumePlaylistSyncActivityFocus,
  playlistSyncDataRefreshEventName,
  playlistSyncProgressEventName,
  progressFromSyncJob,
  readPlaylistSyncProgress,
  requestPlaylistSyncDataRefresh,
  type PlaylistSyncProgress,
  writePlaylistSyncProgress,
} from './playlistSyncProgress';
import { useConnectedMusicPlatforms } from './useConnectedMusicPlatforms';

type SyncOverviewView = 'platform' | 'syncGroup';

const syncOverviewViewOptions: ReadonlyArray<SegmentedSwitchOption<SyncOverviewView>> = [
  { value: 'platform', label: 'Platform' },
  { value: 'syncGroup', label: 'Sync group' },
];

interface AddPlatformMenuProps {
  menuRef: RefObject<HTMLDivElement | null>;
  isOpen: boolean;
  platformsToAdd: typeof platformCatalog;
  onToggle: () => void;
  onSelectPlatform: (platform: (typeof platformCatalog)[number]) => void;
}

function serviceName(service: string): string {
  return platformName(service);
}

function asPlatformId(service: string): PlatformId | null {
  return isPlatformId(service) ? service : null;
}

function statusLabel(status?: string | null): string {
  if (status === 'success') return 'Synced';
  if (status === 'partial_failure') return 'Partial';
  if (status === 'error') return 'Issue';
  return 'Ready';
}

function AddPlatformMenu({ menuRef, isOpen, platformsToAdd, onToggle, onSelectPlatform }: AddPlatformMenuProps) {
  return (
    <div ref={menuRef} className="relative">
      <button
        type="button"
        aria-expanded={isOpen}
        aria-haspopup="menu"
        onClick={onToggle}
        className="inline-flex h-12 items-center gap-2 rounded-2xl border border-border-subtle bg-surface px-5 text-sm font-black text-accent-strong transition hover:border-border-strong hover:bg-accent-soft focus-visible:ring-2 focus-visible:ring-focus focus-visible:ring-offset-2 focus-visible:outline-none"
      >
        <MusicUiIcon name="plus" className="h-5 w-5" />
        Add platform
      </button>
      {isOpen ? (
        <div className="absolute right-0 z-20 mt-2 w-64 rounded-2xl border border-border-subtle bg-surface-translucent p-2 shadow-lg backdrop-blur">
          {platformsToAdd.length === 0 ? (
            <p className="px-3 py-2 text-xs font-semibold text-content-muted">All platforms are already added.</p>
          ) : (
            platformsToAdd.map((platform) => (
              <button
                key={platform.id}
                type="button"
                className="flex w-full items-center gap-3 rounded-xl px-3 py-2 text-left text-sm font-semibold text-content transition hover:bg-surface-subtle disabled:cursor-not-allowed disabled:opacity-60"
                disabled={!platform.implemented}
                onClick={() => onSelectPlatform(platform)}
              >
                <span className="flex h-8 w-8 items-center justify-center rounded-xl bg-surface shadow-sm">
                  <MusicPlatformIcon platformId={platform.iconId} className="" />
                </span>
                <span className="min-w-0 flex-1">
                  <span className="block truncate">{platform.name}</span>
                  <span className="block text-xs font-semibold text-content-subtle">{platform.implemented ? 'Available' : 'Coming soon'}</span>
                </span>
              </button>
            ))
          )}
        </div>
      ) : null}
    </div>
  );
}

function useAddPlatformMenu() {
  const [showAddPlatformMenu, setShowAddPlatformMenu] = useState(false);
  const addPlatformMenuRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    const handleDocumentMouseDown = (event: MouseEvent) => {
      if (addPlatformMenuRef.current && !addPlatformMenuRef.current.contains(event.target as Node)) {
        setShowAddPlatformMenu(false);
      }
    };

    document.addEventListener('mousedown', handleDocumentMouseDown);
    return () => document.removeEventListener('mousedown', handleDocumentMouseDown);
  }, []);

  const handleSelectPlatform = useCallback(async (platform: (typeof platformCatalog)[number]) => {
    setShowAddPlatformMenu(false);
    if (!platform.implemented) return;

    try {
      await platformManager.connect(platform.id, {
        route: `/music/platforms/sync?source=${platform.id}`,
        trigger: 'add-platform-menu',
      });
    } catch {
      setShowAddPlatformMenu(false);
    }
  }, []);

  return {
    addPlatformMenuRef,
    showAddPlatformMenu,
    handleSelectPlatform,
    toggleAddPlatformMenu: () => setShowAddPlatformMenu((previous) => !previous),
  };
}

function useOverviewSyncStatus() {
  const [syncStatus, setSyncStatus] = useState<SyncStatusResponse | null>(null);

  const loadSyncStatus = useCallback(() => {
    syncApi.getSyncStatus()
      .then((status) => setSyncStatus(status))
      .catch(() => setSyncStatus(null));
  }, []);

  useEffect(() => {
    loadSyncStatus();

    window.addEventListener(playlistSyncDataRefreshEventName, loadSyncStatus);
    return () => window.removeEventListener(playlistSyncDataRefreshEventName, loadSyncStatus);
  }, [loadSyncStatus]);

  return syncStatus;
}

function usePlaylistSyncProgress() {
  const [progress, setProgress] = useState<PlaylistSyncProgress | null>(() => readPlaylistSyncProgress());

  useEffect(() => {
    const refreshProgress = () => setProgress(readPlaylistSyncProgress());
    const intervalId = window.setInterval(refreshProgress, 1500);

    window.addEventListener(playlistSyncProgressEventName, refreshProgress);
    return () => {
      window.clearInterval(intervalId);
      window.removeEventListener(playlistSyncProgressEventName, refreshProgress);
    };
  }, []);

  useEffect(() => {
    if (!progress?.jobId || progress.phase !== 'syncing') return;

    let disposed = false;
    const applyJob = (job: Awaited<ReturnType<typeof syncApi.getSyncJob>>) => {
      if (disposed) return;
      const next = progressFromSyncJob(job);
      writePlaylistSyncProgress(next);
      if (next.phase !== 'syncing') {
        requestPlaylistSyncDataRefresh();
      }
    };

    void syncApi.getSyncJob(progress.jobId).then(applyJob).catch(() => undefined);
    const eventSource = new EventSource(`/api/sync/jobs/${encodeURIComponent(progress.jobId)}/events`);
    eventSource.onmessage = (event) => {
      try {
        applyJob(JSON.parse(event.data) as Awaited<ReturnType<typeof syncApi.getSyncJob>>);
      } catch {
        // Ignore malformed events and recover from the next persisted snapshot.
      }
    };
    eventSource.onerror = () => {
      void syncApi.getSyncJob(progress.jobId).then(applyJob).catch(() => undefined);
    };

    return () => {
      disposed = true;
      eventSource.close();
    };
  }, [progress?.jobId, progress?.phase]);

  return progress;
}

function MetricCard({
  icon,
  label,
  value,
  detail,
  tone = 'violet',
}: {
  icon: Parameters<typeof MusicUiIcon>[0]['name'];
  label: string;
  value: string;
  detail: string;
  tone?: 'violet' | 'emerald' | 'amber' | 'rose';
}) {
  const toneClass = {
    violet: 'bg-accent-soft text-accent-strong',
    emerald: 'bg-emerald-100 text-emerald-700',
    amber: 'bg-amber-100 text-amber-700',
    rose: 'bg-rose-100 text-rose-700',
  }[tone];

  return (
    <GlassCard className="p-5">
      <div className="flex items-center gap-4">
        <span className={`flex h-12 w-12 items-center justify-center rounded-2xl ${toneClass}`}>
          <MusicUiIcon name={icon} className="h-5 w-5" />
        </span>
        <div className="min-w-0">
          <p className="text-xs font-black tracking-[0.18em] text-content-muted uppercase">{label}</p>
          <p className="mt-1 text-2xl font-black text-content">{value}</p>
          <p className="truncate text-xs font-semibold text-content-muted">{detail}</p>
        </div>
      </div>
    </GlassCard>
  );
}

function PlaylistStatusIcon({ status }: { status?: string | null }) {
  const isHealthy = status === 'success';
  const isProblem = status === 'partial_failure' || status === 'error';

  return (
    <span className={`flex h-8 w-8 items-center justify-center rounded-full ${
      isHealthy ? 'bg-emerald-100 text-emerald-700' : isProblem ? 'bg-amber-100 text-amber-700' : 'bg-surface-subtle text-content-muted'
    }`}>
      <MusicUiIcon name={isHealthy ? 'squareCheck' : isProblem ? 'warning' : 'clock'} className="h-4 w-4" />
    </span>
  );
}

function PlatformGroupedView({
  library,
  connectedPlatformIds,
}: {
  library: MusicLibraryResponse;
  connectedPlatformIds: PlatformId[];
}) {
  const connectedPlatformIdSet = new Set(connectedPlatformIds);

  return (
    <div className="grid gap-4 xl:grid-cols-2 2xl:grid-cols-4">
      {platformCatalog.map((platform) => {
        const playlists = library.playlists.filter((playlist) => playlist.services.some((service) => service.service === platform.id));
        const isConnected = connectedPlatformIdSet.has(platform.id);

        return (
          <GlassCard key={platform.id} className="flex min-h-[300px] flex-col p-4">
            <div className="mb-4 flex items-start gap-3">
              <span className={`flex h-12 w-12 items-center justify-center rounded-2xl bg-linear-to-br ${platform.gradient} text-content-inverse`}>
                <MusicPlatformIcon platformId={platform.iconId} className="h-8 w-8" />
              </span>
              <div className="min-w-0 flex-1">
                <div className="flex items-center justify-between gap-2">
                  <h3 className="truncate font-black text-content">{platform.name}</h3>
                  <StatusBadge status={isConnected ? 'connected' : platform.implemented ? 'available' : 'warning'} />
                </div>
                <p className="mt-1 text-xs font-semibold text-content-muted">
                  {playlists.length.toLocaleString()} synced playlist{playlists.length === 1 ? '' : 's'}
                </p>
              </div>
            </div>

            <div className="min-h-0 flex-1 space-y-2">
              {playlists.slice(0, 5).map((playlist) => {
                const mapping = playlist.services.find((service) => service.service === platform.id);
                return (
                  <Link
                    key={`${platform.id}-${playlist.id}`}
                    to="/music/playlists/$playlistId"
                    params={{ playlistId: playlist.id }}
                    className="grid grid-cols-[1fr_auto] items-center gap-3 rounded-2xl bg-surface-translucent px-3 py-2.5 transition hover:bg-surface"
                  >
                    <span className="min-w-0">
                      <span className="block truncate text-sm font-black text-content">{playlist.name}</span>
                      <span className="block text-xs font-semibold text-content-muted">
                        {playlist.entryCount.toLocaleString()} songs · {formatRelativeTime(mapping?.lastSyncedAt)}
                      </span>
                    </span>
                    <PlaylistStatusIcon status={mapping?.lastSyncStatus} />
                  </Link>
                );
              })}
              {playlists.length === 0 ? (
                <div className="flex min-h-32 flex-col items-center justify-center rounded-2xl bg-surface-translucent px-4 text-center">
                  <MusicUiIcon name="cloudSync" className="h-8 w-8 text-content-subtle" />
                  <p className="mt-2 text-sm font-black text-content">No synced playlists</p>
                  <p className="mt-1 text-xs font-semibold text-content-muted">Create a sync to pull playlists into Cantaro.</p>
                </div>
              ) : null}
            </div>
          </GlassCard>
        );
      })}
    </div>
  );
}

function SyncGroupView({ library }: { library: MusicLibraryResponse }) {
  const syncedPlaylists = library.playlists.filter((playlist) => playlist.services.length > 0);

  if (syncedPlaylists.length === 0) {
    return (
      <GlassCard className="p-8 text-center">
        <MusicUiIcon name="repeat" className="mx-auto h-10 w-10 text-accent" />
        <h3 className="mt-3 text-xl font-black text-content">No sync groups yet</h3>
        <p className="mx-auto mt-2 max-w-xl text-sm leading-6 font-semibold text-content-muted">
          A sync group appears when Cantaro has a canonical playlist connected to at least one external platform playlist.
        </p>
      </GlassCard>
    );
  }

  return (
    <div className="grid gap-4 xl:grid-cols-2">
      {syncedPlaylists.map((playlist) => {
        const lastSync = latestTimestamp(playlist.services.map((service) => service.lastSyncedAt));
        const hasProblem = playlist.services.some((service) => service.lastSyncStatus === 'partial_failure' || service.lastSyncStatus === 'error');

        return (
          <GlassCard key={playlist.id} className="p-4">
            <div className="flex items-start justify-between gap-3">
              <div className="min-w-0">
                <Link to="/music/playlists/$playlistId" params={{ playlistId: playlist.id }} className="text-lg font-black text-content transition hover:text-accent-strong">
                  {playlist.name}
                </Link>
                <p className="mt-1 text-sm font-semibold text-content-muted">
                  {playlist.entryCount.toLocaleString()} songs · last sync {formatRelativeTime(lastSync)}
                </p>
              </div>
              <StatusBadge status={hasProblem ? 'warning' : 'connected'} />
            </div>

            <div className="mt-4 grid gap-2 sm:grid-cols-2">
              {playlist.services.map((service) => {
                const platformId = asPlatformId(service.service);
                return (
                  <div key={`${playlist.id}-${service.service}-${service.servicePlaylistId}`} className="flex items-center gap-3 rounded-2xl bg-surface-translucent px-3 py-2.5">
                    <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-surface shadow-sm">
                      {platformId ? <MusicPlatformIcon platformId={platformId} className="h-5 w-5" /> : <MusicUiIcon name="cable" className="h-5 w-5 text-content-muted" />}
                    </span>
                    <span className="min-w-0 flex-1">
                      <span className="block truncate text-sm font-black text-content">{serviceName(service.service)}</span>
                      <span className="block truncate text-xs font-semibold text-content-muted">{statusLabel(service.lastSyncStatus)} · {formatRelativeTime(service.lastSyncedAt)}</span>
                    </span>
                  </div>
                );
              })}
            </div>
          </GlassCard>
        );
      })}
    </div>
  );
}

function progressStatusClassName(phase: PlaylistSyncProgress['phase']): string {
  const statusStyles = {
    syncing: 'bg-accent-soft text-accent-strong',
    completed: 'bg-emerald-100 text-emerald-700',
    failed: 'bg-rose-100 text-rose-700',
  };

  return statusStyles[phase];
}

function progressStatusLabel(phase: PlaylistSyncProgress['phase']): string {
  const statusLabels = {
    syncing: 'Syncing now',
    completed: 'Completed',
    failed: 'Needs review',
  };

  return statusLabels[phase];
}

function progressDetail(progress: PlaylistSyncProgress): string {
  if (progress.phase === 'failed' && progress.errorMessage) {
    return progress.errorMessage;
  }

  const processedPlaylists = progress.processedPlaylistCount ?? 0;
  const processedSongs = progress.processedSongCount ?? 0;
  return `${processedPlaylists.toLocaleString()}/${progress.playlistCount.toLocaleString()} playlists · ${processedSongs.toLocaleString()}/${progress.songCount.toLocaleString()} songs`;
}

function ProgressSpinner({ phase }: { phase: PlaylistSyncProgress['phase'] }) {
  if (phase !== 'syncing') return null;

  return (
    <span className="absolute -top-1 -right-1 flex h-4 w-4 items-center justify-center rounded-full bg-action text-action-content">
      <MusicUiIcon name="loader" className="h-3 w-3 animate-spin" />
    </span>
  );
}

function ProgressPlaylistNames({ names }: { names: string[] }) {
  if (names.length === 0) return null;

  return (
    <span className="mt-1 block truncate text-[11px] font-semibold text-content-muted">
      {names.join(', ')}
    </span>
  );
}

function ProgressCurrentSong({ progress }: { progress: PlaylistSyncProgress }) {
  if (!progress.currentSongName) {
    return <ProgressPlaylistNames names={progress.playlistNames} />;
  }

  const activity = progress.currentPlaylistName
    ? `Matching ${progress.currentSongName} · ${progress.currentPlaylistName}`
    : `Matching ${progress.currentSongName}`;

  return (
    <span className="mt-1 block truncate text-[11px] font-semibold text-content-muted" title={activity}>
      {activity}
    </span>
  );
}

function ProgressActivityRow({ progress }: { progress: PlaylistSyncProgress }) {
  const sourcePlatformName = serviceName(progress.sourcePlatformId);
  const statusStyles = {
    className: progressStatusClassName(progress.phase),
    label: progressStatusLabel(progress.phase),
  };

  return (
    <div className="music-sync-progress grid grid-cols-[auto_1fr_auto] items-center gap-3 rounded-2xl border border-border-subtle bg-accent-soft px-3 py-3 shadow-[0_14px_34px_rgba(88,74,150,0.08)] dark:border-[rgba(167,139,250,0.28)] dark:bg-[rgba(91,33,182,0.18)] dark:shadow-none">
      <span className="relative flex h-10 w-10 items-center justify-center rounded-xl bg-surface shadow-sm">
        <MusicPlatformIcon platformId={progress.sourcePlatformId} className="h-5 w-5" />
        <ProgressSpinner phase={progress.phase} />
      </span>
      <span className="min-w-0">
        <span className="block truncate text-sm font-black text-content">
          {progress.playlistCount.toLocaleString()} playlist{progress.playlistCount === 1 ? '' : 's'} importing from {sourcePlatformName}
        </span>
        <span className="block truncate text-xs font-semibold text-content-muted">
          {progressDetail(progress)}
        </span>
        <ProgressCurrentSong progress={progress} />
      </span>
      <span className={`rounded-full px-3 py-1.5 text-right text-xs font-black ${statusStyles.className}`}>{statusStyles.label}</span>
    </div>
  );
}

function ActivityPanel({ library, syncStatus, syncProgress }: { library: MusicLibraryResponse; syncStatus: SyncStatusResponse | null; syncProgress: PlaylistSyncProgress | null }) {
  const activities = useMemo(() => {
    const fromLibrary = library.playlists.flatMap((playlist) =>
      playlist.services.map((service) => ({
        id: `${playlist.id}-${service.service}-${service.servicePlaylistId}`,
        playlistName: playlist.name,
        service: service.service,
        status: service.lastSyncStatus,
        lastSyncedAt: service.lastSyncedAt,
      })),
    );

    const fromStatus = syncStatus?.playlists.map((playlist) => ({
      id: `${playlist.playlistId}-${playlist.service}-${playlist.servicePlaylistId}`,
      playlistName: playlist.name,
      service: playlist.service,
      status: playlist.lastSyncStatus,
      lastSyncedAt: playlist.lastSyncedAt,
    })) ?? [];

    return [...fromLibrary, ...fromStatus]
      .filter((activity, index, all) => all.findIndex((item) => item.id === activity.id) === index)
      .sort((left, right) => new Date(right.lastSyncedAt ?? 0).getTime() - new Date(left.lastSyncedAt ?? 0).getTime())
      .slice(0, 5);
  }, [library.playlists, syncStatus?.playlists]);

  return (
    <GlassCard className="p-5">
      <div className="flex items-center justify-between gap-3">
        <div>
          <h2 className="text-lg font-black text-content">Recent sync activity</h2>
          <p className="mt-1 text-sm font-semibold text-content-muted">Compact status from the latest known playlist mappings.</p>
        </div>
        <MusicUiIcon name="activity" className="h-5 w-5 text-accent" />
      </div>

      <div className="mt-4 space-y-2">
        {syncProgress ? <ProgressActivityRow progress={syncProgress} /> : null}
        {activities.length > 0 ? activities.map((activity) => {
          const platformId = asPlatformId(activity.service);
          return (
            <div key={activity.id} className="grid grid-cols-[auto_1fr_auto] items-center gap-3 rounded-2xl bg-surface-translucent px-3 py-2.5">
              <span className="flex h-9 w-9 items-center justify-center rounded-xl bg-surface shadow-sm">
                {platformId ? <MusicPlatformIcon platformId={platformId} className="h-5 w-5" /> : <MusicUiIcon name="cable" className="h-5 w-5 text-content-muted" />}
              </span>
              <span className="min-w-0">
                <span className="block truncate text-sm font-black text-content">{activity.playlistName}</span>
                <span className="block truncate text-xs font-semibold text-content-muted">
                  {serviceName(activity.service)} -&gt; Cantaro -&gt; connected platforms
                </span>
              </span>
              <span className="text-right text-xs font-black text-content-muted">{formatRelativeTime(activity.lastSyncedAt)}</span>
            </div>
          );
        }) : !syncProgress ? (
          <div className="rounded-2xl bg-surface-translucent p-4 text-sm font-semibold text-content-muted">No sync activity yet.</div>
        ) : null}
      </div>
    </GlassCard>
  );
}

function HealthPanel({ library, syncStatus }: { library: MusicLibraryResponse; syncStatus: SyncStatusResponse | null }) {
  const syncedPlaylists = library.playlists.filter((playlist) => playlist.services.length > 0);
  const failedCount = syncedPlaylists.flatMap((playlist) => playlist.services).filter((service) => service.lastSyncStatus === 'partial_failure' || service.lastSyncStatus === 'error').length;
  const healthRows = [
    { label: 'Platform connections', value: `${syncStatus?.overall.canSyncNow ? 'Ready' : 'Limited'}`, healthy: Boolean(syncStatus?.overall.canSyncNow) },
    { label: 'Synced playlists', value: syncedPlaylists.length.toLocaleString(), healthy: syncedPlaylists.length > 0 },
    { label: 'Failed or partial mappings', value: failedCount.toLocaleString(), healthy: failedCount === 0 },
    { label: 'Rate window remaining', value: (syncStatus?.overall.remainingSongsInWindow ?? 0).toLocaleString(), healthy: Boolean(syncStatus?.overall.canSyncNow) },
  ];

  return (
    <GlassCard className="p-5">
      <div className="flex items-center justify-between gap-3">
        <div>
          <h2 className="text-lg font-black text-content">Sync health</h2>
          <p className="mt-1 text-sm font-semibold text-content-muted">Whether Cantaro can safely run playlist sync right now.</p>
        </div>
        <MusicUiIcon name="shieldCheck" className="h-5 w-5 text-emerald-500" />
      </div>

      <div className="mt-4 divide-y divide-border-subtle">
        {healthRows.map((row) => (
          <div key={row.label} className="flex items-center justify-between gap-3 py-3 text-sm">
            <span className="font-semibold text-content-muted">{row.label}</span>
            <span className={`inline-flex items-center gap-2 font-black ${row.healthy ? 'text-emerald-600' : 'text-amber-600'}`}>
              {row.value}
              <MusicUiIcon name={row.healthy ? 'squareCheck' : 'warning'} className="h-4 w-4" />
            </span>
          </div>
        ))}
      </div>
    </GlassCard>
  );
}

function OverviewHeader({
  platformsToAdd,
  menuRef,
  showAddPlatformMenu,
  onToggleAddPlatformMenu,
  onSelectPlatform,
}: {
  platformsToAdd: typeof platformCatalog;
  menuRef: RefObject<HTMLDivElement | null>;
  showAddPlatformMenu: boolean;
  onToggleAddPlatformMenu: () => void;
  onSelectPlatform: (platform: (typeof platformCatalog)[number]) => void;
}) {
  return (
    <header className="flex flex-wrap items-start justify-between gap-4">
      <div>
        <p className="text-xs font-black tracking-[0.22em] text-accent uppercase">Archive sync</p>
        <h1 className="mt-2 text-4xl font-black text-content">Playlist sync</h1>
        <p className="mt-2 max-w-3xl text-sm leading-6 font-semibold text-content-muted">
          Cantaro keeps the canonical playlist copy here, then tracks which connected platforms have a mapped version.
        </p>
      </div>
      <div className="flex flex-wrap items-center gap-3">
        <Link
          to="/music/platforms/sync"
          className="inline-flex h-12 items-center gap-2 rounded-2xl bg-action px-5 text-sm font-black text-action-content shadow-[0_16px_40px_rgba(15,23,42,0.18)] transition hover:bg-action-hover"
        >
          <MusicUiIcon name="refresh" className="h-5 w-5" />
          Add playlist sync
        </Link>
        <AddPlatformMenu
          menuRef={menuRef}
          isOpen={showAddPlatformMenu}
          platformsToAdd={platformsToAdd}
          onToggle={onToggleAddPlatformMenu}
          onSelectPlatform={onSelectPlatform}
        />
      </div>
    </header>
  );
}

function OverviewMetrics({
  isCheckingConnectedAccounts,
  connectedPlatformCount,
  syncedPlaylistCount,
  totalPlaylistCount,
  healthValue,
  healthTone,
  statusMessage,
  lastSync,
}: {
  isCheckingConnectedAccounts: boolean;
  connectedPlatformCount: number;
  syncedPlaylistCount: number;
  totalPlaylistCount: number;
  healthValue: string;
  healthTone: 'emerald' | 'amber';
  statusMessage?: string;
  lastSync: string | null;
}) {
  return (
    <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
      <MetricCard icon="cable" label="Connected platforms" value={isCheckingConnectedAccounts ? '...' : connectedPlatformCount.toString()} detail={`${platformCatalog.length} platforms configured`} />
      <MetricCard icon="listMusic" label="Synced playlists" value={syncedPlaylistCount.toString()} detail={`${totalPlaylistCount.toLocaleString()} total Cantaro playlists`} />
      <MetricCard icon="shieldCheck" label="Sync health" value={healthValue} detail={statusMessage ?? 'Current systems normal'} tone={healthTone} />
      <MetricCard icon="clock" label="Last sync" value={formatRelativeTime(lastSync)} detail="Across all mapped playlists" />
    </section>
  );
}

function MusicPlatformsOverview({ library }: { library: MusicLibraryResponse }) {
  const [view, setView] = useState<SyncOverviewView>('platform');
  const activitySectionRef = useRef<HTMLElement | null>(null);
  const syncStatus = useOverviewSyncStatus();
  const syncProgress = usePlaylistSyncProgress();
  const {
    addPlatformMenuRef,
    showAddPlatformMenu,
    handleSelectPlatform,
    toggleAddPlatformMenu,
  } = useAddPlatformMenu();
  const { connectedPlatformIds, isCheckingConnectedAccounts } = useConnectedMusicPlatforms();
  const connectedPlatformIdSet = new Set(connectedPlatformIds);
  const connectedPlatforms = platformCatalog.filter((platform) => connectedPlatformIdSet.has(platform.id));
  const platformsToAdd = platformCatalog.filter((platform) => !connectedPlatformIdSet.has(platform.id));
  const syncedPlaylists = library.playlists.filter((playlist) => playlist.services.length > 0);
  const lastSync = latestTimestamp(syncedPlaylists.flatMap((playlist) => playlist.services.map((service) => service.lastSyncedAt)));
  const hasSyncProblem = syncedPlaylists.some((playlist) => playlist.services.some((service) => service.lastSyncStatus === 'partial_failure' || service.lastSyncStatus === 'error'));
  const healthValue = hasSyncProblem ? 'Needs review' : syncedPlaylists.length > 0 ? 'Healthy' : 'Ready';
  const healthTone = hasSyncProblem ? 'amber' : 'emerald';

  useEffect(() => {
    if (!syncProgress || !consumePlaylistSyncActivityFocus()) return;

    window.setTimeout(() => {
      activitySectionRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }, 120);
  }, [syncProgress]);

  return (
    <MusicPageShell library={library}>
      <div className="space-y-7">
        <OverviewHeader
          platformsToAdd={platformsToAdd}
          menuRef={addPlatformMenuRef}
          showAddPlatformMenu={showAddPlatformMenu}
          onToggleAddPlatformMenu={toggleAddPlatformMenu}
          onSelectPlatform={handleSelectPlatform}
        />

        <OverviewMetrics
          isCheckingConnectedAccounts={isCheckingConnectedAccounts}
          connectedPlatformCount={connectedPlatforms.length}
          syncedPlaylistCount={syncedPlaylists.length}
          totalPlaylistCount={library.summary.playlistCount}
          healthValue={healthValue}
          healthTone={healthTone}
          statusMessage={syncStatus?.overall.message}
          lastSync={lastSync}
        />

        <section className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <h2 className="text-xl font-black text-content">Current synced playlists</h2>
            <p className="mt-1 text-sm font-semibold text-content-muted">Switch between platform ownership and canonical Cantaro sync groups.</p>
          </div>
          <SegmentedSwitch
            label="View by"
            value={view}
            options={syncOverviewViewOptions}
            onChange={setView}
          />
        </section>

        {view === 'platform' ? (
          <PlatformGroupedView library={library} connectedPlatformIds={connectedPlatformIds} />
        ) : (
          <SyncGroupView library={library} />
        )}

        <section ref={activitySectionRef} className="grid scroll-mt-28 gap-5 xl:grid-cols-[minmax(0,1fr)_460px]">
          <ActivityPanel library={library} syncStatus={syncStatus} syncProgress={syncProgress} />
          <HealthPanel library={library} syncStatus={syncStatus} />
        </section>
      </div>
    </MusicPageShell>
  );
}

export function MusicPlatformsPage() {
  return (
    <MusicLibraryPanel>
      {(library) => <MusicPlatformsOverview library={library} />}
    </MusicLibraryPanel>
  );
}
