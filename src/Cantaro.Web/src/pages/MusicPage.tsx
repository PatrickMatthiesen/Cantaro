import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import { Link, Outlet, useRouterState } from '@tanstack/react-router';
import {
  musicLibraryApi,
  platformCatalog,
  type MusicLibraryPlaylist,
  type MusicLibraryResponse,
  type MusicLibrarySong,
} from '@cantaro/client-shared/music';
import { GlassCard, StatusBadge } from '@cantaro/client-shared/ui';
import { AppPageShell, RequireAuth } from '../components/AppShell';
import { MatchingReviewPage } from './MatchingReviewPage';
import { YouTubePlaylistsPage } from './YouTubePlaylistsPage';
import { useAuth } from '../contexts/AuthContext';

const fallbackArtwork = [
  'https://images.unsplash.com/photo-1493225457124-a3eb161ffa5f?auto=format&fit=crop&w=640&q=80',
  'https://images.unsplash.com/photo-1516280440614-37939bbacd81?auto=format&fit=crop&w=640&q=80',
  'https://images.unsplash.com/photo-1511379938547-c1f69419868d?auto=format&fit=crop&w=640&q=80',
  'https://images.unsplash.com/photo-1524650359799-842906ca1c06?auto=format&fit=crop&w=640&q=80',
  'https://images.unsplash.com/photo-1458560871784-56d23406c091?auto=format&fit=crop&w=640&q=80',
  'https://images.unsplash.com/photo-1508700115892-45ecd05ae2ad?auto=format&fit=crop&w=640&q=80',
];

const moodTiles = [
  { label: 'Bright', className: 'from-rose-300 to-orange-300' },
  { label: 'Late night', className: 'from-indigo-400 to-slate-800' },
  { label: 'Soft focus', className: 'from-emerald-300 to-cyan-500' },
  { label: 'Loud', className: 'from-fuchsia-400 to-red-500' },
  { label: 'Rainy', className: 'from-sky-300 to-violet-500' },
  { label: 'Golden', className: 'from-amber-300 to-yellow-500' },
];

const playlistGradients = [
  'from-[#ff9a8b] via-[#ff6a88] to-[#8054ff]',
  'from-[#ffd166] via-[#8bd3dd] to-[#3d5afe]',
  'from-[#6ee7b7] via-[#60a5fa] to-[#7c3aed]',
  'from-[#fca5a5] via-[#fdba74] to-[#92400e]',
  'from-[#93c5fd] via-[#c4b5fd] to-[#312e81]',
  'from-[#f0abfc] via-[#c084fc] to-[#be123c]',
];

function formatDuration(seconds?: number): string {
  if (!seconds) return '0:00';
  const minutes = Math.floor(seconds / 60);
  const remainingSeconds = seconds % 60;
  return `${minutes}:${String(remainingSeconds).padStart(2, '0')}`;
}

function formatTimestamp(value?: string): string | null {
  if (!value) return null;
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return null;
  return date.toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
}

function platformName(platformId: string): string {
  return platformCatalog.find((platform) => platform.id === platformId)?.name ?? platformId;
}

function visiblePlatformNames(song: MusicLibrarySong): string[] {
  const names = song.sourcePlatforms
    .filter((source) => source !== 'musicbrainz')
    .map(platformName);

  return names.length > 0 ? names : ['Library'];
}

function songArtwork(song: MusicLibrarySong, index = 0): string {
  return song.thumbnailUrl || fallbackArtwork[index % fallbackArtwork.length];
}

function playlistArtwork(playlist: MusicLibraryPlaylist, index = 0): string {
  const seed = playlist.services.at(0)?.servicePlaylistId ?? playlist.id;
  const hash = Array.from(seed).reduce((total, char) => total + char.charCodeAt(0), index);
  return fallbackArtwork[hash % fallbackArtwork.length];
}

