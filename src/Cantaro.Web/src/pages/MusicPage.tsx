import { useCallback, useEffect, useState, type ReactNode } from 'react';
import {
  musicLibraryApi,
  platformCatalog,
  platformManager,
  type MusicLibraryPlaylist,
  type MusicLibraryResponse,
  type MusicLibrarySong,
  type PlatformId,
} from '@cantaro/client-shared/music';
import { GlassCard, GradientPageShell, PageLoadingState, StatusBadge } from '@cantaro/client-shared/ui';
import { MatchingReviewPage } from './MatchingReviewPage';

type MusicTab = 'songs' | 'playlists' | 'matching';

interface MusicPageProps {
  navigation?: ReactNode;
  initialTab?: MusicTab;
  onNavigatePlatform: (platformId: PlatformId) => void;
}

function formatDuration(seconds?: number): string | null {
  if (!seconds) return null;
  const minutes = Math.floor(seconds / 60);
  const remainingSeconds = seconds % 60;
  return `${minutes}:${String(remainingSeconds).padStart(2, '0')}`;
}

function formatTimestamp(value?: string): string | null {
  if (!value) return null;
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return null;
  return date.toLocaleString();
}

function platformName(platformId: string): string {
  return platformCatalog.find((platform) => platform.id === platformId)?.name ?? platformId;
}

function MusicTabButton({
  active,
  children,
  onClick,
}: {
  active: boolean;
  children: ReactNode;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      className={`rounded-xl px-4 py-2 text-sm font-semibold transition ${
        active
          ? 'bg-gray-900 text-white shadow-sm'
          : 'bg-white/70 text-gray-700 hover:bg-white'
      }`}
      aria-pressed={active}
      onClick={onClick}
    >
      {children}
    </button>
  );
}

function MusicSummary({ library }: { library: MusicLibraryResponse }) {
  const stats = [
    { label: 'Songs', value: library.summary.songCount },
    { label: 'Playlists', value: library.summary.playlistCount },
  ];

  return (
    <div className="grid gap-3 sm:grid-cols-2">
      {stats.map((stat) => (
        <GlassCard key={stat.label} className="p-4">
          <p className="text-xs tracking-[0.2em] text-gray-500 uppercase">{stat.label}</p>
          <p className="mt-2 text-2xl font-semibold text-gray-900">{stat.value.toLocaleString()}</p>
        </GlassCard>
      ))}
    </div>
  );
}

function SongsView({ songs }: { songs: MusicLibrarySong[] }) {
  if (songs.length === 0) {
    return (
      <GlassCard className="p-6">
        <p className="text-sm text-gray-600">No synced songs yet.</p>
      </GlassCard>
    );
  }

  return (
    <div className="space-y-3">
      {songs.map((song) => (
        <GlassCard key={song.id} className="p-4">
          <div className="flex gap-4">
            {song.thumbnailUrl ? (
              <img src={song.thumbnailUrl} alt="" className="h-16 w-24 shrink-0 rounded-xl object-cover" />
            ) : (
              <div className="flex h-16 w-24 shrink-0 items-center justify-center rounded-xl bg-white/80 text-lg text-gray-400">
                ♫
              </div>
            )}
            <div className="min-w-0 flex-1">
              <div className="flex flex-wrap items-start justify-between gap-2">
                <div className="min-w-0">
                  <h2 className="line-clamp-2 text-base font-semibold text-gray-900">{song.title}</h2>
                  <p className="mt-1 text-sm text-gray-500">
                    {[song.artist, formatDuration(song.durationSeconds)].filter(Boolean).join(' · ') || 'Unknown artist'}
                  </p>
                </div>
                {song.matchStatus ? (
                  <div className="flex items-center gap-2">
                    <StatusBadge status="warning" />
                    <span className="text-xs font-semibold text-amber-700">{song.matchStatus}</span>
                  </div>
                ) : null}
              </div>
              <div className="mt-3 flex flex-wrap gap-2">
                {song.sourcePlatforms.map((source) => (
                  <span key={source} className="rounded-full bg-white/80 px-3 py-1 text-xs font-semibold text-gray-600">
                    {platformName(source)}
                  </span>
                ))}
                {song.playlists.slice(0, 3).map((playlist) => (
                  <span key={`${song.id}-${playlist.playlistId}-${playlist.position}`} className="rounded-full bg-indigo-50 px-3 py-1 text-xs font-semibold text-indigo-700">
                    {playlist.playlistName}
                  </span>
                ))}
                {song.playlists.length > 3 ? (
                  <span className="rounded-full bg-white/80 px-3 py-1 text-xs font-semibold text-gray-500">
                    +{song.playlists.length - 3} more
                  </span>
                ) : null}
              </div>
            </div>
          </div>
        </GlassCard>
      ))}
    </div>
  );
}

