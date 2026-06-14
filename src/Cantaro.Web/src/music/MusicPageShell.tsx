import { Link, useRouterState } from '@tanstack/react-router';
import { MusicPlatformIcon, MusicUiIcon, platformCatalog, type MusicLibraryPlaylist, type MusicLibraryResponse, type MusicLibrarySong } from '@cantaro/client-shared/music';
import { useEffect, useState, type ReactNode } from 'react';
import { PageShell } from '../components/PageShell';
import { PageSideNavigation, type PageNavigationSection } from '../components/PageNavigation';
import { formatDuration, playlistArtwork, songArtist, songArtwork, visiblePlatformNames } from './musicPresentation';

function PlayIcon() {
  return <MusicUiIcon name="play" className="h-5 w-5 fill-current" />;
}

function PauseIcon() {
  return <MusicUiIcon name="pause" className="h-5 w-5" />;
}

function StopIcon() {
  return <MusicUiIcon name="square" className="h-5 w-5 fill-current" />;
}

function SidebarPlaylists({ playlists }: { playlists: MusicLibraryPlaylist[] }) {
  return (
    <section>
      <div className="mb-3 flex items-center justify-between">
        <h2 className="text-xs font-black tracking-[0.22em] text-slate-500 uppercase">Playlists</h2>
        <Link
          to="/music/platforms/sync"
          className="flex h-7 w-7 items-center justify-center rounded-xl text-slate-500 transition hover:bg-white hover:text-violet-600"
          aria-label="Create playlist sync"
        >
          <MusicUiIcon name="refresh" className="h-3.5 w-3.5" />
        </Link>
      </div>
      <div className="space-y-1.5">
        {playlists.slice(0, 6).map((playlist, index) => (
          <Link
            key={playlist.id}
            to="/music/playlists/$playlistId"
            params={{ playlistId: playlist.id }}
            className="group flex items-center gap-3 rounded-2xl px-3 py-2 text-sm font-semibold text-slate-700 transition hover:bg-white"
          >
            <img src={playlistArtwork(playlist, index)} alt="" className="h-7 w-7 rounded-lg object-cover" />
            <span className="min-w-0 flex-1 truncate">{playlist.name}</span>
            <span className="opacity-0 transition group-hover:opacity-100">...</span>
          </Link>
        ))}
        {playlists.length === 0 ? (
          <p className="rounded-2xl bg-white/70 px-3 py-3 text-sm font-medium text-slate-500">No playlists yet</p>
        ) : null}
      </div>
    </section>
  );
}

function SidebarMixtapeCard() {
  return (
    <div className="rounded-3xl bg-white/70 p-5 shadow-[0_20px_60px_rgba(88,74,150,0.08)]">
      <p className="text-lg font-black">Today's Mixtape</p>
      <p className="mt-2 text-sm leading-6 text-slate-600">A mix from your saved songs and playlists.</p>
      <Link
        to="/music/songs"
        className="mt-5 w-full rounded-2xl bg-slate-950 px-4 py-3 text-sm font-black text-white transition hover:bg-slate-800"
      >
        Open songs
      </Link>
    </div>
  );
}

function createMusicNavigationSections(): PageNavigationSection[] {
  return [
    {
      title: 'Music',
      items: [
        { label: 'Song Browsing', to: '/music/songs', icon: <MusicUiIcon name="music" className="h-4 w-4" />, matchPrefix: '/music/songs' },
        { label: 'Playlists', to: '/music/playlists', icon: <MusicUiIcon name="listMusic" className="h-4 w-4" />, matchPrefix: '/music/playlists' },
        { label: 'YouTube', to: '/music/platforms/$platformId', params: { platformId: 'youtube' }, icon: <MusicPlatformIcon platformId="youtube" className="h-4 w-4" />, matchPrefix: '/music/platforms/youtube' },
        { label: 'Matching', to: '/music/matching', icon: <MusicUiIcon name="sparkles" className="h-4 w-4" />, matchPrefix: '/music/matching' },
      ],
    },
    {
      title: 'Discover',
      items: [
        { label: 'Added Recently', to: '/music/songs', icon: <MusicUiIcon name="clock" className="h-4 w-4" />, matchPrefix: '/music/discover/added-recently' },
        { label: 'Old Bangers', to: '/music/songs', icon: <MusicUiIcon name="library" className="h-4 w-4" />, matchPrefix: '/music/discover/old-bangers' },
        { label: "Today's Mixtape", to: '/music/songs', icon: <MusicUiIcon name="radio" className="h-4 w-4" />, matchPrefix: '/music/discover/todays-mixtape' },
      ],
    },
    {
      title: 'Tools',
      items: [
        { label: 'Playlist Sync', to: '/music/platforms', icon: <MusicUiIcon name="refresh" className="h-4 w-4" />, exact: true },
      ],
    },
    {
      title: 'Platforms',
      titleAction: {
        label: 'Manage',
        to: '/music/platforms',
        icon: <MusicUiIcon name="settings" className="h-3.5 w-3.5" />,
        ariaLabel: 'Manage platforms',
      },
      items: platformCatalog.map((platform) => ({
        label: platform.name,
        to: '/music/platforms/$platformId',
        params: { platformId: platform.id },
        icon: <MusicPlatformIcon platformId={platform.iconId} className="h-4 w-4" />,
        detail: platform.implemented ? 'Available' : 'Coming soon',
        disabled: !platform.implemented,
        matchPrefix: `/music/platforms/${platform.id}`,
      })),
    },
  ];
}