function songArtist(song: MusicLibrarySong): string {
  return song.artist || 'Unknown artist';
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
      <MusicWorkspaceShell>
        <div className="flex min-h-[360px] items-center justify-center rounded-3xl border border-white/80 bg-white/65 shadow-[0_24px_80px_rgba(82,70,140,0.08)] backdrop-blur">
          <div className="flex items-center gap-3 text-sm font-semibold text-slate-600">
            <span className="h-2.5 w-2.5 animate-pulse rounded-full bg-violet-500" aria-hidden />
            Loading your library
          </div>
        </div>
      </MusicWorkspaceShell>
    );
  }

  if (error) {
    return (
      <MusicWorkspaceShell>
        <GlassCard className="border-rose-200 bg-rose-50 p-5 text-sm text-rose-700">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <span>{error}</span>
            <button type="button" className="font-semibold text-rose-800 underline" onClick={() => void loadLibrary()}>
              Retry
            </button>
          </div>
        </GlassCard>
      </MusicWorkspaceShell>
    );
  }

  return <>{library ? children(library) : null}</>;
}

function MusicWorkspaceShell({
  children,
  library,
  activeSong,
}: {
  children: ReactNode;
  library?: MusicLibraryResponse;
  activeSong?: MusicLibrarySong;
}) {
  const pathname = useRouterState({ select: (state) => state.location.pathname });
  const { user } = useAuth();
  const featuredSong = getFeaturedSong(library, activeSong);

  return (
    <div className="min-h-screen bg-[#f7f5ff] text-slate-950">
      <div className="grid min-h-screen grid-cols-1 lg:grid-cols-[272px_1fr]">
        <MusicSidebar library={library} />

        <div className="flex min-w-0 flex-col pb-28">
          <MusicTopBar pathname={pathname} userEmail={user?.email} />

          <main className="w-full px-4 py-6 sm:px-8 lg:px-10">
            {children}
          </main>
        </div>
      </div>

      {featuredSong ? <BottomPlayer song={featuredSong} /> : null}
    </div>
  );
}

function getFeaturedSong(library?: MusicLibraryResponse, activeSong?: MusicLibrarySong): MusicLibrarySong | null {
  return activeSong ?? library?.songs[0] ?? null;
}

function MusicSidebar({ library }: { library?: MusicLibraryResponse }) {
  return (
    <aside className="hidden-scrollbar-until-hover sticky top-0 hidden h-screen overflow-y-auto border-r border-[#e8e4fb] bg-white/55 px-5 py-6 shadow-[12px_0_40px_rgba(88,74,150,0.05)] backdrop-blur-xl lg:block">
      <div className="min-h-full pb-32">
        <Link to="/" className="flex items-center gap-3">
          <div className="flex h-10 w-10 items-center justify-center rounded-2xl bg-violet-600 text-lg font-black text-white shadow-[0_12px_30px_rgba(124,92,255,0.3)]">
            C
          </div>
          <div>
            <p className="text-lg font-black tracking-[0.04em]">CANTARO</p>
            <p className="text-xs font-bold tracking-[0.32em] text-slate-500">MUSIC</p>
          </div>
        </Link>

        <SideSection className="mt-9" title="Discover" items={['Added Recently', 'Old Bangers', "Today's Mixtape"]} />
        <SideSection className="mt-8" title="Your Library" items={['Recently Played', 'Liked Songs', 'Albums', 'Artists']} />
        <SidebarPlaylists playlists={library?.playlists ?? []} />
        <SidebarMixtapeCard />
      </div>
    </aside>
  );
}