function PlaylistsView({ playlists }: { playlists: MusicLibraryPlaylist[] }) {
  if (playlists.length === 0) {
    return (
      <GlassCard className="p-6">
        <p className="text-sm text-gray-600">No synced playlists yet.</p>
      </GlassCard>
    );
  }

  return (
    <div className="grid gap-3 md:grid-cols-2">
      {playlists.map((playlist) => {
        const lastSyncedAt = playlist.services
          .map((service) => service.lastSyncedAt)
          .filter((value): value is string => Boolean(value))
          .sort()
          .at(-1);

        return (
          <GlassCard key={playlist.id} className="p-5">
            <div className="flex items-start justify-between gap-3">
              <div className="min-w-0">
                <h2 className="line-clamp-2 text-lg font-semibold text-gray-900">{playlist.name}</h2>
                {playlist.description ? <p className="mt-2 line-clamp-2 text-sm text-gray-600">{playlist.description}</p> : null}
              </div>
              <span className="shrink-0 rounded-full bg-white/80 px-3 py-1 text-xs font-semibold text-gray-600">
                {playlist.entryCount.toLocaleString()} songs
              </span>
            </div>
            <div className="mt-4 flex flex-wrap gap-2">
              {playlist.services.length > 0 ? playlist.services.map((service) => (
                <span key={`${playlist.id}-${service.service}-${service.servicePlaylistId}`} className="rounded-full bg-white/80 px-3 py-1 text-xs font-semibold text-gray-600">
                  {platformName(service.service)}
                  {service.lastSyncStatus ? ` · ${service.lastSyncStatus}` : ''}
                </span>
              )) : (
                <span className="rounded-full bg-white/80 px-3 py-1 text-xs font-semibold text-gray-500">Cantaro</span>
              )}
            </div>
            {lastSyncedAt ? (
              <p className="mt-3 text-xs text-gray-500">Last synced {formatTimestamp(lastSyncedAt)}</p>
            ) : null}
          </GlassCard>
        );
      })}
    </div>
  );
}

function PlatformsSidebar({ onNavigatePlatform }: { onNavigatePlatform: (platformId: PlatformId) => void }) {
  const [connectedPlatformIds, setConnectedPlatformIds] = useState<PlatformId[]>([]);

  useEffect(() => {
    let isCancelled = false;

    const loadStatuses = async () => {
      const statuses = await Promise.all(
        platformCatalog
          .filter((platform) => platform.implemented)
          .map(async (platform) => {
            try {
              const status = await platformManager.status(platform.id);
              return status.isConnected ? platform.id : null;
            } catch {
              return null;
            }
          }),
      );

      if (!isCancelled) {
        setConnectedPlatformIds(statuses.filter((platformId): platformId is PlatformId => platformId !== null));
      }
    };

    void loadStatuses();
    return () => {
      isCancelled = true;
    };
  }, []);

  return (
    <GlassCard className="p-5">
      <h2 className="text-lg font-semibold text-gray-900">Platforms</h2>
      <div className="mt-4 space-y-3">
        {platformCatalog.map((platform) => {
          const isConnected = connectedPlatformIds.includes(platform.id);
          const canOpen = platform.implemented;

          return (
            <button
              key={platform.id}
              type="button"
              className="group w-full rounded-2xl bg-white/70 p-3 text-left transition hover:bg-white disabled:cursor-not-allowed disabled:opacity-60"
              disabled={!canOpen}
              onClick={() => onNavigatePlatform(platform.id)}
            >
              <div className="flex items-center gap-3">
                <div className={`flex h-11 w-11 items-center justify-center rounded-xl bg-linear-to-br ${platform.gradient} text-white`}>
                  {platform.icon}
                </div>
                <div className="min-w-0 flex-1">
                  <p className="font-semibold text-gray-800">{platform.name}</p>
                  <p className="text-xs text-gray-500">
                    {platform.implemented ? (isConnected ? 'Connected' : 'Open setup') : 'Coming soon'}
                  </p>
                </div>
              </div>
            </button>
          );
        })}
      </div>
    </GlassCard>
  );
}

