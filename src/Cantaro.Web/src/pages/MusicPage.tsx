import { useCallback, useEffect, useState, type ReactNode } from 'react';
import { Link, Outlet } from '@tanstack/react-router';
import {
  musicLibraryApi,
  platformCatalog,
  type MusicLibraryPlaylist,
  type MusicLibraryResponse,
  type MusicLibrarySong,
} from '@cantaro/client-shared/music';
import { GlassCard, StatusBadge } from '@cantaro/client-shared/ui';
import { AppPageShell, GlobalHeader, RequireAuth } from '../components/AppShell';
import { SubjectNav, type SubjectNavItem } from '../components/SubjectNav';
import { MatchingReviewPage } from './MatchingReviewPage';
import { YouTubePlaylistsPage } from './YouTubePlaylistsPage';
import { useConnectedMusicPlatforms } from '../music/useConnectedMusicPlatforms';

const musicNavItems: SubjectNavItem[] = [
  { label: 'Songs', to: '/music/songs' },
  { label: 'Playlists', to: '/music/playlists' },
  { label: 'Matching', to: '/music/matching' },
];

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

function MusicPlatformSidebar() {
  const { connectedPlatformIds } = useConnectedMusicPlatforms();

  return (
    <GlassCard className="p-5">
      <h2 className="text-lg font-semibold text-gray-900">Platforms</h2>
      <div className="mt-4 space-y-3">
        {platformCatalog.map((platform) => {
          const isConnected = connectedPlatformIds.includes(platform.id);
          const canOpen = platform.implemented;

          return (
            <Link
              key={platform.id}
              to="/music/platforms/$platformId"
              params={{ platformId: platform.id }}
              disabled={!canOpen}
              className="group block w-full rounded-2xl bg-white/70 p-3 text-left transition hover:bg-white aria-disabled:pointer-events-none aria-disabled:opacity-60"
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
            </Link>
          );
        })}
      </div>
    </GlassCard>
  );
}

function useMusicLibrary() {
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

  return { library, error, isLoading, loadLibrary };
}

function MusicLibraryPanel({ children }: { children: (library: MusicLibraryResponse) => ReactNode }) {
  const { library, error, isLoading, loadLibrary } = useMusicLibrary();

  if (isLoading) {
    return (
      <GlassCard className="p-6">
        <p className="text-sm text-gray-600">Loading music library…</p>
      </GlassCard>
    );
  }

  return (
    <div className="space-y-5">
      {error ? (
        <GlassCard className="border-rose-300 bg-rose-50 p-4 text-sm text-rose-700">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <span>{error}</span>
            <button type="button" className="text-sm font-semibold text-rose-800 underline" onClick={() => void loadLibrary()}>
              Retry
            </button>
          </div>
        </GlassCard>
      ) : null}

      {library ? (
        <>
          <MusicSummary library={library} />
          {children(library)}
        </>
      ) : null}
    </div>
  );
}

export function MusicLayout() {
  return (
    <RequireAuth>
      <AppPageShell contentClassName="max-w-7xl">
        <GlobalHeader eyebrow="Cantaro · Music" title="Music library" />
        <div className="grid gap-5 lg:grid-cols-[1fr_280px]">
          <main className="space-y-5">
            <SubjectNav label="Music sections" items={musicNavItems} />
            <Outlet />
          </main>
          <aside>
            <MusicPlatformSidebar />
          </aside>
        </div>
      </AppPageShell>
    </RequireAuth>
  );
}

export function MusicSongsPage() {
  return (
    <MusicLibraryPanel>
      {(library) => <SongsView songs={library.songs} />}
    </MusicLibraryPanel>
  );
}

export function MusicPlaylistsPage() {
  return (
    <MusicLibraryPanel>
      {(library) => <PlaylistsView playlists={library.playlists} />}
    </MusicLibraryPanel>
  );
}

export function MusicMatchingPage() {
  return <MatchingReviewPage embedded />;
}

export function MusicPlatformPage({ platformId, playlistId = null }: { platformId: string; playlistId?: string | null }) {
  if (platformId === 'youtube') {
    return <YouTubePlaylistsPage embedded playlistId={playlistId} />;
  }

  const platform = platformCatalog.find((item) => item.id === platformId);

  return (
    <GlassCard className="p-6">
      <p className="text-xs tracking-[0.24em] text-gray-500 uppercase">Platform</p>
      <h2 className="mt-2 text-2xl font-semibold text-gray-900">{platform?.name ?? platformId}</h2>
      <p className="mt-2 text-sm text-gray-600">This platform is not available yet.</p>
    </GlassCard>
  );
}