function SidebarPlaylists({ playlists }: { playlists: MusicLibraryPlaylist[] }) {
  return (
    <section className="mt-8">
      <div className="mb-3 flex items-center justify-between">
        <h2 className="text-xs font-black tracking-[0.22em] text-slate-500 uppercase">Playlists</h2>
        <span className="text-lg font-semibold text-slate-500">+</span>
      </div>
      <div className="space-y-1.5">
        {playlists.slice(0, 6).map((playlist, index) => (
          <Link
            key={playlist.id}
            to="/music/playlists"
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
    <div className="mt-8 rounded-3xl bg-white/70 p-5 shadow-[0_20px_60px_rgba(88,74,150,0.08)]">
      <p className="text-lg font-black">Today's Mixtape</p>
      <p className="mt-2 text-sm leading-6 text-slate-600">A mix from your saved songs and playlists.</p>
      <button type="button" className="mt-5 w-full rounded-2xl bg-slate-950 px-4 py-3 text-sm font-black text-white transition hover:bg-slate-800">
        Play Mix
      </button>
    </div>
  );
}

function MusicTopBar({ pathname, userEmail }: { pathname: string; userEmail?: string }) {
  const userInitial = userEmail?.trim().charAt(0).toUpperCase() || 'C';

  return (
    <header className="sticky top-0 z-20 border-b border-white/80 bg-[#f7f5ff]/82 px-4 py-4 backdrop-blur-xl sm:px-8 lg:px-10">
      <div className="flex flex-wrap items-center gap-4">
        <MusicTopNavigation pathname={pathname} />
        <TopSearchInput />
        <button type="button" className="flex h-11 w-11 items-center justify-center rounded-full text-slate-700 transition hover:bg-white/70" aria-label="Notifications">
          <svg className="h-5 w-5" viewBox="0 0 24 24" fill="none" aria-hidden>
            <path
              d="M18 8.8a6 6 0 0 0-12 0c0 7.2-3 7.2-3 9.2h18c0-2-3-2-3-9.2Z"
              stroke="currentColor"
              strokeLinecap="round"
              strokeLinejoin="round"
              strokeWidth="1.8"
            />
            <path
              d="M9.8 21a2.4 2.4 0 0 0 4.4 0"
              stroke="currentColor"
              strokeLinecap="round"
              strokeLinejoin="round"
              strokeWidth="1.8"
            />
          </svg>
        </button>
        <button type="button" className="flex h-12 w-12 items-center justify-center rounded-full bg-linear-to-br from-violet-500 to-slate-950 text-sm font-black text-white shadow-[0_14px_34px_rgba(88,74,150,0.22)]">
          {userInitial}
        </button>
      </div>
    </header>
  );
}

function MusicTopNavigation({ pathname }: { pathname: string }) {
  return (
    <nav className="flex items-center gap-2" aria-label="Music navigation">
      {[
        { label: 'Home', to: '/' },
        { label: 'Browse', to: '/music/songs' },
        { label: 'Playlists', to: '/music/playlists' },
      ].map((item) => (
        <Link
          key={item.label}
          to={item.to}
          className={`rounded-2xl px-5 py-3 text-sm font-black transition ${
            isMusicNavActive(pathname, item.to) ? 'bg-[#ebe7ff] text-slate-950' : 'text-slate-700 hover:bg-white/80'
          }`}
        >
          {item.label}
        </Link>
      ))}
    </nav>
  );
}

function isMusicNavActive(pathname: string, to: string): boolean {
  if (to === '/music/songs') {
    return pathname === '/music' || pathname === '/music/' || pathname === '/music/songs';
  }

  return pathname === to;
}

function TopSearchInput() {
  return (
    <div className="min-w-[220px] flex-1">
      <label className="relative block">
        <span className="pointer-events-none absolute top-1/2 left-4 -translate-y-1/2 text-slate-400">⌕</span>
        <input
          className="h-12 w-full rounded-2xl border border-[#e3def8] bg-white/70 pr-4 pl-11 text-sm font-medium text-slate-800 transition outline-none placeholder:text-slate-400 focus:border-violet-300 focus:bg-white"
          placeholder="Search songs, artists, playlists..."
          type="search"
        />
      </label>
    </div>
  );
}

function SideSection({
  title,
  items,
  className = '',
}: {
  title: string;
  items: string[];
  className?: string;
}) {
  return (
    <section className={className}>
      <h2 className="mb-3 text-xs font-black tracking-[0.22em] text-slate-500 uppercase">{title}</h2>
      <div className="space-y-1.5">
        {items.map((item) => (
          <button
            key={item}
            type="button"
            className="flex w-full items-center gap-3 rounded-2xl px-3 py-2.5 text-left text-sm font-semibold text-slate-700 transition hover:bg-white"
          >
            <span className="flex h-6 w-6 items-center justify-center rounded-lg border border-[#e3def8] text-xs text-violet-600">
              {item.charAt(0)}
            </span>
            <span>{item}</span>
          </button>
        ))}
      </div>
    </section>
  );
}

function MusicHomeDashboard({ library }: { library: MusicLibraryResponse }) {
  const [query, setQuery] = useState('');
  const songs = library.songs;
  const featuredSong = songs[0] ?? null;
  const filteredSongs = useMemo(() => {
    const normalizedQuery = query.trim().toLowerCase();
    if (!normalizedQuery) return songs;

    return songs.filter((song) => {
      const haystack = [song.title, song.artist, ...song.playlists.map((playlist) => playlist.playlistName)]
        .filter(Boolean)
        .join(' ')
        .toLowerCase();
      return haystack.includes(normalizedQuery);
    });
  }, [query, songs]);

  return (
    <MusicWorkspaceShell library={library} activeSong={featuredSong ?? undefined}>
      <div className="grid gap-6 xl:grid-cols-[1fr_278px] 2xl:grid-cols-[1fr_320px]">
        <div className="min-w-0 space-y-7">
          <HeroPanel library={library} featuredSong={featuredSong} />
          <PlaylistStrip playlists={library.playlists} />
          <SongTable songs={filteredSongs} query={query} onQueryChange={setQuery} />
        </div>

        <aside className="space-y-5">
          <CompactSongPanel title="Added Recently" songs={songs.slice(0, 5)} />
          <CompactSongPanel title="Old Bangers" songs={[...songs].reverse().slice(0, 5)} ranked />
          <MoodPanel />
          <PlatformsPanel />
        </aside>
      </div>
    </MusicWorkspaceShell>
  );
}

function HeroPanel({
  library,
  featuredSong,
}: {
  library: MusicLibraryResponse;
  featuredSong: MusicLibrarySong | null;
}) {
  const hero = getHeroContent(library, featuredSong);

  return (
    <section className="relative overflow-hidden rounded-3xl bg-[#7c6ae5] p-5 text-white shadow-[0_28px_90px_rgba(89,75,180,0.22)]">
      <div className="absolute inset-0">
        <img src={hero.artwork} alt="" className="h-full w-full object-cover opacity-45" />
        <div className="absolute inset-0 bg-linear-to-r from-[#271d66]/95 via-[#5f54c8]/76 to-[#f5a7ba]/22" />
      </div>
      <div className="relative grid gap-6 md:grid-cols-[190px_1fr]">
        <img src={hero.artwork} alt="" className="aspect-square w-full max-w-[190px] rounded-3xl border border-white/45 object-cover shadow-[0_24px_70px_rgba(15,23,42,0.28)]" />
        <div className="flex min-h-[190px] flex-col justify-center">
          <p className="text-xs font-black tracking-[0.22em] text-white/78 uppercase">From your library</p>
          <h1 className="mt-3 max-w-2xl text-4xl leading-tight font-black sm:text-5xl">{hero.title}</h1>
          <p className="mt-3 max-w-xl text-base leading-7 font-medium text-white/88">{hero.subtitle}</p>
          <div className="mt-6 flex flex-wrap gap-3">
            <button type="button" className="rounded-2xl bg-slate-950 px-5 py-3 text-sm font-black text-white shadow-[0_14px_30px_rgba(15,23,42,0.22)]">
              Play
            </button>
            <Link to="/music/playlists" className="rounded-2xl bg-white/18 px-5 py-3 text-sm font-black text-white backdrop-blur transition hover:bg-white/25">
              View playlist
            </Link>
          </div>
        </div>
      </div>
    </section>
  );
}

function getHeroContent(library: MusicLibraryResponse, featuredSong: MusicLibrarySong | null) {
  const firstPlaylist = library.playlists[0] ?? null;

  return {
    artwork: heroArtwork(featuredSong),
    title: heroTitle(firstPlaylist, featuredSong),
    subtitle: heroSubtitle(library, firstPlaylist, featuredSong),
  };
}

function heroArtwork(featuredSong: MusicLibrarySong | null): string {
  return featuredSong ? songArtwork(featuredSong) : fallbackArtwork[0];
}

function heroTitle(firstPlaylist: MusicLibraryPlaylist | null, featuredSong: MusicLibrarySong | null): string {
  return firstPlaylist?.name || featuredSong?.title || 'Your music library';
}

function heroSubtitle(
  library: MusicLibraryResponse,
  firstPlaylist: MusicLibraryPlaylist | null,
  featuredSong: MusicLibrarySong | null,
): string {
  if (firstPlaylist) return heroPlaylistSubtitle(library, firstPlaylist);
  if (featuredSong) return heroSongSubtitle(library, featuredSong);
  return 'Add songs and playlists to start building your personal listening home.';
}

function heroPlaylistSubtitle(library: MusicLibraryResponse, playlist: MusicLibraryPlaylist): string {
  const otherSongs = Math.max(library.summary.songCount - playlist.entryCount, 0).toLocaleString();
  return `${playlist.entryCount.toLocaleString()} songs ready here, with ${otherSongs} more across your library.`;
}

function heroSongSubtitle(library: MusicLibraryResponse, featuredSong: MusicLibrarySong): string {
  const otherSongs = Math.max(library.summary.songCount - 1, 0).toLocaleString();
  return `${songArtist(featuredSong)} is ready alongside ${otherSongs} more songs.`;
}

function PlaylistStrip({ playlists }: { playlists: MusicLibraryPlaylist[] }) {
  const visiblePlaylists = playlists.slice(0, 8);

  return (
    <section>
      <div className="mb-4 flex items-center justify-between gap-3">
        <h2 className="text-lg font-black text-slate-950">Library Playlists</h2>
        <Link to="/music/playlists" className="text-sm font-black text-violet-600">View all</Link>
      </div>

      {visiblePlaylists.length > 0 ? (
        <div className="grid grid-cols-[repeat(auto-fit,minmax(150px,1fr))] gap-4">
          {visiblePlaylists.map((playlist, index) => (
            <article key={playlist.id} className={`group relative min-h-[160px] overflow-hidden rounded-3xl bg-linear-to-br ${playlistGradients[index % playlistGradients.length]} p-4 text-white shadow-[0_18px_44px_rgba(88,74,150,0.12)]`}>
              <img src={playlistArtwork(playlist, index)} alt="" className="absolute inset-0 h-full w-full object-cover opacity-45 transition group-hover:scale-105 group-hover:opacity-60" />
              <div className="absolute inset-0 bg-linear-to-t from-slate-950/82 via-slate-950/22 to-transparent" />
              <div className="relative flex h-full flex-col justify-end">
                <h3 className="line-clamp-2 text-sm font-black">{playlist.name}</h3>
                <p className="mt-1 text-xs font-semibold text-white/75">{playlist.entryCount.toLocaleString()} songs</p>
                <button type="button" className="absolute right-0 bottom-0 flex h-9 w-9 items-center justify-center rounded-full bg-white text-slate-950 shadow-lg">
                  ▶
                </button>
              </div>
            </article>
          ))}
        </div>
      ) : (
        <EmptyPanel title="No playlists yet" detail="Your playlists will settle in here once Cantaro has music to work with." />
      )}
    </section>
  );
}

function SongTable({
  songs,
  query,
  onQueryChange,
}: {
  songs: MusicLibrarySong[];
  query: string;
  onQueryChange: (value: string) => void;
}) {
  return (
    <section className="rounded-3xl bg-white/64 p-4 shadow-[0_24px_80px_rgba(88,74,150,0.08)] backdrop-blur-xl">
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-lg font-black text-slate-950">Today's Mixtape</h2>
        <div className="flex flex-wrap items-center gap-2">
          <input
            className="h-10 rounded-2xl border border-[#e3def8] bg-white/80 px-4 text-sm font-medium outline-none placeholder:text-slate-400 focus:border-violet-300"
            placeholder="Filter songs"
            type="search"
            value={query}
            onChange={(event) => onQueryChange(event.target.value)}
          />
          {['All', 'Songs', 'Albums'].map((filter) => (
            <button
              key={filter}
              type="button"
              className={`rounded-2xl px-4 py-2 text-xs font-black ${filter === 'All' ? 'bg-slate-950 text-white' : 'border border-[#e3def8] bg-white/70 text-slate-700'}`}
            >
              {filter}
            </button>
          ))}
        </div>
      </div>

      {songs.length > 0 ? (
        <div className="overflow-x-auto">
          <table className="w-full min-w-[720px] border-collapse">
            <thead>
              <tr className="text-left text-xs font-black tracking-[0.14em] text-slate-500 uppercase">
                <th className="w-12 px-3 py-3">#</th>
                <th className="px-3 py-3">Title</th>
                <th className="px-3 py-3">Artist</th>
                <th className="px-3 py-3">Available</th>
                <th className="px-3 py-3 text-right">Time</th>
                <th className="w-12 px-3 py-3" />
              </tr>
            </thead>
            <tbody>
              {songs.slice(0, 10).map((song, index) => (
                <tr key={song.id} className="group border-t border-[#ece8fb] text-sm text-slate-700 transition hover:bg-white/70">
                  <td className="px-3 py-3 font-mono text-slate-500">{index + 1}</td>
                  <td className="px-3 py-3">
                    <div className="flex items-center gap-3">
                      <img src={songArtwork(song, index)} alt="" className="h-10 w-10 rounded-xl object-cover" />
                      <div className="min-w-0">
                        <p className="truncate font-black text-slate-950">{song.title}</p>
                      </div>
                    </div>
                  </td>
                  <td className="px-3 py-3">{songArtist(song)}</td>
                  <td className="px-3 py-3">
                    <div className="flex flex-wrap gap-1.5">
                      {visiblePlatformNames(song).slice(0, 3).map((source) => (
                        <span key={source} className="rounded-full bg-[#eeeaff] px-2.5 py-1 text-xs font-black text-violet-700">
                          {source}
                        </span>
                      ))}
                    </div>
                  </td>
                  <td className="px-3 py-3 text-right font-mono">{formatDuration(song.durationSeconds)}</td>
                  <td className="px-3 py-3 text-right font-black text-slate-500">...</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <EmptyPanel title="No songs found" detail="Try a different search." />
      )}
    </section>
  );
}

function CompactSongPanel({
  title,
  songs,
  ranked = false,
}: {
  title: string;
  songs: MusicLibrarySong[];
  ranked?: boolean;
}) {
  return (
    <section className="rounded-3xl bg-white/64 p-4 shadow-[0_24px_80px_rgba(88,74,150,0.08)] backdrop-blur-xl">
      <div className="mb-3 flex items-center justify-between gap-3">
        <h2 className="text-base font-black text-slate-950">{title}</h2>
        <button type="button" className="text-xs font-black text-violet-600">View all</button>
      </div>
      <div className="space-y-3">
        {songs.length > 0 ? songs.map((song, index) => (
          <div key={song.id} className="grid grid-cols-[auto_44px_1fr_auto] items-center gap-3">
            {ranked ? <span className="w-4 text-sm font-black text-slate-500">{index + 1}</span> : null}
            <img src={songArtwork(song, index)} alt="" className="h-11 w-11 rounded-xl object-cover" />
            <div className="min-w-0">
              <p className="truncate text-sm font-black text-slate-950">{song.title}</p>
              <p className="truncate text-xs font-semibold text-slate-500">{songArtist(song)}</p>
            </div>
            <span className="font-mono text-xs text-slate-500">{formatDuration(song.durationSeconds)}</span>
          </div>
        )) : (
          <p className="text-sm font-medium text-slate-500">Nothing here yet</p>
        )}
      </div>
    </section>
  );
}

function MoodPanel() {
  return (
    <section className="rounded-3xl bg-white/64 p-4 shadow-[0_24px_80px_rgba(88,74,150,0.08)] backdrop-blur-xl">
      <div className="mb-3 flex items-center justify-between gap-3">
        <h2 className="text-base font-black text-slate-950">Browse Moods</h2>
        <button type="button" className="text-xs font-black text-violet-600">View all</button>
      </div>
      <div className="grid grid-cols-2 gap-3">
        {moodTiles.map((mood) => (
          <button
            key={mood.label}
            type="button"
            className={`min-h-14 rounded-2xl bg-linear-to-br ${mood.className} px-3 text-left text-sm font-black text-white shadow-[0_14px_34px_rgba(88,74,150,0.12)]`}
          >
            {mood.label}
          </button>
        ))}
      </div>
    </section>
  );
}

function PlatformsPanel() {
  return (
    <section className="rounded-3xl bg-white/64 p-4 shadow-[0_24px_80px_rgba(88,74,150,0.08)] backdrop-blur-xl">
      <div className="mb-3 flex items-center justify-between gap-3">
        <h2 className="text-base font-black text-slate-950">Platforms</h2>
        <span className="text-xs font-black text-slate-400">Sources</span>
      </div>
      <div className="space-y-2">
        {platformCatalog.map((platform) => (
          <Link
            key={platform.id}
            to="/music/platforms/$platformId"
            params={{ platformId: platform.id }}
            disabled={!platform.implemented}
            className="group flex items-center gap-3 rounded-2xl bg-white/62 p-3 text-left transition hover:bg-white aria-disabled:pointer-events-none aria-disabled:opacity-55"
          >
            <div className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-2xl bg-linear-to-br ${platform.gradient} text-sm font-black text-white shadow-[0_12px_26px_rgba(88,74,150,0.14)]`}>
              {platform.icon}
            </div>
            <div className="min-w-0 flex-1">
              <p className="truncate text-sm font-black text-slate-950">{platform.name}</p>
              <p className="text-xs font-semibold text-slate-500">{platform.implemented ? 'Available' : 'Coming soon'}</p>
            </div>
            <span className="text-sm font-black text-violet-500 opacity-0 transition group-hover:opacity-100">›</span>
          </Link>
        ))}
      </div>
    </section>
  );
}

function BottomPlayer({ song }: { song: MusicLibrarySong }) {
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
          <button type="button" className="text-lg font-black text-slate-700">↺</button>
          <button type="button" className="text-lg font-black text-slate-700">◀</button>
          <button type="button" className="flex h-12 w-12 items-center justify-center rounded-full bg-slate-950 text-white shadow-[0_14px_30px_rgba(15,23,42,0.18)]">Ⅱ</button>
          <button type="button" className="text-lg font-black text-slate-700">▶</button>
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

function EmptyPanel({ title, detail }: { title: string; detail: string }) {
  return (
    <div className="rounded-3xl border border-dashed border-[#ded8f2] bg-white/54 p-6">
      <p className="font-black text-slate-950">{title}</p>
      <p className="mt-1 text-sm font-medium text-slate-500">{detail}</p>
    </div>
  );
}

function PlaylistsDirectory({ library }: { library: MusicLibraryResponse }) {
  return (
    <MusicWorkspaceShell library={library} activeSong={library.songs[0]}>
      <section className="space-y-5">
        <div className="flex flex-wrap items-end justify-between gap-3">
          <div>
            <p className="text-xs font-black tracking-[0.22em] text-violet-600 uppercase">Your library</p>
            <h1 className="mt-2 text-4xl font-black text-slate-950">Playlists</h1>
          </div>
          <span className="rounded-2xl bg-white/70 px-4 py-2 text-sm font-black text-slate-600">
            {library.summary.playlistCount.toLocaleString()} total
          </span>
        </div>

        {library.playlists.length > 0 ? (
          <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
            {library.playlists.map((playlist, index) => {
              const lastSyncedAt = playlist.services
                .map((service) => service.lastSyncedAt)
                .filter((value): value is string => Boolean(value))
                .sort()
                .at(-1);

              return (
                <article key={playlist.id} className="overflow-hidden rounded-3xl bg-white/70 shadow-[0_24px_80px_rgba(88,74,150,0.08)] backdrop-blur">
                  <img src={playlistArtwork(playlist, index)} alt="" className="h-40 w-full object-cover" />
                  <div className="p-5">
                    <div className="flex items-start justify-between gap-3">
                      <div className="min-w-0">
                        <h2 className="line-clamp-2 text-xl font-black text-slate-950">{playlist.name}</h2>
                        {playlist.description ? <p className="mt-2 line-clamp-2 text-sm font-medium text-slate-500">{playlist.description}</p> : null}
                      </div>
                      <span className="shrink-0 rounded-full bg-[#eeeaff] px-3 py-1 text-xs font-black text-violet-700">
                        {playlist.entryCount.toLocaleString()}
                      </span>
                    </div>
                    <div className="mt-4 flex flex-wrap gap-2">
                      {playlist.services.length > 0 ? playlist.services.map((service) => (
                        <span key={`${playlist.id}-${service.service}-${service.servicePlaylistId}`} className="rounded-full bg-slate-100 px-3 py-1 text-xs font-black text-slate-600">
                          {platformName(service.service)}
                        </span>
                      )) : (
                        <span className="rounded-full bg-slate-100 px-3 py-1 text-xs font-black text-slate-500">Cantaro</span>
                      )}
                    </div>
                    {lastSyncedAt ? <p className="mt-4 text-xs font-semibold text-slate-400">Updated {formatTimestamp(lastSyncedAt)}</p> : null}
                  </div>
                </article>
              );
            })}
          </div>
        ) : (
          <EmptyPanel title="No playlists yet" detail="Your playlists will appear here once Cantaro has music to work with." />
        )}
      </section>
    </MusicWorkspaceShell>
  );
}

export function MusicLayout() {
  return (
    <RequireAuth>
      <Outlet />
    </RequireAuth>
  );
}

export function MusicSongsPage() {
  return (
    <MusicLibraryPanel>
      {(library) => <MusicHomeDashboard library={library} />}
    </MusicLibraryPanel>
  );
}

export function MusicPlaylistsPage() {
  return (
    <MusicLibraryPanel>
      {(library) => <PlaylistsDirectory library={library} />}
    </MusicLibraryPanel>
  );
}

export function MusicMatchingPage() {
  return (
    <AppPageShell contentClassName="max-w-7xl">
      <div className="mb-5 flex items-center gap-2">
        <StatusBadge status="warning" />
        <h1 className="text-2xl font-black text-gray-900">Library attention</h1>
      </div>
      <MatchingReviewPage embedded />
    </AppPageShell>
  );
}

export function MusicPlatformPage({ platformId, playlistId = null }: { platformId: string; playlistId?: string | null }) {
  if (platformId === 'youtube') {
    return <YouTubePlaylistsPage embedded playlistId={playlistId} />;
  }

  const platform = platformCatalog.find((item) => item.id === platformId);

  return (
    <MusicWorkspaceShell>
      <GlassCard className="p-6">
        <p className="text-xs tracking-[0.24em] text-gray-500 uppercase">Platform</p>
        <h2 className="mt-2 text-2xl font-semibold text-gray-900">{platform?.name ?? platformId}</h2>
        <p className="mt-2 text-sm text-gray-600">This platform is not available yet.</p>
      </GlassCard>
    </MusicWorkspaceShell>
  );
}