function MusicSidebar({ library }: { library?: MusicLibraryResponse }) {
  const pathname = useRouterState({ select: (state) => state.location.pathname });

  return (
    <PageSideNavigation
      activePathname={pathname}
      subtitle="Music"
      sections={createMusicNavigationSections()}
      footer={
        <div className="space-y-8">
          <SidebarPlaylists playlists={library?.playlists ?? []} />
          <SidebarMixtapeCard />
        </div>
      }
    />
  );
}

function BottomPlayer({ song, onStop }: { song: MusicLibrarySong; onStop?: () => void }) {
  const [isPaused, setIsPaused] = useState(false);
  const [isVisible, setIsVisible] = useState(true);

  useEffect(() => {
    setIsPaused(false);
    setIsVisible(true);
  }, [song.id]);

  const closePlayer = () => {
    setIsVisible(false);
    onStop?.();
  };

  if (!isVisible) return null;

  return (
    <div className="fixed right-4 bottom-4 left-4 z-30 rounded-3xl border border-white/80 bg-white/80 px-5 py-4 shadow-[0_24px_80px_rgba(55,45,120,0.16)] backdrop-blur-xl lg:left-[292px]">
      <div className="grid items-center gap-4 md:grid-cols-[260px_1fr_220px]">
        <div className="flex min-w-0 items-center gap-3">
          <img src={songArtwork(song)} alt="" className="h-14 w-14 rounded-2xl object-cover" />
          <div className="min-w-0">
            <p className="truncate font-black text-slate-950">{song.title}</p>
            <p className="truncate text-sm font-semibold text-slate-500">{songArtist(song)}</p>
          </div>
        </div>
        <div className="hidden items-center justify-center gap-5 md:flex">
          <button
            type="button"
            className="flex h-12 w-12 items-center justify-center rounded-full bg-slate-950 text-white shadow-[0_14px_30px_rgba(15,23,42,0.18)] transition hover:bg-slate-800"
            aria-label={isPaused ? 'Play' : 'Pause'}
            onClick={() => setIsPaused((current) => !current)}
          >
            {isPaused ? <PlayIcon /> : <PauseIcon />}
          </button>
          <button
            type="button"
            className="flex h-9 w-9 items-center justify-center rounded-full text-slate-500 transition hover:bg-rose-50 hover:text-rose-600"
            aria-label="Stop and close player"
            onClick={closePlayer}
          >
            <StopIcon />
          </button>
          <div className="flex w-full max-w-sm items-center gap-3">
            <span className="font-mono text-xs text-slate-500">1:24</span>
            <div className="h-1.5 flex-1 rounded-full bg-slate-200">
              <div className="h-full w-2/5 rounded-full bg-violet-500" />
            </div>
            <span className="font-mono text-xs text-slate-500">{formatDuration(song.durationSeconds)}</span>
          </div>
        </div>
        <div className="hidden justify-end gap-2 md:flex">
          {visiblePlatformNames(song).slice(0, 2).map((source) => (
            <span key={source} className="rounded-full bg-[#eeeaff] px-3 py-1.5 text-xs font-black text-violet-700">
              {source}
            </span>
          ))}
        </div>
      </div>
    </div>
  );
}

function getFeaturedSong(activeSong?: MusicLibrarySong): MusicLibrarySong | null {
  return activeSong ?? null;
}

export function MusicPageShell({
  children,
  library,
  activeSong,
  onStopActiveSong,
  searchValue,
  onSearchChange,
  onSearchSubmit,
}: {
  children: ReactNode;
  library?: MusicLibraryResponse;
  activeSong?: MusicLibrarySong;
  onStopActiveSong?: () => void;
  searchValue?: string;
  onSearchChange?: (value: string) => void;
  onSearchSubmit?: () => void;
}) {
  const featuredSong = getFeaturedSong(activeSong);

  return (
    <PageShell
      sidebar={<MusicSidebar library={library} />}
      bottomSlot={featuredSong ? <BottomPlayer song={featuredSong} onStop={onStopActiveSong} /> : null}
      searchPlaceholder="Search songs, artists, playlists..."
      searchValue={searchValue}
      onSearchChange={onSearchChange}
      onSearchSubmit={onSearchSubmit}
    >
      {children}
    </PageShell>
  );
}