function resolveInitialMusicTab(initialTab?: MusicTab): MusicTab {
  if (initialTab) return initialTab;
  const pathTab = window.location.pathname.split('/').filter(Boolean)[1];
  if (pathTab === 'playlists' || pathTab === 'matching') return pathTab;
  const tab = new URLSearchParams(window.location.search).get('tab');
  return tab === 'playlists' || tab === 'matching' ? tab : 'songs';
}

function pathForMusicTab(tab: MusicTab): string {
  return `/music/${tab}`;
}

export function MusicPage({ navigation, initialTab, onNavigatePlatform }: MusicPageProps) {
  const [activeTab, setActiveTab] = useState<MusicTab>(() => resolveInitialMusicTab(initialTab));
  const [library, setLibrary] = useState<MusicLibraryResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  const loadLibrary = useCallback(async () => {
    setIsLoading(true);
    setError(null);

    try {
      setLibrary(await musicLibraryApi.getLibrary());
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : 'Failed to load music library');
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadLibrary();
  }, [loadLibrary]);

  useEffect(() => {
    const handlePopState = () => setActiveTab(resolveInitialMusicTab(initialTab));
    window.addEventListener('popstate', handlePopState);
    return () => window.removeEventListener('popstate', handlePopState);
  }, [initialTab]);

  const selectTab = (tab: MusicTab) => {
    setActiveTab(tab);
    window.history.pushState({}, '', pathForMusicTab(tab));
  };

  if (isLoading) {
    return <PageLoadingState message="Loading music library…" />;
  }

  return (
    <GradientPageShell className="text-gray-900" contentClassName="max-w-7xl">
      <header className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <p className="text-xs tracking-[0.32em] text-gray-500 uppercase">Cantaro · Music</p>
          <h1 className="mt-1 text-3xl font-bold">Music library</h1>
        </div>
        {navigation}
      </header>

      {error ? (
        <GlassCard className="border-rose-300 bg-rose-50 p-4 text-sm text-rose-700">
          {error}
        </GlassCard>
      ) : null}

      {library ? (
        <div className="grid gap-5 lg:grid-cols-[1fr_280px]">
          <main className="space-y-5">
            <MusicSummary library={library} />
            <GlassCard className="p-2">
              <div className="flex gap-2">
                <MusicTabButton active={activeTab === 'songs'} onClick={() => selectTab('songs')}>
                  Songs
                </MusicTabButton>
                <MusicTabButton active={activeTab === 'playlists'} onClick={() => selectTab('playlists')}>
                  Playlists
                </MusicTabButton>
                <MusicTabButton active={activeTab === 'matching'} onClick={() => selectTab('matching')}>
                  Matching
                </MusicTabButton>
              </div>
            </GlassCard>
            {activeTab === 'matching' ? (
              <MatchingReviewPage embedded />
            ) : activeTab === 'songs' ? (
              <SongsView songs={library.songs} />
            ) : (
              <PlaylistsView playlists={library.playlists} />
            )}
          </main>
          <aside>
            <PlatformsSidebar onNavigatePlatform={onNavigatePlatform} />
          </aside>
        </div>
      ) : null}
    </GradientPageShell>
  );
}
